using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Nodes.TreasureRooms;
using SpireCompanion.Core;

namespace SpireCompanion;

internal static partial class GameReader
{
    private sealed record UiTarget(Control Control, string Name, string Detail, bool Locked);

    private static IEnumerable<Node> UiNodes(Node node)
    {
        if (node.HasMeta("spire_companion_ui")) yield break;
        if (node is CanvasItem canvas && !canvas.IsVisibleInTree()) yield break;
        yield return node;
        foreach (var child in node.GetChildren())
            foreach (var descendant in UiNodes(child)) yield return descendant;
    }

    // A modal/submenu owns input. No active context means a transition, not permission to scan behind it.
    private static Node? UiScope() => ActiveScreenContext.Instance.GetCurrentScreen() as Node;

    private static bool NativeRoomVisible(RoomView room, Node? scope) => room.Kind switch
    {
        "Reward" => scope is NCardRewardSelectionScreen && ScreenId(scope) == room.Id,
        "Rewards" => scope is NRewardsScreen && ScreenId(scope) == room.Id,
        "Select" => scope is NCardGridSelectionScreen && ScreenId(scope) == room.Id,
        "Relic" => scope is NChooseARelicSelection && ScreenId(scope) == room.Id,
        "Map" => scope is NMapScreen or NMapRoom,
        "Shop" => scope is NMerchantRoom or NMerchantInventory,
        "Rest" => scope is NRestSiteRoom,
        "Event" => scope is NEventRoom,
        "Treasure" => scope is NTreasureRoom or NTreasureRoomRelicCollection,
        _ => false
    };

    private static string UiName(Control control)
    {
        if (control is NCharacterSelectButton character) return Text(character.Character.Title);
        if (control is NCardHolder card && card.CardModel != null) return Text(card.CardModel.Title);
        if (control is NTopBarMapButton) return "地图";
        if (control is NTopBarDeckButton) return "牌组";
        if (control is NTopBarPauseButton) return "暂停菜单";
        if (control is Button button && !string.IsNullOrWhiteSpace(button.Text)) return button.Text.Trim();
        var labels = UiNodes(control).Select(node => node switch
        { Label label => label.Text, RichTextLabel rich => rich.GetParsedText(), _ => "" })
            .Where(value => !string.IsNullOrWhiteSpace(value)).Take(3).ToArray();
        if (labels.Length > 0) return Shorten(StripTags(string.Join(" · ", labels)), 100);
        if (!string.IsNullOrWhiteSpace(control.TooltipText)) return Shorten(control.TooltipText, 100);
        if (control.Name.ToString().Contains("BackButton")) return "返回";
        if (control.Name == "ConfirmButton") return control.GetParent()?.GetType().Name == "NCharacterSelectScreen" ? "开始游戏" : "确认";
        return control.GetParent()?.Name + "/" + control.Name;
    }

    private static Dictionary<string, UiTarget> UiTargets()
    {
        var result = new Dictionary<string, UiTarget>();
        var scope = UiScope();
        if (scope == null) return result;
        IEnumerable<Node> nodes = UiNodes(scope);
        // The top bar is a sibling of room content, but is not actionable underneath a modal.
        if (scope is NCombatRoom or NRestSiteRoom or NEventRoom or NMerchantRoom or NTreasureRoom or NMapRoom)
            nodes = nodes.Concat(FindAll<NTopBar>(((SceneTree)Engine.GetMainLoop()).Root).SelectMany(UiNodes));
        foreach (var control in nodes.OfType<Control>().Distinct())
        {
            if (control.GetGlobalRect().Size.X <= 1 || control.GetGlobalRect().Size.Y <= 1) continue;
            bool supported = control is NClickableControl or BaseButton or NCardHolder ||
                control.MouseFilter != Control.MouseFilterEnum.Ignore && control.GetSignalConnectionList(Control.SignalName.GuiInput).Count > 0;
            if (!supported) continue;
            // Labels decorating a native button are not separate click targets.
            if (control is not (NClickableControl or BaseButton or NCardHolder) && control.GetParent() is NClickableControl or BaseButton or NCardHolder) continue;
            bool locked = control switch { NClickableControl native => !native.IsEnabled, BaseButton button => button.Disabled, _ => false };
            result.Add("ui:" + control.GetInstanceId(), new(control, UiName(control),
                $"{control.GetType().Name} · {control.Name}", locked));
        }
        return result;
    }

    private static GameSnapshot AddUiTargets(GameSnapshot snapshot)
    {
        var targets = UiTargets();
        var ui = targets.Select(pair => new RoomOptionView(pair.Key, pair.Value.Name, pair.Value.Detail, pair.Value.Locked));
        var scope = UiScope();
        var room = snapshot.Room;
        // Native room choices remain available in their own screens; menus and combat use observed UI controls.
        if (room == null || snapshot.CombatTurn != "无" || !NativeRoomVisible(room, scope))
            room = new("Ui", scope?.GetType().Name ?? "界面切换中", "当前游戏界面可点击控件；每条指令只点击一个目标", []) { Id = scope?.GetInstanceId().ToString() ?? "" };
        return snapshot with { Room = room with { Options = [.. room.Options, .. ui] } };
    }

    private static ActionOutcome ClickUi(string id, Action<string> record)
    {
        if (!UiTargets().TryGetValue(id, out var target) || target.Locked) return new(false, "控件已隐藏或禁用，本次未点击。");
        record($"已尝试点击「{target.Name}」；结果尚未确认，不会自动重试。");
        switch (target.Control)
        {
            case NCardHolder holder: holder.EmitSignal(NCardHolder.SignalName.Pressed, holder); break;
            case NClickableControl native: native.ForceClick(); break;
            case BaseButton button:
                if (button.ToggleMode) button.ButtonPressed = !button.ButtonPressed;
                button.EmitSignal(BaseButton.SignalName.Pressed);
                break;
            default:
                // Deliver a click to the observed game control, never to OS coordinates or the companion overlay.
                var local = target.Control.Size / 2;
                foreach (var pressed in new[] { true, false })
                {
                    var input = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed,
                        Position = local, GlobalPosition = target.Control.GetGlobalRect().GetCenter(),
                        ButtonMask = pressed ? MouseButtonMask.Left : 0 };
                    target.Control.EmitSignal(Control.SignalName.GuiInput, input);
                }
                break;
        }
        var receipt = $"已提交点击「{target.Name}」，正在检查界面。";
        record(receipt);
        return new(true, receipt);
    }
}
