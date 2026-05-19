using System.Reflection;
using System.Runtime.CompilerServices;
using Bunit;
using ControlMenu.Components.Pages.HomeSections;
using ControlMenu.Data.Entities;
using ControlMenu.Modules;
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Modules.Cameras.Entities;
using ControlMenu.Modules.Cameras.Network;
using ControlMenu.Modules.Cameras.Services;
using ControlMenu.Services;
using ControlMenu.Services.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Components.HomeSections;

public class HomeIntegrationTests : BunitContext
{
    /// <summary>
    /// Bypass the reflection-based ModuleDiscoveryService constructor by using
    /// GetUninitializedObject + reflection to set the backing field directly.
    /// Mirrors the pattern in HomeModuleTilesTests / SidebarFlyoutTests.
    /// </summary>
    private static ModuleDiscoveryService MakeDiscovery(IEnumerable<IToolModule> modules)
    {
        var svc = (ModuleDiscoveryService)RuntimeHelpers.GetUninitializedObject(typeof(ModuleDiscoveryService));
        var field = typeof(ModuleDiscoveryService)
            .GetField("<Modules>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(svc, (IReadOnlyList<IToolModule>)modules.ToList());
        return svc;
    }

    private readonly Mock<IConfigurationService> _config = new();

    public HomeIntegrationTests()
    {
        _config.Setup(c => c.GetSettingAsync("setup-completed", null)).ReturnsAsync("true");
        Services.AddSingleton(_config.Object);

        Services.AddSingleton(new Mock<IAdbService>().Object);
        Services.AddSingleton(new Mock<IDeviceQuickScanService>().Object);
        var cameraScan = new Mock<ICameraScanService>();
        cameraScan.Setup(s => s.Hits).Returns(Array.Empty<CameraScanHit>());
        Services.AddSingleton(cameraScan.Object);
        Services.AddSingleton(new Mock<IDeviceChangeNotifier>().Object);
        Services.AddSingleton(new Mock<ICameraChangeNotifier>().Object);

        var deviceService = new Mock<IDeviceService>();
        deviceService.Setup(s => s.GetAllDevicesAsync())
            .ReturnsAsync((IReadOnlyList<Device>)new List<Device>());
        Services.AddSingleton(deviceService.Object);

        var cameraService = new Mock<ICameraService>();
        cameraService.Setup(s => s.GetAllAsync())
            .ReturnsAsync((IReadOnlyList<Camera>)new List<Camera>());
        Services.AddSingleton(cameraService.Object);

        Services.AddSingleton(new Mock<IOnvifClient>().Object);
        Services.AddSingleton(new Mock<IHikvisionIsapiClient>().Object);

        var handler = new Mock<IScanLifecycleHandler>();
        handler.Setup(h => h.Discovered).Returns(new List<DiscoveredDevice>());
        handler.Setup(h => h.Phase).Returns(ScanPhase.Idle);
        Services.AddSingleton(handler.Object);

        // SubnetDetectionClient is a sealed concrete class injected by Home.razor
        // for the cameras quick-scan path. Build a real instance from mocked deps;
        // tests never trigger the scan, so DetectAsync is never called.
        var httpFactory = new Mock<IHttpClientFactory>();
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var wsScrcpy = new WsScrcpyService(scopeFactory.Object, httpFactory.Object, NullLogger<WsScrcpyService>.Instance);
        Services.AddSingleton(new SubnetDetectionClient(httpFactory.Object, wsScrcpy));

        Services.AddSingleton(MakeDiscovery(Array.Empty<IToolModule>()));
        Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    [Fact]
    public void SetupComplete_RendersAllFourSections()
    {
        var cut = Render<ControlMenu.Components.Pages.Home>();

        Assert.Single(cut.FindAll(".home-header"));
        Assert.Single(cut.FindAll(".home-tiles-band"));
        Assert.NotNull(cut.FindComponent<HomeScanBand>());
        Assert.NotNull(cut.FindComponent<HomeModuleTiles>());
    }

    [Fact]
    public void SetupNotComplete_RendersSetupWizardOnly()
    {
        _config.Setup(c => c.GetSettingAsync("setup-completed", null))
            .ReturnsAsync((string?)null);

        var cut = Render<ControlMenu.Components.Pages.Home>();

        Assert.Empty(cut.FindAll(".home-header"));
    }

    [Fact]
    public void StatusLine_BeforeAnyScan_ShowsEmptyStateCopy()
    {
        var cut = Render<ControlMenu.Components.Pages.Home>();

        var status = cut.Find(".home-status").TextContent;
        Assert.Contains("Find devices and cameras", status);
    }

    [Fact]
    public void StatusLine_AfterScan_ShowsSpecFormatWithCounts()
    {
        // Arrange: replace the default services with ones that yield non-zero counts.
        Services.RemoveAll<IScanLifecycleHandler>();
        var handler = new Mock<IScanLifecycleHandler>();
        handler.Setup(h => h.Discovered).Returns(new List<DiscoveredDevice>
        {
            new("svc1", "10.0.0.1", 5555, "AA:BB:CC:DD:EE:01"),
            new("svc2", "10.0.0.2", 5555, "AA:BB:CC:DD:EE:02"),
        });
        handler.Setup(h => h.Phase).Returns(ScanPhase.Idle);
        Services.AddSingleton(handler.Object);

        Services.RemoveAll<ICameraScanService>();
        var scan = new Mock<ICameraScanService>();
        scan.Setup(s => s.Hits).Returns(new List<CameraScanHit>
        {
            new("10.0.0.10", 80, true, "vendor", "model", null),
            new("10.0.0.11", 80, true, "vendor", "model", null),
            new("10.0.0.12", 80, true, "vendor", "model", null),
        });
        Services.AddSingleton(scan.Object);

        Services.RemoveAll<IDeviceService>();
        var deviceService = new Mock<IDeviceService>();
        deviceService.Setup(s => s.GetAllDevicesAsync())
            .ReturnsAsync((IReadOnlyList<Device>)new List<Device>
            {
                new() { Name = "d1", MacAddress = "AA:BB:CC:DD:EE:01", ModuleId = "android" },
                new() { Name = "d2", MacAddress = "AA:BB:CC:DD:EE:02", ModuleId = "android" },
                new() { Name = "d3", MacAddress = "AA:BB:CC:DD:EE:03", ModuleId = "android" },
                new() { Name = "d4", MacAddress = "AA:BB:CC:DD:EE:04", ModuleId = "android" },
            });
        Services.AddSingleton(deviceService.Object);

        Services.RemoveAll<ICameraService>();
        var cameraService = new Mock<ICameraService>();
        cameraService.Setup(s => s.GetAllAsync())
            .ReturnsAsync((IReadOnlyList<Camera>)new List<Camera> { new() { Name = "c1", IpAddress = "10.0.0.20" } });
        Services.AddSingleton(cameraService.Object);

        var cut = Render<ControlMenu.Components.Pages.Home>();

        // Force the post-scan branch by setting _androidScanned via reflection
        // (the StatusLine getter switches branches on this flag).
        var instance = cut.Instance;
        typeof(ControlMenu.Components.Pages.Home)
            .GetField("_androidScanned", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(instance, true);
        cut.Render();

        var status = cut.Find(".home-status").TextContent;
        // N=2 Android discovered, M=3 Cameras discovered, 4 Android + 1 Camera registered
        Assert.Contains("2 Android", status);
        Assert.Contains("3 Cameras discovered", status);
        Assert.Contains("4 Androids and 1 Cameras registered", status);
    }

    [Fact]
    public async Task ScanCameras_WhenSubnetDetectionReturnsNull_ShowsErrorAndDoesNotMarkScanned()
    {
        // Arrange: replace SubnetDetectionClient with a mock returning null.
        Services.RemoveAll<SubnetDetectionClient>();
        var detector = new Mock<SubnetDetectionClient>(
            new Mock<IHttpClientFactory>().Object,
            new WsScrcpyService(new Mock<IServiceScopeFactory>().Object, new Mock<IHttpClientFactory>().Object, NullLogger<WsScrcpyService>.Instance));
        detector.Setup(d => d.DetectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DetectedSubnet?)null);
        Services.AddSingleton(detector.Object);

        var cut = Render<ControlMenu.Components.Pages.Home>();

        // Act: invoke the private ScanCamerasAsync method directly.
        var instance = cut.Instance;
        var method = typeof(ControlMenu.Components.Pages.Home)
            .GetMethod("ScanCamerasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(instance, null)!);

        // Assert: error rendered, _camerasScanned stayed false.
        var error = cut.Find(".home-error").TextContent;
        Assert.Contains("Could not auto-detect", error);

        var camerasScanned = (bool)typeof(ControlMenu.Components.Pages.Home)
            .GetField("_camerasScanned", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(instance)!;
        Assert.False(camerasScanned);
    }
}
