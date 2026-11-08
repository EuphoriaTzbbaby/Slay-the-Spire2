using System.Text.RegularExpressions;

namespace SpireCompanion.Core;

/// <summary>Model selects an observed id; displayed option names and effects come only from the captured game state.</summary>
public static class CurrentChoiceAdvice
{
    public static bool Applies(GameSnapshot snapshot, string question)
    {
        if (snapshot.Room?.Kind is not ("Event" or "Reward" or "Rest" or "Shop" or "Select" or "Relic" or "Rewards" or "Treasure" or "Map")) return false;
        if (Regex.IsMatch(question.Trim(), "^(?:你好|您好|谢谢|晚安|再见)[！!。，,\\s]*$|翻译|复述|原样|说一句|我爱你|爱我|年龄|几岁|你是谁|介绍自己")) return false;
        // These screens already establish a choice context. ASR omissions and short follow-ups
        // must not be allowed back into free-form card names/effects.
        return snapshot.Room.Kind is "Reward" or "Select" or "Relic" ||
            Regex.IsMatch(question, "选|推荐|建议|比较|哪个好|分析|效果|当前|屏幕|局面|页面|抓|答案|那一个|哪一个");
    }

    public static bool WantsBrief(string question) => Regex.IsMatch(question, "答案|简短|简洁|一句|结论|别解释|只说|直接说");

    public static RoomOptionView[] Options(GameSnapshot snapshot) => snapshot.Room?.Options
        .Where(option => !option.Id.StartsWith("ui:", StringComparison.Ordinal)).ToArray() ?? [];

    public static string Render(RoomOptionView[] options, string decision, bool brief = false)
    {
        var (id, _, _) = ChoiceParser.ParseWithIds(decision, options.Where(option => !option.Locked).Select(option => option.Id).ToArray());
        var selected = options.FirstOrDefault(option => option.Id == id && !option.Locked);
        if (brief && selected != null) return $"建议选「{GameText.Plain(selected.Name)}」。";
        var facts = "当前界面的实际选项：\n" + string.Join("\n", options.Select((option, index) =>
            $"{index + 1}. 「{GameText.Plain(option.Name)}」：{GameText.Plain(option.Detail)}" + (option.Locked ? "（当前不可选）" : "")));
        // Never display the model's free-text names/effects, even if its id was valid.
        return facts + (selected == null ? "\n本次推荐未通过选项校验，未给出选择，也未执行操作。"
            : $"\n结合本次状态，我建议选「{GameText.Plain(selected.Name)}」。以上效果来自游戏状态；这只是建议，尚未点击。");
    }
}
