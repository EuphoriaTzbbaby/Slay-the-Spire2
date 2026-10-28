using Godot;
using MegaCrit.Sts2.Core.Nodes;
using SpireCompanion.Core;

namespace SpireCompanion;

internal sealed class ChatPanel : IDisposable
{
    private readonly ChatClient client = new();
    private readonly ChatConversation conversation;
    private readonly Action<Action> onMain;
    private readonly LocalProfilePanel profiles;
    private string settingsPath => profiles.SettingsPath;
    private readonly LineEdit endpoint = new() { PlaceholderText = "https://api.deepseek.com" };
    private readonly LineEdit model = new() { PlaceholderText = "deepseek-flash" };
    private readonly LineEdit key = new() { PlaceholderText = "API Key（加密保存在本地档案）", Secret = true };
    private readonly CheckButton remember = new() { Text = "记住 API Key（加密保存在当前档案）", ButtonPressed = true };
    private readonly Button forget = new() { Text = "删除已记住的密钥" };
    private string? keyOrigin;
    private readonly OptionButton mode = new();
    private readonly Label info = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label testInfo = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
    private readonly ScrollContainer logScroll = new()
    {
        SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        CustomMinimumSize = new Vector2(0, 260)
    };
    private readonly VBoxContainer log = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    private readonly LineEdit question = new() { PlaceholderText = "问问搭子，或点击分析当前局面", MaxLength = 4000 };
    private readonly Button test = new() { Text = "测试连接" };
    private CancellationTokenSource? probe;
    private readonly Button send = new() { Text = "发送" };
    private readonly Button advice = new() { Text = "分析当前局面" };
    private readonly Button cancel = new() { Text = "取消回复", Disabled = true };
    private readonly Button save = new() { Text = "保存设置" };
    private VBoxContainer welcome = null!;
    private bool busy;
    private string lastScreenKey = "";
    private PanelContainer? typingBubble;
    private bool cancelRequested;
    private bool disposed;
    private int requestNumber;
    private ChatSettings settings = new();

