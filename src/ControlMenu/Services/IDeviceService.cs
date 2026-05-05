using ControlMenu.Data.Entities;

namespace ControlMenu.Services;

public interface IDeviceService
{
    Task<IReadOnlyList<Device>> GetAllDevicesAsync();
    Task<Device?> GetDeviceAsync(Guid id);
    Task<Device> AddDeviceAsync(Device device);
    Task UpdateDeviceAsync(Device device);
    Task DeleteDeviceAsync(Guid id);
    Task UpdateLastSeenAsync(Guid id, string ipAddress);
    /// <summary>Deletes all devices. Fires the change notifier exactly once.</summary>
    Task<int> DeleteAllDevicesAsync();
}
