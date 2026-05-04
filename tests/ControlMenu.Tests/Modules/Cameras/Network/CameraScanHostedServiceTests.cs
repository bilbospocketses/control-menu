using ControlMenu.Modules.Cameras.Network;
using ControlMenu.Services;
using ControlMenu.Services.Network;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Modules.Cameras.Network;

public class CameraScanHostedServiceTests
{
    private readonly Mock<ICameraScanService> _scanSvc = new();
    private readonly Mock<IConfigurationService> _config = new();
    private readonly Mock<ICameraSubnetDetector> _detect = new();

    [Fact]
    public async Task IntervalZero_DoesNotInvokeScan()
    {
        _config.Setup(c => c.GetSettingAsync("cameras-scan-interval-minutes", "cameras")).ReturnsAsync("0");
        _scanSvc.SetupGet(s => s.Phase).Returns(ScanPhase.Idle);

        var sut = new CameraScanHostedService(_scanSvc.Object, _config.Object, _detect.Object, NullLogger<CameraScanHostedService>.Instance);
        await sut.RunOneTickForTestsAsync(default);

        _scanSvc.Verify(s => s.StartScanAsync(It.IsAny<IReadOnlyList<ParsedSubnet>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ScanInProgress_SkipsTick()
    {
        _config.Setup(c => c.GetSettingAsync("cameras-scan-interval-minutes", "cameras")).ReturnsAsync("15");
        _scanSvc.SetupGet(s => s.Phase).Returns(ScanPhase.Scanning);

        var sut = new CameraScanHostedService(_scanSvc.Object, _config.Object, _detect.Object, NullLogger<CameraScanHostedService>.Instance);
        await sut.RunOneTickForTestsAsync(default);

        _scanSvc.Verify(s => s.StartScanAsync(It.IsAny<IReadOnlyList<ParsedSubnet>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
