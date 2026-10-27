using Godot;
using SpireCompanion.Core;
using System.Security.Cryptography;
using System.Text.Json;

namespace SpireCompanion;

internal sealed class LocalProfilePanel : IDisposable
{
    private sealed record Preference(Guid Id, bool AutoUnlock);
    private sealed record Opened(EncryptedProfileStore.Session Session, string? Warning);
    private readonly string root = ProjectSettings.GlobalizePath("user://spire-companion");
    private readonly EncryptedProfileStore store;
    private readonly Action<Action> onMain;
    private readonly Action<EncryptedProfileStore.Session?> changed;
    private readonly OptionButton selector = new();
    private readonly LineEdit name = new() { PlaceholderText = "创建档案的名字，例如 小马" };
    private readonly LineEdit password = new() { PlaceholderText = "创建或手动解锁密码（至少 8 字符）", Secret = true };
    private readonly CheckButton automatic = new() { Text = "在本机自动解锁（系统只保存解密凭据）", ButtonPressed = true };
    private readonly Button create = new() { Text = "创建本地档案" };
    private readonly Button unlock = new() { Text = "解锁 / 切换" };
    private readonly Button lockButton = new() { Text = "锁定档案" };
    private readonly Label status = new() { Text = "未选择本地档案；可先进行会话聊天。", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private LocalProfileInfo[] profiles = [];
    private bool working, chatBusy, disposed;
    public EncryptedProfileStore.Session? Current { get; private set; }
    public bool Busy => working;
    public string SettingsPath => Current == null ? Path.Combine(root, "chat-settings.json") :
        Path.Combine(root, "profiles", Current.Profile.Id.ToString("N") + ".settings.json");

    public LocalProfilePanel(VBoxContainer parent, Action<Action> onMain, Action<EncryptedProfileStore.Session?> changed)
    {
        this.onMain=onMain; this.changed=changed; store=new(Path.Combine(root,"profiles"));
        parent.AddChild(new Label { Text="本地账户档案（云端登录后续接入）" });
        parent.AddChild(selector); parent.AddChild(name); parent.AddChild(password); parent.AddChild(automatic);
        var buttons=new HBoxContainer();buttons.AddChild(create);buttons.AddChild(unlock);buttons.AddChild(lockButton);parent.AddChild(buttons);parent.AddChild(status);
        create.Pressed += () =>
        {
            var display=name.Text;var secret=password.Text;bool auto=automatic.ButtonPressed;
            Start(() => FinishOpen(store.Create(display,secret),auto));
        };
        unlock.Pressed += () =>
        {
            if(selector.Selected<0 || selector.Selected>=profiles.Length)return;
            var selected=profiles[selector.Selected];var secret=password.Text;bool auto=automatic.ButtonPressed;
            Start(() => Open(selected.Id,secret,auto));
        };
        lockButton.Pressed += () =>
        {
            if(working || chatBusy)return;
            Current?.Dispose();Current=null;password.Text="";changed(null);status.Text="已锁定；本机自动解锁凭据保留。";
        };
        automatic.Toggled += _ => { if(Current!=null)status.Text="点击解锁 / 切换以应用自动解锁选项。"; };
        Reload();
        try
        {
            var path=Path.Combine(root,"last-profile.json");
            var last=File.Exists(path)?JsonSerializer.Deserialize<Preference>(File.ReadAllText(path)):null;
            if(last!=null && profiles.Any(p=>p.Id==last.Id))
            {
                selector.Select(Array.FindIndex(profiles,p=>p.Id==last.Id));automatic.ButtonPressed=last.AutoUnlock;
                if(last.AutoUnlock)Start(()=>Open(last.Id,"",true));
            }
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException)
        {status.Text="读取上次档案记录失败，可以手动选择档案。";}
    }
    private Opened Open(Guid id,string secret,bool auto)
    {
        EncryptedProfileStore.Session session;
        if(!string.IsNullOrEmpty(secret))session=store.Unlock(id,secret);
        else
        {
            var dataKey=ModelSecretStore.LoadUnlockKey(id) ?? throw new ProfileVaultException("没有自动解锁凭据，请输入档案密码。");
            try {session=store.UnlockWithKey(id,dataKey);}finally{CryptographicOperations.ZeroMemory(dataKey);}
        }
        return FinishOpen(session,auto);
    }
    private static Opened FinishOpen(EncryptedProfileStore.Session session,bool auto)
    {
        string? warning=null;
        try
        {
            if(auto)
            {
                var key=session.ExportAutoUnlockKey();
                try{ModelSecretStore.SaveUnlockKey(session.Profile.Id,key);}finally{CryptographicOperations.ZeroMemory(key);}
            }
            else ModelSecretStore.DeleteUnlockKey(session.Profile.Id);
        }
        catch(Exception) {warning="本地档案已解锁，但自动解锁凭据未保存或删除成功，请检查系统授权。";}
        return new(session,warning);
    }
    private void Start(Func<Opened> operation)
    {
        if(disposed || working || chatBusy)return;
        working=true;UpdateButtons();status.Text="正在处理本地档案…";
        _=Task.Run(() =>
        {
            Opened result;
            try{result=operation();}
            catch(Exception error)
            {
                var message=error is ProfileVaultException or ArgumentException ? error.Message : "档案操作失败，请检查文件权限或系统授权。";
                onMain(()=>{if(!disposed){working=false;password.Text="";status.Text=message;UpdateButtons();}});return;
            }
            onMain(()=>
            {
                if(disposed){result.Session.Dispose();return;}
                try
                {
                    Current?.Dispose();Current=result.Session;password.Text="";
                    Directory.CreateDirectory(root);
                    // Auto-unlock is device-specific; this file has no password or API Key.
                    File.WriteAllText(Path.Combine(root,"last-profile.json"),JsonSerializer.Serialize(new Preference(Current.Profile.Id,automatic.ButtonPressed)));
                    Reload();selector.Select(Array.FindIndex(profiles,p=>p.Id==Current.Profile.Id));
                    changed(Current);status.Text=result.Warning ?? $"当前档案：{Current.Profile.Name}（已解锁）";
                }
                catch(Exception){status.Text="档案已打开，但配置加载失败；请检查本地目录。";}
                finally{working=false;UpdateButtons();}
            });
        });
    }
    private void Reload()
    {
        try
        {
            profiles=store.ListProfiles();selector.Clear();
            // An empty option button renders as a black bar, so say what the empty state means.
            if(profiles.Length==0){selector.AddItem("尚未创建档案");selector.Select(0);status.Text="尚未创建本地档案：填写名字和至少 8 位密码后点击“创建本地档案”。";}
            else foreach(var p in profiles)selector.AddItem(p.Name);
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ProfileVaultException)
        {status.Text="档案列表读取失败，请检查本地档案目录。";}
    }
    public void SetChatBusy(bool value){chatBusy=value;UpdateButtons();}
    private void UpdateButtons()
    {
        bool block=working||chatBusy;
        create.Disabled=unlock.Disabled=lockButton.Disabled=automatic.Disabled=block;
        selector.Disabled=block||profiles.Length==0;
        name.Editable=password.Editable=!block;
    }
    public void Dispose(){disposed=true;Current?.Dispose();Current=null;}
}
