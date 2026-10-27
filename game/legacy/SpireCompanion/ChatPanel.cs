using Godot;
using SpireCompanion.Core;

namespace SpireCompanion;

internal sealed class ChatPanel : IDisposable
{
    private readonly ChatClient client = new();
    private readonly ChatConversation conversation;
    private readonly Action<Action> onMain;
    private readonly LocalProfilePanel profiles;
    private string settingsPath => profiles.SettingsPath;
    private readonly LineEdit endpoint = new() { PlaceholderText = "模型服务地址，例如 https://服务地址/v1" };
    private readonly LineEdit model = new() { PlaceholderText = "模型名称" };
    private readonly LineEdit key = new() { PlaceholderText = "API Key（加密保存在本地档案）", Secret = true };
    private readonly CheckButton remember = new() { Text = "记住 API Key（加密保存在当前档案）", ButtonPressed = true };
    private readonly Button forget = new() { Text = "删除已记住的密钥" };
    private string? keyOrigin;
    private readonly OptionButton mode = new();
    private readonly Label info = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly TextEdit transcript = new() { Editable = false, CustomMinimumSize = new Vector2(400, 170), WrapMode = TextEdit.LineWrappingMode.Boundary };
    private readonly LineEdit question = new() { PlaceholderText = "问问搭子，或点击分析当前局面", MaxLength = 4000 };
    private readonly Button test = new() { Text = "测试 API 连接" };
    private CancellationTokenSource? probe;
    private readonly Button send = new() { Text = "发送" };
    private readonly Button advice = new() { Text = "分析当前局面" };
    private readonly Button cancel = new() { Text = "取消回复", Disabled = true };
    private readonly Button save = new() { Text = "保存设置" };
    private bool busy;
    private bool cancelRequested;
    private bool disposed;
    private int requestNumber;
    private ChatSettings settings = new();

