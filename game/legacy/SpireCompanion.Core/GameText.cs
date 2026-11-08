using System.Text.RegularExpressions;

namespace SpireCompanion.Core;

/// <summary>Game localization markup is data, not chat BBCode or model instructions.</summary>
public static class GameText
{
    public static string Plain(string value) => Regex.Replace(
        Regex.Replace(value ?? "", "<[^>]+>", ""), @"\[/?[a-zA-Z]+(?:=[^\]]*)?\]", "").Trim();

    public static string OptionDescription(string description, string relicDescription)
    {
        var primary = Plain(description);
        return string.IsNullOrWhiteSpace(primary) || primary == "未知" ? Plain(relicDescription) : primary;
    }
}
