using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace SpireCompanion;

/// <summary>
/// Minimal local WebSocket client for the voice module. Runs on a background task and
/// raises plain callbacks; the panel marshals them to the Godot main thread.
/// </summary>
internal sealed class VoiceClient
{
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private CancellationTokenSource? cts;
    private ClientWebSocket? socket;

    public event Action<string>? StatusChanged;
    public event Action<string, string>? TextReceived;
    public event Action<string, string>? IntentReceived;

    public bool Running { get; private set; }

    public void Start(int port, string token)
    {
        Stop();
        cts = new CancellationTokenSource();
        Running = true;
        _ = Task.Run(() => LoopAsync(port, token, cts.Token));
    }

    public void Stop()
    {
        try { cts?.Cancel(); } catch { }
        cts = null;
        Running = false;
        socket = null;
        StatusChanged?.Invoke("已断开");
    }

    public async Task AckAsync(string inputId, string status)
    {
        if (socket is not { State: WebSocketState.Open }) return;
        var payload = new
        {
            version = 1, kind = "request", id = "ack-" + Guid.NewGuid().ToString("N"),
            method = "input.ack", @params = new { input_id = inputId, status }
        };
        await SendAsync(payload).ConfigureAwait(false);
    }

    private async Task LoopAsync(int port, string token, CancellationToken cancel)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                socket = ws;
                StatusChanged?.Invoke("正在连接语音服务…");
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), cancel).ConfigureAwait(false);
                await SendHelloAsync(ws, token, cancel).ConfigureAwait(false);
                StatusChanged?.Invoke("语音服务已连接");
                delay = TimeSpan.FromSeconds(2);
                await ReceiveLoopAsync(ws, cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception error)
            {
                StatusChanged?.Invoke($"语音连接失败：{error.Message}");
            }
            finally { socket = null; }
            try { await Task.Delay(delay, cancel).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            delay = TimeSpan.FromSeconds(Math.Min(10, delay.TotalSeconds * 2));
        }
        Running = false;
    }

    private async Task SendHelloAsync(WebSocket ws, string token, CancellationToken cancel)
    {
        var payload = new
        {
            version = 1, kind = "request", id = "hello-" + Guid.NewGuid().ToString("N"),
            method = "session.hello", @params = new { token, protocol_version = 1 }
        };
        await SendAsync(payload, ws, cancel).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(WebSocket ws, CancellationToken cancel)
    {
        var buffer = new byte[16 * 1024];
        var text = new StringBuilder();
        while (!cancel.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                StatusChanged?.Invoke("语音服务已断开");
                return;
            }
            text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage) continue;
            var message = text.ToString();
            text.Clear();
            Dispatch(message);
        }
    }

    private void Dispatch(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("kind", out var kind) || kind.GetString() != "event") return;
            if (!root.TryGetProperty("name", out var name)) return;
            var data = root.TryGetProperty("data", out var d) ? d : default;
            switch (name.GetString())
            {
                case "input.final":
                {
                    var id = data.TryGetProperty("input_id", out var i) ? i.GetString() ?? "" : "";
                    var text = data.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                    var intent = data.TryGetProperty("intent", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                    if (id.Length == 0 || text.Length == 0 && intent == null) return;
                    if (!string.IsNullOrWhiteSpace(intent)) IntentReceived?.Invoke(id, intent!);
                    else TextReceived?.Invoke(id, text);
                    break;
                }
            }
        }
        catch (JsonException) { }
    }

    private Task SendAsync(object payload) => SendAsync(payload, socket!, CancellationToken.None);

    private async Task SendAsync(object payload, WebSocket ws, CancellationToken cancel)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await sendLock.WaitAsync(cancel).ConfigureAwait(false);
        try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, cancel).ConfigureAwait(false); }
        finally { sendLock.Release(); }
    }
}
