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
    private CancellationTokenSource? _cts;

    public TransientNotice(Func<Task> onChange) => _onChange = onChange;

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

        _ = Task.Delay(dismissMs, token).ContinueWith(async _ =>
        {
            Message = null;
            CssClass = "";
            Icon = "";
            await _onChange();
        }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    /// <summary>Clears the message immediately and cancels any pending auto-dismiss.</summary>
    public void Clear()
    {
        _cts?.Cancel();
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
