using SpireCompanion.Core;

if (args is ["--inspect-mod", var modPath])
{
    ModDependencyCheck.Run(modPath);
    return;
}

if (args is ["--credential-store-check"])
{
    var profile = Guid.NewGuid();
    var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
    try
    {
        SpireCompanion.ModelSecretStore.SaveUnlockKey(profile, key);
        var restored = SpireCompanion.ModelSecretStore.LoadUnlockKey(profile);
        try
        {
            if (restored == null || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(restored, key))
                throw new Exception("Auto-unlock key round trip failed");
        }
        finally { if (restored != null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(restored); }
        SpireCompanion.ModelSecretStore.DeleteUnlockKey(profile);
        if (SpireCompanion.ModelSecretStore.LoadUnlockKey(profile) != null) throw new Exception("Auto-unlock deletion failed");
        Console.WriteLine("PASS OS automatic-unlock credential save/read/delete; no API Key used.");
    }
    finally
    {
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
        SpireCompanion.ModelSecretStore.DeleteUnlockKey(profile);
    }
    return;
}

int passed = 0;
void Check(string name, Action test) { test(); Console.WriteLine($"PASS {name}"); passed++; }
void Require(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
Check("Default pause blocks dispatch", () => { var g = new ActionGate(); int n=0; Require(!g.Execute(g.Capture("s"),"s",true,()=>n++).Submitted && n==0); });
Check("Pause and resume invalidate in-flight work", () => { var g=new ActionGate();g.Resume();var t=g.Capture("s");g.Pause();g.Resume();Require(!g.Execute(t,"s",true,()=>throw new Exception()).Submitted); });
Check("Changed snapshot blocks stale request", () => {var g=new ActionGate();g.Resume();Require(!g.Execute(g.Capture("old"),"new",true,()=>throw new Exception()).Submitted);});
Check("Duplicate and reentrant submissions execute once", () => {var g=new ActionGate();g.Resume();var t=g.Capture("s");int n=0;Require(g.Execute(t,"s",true,()=>{n++;Require(!g.Execute(t,"s",true,()=>n++).Submitted);}).Submitted);Require(!g.Execute(t,"s",true,()=>n++).Submitted && n==1);});
Check("Illegal action never dispatches", () => {var g=new ActionGate();g.Resume();Require(!g.Execute(g.Capture("s"),"s",false,()=>throw new Exception()).Submitted);});
Check("Uncertain dispatch pauses and cannot retry same state", () => {var g=new ActionGate();g.Resume();int n=0;try{g.Execute(g.Capture("s"),"s",true,()=>{n++;throw new InvalidOperationException();});}catch(InvalidOperationException){}Require(g.Paused);g.Resume();Require(!g.Execute(g.Capture("s"),"s",true,()=>n++).Submitted && n==1);});
Check("Solver contract rejects missing cancellation capability", () => {
    bool rejected=false;try{_ = new SolverContract(typeof(UnsupportedSolver));}catch(NotSupportedException){rejected=true;}Require(rejected);
});
Check("Solver takeover must be confirmed", () => {
    FakeSolver.Reset(); var adapter=new SolverContract(typeof(FakeSolver));
    FakeSolver.Reject=true;bool rejected=false;
    try{adapter.Start(new object(),new object());}catch(InvalidOperationException){rejected=true;}
    Require(rejected);FakeSolver.Reject=false;adapter.Start(new object(),new object());Require(adapter.Status().FullAuto);
});
Check("Solver pause cancels work without persisting user settings", () => {
    FakeSolver.Reset();var adapter=new SolverContract(typeof(FakeSolver));adapter.Start(new object(),new object());adapter.Stop();
    var status=adapter.Status();Require(status.Disabled && !status.FullAuto && !status.Searching && !FakeSolver.Persisted);
    adapter.SetEnabled(true);Require(!adapter.Status().Disabled);
});
Check("Solver stop cannot claim success while deployment is still active", () => {
    FakeSolver.Reset();var adapter=new SolverContract(typeof(FakeSolver));adapter.Start(new object(),new object());FakeSolver.KeepDeploying=true;
    bool rejected=false;try{adapter.Stop();}catch(InvalidOperationException){rejected=true;}
    Require(rejected && adapter.Status().Deploying && adapter.Status().Description.Contains("正在停止"));
    FakeSolver.KeepDeploying=false;adapter.Stop();Require(!adapter.Status().Deploying);
});
Check("Room options are part of the snapshot fingerprint", () => {
    var room = new RoomView("Event", "金色神像", "一座神像。", [new RoomOptionView("0", "祈祷", "获得最大生命", false)]);
    var a = new GameSnapshot("EventRoom", "铁甲战士", 60, 80, 99, [], "无", false, "") { Room = room };
    var b = a with { Room = room with { Options = [new RoomOptionView("0", "祈祷", "失去最大生命", false)] } };
    Require(a.Fingerprint != b.Fingerprint);
    Require(a.Fingerprint == (a with { }).Fingerprint);
});
Check("Snapshot fingerprint excludes itself and detects combat changes", () => {
    var state=new GameSnapshot("combat","铁甲战士",70,80,100,["打击"],"1:Player",true,"")
        { Energy=3, Cards=[new CardView(0,"STRIKE","打击","1",false)] };
    var json=System.Text.Json.JsonSerializer.Serialize(state);
    Require(!json.Contains("Fingerprint"));
    var restored=System.Text.Json.JsonSerializer.Deserialize<GameSnapshot>(json)!;
    Require(state.Fingerprint==restored.Fingerprint);
    Require(state.Fingerprint!=(state with {Energy=2}).Fingerprint);
    Require(state.Fingerprint!=(state with {Enemies=[new EnemyView(0,"enemy","敌人",10,20,0)]}).Fingerprint);
});
passed += ChatChecks.RunAsync().GetAwaiter().GetResult();
passed += VaultChecks.Run();
Console.WriteLine($"{passed} checks passed. No game process or saves accessed.");

public static class UnsupportedSolver { }
public static class FakeSolver
{
    public static bool FullAutoEnabled {get;private set;}
    public static bool SolverDisabled {get;private set;}
    public static bool IsSearching {get;private set;}
    public static bool IsDeploying {get;private set;}
    public static bool KeepDeploying {get;set;}
    public static bool Reject {get;set;}
    public static bool Persisted {get;private set;}
    public static void Reset(){FullAutoEnabled=false;SolverDisabled=false;IsSearching=false;Reject=false;Persisted=false;IsDeploying=false;KeepDeploying=false;}
    public static void SetFullAuto(object host,object state,bool enabled){if(!Reject){FullAutoEnabled=enabled;IsSearching=enabled;IsDeploying=enabled;}}
    public static void SetSolverDisabled(bool disabled,bool persist){SolverDisabled=disabled;Persisted=persist;if(disabled){FullAutoEnabled=false;IsSearching=false;if(!KeepDeploying)IsDeploying=false;}}
}
