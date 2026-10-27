using System.Reflection;

namespace SpireCompanion.Core;

public sealed record SolverStatus(bool Available, bool Disabled, bool FullAuto, bool Searching, bool Deploying, string Description);

/// <summary>Version-sensitive adapter. A missing member fails explicitly, never invents a status.</summary>
public sealed class SolverContract
{
    private readonly Type controller;
    private readonly MethodInfo setAuto;
    private readonly MethodInfo setDisabled;
    public SolverContract(Type controller)
    {
        this.controller = controller;
        foreach (var name in new[] { "FullAutoEnabled", "SolverDisabled", "IsSearching", "IsDeploying" })
            if (controller.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.PropertyType != typeof(bool))
                throw new NotSupportedException($"求解器缺少 bool 属性 {name}");
        var starts = controller.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "SetFullAuto" && m.ReturnType == typeof(void)
                && m.GetParameters().Length == 3 && m.GetParameters()[2].ParameterType == typeof(bool)).ToArray();
        if (starts.Length != 1) throw new NotSupportedException("求解器缺少唯一兼容的 SetFullAuto");
        setAuto = starts[0];
        setDisabled = controller.GetMethod("SetSolverDisabled", BindingFlags.Public | BindingFlags.Static, [typeof(bool), typeof(bool)])
            ?? throw new NotSupportedException("求解器缺少非持久化停止接口");
    }
    private bool Read(string name) => (bool)controller.GetProperty(name)!.GetValue(null)!;
    public SolverStatus Status()
    {
        var disabled = Read("SolverDisabled"); var fullAuto = Read("FullAutoEnabled"); var searching = Read("IsSearching");
        var deploying = Read("IsDeploying");
        return new(true, disabled, fullAuto, searching, deploying,
            disabled && (searching || deploying) ? "正在停止搜索或执行" :
            disabled ? "已禁用" : deploying ? "正在执行路线" : fullAuto ? "自动战斗中" : searching ? "搜索中" : "就绪");
    }
    public void SetEnabled(bool enabled) => setDisabled.Invoke(null, [!enabled, false]);
    public void Start(object host, object state)
    {
        setAuto.Invoke(null, [host, state, true]);
        if (!Status().FullAuto) throw new InvalidOperationException("求解器拒绝接管当前阶段");
    }
    public void Stop()
    {
        // SetFullAuto(false) alone does not promise cancellation of queued deployments.
        SetEnabled(false);
        var result = Status();
        if (!result.Disabled || result.FullAuto || result.Searching || result.Deploying) throw new InvalidOperationException("求解器停止状态未确认");
    }
}
