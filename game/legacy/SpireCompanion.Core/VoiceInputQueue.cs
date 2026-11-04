namespace SpireCompanion.Core;

public sealed record PendingVoiceInput(string SessionId, string InputId, string Text);
public sealed record ComposedVoiceTurn(PendingVoiceInput[] Inputs, string Question, string NewText);

/// <summary>Bounded turns while the text model is busy. Closed/old sessions never replay.</summary>
public sealed class VoiceInputQueue
{
    private readonly Queue<PendingVoiceInput> inputs = new();
    public int Count => inputs.Count;
    public int TextLength => inputs.Sum(input => input.Text.Length);

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

    public ComposedVoiceTurn? TakeTurn(string sessionId, string interruptedQuestion)
    {
        var current = new List<PendingVoiceInput>();
        while (Take(sessionId) is { } input) current.Add(input);
        if (current.Count == 0) return null;
        var text = string.Join("\n", current.Select(input => input.Text));
        return new(current.ToArray(), interruptedQuestion.Length == 0 ? text
            : interruptedQuestion + "\n（我接着补充或更正：）\n" + text, text);
    }
}
