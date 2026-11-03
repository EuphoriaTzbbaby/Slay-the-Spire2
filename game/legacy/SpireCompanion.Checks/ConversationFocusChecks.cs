using System.Net;
using System.Text.Json;
using SpireCompanion.Core;

internal static class ConversationFocusChecks
{
    public static async Task<int> RunAsync()
    {
        var settings = new ChatSettings("https://model.example/v1", "test-model");
        var menu = new GameSnapshot("主菜单或无跑局", "无", 0, 0, 0, [], "无", false, "尚未进入游戏");
        var question = "我现在八岁了你猜我现在几岁了";
        int requests = 0, toolCalls = 0;
        using (var client = new ChatClient(new Handler(async (request, token) =>
        {
            requests++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var root = body.RootElement;
            var messages = root.GetProperty("messages");
            Require(root.GetProperty("tool_choice").GetString() == "auto");
            Require(messages[messages.GetArrayLength() - 1].GetProperty("content").GetString() == question);
            using var background = JsonDocument.Parse(messages[messages.GetArrayLength() - 2].GetProperty("content").GetString()!);
            Require(!background.RootElement.GetProperty("player_stats_available").GetBoolean());
            Require(background.RootElement.GetProperty("live_state").GetString() == "当前未开始跑局");
            return Reply("你刚才说八岁，我猜八岁。");
        })))
        {
            var answer = await client.AskAsync(settings, "", question, menu,
                [new("怎么开局", "当前在主菜单。")], default,
                toolExecutor: (_, _) => { toolCalls++; return "主菜单"; }, liveState: "当前未开始跑局");
            Require(answer == "你刚才说八岁，我猜八岁。" && requests == 1 && toolCalls == 0);
        }
        Console.WriteLine("PASS Latest transcribed question stays explicit; casual chat needs no game query or state-only retry");

        int echoRequests = 0;
        using (var client = new ChatClient(new Handler((_, _) =>
        {
            echoRequests++;
            return Task.FromResult(Reply("「我爱你」"));
        })))
        using (var conversation = new ChatConversation(client))
        {
            var answer = await conversation.AskAsync(settings, "", "快说一句我爱你", menu);
            Require(answer.Text == "「我爱你」" && echoRequests == 1);
            Require(conversation.Last?.Question == "快说一句我爱你");
            Require(AnswerGrounding.Check("我叫「奶龙」。", menu).Unknown.Length == 0);
        }
        Console.WriteLine("PASS Ordinary quoted speech and persona names are not rejected as missing cards");

        var combat = menu with { Hp = 60, MaxHp = 80, Cards = [new CardView(0, "STRIKE", "打击", "1", false)] };
        Require(AnswerGrounding.Check("选「余烬」更好。", combat).Unknown.SequenceEqual(["余烬"]));
        Require(AnswerGrounding.Check("「余烬」造成 10 点伤害。", combat).Unknown.SequenceEqual(["余烬"]));
        Require(AnswerGrounding.Check("打出「打击」。", combat).Unknown.Length == 0);
        Console.WriteLine("PASS Unsupported quoted game recommendations and effect claims still fail grounding");
        int retries = 0;
        using (var client = new ChatClient(new Handler(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var messages = body.RootElement.GetProperty("messages");
            Require(messages[messages.GetArrayLength() - 1].GetProperty("content").GetString() == "这手牌怎么打");
            return ++retries == 1 ? Reply("选「余烬」更好。") : Reply("选「打击」更好。");
        })))
        {
            Require(await client.AskAsync(settings, "", "这手牌怎么打", combat, [], default) == "选「打击」更好。");
            Require(retries == 2);
        }
        Console.WriteLine("PASS A grounding correction preserves the original player question as the final message");
        return 4;
    }

    private static HttpResponseMessage Reply(string text) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = text } } } })) };
    private static void Require(bool condition)
    { if (!condition) throw new Exception("Conversation focus assertion failed"); }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => callback(request, token);
    }
}
