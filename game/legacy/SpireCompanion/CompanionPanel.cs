using Godot;
using System.Collections.Concurrent;
using System.Text.Json;
using SpireCompanion.Core;
using MegaCrit.Sts2.Core.Localization.Fonts;

namespace SpireCompanion;

internal sealed class CompanionPanel
{
    private readonly ConcurrentQueue<Action> mainQueue = new();
    private ChatPanel chat = null!;
    private PanelContainer panel = null!;
    private Label status = null!;
    private Label stateLabel = null!;
    private readonly SceneTree tree;
    private ScrollContainer chatScroll = null!;
    private ScrollContainer settingsScroll = null!;
    private ScrollContainer gameScroll = null!;
    private bool dragging;
    private Vector2 dragOffset;
    private Button pause = null!;
    private Button autoToggle = null!;
    private bool autoEnabled;
    private CancellationTokenSource? autoCts;
    private string autoPending = "";
    private string autoDone = "";
    private double autoWait;
    private string autoArmedKind = "";
    private bool autoSelectPendingConfirm;
    private string windowStatePath = "";
    private Label autoLabel = null!;
    private PanelContainer decisionCard = null!;
    private VBoxContainer decisionButtons = null!;
    private Label decisionStatus = null!;
    private string decisionSignature = "";
    private Button toggle = null!;
    private Button solverStart = null!;
    private bool reportedReady;
    private bool layoutReported;
    private int ticks;
    private TabContainer tabs = null!;
    private double remaining;
    private bool f9Down;
    private string message = "只读建议：不会自动出牌，自动执行交给求解器。";

    public CompanionPanel(SceneTree tree)
    {
        this.tree = tree;
        var layer = new CanvasLayer { Layer = 90 };
        panel = new PanelContainer { Position = new Vector2(24, 70) };
        // Use game fonts, never depend on operating-system font enumeration.
        panel.Theme = new Theme();
        UiStyle.Apply(panel.Theme);
        var windowStyle = UiStyle.Box(UiStyle.Background, UiStyle.Border, 20, 16);
        windowStyle.ShadowColor = new Color(0.42f, 0.26f, 0.04f, 0.18f);
        windowStyle.ShadowSize = 10;
        panel.AddThemeStyleboxOverride("panel", windowStyle);

        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 10);
        panel.AddChild(layout);
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        layout.AddChild(header);
        var title = new Label
        {
            Text = "尖塔 AI 陪玩", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Stop
        };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.GuiInput += input =>
        {
            if (input is InputEventMouseButton mouse && mouse.ButtonIndex == MouseButton.Left && mouse.Pressed)
            { dragging = true; dragOffset = title.GetGlobalMousePosition() - panel.Position; }
        };
        header.AddChild(title);
        if (UiStyle.Avatar is { } face)
            header.AddChild(new TextureRect
            {
                Texture = face, CustomMinimumSize = new Vector2(44, 44),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
            });
        stateLabel = new Label { Text = "就绪", VerticalAlignment = VerticalAlignment.Center };
        stateLabel.AddThemeColorOverride("font_color", UiStyle.Good);
        header.AddChild(stateLabel);
        autoLabel = new Label { Text = "自动：关", VerticalAlignment = VerticalAlignment.Center };
        autoLabel.AddThemeColorOverride("font_color", UiStyle.Muted);
        header.AddChild(autoLabel);
        var close = new Button { Text = "关闭" }; close.Pressed += () => panel.Hide();
        UiStyle.Size(close, 40, 84); header.AddChild(close);

        tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; layout.AddChild(tabs);
        chatScroll = Page("聊天", out var chatBody);
        settingsScroll = Page("设置", out var settingsBody);
        gameScroll = Page("游戏状态", out var body);
        UiStyle.StyleTabs(tabs);

        // Executable choices go first: they are the only thing the player must act on.
        var decisionBody = UiStyle.Card(body, "可以执行", out decisionCard);
        decisionCard.Visible = false;
        decisionBody.AddChild(UiStyle.Hint("只执行你点下的选择，不会自动选或自动购买。"));
        decisionStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        decisionStatus.AddThemeColorOverride("font_color", UiStyle.AccentDeep);
        decisionBody.AddChild(decisionStatus);
        decisionButtons = new VBoxContainer();
        decisionButtons.AddThemeConstantOverride("separation", 6);
        decisionBody.AddChild(decisionButtons);

