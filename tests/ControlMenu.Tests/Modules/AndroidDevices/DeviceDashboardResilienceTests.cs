using System.Net.Http;
using Bunit;
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Modules.AndroidDevices.Pages;
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Modules.AndroidDevices;

/// <summary>
/// Circuit-resilience tests for the shared <see cref="DeviceDashboard"/> (#10-rem): a failing
/// adb call during the unbidden OnInitializedAsync connect path must degrade to "Disconnected"
/// rather than throw out of the render and tear down the SignalR circuit.
/// </summary>
public class DeviceDashboardResilienceTests : BunitContext
{
    private readonly Mock<IDeviceService> _devices = new();
    private readonly Mock<IAdbService> _adb = new();
    private readonly Mock<IConfigurationService> _config = new();

    public DeviceDashboardResilienceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_devices.Object);
        Services.AddSingleton(_adb.Object);
        Services.AddSingleton(_config.Object);
        Services.AddSingleton(new Mock<INetworkDiscoveryService>().Object);
        Services.AddSingleton(new Mock<IDeviceChangeNotifier>().Object);
        Services.AddSingleton(new Mock<IScrcpyProbeService>().Object);
        // ScrcpyMirror child resolves WsScrcpyService; a non-started instance reports IsRunning=false
        // so it renders its "unavailable" alert (no iframe / focus interop) — enough for these tests.
        Services.AddSingleton(new WsScrcpyService(
            Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpClientFactory>(), NullLogger<WsScrcpyService>.Instance));
    }

    [Fact]
    public void Init_when_adb_connect_throws_degrades_to_disconnected_without_tearing_down_circuit()
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Type = DeviceType.AndroidPhone,
            ModuleId = "android-devices",
            Name = "Pixel",
            MacAddress = "",
            LastKnownIp = "1.2.3.4",
            AdbPort = 5555,
        };
        _devices.Setup(s => s.GetAllDevicesAsync()).ReturnsAsync(new[] { device });
        _config.Setup(c => c.GetSecretAsync(It.IsAny<string>())).ReturnsAsync((string?)null);
        _adb.Setup(a => a.ConnectAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("adb unreachable"));

        var cut = Render<DeviceDashboard>(p => p
            .Add(d => d.Title, "Android Phone")
            .Add(d => d.Icon, "bi-phone")
            .Add(d => d.DeviceType, DeviceType.AndroidPhone)
            .Add(d => d.DeviceKind, "phone"));

        // Reached render without an unhandled exception, and the failed connect shows as disconnected.
        Assert.Contains("Disconnected", cut.Markup);
    }
}
