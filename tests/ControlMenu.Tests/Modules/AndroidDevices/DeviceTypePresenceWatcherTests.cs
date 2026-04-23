using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Tests.Services.Fakes;

namespace ControlMenu.Tests.Modules.AndroidDevices;

public class DeviceTypePresenceWatcherTests
{
    private readonly FakeDeviceService _deviceService = new();
    private readonly FakeNavigationManager _nav = new();

    private static Device MakePhone()
        => new() { Id = Guid.NewGuid(), Name = "P", Type = DeviceType.AndroidPhone, MacAddress = "aa", ModuleId = "android-devices" };

    [Fact]
    public async Task EnsurePresentOrRedirectAsync_NoDevicesOfType_Redirects()
    {
        using var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _nav, null);

        var redirected = await watcher.EnsurePresentOrRedirectAsync();

        Assert.True(redirected);
        Assert.Single(_nav.Navigations);
        Assert.Equal("/android/devices", _nav.Navigations[0].Uri);
        Assert.True(_nav.Navigations[0].Replace);
    }

    [Fact]
    public async Task EnsurePresentOrRedirectAsync_DevicesPresent_DoesNotRedirect()
    {
        _deviceService.Devices.Add(MakePhone());
        using var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _nav, null);

        var redirected = await watcher.EnsurePresentOrRedirectAsync();

        Assert.False(redirected);
        Assert.Empty(_nav.Navigations);
    }

    [Fact]
    public async Task DevicesChanged_LastDeviceDeleted_Redirects()
    {
        var phone = MakePhone();
        _deviceService.Devices.Add(phone);
        using var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _nav, null);
        await watcher.EnsurePresentOrRedirectAsync();

        _deviceService.Devices.Clear();
        _deviceService.RaiseChanged();
        await Task.Delay(50);  // let async-void handler settle

        Assert.Single(_nav.Navigations);
        Assert.Equal("/android/devices", _nav.Navigations[0].Uri);
    }

    [Fact]
    public async Task DevicesChanged_OtherDevicesPresent_InvokesInvalidateCallback_DoesNotRedirect()
    {
        _deviceService.Devices.Add(MakePhone());
        var invalidateCount = 0;
        using var watcher = new DeviceTypePresenceWatcher(
            DeviceType.AndroidPhone,
            _deviceService,
            _nav,
            () => { invalidateCount++; return Task.CompletedTask; });
        await watcher.EnsurePresentOrRedirectAsync();

        _deviceService.Devices.Add(MakePhone());
        _deviceService.RaiseChanged();
        await Task.Delay(50);

        Assert.Empty(_nav.Navigations);
        Assert.Equal(1, invalidateCount);
    }

    [Fact]
    public async Task DevicesChanged_AfterAlreadyRedirected_DoesNotRedirectAgain()
    {
        using var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _nav, null);
        await watcher.EnsurePresentOrRedirectAsync();   // first redirect

        _deviceService.RaiseChanged();
        await Task.Delay(50);

        Assert.Single(_nav.Navigations);  // still only the initial redirect
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromDevicesChanged()
    {
        _deviceService.Devices.Add(MakePhone());
        var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _nav, null);
        await watcher.EnsurePresentOrRedirectAsync();

        watcher.Dispose();
        _deviceService.Devices.Clear();
        _deviceService.RaiseChanged();
        await Task.Delay(50);

        Assert.Empty(_nav.Navigations);
    }
}