    public ChatPanel(VBoxContainer chat, VBoxContainer settingsBody, Action<Action> onMain)
    {
        this.onMain = onMain;
        conversation = new ChatConversation(client);

        var profileCard = UiStyle.Card(settingsBody, "账户档案");
        profiles = new LocalProfilePanel(profileCard, onMain, OnProfileChanged);

        var modelCard = UiStyle.Card(settingsBody, "模型与连接");
        modelCard.AddChild(new Label { Text = "API 地址（默认 DeepSeek，可修改）" });
        modelCard.AddChild(endpoint);
        modelCard.AddChild(new Label { Text = "模型名称" });
        modelCard.AddChild(model);
        modelCard.AddChild(new Label { Text = "API Key" });
        modelCard.AddChild(key);
        var reveal = new CheckButton { Text = "显示密钥" };
        reveal.Toggled += visible => key.Secret = !visible;
        modelCard.AddChild(reveal);
        modelCard.AddChild(remember);
        var importOld = new Button { Text = "导入此前的系统密钥（可选）" };
        importOld.Pressed += ImportLegacy;
        modelCard.AddChild(importOld);
        modelCard.AddChild(forget);
        modelCard.AddChild(testInfo);
        modelCard.AddChild(UiStyle.Hint("密钥保存在本地加密档案中，系统只保存自动解锁凭据，不进入游戏设置或 Git。"));
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        save.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        UiStyle.Primary(save); UiStyle.Secondary(test); UiStyle.Danger(forget); UiStyle.Ghost(importOld);
        UiStyle.Size(save, 46); UiStyle.Size(test, 46, 110); UiStyle.Size(forget, 42, 190); UiStyle.Size(importOld, 42);
        actions.AddChild(save); actions.AddChild(test);
        modelCard.AddChild(actions);

        var styleCard = UiStyle.Card(settingsBody, "回复风格");
        mode.AddItem("安静"); mode.AddItem("普通"); mode.AddItem("话多");
        mode.Select(1);
        styleCard.AddChild(mode);
        styleCard.AddChild(UiStyle.Hint("安静只回关键点，普通会解释依据，话多会多聊几句。"));

        welcome = new VBoxContainer();
        welcome.AddThemeConstantOverride("separation", 8);
        welcome.AddChild(Speech(
            "我是奶龙，你的尖塔陪练。先随便聊聊，或者点「分析当前局面」，我看看这一手该打哪张牌。", 132));
        chat.AddChild(welcome);

        var transcriptCard = UiStyle.Card(chat, "对话");
        log.AddThemeConstantOverride("separation", 8);
        logScroll.AddChild(log);
        transcriptCard.AddChild(logScroll);
        chat.AddChild(question);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        send.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        UiStyle.Primary(send);
        var clear = new Button { Text = "清空" };
        UiStyle.Secondary(advice); UiStyle.Ghost(cancel); UiStyle.Ghost(clear);
        UiStyle.Size(send, 44); UiStyle.Size(advice, 44, 132); UiStyle.Size(cancel, 44, 108); UiStyle.Size(clear, 44, 76);
        buttons.AddChild(send); buttons.AddChild(advice); buttons.AddChild(cancel); buttons.AddChild(clear);
        cancel.Visible = false;
        chat.AddChild(buttons);
        chat.AddChild(UiStyle.Hint("只提供建议，不会自动出牌、购物或用药。回复可能产生模型服务费用。"));
        chat.AddChild(info);

        send.Pressed += () => Begin(question.Text);
        question.TextSubmitted += Begin;
        advice.Pressed += () => Begin("请根据当前局面给出最值得关注的建议，先说结论，再简短解释。如果缺少关键状态请说明。");
        cancel.Pressed += Cancel;
        clear.Pressed += () =>
        {
            if (busy) { info.Text = "请先取消并等待当前回复结束。"; return; }
            conversation.Clear(); ClearLog(); info.Text = "已清空本次会话。";
        };
        save.Pressed += Save;
        test.Pressed += TestConnection;
        forget.Pressed += Forget;
        remember.Toggled += _ => { info.Text = "点击保存设置以应用密钥记忆选项。"; };
        try
        {
            settings = ChatSettings.Load(settingsPath);
            endpoint.Text = settings.Endpoint; model.Text = settings.Model;
            remember.ButtonPressed = settings.RememberApiKey;
            mode.Select(settings.TalkMode switch { "quiet" => 0, "chatty" => 2, _ => 1 });
            mode.ItemSelected += index =>
            {
                // Switching style must take effect on the next request, not only after Save.
                settings = settings with { TalkMode = index switch { 0 => "quiet", 2 => "chatty", _ => "normal" } };
                info.Text = $"回复风格已切换为「{new[] { "安静", "普通", "话多" }[Math.Min(index, 2)]}」，下一条回复生效。";
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { info.Text = "读取配置失败，请重新填写并保存。"; }
        endpoint.Text = string.IsNullOrWhiteSpace(endpoint.Text) ? settings.Endpoint : endpoint.Text;
        model.Text = string.IsNullOrWhiteSpace(model.Text) ? settings.Model : model.Text;
        keyOrigin = Origin(endpoint.Text);
        key.Text = System.Environment.GetEnvironmentVariable("SPIRE_COMPANION_API_KEY") ?? "";
        endpoint.TextChanged += value =>
        {
            var next = Origin(value);
            if (next != null && keyOrigin != null && next != keyOrigin)
            { key.Text = ""; info.Text = "服务地址已更换，请填写对应服务的密钥。"; }
            if (next != null) keyOrigin = next;
        };
    }

    private void OnProfileChanged(EncryptedProfileStore.Session? profile)
    {
        conversation.Clear(); ClearLog(); key.Text = ""; requestNumber++;
        settings = ChatSettings.Load(settingsPath);
        endpoint.Text = settings.Endpoint; model.Text = settings.Model;
        mode.Select(settings.TalkMode switch { "quiet" => 0, "chatty" => 2, _ => 1 });
        remember.ButtonPressed = settings.RememberApiKey;
        keyOrigin = Origin(settings.Endpoint);
        key.Text = profile?.GetApiKey(settings.Endpoint) ?? "";
        info.Text = profile == null ? "档案已锁定，已清除当前密钥和对话。" : $"已切换到 {profile.Profile.Name}，配置与密钥独立。";
    }
    private void ImportLegacy()
    {
        if (busy || profiles.Busy) return;
        try
        {
            if (profiles.Current == null) throw new ProfileVaultException("请先解锁目标本地档案。");
            var old = ModelSecretStore.Load(endpoint.Text.Trim());
            if (string.IsNullOrEmpty(old)) { info.Text = "该服务没有此前保存的系统密钥。"; return; }
            profiles.Current.SaveApiKey(endpoint.Text.Trim(), old); key.Text = old;
            info.Text = "已导入并加密保存，旧系统密钥未自动删除。";
        }
        catch (Exception error) { info.Text = error is ProfileVaultException ? error.Message : "旧密钥导入失败，请手动填写。"; }
    }

    private void TestConnection()
    {
        if (disposed || busy || profiles.Busy) return;
        try
        {
            var config = FromFields(); _ = config.RequestUri();
            probe = new CancellationTokenSource();
            int number = ++requestNumber;
            SetBusy(true); info.Text = "正在测试 API 连接和模型调用…";
            testInfo.Visible = true; testInfo.Text = "正在测试 API 连接和模型调用…";
            _ = ProbeAsync(number, config, key.Text, probe);
        }
        catch (ArgumentException error) { info.Text = error.Message; }
    }
    private async Task ProbeAsync(int number, ChatSettings config, string apiKey, CancellationTokenSource request)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            _ = await client.TestAsync(config, apiKey, request.Token).ConfigureAwait(false);
            onMain(() => { if (!disposed && number == requestNumber) { info.Text = $"连接及模型调用成功（{elapsed.ElapsedMilliseconds} ms），现在可以发送消息。"; testInfo.Text = info.Text; } });
        }
        catch (OperationCanceledException)
        { onMain(() => { if (!disposed && number == requestNumber) { info.Text = "连接测试已取消。"; testInfo.Text = info.Text; } }); }
        catch (Exception error) when (error is ModelReplyException or ArgumentException)
        { onMain(() => { if (!disposed && number == requestNumber) { info.Text = error.Message; testInfo.Text = error.Message; } }); }
        catch (Exception)
        { onMain(() => { if (!disposed && number == requestNumber) { info.Text = "连接测试失败，请核对配置。"; testInfo.Text = info.Text; } }); }
        finally
        {
            onMain(() =>
            {
                if (!disposed) { if (cancelRequested) info.Text = "本次请求已取消。"; SetBusy(false); }
                if (ReferenceEquals(probe, request)) probe = null;
                request.Dispose();
            });
        }
    }

