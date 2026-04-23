using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Tests.Services.Fakes;

namespace ControlMenu.Tests.Modules.AndroidDevices;

public class DeviceTypePresenceWatcherTests
{
    private readonly FakeDeviceService _deviceService = new();
    private readonly FakeDeviceChangeNotifier _notifier = new();
    private readonly FakeNavigationManager _nav = new();

    private static Device MakePhone()
        => new() { Id = Guid.NewGuid(), Name = "P", Type = DeviceType.AndroidPhone, MacAddress = "aa", ModuleId = "android-devices" };

    [Fact]
    public async Task EnsurePresentOrRedirectAsync_NoDevicesOfType_Redirects()
    {
        using var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _notifier, _nav, null);

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
        using var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _notifier, _nav, null);

        var redirected = await watcher.EnsurePresentOrRedirectAsync();

        Assert.False(redirected);
        Assert.Empty(_nav.Navigations);
    }

    [Fact]
    public async Task NotifierChanged_LastDeviceDeleted_Redirects()
    {
        var phone = MakePhone();
        _deviceService.Devices.Add(phone);
        using var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _notifier, _nav, null);
        await watcher.EnsurePresentOrRedirectAsync();

        _deviceService.Devices.Clear();
        _notifier.RaiseChanged();
        await Task.Delay(50);

        Assert.Single(_nav.Navigations);
        Assert.Equal("/android/devices", _nav.Navigations[0].Uri);
    }

    [Fact]
    public async Task NotifierChanged_OtherDevicesPresent_InvokesInvalidateCallback_DoesNotRedirect()
    {
        _deviceService.Devices.Add(MakePhone());
        var invalidateCount = 0;
        using var watcher = new DeviceTypePresenceWatcher(
            DeviceType.AndroidPhone,
            _deviceService,
            _notifier,
            _nav,
            () => { invalidateCount++; return Task.CompletedTask; });
        await watcher.EnsurePresentOrRedirectAsync();

        _deviceService.Devices.Add(MakePhone());
        _notifier.RaiseChanged();
        await Task.Delay(50);

        Assert.Empty(_nav.Navigations);
        Assert.Equal(1, invalidateCount);
    }

    [Fact]
    public async Task NotifierChanged_AfterAlreadyRedirected_DoesNotRedirectAgain()
    {
        using var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _notifier, _nav, null);
        await watcher.EnsurePresentOrRedirectAsync();

        _notifier.RaiseChanged();
        await Task.Delay(50);

        Assert.Single(_nav.Navigations);
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromNotifier()
    {
        _deviceService.Devices.Add(MakePhone());
        var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _notifier, _nav, null);
        await watcher.EnsurePresentOrRedirectAsync();

        watcher.Dispose();
        _deviceService.Devices.Clear();
        _notifier.RaiseChanged();
        await Task.Delay(50);

        Assert.Empty(_nav.Navigations);
    }
}
