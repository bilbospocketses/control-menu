using ControlMenu.Modules.Cameras.Network;
using ControlMenu.Services;
using ControlMenu.Services.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Modules.Cameras.Network;

public class CameraScanHostedServiceTests
{
    private readonly Mock<ICameraScanService> _scanSvc = new();
    private readonly Mock<IConfigurationService> _config = new();
    private readonly Mock<ICameraSubnetDetector> _detect = new();

    private IServiceScopeFactory BuildScopeFactory()
    {
        var sp = new Mock<IServiceProvider>();
        sp.Setup(p => p.GetService(typeof(IConfigurationService))).Returns(_config.Object);

        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(sp.Object);

        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }

    [Fact]
    public async Task IntervalZero_DoesNotInvokeScan()
    {
        _config.Setup(c => c.GetSettingAsync("cameras-scan-interval-minutes", "cameras")).ReturnsAsync("0");
        _scanSvc.SetupGet(s => s.Phase).Returns(ScanPhase.Idle);

        var sut = new CameraScanHostedService(_scanSvc.Object, BuildScopeFactory(), _detect.Object, NullLogger<CameraScanHostedService>.Instance);
        await sut.RunOneTickForTestsAsync(default);

        _scanSvc.Verify(s => s.StartScanAsync(It.IsAny<IReadOnlyList<ParsedSubnet>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ScanInProgress_SkipsTick()
    {
        _config.Setup(c => c.GetSettingAsync("cameras-scan-interval-minutes", "cameras")).ReturnsAsync("15");
        _scanSvc.SetupGet(s => s.Phase).Returns(ScanPhase.Scanning);

        var sut = new CameraScanHostedService(_scanSvc.Object, BuildScopeFactory(), _detect.Object, NullLogger<CameraScanHostedService>.Instance);
        await sut.RunOneTickForTestsAsync(default);

        _scanSvc.Verify(s => s.StartScanAsync(It.IsAny<IReadOnlyList<ParsedSubnet>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
