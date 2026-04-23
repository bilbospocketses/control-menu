using ControlMenu.Data.Enums;

namespace ControlMenu.Services;

public interface IDeviceTypeCache
{
    bool HasDevicesOfType(DeviceType type);
    event Action? CacheUpdated;
    Task RefreshAsync();
}
