using System.Text.RegularExpressions;

namespace SpireCompanion.Core;

/// <summary>Model selects an observed id; displayed event names and effects come only from the captured game state.</summary>
public static class CurrentEventAdvice
{
    public static bool Applies(GameSnapshot snapshot, string question) => snapshot.Room?.Kind == "Event" &&
        Regex.IsMatch(question, "选|推荐|建议|比较|哪个好|分析|效果|当前|屏幕|局面");

    public static RoomOptionView[] Options(GameSnapshot snapshot) => snapshot.Room?.Options
        .Where(option => !option.Id.StartsWith("ui:", StringComparison.Ordinal)).ToArray() ?? [];

    public static string Render(RoomOptionView[] options, string decision)
    {
        var (id, _, _) = ChoiceParser.ParseWithIds(decision, options.Where(option => !option.Locked).Select(option => option.Id).ToArray());
        var selected = options.FirstOrDefault(option => option.Id == id && !option.Locked);
        var facts = "当前事件的实际选项：\n" + string.Join("\n", options.Select((option, index) =>
            $"{index + 1}. 「{option.Name}」：{option.Detail}" + (option.Locked ? "（当前不可选）" : "")));
        // Never display the model's free-text names/effects, even if its id was valid.
        return facts + (selected == null ? "\n本次推荐未通过选项校验，未给出选择，也未执行操作。"
            : $"\n结合本次状态，我建议选「{selected.Name}」。以上效果来自游戏状态；这只是建议，尚未点击。");
    }
}
