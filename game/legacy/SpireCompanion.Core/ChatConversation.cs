namespace SpireCompanion.Core;

/// <summary>One in-flight message; cancelled/old requests cannot append history.</summary>
public sealed class ChatConversation(ChatClient client) : IDisposable
{
    private readonly object sync = new();
    private readonly List<ChatTurn> history = [];
    private CancellationTokenSource? active;
    private bool disposed;
    public async Task<ChatReply> AskAsync(ChatSettings settings, string key, string question, GameSnapshot snapshot)
    {
        CancellationTokenSource request;
        ChatTurn[] previous;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (active != null) throw new InvalidOperationException("上一条消息仍在处理或取消中。");
            active = request = new CancellationTokenSource();
            previous = history.ToArray();
        }
        try
        {
            var text = await client.AskAsync(settings, key, question, snapshot, previous, request.Token).ConfigureAwait(false);
            lock (sync)
            {
                request.Token.ThrowIfCancellationRequested();
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
