using System.Text;

namespace SpireCompanion.Core;

/// <summary>Human-readable "what is on screen right now", placed before the JSON so the model cannot miss it.</summary>
public static class SnapshotBrief
{
    public static string Describe(GameSnapshot snapshot)
    {
        var text = new StringBuilder();
        text.Append($"场景：{snapshot.Scene}；角色：{snapshot.Character}；A{snapshot.Ascension} 第 {snapshot.Floor} 层；HP {snapshot.Hp}/{snapshot.MaxHp}；金币 {snapshot.Gold}。");
        if (snapshot.Room is { } room)
        {
            text.Append($"当前界面：{room.Kind}「{room.Title}」");
            if (room.Options.Length > 0)
            {
                text.Append("；选项：");
                foreach (var option in room.Options.Take(8))
                    text.Append($"「{option.Name}」({Trim(option.Detail, 40)})").Append(option.Locked ? "[不可用]" : "").Append(' ');
            }
        }
        if (snapshot.Hand.Length > 0) text.Append($"；手牌：{string.Join("、", snapshot.Hand)}");
        if (snapshot.Cards.Length > 0)
            text.Append($";手牌效果：{string.Join(" | ", snapshot.Cards.Take(8).Select(card => $"{card.Name}：{Trim(card.Text, 60)}"))}");
        if (snapshot.Enemies.Length > 0)
            text.Append($"；敌人：{string.Join("、", snapshot.Enemies.Select(enemy => $"{enemy.Name} {enemy.Hp}/{enemy.MaxHp}" + (enemy.Intent.Length > 0 ? $"[意图:{enemy.Intent}]" : "")))}");
        if (snapshot.Deck.Length > 0)
            text.Append($"；牌组 {snapshot.Deck.Length} 张（{string.Join("、", snapshot.Deck.Take(12).Select(card => card.Name))}）");
        return text.ToString();
    }

    private static string Trim(string value, int limit) => value.Length <= limit ? value : value[..limit] + "…";
}
