using Bunit;
using ControlMenu.Components.Pages.HomeSections;
using ControlMenu.Modules.Cameras.Entities;
using ControlMenu.Modules.Cameras.Network;
using ControlMenu.Modules.Cameras.Services;
using ControlMenu.Services;
using ControlMenu.Services.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Components.HomeSections;

public class HomeDiscoveredCamerasTests : TestContext
{
    private readonly Mock<ICameraScanService> _scanService = new();
    private readonly Mock<IOnvifClient> _onvif = new();
    private readonly Mock<ICameraService> _cameraService = new();
    private readonly Mock<IHikvisionIsapiClient> _hik = new();
    private readonly Mock<ICameraChangeNotifier> _notifier = new();

    public HomeDiscoveredCamerasTests()
    {
        _scanService.Setup(s => s.Hits).Returns(Array.Empty<CameraScanHit>());
        _scanService.Setup(s => s.Phase).Returns(ScanPhase.Idle);
        _scanService.Setup(s => s.Subscribe(It.IsAny<Action<CameraScanEvent>>()))
            .Returns(Mock.Of<IDisposable>());
        _cameraService.Setup(s => s.GetAllAsync())
            .ReturnsAsync((IReadOnlyList<Camera>)new List<Camera>());

        Services.AddSingleton(_scanService.Object);
        Services.AddSingleton(_onvif.Object);
        Services.AddSingleton(_cameraService.Object);
        Services.AddSingleton(_hik.Object);
        Services.AddSingleton(_notifier.Object);
        Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    [Fact]
    public void EmptyCold_NoScanRun_RendersSectionWithPlaceholder()
    {
        var cut = RenderComponent<HomeDiscoveredCameras>(p => p
            .Add(c => c.HasScanned, false));

        // Section now ALWAYS renders; empty cold state shows the outlined placeholder card.
        Assert.Single(cut.FindAll(".home-disc-section"));
        Assert.Single(cut.FindAll(".home-disc-placeholder"));
        Assert.Contains("No new cameras discovered", cut.Markup);
        Assert.Contains("Run a scan to look for cameras on your network", cut.Markup);
    }

    [Fact]
    public void PostScan_EmptyHits_RendersHeaderAndPlaceholder()
    {
        var cut = RenderComponent<HomeDiscoveredCameras>(p => p
            .Add(c => c.HasScanned, true));

        Assert.Single(cut.FindAll(".home-disc-section"));
        Assert.Contains("DISCOVERED — CAMERAS", cut.Markup);
        // User feedback overrides earlier spec amendment: red count badge is required.
        var badge = cut.Find(".home-disc-count-cameras");
        Assert.Equal("0", badge.TextContent);
        // Empty case shows the outlined placeholder card, not the embedded panel.
        Assert.Single(cut.FindAll(".home-disc-placeholder"));
        Assert.Contains("No new cameras discovered", cut.Markup);
    }

    [Fact]
    public void PostScan_BadgeReflectsHitsCount()
    {
        var hits = new[]
        {
            new CameraScanHit("10.0.0.1", 80, true, null, null, "http://10.0.0.1/onvif"),
            new CameraScanHit("10.0.0.2", 80, true, null, null, "http://10.0.0.2/onvif"),
            new CameraScanHit("10.0.0.3", 554, false, null, null, null),
        };
        _scanService.Setup(s => s.Hits).Returns(hits);

        var cut = RenderComponent<HomeDiscoveredCameras>(p => p
            .Add(c => c.HasScanned, true));

        var badge = cut.Find(".home-disc-count-cameras");
        Assert.Equal("3", badge.TextContent);
    }
}
