using Bunit;
using ControlMenu.Components.Pages.HomeSections;
using ControlMenu.Modules.Cameras.Entities;
using ControlMenu.Modules.Cameras.Network;
using ControlMenu.Modules.Cameras.Services;
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
        Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    [Fact]
    public void EmptyCold_NoScanRun_RendersNothing()
    {
        var cut = RenderComponent<HomeDiscoveredCameras>(p => p
            .Add(c => c.HasScanned, false));

        Assert.Empty(cut.FindAll(".home-disc-section"));
    }

    [Fact]
    public void PostScan_RendersHeader_AndDelegatesToDiscoveredCamerasPanel()
    {
        var cut = RenderComponent<HomeDiscoveredCameras>(p => p
            .Add(c => c.HasScanned, true));

        Assert.Single(cut.FindAll(".home-disc-section"));
        Assert.Contains("DISCOVERED — CAMERAS", cut.Markup);
        // Per spec amendment: no count badge on the cameras side.
        Assert.Empty(cut.FindAll(".home-disc-count"));
    }
}
