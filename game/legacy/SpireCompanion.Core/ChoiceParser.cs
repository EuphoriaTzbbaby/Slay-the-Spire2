using System.Text.Json;

namespace SpireCompanion.Core;

/// <summary>
/// Turns the model's reply into a legal option id. Auto-execution must never act on a
/// guessed value, so anything that is not an exact offer id is rejected.
/// </summary>
public static class ChoiceParser
{
    public static string? Parse(string reply, IReadOnlyCollection<string> allowed) =>
        ParseWithReason(reply, allowed).Id;

    /// <summary>Same validation as Parse, plus the short reason the model was asked to include.</summary>
    public static (string? Id, string Reason) ParseWithReason(string reply, IReadOnlyCollection<string> allowed)
    {
        var (id, _, reason) = ParseWithIds(reply, allowed);
        return (id, reason);
    }

    /// <summary>
    /// Accepts an optional ranked "ids" array for screens that want several cards; every id is
    /// validated, and the primary choice is always first.
    /// </summary>
    public static (string? Id, string[] Ids, string Reason) ParseWithIds(string reply, IReadOnlyCollection<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(reply) || allowed.Count == 0) return (null, [], "");
        var text = reply.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            // Strip a fenced block without caring about the language tag.
            var firstBreak = text.IndexOf('\n');
            if (firstBreak > 0) text = text[(firstBreak + 1)..];
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0) text = text[..lastFence];
            text = text.Trim();
        }
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try
            {
                using var json = JsonDocument.Parse(text[start..(end + 1)]);
                if (json.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in new[] { "id", "choice", "option" })
                    {
                        if (!json.RootElement.TryGetProperty(property, out var value)) continue;
                        var id = value.ValueKind switch
                        {
                            JsonValueKind.String => value.GetString(),
                            JsonValueKind.Number => value.GetRawText(),
                            _ => null
                        };
                        if (id != null && allowed.Contains(id, StringComparer.Ordinal))
                        {
                            var ranked = new List<string> { id };
                            if (json.RootElement.TryGetProperty("ids", out var list) && list.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in list.EnumerateArray())
                                {
                                    var extra = item.ValueKind switch
                                    {
                                        JsonValueKind.String => item.GetString(),
                                        JsonValueKind.Number => item.GetRawText(),
                                        _ => null
                                    };
                                    if (extra != null && allowed.Contains(extra, StringComparer.Ordinal) && !ranked.Contains(extra))
                                        ranked.Add(extra);
                                }
                            }
                            return (id, ranked.ToArray(), Reason(json.RootElement));
                        }
                    }
                }
            }
            catch (JsonException) { /* fall through to the plain-text check */ }
        }
        // Accept a bare id only; arbitrary prose must not be treated as a choice.
        return allowed.Contains(text, StringComparer.Ordinal) ? (text, [text], "") : (null, [], "");
    }

    private static string Reason(JsonElement root)
    {
        foreach (var property in new[] { "reason", "why", "explanation" })
        {
            if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) continue;
            var text = value.GetString()?.Trim() ?? "";
            return text.Length > 120 ? text[..120] : text;
        }
        return "";
    }
}
