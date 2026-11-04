using System.Net;
using System.Text.Json;
using SpireCompanion.Core;

internal static class CommandChecks
{
    private static readonly ChatSettings Settings = new("https://model.example/v1", "test-model");
    private static GameSnapshot State => new("RestSiteRoom", "铁甲战士", 40, 80, 100, [], "无", false, "当前不在战斗中")
    { Room = new("Rest", "休息点", "选择一项", [new("rest", "休息", "恢复生命", false), new("smith", "锻造", "升级卡牌", false), new("locked", "不可用", "条件不足", true)]) { Id = "screen-1" } };

    public static async Task<int> RunAsync()
    {
        foreach (var text in new[] { "帮我选一张适合当前卡组的牌", "这里休息", "买这件商品", "请帮我根据当前卡组选一张", "可以帮我选一张吗", "选第二个", "跳过奖励", "确认选择", "你来决定", "帮我删除一张打击" })
            Require(RoomCommand.IsRequested(text), text);
        foreach (var text in new[] { "哪张牌更好", "选哪个", "买什么", "帮我看看应该选哪张", "为什么不买这个", "只给建议不要操作", "不要帮我选牌", "别选这张", "帮我别买这个", "选这个好吗", "复述这句话：帮我选牌", "以后帮我优先买攻击牌", "你好" })
            Require(!RoomCommand.IsRequested(text), text);
        Pass("Only explicit current instructions enable actions; questions, negations and examples stay read-only");

        var command = RoomCommand.Parse(State, "{\"id\":\"rest\"}");
        Require(command.ValidateCurrent(State, true) == null);
        Require(command.ValidateCurrent(State with { Gold = 90 }, true) != null);
        Require(command.ValidateCurrent(State with { Room = State.Room! with { Id = "screen-2" } }, true) != null);
        Require(command.ValidateCurrent(State, false) != null);
        Require(!RoomCommand.CanOffer(State with { CombatTurn = "1:Player", CanEndTurn = true }));
        Require(!RoomCommand.CanOffer(State with { Enemies = [new(0, "enemy", "敌人", 10, 10, 0)] }));
        Require(!RoomCommand.CanOffer(State with { Room = null }));
        foreach (var args in new[] { "{\"id\":\"locked\"}", "{\"id\":\"unknown\"}", "{\"id\":\"rest\",\"ids\":[\"smith\"]}", "{\"ids\":[\"rest\",\"rest\"]}" })
        {
            bool rejected = false;
            try { RoomCommand.Parse(State, args); } catch (ArgumentException) { rejected = true; }
            Require(rejected, args);
        }
        Pass("Commands reject combat, unavailable options, changed prices/screens and conflicting ids before dispatch");

        var select = State with { Room = new("Select", "移除两张牌", "选牌", [new("0", "打击", "副本一", false), new("1", "打击", "副本二", false)]) { Required = 2, Id = "select-1" } };
        Require(RoomCommand.Parse(select, "{\"id\":\"0\",\"ids\":[\"0\",\"1\"]}").Ids.Length == 2);
        bool tooFew = false;
        try { RoomCommand.Parse(select, "{\"id\":\"0\"}"); } catch (ArgumentException) { tooFew = true; }
        Require(tooFew);
        Pass("Grid instructions require the full distinct selection, including separate copies of a card");

        int dispatched = 0;
        var once = new RoomCommandSession(State, (_, record, _) =>
        { dispatched++; record("已提交休息"); return Task.FromResult(new ActionOutcome(true, "已提交休息")); });
        await Task.WhenAll(once.ExecuteAsync("{\"id\":\"rest\"}", default), once.ExecuteAsync("{\"id\":\"smith\"}", default));
        Require(dispatched == 1 && once.HasSubmitted);
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var session = new RoomCommandSession(State, (_, _, _) => { dispatched++; throw new Exception("Must not execute"); });
            try { await session.ExecuteAsync("{\"id\":\"rest\"}", cancelled.Token); } catch (OperationCanceledException) { }
            Require(dispatched == 1 && !session.HasSubmitted);
        }
        Pass("Repeated/parallel tool calls dispatch once; cancellation before dispatch performs no action");

