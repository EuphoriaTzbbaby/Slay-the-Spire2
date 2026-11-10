using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using SpireCompanion.Core;

internal static class VoiceServiceHostChecks
{
    public static async Task FixtureAsync(string[] args)
    {
        if (args[0] == "--voice-host-denied-fixture")
        {
            Console.Error.WriteLine("SPIRE_VOICE_ERROR:microphone_permission_denied");
            return;
        }
        var port = int.Parse(args[Array.IndexOf(args, "--port") + 1]);
        var path = args[Array.IndexOf(args, "--token-file") + 1];
        File.WriteAllText(path + ".pid", Environment.ProcessId.ToString());
        using var listener = new TcpListener(IPAddress.Loopback, port);
        if (args[0] != "--voice-host-wait-fixture") listener.Start();
        await Console.In.ReadToEndAsync(); // Closing the parent's private pipe must terminate this process.
        File.WriteAllText(path + ".closed", "EOF");
    }

    public static async Task<int> RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "语音 启动检查-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var model = Path.Combine(directory, "model");
            Directory.CreateDirectory(Path.Combine(model, "am"));
            File.WriteAllText(Path.Combine(model, "am", "final.mdl"), "fixture");
            var python = Path.Combine(directory, "python fixture");
            var script = Path.Combine(directory, "voice fixture.py");
            File.WriteAllText(python, "fixture"); File.WriteAllText(script, "fixture");
            string Helper(string parent)
            {
                var app = Path.Combine(parent, "SpireVoice.app");
                Directory.CreateDirectory(Path.Combine(app, "Contents", "MacOS"));
                Directory.CreateDirectory(Path.Combine(app, "Contents", "Resources"));
                File.WriteAllText(Path.Combine(app, "Contents", "MacOS", "SpireVoice"), "fixture");
                File.WriteAllText(Path.Combine(app, "Contents", "Resources", "runner.json"), "{}");
                return app;
            }
            var helper = Helper(directory);
            Directory.CreateDirectory(Path.Combine(directory, "macos"));
            var bridge = Path.Combine(directory, "macos", "launch.py");
            File.WriteAllText(bridge, "fixture");
            File.WriteAllText(Path.Combine(directory, "voice-launch.json"), JsonSerializer.Serialize(new { Python = python, Script = script, Model = model, MacApp = helper }));
            var local = VoiceLaunchPlan.Discover(directory);
            Require(OperatingSystem.IsMacOS()
                ? local.Executable == python && local.Arguments.SequenceEqual(new[] { bridge, "--app", helper })
                : local.Executable == python && local.Arguments.SequenceEqual(new[] { script, "--model", model }));
            var runtime = Path.Combine(directory, "voice");
            var packagedModel = Path.Combine(runtime, "models", "vosk-model-small-cn-0.22", "am");
            Directory.CreateDirectory(packagedModel);
            var executable = Path.Combine(runtime, OperatingSystem.IsWindows() ? "spire-voice.exe" : "spire-voice");
            File.WriteAllText(executable, "fixture");
            File.WriteAllText(Path.Combine(packagedModel, "final.mdl"), "fixture");
            Helper(runtime);
            File.WriteAllText(Path.Combine(runtime, "spire-voice-launch"), "fixture");
            Require(VoiceLaunchPlan.Discover(directory).Executable == (OperatingSystem.IsMacOS() ? Path.Combine(runtime, "spire-voice-launch") : executable));
            File.Delete(Path.Combine(packagedModel, "final.mdl"));
            bool missing = false;
            try { VoiceLaunchPlan.Discover(directory); } catch (FileNotFoundException) { missing = true; }
            Require(missing);
            Console.WriteLine("PASS Voice discovers bundled runtime or local development paths and rejects a missing model");

