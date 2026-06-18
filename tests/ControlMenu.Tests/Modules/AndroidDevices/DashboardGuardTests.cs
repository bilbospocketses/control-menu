using ControlMenu.Modules.AndroidDevices;

namespace ControlMenu.Tests.Modules.AndroidDevices;

/// <summary>
/// Unit tests for the dashboard action guard (#10-rem). The guard wraps an async dashboard
/// action so the busy flag is always cleared and an exception can never escape to tear down
/// the Blazor circuit — a throwing adb/probe call inside an event handler otherwise kills it.
/// </summary>
public class DashboardGuardTests
{
    [Fact]
    public async Task RunAsync_on_success_toggles_busy_on_then_off_and_skips_onError()
    {
        var busy = new List<bool>();
        var ran = false;
        var errored = false;

        await DashboardGuard.RunAsync(
            b => busy.Add(b),
            () => { ran = true; return Task.CompletedTask; },
            () => errored = true);

        Assert.True(ran);
        Assert.Equal(new[] { true, false }, busy);
        Assert.False(errored);
    }

    [Fact]
    public async Task RunAsync_when_action_throws_resets_busy_invokes_onError_and_does_not_propagate()
    {
        var busy = new List<bool>();
        var errored = false;

        await DashboardGuard.RunAsync(
            b => busy.Add(b),
            () => throw new InvalidOperationException("boom"),
            () => errored = true);

        Assert.Equal(new[] { true, false }, busy);
        Assert.True(errored);
    }
}
