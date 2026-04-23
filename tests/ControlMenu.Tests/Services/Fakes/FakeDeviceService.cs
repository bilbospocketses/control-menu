using ControlMenu.Data.Entities;
using ControlMenu.Services;

namespace ControlMenu.Tests.Services.Fakes;

public sealed class FakeDeviceService : IDeviceService
{
    public List<Device> Devices { get; } = new();

    public Task<IReadOnlyList<Device>> GetAllDevicesAsync()
        => Task.FromResult<IReadOnlyList<Device>>(Devices.ToList());

    public Task<Device?> GetDeviceAsync(Guid id)
        => Task.FromResult(Devices.FirstOrDefault(d => d.Id == id));

    public Task<Device> AddDeviceAsync(Device device)
    {
        Devices.Add(device);
        return Task.FromResult(device);
    }

    public Task UpdateDeviceAsync(Device device)
        => Task.CompletedTask;

    public Task DeleteDeviceAsync(Guid id)
    {
        Devices.RemoveAll(d => d.Id == id);
        return Task.CompletedTask;
    }

    public Task UpdateLastSeenAsync(Guid id, string ipAddress)
        => Task.CompletedTask;
}
