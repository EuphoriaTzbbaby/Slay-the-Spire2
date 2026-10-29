namespace SpireCompanion.Core;

public sealed class DefectProfile : ICharacterProfile
{
    public bool Matches(string character) =>
        character.Contains("故障", StringComparison.OrdinalIgnoreCase) ||
        character.Contains("DEFECT", StringComparison.OrdinalIgnoreCase);

    public string Guidance => "角色要点（故障机器人）：靠充能球（闪电/冰霜/黑暗）与集中叠输出/防御；" +
        "选牌优先充能、集中与球位相关牌，缺防御时拿冰霜球；" +
        "商店优先删打击，集中类遗物优先；战斗注意球位上限与激发时机，AOE 靠闪电球循环。";
}
