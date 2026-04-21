using ControlMenu.Data.Entities;
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Services;
using ControlMenu.Services.Network;
using Moq;

namespace ControlMenu.Tests.Services;

public class ScanLifecycleHandlerTests
{
    private readonly FakeNetworkScanService _scan = new();
    private readonly Mock<IAdbService> _adb = new();
    private readonly Mock<INetworkDiscoveryService> _net = new();
    private readonly Mock<IConfigurationService> _config = new();
    private readonly Mock<IDeviceService> _devices = new();

    private ScanLifecycleHandler CreateHandler() =>
        new(_scan, _adb.Object, _net.Object, _config.Object, _devices.Object);

    [Fact]
    public void Constructor_SubscribesToScanService_AndSeedsPhase()
    {
        _scan.Phase = ScanPhase.Scanning;

        using var handler = CreateHandler();

        Assert.Equal(1, _scan.SubscriberCount);
        Assert.Equal(ScanPhase.Scanning, handler.Phase);
    }
}
