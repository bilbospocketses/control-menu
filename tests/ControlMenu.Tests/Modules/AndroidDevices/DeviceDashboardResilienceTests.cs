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
    private readonly Mock<INetworkDiscoveryService> _net = new();

    public DeviceDashboardResilienceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_devices.Object);
        Services.AddSingleton(_adb.Object);
        Services.AddSingleton(_config.Object);
        Services.AddSingleton(_net.Object);
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

    [Fact]
    public void Connect_action_when_it_throws_shows_error_notice_and_does_not_tear_down_circuit()
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Type = DeviceType.AndroidPhone,
            ModuleId = "android-devices",
            Name = "Pixel",
            MacAddress = "aa:bb:cc",
            LastKnownIp = null,
            AdbPort = 5555,
        };
        _devices.Setup(s => s.GetAllDevicesAsync()).ReturnsAsync(new[] { device });
        _config.Setup(c => c.GetSecretAsync(It.IsAny<string>())).ReturnsAsync((string?)null);
        // Drive the Connect handler down a throwing path; the guard must catch it.
        _net.Setup(n => n.ResolveIpFromMacAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("network down"));

        var cut = Render<DeviceDashboard>(p => p
            .Add(d => d.Title, "Android Phone")
            .Add(d => d.Icon, "bi-phone")
            .Add(d => d.DeviceType, DeviceType.AndroidPhone)
            .Add(d => d.DeviceKind, "phone"));

        var connect = cut.FindAll("button").First(b => b.TextContent.Trim() == "Connect");
        var ex = Record.Exception(() => connect.Click());

        Assert.Null(ex);                                    // guard swallowed the throw; circuit intact
        Assert.Contains("Connection failed.", cut.Markup);  // and surfaced the error notice
    }
}
