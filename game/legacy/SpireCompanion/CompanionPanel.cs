using Godot;
using System.Collections.Concurrent;
using System.Text.Json;
using SpireCompanion.Core;
using MegaCrit.Sts2.Core.Localization.Fonts;

namespace SpireCompanion;

internal sealed class CompanionPanel
{
    private readonly ActionGate gate = new();
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
    private Button endTurn = null!;
    private Button pause = null!;
    private Button resume = null!;
    private Button toggle = null!;
    private GameSnapshot? shown;
    private Button solverStart = null!;
    private bool reportedReady;
    private bool layoutReported;
    private int ticks;
    private TabContainer tabs = null!;
    private double remaining;
    private bool f9Down;
    private string message = "默认暂停。本版仅供手动验证，不会自动出牌。";

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
        var close = new Button { Text = "关闭" }; close.Pressed += () => panel.Hide();
        UiStyle.Size(close, 40, 84); header.AddChild(close);

        tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; layout.AddChild(tabs);
        chatScroll = Page("聊天", out var chatBody);
        settingsScroll = Page("设置", out var settingsBody);
        gameScroll = Page("游戏状态", out var body);
        UiStyle.StyleTabs(tabs);

        var statusCard = UiStyle.Card(body, "当前局面");
        status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(0, 80) };
        statusCard.AddChild(status);
        var controlCard = UiStyle.Card(body, "手动控制");
        pause = new Button { Text = "暂停所有本模块动作" };
        pause.Pressed += PauseAll;
        UiStyle.Secondary(pause); UiStyle.Size(pause, 44);
        controlCard.AddChild(pause);
        resume = new Button { Text = "启用手动操作" };
        resume.Pressed += () => { gate.Resume(); message = "已启用手动操作，仍需点击动作按钮。"; Refresh(); };
        UiStyle.Secondary(resume); UiStyle.Size(resume, 44);
        controlCard.AddChild(resume);
        endTurn = new Button { Text = "手动结束回合" };
        endTurn.Pressed += SubmitEndTurn;
        UiStyle.Primary(endTurn); UiStyle.Size(endTurn, 44);
        controlCard.AddChild(endTurn);
        solverStart = new Button { Text = "交给 CombatSolver 打本场战斗" };
        solverStart.Pressed += StartSolver;
        UiStyle.Secondary(solverStart); UiStyle.Size(solverStart, 44);
        controlCard.AddChild(solverStart);
        var export = new Button { Text = "导出当前状态（调试）" };
        export.Pressed += ExportSnapshot;
        UiStyle.Ghost(export); UiStyle.Size(export, 40);
        controlCard.AddChild(export);

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
        tree.Root.AddChild(layer);
        tree.ProcessFrame += () => Tick(tree.Root.GetProcessDeltaTime());
        tree.Root.TreeExiting += () =>
        {
            gate.Pause();
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
        bool pressed = Input.IsPhysicalKeyPressed(Key.F9);
        if (pressed && !f9Down) panel.Visible = !panel.Visible;
        f9Down = pressed;
        remaining -= delta;
        if (remaining <= 0) { remaining = 0.25; Refresh(); }
    }

    private void SubmitEndTurn()
    {
        if (shown == null) return;
        var ticket = gate.Capture(shown.Fingerprint);
        try
        {
            var fresh = GameReader.Read();
            var result = gate.Execute(ticket, fresh.Fingerprint, fresh.CanEndTurn, GameReader.EndTurn);
            message = result.Message;
            GD.Print($"[SpireCompanion] end_turn submitted={result.Submitted} {message}");
        }
        catch (Exception error)
        {
            gate.Pause();
            message = "操作异常，已暂停。请检查游戏日志；不会自动重试。";
            GD.PushError($"[SpireCompanion] end_turn: {error}");
        }
        Refresh();
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

    private void PauseAll()
    {
        gate.Pause();
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
        gate.Pause(); // Native manual actions stay disabled while the solver owns execution.
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
            // Ending a turn changes the state on purpose, so manual mode stays enabled;
            // stale or duplicate submissions are already rejected by the gate's fingerprint check.
            shown = next;
            stateLabel.Text = SolverBridge.Owned ? "求解器接管" : gate.Paused ? "暂停" : "就绪";
            stateLabel.AddThemeColorOverride("font_color", SolverBridge.Owned ? UiStyle.AccentDeep : gate.Paused ? UiStyle.Muted : UiStyle.Good);
            status.Text = $"场景：{next.Scene} · {next.CombatTurn}\n角色：{next.Character} · HP {next.Hp}/{next.MaxHp} · 金币 {next.Gold}\n能量：{next.Energy?.ToString() ?? "—"} · 格挡：{next.Block}\n手牌：{string.Join("、", next.Cards.Select(c => $"{c.Name}({c.Cost})"))}\n敌人：{string.Join("、", next.Enemies.Select(e => $"{e.Name} {e.Hp}/{e.MaxHp}"))}\n牌组 {next.Deck.Length} · 遗物 {next.Relics.Length} · 药水 {next.Potions.Length}\n{(next.CanEndTurn ? "可以手动结束回合" : next.BlockReason)}\n求解器：{solver.Description}\n{message}";
            endTurn.Disabled = gate.Paused || !next.CanEndTurn;
            resume.Disabled = !gate.Paused;
            resume.Text = gate.Paused ? "启用手动操作" : "手动操作已启用";
            solverStart.Disabled = !solver.Available || solver.FullAuto || solver.Deploying || SolverBridge.Owned || !SolverBridge.CanStart;
            if (!reportedReady)
            {
                reportedReady = true;
                GD.Print($"[SpireCompanion] PANEL_READY scene={next.Scene} paused={gate.Paused} font_ready={font != null} fingerprint={next.Fingerprint[..16]}");
            }
        }
        catch (Exception error)
        {
            gate.Pause(); shown = null; endTurn.Disabled = true; solverStart.Disabled = true;
            status.Text = "状态读取失败，已暂停。详见游戏日志。";
            GD.PushError($"[SpireCompanion] read_state: {error}");
            // Avoid emitting a failing read every frame.
            remaining = 5;
        }
    }
}
