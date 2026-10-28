using System.Net;
using System.Text.Json;
using SpireCompanion.Core;

internal static class ChatChecks
{
    private static void Require(bool value) { if (!value) throw new Exception("Chat assertion failed"); }
    private static HttpResponseMessage Reply() => new(HttpStatusCode.OK)
    { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"先补防御。\"}}]}") };
    public static async Task<int> RunAsync()
    {
        int passed = 0;
        async Task Check(string name, Func<Task> test) { await test(); passed++; Console.WriteLine("PASS " + name); }
        var settings = new ChatSettings("https://model.example/v1/", "test-model");
        var snapshot = new GameSnapshot("CombatRoom", "铁甲战士", 60, 80, 100, ["打击"], "1:Player", true, "");
        await Check("Model request includes fresh state and bounded role history, never key in body", async () =>
        {
            using var client = new ChatClient(new Handler(async (request, token) =>
            {
                Require(request.RequestUri!.AbsoluteUri == "https://model.example/v1/chat/completions");
                Require(request.Headers.Authorization?.Parameter == "test-secret");
                var text = await request.Content!.ReadAsStringAsync(token);
                Require(!text.Contains("test-secret"));
                using var json = JsonDocument.Parse(text);
                Require(json.RootElement.GetProperty("model").GetString() == "test-model");
                var messages = json.RootElement.GetProperty("messages");
                var user = JsonDocument.Parse(messages[messages.GetArrayLength()-1].GetProperty("content").GetString()!);
                Require(user.RootElement.GetProperty("snapshot").GetProperty("state").GetProperty("Hp").GetInt32() == 60);
                Require(user.RootElement.GetProperty("snapshot").GetProperty("fingerprint").GetString() == snapshot.Fingerprint);
                return Reply();
            }));
            Require(await client.AskAsync(settings, "test-secret", "怎么打", snapshot, [], default) == "先补防御。");
        });
        await Check("Connection probe uses a separate minimal request without live game data", async () =>
        {
            using var client = new ChatClient(new Handler(async (request, token) =>
            {
                var body = await request.Content!.ReadAsStringAsync(token);
                using var json = JsonDocument.Parse(body);
                var messages = json.RootElement.GetProperty("messages");
                using var context = JsonDocument.Parse(messages[messages.GetArrayLength()-1].GetProperty("content").GetString()!);
                Require(context.RootElement.GetProperty("snapshot").GetProperty("state").GetProperty("Scene").GetString() == "连接测试");
                Require(messages.GetArrayLength() == 2);
                return Reply();
            }));
            Require(!string.IsNullOrWhiteSpace(await client.TestAsync(settings, "", default)));
        });
        await Check("Service errors do not expose provider response contents", async () =>
        {
            using var client = new ChatClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            { Content = new StringContent("sensitive-provider-content") })));
            try { await client.AskAsync(settings,"","分析",snapshot,[],default); throw new Exception("Expected failure"); }
            catch (ModelReplyException error) { Require(error.Message.Contains("401") && !error.Message.Contains("sensitive-provider-content")); }
        });
        await Check("Cancellation rejects concurrent sends and does not enter conversation history", async () =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls=0;
            using var client = new ChatClient(new Handler(async (request, token) =>
            {
                if (Interlocked.Increment(ref calls)==1)
                { entered.SetResult(); await Task.Delay(Timeout.Infinite,token); }
                Require(!(await request.Content!.ReadAsStringAsync(token)).Contains("cancel-me"));
                return Reply();
            }));
            using var conversation = new ChatConversation(client);
            var pending=conversation.AskAsync(settings,"","cancel-me",snapshot);
            await entered.Task;
            try { await conversation.AskAsync(settings,"","parallel",snapshot); throw new Exception("Expected busy"); }
            catch(InvalidOperationException) { }
            conversation.Cancel();
            try { await pending; throw new Exception("Expected cancellation"); } catch(OperationCanceledException) { }
            var next=await conversation.AskAsync(settings,"","fresh",snapshot);
            Require(next.Text=="先补防御。" && calls==2);
        });
        await Check("Empty replies cannot be treated as useful advice", async () =>
        {
            using var client=new ChatClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {Content=new StringContent("{\"choices\":[]}")})));
            try {await client.AskAsync(settings,"","分析",snapshot,[],default);throw new Exception("Expected failure");}
            catch(ModelReplyException) { }
        });
        await Check("Oversized response is rejected before JSON advice parsing", async () =>
        {
            using var client=new ChatClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {Content=new StringContent(new string('x',140_000))})));
            try {await client.AskAsync(settings,"","分析",snapshot,[],default);throw new Exception("Expected failure");}
            catch(ModelReplyException error) {Require(error.Message.Contains("过大"));}
        });
        await Check("DeepSeek defaults and blank-setting migration preserve custom providers", () =>
        {
            var defaults=new ChatSettings();
            Require(defaults.Endpoint=="https://api.deepseek.com" && defaults.Model=="deepseek-flash" && defaults.RememberApiKey);
            Require(defaults.RequestUri().AbsoluteUri=="https://api.deepseek.com/chat/completions");
            var path=Path.Combine(Path.GetTempPath(),"spire-defaults-"+Guid.NewGuid()+".json");
            try
            {
                File.WriteAllText(path,"{\"Endpoint\":\"\",\"Model\":\"\"}");
                Require(ChatSettings.Load(path).Endpoint==defaults.Endpoint && ChatSettings.Load(path).Model==defaults.Model);
                settings.Save(path);Require(ChatSettings.Load(path).Endpoint==settings.Endpoint && ChatSettings.Load(path).Model==settings.Model);
            }
            finally {File.Delete(path);}
            return Task.CompletedTask;
        });
        await Check("Settings persist only non-secret options and reject credential-bearing URL", () =>
        {
            var path=Path.Combine(Path.GetTempPath(),"spire-chat-check-"+Guid.NewGuid()+".json");
            try
            {
                settings.Save(path);Require(ChatSettings.Load(path)==settings);
                var text=File.ReadAllText(path);Require(!text.Contains("\"ApiKey\"") && !text.Contains("test-secret"));
                try{_=(settings with {Endpoint="https://model.example/v1?api_key=secret"}).RequestUri();throw new Exception("Expected rejection");}
                catch(ArgumentException) { }
            }
            finally {File.Delete(path);}
            return Task.CompletedTask;
        });
        await Check("Content parts and reasoning-only replies are handled", async () =>
        {
            using var parts = new ChatClient(new Handler((request, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":[{\"text\":\"先看\"},{\"text\":\"防御\"}]}}]}")
            })));
            Require(await parts.AskAsync(settings, "k", "问", snapshot, [], default) == "先看防御");
            using var thinking = new ChatClient(new Handler((request, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"\",\"reasoning_content\":\"想了很久\"},\"finish_reason\":\"length\"}]}")
            })));
            try
            {
                await thinking.AskAsync(settings, "k", "问", snapshot, [], default);
                throw new Exception("Expected reasoning-only reply to be rejected");
            }
            catch (ModelReplyException error) { Require(error.Message.Contains("思考") || error.Message.Contains("推理内容")); }
        });
        await Check("Screen guidance is specific to the current interface", () =>
        {
            var combat = new GameSnapshot("CombatRoom", "铁甲战士", 60, 80, 168, ["打击"], "1:Player", true, "")
            { Cards = [new CardView(0, "STRIKE", "打击", "1", false)], Enemies = [new EnemyView(0, "SLIME", "海洋混混", 26, 44, 0)] };
            var eventRoom = combat with { Room = new RoomView("Event", "金色神像", "一座神像。", [new RoomOptionView("0", "祈祷", "获得最大生命", false)]) };
            var shop = combat with { Room = new RoomView("Shop", "商店", "", [new RoomOptionView("0", "打击", "50 金币", false)]) };
            var reward = combat with { Room = new RoomView("Reward", "卡牌奖励", "", [new RoomOptionView("0", "重击", "费用 2", false)]) };
            var rest = combat with { Room = new RoomView("Rest", "休息点", "", [new RoomOptionView("0", "休息", "回复生命", false)]) };
            var map = combat with { Room = new RoomView("Map", "地图", "", [new RoomOptionView("0", "精英", "可达节点", false)]) };
            var texts = new[] { ScreenGuidance.For(combat), ScreenGuidance.For(eventRoom), ScreenGuidance.For(shop),
                ScreenGuidance.For(reward), ScreenGuidance.For(rest), ScreenGuidance.For(map) };
            Require(texts.All(text => text.Length > 20));
            Require(texts.Distinct().Count() == texts.Length);
            Require(ScreenGuidance.For(shop).Contains("价格"));
            Require(ScreenGuidance.For(map).Contains("可达节点"));
            Require(ScreenGuidance.For(combat) == ScreenGuidance.Combat);
            Require(ScreenGuidance.For(new GameSnapshot("主菜单", "无", 0, 0, 0, [], "无", false, "")) == ScreenGuidance.General);
            return Task.CompletedTask;
        });
        await Check("Chat request carries the guidance for the current screen", async () =>
        {
            using var client = new ChatClient(new Handler(async (request, token) =>
            {
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var system = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
                Require(system.Contains("当前是商店界面"));
                Require(system.Contains("当前界面真实"));
                return Reply();
            }));
            var shop = snapshot with { Room = new RoomView("Shop", "商店", "", [new RoomOptionView("0", "打击", "50 金币", false)]) };
            Require(!string.IsNullOrWhiteSpace(await client.AskAsync(settings, "test-secret", "买什么", shop, [], default)));
        });
        await Check("Structured choice accepts only offered ids", () =>
        {
            string[] allowed = ["0", "1", "SMITH"];
            Require(ChoiceParser.Parse("{\"id\":\"1\"}", allowed) == "1");
            Require(ChoiceParser.Parse("```json\n{\"id\":\"SMITH\"}\n```", allowed) == "SMITH");
            Require(ChoiceParser.Parse("0", allowed) == "0");
            Require(ChoiceParser.Parse("{\"id\":\"9\"}", allowed) == null);
            Require(ChoiceParser.Parse("我觉得选第一个", allowed) == null);
            Require(ChoiceParser.Parse("", allowed) == null);
            var (id, reason) = ChoiceParser.ParseWithReason("{\"id\":\"1\",\"reason\":\"缺防御牌\"}", allowed);
            Require(id == "1" && reason == "缺防御牌");
            Require(ChoiceParser.ParseWithReason("{\"id\":\"9\",\"reason\":\"x\"}", allowed).Id == null);
            return Task.CompletedTask;
        });
        await Check("Duplicate card offers are merged before the model picks", () =>
        {
            RoomOptionView[] options =
            [
                new("0", "打击", "费用 1", false),
                new("1", "打击", "费用 1", false),
                new("2", "防御", "费用 1", false),
                new("3", "祭品", "费用 0", false),
            ];
            var merged = RoomOptions.MergeDuplicates(options);
            Require(merged.Length == 3);
            Require(merged[0].Id == "0" && merged[0].Name == "打击" && merged[0].Detail.Contains("共 2 张"));
            Require(merged[1].Id == "2" && merged[1].Name == "防御" && merged[1].Detail == "费用 1");
            Require(merged[2].Name == "祭品");
            return Task.CompletedTask;
        });
        await Check("Proactive comment stays short and disables thinking", async () =>
        {
            string? body = null;
            using var client = new ChatClient(new Handler(async (request, token) =>
            {
                body = await request.Content!.ReadAsStringAsync(token);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"这层有点阴森，小心点。\"}}]}")
                };
            }));
            var reply = await client.CommentAsync(settings, "k", snapshot, "战斗刚开始", default);
            Require(reply.Contains("阴森"));
            Require(body != null && body.Contains("\"thinking\""));
            using var request = System.Text.Json.JsonDocument.Parse(body!);
            var system = request.RootElement.GetProperty("messages")[0].GetProperty("content").GetString() ?? "";
            Require(system.Contains("不超过 30 个字"));
            return;
        });
        await Check("Ranked ids keep the primary first and drop unknown ids", () =>
        {
            var (id, ids, reason) = ChoiceParser.ParseWithIds(
                "{\"id\":\"2\",\"ids\":[\"2\",\"7\",\"99\"],\"reason\":\"移除两张\"}", new[] { "2", "7" });
            Require(id == "2");
            Require(ids.Length == 2 && ids[0] == "2" && ids[1] == "7");
            Require(reason == "移除两张");
            var bad = ChoiceParser.ParseWithIds("{\"id\":\"99\"}", new[] { "2" });
            Require(bad.Id == null && bad.Ids.Length == 0);
            return Task.CompletedTask;
        });
        await Check("Personality modes differ and comfort matches the mode", () =>
        {
            var modes = new[] { "quiet", "normal", "chatty" };
            var instructions = modes.Select(Personality.Instructions).ToArray();
            var comforts = modes.Select(Personality.Comfort).ToArray();
            Require(instructions.All(text => text.Length > 10));
            Require(instructions.Distinct().Count() == 3);
            Require(comforts.Distinct().Count() == 3);
            Require(Personality.Persona.Contains("奶龙"));
            return Task.CompletedTask;
        });
        await Check("Markdown replies become BBCode and raw brackets are escaped", () =>
        {
            var text = ChatMarkdown.ToBbcode("**结论**：先防御。\n- 打 `打击`\n# 标题\n[链接](https://example.com)\n```\ncode\n```\n数值 [2]");
            Require(text.Contains("[b]结论[/b]"));
            Require(text.Contains("• 打 [color=#B45309]打击[/color]"));
            Require(text.Contains("[b]标题[/b]"));
            Require(text.Contains("链接（https://example.com）"));
            Require(!text.Contains("```"));
            Require(text.Contains("[lb]2[rb]"));
            return Task.CompletedTask;
        });
        return passed;
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) => callback(request,token);
    }
}
