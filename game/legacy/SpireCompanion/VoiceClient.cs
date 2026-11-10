using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace SpireCompanion;

/// <summary>Loopback protocol v1 client. Audio and callbacks never run on the game thread.</summary>
internal sealed class VoiceClient
{
    private sealed class Connection : IDisposable
    {
        public readonly ClientWebSocket Socket = new();
        public readonly SemaphoreSlim SendLock = new(1, 1);
        public readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> Pending = new();
        public readonly HashSet<string> Inputs = [];
        public string SessionId = "";
        public string? LastError;
        public bool Ready;
        public volatile string Listening = "disabled";
        public volatile bool Speaking;
        public string RecognizingId = "";
        public void Dispose()
        {
            Socket.Abort();
            foreach (var pending in Pending.Values) pending.TrySetCanceled();
            Socket.Dispose();
        }
    }

    private CancellationTokenSource? lifetime;
    private Connection? current;
    private volatile string? connectionError;
    public event Action<string>? StatusChanged;
    public event Action<string>? Connected;
    public event Action<string, string>? InputStarted;
    public event Action<string, string, string>? InputPartial;
    public event Action<string, string>? InputEnded;
    public event Action<string, string, string>? TextReceived;
    public event Action<string, string, string>? IntentReceived;
    public bool Running => lifetime is { IsCancellationRequested: false };
    public bool IsConnected => current is { Ready: true };
    public bool HasError => connectionError != null || current?.LastError != null;
    public bool IsListening => current is { Ready: true, Listening: "continuous" or "listening" or "recognizing" };
    public bool IsSpeaking => current is { Ready: true, Speaking: true };
    public string SessionId => current is { Ready: true } connection ? connection.SessionId : "";

    public void Start(int port, string token, Func<CancellationToken, Task>? ensureService = null)
    {
        Stop();
        var owner = new CancellationTokenSource();
        lifetime = owner;
        _ = Task.Run(() => LoopAsync(port, token, owner, ensureService));
    }

    public void Stop()
    {
        connectionError = null;
        var previous = Interlocked.Exchange(ref lifetime, null);
        try { previous?.Cancel(); } catch (ObjectDisposedException) { }
        var connection = Interlocked.Exchange(ref current, null);
        try { connection?.Socket.Abort(); } catch (ObjectDisposedException) { }
        StatusChanged?.Invoke("未开启");
    }

    public Task<bool> StartListeningAsync(bool continuous = false) => CommandAsync("listening.start",
        new { mode = continuous ? "continuous" : "wake" }, continuous ? "continuous" : null);
    public Task<bool> StopListeningAsync() => CommandAsync("listening.stop", new { });
    public Task<bool> InterruptAsync() => CommandAsync("speech.interrupt", new { });
    public Task<bool> StopSpeechAsync() => CommandAsync("speech.clear", new { include_current = true });
    public Task<bool> SpeakAsync(string text, string groupId) => CommandAsync("speech.enqueue", new
    {
        utterance_id = "utt-" + Guid.NewGuid().ToString("N"), text, priority = "normal", ttl_ms = 60000, group_id = groupId
    });
    public Task<bool> AckAsync(string inputId, string status) => CommandAsync("input.ack", new { input_id = inputId, status });

    private async Task<bool> CommandAsync(string method, object parameters, string? expectedListeningMode = null)
    {
        var connection = current;
        var owner = lifetime;
        if (connection is not { Ready: true } || owner == null)
        {
            StatusChanged?.Invoke("尚未连接，请先启动本地语音服务。");
            return false;
        }
        try
        {
            if (method != "input.ack") connection.LastError = null;
            var result = await RequestAsync(connection, method, parameters, owner.Token).ConfigureAwait(false);
            if (expectedListeningMode != null &&
                (!result.TryGetProperty("listening_mode", out var mode) || mode.GetString() != expectedListeningMode))
                throw new IOException("语音组件版本过旧，不支持持续监听；请更新语音组件后重新开启。");
            return ReferenceEquals(current, connection);
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception error)
        {
            if (ReferenceEquals(current, connection))
            {
                connection.LastError = "语音请求失败：" + error.Message;
                StatusChanged?.Invoke(connection.LastError);
            }
            return false;
        }
    }

