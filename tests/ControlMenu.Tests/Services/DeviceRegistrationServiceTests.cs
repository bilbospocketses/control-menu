using ControlMenu.Data.Entities;
using ControlMenu.Services;
using ControlMenu.Services.Network;
using Moq;

namespace ControlMenu.Tests.Services;

public class DeviceRegistrationServiceTests
{
    private readonly Mock<IDeviceService> _devices = new();
    private readonly Mock<IConfigurationService> _config = new();
    private readonly Mock<IScanLifecycleHandler> _handler = new();

    public DeviceRegistrationServiceTests()
    {
        // AddDeviceAsync echoes the device back with an Id assigned (mirrors the real service).
        _devices.Setup(s => s.AddDeviceAsync(It.IsAny<Device>()))
            .ReturnsAsync((Device d) => { if (d.Id == Guid.Empty) d.Id = Guid.NewGuid(); return d; });
        _handler.Setup(h => h.Discovered).Returns(new List<DiscoveredDevice>());
    }

    private DeviceRegistrationService CreateService() =>
        new(_devices.Object, _config.Object, _handler.Object);

    private static InlineAddPayload Payload(string mac, string pin = "")
    {
        var disc = new DiscoveredDevice("Pixel", "192.168.1.50", 5555, mac);
        var device = new Device { Name = "Pixel", MacAddress = mac, ModuleId = "android-devices" };
        return new InlineAddPayload(disc, device, pin);
    }

    [Fact]
    public async Task RegisterDiscoveredAsync_NormalizesMac_AndPersists()
    {
        var expected = NetworkDiscoveryService.NormalizeMac("AA:BB:CC:DD:EE:01");

        var service = CreateService();
        var saved = await service.RegisterDiscoveredAsync(Payload("AA:BB:CC:DD:EE:01"));

        Assert.Equal(expected, saved.MacAddress);
        _devices.Verify(s => s.AddDeviceAsync(It.Is<Device>(d => d.MacAddress == expected)), Times.Once);
    }

    [Fact]
    public async Task RegisterDiscoveredAsync_StoresPin_WhenProvided()
    {
        var service = CreateService();
        var saved = await service.RegisterDiscoveredAsync(Payload("AABBCCDDEE02", pin: "1234"));

        _config.Verify(c => c.SetSecretAsync($"device-pin-{saved.Id}", "1234"), Times.Once);
    }

    [Fact]
    public async Task RegisterDiscoveredAsync_DoesNotStorePin_WhenEmpty()
    {
        var service = CreateService();
        await service.RegisterDiscoveredAsync(Payload("AABBCCDDEE03", pin: ""));

        _config.Verify(c => c.SetSecretAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RegisterDiscoveredAsync_DropsClaimedMacFromDiscovered_KeepingOthers()
    {
        var claimed = NetworkDiscoveryService.NormalizeMac("AA:BB:CC:DD:EE:04");
        _handler.Setup(h => h.Discovered).Returns(new List<DiscoveredDevice>
        {
            new("Pixel", "192.168.1.50", 5555, claimed),
            new("Other", "192.168.1.51", 5555, "ffffffffffff"),
        });
        List<DiscoveredDevice>? replaced = null;
        _handler.Setup(h => h.ReplaceDiscovered(It.IsAny<IEnumerable<DiscoveredDevice>>()))
            .Callback((IEnumerable<DiscoveredDevice> d) => replaced = d.ToList());

        var service = CreateService();
        await service.RegisterDiscoveredAsync(Payload("AA:BB:CC:DD:EE:04"));

        Assert.NotNull(replaced);
        Assert.DoesNotContain(replaced!, d => string.Equals(d.Mac, claimed, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(replaced!, d => d.Mac == "ffffffffffff");
    }
}