        var uncertain = new RoomCommandSession(State, (_, record, _) =>
        { dispatched++; record("操作已尝试，结果未确认"); throw new InvalidOperationException("after click"); });
        try { await uncertain.ExecuteAsync("{\"id\":\"rest\"}", default); } catch (InvalidOperationException) { }
        await uncertain.ExecuteAsync("{\"id\":\"rest\"}", default);
        Require(dispatched == 2 && uncertain.Receipt.Contains("未确认"));
        Pass("An uncertain click preserves its receipt and cannot be retried by the model");

        int calls = 0, clicks = 0;
        var actions = new RoomCommandSession(State, (_, record, _) =>
        { clicks++; record("已提交休息，等待结算。"); return Task.FromResult(new ActionOutcome(true, "已提交休息，等待结算。")); });
        using (var client = new ChatClient(new Handler(async (request, token) =>
        {
            var body = await request.Content!.ReadAsStringAsync(token);
            using var json = JsonDocument.Parse(body);
            Require(json.RootElement.GetProperty("tools").EnumerateArray().Any(tool => tool.GetProperty("function").GetProperty("name").GetString() == "choose_room_option"));
            if (++calls == 1) return Tool("choose_room_option", "{\"id\":\"rest\"}");
            Require(json.RootElement.GetProperty("messages").EnumerateArray().Any(m => m.GetProperty("role").GetString() == "tool" && m.GetProperty("content").GetString()!.Contains("submitted")));
            return Reply("按你的要求休息。");
        })))
        {
            var answer = await client.AskAsync(Settings, "", "这里休息", State, [], default, actions: actions);
            Require(clicks == 1 && answer.Contains("等待结算"));
        }
        Pass("The model sees current options, dispatches through the scoped tool, and receives a real action receipt");

        calls = 0;
        using (var client = new ChatClient(new Handler(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Require(!body.RootElement.GetProperty("tools").EnumerateArray().Any(tool => tool.GetProperty("function").GetProperty("name").GetString() == "choose_room_option"));
            return ++calls == 1 ? Tool("choose_room_option", "{\"id\":\"rest\"}") : Reply("建议休息。");
        })))
        {
            await client.AskAsync(Settings, "", "这里怎么选", State, [], default,
                toolExecutor: (_, _) => throw new Exception("An ungranted action must not reach the executor"));
        }
        Pass("A model cannot invoke an unadvertised action through the read-only tool executor");

        calls = 0;
        var unavailable = new RoomCommandSession(State, (_, _, _) => throw new Exception("Must not dispatch"));
        using (var client = new ChatClient(new Handler(async (request, token) =>
        {
            if (++calls == 1) return new(HttpStatusCode.BadRequest);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Require(!body.RootElement.TryGetProperty("tools", out _));
            return Reply("工具不可用，请手动休息。");
        })))
        {
            var answer = await client.AskAsync(Settings, "", "这里休息", State, [], default, actions: unavailable);
            Require(!unavailable.HasSubmitted && answer.StartsWith("本次未点击"));
        }
        Pass("Providers without tools fall back to advice and clearly report no click");

        calls = 0;
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var committed = new RoomCommandSession(State, (_, record, _) =>
        { record("已提交休息"); return Task.FromResult(new ActionOutcome(true, "已提交休息")); });
        using (var client = new ChatClient(new Handler(async (_, token) =>
        {
            if (++calls == 1) return Tool("choose_room_option", "{\"id\":\"rest\"}");
            waiting.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Reply("unreachable");
        })))
        using (var conversation = new ChatConversation(client))
        {
            var pending = conversation.AskAsync(Settings, "", "这里休息", State, actions: committed);
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
            conversation.InterruptUnshownReply();
            try { await pending; } catch (OperationCanceledException) { }
            Require(conversation.Last?.Answer == "已提交休息");
            conversation.InterruptUnshownReply();
            Require(conversation.Last?.Answer == "已提交休息");
        }
        Pass("Cancelling or speaking after a submitted click retains the action in history instead of replaying it");
        return 9;
    }

    private static void Require(bool value, string? detail = null) { if (!value) throw new Exception("Command check failed: " + detail); }
    private static void Pass(string value) => Console.WriteLine("PASS " + value);
    private static HttpResponseMessage Reply(string text) => Response(new { content = text });
    private static HttpResponseMessage Tool(string name, string arguments) => Response(new { content = (string?)null,
        tool_calls = new[] { new { id = "call-1", type = "function", function = new { name, arguments } } } });
    private static HttpResponseMessage Response(object message) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message } } })) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => callback(request, token); }
}
