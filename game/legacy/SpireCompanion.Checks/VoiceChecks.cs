using System.Collections.Concurrent;
using SpireCompanion;
using SpireCompanion.Core;

internal static class VoiceChecks
{
    public static int Preferences()
    {
        var path = Path.Combine(Path.GetTempPath(), "spire-voice-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "{\"AutoEnabled\":true,\"SolverAuto\":true,\"VoiceEnabled\":true,\"VoiceOutputEnabled\":true}");
            Require(WindowState.Load(path) is { AutoEnabled: true, SolverAuto: true, VoiceEnabled: false, VoiceOutputEnabled: false });
            Console.WriteLine("PASS Restart ignores legacy enabled voice/output flags and retains game preferences");
            new WindowState(AutoEnabled: true, VoiceEnabled: true, VoiceOutputEnabled: true).Save(path);
            using var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Require(!saved.RootElement.GetProperty("VoiceEnabled").GetBoolean() && !saved.RootElement.GetProperty("VoiceOutputEnabled").GetBoolean());
            Require(WindowState.Load(path) is { AutoEnabled: true, VoiceEnabled: false, VoiceOutputEnabled: false });
            Console.WriteLine("PASS New settings never persist an enabled microphone or voice output across launches");
            return 2;
        }
        finally { File.Delete(path); }
    }

    public static async Task WireAsync(int port, string token)
    {
        var client = new VoiceClient();
        var texts = new ConcurrentQueue<string>();
        var intents = new ConcurrentQueue<string>();
        using var ready = new SemaphoreSlim(0);
        using var inputs = new SemaphoreSlim(0);
        using var errors = new SemaphoreSlim(0);
        int preparations = 0;
        Task Prepare(CancellationToken cancel) { cancel.ThrowIfCancellationRequested(); Interlocked.Increment(ref preparations); return Task.CompletedTask; }
        client.Connected += _ => ready.Release();
        client.TextReceived += (session, id, text) => { texts.Enqueue(text); inputs.Release(); _ = client.AckAsync(id, "accepted"); };
        client.IntentReceived += (session, id, intent) => { intents.Enqueue(intent); inputs.Release(); _ = client.AckAsync(id, "accepted"); };
        client.StatusChanged += value => { if (value == "测试播报失败") errors.Release(); };
        try
        {
            // Rapid restart must not let the old background loop stop the new connection.
            client.Start(port, token, Prepare);
            client.Stop();
            client.Start(port, token, Prepare);
            for (int session = 0; session < 2; session++)
            {
                Require(await ready.WaitAsync(TimeSpan.FromSeconds(10)));
                Require(client.IsConnected && await client.StartListeningAsync());
                Require(await client.SpeakAsync("中文播报测试", "wire-test"));
                // A following idle/listening event must keep an actionable TTS error visible.
                Require(await errors.WaitAsync(TimeSpan.FromSeconds(5)));
                Require(await errors.WaitAsync(TimeSpan.FromSeconds(5)));
                Require(await client.InterruptAsync());
                Require(await inputs.WaitAsync(TimeSpan.FromSeconds(5)));
                Require(await inputs.WaitAsync(TimeSpan.FromSeconds(5)));
                Require(!await client.StartListeningAsync(continuous: true)); // old Python response is not success
                Require(await client.StartListeningAsync(continuous: true));
                Require(client.IsListening);
                Require(await client.StopListeningAsync());
                Require(!client.IsListening);
                await Task.Delay(100);
                Require(texts.Count == session + 1 && intents.Count == session + 1);
                client.Stop();
                Require(!client.Running && !client.IsConnected && !await client.SpeakAsync("不能重放", "stopped"));
                if (session == 0) client.Start(port, token, Prepare);
            }
            Require(preparations >= 2);
            Require(texts.All(text => text == "这手牌怎么打？") && intents.All(intent => intent == "pause"));
            Console.WriteLine("PASS C# ↔ Python: handshake, UTF-8 fragments, input deduplication, session isolation, TTS, interrupt and restart");
        }
        finally { client.Stop(); }
    }

    public static int PendingInputs()
    {
        var queue = new VoiceInputQueue();
        Require(queue.TryEnqueue("session", "1", "第一句"));
        Require(queue.TryEnqueue("session", "2", "第二句"));
        Require(queue.Take("session")?.Text == "第一句");
        Require(queue.Take("session")?.Text == "第二句");
        Require(queue.Take("session") == null);
        Console.WriteLine("PASS Continuous voice turns retain FIFO order while the model is busy");

        Require(queue.TryEnqueue("old", "old", "旧会话"));
        Require(queue.TryEnqueue("new", "new", "新会话"));
        Require(queue.Take("new")?.InputId == "new");
        Require(queue.Take("new") == null);
        for (int i = 0; i < 4; i++) Require(queue.TryEnqueue("new", i.ToString(), "排队"));
        Require(!queue.TryEnqueue("new", "overflow", "拒绝"));
        queue.Clear();
        Require(queue.Count == 0 && queue.Take("new") == null);
        Console.WriteLine("PASS Voice close clears queued turns; stale sessions and queue overflow never replay");
        return 2;
    }

    private static void Require(bool condition)
    { if (!condition) throw new Exception("Voice integration assertion failed"); }
}
