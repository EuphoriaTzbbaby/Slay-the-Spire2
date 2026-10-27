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
        return passed;
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) => callback(request,token);
    }
}
