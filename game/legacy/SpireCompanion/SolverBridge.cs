using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using SpireCompanion.Core;

namespace SpireCompanion;

internal static class SolverBridge
{
    private static SolverContract? contract;
    private static bool owned;
    private static bool disabledByUs;
    private static string? incompatible;
    public static bool Owned => owned;
    private static SolverContract? Probe()
    {
        if (contract != null) return contract;
        if (incompatible != null) return null;
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "CombatSolver");
        if (assembly == null) return null;
        try
        {
            var type = assembly.GetType("CombatSolver.SolverController") ?? throw new NotSupportedException("CombatSolver 控制器不存在");
            return contract = new SolverContract(type);
        }
        catch (NotSupportedException error)
        {
            incompatible = $"CombatSolver {assembly.GetName().Version} 不兼容：{error.Message}";
            Godot.GD.PushWarning($"[SpireCompanion] {incompatible}");
            return null;
        }
    }
    public static SolverStatus Status() => Probe()?.Status() ?? new(false, false, false, false, false, incompatible ?? "尚未加载 CombatSolver");
    public static bool CanStart
    {
        get
        {
            var manager = RunManager.Instance;
            var run = manager.IsInProgress ? manager.DebugOnlyGetState() : null;
            var me = run != null ? LocalContext.GetMe(run) : null;
            return run?.Players.Count == 1 && me != null
                && me.Character.Id.Entry.Contains("IRONCLAD", StringComparison.OrdinalIgnoreCase)
                && CombatManager.Instance.IsInProgress && manager.NetService?.IsGameLoading != true
                && !AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "SpireBuddy" or "STS2AIAgent");
        }
    }
    public static void ObserveLifecycle()
    {
        // This product button takes over one fight, not all future fights.
        if (owned && !CombatManager.Instance.IsInProgress) StopOwned();
    }
    public static void Start()
    {
        if (!CanStart) throw new InvalidOperationException("需要单人铁甲战士战斗，且不能同时加载其他自动控制器");
        var solver = Probe() ?? throw new InvalidOperationException("CombatSolver 未加载");
        if (solver.Status().Disabled && !disabledByUs) throw new InvalidOperationException("求解器已被手动禁用，请先在其界面启用");
        var state = CombatManager.Instance.DebugOnlyGetState() ?? throw new InvalidOperationException("战斗状态未就绪");
        var host = NGame.Instance ?? throw new InvalidOperationException("游戏入口未就绪");
        owned = true; // Even a partially failed takeover must be cancellable.
        try
        {
            if (disabledByUs) { solver.SetEnabled(true); disabledByUs = false; }
            solver.Start(host, state);
        }
        catch
        {
            StopOwned();
            throw;
        }
    }
    public static void StopOwned()
    {
        if (!owned) return;
        var solver = Probe() ?? throw new InvalidOperationException("已接管的求解器消失，不能确认停止");
        solver.Stop();
        disabledByUs = true;
        owned = false;
    }
}
