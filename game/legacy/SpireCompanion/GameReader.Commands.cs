using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using SpireCompanion.Core;

namespace SpireCompanion;

internal static partial class GameReader
{
    public static bool CanExecuteCommand()
    {
        var manager = RunManager.Instance;
        return manager.IsInProgress && manager.DebugOnlyGetState()?.Players.Count == 1 &&
            !CombatManager.Instance.IsInProgress && manager.NetService?.IsGameLoading != true &&
            !IsGameOver() && !SolverBridge.Owned;
    }

    // Supplement only chat's snapshot; do not change the existing automatic decision policy.
    public static GameSnapshot ReadCommandSnapshot()
    {
        var snapshot = Read();
        if (snapshot.Room is not { } room) return snapshot;
        var options = room.Options.ToList();
        if (room.Kind == "Reward" && CanSkipReward()) options.Add(new("skip", "跳过卡牌奖励", "不拿牌，继续", false));
        if (room.Kind == "Select" && NOverlayStack.Instance?.Peek() is NCardGridSelectionScreen screen)
        {
            var selected = SelectedCards(screen);
            options = SortedGridHolders(screen).Select((holder, index) =>
                new RoomOptionView(index.ToString(), Text(holder.CardModel?.Title),
                    (holder.CardModel is { } card ? CardText(card) : "") +
                    (holder.CardModel != null && selected?.Contains(holder.CardModel) == true ? "（已选中）" : ""),
                    holder.CardModel == null)).ToList();
            if (CanConfirmSelection()) options.Add(new("confirm", "确认当前选择", "确认界面里已经选中的牌", false));
        }
        return snapshot with { Room = room with { Options = options.ToArray() } };
    }

    private static HashSet<CardModel>? SelectedCards(NCardGridSelectionScreen screen)
    {
        for (var type = screen.GetType(); type != null; type = type.BaseType)
            if (type.GetField("_selectedCards", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                ?.GetValue(screen) is IEnumerable<CardModel> cards) return cards.ToHashSet();
        return null;
    }

    public static ActionOutcome ExecuteCommand(RoomCommand command, Action<string> record, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var denied = command.ValidateCurrent(ReadCommandSnapshot(), CanExecuteCommand());
        if (denied != null) return new(false, denied);
        var room = command.Snapshot.Room!;
        var names = string.Join("、", command.Ids.Select(id => room.Options.First(option => option.Id == id).Name));
        var receipt = $"已提交「{names}」的操作，等待游戏结算。";
        var id = command.Ids[0];
        if (room.Kind == "Select" && id != "confirm")
        {
            var screen = (NCardGridSelectionScreen)NOverlayStack.Instance!.Peek()!;
            var selected = SelectedCards(screen);
            var holders = SortedGridHolders(screen);
            var targets = command.Ids.Select(value => holders[int.Parse(value)].CardModel!).ToHashSet();
            if (selected == null) return new(false, "无法确认当前选牌状态，请在游戏内手动选择。");
            if (selected.Except(targets).Any()) return new(false, "界面已有其他牌被选中，请先清空或确认，避免替你提交错误选择。");
            record($"已开始选择「{names}」；取消回复不会撤销已提交的点击。");
            foreach (var value in command.Ids)
            {
                token.ThrowIfCancellationRequested();
                var index = int.Parse(value);
                if (!selected.Contains(holders[index].CardModel!)) SelectGridCard(index);
            }
            return new(true, $"已选中「{names}」，正在确认。" );
        }
        // Reserve before native dispatch. An exception may occur after a click has reached the game.
        record($"已尝试提交「{names}」；请以游戏画面为准，不会自动重试。" );
        switch (room.Kind)
        {
            case "Reward" when id == "skip": SkipReward(); break;
            case "Reward": SelectReward(int.Parse(id)); break;
            case "Rest" or "Shop" or "Rewards" or "Treasure" when id == "proceed": Proceed(); break;
            case "Rest": SelectRestOption(id); break;
            case "Shop" when id == "open": OpenShop(); break;
            case "Shop" when id == "close": CloseShop(); break;
            case "Shop": Purchase(id); break;
            case "Event": SelectEventOption(int.Parse(id)); break;
            case "Map": ChooseMapNode(int.Parse(id)); break;
            case "Relic" when id == "skip": SkipRelic(); break;
            case "Relic": SelectRelic(int.Parse(id)); break;
            case "Rewards": ClaimReward(int.Parse(id)); break;
            case "Treasure" when id == "open": OpenTreasure(); break;
            case "Treasure": ClaimTreasureRelic(int.Parse(id)); break;
            case "Select" when id == "confirm": ConfirmSelection(); break;
            default: return new(false, "当前选项尚不支持执行。" );
        }
        record(receipt);
        return new(true, receipt);
    }

    public static string ConfirmCommandSelection(RoomCommand command, HashSet<ulong> clickedButtons)
    {
        if (!CanExecuteCommand()) return "游戏状态变化，已停止后续确认；已提交的选择请以画面为准。";
        if (NOverlayStack.Instance?.Peek() is not NCardGridSelectionScreen screen || ScreenId(screen) != command.Snapshot.Room!.Id)
            return "选牌界面已更新，本条指令结束。";
        var selected = SelectedCards(screen);
        var holders = SortedGridHolders(screen);
        var targets = command.Ids.Select(id => int.Parse(id)).ToArray();
        if (selected == null || targets.Any(index => index >= holders.Count) ||
            !selected.SetEquals(targets.Select(index => holders[index].CardModel!)))
            return "当前选中的牌发生变化，未自动确认，请在游戏内检查。";
        var confirm = FindAll<NConfirmButton>(screen).FirstOrDefault(button => button.IsEnabled && button.IsVisibleInTree());
        if (confirm != null && clickedButtons.Count < 2 && clickedButtons.Add(confirm.GetInstanceId())) confirm.ForceClick();
        return "";
    }
}
