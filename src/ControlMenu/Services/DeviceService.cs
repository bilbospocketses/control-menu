using ControlMenu.Data;
using ControlMenu.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Services;

public class DeviceService : IDeviceService
{
    private readonly AppDbContext _db;

    public DeviceService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Device>> GetAllDevicesAsync()
    {
        return await _db.Devices.OrderBy(d => d.Name).ToListAsync();
    }

    public async Task<Device?> GetDeviceAsync(Guid id)
    {
        return await _db.Devices.FindAsync(id);
    }

    public async Task<Device> AddDeviceAsync(Device device)
    {
        if (device.Id == Guid.Empty)
            device.Id = Guid.NewGuid();
        _db.Devices.Add(device);
        await _db.SaveChangesAsync();
        return device;
    }

    public async Task UpdateDeviceAsync(Device device)
    {
        _db.Devices.Update(device);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteDeviceAsync(Guid id)
    {
        var device = await _db.Devices.FindAsync(id);
        if (device is not null)
        {
            _db.Devices.Remove(device);
            await _db.SaveChangesAsync();
        }
    }

    public async Task UpdateLastSeenAsync(Guid id, string ipAddress)
    {
        var device = await _db.Devices.FindAsync(id);
        if (device is not null)
        {
            device.LastKnownIp = ipAddress;
            device.LastSeen = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }
}