    private static string? Origin(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
        ? uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant() : null;
    private ChatSettings FromFields() => new(endpoint.Text.Trim(), model.Text.Trim(), mode.Selected switch { 0 => "quiet", 2 => "chatty", _ => "normal" }, settings.TimeoutSeconds, remember.ButtonPressed);
    private void Save()
    {
        if (busy || profiles.Busy) return;
        try
        {
            var next = FromFields(); _ = next.RequestUri();
            string detail;
            if (!next.RememberApiKey)
            {
                profiles.Current?.DeleteApiKey(next.Endpoint);
                detail = "设置已保存，密钥仅保留在当前会话。";
            }
            else if (profiles.Current == null)
                detail = "设置已保存；未解锁本地档案，API Key 未落盘，仅在本次会话有效。";
            else if (string.IsNullOrWhiteSpace(key.Text))
                detail = "设置已保存；密钥输入为空，档案中未写入 API Key。";
            else
            {
                profiles.Current.SaveApiKey(next.Endpoint, key.Text);
                detail = "设置已保存，API Key 已加密写入当前本地档案。";
            }
            next.Save(settingsPath); settings = next;
            info.Text = detail;
        }
        catch (Exception error) when (error is ProfileVaultException or System.Security.Cryptography.CryptographicException or ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException or System.Runtime.InteropServices.ExternalException or TypeInitializationException or DllNotFoundException or EntryPointNotFoundException or UriFormatException)
        { info.Text = error is ProfileVaultException ? error.Message : "保存失败，请检查配置、密钥或本地档案权限。"; }
    }
    private void Forget()
    {
        if (busy || profiles.Busy) return;
        try
        {
            var next = FromFields() with { RememberApiKey = false }; _ = next.RequestUri();
            profiles.Current?.DeleteApiKey(next.Endpoint); next.Save(settingsPath); settings = next;
            key.Text = ""; remember.ButtonPressed = false;
            info.Text = "已删除该服务记住的密钥，并关闭自动记忆。";
        }
        catch (Exception error) when (error is ProfileVaultException or System.Security.Cryptography.CryptographicException or ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException or System.Runtime.InteropServices.ExternalException or TypeInitializationException or DllNotFoundException or EntryPointNotFoundException or UriFormatException)
        { info.Text = "删除失败，请检查服务地址和本地档案权限。"; }
    }
    /// <summary>Entry point for text that did not come from the input box (voice input will use this).</summary>
    public void SubmitExternal(string text)
    {
        if (disposed) return;
        if (busy) { info.Text = "上一条还在回复，请稍候或先取消。"; return; }
        Begin(text);
    }

    public bool Busy => busy;

    /// <summary>The tool callback runs on a worker thread; marshal the game read to the main thread.</summary>
    private string? LookupCardOnMain(string name)
    {
        if (NGame.IsMainThread()) return GameReader.LookupCardText(name);
        string? result = null;
        using var done = new ManualResetEventSlim();
        onMain(() =>
        {
            try { result = GameReader.LookupCardText(name); }
            finally { done.Set(); }
        });
        return done.Wait(TimeSpan.FromSeconds(3)) ? result : null;
    }

    private void Begin(string text)
    {
        if (disposed || busy || profiles.Busy) return;
        text = text.Trim();
        try
        {
            if (text.Length == 0) { info.Text = "请输入消息。"; return; }
            var next = FromFields(); _ = next.RequestUri();
            var snapshot = GameReader.Read();
            var apiKey = key.Text;
            int number = ++requestNumber;
            SetBusy(true); info.Text = "搭子正在思考…"; question.Text = "";
            Append("你", text);
            ShowTyping();
            _ = CompleteAsync(number, next, apiKey, text, snapshot);
        }
        catch (ArgumentException error) { info.Text = error.Message; }
        catch (Exception) { info.Text = "当前状态暂不可读，请稍后再试。"; }
    }
    private async Task CompleteAsync(int number, ChatSettings config, string apiKey, string text, GameSnapshot snapshot)
    {
        try
        {
            var screenKey = snapshot.Scene + "|" + (snapshot.Room?.Id ?? "");
            var screenChanged = lastScreenKey.Length > 0 && screenKey != lastScreenKey;
            lastScreenKey = screenKey;
            var reply = await conversation.AskAsync(config, apiKey, text, snapshot, PlanProvider?.Invoke() ?? "", screenChanged,
                LookupCardOnMain, message => Godot.GD.Print($"[SpireCompanion] chat_tool {message}")).ConfigureAwait(false);
            onMain(() =>
            {
                if (disposed || number != requestNumber) return;
                bool changed;
                try { changed = GameReader.Read().Fingerprint != reply.Fingerprint; }
                catch { changed = true; }
                var grounding = AnswerGrounding.Check(reply.Text, snapshot);
                var body = reply.Text;
                if (grounding.Unknown.Length > 0)
                    body += $"\n\n（提示：以下名称不在当前界面：{string.Join("、", grounding.Unknown)}；可能是模型记忆，请以屏幕为准。）";
                Append("搭子", body);
                info.Text = changed ? "局面已变化：这条建议基于提问时的状态，可重新分析。" : "建议已返回，由你决定是否采用。";
            });
        }
        catch (OperationCanceledException)
        { onMain(() => { if (!disposed && number == requestNumber) info.Text = "本次回复已取消。"; }); }
        catch (Exception error) when (error is ModelReplyException or ArgumentException or InvalidOperationException)
        { onMain(() => { if (!disposed && number == requestNumber) info.Text = error.Message; }); }
        catch (Exception)
        { onMain(() => { if (!disposed && number == requestNumber) info.Text = "模型请求失败，请检查配置后重试。"; }); }
        finally
        { onMain(() => { if (!disposed) { if (cancelRequested) info.Text = "本次回复已取消。"; HideTyping(); SetBusy(false); } }); }
    }
    /// <summary>One-shot structured choice for auto mode; never touches the visible conversation.</summary>
    /// <summary>Current reply style, used for local lines such as the defeat note.</summary>
    public string TalkMode => settings.TalkMode;

    public Task<string> DecideAsync(GameSnapshot snapshot, RoomOptionView[] options, int required, string directive,
        CancellationToken token, string plan = "") =>
        client.DecideAsync(settings, key.Text, snapshot, options, required, directive, token, plan);

    /// <summary>Shared run plan supplied by the panel, so chat and decisions see the same goal.</summary>
    public Func<string>? PlanProvider { get; set; }

    /// <summary>Auto-mode note in the conversation; not added to model history.</summary>
    public void Notify(string text) => Append("自动", text);

    /// <summary>Raised when the player starts talking to the companion or cancels a request.</summary>
    public event Action? Interrupted;

    /// <summary>Raised with the player's text, so auto decisions can honour explicit instructions.</summary>
    public event Action<string>? UserSaid;

    public (ChatSettings Settings, string ApiKey) RequestContext => (settings, key.Text);

    public Task<string> CommentAsync(ChatSettings config, string apiKey, GameSnapshot snapshot, string occasion, CancellationToken token) =>
        client.CommentAsync(config, apiKey, snapshot, occasion, token);

    public void SayProactive(string text) => Append("搭子", text);

    public void Cancel()
    {
        HideTyping();
        Interrupted?.Invoke();
        if (!busy) return;
        cancelRequested = true;
        requestNumber++; // Suppress a reply already queued for the next game frame as well.
        conversation.Cancel(); probe?.Cancel(); info.Text = "正在取消请求…";
    }
    private void SetBusy(bool value)
    {
        if (value) cancelRequested = false;
        profiles.SetChatBusy(value);
        busy = value; send.Disabled = advice.Disabled = save.Disabled = test.Disabled = value;
        cancel.Disabled = !value; cancel.Visible = value;
        endpoint.Editable = model.Editable = key.Editable = !value; mode.Disabled = value; remember.Disabled = forget.Disabled = value;
    }
    private void Append(string who, string text)
    {
        welcome.Visible = false;
        var assistant = who != "你";
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var bubble = new PanelContainer();
        bubble.AddThemeStyleboxOverride("panel", assistant
            ? UiStyle.Box(UiStyle.CardColor, new Color("EFD3A0"), 14, 10)
            : UiStyle.Box(new Color("FFE9C8"), new Color("E8AE4B"), 14, 10));
        bubble.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var body = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false, SelectionEnabled = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        var color = assistant ? "#B45309" : "#C2410C";
        body.AppendText($"[color={color}][b]{who}[/b][/color]  {ChatMarkdown.ToBbcode(text)}");
        bubble.AddChild(body);
        if (assistant && UiStyle.Avatar is { } face)
            row.AddChild(new TextureRect
            {
                Texture = face, CustomMinimumSize = new Vector2(36, 36), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
            });
        row.AddChild(bubble);
        log.AddChild(row);
        while (log.GetChildCount() > 60) log.GetChild(0).Free();
        // Bounded visible history; model history is independently bounded by conversation pairs.
        ScrollLogToEnd();
    }

    private void ClearLog()
    {
        foreach (var child in log.GetChildren()) child.Free();
        typingBubble = null;
        welcome.Visible = true;
    }

    /// <summary>Visible "typing" bubble so the player can tell the model is still working.</summary>
    private void ShowTyping()
    {
        if (typingBubble != null && GodotObject.IsInstanceValid(typingBubble)) return;
        welcome.Visible = false;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        if (UiStyle.Avatar is { } face)
            row.AddChild(new TextureRect
            {
                Texture = face, CustomMinimumSize = new Vector2(36, 36), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
            });
        var bubble = new PanelContainer();
        bubble.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.CardColor, new Color("EFD3A0"), 14, 10));
        bubble.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var body = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        body.AppendText("[color=#B45309][b]搭子[/b][/color]  正在输入…");
        bubble.AddChild(body);
        row.AddChild(bubble);
        log.AddChild(row);
        typingBubble = bubble;
        ScrollLogToEnd();
    }

    private async void ScrollLogToEnd()
    {
        // Wait one frame so the container has laid out the new bubble before scrolling.
        var tree = (SceneTree)Engine.GetMainLoop();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (disposed || !GodotObject.IsInstanceValid(logScroll)) return;
        logScroll.ScrollVertical = (int)logScroll.GetVScrollBar().MaxValue;
    }

    private void HideTyping()
    {
        if (typingBubble != null && GodotObject.IsInstanceValid(typingBubble))
            (typingBubble.GetParent() as Godot.Node)?.QueueFree();
        typingBubble = null;
    }

    /// <summary>One-off greeting: mascot on the left, the line in a speech bubble.</summary>
    private static HBoxContainer Speech(string text, int mascot)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        if (UiStyle.Avatar is { } face)
            row.AddChild(new TextureRect
            {
                Texture = face, CustomMinimumSize = new Vector2(mascot, mascot),
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
            });
        var bubble = new PanelContainer();
        bubble.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.CardColor, new Color("EFD3A0"), 16, 14));
        bubble.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var label = new Label
        {
            Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.AddThemeFontSizeOverride("font_size", 19);
        bubble.AddChild(label);
        row.AddChild(bubble);
        return row;
    }
    public void Dispose()
    {
        disposed = true; requestNumber++; probe?.Cancel(); profiles.Dispose(); conversation.Dispose(); client.Dispose();
    }
}
