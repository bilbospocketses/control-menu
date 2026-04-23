using ControlMenu.Data.Entities;
using ControlMenu.Services;

namespace ControlMenu.Tests.Services.Fakes;

public sealed class FakeDeviceService : IDeviceService
{
    public List<Device> Devices { get; } = new();
    public event Action? DevicesChanged;

    public Task<IReadOnlyList<Device>> GetAllDevicesAsync()
        => Task.FromResult<IReadOnlyList<Device>>(Devices.ToList());

    public Task<Device?> GetDeviceAsync(Guid id)
        => Task.FromResult(Devices.FirstOrDefault(d => d.Id == id));

    public Task<Device> AddDeviceAsync(Device device)
    {
        Devices.Add(device);
        DevicesChanged?.Invoke();
        return Task.FromResult(device);
    }

    public Task UpdateDeviceAsync(Device device)
    {
        DevicesChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task DeleteDeviceAsync(Guid id)
    {
        Devices.RemoveAll(d => d.Id == id);
        DevicesChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task UpdateLastSeenAsync(Guid id, string ipAddress)
        => Task.CompletedTask;

    public void RaiseChanged() => DevicesChanged?.Invoke();

    event Action IDeviceService.DevicesChanged
    {
        add => DevicesChanged += value;
        remove => DevicesChanged -= value;
    }
}
