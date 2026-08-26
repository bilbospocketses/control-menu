namespace ControlMenu.Components.Shared;

/// <summary>
/// Holds a transient status/notification message that auto-dismisses after a delay, replacing the
/// per-component `SetStatus`/`ShowMessage` copies. A single CancellationTokenSource backs the
/// auto-dismiss timer and is cancelled on every <see cref="Show"/>, so a stale earlier timer can no
/// longer wipe a newer message — the bug the settings pages had (they started a Task.Delay with no
/// CTS to cancel). The component supplies an <paramref name="onChange"/> callback (typically
/// <c>() =&gt; InvokeAsync(StateHasChanged)</c>) so the auto-dismiss can trigger a re-render.
/// </summary>
public sealed class TransientNotice : IDisposable
{
    private readonly Func<Task> _onChange;
    private readonly TimeProvider _timeProvider;
    private CancellationTokenSource? _cts;

    /// <param name="onChange">Invoked after an auto-dismiss clears the message, so the owning
    /// component can re-render.</param>
    /// <param name="timeProvider">Clock backing the auto-dismiss delay. Defaults to
    /// <see cref="TimeProvider.System"/>; tests pass a fake clock so they can land assertions
    /// exactly on a dismiss deadline instead of racing real elapsed time.</param>
    public TransientNotice(Func<Task> onChange, TimeProvider? timeProvider = null)
    {
        _onChange = onChange;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string? Message { get; private set; }
    public string CssClass { get; private set; } = "";
    public string Icon { get; private set; } = "";
    public bool IsVisible => !string.IsNullOrEmpty(Message);

    public void Show(string message, string cssClass = "", string icon = "", int dismissMs = 5000)
    {
        Message = message;
        CssClass = cssClass;
        Icon = icon;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _ = DismissAfterAsync(dismissMs, token);
    }

    private async Task DismissAfterAsync(int dismissMs, CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(dismissMs), _timeProvider, token)
                      .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;   // a newer Show() or a Clear() superseded this timer
        }

        Message = null;
        CssClass = "";
        Icon = "";
        await _onChange().ConfigureAwait(false);
    }

    /// <summary>Clears the message immediately and cancels/disposes any pending auto-dismiss.</summary>
    public void Clear()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        Message = null;
        CssClass = "";
        Icon = "";
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