    public ChatPanel(VBoxContainer parent, Action<Action> onMain)
    {
        this.onMain = onMain;
        conversation = new ChatConversation(client);
        profiles = new LocalProfilePanel(parent, onMain, OnProfileChanged);
        parent.AddChild(new HSeparator());
        parent.AddChild(new Label { Text = "文字搭子 · 只提供建议，不自动操作" });
        parent.AddChild(new Label { Text = "API 地址（默认 DeepSeek，可修改）" });
        parent.AddChild(endpoint);
        parent.AddChild(new Label { Text = "模型名称（填写服务商提供的准确名称）" });
        parent.AddChild(model);
        parent.AddChild(new Label { Text = "API Key（可记住，下次启动自动加载）" });
        parent.AddChild(key);
        var reveal = new CheckButton { Text = "显示密钥" };
        reveal.Toggled += visible => key.Secret = !visible;
        parent.AddChild(reveal);
        parent.AddChild(remember); parent.AddChild(forget);
        parent.AddChild(new Label { Text = "API Key 保存在跨平台加密文件中，系统仅保管自动解锁凭据。", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        parent.AddChild(new Label { Text = "回复风格" });
        mode.AddItem("安静"); mode.AddItem("普通"); mode.AddItem("话多");
        mode.Select(1); parent.AddChild(mode);
        var configButtons = new HBoxContainer(); configButtons.AddChild(save); configButtons.AddChild(test); parent.AddChild(configButtons);
        parent.AddChild(new Label { Text = "测试发送简短请求；普通对话附带当前游戏状态，可能产生模型服务费用。", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        parent.AddChild(transcript); parent.AddChild(question);
        var buttons = new HBoxContainer();
        buttons.AddChild(send); buttons.AddChild(advice); buttons.AddChild(cancel);
        var clear = new Button { Text = "清空对话" }; buttons.AddChild(clear);
        parent.AddChild(buttons); parent.AddChild(info);
        send.Pressed += () => Begin(question.Text);
        question.TextSubmitted += Begin;
        advice.Pressed += () => Begin("请根据当前局面给出最值得关注的建议，先说结论，再简短解释。如果缺少关键状态请说明。");
        cancel.Pressed += Cancel;
        clear.Pressed += () =>
        {
            if (busy) { info.Text = "请先取消并等待当前回复结束。"; return; }
            conversation.Clear(); transcript.Text = ""; info.Text = "已清空本次会话。";
        };
        save.Pressed += Save;
        test.Pressed += TestConnection;
        try
        {
            settings = ChatSettings.Load(settingsPath);
            endpoint.Text = settings.Endpoint; model.Text = settings.Model;
            remember.ButtonPressed = settings.RememberApiKey;
            mode.Select(settings.TalkMode switch { "quiet" => 0, "chatty" => 2, _ => 1 });
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
        var importOld = new Button { Text = "导入此前保存的系统密钥（可选）" };
        importOld.Pressed += ImportLegacy;
        parent.AddChild(importOld);
        forget.Pressed += Forget;
        remember.Toggled += _ => { info.Text = "点击保存设置以应用密钥记忆选项。"; };
    }

    private void OnProfileChanged(EncryptedProfileStore.Session? profile)
    {
        conversation.Clear(); transcript.Text = ""; key.Text = ""; requestNumber++;
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
            onMain(() => { if (!disposed && number == requestNumber) info.Text = $"连接及模型调用成功（{elapsed.ElapsedMilliseconds} ms），现在可以发送消息。"; });
        }
        catch (OperationCanceledException)
        { onMain(() => { if (!disposed && number == requestNumber) info.Text = "连接测试已取消。"; }); }
        catch (Exception error) when (error is ModelReplyException or ArgumentException)
        { onMain(() => { if (!disposed && number == requestNumber) info.Text = error.Message; }); }
        catch (Exception)
        { onMain(() => { if (!disposed && number == requestNumber) info.Text = "连接测试失败，请核对配置。"; }); }
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
            if (next.RememberApiKey)
            {
                if (profiles.Current == null) throw new ProfileVaultException("请先创建或解锁本地档案，再保存 API Key。");
                profiles.Current.SaveApiKey(next.Endpoint, key.Text);
            }
            else profiles.Current?.DeleteApiKey(next.Endpoint);
            next.Save(settingsPath); settings = next;
            info.Text = next.RememberApiKey ? "设置已保存，API Key 已加密写入当前本地档案。" : "设置已保存，密钥仅保留在当前会话。";
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
            _ = CompleteAsync(number, next, apiKey, text, snapshot);
        }
        catch (ArgumentException error) { info.Text = error.Message; }
        catch (Exception) { info.Text = "当前状态暂不可读，请稍后再试。"; }
    }
    private async Task CompleteAsync(int number, ChatSettings config, string apiKey, string text, GameSnapshot snapshot)
    {
        try
        {
            var reply = await conversation.AskAsync(config, apiKey, text, snapshot).ConfigureAwait(false);
            onMain(() =>
            {
                if (disposed || number != requestNumber) return;
                bool changed;
                try { changed = GameReader.Read().Fingerprint != reply.Fingerprint; }
                catch { changed = true; }
                Append("搭子", reply.Text);
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
        { onMain(() => { if (!disposed) { if (cancelRequested) info.Text = "本次回复已取消。"; SetBusy(false); } }); }
    }
    public void Cancel()
    {
        if (!busy) return;
        cancelRequested = true;
        requestNumber++; // Suppress a reply already queued for the next game frame as well.
        conversation.Cancel(); probe?.Cancel(); info.Text = "正在取消请求…";
    }
    private void SetBusy(bool value)
    {
        if (value) cancelRequested = false;
        profiles.SetChatBusy(value);
        busy = value; send.Disabled = advice.Disabled = save.Disabled = test.Disabled = value; cancel.Disabled = !value;
        endpoint.Editable = model.Editable = key.Editable = !value; mode.Disabled = value; remember.Disabled = forget.Disabled = value;
    }
    private void Append(string who, string text)
    {
        transcript.Text += $"{who}：{text}\n\n";
        // Bounded visible history; model history is independently bounded by conversation pairs.
        if (transcript.Text.Length > 24_000) transcript.Text = transcript.Text[^24_000..];
        transcript.ScrollVertical = transcript.GetLineCount();
    }
    public void Dispose()
    {
        disposed = true; requestNumber++; probe?.Cancel(); profiles.Dispose(); conversation.Dispose(); client.Dispose();
    }
}
