using SpireCompanion.Core;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpireCompanion;

internal static class GameReader
{
    private static readonly string[] ConflictingControllers = ["SpireBuddy", "STS2AIAgent", "CombatSolver"];

    public static GameSnapshot Read()
    {
        var manager = RunManager.Instance;
        var run = manager.IsInProgress ? manager.DebugOnlyGetState() : null;
        if (run == null) return new("主菜单或无跑局", "无", 0, 0, 0, [], "无", false, "尚未进入游戏");
        var player = LocalContext.GetMe(run);
        if (player == null) return new("状态未就绪", "未知", 0, 0, 0, [], "无", false, "没有本地角色");
        var combat = CombatManager.Instance;
        var state = combat.IsInProgress ? combat.DebugOnlyGetState() : null;
        var handUi = NCombatRoom.Instance?.Ui?.Hand;
        var conflict = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name).FirstOrDefault(n => ConflictingControllers.Contains(n));
        var reason = run.Players.Count != 1 ? "当前仅支持单人" :
            !player.Character.Id.Entry.Contains("IRONCLAD", StringComparison.OrdinalIgnoreCase) ? "当前仅支持铁甲战士" :
            conflict != null ? $"检测到 {conflict}，本阶段仅只读，避免双重控制" :
            manager.NetService?.IsGameLoading == true ? "游戏加载中" :
            state == null ? "当前不在战斗中" :
            state.CurrentSide != CombatSide.Player || player.PlayerCombatState?.Phase != PlayerTurnPhase.Play ? "不是玩家可操作阶段" :
            combat.PlayerActionsDisabled ? "玩家动作已禁用" :
            manager.ActionExecutor.IsRunning || !manager.ActionQueueSet.IsEmpty ? "动作正在结算" :
            NOverlayStack.Instance?.Peek() != null ? "请先关闭覆盖界面或完成选牌" :
            handUi == null || handUi.InCardPlay || handUi.CurrentMode != NPlayerHand.Mode.Play ? "手牌尚未就绪" : "";
        return new(run.CurrentRoom?.GetType().Name ?? "未知场景", Text(player.Character.Title),
            player.Creature.CurrentHp, player.Creature.MaxHp, player.Gold,
            state != null ? player.PlayerCombatState?.Hand.Cards.Select(c => Text(c.Title)).ToArray() ?? [] : [],
            state != null ? $"{state.RoundNumber}:{state.CurrentSide}:{player.PlayerCombatState?.Phase}" : "无",
            reason.Length == 0, reason,
            $"{RuntimeHelpers.GetHashCode(run)}:{(state != null ? RuntimeHelpers.GetHashCode(state) : 0)}:" +
            $"{player.Creature.Block}:{(state != null ? player.PlayerCombatState?.Energy : 0)}:" +
            string.Join(";", state?.Enemies.Select(e => $"{RuntimeHelpers.GetHashCode(e)}:{e.CurrentHp}:{e.Block}") ?? []))
        {
            Energy = state != null ? player.PlayerCombatState?.Energy : null,
            Block = player.Creature.Block,
            Cards = state != null ? player.PlayerCombatState?.Hand.Cards.Select((c, i) => Card(c, i, true)).ToArray() ?? [] : [],
            Deck = player.Deck.Cards.Select((c, i) => Card(c, i, false)).ToArray(),
            Relics = player.Relics.Select((r, i) => new ItemView(i, r.Id.Entry, Text(r.Title))).ToArray(),
            Potions = player.PotionSlots.Select((p, i) => p == null ? null : new ItemView(i, p.Id.Entry, Text(p.Title)))
                .OfType<ItemView>().ToArray(),
            Room = ReadRoom(run),
            Enemies = state?.Enemies.Select((e, i) => new EnemyView(i, e.Monster?.Id.Entry ?? "unknown",
                e.Monster != null ? Text(e.Monster.Title) : "未知敌人", e.CurrentHp, e.MaxHp, e.Block)).ToArray() ?? []
        };
    }

    public static void EndTurn()
    {
        // Read again immediately before mutation; never retain a live Player in background work.
        var snapshot = Read();
        if (!snapshot.CanEndTurn) throw new InvalidOperationException(snapshot.BlockReason);
        var run = RunManager.Instance.DebugOnlyGetState()!;
        Player player = LocalContext.GetMe(run) ?? throw new InvalidOperationException("本地角色已失效");
        PlayerCmd.EndTurn(player, canBackOut: false);
    }

    /// <summary>
    /// Out-of-combat screens. Event, shop and rest site expose plain model lists, so they can be
    /// read without touching UI text; the card reward and map are the two exceptions noted below.
    /// </summary>
    private static RoomView? ReadRoom(RunState run)
    {
        if (NOverlayStack.Instance?.Peek() is NCardRewardSelectionScreen rewardScreen)
        {
            var cards = CardHolders(rewardScreen).Select(holder => holder.CardModel).OfType<CardModel>().ToArray();
            if (cards.Length > 0)
                return new RoomView("Reward", "卡牌奖励", "选择一张加入牌组，或跳过",
                    cards.Select((card, index) => new RoomOptionView(index.ToString(), Text(card.Title),
                        $"费用 {(card.EnergyCost.CostsX ? "X" : card.EnergyCost.GetAmountToSpend().ToString())}" +
                        (card.IsUpgraded ? " · 已升级" : ""), false)).ToArray());
        }
        switch (run.CurrentRoom)
        {
            case EventRoom eventRoom:
                EventModel? model = null;
                try { model = eventRoom.LocalMutableEvent; }
                catch (Exception error) { Godot.GD.PushWarning($"[SpireCompanion] event_state: {error.Message}"); }
                model ??= eventRoom.CanonicalEvent;
                if (model == null) return new RoomView("Event", "事件（内容未就绪）", "", []);
                return new RoomView("Event", Text(model.Title), Text(model.Description),
                    model.CurrentOptions?.Select((option, index) => new RoomOptionView(
                        index.ToString(), Text(option.Title), Text(option.Description), option.IsLocked)).ToArray() ?? []);
            case MerchantRoom shop:
                var inventory = shop.GetLocalInventory();
                return new RoomView("Shop", "商店", "金币有限，先买最缺的",
                    inventory?.AllEntries.Select((entry, index) =>
                    {
                        var (name, detail) = Entry(entry);
                        return new RoomOptionView(index.ToString(), name, detail, !entry.IsStocked || !entry.EnoughGold);
                    }).ToArray() ?? []);
            case RestSiteRoom rest:
                return new RoomView("Rest", "休息点", "选择休息或升级",
                    rest.Options.Select((option, index) => new RoomOptionView(
                        string.IsNullOrEmpty(option.OptionId) ? index.ToString() : option.OptionId,
                        Text(option.Title), Text(option.Description), !option.IsEnabled)).ToArray());
            case MapRoom:
                // Next reachable nodes are exactly the current point's children: no hidden topology.
                var point = run.CurrentMapPoint;
                return new RoomView("Map", "地图", "选择下一层路线",
                    point?.Children.OrderBy(child => child.PointType.ToString())
                        .Select((child, index) => new RoomOptionView(index.ToString(), PointName(child.PointType), "可达节点", false))
                        .ToArray() ?? []);
            default:
                return null;
        }
    }

    private static (string Name, string Detail) Entry(MerchantEntry entry) => entry switch
    {
        MerchantCardEntry card when card.CreationResult?.Card is { } model =>
            (Text(model.Title), $"{card.Cost} 金币{(card.IsOnSale ? " · 打折" : "")}{(card.IsStocked ? "" : " · 已售出")}"),
        MerchantRelicEntry relic when relic.Model is { } model =>
            (Text(model.Title), $"{relic.Cost} 金币{(relic.IsStocked ? "" : " · 已售出")}"),
        MerchantPotionEntry potion when potion.Model is { } model =>
            (Text(model.Title), $"{potion.Cost} 金币{(potion.IsStocked ? "" : " · 已售出")}"),
        MerchantCardRemovalEntry removal => ("删牌服务", $"{removal.Cost} 金币{(removal.Used ? " · 已使用" : "")}"),
        _ => ("商品", $"{entry.Cost} 金币")
    };

    private static string PointName(MapPointType type) => type switch
    {
        MapPointType.Monster => "小怪",
        MapPointType.Elite => "精英",
        MapPointType.Boss => "Boss",
        MapPointType.Shop => "商店",
        MapPointType.RestSite => "休息点",
        MapPointType.Treasure => "宝箱",
        MapPointType.Ancient => "远古",
        _ => "未知"
    };

    // The card reward screen exposes cards only through its holders; there is no model list to read.
    private static IEnumerable<NCardHolder> CardHolders(Godot.Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is NCardHolder holder) yield return holder;
            foreach (var nested in CardHolders(child)) yield return nested;
        }
    }

    private static CardView Card(CardModel card, int index, bool inHand) => new(index, card.Id.Entry, Text(card.Title),
        card.EnergyCost.CostsX ? "X" : inHand ? card.EnergyCost.GetAmountToSpend().ToString() : "未在手牌，不计算动态费用", card.IsUpgraded);

    private static string Text(object value) => value is LocString loc ? loc.GetFormattedText() : value.ToString() ?? "未知";
}
