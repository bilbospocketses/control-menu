using ControlMenu.Data;
using ControlMenu.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Services;

public class DeviceService : IDeviceService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public DeviceService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public event Action? DevicesChanged;

    event Action IDeviceService.DevicesChanged
    {
        add => DevicesChanged += value;
        remove => DevicesChanged -= value;
    }

    public async Task<IReadOnlyList<Device>> GetAllDevicesAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Devices.AsNoTracking().OrderBy(d => d.Name).ToListAsync();
    }

    public async Task<Device?> GetDeviceAsync(Guid id)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<Device> AddDeviceAsync(Device device)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        if (device.Id == Guid.Empty)
            device.Id = Guid.NewGuid();
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        DevicesChanged?.Invoke();
        return device;
    }

    public async Task UpdateDeviceAsync(Device device)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.Devices.FindAsync(device.Id);
        if (existing is null)
            throw new InvalidOperationException($"Device {device.Id} not found in database.");

        db.Entry(existing).CurrentValues.SetValues(device);
        await db.SaveChangesAsync();
        DevicesChanged?.Invoke();
    }

    public async Task DeleteDeviceAsync(Guid id)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var device = await db.Devices.FindAsync(id);
        if (device is not null)
        {
            db.Devices.Remove(device);
            await db.SaveChangesAsync();
            DevicesChanged?.Invoke();
        }
    }

    public async Task UpdateLastSeenAsync(Guid id, string ipAddress)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var device = await db.Devices.FindAsync(id);
        if (device is not null)
        {
            device.LastKnownIp = ipAddress;
            device.LastSeen = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }
}
