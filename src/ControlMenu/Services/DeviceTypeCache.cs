using ControlMenu.Data.Enums;

namespace ControlMenu.Services;

public sealed class DeviceTypeCache : IDeviceTypeCache, IDisposable
{
    private readonly IDeviceService _deviceService;
    private readonly ReaderWriterLockSlim _lock = new();
    private HashSet<DeviceType> _typesPresent = new();

    public event Action? CacheUpdated;

    public DeviceTypeCache(IDeviceService deviceService)
    {
        _deviceService = deviceService;
        _deviceService.DevicesChanged += OnDevicesChanged;
    }

    public bool HasDevicesOfType(DeviceType type)
    {
        _lock.EnterReadLock();
        try { return _typesPresent.Contains(type); }
        finally { _lock.ExitReadLock(); }
    }

    public async Task RefreshAsync()
    {
        var devices = await _deviceService.GetAllDevicesAsync();
        var newSet = devices.Select(d => d.Type).ToHashSet();
        _lock.EnterWriteLock();
        try { _typesPresent = newSet; }
        finally { _lock.ExitWriteLock(); }
        CacheUpdated?.Invoke();
    }

    private async void OnDevicesChanged()
    {
        try { await RefreshAsync(); }
        catch
        {
            // Async-void event handler: exceptions must be swallowed to avoid
            // terminating the process. Host logging pipeline covers mutation failures.
        }
    }

    public void Dispose()
    {
        _deviceService.DevicesChanged -= OnDevicesChanged;
        _lock.Dispose();
    }
}
