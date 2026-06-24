namespace ControlMenu.Services;

/// <summary>
/// Hot-reload signal for background services whose tick cadence is driven by a user-editable
/// config setting. Settings UI calls <see cref="Trigger"/> after saving; the hosted service awaits
/// <see cref="WaitAsync"/> alongside its outer Task.Delay so the next tick re-reads config without
/// waiting out the remainder of the current delay.
///
/// A <see cref="Trigger"/> that arrives while the consumer is mid-tick — i.e. between two
/// <see cref="WaitAsync"/> calls, so no waiter is parked — is LATCHED, so the next
/// <see cref="WaitAsync"/> returns immediately instead of dropping the signal (the lost-wakeup the
/// previous "remove the registered TCS or no-op" design had). One consumer per key.
/// </summary>
public sealed class IntervalChangeSignal
{
    public const string CameraLiveness = "cameras-liveness";
    public const string AndroidLiveness = "android-liveness";

    private readonly object _gate = new();
    private readonly Dictionary<string, TaskCompletionSource> _waiters = new();
    private readonly HashSet<string> _pending = new();

    public void Trigger(string key)
    {
        lock (_gate)
        {
            if (_waiters.Remove(key, out var tcs))
                tcs.TrySetResult();   // a waiter is parked → wake it
            else
                _pending.Add(key);    // no waiter yet → latch so the next WaitAsync returns at once
        }
    }

    public Task WaitAsync(string key)
    {
        lock (_gate)
        {
            if (_pending.Remove(key))
                return Task.CompletedTask;   // consume a latched trigger — no wait

            if (!_waiters.TryGetValue(key, out var tcs))
            {
                tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters[key] = tcs;
            }
            return tcs.Task;
        }
    }
}
