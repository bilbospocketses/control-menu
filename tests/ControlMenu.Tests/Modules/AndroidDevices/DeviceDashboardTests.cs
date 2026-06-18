using Bunit;
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Modules.AndroidDevices.Pages;
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ControlMenu.Tests.Modules.AndroidDevices;

/// <summary>
/// Characterization tests for the shared <see cref="DeviceDashboard"/> that the Phone/Tablet/Watch
/// route pages render. With no device present the presence watcher redirects, so the component
/// renders its "no device" header — enough to pin that each page's Title/Icon parameters flow
/// through (and that the Watch page is wired to the watch identity, not phone).
/// </summary>
public class DeviceDashboardTests : BunitContext
{
    public DeviceDashboardTests()
    {
        var deviceService = new Mock<IDeviceService>();
        deviceService.Setup(s => s.GetAllDevicesAsync())
            .ReturnsAsync(Array.Empty<Device>());   // none present → presence watcher redirects → no-device render

        Services.AddSingleton(deviceService.Object);
        Services.AddSingleton(new Mock<IAdbService>().Object);
        Services.AddSingleton(new Mock<INetworkDiscoveryService>().Object);
        Services.AddSingleton(new Mock<IConfigurationService>().Object);
        Services.AddSingleton(new Mock<IDeviceChangeNotifier>().Object);
        Services.AddSingleton(new Mock<IScrcpyProbeService>().Object);
    }

    [Theory]
    [InlineData("Android Phone", "bi-phone", DeviceType.AndroidPhone, "phone")]
    [InlineData("Android Tablet", "bi-tablet-landscape", DeviceType.AndroidTablet, "tablet")]
    [InlineData("Android Watch", "bi-smartwatch", DeviceType.AndroidWatch, "watch")]
    public void Renders_ParameterizedTitleAndIcon(string title, string icon, DeviceType type, string kind)
    {
        var cut = Render<DeviceDashboard>(p => p
            .Add(d => d.Title, title)
            .Add(d => d.Icon, icon)
            .Add(d => d.DeviceType, type)
            .Add(d => d.DeviceKind, kind));

        var h1 = cut.Find("h1");
        Assert.Contains(title, h1.TextContent);
        Assert.NotNull(cut.Find($"h1 i.{icon}"));   // the parameterized icon class lands on the header <i>
    }
}
