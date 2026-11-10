using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace SpireCompanion.Core;

public sealed record VoiceLaunchPlan(string Executable, string[] Arguments, string WorkingDirectory)
{
    public static VoiceLaunchPlan Discover(string modDirectory)
    {
        var runtime = Path.Combine(modDirectory, "voice");
        var executable = Path.Combine(runtime, OperatingSystem.IsWindows() ? "spire-voice.exe" : "spire-voice");
        if (File.Exists(executable))
        {
            var model = Path.Combine(runtime, "models", "vosk-model-small-cn-0.22");
            RequireModel(model);
            return OperatingSystem.IsMacOS() ? MacApp(Path.Combine(runtime, "SpireVoice.app"), Path.Combine(runtime, "spire-voice-launch"), [])
                : new(executable, ["--model", model], runtime);
        }
        var config = Path.Combine(modDirectory, "voice-launch.json");
        if (File.Exists(config))
        {
            var local = JsonSerializer.Deserialize<DevelopmentVoice>(File.ReadAllText(config));
            if (local is { Python.Length: > 0, Script.Length: > 0, Model.Length: > 0 } &&
                Path.IsPathFullyQualified(local.Python) && File.Exists(local.Python) &&
                Path.IsPathFullyQualified(local.Script) && File.Exists(local.Script) && Path.IsPathFullyQualified(local.Model))
            {
                RequireModel(local.Model);
                var bridge = Path.Combine(Path.GetDirectoryName(local.Script)!, "macos", "launch.py");
                if (OperatingSystem.IsMacOS() && !File.Exists(bridge))
                    throw new FileNotFoundException("macOS 语音启动组件缺失，请重新安装 Mod。");
                return OperatingSystem.IsMacOS() ? MacApp(local.MacApp, local.Python, [bridge])
                    : new(local.Python, [local.Script, "--model", local.Model], Path.GetDirectoryName(local.Script)!);
            }
        }
        throw new FileNotFoundException("语音组件未就绪，请重新安装含语音组件的版本；仍可使用文字聊天。");
    }

    private static void RequireModel(string model)
    {
        if (!File.Exists(Path.Combine(model, "am", "final.mdl")))
            throw new FileNotFoundException("中文识别模型缺失，请修复语音组件后重试。");
    }

    private static VoiceLaunchPlan MacApp(string? app, string launcher, string[] prefix)
    {
        if (string.IsNullOrEmpty(app) || !Path.IsPathFullyQualified(app) ||
            !File.Exists(Path.Combine(app, "Contents", "MacOS", "SpireVoice")) ||
            !File.Exists(Path.Combine(app, "Contents", "Resources", "runner.json")) || !File.Exists(launcher))
            throw new FileNotFoundException("macOS 语音组件需要独立的麦克风授权应用，请重新安装 Mod 后再开启语音。");
        // LaunchServices must establish a separate app identity. Direct exec inherits the game's denied consent.
        return new(launcher, [.. prefix, "--app", app], Path.GetDirectoryName(app)!);
    }

    private sealed record DevelopmentVoice(string Python, string Script, string Model, string? MacApp = null);
}

/// <summary>Owns only the child it launches. Closing chat leaves its model warm, with no microphone.</summary>
public sealed class VoiceServiceHost(Func<VoiceLaunchPlan> resolve) : IDisposable
{
    private readonly object sync = new();
    private readonly SemaphoreSlim starting = new(1, 1);
    private Process? process;
    private bool disposed;
    private int launches;
    private volatile string? startupError;

    public void BeginSession() { lock (sync) { launches = 0; startupError = null; } }

    public async Task EnsureRunningAsync(int port, string tokenFile, CancellationToken cancel)
    {
        await starting.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            lock (sync) ObjectDisposedException.ThrowIf(disposed, this);
            // A manually started developer service is reused, never killed or replaced.
            if (await ReachableAsync(port, cancel).ConfigureAwait(false)) return;
            Process owned;
            lock (sync)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                cancel.ThrowIfCancellationRequested();
                    if (startupError != null) throw new IOException(startupError);
                if (process is not { HasExited: false })
                {
                    process?.Dispose();
                    process = null;
                    if (launches >= 3)
                        throw new IOException("语音组件连续启动失败，请关闭语音后重新开启重试。");
                    var plan = resolve();
                    var info = new ProcessStartInfo(plan.Executable)
                    {
                        WorkingDirectory = plan.WorkingDirectory, UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
                    };
                    foreach (var argument in plan.Arguments) info.ArgumentList.Add(argument);
                    foreach (var argument in new[] { "--port", port.ToString(), "--token-file", tokenFile, "--managed" })
                        info.ArgumentList.Add(argument);
                    info.Environment["PYTHONUTF8"] = "1";
                    info.Environment["PYTHONUNBUFFERED"] = "1";
                    process = Process.Start(info) ?? throw new IOException("无法启动语音组件。");
                    launches++;
                    // Drain output without putting audio text or local credentials into game logs.
                    process.OutputDataReceived += (_, _) => { };
                    var launched = process;
                    process.ErrorDataReceived += (_, line) =>
                    {
                        if (!ReferenceEquals(process, launched)) return;
                        if (line.Data == "SPIRE_VOICE_ERROR:microphone_permission_denied")
                            startupError = "麦克风权限未允许：请在系统设置 → 隐私与安全性 → 麦克风中允许「尖塔语音 / Spire Voice」，然后重新开启语音。";
                        else if (line.Data == "SPIRE_VOICE_ERROR:invalid_installation")
                            startupError = "语音启动配置失效，请重新安装语音组件。";
                    };
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }
                owned = process;
            }
            // Loading the offline model must not block the game thread or capture audio early.
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(45))
            {
                cancel.ThrowIfCancellationRequested();
                lock (sync)
                {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    if (owned.HasExited)
                    {
                        owned.WaitForExit(); // Flush only the known error markers; never log arbitrary child output.
                        throw new IOException(startupError ?? "语音组件启动失败，请检查语音安装是否完整后重新开启。");
                    }
                }
                if (await ReachableAsync(port, cancel).ConfigureAwait(false)) return;
                await Task.Delay(100, cancel).ConfigureAwait(false);
            }
            throw new TimeoutException("语音组件准备时间较长，仍在后台加载；可稍候或关闭语音。");
        }
        finally { starting.Release(); }
    }

    private static async Task<bool> ReachableAsync(int port, CancellationToken cancel)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(300);
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (SocketException) { return false; }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { return false; }
    }

    public async Task<bool> RecycleOwnedAsync()
    {
        await starting.WaitAsync().ConfigureAwait(false);
        try
        {
            Process? owned;
            lock (sync)
            {
                if (disposed || process == null) return false;
                owned = process;
                process = null;
                startupError = null;
            }
            await StopOwnedAsync(owned).ConfigureAwait(false);
            return true;
        }
        finally { starting.Release(); }
    }

    public void Dispose()
    {
        Process? owned;
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            owned = process;
            process = null;
        }
        if (owned != null) _ = StopOwnedAsync(owned);
    }

    private static async Task StopOwnedAsync(Process owned)
    {
        try { owned.StandardInput.Close(); } catch (IOException) { }
        // EOF lets Python stop audio even if the game crashes. Only our own child is a fallback kill target.
        using (owned)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await owned.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    owned.Kill(entireProcessTree: true);
                    using var stopped = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await owned.WaitForExitAsync(stopped.Token).ConfigureAwait(false);
                }
                catch (InvalidOperationException) { }
            }
            catch (InvalidOperationException) { }
        }
    }
}