            string tokenFile = Path.Combine(directory, "会话 with spaces.json");
            VoiceLaunchPlan Fixture(bool wait = false, bool denied = false)
            {
                var program = Environment.ProcessPath!;
                string[] arguments = Path.GetFileNameWithoutExtension(program).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                    ? [typeof(VoiceServiceHostChecks).Assembly.Location] : [];
                return new(program, [.. arguments, denied ? "--voice-host-denied-fixture" : wait ? "--voice-host-wait-fixture" : "--voice-host-fixture"], directory);
            }
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var port = FreePort();
            int resolved = 0;
            using var host = new VoiceServiceHost(() => { resolved++; return Fixture(); });
            await Task.WhenAll(host.EnsureRunningAsync(port, tokenFile, limit.Token), host.EnsureRunningAsync(port, tokenFile, limit.Token));
            Require(resolved == 1);
            using (var child = Process.GetProcessById(int.Parse(File.ReadAllText(tokenFile + ".pid"))))
            {
                await host.EnsureRunningAsync(port, tokenFile, limit.Token);
                Require(resolved == 1 && !child.HasExited);
                child.Kill(); await child.WaitForExitAsync(limit.Token);
            }
            await host.EnsureRunningAsync(port, tokenFile, limit.Token);
            using (var restarted = Process.GetProcessById(int.Parse(File.ReadAllText(tokenFile + ".pid"))))
            {
                Require(resolved == 2);
                host.Dispose();
                await restarted.WaitForExitAsync(limit.Token);
                Require(File.ReadAllText(tokenFile + ".closed") == "EOF");
            }
            Console.WriteLine("PASS Concurrent QQ startup launches once; service stays warm, recovers after a crash and exits on parent EOF");

            using (var recover = new VoiceServiceHost(() => Fixture()))
            {
                var recoveryPort = FreePort();
                var recoveryFile = tokenFile + ".recovery";
                await recover.EnsureRunningAsync(recoveryPort, recoveryFile, limit.Token);
                using var original = Process.GetProcessById(int.Parse(File.ReadAllText(recoveryFile + ".pid")));
                var recycling = recover.RecycleOwnedAsync();
                await recover.EnsureRunningAsync(recoveryPort, recoveryFile, limit.Token);
                Require(await recycling && original.HasExited);
                Require(File.ReadAllText(recoveryFile + ".closed") == "EOF");
                using var replacement = Process.GetProcessById(int.Parse(File.ReadAllText(recoveryFile + ".pid")));
                Require(replacement.Id != original.Id && !replacement.HasExited);
                recover.Dispose();
                await replacement.WaitForExitAsync(limit.Token);
            }
            Console.WriteLine("PASS Audio-failure recycling closes only the owned child and a concurrent retry starts a fresh process");

            using (var external = new TcpListener(IPAddress.Loopback, 0))
            {
                external.Start();
                var externalPort = ((IPEndPoint)external.LocalEndpoint).Port;
                using var reuse = new VoiceServiceHost(() => throw new Exception("Must not launch over an existing service"));
                await reuse.EnsureRunningAsync(externalPort, tokenFile, limit.Token);
                Require(!await reuse.RecycleOwnedAsync());
                reuse.Dispose();
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, externalPort, limit.Token);
            }
            Console.WriteLine("PASS External developer service is reused and is never stopped by the game process host");

            using (var waiting = new VoiceServiceHost(() => Fixture(wait: true)))
            using (var cancel = new CancellationTokenSource())
            {
                var waitFile = tokenFile + ".waiting";
                var preparing = waiting.EnsureRunningAsync(FreePort(), waitFile, cancel.Token);
                while (!File.Exists(waitFile + ".pid")) await Task.Delay(20, limit.Token);
                cancel.Cancel();
                bool cancelled = false;
                try { await preparing; }
                catch (OperationCanceledException) { cancelled = true; }
                Require(cancelled);
                using var child = Process.GetProcessById(int.Parse(File.ReadAllText(waitFile + ".pid")));
                waiting.Dispose();
                await child.WaitForExitAsync(limit.Token);
            }
            Console.WriteLine("PASS Closing during voice preparation cancels promptly and disposal releases the waiting child");
            int deniedLaunches = 0;
            using (var denied = new VoiceServiceHost(() => { deniedLaunches++; return Fixture(denied: true); }))
            {
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    bool explained = false;
                    try { await denied.EnsureRunningAsync(FreePort(), tokenFile, limit.Token); }
                    catch (IOException e) { explained = e.Message.Contains("麦克风权限未允许"); }
                    Require(explained);
                }
                Require(deniedLaunches == 1);
            }
            Console.WriteLine("PASS Denied microphone permission stays explicit and does not relaunch the consent app automatically");
            return 6;
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
    private static void Require(bool value) { if (!value) throw new Exception("Voice service lifecycle check failed"); }
}
