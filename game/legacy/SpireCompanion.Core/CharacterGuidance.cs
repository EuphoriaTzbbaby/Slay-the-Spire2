namespace SpireCompanion.Core;

/// <summary>Per-character mechanics and drafting priorities injected into chat and decisions.</summary>
public static class CharacterGuidance
{
    public static string For(string character)
    {
        var name = character ?? "";
        if (Contains(name, "铁甲", "IRONCLAD"))
            return "角色要点（铁甲战士）：靠力量与易伤放大伤害，自伤和消耗换节奏；" +
                "选牌优先能叠力量、提供易伤或高质量格挡的牌，牌组超过 28 张时少拿普通攻击；" +
                "商店优先删打击，力量核心牌值得买；战斗中有力量时优先多段攻击，易伤窗口内集中输出。";
        if (Contains(name, "静默", "SILENT"))
            return "角色要点（静默猎手）：靠中毒、小刀与抽牌打持续伤害，防御偏闪避与虚弱；" +
                "选牌优先中毒叠加、抽牌与低费小刀组件，注意牌组别太薄；" +
                "商店优先删防御或打击，毒核心与抽牌遗物优先；战斗中毒要先铺再拖，小刀流注意每回合出牌数。";
        if (Contains(name, "故障", "DEFECT"))
            return "角色要点（故障机器人）：靠充能球（闪电/冰霜/黑暗）与集中叠输出/防御；" +
                "选牌优先充能、集中与球位相关牌，缺防御时拿冰霜球；" +
                "商店优先删打击，集中类遗物优先；战斗注意球位上限与激发时机，AOE 靠闪电球循环。";
        return "";
    }

    private static bool Contains(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
