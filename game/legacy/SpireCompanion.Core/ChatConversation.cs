namespace SpireCompanion.Core;

/// <summary>One in-flight message; cancelled/old requests cannot append history.</summary>
public sealed class ChatConversation(ChatClient client) : IDisposable
{
    private readonly object sync = new();
    private readonly List<ChatTurn> history = [];
    private CancellationTokenSource? active;
    private bool disposed;
    public async Task<ChatReply> AskAsync(ChatSettings settings, string key, string question, GameSnapshot snapshot, string plan = "", bool screenChanged = false, Func<string, string, string?>? toolExecutor = null, Action<string>? toolTrace = null)
    {
        CancellationTokenSource request;
        ChatTurn[] previous;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (active != null) throw new InvalidOperationException("上一条消息仍在处理或取消中。");
            active = request = new CancellationTokenSource();
            // A different screen invalidates earlier card/option context; start that request clean.
            if (screenChanged) history.Clear();
            previous = history.ToArray();
        }
        try
        {
            var text = await client.AskAsync(settings, key, question, snapshot, previous, request.Token, plan, screenChanged, toolExecutor, toolTrace).ConfigureAwait(false);
            lock (sync)
            {
                request.Token.ThrowIfCancellationRequested();
                var grounding = AnswerGrounding.Check(text, snapshot);
                if (grounding.Unknown.Length > 0)
                {
                    // Do not show or remember an answer that names cards outside this screen.
                    var blocked = $"（这条回答被拦截：模型提到了当前界面没有的卡牌：{string.Join("、", grounding.Unknown)}。请点「分析当前局面」或再问一次。）";
                    return new(blocked, snapshot.Fingerprint);
                }
                history.Add(new(question, text));
                while (history.Count > 6 || history.Sum(t => t.Question.Length + t.Answer.Length) > 18_000) history.RemoveAt(0);
            }
            return new(text, snapshot.Fingerprint);
        }
        finally
        {
            lock (sync) { active = null; request.Dispose(); }
        }
    }
    public void Cancel() { lock (sync) active?.Cancel(); }
    public void Clear()
    {
        lock (sync)
        {
            if (active != null) throw new InvalidOperationException("请先取消并等待当前回复结束。");
            history.Clear();
        }
    }
    public void Dispose()
    {
        lock (sync) { disposed = true; active?.Cancel(); history.Clear(); }
        // The panel disposes the client separately after requests have received cancellation.
    }
}
