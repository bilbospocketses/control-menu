using Bunit;
using ControlMenu.Components.Pages.HomeSections;
using ControlMenu.Data.Entities;
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Services;
using ControlMenu.Services.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Components.HomeSections;

public class HomeDiscoveredAndroidTests : TestContext
{
    private readonly Mock<IScanLifecycleHandler> _handler = new();
    private readonly Mock<IDeviceChangeNotifier> _notifier = new();
    private readonly Mock<IDeviceService> _deviceService = new();
    private readonly Mock<IConfigurationService> _config = new();

    public HomeDiscoveredAndroidTests()
    {
        _handler.Setup(h => h.Discovered).Returns(new List<DiscoveredDevice>());
        _handler.Setup(h => h.Phase).Returns(ScanPhase.Idle);
        _deviceService.Setup(s => s.GetAllDevicesAsync())
            .ReturnsAsync((IReadOnlyList<Device>)new List<Device>());

        Services.AddSingleton(_handler.Object);
        Services.AddSingleton(_notifier.Object);
        Services.AddSingleton(_deviceService.Object);
        Services.AddSingleton(_config.Object);
        // DiscoveredPanelRow injects these — register loose mocks so the
        // populated render path doesn't blow up.
        Services.AddSingleton(new Mock<IAdbService>().Object);
        Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    [Fact]
    public void EmptyCold_NoScanRun_RendersNothing()
    {
        var cut = RenderComponent<HomeDiscoveredAndroid>(p => p
            .Add(c => c.HasScanned, false));

        Assert.Empty(cut.FindAll(".home-disc-section"));
    }

    [Fact]
    public void Populated_RendersSectionHeader_AndDelegatesToDiscoveredPanel()
    {
        var hits = new List<DiscoveredDevice>
        {
            new("Pixel 8", "192.168.1.42", 5555, "AA:BB:CC:DD:EE:01")
        };
        _handler.Setup(h => h.Discovered).Returns(hits);

        var cut = RenderComponent<HomeDiscoveredAndroid>(p => p
            .Add(c => c.HasScanned, true));

        Assert.Single(cut.FindAll(".home-disc-section"));
        Assert.Contains("DISCOVERED — ANDROID", cut.Markup);
        Assert.Contains("1", cut.Find(".home-disc-count-android").TextContent);
        // Delegated child renders at least one row
        Assert.NotEmpty(cut.FindAll("table"));
    }

    [Fact]
    public void EmptyPostScan_RendersHeaderAndEmptyMessage()
    {
        var cut = RenderComponent<HomeDiscoveredAndroid>(p => p
            .Add(c => c.HasScanned, true));

        Assert.Single(cut.FindAll(".home-disc-section"));
        Assert.Contains("No Android devices found", cut.Markup);
    }
}
