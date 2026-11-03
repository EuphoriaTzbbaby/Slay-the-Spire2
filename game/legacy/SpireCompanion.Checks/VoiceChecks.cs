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
            File.WriteAllText(path, "{\"VoiceEnabled\":true}");
            Require(WindowState.Load(path) is { VoiceEnabled: true, VoiceOutputEnabled: false });
            new WindowState(VoiceEnabled: true, VoiceOutputEnabled: true).Save(path);
            Require(WindowState.Load(path).VoiceOutputEnabled);
            Console.WriteLine("PASS Voice modes persist; old voice settings still mean voice-to-text");
            return 1;
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
        client.Connected += _ => ready.Release();
        client.TextReceived += (session, id, text) => { texts.Enqueue(text); inputs.Release(); _ = client.AckAsync(id, "accepted"); };
        client.IntentReceived += (session, id, intent) => { intents.Enqueue(intent); inputs.Release(); _ = client.AckAsync(id, "accepted"); };
        client.StatusChanged += value => { if (value == "测试播报失败") errors.Release(); };
        try
        {
            // Rapid restart must not let the old background loop stop the new connection.
            client.Start(port, token);
            client.Stop();
            client.Start(port, token);
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
                await Task.Delay(100);
                Require(texts.Count == session + 1 && intents.Count == session + 1);
                client.Stop();
                Require(!client.Running && !client.IsConnected && !await client.SpeakAsync("不能重放", "stopped"));
                if (session == 0) client.Start(port, token);
            }
            Require(texts.All(text => text == "这手牌怎么打？") && intents.All(intent => intent == "pause"));
            Console.WriteLine("PASS C# ↔ Python: handshake, UTF-8 fragments, input deduplication, session isolation, TTS, interrupt and restart");
        }
        finally { client.Stop(); }
    }

    private static void Require(bool condition)
    { if (!condition) throw new Exception("Voice integration assertion failed"); }
}
