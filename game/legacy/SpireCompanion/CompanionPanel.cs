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
    private readonly SceneTree tree;
    private ScrollContainer chatScroll = null!;
    private ScrollContainer gameScroll = null!;
    private bool dragging;
    private Vector2 dragOffset;
    private Button endTurn = null!;
    private Button toggle = null!;
    private GameSnapshot? shown;
    private Button solverStart = null!;
    private bool reportedReady;
    private double remaining;
    private bool f9Down;
    private string message = "默认暂停。本版仅供手动验证，不会自动出牌。";

    public CompanionPanel(SceneTree tree)
    {
        this.tree = tree;
        var layer = new CanvasLayer { Layer = 90 };
        panel = new PanelContainer { Position = new Vector2(24, 70) };
        // Use game fonts, never depend on operating-system font enumeration.
        panel.Theme = new Theme { DefaultFontSize = 18 };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("18212e"), BorderColor = new Color("46617e"),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 14
        });
        var layout = new VBoxContainer(); panel.AddChild(layout);
        var header = new HBoxContainer(); layout.AddChild(header);
        var title = new Label { Text = "尖塔 AI 陪玩 · 拖动标题移动", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop };
        title.GuiInput += input =>
        {
            if (input is InputEventMouseButton mouse && mouse.ButtonIndex == MouseButton.Left && mouse.Pressed)
            { dragging = true; dragOffset = title.GetGlobalMousePosition() - panel.Position; }
        };
        header.AddChild(title);
        var close = new Button { Text = "关闭" }; close.Pressed += () => panel.Hide(); header.AddChild(close);
        var tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; layout.AddChild(tabs);
        chatScroll = new ScrollContainer { Name = "聊天与API", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var chatBody = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        chatScroll.AddChild(chatBody); tabs.AddChild(chatScroll);
        gameScroll = new ScrollContainer { Name = "游戏状态与控制", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        gameScroll.AddChild(body); tabs.AddChild(gameScroll);
        status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 100) };
        body.AddChild(status);
        var pause = new Button { Text = "暂停所有本模块动作" };
        pause.Pressed += PauseAll;
        body.AddChild(pause);
        var resume = new Button { Text = "启用本模块手动操作（非自动托管）" };
        resume.Pressed += () => { gate.Resume(); message = "已启用手动操作，仍需点击动作按钮。"; Refresh(); };
        body.AddChild(resume);
        endTurn = new Button { Text = "手动结束回合" };
        endTurn.Pressed += SubmitEndTurn;
        body.AddChild(endTurn);
        solverStart = new Button { Text = "交给 CombatSolver 打本场战斗" };
        solverStart.Pressed += StartSolver;
        body.AddChild(solverStart);
        var export = new Button { Text = "导出当前状态（调试）" };
        export.Pressed += ExportSnapshot;
        body.AddChild(export);
        chat = new ChatPanel(chatBody, mainQueue.Enqueue);
        layer.AddChild(panel);
        toggle = new Button { Text = "AI 搭子", Position = new Vector2(16, 16), TooltipText = "显示或隐藏窗口（F9）", Theme = panel.Theme };
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

    private void Tick(double delta)
    {
        while (mainQueue.TryDequeue(out var update)) update();
        var viewport = tree.Root.GetVisibleRect().Size;
        var width = Math.Clamp(viewport.X - 40, 360, 600);
        var height = Math.Clamp(viewport.Y - 100, 300, 720);
        chatScroll.CustomMinimumSize = gameScroll.CustomMinimumSize = new Vector2(width - 40, height - 90);
        panel.Size = new Vector2(width, height);
        if (dragging)
        {
            if (!Input.IsMouseButtonPressed(MouseButton.Left)) dragging = false;
            else panel.Position = tree.Root.GetMousePosition() - dragOffset;
        }
        panel.Position = panel.Position.Clamp(Vector2.Zero, (viewport - panel.Size).Max(Vector2.Zero));
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
            message = "已导出当前状态，文件位置见日志。";
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
            }
            SolverBridge.ObserveLifecycle();
            var next = GameReader.Read();
            var solver = SolverBridge.Status();
            // While enabled, any unowned state change invalidates outstanding work.
            if (!SolverBridge.Owned && !gate.Paused && shown != null && next.Fingerprint != shown.Fingerprint)
            {
                gate.Pause();
                message = "状态已变化，已暂停；检查后可重新启用手动操作。";
            }
            shown = next;
            status.Text = $"控制：{(SolverBridge.Owned ? "求解器接管" : gate.Paused ? "暂停" : "手动可用")}\n场景：{next.Scene} · {next.CombatTurn}\n角色：{next.Character} · HP {next.Hp}/{next.MaxHp} · 金币 {next.Gold}\n能量：{next.Energy?.ToString() ?? "—"} · 格挡：{next.Block}\n手牌：{string.Join("、", next.Cards.Select(c => $"{c.Name}({c.Cost})"))}\n敌人：{string.Join("、", next.Enemies.Select(e => $"{e.Name} {e.Hp}/{e.MaxHp}"))}\n牌组 {next.Deck.Length} · 遗物 {next.Relics.Length} · 药水 {next.Potions.Length}\n{(next.CanEndTurn ? "可以手动结束回合" : next.BlockReason)}\n求解器：{solver.Description}\n{message}";
            endTurn.Disabled = gate.Paused || !next.CanEndTurn;
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
