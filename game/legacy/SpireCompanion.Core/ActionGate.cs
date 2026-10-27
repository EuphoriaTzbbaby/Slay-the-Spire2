namespace SpireCompanion.Core;

public readonly record struct ActionTicket(long Epoch, string Fingerprint);
public sealed record ActionOutcome(bool Submitted, string Message);

/// <summary>Only call on the game thread. One submission per observed state; pause invalidates old work.</summary>
public sealed class ActionGate
{
    public bool Paused { get; private set; } = true;
    public long Epoch { get; private set; }
    private string? submittedState;
    private bool dispatching;

    public void Pause() { Paused = true; Epoch++; }
    public void Resume() { Paused = false; Epoch++; }
    public ActionTicket Capture(string fingerprint) => new(Epoch, fingerprint);

    public ActionOutcome Execute(ActionTicket ticket, string currentFingerprint, bool legal, Action dispatch)
    {
        if (Paused || ticket.Epoch != Epoch) return new(false, "已暂停或控制请求过期");
        if (ticket.Fingerprint != currentFingerprint) return new(false, "游戏状态已变化，请刷新后重试");
        if (!legal) return new(false, "当前不允许结束回合");
        if (dispatching || submittedState == currentFingerprint) return new(false, "该状态已有动作提交，不重复执行");
        // Reserve before dispatch: an uncertain outcome must never be blindly retried.
        submittedState = currentFingerprint;
        dispatching = true;
        try { dispatch(); return new(true, "已提交结束回合，等待游戏结算"); }
        catch { Pause(); throw; }
        finally { dispatching = false; }
    }
}
