using System.Collections.Concurrent;

namespace ControlMenu.Services;

/// <summary>
/// Hot-reload signal for background services whose tick cadence is driven by a
/// user-editable config setting. Settings UI calls <see cref="Trigger"/> after
/// saving; the hosted service awaits <see cref="WaitAsync"/> alongside its
/// outer Task.Delay so the next tick re-reads config without waiting out the
/// remainder of the current delay.
/// </summary>
public sealed class IntervalChangeSignal
{
    public const string CameraLiveness = "cameras-liveness";
    public const string AndroidLiveness = "android-liveness";

    private readonly ConcurrentDictionary<string, TaskCompletionSource> _sources = new();

    public void Trigger(string key)
    {
        if (_sources.TryRemove(key, out var tcs))
            tcs.TrySetResult();
    }

    public Task WaitAsync(string key) =>
        _sources.GetOrAdd(key, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
}
