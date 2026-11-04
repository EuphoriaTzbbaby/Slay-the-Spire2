namespace SpireCompanion.Core;

/// <summary>One in-flight message; cancelled/old requests cannot append history.</summary>
public sealed class ChatConversation(ChatClient client) : IDisposable
{
    private readonly object sync = new();
    private readonly List<ChatTurn> history = [];
    private CancellationTokenSource? active;
    private bool disposed;
    private long requestVersion, committedVersion;
    private long actionVersion;
    public (string Question, string Answer)? Last
    {
        get { lock (sync) return history.Count == 0 ? null : (history[^1].Question, history[^1].Answer); }
    }

    public async Task<ChatReply> AskAsync(ChatSettings settings, string key, string question, GameSnapshot snapshot, string plan = "", bool screenChanged = false, Func<string, string, string?>? toolExecutor = null, Action<string>? toolTrace = null, string liveState = "", RoomCommandSession? actions = null)
    {
        CancellationTokenSource request;
        ChatTurn[] previous;
        long version;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (active != null) throw new InvalidOperationException("上一条消息仍在处理或取消中。");
            active = request = new CancellationTokenSource();
            version = ++requestVersion;
            // A different screen invalidates earlier card/option context; start that request clean.
            if (screenChanged) history.Clear();
            previous = history.ToArray();
        }
        try
        {
            var text = await client.AskAsync(settings, key, question, snapshot, previous, request.Token, plan, screenChanged, toolExecutor, toolTrace, liveState, actions).ConfigureAwait(false);
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
                committedVersion = version;
                if (actions?.HasSubmitted == true) actionVersion = version;
                while (history.Count > 6 || history.Sum(t => t.Question.Length + t.Answer.Length) > 18_000) history.RemoveAt(0);
            }
            return new(text, snapshot.Fingerprint);
        }
        finally
        {
            lock (sync)
            {
                // A click cannot be undone by cancelling the following model reply. Preserve its receipt.
                if (!disposed && actions?.HasSubmitted == true && committedVersion != version)
                {
                    history.Add(new(question, actions.Receipt));
                    committedVersion = actionVersion = version;
                    while (history.Count > 6 || history.Sum(t => t.Question.Length + t.Answer.Length) > 18_000) history.RemoveAt(0);
                }
                active = null;
                request.Dispose();
            }
        }
    }
    public void Cancel() { lock (sync) active?.Cancel(); }
    /// <summary>The UI is still awaiting this answer; a spoken correction supersedes it.</summary>
    public void InterruptUnshownReply()
    {
        lock (sync)
        {
            active?.Cancel();
            if (history.Count > 0 && committedVersion == requestVersion && actionVersion != requestVersion)
            {
                history.RemoveAt(history.Count - 1);
                committedVersion = 0;
            }
        }
    }
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
