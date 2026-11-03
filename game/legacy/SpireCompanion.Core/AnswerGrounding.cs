using System.Text.RegularExpressions;

namespace SpireCompanion.Core;

public sealed record GroundingResult(string[] Unknown, string[] Known);

/// <summary>Checks quoted game references, while leaving ordinary quoted speech alone.</summary>
public static partial class AnswerGrounding
{
    public static GroundingResult Check(string reply, GameSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(reply)) return new([], []);
        var known = KnownNames(snapshot);
        var unknown = new List<string>();
        var mentioned = new List<string>();
        foreach (Match match in QuotePattern().Matches(reply))
        {
            var name = match.Groups[1].Value.Trim();
            if (name.Length is < 2 or > 12) continue;
            if (known.Any(candidate => candidate == name || candidate.Contains(name) || name.Contains(candidate)))
            {
                if (!mentioned.Contains(name)) mentioned.Add(name);
            }
            else if (IsGameReference(reply, match) && !unknown.Contains(name)) unknown.Add(name);
        }
        return new(unknown.ToArray(), mentioned.ToArray());
    }

    private static bool IsGameReference(string reply, Match quote)
    {
        // Quotes alone do not mean a card name: e.g. the player may ask us to say「我爱你」.
        // Examine the nearby clause for a game effect or an action referring to a quoted choice.
        var start = Math.Max(0, quote.Index - 20);
        var end = Math.Min(reply.Length, quote.Index + quote.Length + 24);
        for (int i = quote.Index - 1; i >= start; i--)
            if (reply[i] is '。' or '！' or '？' or '\n') { start = i + 1; break; }
        for (int i = quote.Index + quote.Length; i < end; i++)
            if (reply[i] is '。' or '！' or '？' or '\n') { end = i; break; }
        return GameReferencePattern().IsMatch(reply[start..end]);
    }

    private static HashSet<string> KnownNames(GameSnapshot snapshot)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(snapshot.Character)) names.Add(snapshot.Character);
        foreach (var card in snapshot.Cards) names.Add(card.Name);
        foreach (var card in snapshot.Deck) names.Add(card.Name);
        foreach (var relic in snapshot.Relics) names.Add(relic.Name);
        foreach (var potion in snapshot.Potions) names.Add(potion.Name);
        if (snapshot.Room is { } room)
            foreach (var option in room.Options) names.Add(option.Name);
        return names;
    }

    [GeneratedRegex("[「“\"]([^」”\"\\n]{2,12})[」”\"]")]
    private static partial Regex QuotePattern();

    [GeneratedRegex("卡牌|这张牌|那张牌|遗物|药水|金币|费用|费[，,：:）)]|伤害|格挡|出牌|抽牌|锻造|删牌|移除|升级|(?:选择|换成|推荐|优先|选|买|拿|出)\\s*[「“\"]")]
    private static partial Regex GameReferencePattern();
}
