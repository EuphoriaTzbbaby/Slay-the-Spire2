using System.Text.RegularExpressions;

namespace SpireCompanion.Core;

public sealed record GroundingResult(string[] Unknown, string[] Known);

/// <summary>Checks quoted names in a chat answer against the current snapshot.</summary>
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
            else if (!unknown.Contains(name)) unknown.Add(name);
        }
        return new(unknown.ToArray(), mentioned.ToArray());
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
}
