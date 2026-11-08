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
            Require(CurrentChoiceAdvice.Render(CurrentChoiceAdvice.Options(snapshot), bad).Contains("未通过选项校验"));
        Require(!CurrentChoiceAdvice.Applies(snapshot, "你好") && !CurrentChoiceAdvice.Applies(snapshot with { Room = null }, "选哪个"));
        Console.WriteLine("PASS Event advice rejects invented names/effects and locked/unknown ids; canonical game facts include complete costs and benefits without clicks");
        var reward = snapshot with { Room = new("Reward", "卡牌奖励", "选择一张牌", [
            new("0", "灰烬打击", "造成伤害。你的消耗牌堆中每有一张牌，伤害增加。", false),
            new("1", "血墙", "失去2点生命。获得16点格挡。重放1。", false),
            new("2", "杂耍", "将你在每回合打出的第三张攻击牌的复制品加入你的手牌。重放1。", false),
            new("skip", "跳过卡牌奖励", "不拿牌，继续", false)]) };
        using var rewardClient = new ChatClient(new Handler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            using var context = JsonDocument.Parse(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            Require(context.RootElement.GetProperty("options")[1].GetProperty("name").GetString() == "血墙");
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new {
                choices = new[] { new { message = new { content = "{\"id\":\"1\",\"reason\":\"荆棘之墙和诡计比不上硬核打击\"}" } } }
            })) };
        }));
        var rewardAnswer = await rewardClient.AskAsync(new("https://model.example/v1", "test"), "", "你觉得现在选哪个", reward, [], default);
        Require(rewardAnswer.Contains("建议选「血墙」") && rewardAnswer.Contains("失去2点生命") && rewardAnswer.Contains("杂耍"));
        Require(!rewardAnswer.Contains("硬核") && !rewardAnswer.Contains("荆棘") && !rewardAnswer.Contains("诡计"));
        Require(CurrentChoiceAdvice.Applies(reward, "当前页面你捕捉不到吗"));
        Console.WriteLine("PASS Card rewards and page follow-ups use current canonical names/effects, including skip, without model-invented text");
        Require(GameText.Plain("[gold]变化[/gold][blue]1[/blue]张牌。") == "变化1张牌。");
        Require(GameText.Plain("数组[0, 1] [待确认]") == "数组[0, 1] [待确认]");
        Require(GameText.OptionDescription("[red]失去[/red]金币，附魔华彩。", "拾起时失去金币，附魔华彩。") == "失去金币，附魔华彩。");
        Require(GameText.OptionDescription("", "[gold]升级[/gold]一张牌") == "升级一张牌");
        var marked = CurrentChoiceAdvice.Render([new("0", "新叶", "[gold]变化[/gold][blue]1[/blue]张牌。", false)], "0");
        Require(marked.Contains("变化1张牌") && !marked.Contains("[gold]"));
        Console.WriteLine("PASS Game markup is removed before display; complete option text is preferred over duplicate relic text");
        var screenshot = reward with { Room = new("Reward", "卡牌奖励", "", [
            new("0", "惊逃", "费用2：在你的回合结束时，随机打出你手牌中的1张攻击牌攻击随机敌人。", false),
            new("1", "暴走", "费用1：造成10点伤害。将这张牌在本场战斗中的伤害增加5。", false),
            new("2", "跃跃欲试", "费用3：获得15点格挡。每有一点力量额外获得5点格挡。", false)]) { Id = "reward-a" } };
        using var typoClient = new ChatClient(new Handler(async request =>
        {
            using var data = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            using var context = JsonDocument.Parse(data.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            Require(context.RootElement.GetProperty("options")[0].GetProperty("name").GetString() == "惊逃");
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new {
                choices = new[] { new { message = new { content = "{\"id\":\"1\",\"reason\":\"慎选和猛攻比暴走更强\"}" } } }
            })) };
        }));
        foreach (var transcript in new[] { "这三个才你蜕变抓那一个", "自说答案", "就它们里面吧" })
        {
            var result = await typoClient.AskAsync(new("https://model.example/v1", "test"), "", transcript, screenshot, [], default);
            Require(result.Contains("暴走") && !result.Contains("慎选") && !result.Contains("猛攻"));
            if (transcript.Contains("答案")) Require(result == "建议选「暴走」。");
        }
        Require(!CurrentChoiceAdvice.Applies(screenshot, "你好") && !CurrentChoiceAdvice.Applies(screenshot, "快说一句我爱你"));
        Console.WriteLine("PASS Actual ASR typos and keyword-free card follow-ups remain bounded; answer-only requests are concise and smalltalk stays separate");
        var animated = screenshot with { Room = screenshot.Room! with { Options = [.. screenshot.Room!.Options,
            new("ui:99", "悬浮链接", "位置(51%,30%)", false)] } };
        Require(ReplyFreshness.Same(screenshot, animated));
        Require(!ReplyFreshness.Same(screenshot, screenshot with { Room = screenshot.Room! with { Id = "reward-b" } }));
        Require(!ReplyFreshness.Same(screenshot, screenshot with { Room = screenshot.Room! with { Options = [new("0", "其他牌", "不同效果", false)] } }));
        Require(!ReplyFreshness.Same(screenshot, screenshot with { Gold = 0 }));
        Console.WriteLine("PASS Reply freshness ignores decorative UI changes but rejects real offers, screen changes and gameplay-resource changes");
        return 5;
    }
    private static void Require(bool condition) { if (!condition) throw new Exception("Event advice check failed"); }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request);
    }
}