        var statusCard = UiStyle.Card(body, "当前局面");
        status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(0, 80) };
        statusCard.AddChild(status);
        var controlCard = UiStyle.Card(body, "托管与求解器");
        windowStatePath = Path.Combine(ProjectSettings.GlobalizePath("user://spire-companion"), "window-state.json");
        autoEnabled = WindowState.Load(windowStatePath).AutoEnabled;
        autoToggle = new Button { Text = autoEnabled ? "局外自动操作：开" : "局外自动操作：关" };
        autoToggle.Pressed += ToggleAuto;
        UiStyle.Secondary(autoToggle); UiStyle.Size(autoToggle, 44);
        controlCard.AddChild(autoToggle);
        controlCard.AddChild(UiStyle.Hint("开启后由模型在奖励/休息点/商店/选牌界面里选一项并执行，每个界面只操作一次（选牌会自动确认）；战斗仍归求解器。"));
        solverStart = new Button { Text = "交给 CombatSolver 打本场战斗" };
        solverStart.Pressed += StartSolver;
        UiStyle.Primary(solverStart); UiStyle.Size(solverStart, 44);
        controlCard.AddChild(solverStart);
        pause = new Button { Text = "暂停所有本模块动作" };
        pause.Pressed += PauseAll;
        UiStyle.Secondary(pause); UiStyle.Size(pause, 44);
        controlCard.AddChild(pause);
        controlCard.AddChild(UiStyle.Hint("本模块只给文字建议；战斗执行由求解器负责，不经过模型判断。"));

        var debugCard = UiStyle.Card(body, "调试");
        var export = new Button { Text = "导出当前状态" };
        export.Pressed += ExportSnapshot;
        UiStyle.Ghost(export); UiStyle.Size(export, 40);
        debugCard.AddChild(export);

        chat = new ChatPanel(chatBody, settingsBody, mainQueue.Enqueue);

        layer.AddChild(panel);
        toggle = new Button
        {
            Text = "AI 搭子", Icon = UiStyle.SmallAvatar, Position = new Vector2(16, 16),
            TooltipText = "显示或隐藏窗口（F9）", Theme = panel.Theme
        };
        UiStyle.Size(toggle, 44, 108);
        toggle.Pressed += () => panel.Visible = !panel.Visible;
        layer.AddChild(toggle);
        autoLabel.Text = autoEnabled ? "自动：开" : "自动：关";
        tree.Root.AddChild(layer);
        tree.ProcessFrame += () => Tick(tree.Root.GetProcessDeltaTime());
        tree.Root.TreeExiting += () =>
        {
            autoCts?.Cancel();
            chat.Dispose();
            try { SolverBridge.StopOwned(); }
            catch (Exception error) { GD.PushError($"[SpireCompanion] shutdown_stop: {error}"); }
        };
        Refresh();
    }

    private ScrollContainer Page(string name, out VBoxContainer body)
    {
        var page = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 8);
        page.AddChild(body);
        tabs.AddChild(page);
        return page;
    }

    private void Tick(double delta)
    {
        while (mainQueue.TryDequeue(out var update)) update();
        var viewport = tree.Root.GetVisibleRect().Size;
        var width = Math.Clamp(viewport.X - 40, 360, 620);
        var height = Math.Clamp(viewport.Y - 100, 300, 720);
        chatScroll.CustomMinimumSize = settingsScroll.CustomMinimumSize = gameScroll.CustomMinimumSize =
            new Vector2(width - 40, 140);
        panel.Size = new Vector2(width, height);
        if (dragging)
        {
            if (!Input.IsMouseButtonPressed(MouseButton.Left)) dragging = false;
            else panel.Position = tree.Root.GetMousePosition() - dragOffset;
        }
        panel.Position = panel.Position.Clamp(Vector2.Zero, (viewport - panel.Size).Max(Vector2.Zero));
        ticks++;
        if (!layoutReported && ticks > 5 && panel.IsVisibleInTree())
        {
            // Report the real laid-out geometry once so a broken window is diagnosable from the log alone.
            layoutReported = true;
            var titles = string.Join("|", Enumerable.Range(0, tabs.GetTabCount()).Select(tabs.GetTabTitle));
            var page = tabs.GetCurrentTabControl();
            GD.Print($"[SpireCompanion] PANEL_LAYOUT viewport=({viewport.X},{viewport.Y}) " +
                $"rect=({panel.Position.X},{panel.Position.Y},{panel.Size.X},{panel.Size.Y}) " +
                $"tabs={tabs.GetTabCount()} titles={titles} current={tabs.CurrentTab} " +
                $"scroll=({chatScroll.Size.X},{chatScroll.Size.Y}) page=({page?.Size.X},{page?.Size.Y}) " +
                $"font_ready={panel.Theme.DefaultFont != null} visible={panel.Visible}");
        }
        autoWait = Math.Max(0, autoWait - delta);
        bool pressed = Input.IsPhysicalKeyPressed(Key.F9);
        if (pressed && !f9Down) panel.Visible = !panel.Visible;
        f9Down = pressed;
        remaining -= delta;
        if (remaining <= 0) { remaining = 0.25; Refresh(); }
    }

    private void ExportSnapshot()
    {
        try
        {
            var fresh = GameReader.Read();
            var directory = ProjectSettings.GlobalizePath("user://spire-companion/captures");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"state-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { schema_version = 1, captured_at = DateTimeOffset.UtcNow,
                fingerprint = fresh.Fingerprint, state = fresh }, new JsonSerializerOptions { WriteIndented = true }));
            message = $"已导出到 {path}";
            GD.Print($"[SpireCompanion] snapshot_saved path={path}");
        }
        catch (Exception error)
        {
            message = "状态导出失败，详见日志。";
            GD.PushError($"[SpireCompanion] snapshot_export: {error}");
        }
        Refresh();
    }

    /// <summary>Offer executable choices only for screens we can actually act on.</summary>
    private void UpdateDecision(GameSnapshot next)
    {
        var room = next.Room;
        if (room is null || room.Options.Length == 0 || room.Kind is not ("Reward" or "Rest" or "Shop" or "Select"))
        {
            decisionCard.Visible = false;
            decisionSignature = "";
            return;
        }
        // Selection itself may not change the snapshot fingerprint, so include whether confirm is available.
        var canConfirm = room.Kind == "Select" && GameReader.CanConfirmSelection();
        var signature = next.Fingerprint + "|" + room.Kind + "|" + canConfirm + "|" +
            string.Join("|", room.Options.Select(option => option.Id + option.Name + option.Locked));
        if (signature != decisionSignature)
        {
            decisionSignature = signature;
            foreach (var child in decisionButtons.GetChildren()) child.Free();
            switch (room.Kind)
            {
                case "Reward": BuildRewardButtons(room); break;
                case "Rest": BuildRestButtons(room); break;
                case "Select": BuildSelectButtons(room); break;
                default: BuildShopButtons(room); break;
            }
            decisionStatus.Text = $"检测到 {room.Options.Length} 个可选项，点击后立即执行。";
            GD.Print($"[SpireCompanion] decision kind={room.Kind} options={room.Options.Length} " +
                $"names={string.Join("|", room.Options.Select(option => $"{option.Id}:{option.Name}:{(option.Locked ? "locked" : "open")}"))}");
        }
        decisionCard.Visible = true;
    }

    private void BuildRewardButtons(RoomView room)
    {
        foreach (var option in room.Options)
        {
            if (!int.TryParse(option.Id, out var index)) continue;
            var button = new Button { Text = $"选「{option.Name}」 · {option.Detail}" };
            button.Pressed += () => ExecuteReward(index);
            UiStyle.Secondary(button); UiStyle.Size(button, 42);
            decisionButtons.AddChild(button);
        }
        if (GameReader.CanSkipReward())
        {
            var skip = new Button { Text = "跳过这张奖励" };
            skip.Pressed += ExecuteSkipReward;
            UiStyle.Ghost(skip); UiStyle.Size(skip, 40);
            decisionButtons.AddChild(skip);
        }
    }

    private void BuildRestButtons(RoomView room)
    {
        foreach (var option in room.Options)
        {
            var button = new Button { Text = $"选「{option.Name}」 · {option.Detail}", Disabled = option.Locked };
            var id = option.Id;
            button.Pressed += () => ExecuteRest(id);
            UiStyle.Secondary(button); UiStyle.Size(button, 42);
            decisionButtons.AddChild(button);
        }
    }

    private void BuildSelectButtons(RoomView room)
    {
        foreach (var option in room.Options)
        {
            if (!int.TryParse(option.Id, out var index)) continue;
            var button = new Button { Text = $"选「{option.Name}」 · {option.Detail}" };
            button.Pressed += () => ExecuteGridCard(index);
            UiStyle.Secondary(button); UiStyle.Size(button, 42);
            decisionButtons.AddChild(button);
        }
        if (GameReader.CanConfirmSelection())
        {
            var confirm = new Button { Text = "确认选择" };
            confirm.Pressed += ExecuteConfirm;
            UiStyle.Primary(confirm); UiStyle.Size(confirm, 42);
            decisionButtons.AddChild(confirm);
        }
    }

    private void BuildShopButtons(RoomView room)
    {
        foreach (var option in room.Options)
        {
            var button = new Button { Text = $"买「{option.Name}」 · {option.Detail}", Disabled = option.Locked };
            var id = option.Id;
            button.Pressed += () => ExecutePurchase(id);
            UiStyle.Secondary(button); UiStyle.Size(button, 42);
            decisionButtons.AddChild(button);
        }
    }

    private void ExecuteRest(string optionId)
    {
        try { GameReader.SelectRestOption(optionId); message = "已选择该休息点选项。"; decisionStatus.Text = message; GD.Print($"[SpireCompanion] decision_result action=rest id={optionId} ok"); }
        catch (Exception error)
        {
            message = $"选择失败：{error.Message}";
            decisionStatus.Text = message;
            GD.PushError($"[SpireCompanion] rest_option: {error}");
        }
        decisionSignature = "";
        Refresh();
    }

    private void ExecutePurchase(string entryId)
    {
        try { GameReader.Purchase(entryId); message = "已购买该商品，等待结算。"; decisionStatus.Text = message; GD.Print($"[SpireCompanion] decision_result action=shop id={entryId} ok"); }
        catch (Exception error)
        {
            message = $"购买失败：{error.Message}";
            decisionStatus.Text = message;
            GD.PushError($"[SpireCompanion] purchase: {error}");
        }
        decisionSignature = "";
        Refresh();
    }

    private void ToggleAuto()
    {
        autoEnabled = !autoEnabled;
        autoToggle.Text = autoEnabled ? "局外自动操作：开" : "局外自动操作：关";
        message = autoEnabled
            ? "已开启局外自动操作：只在奖励、休息点、商店里替你点一次。"
            : "已关闭局外自动操作。";
        if (!autoEnabled) StopAuto();
        else { autoDone = ""; autoWait = 0; }
        autoLabel.Text = autoEnabled ? "自动：开" : "自动：关";
        new WindowState(autoEnabled).Save(windowStatePath);
        Refresh();
    }

    private void StopAuto()
    {
        autoEnabled = false;
        if (autoToggle != null) autoToggle.Text = "局外自动操作：关";
        if (autoLabel != null) autoLabel.Text = "自动：关";
        if (windowStatePath.Length > 0) new WindowState(false).Save(windowStatePath);
        autoPending = "";
        autoCts?.Cancel();
        autoCts = null;
    }

    /// <summary>Auto mode only ever acts on an executable screen, one decision per observed state.</summary>
    private void AutoStep(GameSnapshot next)
    {
        if (!autoEnabled || SolverBridge.Owned || autoPending.Length > 0 || autoWait > 0) return;
        var room = next.Room;
        if (room is null) { autoArmedKind = ""; autoSelectPendingConfirm = false; return; }
        if (room.Kind != "Select") autoSelectPendingConfirm = false;
        if (autoSelectPendingConfirm)
        {
            if (GameReader.CanConfirmSelection())
            {
                try { GameReader.ConfirmSelection(); message = "自动操作：已确认选牌。"; autoWait = 1.0; autoArmedKind = "Select"; }
                catch (Exception error) { message = $"自动确认失败：{error.Message}"; StopAuto(); }
                autoSelectPendingConfirm = false;
            }
            return;
        }
        if (room.Kind != autoArmedKind) autoArmedKind = "";
        // One action per screen visit: a shop should not drain the purse on its own.
        if (room.Kind == autoArmedKind) return;
        if (room.Kind is not ("Reward" or "Rest" or "Shop" or "Select")) return;
        if (room.Options.All(option => option.Locked)) return;
        var key = next.Fingerprint + "|" + room.Kind;
        if (key == autoDone) return;
        autoPending = key;
        autoCts = new CancellationTokenSource();
        _ = DecideAsync(next, room.Options, autoCts.Token);
    }

    private async Task DecideAsync(GameSnapshot snapshot, RoomOptionView[] options, CancellationToken token)
    {
        try
        {
            var reply = await chat.DecideAsync(snapshot, options, token).ConfigureAwait(false);
            var id = ChoiceParser.Parse(reply, options.Select(option => option.Id).ToArray());
            mainQueue.Enqueue(() => ApplyAuto(snapshot.Fingerprint, id));
        }
        catch (OperationCanceledException) { mainQueue.Enqueue(() => autoPending = ""); }
        catch (Exception error)
        {
            mainQueue.Enqueue(() =>
            {
                autoPending = "";
                message = $"自动决策失败：{error.Message}";
                StopAuto();
            });
        }
    }

    private void ApplyAuto(string fingerprint, string? id)
    {
        autoPending = "";
        if (!autoEnabled) return;
        if (id == null)
        {
            StopAuto();
            message = "自动决策未返回合法选项，已停止自动操作。";
            return;
        }
        var fresh = GameReader.Read();
        if (fresh.Fingerprint != fingerprint) { message = "状态已变化，已丢弃这条自动决策。"; return; }
        var room = fresh.Room;
        if (room is null || !room.Options.Any(option => option.Id == id && !option.Locked))
        {
            message = "选项已失效，已丢弃这条自动决策。";
            return;
        }
        autoDone = fingerprint + "|" + room.Kind;
        autoArmedKind = room.Kind;
        autoWait = 1.0;
        switch (room.Kind)
        {
            case "Reward" when int.TryParse(id, out var index): ExecuteReward(index); break;
            case "Rest": ExecuteRest(id); break;
            case "Shop": ExecutePurchase(id); break;
            case "Select" when int.TryParse(id, out var cardIndex):
                autoSelectPendingConfirm = true;
                autoWait = 0.4;
                ExecuteGridCard(cardIndex);
                break;
        }
    }

    private void ExecuteGridCard(int index)
    {
        try { GameReader.SelectGridCard(index); message = "已选中该卡牌，需要时再点确认。"; decisionStatus.Text = message; GD.Print($"[SpireCompanion] decision_result action=select_grid id={index} ok"); }
        catch (Exception error)
        {
            message = $"选牌失败：{error.Message}";
            decisionStatus.Text = message;
            GD.PushError($"[SpireCompanion] select_grid: {error}");
        }
        decisionSignature = "";
        Refresh();
    }

    private void ExecuteConfirm()
    {
        try { GameReader.ConfirmSelection(); message = "已确认选牌。"; decisionStatus.Text = message; GD.Print("[SpireCompanion] decision_result action=confirm_select ok"); }
        catch (Exception error)
        {
            message = $"确认失败：{error.Message}";
            decisionStatus.Text = message;
            GD.PushError($"[SpireCompanion] confirm_select: {error}");
        }
        decisionSignature = "";
        Refresh();
    }

    private void ExecuteReward(int index)
    {
        try { GameReader.SelectReward(index); message = "已选择奖励卡牌。"; decisionStatus.Text = message; GD.Print($"[SpireCompanion] decision_result action=reward id={index} ok"); }
        catch (Exception error)
        {
            message = $"选择失败：{error.Message}";
            decisionStatus.Text = message;
            GD.PushError($"[SpireCompanion] select_reward: {error}");
        }
        decisionSignature = "";
        Refresh();
    }

    private void ExecuteSkipReward()
    {
        try { GameReader.SkipReward(); message = "已跳过这张奖励。"; }
        catch (Exception error)
        {
            message = $"跳过失败：{error.Message}";
            GD.PushError($"[SpireCompanion] skip_reward: {error}");
        }
        decisionSignature = "";
        Refresh();
    }

    private void PauseAll()
    {
        StopAuto();
        chat.Cancel();
        try { SolverBridge.StopOwned(); message = "本模块已暂停，已接管的求解器也已停止。"; }
        catch (Exception error)
        {
            message = "本模块已暂停，但求解器停止未确认，请手动停止求解器。";
            GD.PushError($"[SpireCompanion] solver_stop: {error}");
        }
        Refresh();
    }

    private void StartSolver()
    {
        StopAuto();
        try { SolverBridge.Start(); message = "已交给 CombatSolver；暂停按钮可停止接管。"; }
        catch (Exception error)
        {
            message = "求解器未成功接管，详见日志；不会改用模型出牌。";
            GD.PushError($"[SpireCompanion] solver_start: {error}");
        }
        Refresh();
    }

    private void Refresh()
    {
        try
        {
            var font = FontManager.GetSubstituteFont("zhs", FontType.Regular);
            if (font != null && panel.Theme.DefaultFont != font)
            {
                panel.Theme.DefaultFont = font;
                // The launcher is a sibling of the window: it must share its CJK theme explicitly.
                toggle.Theme = panel.Theme;
                toggle.AddThemeFontOverride("font", font);
                foreach (var type in new[] { "Button", "Label", "LineEdit", "TextEdit", "OptionButton", "CheckButton", "TabBar", "PopupMenu", "TooltipLabel" })
                    panel.Theme.SetFont("font", type, font);
                // RichTextLabel uses its own font items; without these the reply falls back to the game's light theme font.
                foreach (var name in new[] { "normal_font", "bold_font", "italics_font", "mono_font" })
                    panel.Theme.SetFont(name, "RichTextLabel", font);
                foreach (var name in new[] { "normal_font_size", "bold_font_size", "italics_font_size", "mono_font_size" })
                    panel.Theme.SetFontSize(name, "RichTextLabel", 18);
            }
            SolverBridge.ObserveLifecycle();
            var next = GameReader.Read();
            var solver = SolverBridge.Status();
            UpdateDecision(next);
            AutoStep(next);
            stateLabel.Text = SolverBridge.Owned ? "求解器接管" : "只读建议";
            stateLabel.AddThemeColorOverride("font_color", SolverBridge.Owned ? UiStyle.AccentDeep : UiStyle.Good);
            status.Text = $"场景：{next.Scene} · {next.CombatTurn}\n角色：{next.Character} · HP {next.Hp}/{next.MaxHp} · 金币 {next.Gold}\n能量：{next.Energy?.ToString() ?? "—"} · 格挡：{next.Block}\n手牌：{string.Join("、", next.Cards.Select(c => $"{c.Name}({c.Cost})"))}\n敌人：{string.Join("、", next.Enemies.Select(e => $"{e.Name} {e.Hp}/{e.MaxHp}"))}\n牌组 {next.Deck.Length} · 遗物 {next.Relics.Length} · 药水 {next.Potions.Length}\n阶段：{(next.CanEndTurn ? "可操作" : next.BlockReason)}\n求解器：{solver.Description}\n{message}";
            solverStart.Disabled = !solver.Available || solver.FullAuto || solver.Deploying || SolverBridge.Owned || !SolverBridge.CanStart;
            if (!reportedReady)
            {
                reportedReady = true;
                GD.Print($"[SpireCompanion] PANEL_READY scene={next.Scene} font_ready={font != null} fingerprint={next.Fingerprint[..16]}");
            }
        }
        catch (Exception error)
        {
            solverStart.Disabled = true;
            status.Text = "状态读取失败。详见游戏日志。";
            GD.PushError($"[SpireCompanion] read_state: {error}");
            // Avoid emitting a failing read every frame.
            remaining = 5;
        }
    }
}
