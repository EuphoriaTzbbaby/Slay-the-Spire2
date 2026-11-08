using Godot;
using SpireCompanion.Core;

namespace SpireCompanion;

internal sealed partial class ChatPanel
{
    private async Task<T> CommandOnMainAsync<T>(int number, Func<T> action, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = timeout.Token.Register(() => completion.TrySetCanceled(timeout.Token));
        onMain(() =>
        {
            if (completion.Task.IsCompleted) return;
            if (disposed || number != requestNumber) { completion.TrySetCanceled(); return; }
            try { timeout.Token.ThrowIfCancellationRequested(); completion.TrySetResult(action()); }
            catch (OperationCanceledException) { completion.TrySetCanceled(); }
            catch (Exception error) { completion.TrySetException(error); }
        });
        return await completion.Task.ConfigureAwait(false);
    }

    private async Task<ActionOutcome> ExecuteRoomCommandAsync(int number, RoomCommand command, Action<string> record, CancellationToken token)
    {
        bool attempted = false;
        void Record(string text) { attempted = true; record(text); }
        try
        {
            var result = await CommandOnMainAsync(number, () =>
            {
                var outcome = GameReader.ExecuteCommand(command, Record, token);
                Append("操作", outcome.Message);
                var targets = command.Snapshot.Room!.Options.Where(option => command.Ids.Contains(option.Id));
                GD.Print($"[SpireCompanion] command_result kind={command.Snapshot.Room.Kind} targets={string.Join(";", targets.Select(option => option.Name + " [" + option.Detail + "]"))} submitted={outcome.Submitted}");
                return outcome;
            }, token).ConfigureAwait(false);
            if (result.Submitted && command.Ids[0].StartsWith("ui:", StringComparison.Ordinal))
            {
                var starting = command.Snapshot.Room!.Title == "NCharacterSelectScreen" &&
                    command.Snapshot.Room.Options.Any(option => option.Id == command.Ids[0] && option.Detail.StartsWith("NConfirmButton ·"));
                for (int i = 0; i < (starting ? 100 : 8); i++)
                {
                    await Task.Delay(100, token).ConfigureAwait(false);
                    var changed = await CommandOnMainAsync(number, () =>
                    {
                        var current = GameReader.ReadCommandSnapshot();
                        return starting ? current.Room?.Id != command.Snapshot.Room.Id : current.Fingerprint != command.Snapshot.Fingerprint;
                    }, token).ConfigureAwait(false);
                    if (changed)
                    {
                        var text = "已点击「" + command.Snapshot.Room!.Options.First(option => option.Id == command.Ids[0]).Name + "」，点击后游戏界面已更新。";
                        Record(text);
                        return new(true, text);
                    }
                }
                var unconfirmed = "已发送点击，但尚未观察到界面变化；不能确认操作成功，请查看游戏画面。";
                Record(unconfirmed);
                return new(true, unconfirmed);
            }
            if (!result.Submitted || command.Snapshot.Room?.Kind != "Select" || command.Ids[0] == "confirm") return result;
            var clicked = new HashSet<ulong>();
            for (int step = 0; step < 15; step++)
            {
                await Task.Delay(100, token).ConfigureAwait(false);
                var status = await CommandOnMainAsync(number, () => GameReader.ConfirmCommandSelection(command, clicked), token).ConfigureAwait(false);
                if (status.Length > 0)
                {
                    Record(status);
                    return new(true, status);
                }
            }
            var message = clicked.Count > 0 ? "已提交选牌确认；若界面仍有确认按钮，请在游戏内完成。"
                : "已选中卡牌，但确认尚不可用，请在游戏内检查；不会继续自动点击。";
            Record(message);
            return new(true, message);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            var message = attempted ? "操作结果尚未确认，已停止后续点击，请检查游戏画面。" : "操作未执行，请根据当前界面重试。";
            if (attempted) Record(message);
            GD.PushWarning($"[SpireCompanion] command_failed {error.GetType().Name}");
            return new(attempted, message);
        }
    }
}
