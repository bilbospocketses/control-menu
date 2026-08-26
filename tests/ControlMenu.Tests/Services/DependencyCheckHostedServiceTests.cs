using ControlMenu.Services;
using ControlMenu.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace ControlMenu.Tests.Services;

public class DependencyCheckHostedServiceTests
{
    private static (DependencyCheckHostedService Service, Mock<IDependencyManagerService> Manager, Task Checked, Task IntervalRead)
        Build(FakeTimeProvider time, string interval = "86400")
    {
        var checkCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var intervalRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var manager = new Mock<IDependencyManagerService>();
        manager.Setup(m => m.CheckAllAsync())
            .Callback(() => checkCalled.TrySetResult())
            .ReturnsAsync(Array.Empty<DependencyCheckResult>());

        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetSettingAsync("dep-check-interval", null))
            .Callback(() => intervalRead.TrySetResult())
            .ReturnsAsync(interval);

        var provider = new ServiceCollection()
            .AddScoped(_ => manager.Object)
            .AddScoped(_ => config.Object)
            .BuildServiceProvider();

        var service = new DependencyCheckHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DependencyCheckHostedService>.Instance,
            time);

        return (service, manager, checkCalled.Task, intervalRead.Task);
    }

    [Fact]
    public async Task ExecuteAsync_WaitsOutTheStartupDelay_ThenChecks()
    {
        var time = new FakeTimeProvider();
        var (service, manager, checkedOnce, _) = Build(time);

        await service.StartAsync(CancellationToken.None);

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
        var time = new FakeTimeProvider();
        var (service, manager, checkedOnce, intervalRead) = Build(time, interval: "3600");

        await service.StartAsync(CancellationToken.None);
        await AsyncSignal.SettleAsync();   // let ExecuteAsync arm the startup timer
        time.Advance(TimeSpan.FromSeconds(10));
        await AsyncSignal.ArrivesAsync(checkedOnce);
        manager.Verify(m => m.CheckAllAsync(), Times.Once);

        // The loop has read the interval, which is the last thing it does before arming the next
        // timer; settle so that timer is actually armed before we move the clock at it.
        await AsyncSignal.ArrivesAsync(intervalRead);
        await AsyncSignal.SettleAsync();

        // Half an hour into a one-hour interval: still exactly one check.
        time.Advance(TimeSpan.FromMinutes(30));
        await AsyncSignal.SettleAsync();
        manager.Verify(m => m.CheckAllAsync(), Times.Once);

        // The hour is up -- it runs again.
        time.Advance(TimeSpan.FromMinutes(30));
        await AsyncSignal.BecomesTrueAsync(
            () => manager.Invocations.Count(i => i.Method.Name == nameof(IDependencyManagerService.CheckAllAsync)) >= 2,
            "the check should repeat once the configured interval elapses");

        await service.StopAsync(CancellationToken.None);
    }
}
