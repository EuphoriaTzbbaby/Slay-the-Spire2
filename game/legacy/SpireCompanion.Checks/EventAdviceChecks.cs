using System.Net;
using System.Text.Json;
using SpireCompanion.Core;

internal static class EventAdviceChecks
{
    public static async Task<int> RunAsync()
    {
        var options = new RoomOptionView[] {
            new("0", "新叶", "变化1张牌。", false),
            new("1", "钓鱼竿", "每3场普通战斗，随机升级你牌组中的一张牌。", false),
            new("2", "华美发束", "失去所有金币。为第一次卡牌奖励中的所有牌附魔：华彩。", false),
            new("locked", "不可选", "条件不足", true),
            new("ui:back", "返回", "NBackButton", false)
        };
        var snapshot = new GameSnapshot("EventRoom", "铁甲战士", 80, 80, 100, [], "无", false, "")
            { Room = new("Event", "涅奥", "", options) };
        using var client = new ChatClient(new Handler(async request =>
        {
            using var data = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var content = data.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            using var context = JsonDocument.Parse(content);
            var offers = context.RootElement.GetProperty("options");
            Require(offers.GetArrayLength() == 4 && !offers.EnumerateArray().Any(offer => offer.GetProperty("id").GetString() == "ui:back"));
            Require(offers[2].GetProperty("detail").GetString()!.Contains("华彩"));
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new {
                choices = new[] { new { message = new { content = "{\"id\":\"1\",\"reason\":\"铁皮鲨鱼每场战斗升级新牌\"}" } } }
            })) };
        }));
        var answer = await client.AskAsync(new("https://model.example/v1", "test"), "", "你认为应该选哪一个呀", snapshot, [], default,
            toolExecutor: (_, _) => throw new Exception("Advice must never dispatch a game action"));
        Require(answer.Contains("建议选「钓鱼竿」") && answer.Contains("变化1张牌") && answer.Contains("华彩"));
        Require(!answer.Contains("铁皮鲨鱼") && !answer.Contains("新牌") && !answer.Contains("返回"));
        foreach (var bad in new[] { "铁皮鲨鱼", "{\"id\":\"unknown\"}", "{\"id\":\"locked\"}" })
            Require(CurrentEventAdvice.Render(CurrentEventAdvice.Options(snapshot), bad).Contains("未通过选项校验"));
        Require(!CurrentEventAdvice.Applies(snapshot, "你好") && !CurrentEventAdvice.Applies(snapshot with { Room = null }, "选哪个"));
        Console.WriteLine("PASS Event advice rejects invented names/effects and locked/unknown ids; canonical game facts include complete costs and benefits without clicks");
        return 1;
    }
    private static void Require(bool condition) { if (!condition) throw new Exception("Event advice check failed"); }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request);
    }
}
