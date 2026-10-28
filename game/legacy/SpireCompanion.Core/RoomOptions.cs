namespace SpireCompanion.Core;

/// <summary>
/// Collapses identical offers before they reach the model. Copies of the same card are
/// equivalent to pick, so one entry per distinct (name, detail) is enough - the id still
/// points at the first real entry so execution picks the right one.
/// </summary>
public static class RoomOptions
{
    public static RoomOptionView[] MergeDuplicates(RoomOptionView[] options)
    {
        if (options.Length < 2) return options;
        var first = new Dictionary<string, RoomOptionView>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var option in options)
        {
            var key = option.Name + "\u0000" + option.Detail;
            if (!first.ContainsKey(key))
            {
                first[key] = option;
                counts[key] = 1;
                order.Add(key);
            }
            else counts[key]++;
        }
        return order.Select(key => counts[key] == 1
            ? first[key]
            : first[key] with { Detail = $"{first[key].Detail}（共 {counts[key]} 张）" }).ToArray();
    }
}
