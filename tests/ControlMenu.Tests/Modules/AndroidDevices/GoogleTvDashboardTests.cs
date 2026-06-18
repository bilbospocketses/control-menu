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
/// Circuit-resilience tests for <see cref="GoogleTvDashboard"/> (#10-rem): a failing adb call
/// during the unbidden OnInitializedAsync -> ConnectToDevice path must degrade to "Disconnected"
/// rather than throw out of the render and tear down the SignalR circuit.
/// </summary>
public class GoogleTvDashboardTests : BunitContext
{
    private readonly Mock<IDeviceService> _devices = new();
    private readonly Mock<IAdbService> _adb = new();

    public GoogleTvDashboardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_devices.Object);
        Services.AddSingleton(_adb.Object);
        Services.AddSingleton(new Mock<INetworkDiscoveryService>().Object);
        Services.AddSingleton(new Mock<IConfigurationService>().Object);
        Services.AddSingleton(new Mock<IDeviceChangeNotifier>().Object);
        Services.AddSingleton(new Mock<IScrcpyProbeService>().Object);
        Services.AddSingleton(new WsScrcpyService(
            Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpClientFactory>(), NullLogger<WsScrcpyService>.Instance));
    }

    [Fact]
    public void Init_when_adb_connect_throws_degrades_to_disconnected_without_tearing_down_circuit()
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Type = DeviceType.GoogleTV,
            ModuleId = "android-devices",
            Name = "Living Room TV",
            MacAddress = "",
            LastKnownIp = "1.2.3.4",
            AdbPort = 5555,
        };
        _devices.Setup(s => s.GetAllDevicesAsync()).ReturnsAsync(new[] { device });
        _adb.Setup(a => a.ConnectAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("adb unreachable"));

        var cut = Render<GoogleTvDashboard>();

        Assert.Contains("Disconnected", cut.Markup);
    }

    [Fact]
    public void Power_check_action_when_it_throws_shows_error_notice_and_does_not_tear_down_circuit()
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Type = DeviceType.GoogleTV,
            ModuleId = "android-devices",
            Name = "Living Room TV",
            MacAddress = "",
            LastKnownIp = null,
            AdbPort = 5555,
        };
        _devices.Setup(s => s.GetAllDevicesAsync()).ReturnsAsync(new[] { device });
        _adb.Setup(a => a.GetPowerStateAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("adb down"));

        var cut = Render<GoogleTvDashboard>();

        var check = cut.FindAll("button").First(b => b.TextContent.Trim() == "Check");
        var ex = Record.Exception(() => check.Click());

        Assert.Null(ex);                                            // guard swallowed the throw; circuit intact
        Assert.Contains("Failed to read power status.", cut.Markup); // and surfaced the error notice
    }
}
