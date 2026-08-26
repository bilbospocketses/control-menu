using ControlMenu.Data.Entities;
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace ControlMenu.Tests.Modules.AndroidDevices.Services;

// Mirrors CameraLivenessHostedServiceTests. The TCP probe is a private TcpClient call (not
// injectable), so the responsive-device path isn't unit-testable here; these cover the interval
// gating and the empty path via the IDeviceService seam.
public class AndroidLivenessHostedServiceTests
{
    private readonly Mock<IConfigurationService> _config = new();
    private readonly FakeTimeProvider _time = new();
    private readonly Mock<IDeviceService> _deviceService = new();

    private IServiceScopeFactory BuildScopeFactory()
    {
        var sp = new Mock<IServiceProvider>();
        sp.Setup(p => p.GetService(typeof(IConfigurationService))).Returns(_config.Object);
        sp.Setup(p => p.GetService(typeof(IDeviceService))).Returns(_deviceService.Object);

        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(sp.Object);

        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }

    private AndroidLivenessHostedService CreateService() =>
        new(BuildScopeFactory(), new IntervalChangeSignal(), NullLogger<AndroidLivenessHostedService>.Instance, _time);

    [Fact]
    public async Task IntervalZero_DoesNotQueryDevices()
    {
        _config.Setup(c => c.GetSettingAsync("discovery-interval", null)).ReturnsAsync("0");

        await CreateService().RunOneTickForTestsAsync(default);

        _deviceService.Verify(s => s.GetAllDevicesAsync(), Times.Never);
    }

    [Fact]
    public async Task NoDevices_DoesNotUpdateLastSeen()
    {
        _config.Setup(c => c.GetSettingAsync("discovery-interval", null)).ReturnsAsync("300");
        _deviceService.Setup(s => s.GetAllDevicesAsync()).ReturnsAsync(new List<Device>());

        await CreateService().RunOneTickForTestsAsync(default);

        _deviceService.Verify(s => s.UpdateLastSeenAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TickInsideTheInterval_IsSkipped_AndRunsOnceItElapses()
    {
        // The interval gate had no coverage: _lastTickTicks starts at 0, so the very first tick
        // always clears it and every existing test only ever ran one tick. With the clock injected
        // the gate is assertable -- this is the behaviour that decides how often devices get probed.
        _config.Setup(c => c.GetSettingAsync("discovery-interval", null)).ReturnsAsync("300");
        _deviceService.Setup(s => s.GetAllDevicesAsync()).ReturnsAsync(new List<Device>());
        var svc = CreateService();

        await svc.RunOneTickForTestsAsync(default);
        _deviceService.Verify(s => s.GetAllDevicesAsync(), Times.Once);

        // One second short of the 300s interval: the device query must not repeat.
        _time.Advance(TimeSpan.FromSeconds(299));
        await svc.RunOneTickForTestsAsync(default);
        _deviceService.Verify(s => s.GetAllDevicesAsync(), Times.Once);

        // Interval elapsed: it runs again.
        _time.Advance(TimeSpan.FromSeconds(2));
        await svc.RunOneTickForTestsAsync(default);
        _deviceService.Verify(s => s.GetAllDevicesAsync(), Times.Exactly(2));
    }
}
