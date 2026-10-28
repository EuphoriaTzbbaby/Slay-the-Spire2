using System.Text.RegularExpressions;

namespace SpireCompanion.Core;

/// <summary>Reads "how many cards does this selection screen want" from its prompt text.</summary>
public static partial class ScreenPrompt
{
    public static int RequiredCards(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return 1;
        var match = CountPattern().Match(prompt);
        if (match.Success)
        {
            var token = match.Groups[1].Value;
            if (int.TryParse(token, out var count) && count is >= 1 and <= 10) return count;
            var map = new Dictionary<string, int> { ["两"] = 2, ["二"] = 2, ["三"] = 3, ["四"] = 4, ["五"] = 5 };
            if (map.TryGetValue(token, out var value)) return value;
        }
        return 1;
    }

    [GeneratedRegex(@"(\d+|[两二三四五])\s*(?:张|cards?)")]
    private static partial Regex CountPattern();
}
