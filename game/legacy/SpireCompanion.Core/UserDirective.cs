using System.Text.RegularExpressions;

namespace SpireCompanion.Core;

/// <summary>Extracts an explicit "pick X" instruction from chat so auto decisions can obey it.</summary>
public static partial class UserDirective
{
    public static string PinTarget(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        // "别选 X" must not be read as "选 X".
        if (NegativePattern().IsMatch(text)) return "";
        var match = Pattern().Match(text);
        if (!match.Success) return "";
        return match.Groups[1].Value.Trim('」', '”', '"', '。', '，');
    }

    [GeneratedRegex(@"(?:一定|必须|就要|就|请|帮我)?\s*(?:选|拿|买|升级|删)\s*[「“""]?([\p{IsCJKUnifiedIdeographs}A-Za-z0-9·]{1,12})")]
    private static partial Regex Pattern();

    [GeneratedRegex(@"(?:别|不要|不用|不能|千万别)\s*(?:选|拿|买|升级|删)")]
    private static partial Regex NegativePattern();
}
