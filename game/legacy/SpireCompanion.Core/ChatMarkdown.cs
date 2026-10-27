using System.Text;
using System.Text.RegularExpressions;

namespace SpireCompanion.Core;

/// <summary>
/// Minimal Markdown to BBCode for the chat transcript: the model answers in Markdown,
/// but RichTextLabel needs BBCode and must never interpret raw square brackets.
/// </summary>
public static partial class ChatMarkdown
{
    private const string CodeColor = "#B45309";

    public static string ToBbcode(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return "";

        // Drop fence markers first, or the inline-code pass swallows ```code``` as one span.
        bool fence = false;
        var kept = new List<string>();
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```")) { fence = !fence; continue; }
            kept.Add(line);
        }
        var text = string.Join('\n', kept);

        var codes = new List<string>();
        text = CodeSpan().Replace(text, match =>
        {
            codes.Add(match.Groups[1].Value);
            return $"\u0001{codes.Count - 1}\u0001";
        });
        text = Link().Replace(text, "$1（$2）");
        text = Escape(text);
        text = Bold().Replace(text, "[b]$1[/b]");
        text = BoldUnderscore().Replace(text, "[b]$1[/b]");
        text = Italic().Replace(text, "[i]$1[/i]");

        var builder = new StringBuilder();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                builder.Append("[b]").Append(trimmed.TrimStart('#').Trim()).Append("[/b]\n");
                continue;
            }
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ "))
            {
                builder.Append("• ").Append(trimmed[2..]).Append('\n');
                continue;
            }
            builder.Append(line).Append('\n');
        }
        var result = builder.ToString().TrimEnd('\n');
        // Restore inline code after the structural pass so its brackets are escaped exactly once.
        for (int i = 0; i < codes.Count; i++)
            result = result.Replace($"\u0001{i}\u0001", $"[color={CodeColor}]{Escape(codes[i])}[/color]");
        while (result.Contains("\n\n\n")) result = result.Replace("\n\n\n", "\n\n");
        return result;
    }

    /// <summary>One pass, because chained Replace would rewrite the brackets it just inserted.</summary>
    private static string Escape(string value) => Bracket().Replace(value, match => match.Value == "[" ? "[lb]" : "[rb]");

    [GeneratedRegex("[\\[\\]]")]
    private static partial Regex Bracket();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"\*\*(.+?)\*\*", RegexOptions.Singleline)]
    private static partial Regex Bold();

    [GeneratedRegex(@"__(.+?)__", RegexOptions.Singleline)]
    private static partial Regex BoldUnderscore();

    [GeneratedRegex(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", RegexOptions.Singleline)]
    private static partial Regex Italic();
}
