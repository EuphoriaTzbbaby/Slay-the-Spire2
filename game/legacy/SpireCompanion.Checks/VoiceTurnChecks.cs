using System.Net;
using System.Text.Json;
using SpireCompanion.Core;

internal static class VoiceTurnChecks
{
    public static async Task<int> RunAsync()
    {
        var queue = new VoiceInputQueue();
        queue.TryEnqueue("old", "old", "丢弃旧会话");
        queue.TryEnqueue("current", "1", "等等，我改一下");
        queue.TryEnqueue("current", "2", "我想先防御");
        var turn = queue.TakeTurn("current", "这手牌怎么打")!;
        Require(turn.Inputs.Select(input => input.InputId).SequenceEqual(["1", "2"]));
        Require(turn.Question.Contains("这手牌怎么打") && turn.Question.EndsWith("我想先防御"));
        Require(!turn.NewText.Contains("这手牌怎么打") && !turn.Question.Contains("丢弃旧会话"));
        Require(queue.Count == 0);
        Console.WriteLine("PASS Spoken supplements preserve the interrupted question, coalesce once and keep old sessions out");

        var settings = new ChatSettings("https://model.example/v1", "test-model");
        var state = new GameSnapshot("menu", "无", 0, 0, 0, [], "无", false, "");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests = 0;
        using (var client = new ChatClient(new Handler(async (request, cancel) =>
        {
            if (++requests == 1)
            {
                entered.SetResult();
                await Task.Delay(Timeout.Infinite, cancel);
            }
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancel));
            var messages = body.RootElement.GetProperty("messages");
            Require(messages[messages.GetArrayLength() - 1].GetProperty("content").GetString() == turn.Question);
            return Reply("听到你的补充了。");
        })))
        using (var conversation = new ChatConversation(client))
        {
            var old = conversation.AskAsync(settings, "", "这手牌怎么打", state);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            conversation.InterruptUnshownReply();
            bool cancelled = false;
            try { await old; } catch (OperationCanceledException) { cancelled = true; }
            Require(cancelled && conversation.Last == null);
            await conversation.AskAsync(settings, "", turn.Question, state);
            Require(conversation.Last?.Question == turn.Question && requests == 2);
        }
        Console.WriteLine("PASS A spoken correction cancels the old request and submits the combined question with no stale history");

        using (var client = new ChatClient(new Handler((_, _) => Task.FromResult(Reply("旧答案")))))
        using (var conversation = new ChatConversation(client))
        {
            await conversation.AskAsync(settings, "", "尚未显示的问题", state);
            conversation.InterruptUnshownReply();
            Require(conversation.Last == null);
        }
        Console.WriteLine("PASS A completed answer still waiting for UI display is discarded when the player interrupts");

        int calls = 0;
        using (var client = new ChatClient(new Handler((_, _) => Task.FromResult(++calls == 1
            ? Reply("已显示的回答") : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))))
        using (var conversation = new ChatConversation(client))
        {
            await conversation.AskAsync(settings, "", "重复的问题", state);
            try { await conversation.AskAsync(settings, "", "重复的问题", state); } catch (ModelReplyException) { }
            conversation.InterruptUnshownReply();
            Require(conversation.Last?.Answer == "已显示的回答");
        }
        Console.WriteLine("PASS Cancelling a failed repeat question never erases an earlier completed turn");
        return 4;
    }

    private static HttpResponseMessage Reply(string text) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = text } } } })) };
    private static void Require(bool value) { if (!value) throw new Exception("Voice turn assertion failed"); }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => callback(request, token);
    }
}