    private async Task LoopAsync(int port, string token, CancellationTokenSource owner, Func<CancellationToken, Task>? ensureService)
    {
        var delay = 1;
        try
        {
            while (!owner.IsCancellationRequested)
            {
                using var connection = new Connection();
                using var connected = CancellationTokenSource.CreateLinkedTokenSource(owner.Token);
                Task? receiver = null;
                Task? heartbeat = null;
                try
                {
                    if (!ReferenceEquals(lifetime, owner)) return;
                    current = connection;
                    if (ensureService != null)
                    {
                        StatusChanged?.Invoke("正在准备语音组件，首次开启需要加载模型；QQ 可关闭。");
                        await ensureService(connected.Token).ConfigureAwait(false);
                    }
                    StatusChanged?.Invoke("正在连接本地服务…");
                    await connection.Socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), connected.Token).ConfigureAwait(false);
                    receiver = ReceiveAsync(connection, connected.Token);
                    var hello = await RequestAsync(connection, "session.hello", new { token, protocol_version = 1 }, connected.Token).ConfigureAwait(false);
                    connection.SessionId = hello.GetProperty("session_id").GetString() ?? throw new JsonException("缺少语音会话 ID。");
                    if (string.IsNullOrWhiteSpace(connection.SessionId)) throw new JsonException("缺少语音会话 ID。");
                    await RequestAsync(connection, "service.status", new { }, connected.Token).ConfigureAwait(false);
                    if (!ReferenceEquals(lifetime, owner)) return;
                    connection.Ready = true;
                    connectionError = null;
                    delay = 1;
                    StatusChanged?.Invoke("语音服务已连接。");
                    Connected?.Invoke(connection.SessionId);
                    heartbeat = HeartbeatAsync(connection, connected.Token);
                    await await Task.WhenAny(receiver, heartbeat).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (owner.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    if (ReferenceEquals(lifetime, owner))
                    {
                        connectionError = error.Message;
                        StatusChanged?.Invoke("语音未就绪：" + error.Message);
                    }
                }
                finally
                {
                    connection.Ready = false;
                    connected.Cancel();
                    connection.Socket.Abort();
                    foreach (var pending in connection.Pending.Values) pending.TrySetCanceled();
                    Interlocked.CompareExchange(ref current, null, connection);
                    try { if (receiver != null) await receiver.ConfigureAwait(false); } catch { }
                    try { if (heartbeat != null) await heartbeat.ConfigureAwait(false); } catch { }
                }
                try { await Task.Delay(TimeSpan.FromSeconds(delay), owner.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                delay = Math.Min(10, delay * 2);
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref lifetime, null, owner);
            owner.Dispose();
        }
    }

    private async Task HeartbeatAsync(Connection connection, CancellationToken cancel)
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancel).ConfigureAwait(false);
            await RequestAsync(connection, "service.ping", new { }, cancel).ConfigureAwait(false);
        }
    }

    private async Task<JsonElement> RequestAsync(Connection connection, string method, object parameters, CancellationToken cancel)
    {
        var id = "req-" + Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Pending[id] = completion;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { version = 1, kind = "request", id, method, @params = parameters }));
            await connection.SendLock.WaitAsync(timeout.Token).ConfigureAwait(false);
            try { await connection.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false); }
            finally { connection.SendLock.Release(); }
            return await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        { throw new TimeoutException("语音服务未及时响应。"); }
        finally { connection.Pending.TryRemove(id, out _); }
    }

    private async Task ReceiveAsync(Connection connection, CancellationToken cancel)
    {
        var buffer = new byte[8192];
        using var message = new MemoryStream();
        while (!cancel.IsCancellationRequested)
        {
            var result = await connection.Socket.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) throw new IOException("语音服务已断开。");
            if (result.MessageType != WebSocketMessageType.Text || message.Length + result.Count > 65536)
                throw new IOException("语音消息格式或长度不受支持。");
            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;
            var raw = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);
            if (ReferenceEquals(current, connection)) Dispatch(connection, raw);
        }
    }

    private void Dispatch(Connection connection, string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.GetProperty("version").GetInt32() != 1) return;
            var kind = root.GetProperty("kind").GetString();
            if (kind == "response")
            {
                var id = root.GetProperty("id").GetString() ?? "";
                if (!connection.Pending.TryGetValue(id, out var pending)) return;
                if (root.GetProperty("ok").GetBoolean())
                {
                    var response = root.GetProperty("result");
                    if (response.TryGetProperty("listening", out var listeningState))
                        connection.Listening = listeningState.GetString() ?? "disabled";
                    if (response.TryGetProperty("playback", out var playbackState))
                        connection.Speaking = playbackState.GetString() == "playing";
                    pending.TrySetResult(response.Clone());
                }
                else pending.TrySetException(new IOException(root.GetProperty("error").GetProperty("message").GetString() ?? "语音请求被拒绝。"));
                return;
            }
            if (kind != "event" || !connection.Ready || root.GetProperty("session_id").GetString() != connection.SessionId) return;
            var data = root.GetProperty("data");
            switch (root.GetProperty("name").GetString())
            {
                case "input.final":
                    var inputId = data.GetProperty("input_id").GetString() ?? "";
                    var text = data.GetProperty("text").GetString() ?? "";
                    var intent = data.TryGetProperty("intent", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                    if (inputId.Length is 0 or > 128 || text.Length > 4000 || string.IsNullOrWhiteSpace(text) && intent == null) return;
                    if (!connection.Inputs.Add(inputId)) { _ = AckAsync(inputId, "ignored"); return; }
                    if (connection.Inputs.Count > 4096) throw new IOException("语音输入过多，请重新连接。");
                    EndInput(connection, inputId);
                    if (!string.IsNullOrWhiteSpace(intent)) IntentReceived?.Invoke(connection.SessionId, inputId, intent);
                    else TextReceived?.Invoke(connection.SessionId, inputId, text);
                    break;
                case "recognition.started":
                    connection.LastError = null;
                    var startedId = data.GetProperty("input_id").GetString() ?? "";
                    if (startedId.Length is > 0 and <= 128 && startedId != connection.RecognizingId && !connection.Inputs.Contains(startedId))
                    {
                        connection.RecognizingId = startedId;
                        InputStarted?.Invoke(connection.SessionId, startedId);
                    }
                    break;
                case "recognition.partial":
                    var draft = data.GetProperty("text").GetString() ?? "";
                    var draftId = data.GetProperty("input_id").GetString() ?? "";
                    if (draft.Length is > 0 and <= 4000 && draftId.Length > 0 && draftId == connection.RecognizingId)
                        InputPartial?.Invoke(connection.SessionId, draftId, draft);
                    break;
                case "recognition.cancelled":
                    EndInput(connection, data.GetProperty("input_id").GetString() ?? "");
                    break;
                case "speech.started": connection.LastError = null; connection.Speaking = true; break;
                case "recognition.timeout":
                    EndInput(connection, data.GetProperty("input_id").GetString() ?? "");
                    StatusChanged?.Invoke("这句话没听清，持续模式会继续接收下一句。"); break;
                case "state.changed":
                    var playback = data.TryGetProperty("playback", out var p) ? p.GetString() : "idle";
                    var listening = data.TryGetProperty("listening", out var l) ? l.GetString() : "disabled";
                    connection.Speaking = playback == "playing";
                    connection.Listening = listening ?? "disabled";
                    StatusChanged?.Invoke(connection.LastError ?? (playback == "playing" ? "正在播报。" : listening switch
                    {
                        "continuous" => "持续监听中，说完一句后可继续说；QQ 关闭。",
                        "listening" => "正在听，请说话…", "recognizing" => "正在识别…",
                        "waiting_wake" => "等待唤醒「搭子」。", _ => "麦克风已关闭，QQ 开启语音聊天。"
                    }));
                    break;
                case "service.error":
                    connection.LastError = data.GetProperty("message").GetString() ?? "语音服务出错。";
                    if (data.TryGetProperty("input_id", out var failedInput) && failedInput.ValueKind == JsonValueKind.String)
                        EndInput(connection, failedInput.GetString() ?? "");
                    StatusChanged?.Invoke(connection.LastError);
                    break;
            }
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { }
    }

    private void EndInput(Connection connection, string id)
    {
        if (id.Length == 0 || id != connection.RecognizingId) return;
        connection.RecognizingId = "";
        InputEnded?.Invoke(connection.SessionId, id);
    }
}
