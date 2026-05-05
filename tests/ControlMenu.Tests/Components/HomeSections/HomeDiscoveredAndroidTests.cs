using Bunit;
using ControlMenu.Components.Pages.HomeSections;
using ControlMenu.Data.Entities;
using ControlMenu.Services;
using ControlMenu.Services.Network;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ControlMenu.Tests.Components.HomeSections;

public class HomeDiscoveredAndroidTests : TestContext
{
    private readonly Mock<IScanLifecycleHandler> _handler = new();
    private readonly Mock<IDeviceChangeNotifier> _notifier = new();
    private readonly Mock<IDeviceService> _deviceService = new();

    public HomeDiscoveredAndroidTests()
    {
        _handler.Setup(h => h.Discovered).Returns(new List<DiscoveredDevice>());
        _handler.Setup(h => h.Phase).Returns(ScanPhase.Idle);
        _deviceService.Setup(s => s.GetAllDevicesAsync())
            .ReturnsAsync((IReadOnlyList<Device>)new List<Device>());

        Services.AddSingleton(_handler.Object);
        Services.AddSingleton(_notifier.Object);
        Services.AddSingleton(_deviceService.Object);
    }

    [Fact]
    public void EmptyCold_NoScanRun_RendersNothing()
    {
        var cut = RenderComponent<HomeDiscoveredAndroid>(p => p
            .Add(c => c.HasScanned, false));

        Assert.Empty(cut.FindAll(".home-disc-section"));
    }
}
