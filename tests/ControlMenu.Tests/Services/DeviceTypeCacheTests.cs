using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Services;
using ControlMenu.Tests.Services.Fakes;

namespace ControlMenu.Tests.Services;

public class DeviceTypeCacheTests
{
    private readonly FakeDeviceService _deviceService = new();
    private readonly DeviceTypeCache _cache;

    public DeviceTypeCacheTests()
    {
        _cache = new DeviceTypeCache(_deviceService);
    }

    private static Device Make(DeviceType type)
        => new() { Id = Guid.NewGuid(), Name = "D", Type = type, MacAddress = "aa:bb", ModuleId = "android-devices" };

    [Fact]
    public void HasDevicesOfType_BeforeRefresh_ReturnsFalse()
    {
        Assert.False(_cache.HasDevicesOfType(DeviceType.AndroidPhone));
    }

    [Fact]
    public async Task HasDevicesOfType_AfterRefreshWithPhones_ReturnsTrueForPhone_FalseForOthers()
    {
        _deviceService.Devices.Add(Make(DeviceType.AndroidPhone));
        _deviceService.Devices.Add(Make(DeviceType.AndroidPhone));
        _deviceService.Devices.Add(Make(DeviceType.AndroidTablet));

        await _cache.RefreshAsync();

        Assert.True(_cache.HasDevicesOfType(DeviceType.AndroidPhone));
        Assert.True(_cache.HasDevicesOfType(DeviceType.AndroidTablet));
        Assert.False(_cache.HasDevicesOfType(DeviceType.GoogleTV));
        Assert.False(_cache.HasDevicesOfType(DeviceType.AndroidWatch));
    }

    [Fact]
    public async Task DevicesChanged_TriggersReadAndCacheUpdated()
    {
        var updated = 0;
        var tcs = new TaskCompletionSource();
        _cache.CacheUpdated += () =>
        {
            updated++;
            tcs.TrySetResult();
        };

        _deviceService.Devices.Add(Make(DeviceType.AndroidPhone));
        _deviceService.RaiseChanged();
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(_cache.HasDevicesOfType(DeviceType.AndroidPhone));
        Assert.Equal(1, updated);
    }

    [Fact]
    public async Task LastDeviceDeleted_MakesHasDevicesOfTypeReturnFalse()
    {
        var phone = Make(DeviceType.AndroidPhone);
        _deviceService.Devices.Add(phone);
        await _cache.RefreshAsync();
        Assert.True(_cache.HasDevicesOfType(DeviceType.AndroidPhone));

        var tcs = new TaskCompletionSource();
        _cache.CacheUpdated += () => tcs.TrySetResult();

        _deviceService.Devices.Clear();
        _deviceService.RaiseChanged();
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(_cache.HasDevicesOfType(DeviceType.AndroidPhone));
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromDevicesChanged()
    {
        var updated = 0;
        _cache.CacheUpdated += () => updated++;

        _cache.Dispose();
        _deviceService.Devices.Add(Make(DeviceType.AndroidPhone));
        _deviceService.RaiseChanged();

        // Give any in-flight handler a chance to run; we expect NONE.
        await Task.Delay(100);

        Assert.Equal(0, updated);
    }
}
