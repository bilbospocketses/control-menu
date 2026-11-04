using ControlMenu.Services;
using ControlMenu.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Services;

public class DependencyCheckHostedServiceTests
{
    private static (DependencyCheckHostedService Service, Mock<IDependencyManagerService> Manager, Task Checked, Task CheckedTwice)
        Build(TimerCountingTimeProvider time, string interval = "86400")
    {
        var checkCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkCalledTwice = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checks = 0;

        var manager = new Mock<IDependencyManagerService>();
        manager.Setup(m => m.CheckAllAsync())
            .Callback(() =>
            {
                checkCalled.TrySetResult();
                if (Interlocked.Increment(ref checks) >= 2) checkCalledTwice.TrySetResult();
            })
            .ReturnsAsync(Array.Empty<DependencyCheckResult>());

        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetSettingAsync("dep-check-interval", null))
            .ReturnsAsync(interval);

        var provider = new ServiceCollection()
            .AddScoped(_ => manager.Object)
            .AddScoped(_ => config.Object)
            .BuildServiceProvider();

        var service = new DependencyCheckHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DependencyCheckHostedService>.Instance,
            time);

        return (service, manager, checkCalled.Task, checkCalledTwice.Task);
    }

    [Fact]
    public async Task ExecuteAsync_WaitsOutTheStartupDelay_ThenChecks()
    {
        var time = new TimerCountingTimeProvider();
        var (service, manager, checkedOnce, _) = Build(time);

        await service.StartAsync(CancellationToken.None);
        // ExecuteAsync runs off the caller's thread, so the startup timer may not exist yet. An
        // advance made before it is armed never reaches it, and the test would hang.
        await time.TimersArmedAsync(1);

        // The 10s settle window exists so the check does not fight app startup for resources.
        // Asserting it is respected is the half the old test could not express: it slept 12 real
        // seconds and only ever proved "called eventually".
        await AsyncSignal.NeverArrivesAsync(
            checkedOnce, "the dependency check must wait out the 10s startup delay");

        time.Advance(TimeSpan.FromSeconds(10));
        await AsyncSignal.ArrivesAsync(checkedOnce);

        await service.StopAsync(CancellationToken.None);
        manager.Verify(m => m.CheckAllAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_RechecksOnTheConfiguredInterval()
    {
        var time = new TimerCountingTimeProvider();
        var (service, manager, checkedOnce, checkedTwice) = Build(time, interval: "3600");

        await service.StartAsync(CancellationToken.None);
        await time.TimersArmedAsync(1);   // the startup timer
        time.Advance(TimeSpan.FromSeconds(10));
        await AsyncSignal.ArrivesAsync(checkedOnce);
        manager.Verify(m => m.CheckAllAsync(), Times.Once);

        // The interval timer is the last thing a cycle arms; move the clock only once it exists.
        await time.TimersArmedAsync(2);

        // Half an hour into a one-hour interval: still exactly one check.
        time.Advance(TimeSpan.FromMinutes(30));
        await AsyncSignal.NeverArrivesAsync(
            checkedTwice, "the check must not repeat before the configured interval elapses");

        // The hour is up -- it runs again.
        time.Advance(TimeSpan.FromMinutes(30));
        await AsyncSignal.ArrivesAsync(checkedTwice);

        await service.StopAsync(CancellationToken.None);
    }
}
