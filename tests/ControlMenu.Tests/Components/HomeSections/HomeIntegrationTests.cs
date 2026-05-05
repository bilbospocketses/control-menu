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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Components.HomeSections;

public class HomeIntegrationTests : TestContext
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
        Services.AddSingleton(new Mock<ICameraScanService>().Object);
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
        var wsScrcpy = new WsScrcpyService(scopeFactory.Object, NullLogger<WsScrcpyService>.Instance);
        Services.AddSingleton(new SubnetDetectionClient(httpFactory.Object, wsScrcpy));

        Services.AddSingleton(MakeDiscovery(Array.Empty<IToolModule>()));
        Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    [Fact]
    public void SetupComplete_RendersAllFourSections()
    {
        var cut = RenderComponent<ControlMenu.Components.Pages.Home>();

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

        var cut = RenderComponent<ControlMenu.Components.Pages.Home>();

        Assert.Empty(cut.FindAll(".home-header"));
    }
}
