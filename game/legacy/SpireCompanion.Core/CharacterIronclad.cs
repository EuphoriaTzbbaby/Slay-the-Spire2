namespace SpireCompanion.Core;

public sealed class IroncladProfile : ICharacterProfile
{
    public bool Matches(string character) =>
        character.Contains("铁甲", StringComparison.OrdinalIgnoreCase) ||
        character.Contains("IRONCLAD", StringComparison.OrdinalIgnoreCase);

    public string Guidance => "角色要点（铁甲战士）：靠力量与易伤放大伤害，自伤和消耗换节奏；" +
        "选牌优先能叠力量、提供易伤或高质量格挡的牌，牌组超过 28 张时少拿普通攻击；" +
        "商店优先删打击，力量核心牌值得买；战斗中有力量时优先多段攻击，易伤窗口内集中输出。";
}
