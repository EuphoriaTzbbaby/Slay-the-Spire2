namespace SpireCompanion.Core;

public sealed class SilentProfile : ICharacterProfile
{
    public bool Matches(string character) =>
        character.Contains("静默", StringComparison.OrdinalIgnoreCase) ||
        character.Contains("SILENT", StringComparison.OrdinalIgnoreCase);

    public string Guidance => "角色要点（静默猎手）：靠中毒、小刀与抽牌打持续伤害，防御偏闪避与虚弱；" +
        "选牌优先中毒叠加、抽牌与低费小刀组件，注意牌组别太薄；" +
        "商店优先删防御或打击，毒核心与抽牌遗物优先；战斗中毒要先铺再拖，小刀流注意每回合出牌数。";
}
