namespace ControlMenu.Modules.AndroidDevices;

/// <summary>
/// Runs a dashboard action under a busy-flag guard: sets busy on, runs the action, then ALWAYS
/// clears busy in a finally — and never lets an exception escape. A throw from an adb/probe call
/// inside a Blazor event handler otherwise propagates as an unhandled circuit exception and tears
/// down the SignalR circuit. On failure <paramref name="onError"/> is invoked so the page can
/// surface a notice in place of the lost work (#10-rem).
/// </summary>
public static class DashboardGuard
{
    public static async Task RunAsync(Action<bool> setBusy, Func<Task> action, Action onError)
    {
        setBusy(true);
        try
        {
            await action();
        }
        catch
        {
            onError();
        }
        finally
        {
            setBusy(false);
        }
    }
}
