namespace SpireCompanion.Core;

public sealed record PendingVoiceInput(string SessionId, string InputId, string Text);

/// <summary>Bounded turns while the text model is busy. Closed/old sessions never replay.</summary>
public sealed class VoiceInputQueue
{
    private readonly Queue<PendingVoiceInput> inputs = new();
    public int Count => inputs.Count;

    public bool TryEnqueue(string sessionId, string inputId, string text)
    {
        if (inputs.Count >= 4) return false;
        inputs.Enqueue(new(sessionId, inputId, text));
        return true;
    }

    public PendingVoiceInput? Take(string sessionId)
    {
        while (inputs.TryDequeue(out var input))
            if (input.SessionId == sessionId) return input;
        return null;
    }

    public void Clear() => inputs.Clear();
}
