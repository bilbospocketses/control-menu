using ControlMenu.Modules.Cameras.Entities;
using ControlMenu.Modules.Cameras.Network;
using ControlMenu.Modules.Cameras.Services;
using ControlMenu.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Modules.Cameras.Network;

public class CameraLivenessHostedServiceTests
{
    private readonly Mock<IConfigurationService> _config = new();
    private readonly Mock<ICameraService> _cameraService = new();
    private readonly Mock<IRtspProbeClient> _probe = new();

    private IServiceScopeFactory BuildScopeFactory()
    {
        var sp = new Mock<IServiceProvider>();
        sp.Setup(p => p.GetService(typeof(IConfigurationService))).Returns(_config.Object);
        sp.Setup(p => p.GetService(typeof(ICameraService))).Returns(_cameraService.Object);
        sp.Setup(p => p.GetService(typeof(IRtspProbeClient))).Returns(_probe.Object);

        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(sp.Object);

        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }

    private CameraLivenessHostedService CreateService() =>
        new(BuildScopeFactory(), new IntervalChangeSignal(), NullLogger<CameraLivenessHostedService>.Instance);

    [Fact]
    public async Task IntervalZero_DoesNotProbe()
    {
        _config.Setup(c => c.GetSettingAsync("cameras-liveness-interval-seconds", "cameras")).ReturnsAsync("0");

        await CreateService().RunOneTickForTestsAsync(default);

        _cameraService.Verify(s => s.GetEnabledAsync(), Times.Never);
        _probe.Verify(p => p.ProbeTcpAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResponsiveCamera_UpdatesLastSeen()
    {
        var camId = Guid.NewGuid();
        _config.Setup(c => c.GetSettingAsync("cameras-liveness-interval-seconds", "cameras")).ReturnsAsync("300");
        _cameraService.Setup(s => s.GetEnabledAsync()).ReturnsAsync(new List<Camera>
        {
            new() { Id = camId, Name = "Cam1", IpAddress = "192.168.1.10", Port = 554 }
        });
        _probe.Setup(p => p.ProbeTcpAsync("192.168.1.10", 554, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);

        await CreateService().RunOneTickForTestsAsync(default);

        _cameraService.Verify(s => s.UpdateLastSeenAsync(camId), Times.Once);
    }

    [Fact]
    public async Task UnresponsiveCamera_DoesNotUpdateLastSeen()
    {
        var camId = Guid.NewGuid();
        _config.Setup(c => c.GetSettingAsync("cameras-liveness-interval-seconds", "cameras")).ReturnsAsync("300");
        _cameraService.Setup(s => s.GetEnabledAsync()).ReturnsAsync(new List<Camera>
        {
            new() { Id = camId, Name = "Cam1", IpAddress = "192.168.1.10", Port = 554 }
        });
        _probe.Setup(p => p.ProbeTcpAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);

        await CreateService().RunOneTickForTestsAsync(default);

        _cameraService.Verify(s => s.UpdateLastSeenAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task NoCameras_NoProbeCalls()
    {
        _config.Setup(c => c.GetSettingAsync("cameras-liveness-interval-seconds", "cameras")).ReturnsAsync("300");
        _cameraService.Setup(s => s.GetEnabledAsync()).ReturnsAsync(new List<Camera>());

        await CreateService().RunOneTickForTestsAsync(default);

        _probe.Verify(p => p.ProbeTcpAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
