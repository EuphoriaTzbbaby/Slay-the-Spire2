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

    private static CardView Card(CardModel card, int index, bool inHand) => new(index, card.Id.Entry, Text(card.Title),
        card.EnergyCost.CostsX ? "X" : inHand ? card.EnergyCost.GetAmountToSpend().ToString() : "未在手牌，不计算动态费用", card.IsUpgraded);

    private static string Text(object value) => value is LocString loc ? loc.GetFormattedText() : value.ToString() ?? "未知";
}
