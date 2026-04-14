using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Services;
using ControlMenu.Tests.Data;

namespace ControlMenu.Tests.Services;

public class DeviceServiceTests : IDisposable
{
    private readonly InMemoryDbContextFactory _factory;
    private readonly DeviceService _service;

    public DeviceServiceTests()
    {
        _factory = TestDbContextFactory.CreateFactory();
        _service = new DeviceService(_factory);
    }

    public void Dispose() => _factory.Dispose();

    private Device MakeDevice(string name = "Test TV", string mac = "aa-bb-cc-dd-ee-ff")
    {
        return new Device
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = DeviceType.GoogleTV,
            MacAddress = mac,
            ModuleId = "android-devices"
        };
    }

    [Fact]
    public async Task GetAllDevicesAsync_Empty_ReturnsEmptyList()
    {
        var devices = await _service.GetAllDevicesAsync();
        Assert.Empty(devices);
    }

    [Fact]
    public async Task AddDeviceAsync_AddsAndReturnsDevice()
    {
        var device = MakeDevice();
        var result = await _service.AddDeviceAsync(device);
        Assert.Equal(device.Name, result.Name);
        var all = await _service.GetAllDevicesAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task GetDeviceAsync_ReturnsById()
    {
        var device = MakeDevice();
        await _service.AddDeviceAsync(device);
        var loaded = await _service.GetDeviceAsync(device.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Test TV", loaded.Name);
    }

    [Fact]
    public async Task GetDeviceAsync_NotFound_ReturnsNull()
    {
        var loaded = await _service.GetDeviceAsync(Guid.NewGuid());
        Assert.Null(loaded);
    }

    [Fact]
    public async Task UpdateDeviceAsync_ModifiesFields()
    {
        var device = MakeDevice();
        await _service.AddDeviceAsync(device);
        device.Name = "Renamed TV";
        device.AdbPort = 5556;
        await _service.UpdateDeviceAsync(device);
        var loaded = await _service.GetDeviceAsync(device.Id);
        Assert.Equal("Renamed TV", loaded!.Name);
        Assert.Equal(5556, loaded.AdbPort);
    }

    [Fact]
    public async Task DeleteDeviceAsync_RemovesDevice()
    {
        var device = MakeDevice();
        await _service.AddDeviceAsync(device);
        await _service.DeleteDeviceAsync(device.Id);
        var all = await _service.GetAllDevicesAsync();
        Assert.Empty(all);
    }

    [Fact]
    public async Task UpdateLastSeenAsync_SetsIpAndTimestamp()
    {
        var device = MakeDevice();
        await _service.AddDeviceAsync(device);
        await _service.UpdateLastSeenAsync(device.Id, "192.168.1.50");
        var loaded = await _service.GetDeviceAsync(device.Id);
        Assert.Equal("192.168.1.50", loaded!.LastKnownIp);
        Assert.NotNull(loaded.LastSeen);
    }
}
