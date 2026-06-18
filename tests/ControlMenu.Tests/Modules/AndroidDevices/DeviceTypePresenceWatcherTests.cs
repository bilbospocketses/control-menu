using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Services;
using ControlMenu.Tests.Services.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ControlMenu.Tests.Modules.AndroidDevices;

/// <summary>
/// Tests for <see cref="DeviceTypePresenceWatcher"/>. Post-#9-rem the watcher resolves
/// <see cref="IDeviceService"/> from a fresh DI scope per call (never a captured request-scoped
/// service) and redirects through a dispatcher-marshalled callback rather than calling
/// NavigationManager off the renderer thread.
/// </summary>
public class DeviceTypePresenceWatcherTests
{
    private readonly FakeDeviceService _deviceService = new();
    private readonly FakeDeviceChangeNotifier _notifier = new();
    private int _redirectCount;

    private static Device MakePhone()
        => new() { Id = Guid.NewGuid(), Name = "P", Type = DeviceType.AndroidPhone, MacAddress = "aa", ModuleId = "android-devices" };

    private IServiceScopeFactory ScopeFactory()
        => new ServiceCollection()
            .AddSingleton<IDeviceService>(_deviceService)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    private DeviceTypePresenceWatcher NewWatcher(Func<Task>? onInvalidate = null)
        => new(
            DeviceType.AndroidPhone,
            ScopeFactory(),
            _notifier,
            () => { _redirectCount++; return Task.CompletedTask; },
            onInvalidate);

    [Fact]
    public async Task EnsurePresentOrRedirectAsync_NoDevicesOfType_Redirects()
    {
        using var watcher = NewWatcher();

        var redirected = await watcher.EnsurePresentOrRedirectAsync();

        Assert.True(redirected);
        Assert.Equal(1, _redirectCount);
    }

    [Fact]
    public async Task EnsurePresentOrRedirectAsync_DevicesPresent_DoesNotRedirect()
    {
        _deviceService.Devices.Add(MakePhone());
        using var watcher = NewWatcher();

        var redirected = await watcher.EnsurePresentOrRedirectAsync();

        Assert.False(redirected);
        Assert.Equal(0, _redirectCount);
    }

    [Fact]
    public async Task NotifierChanged_LastDeviceDeleted_Redirects()
    {
        _deviceService.Devices.Add(MakePhone());
        using var watcher = NewWatcher();
        await watcher.EnsurePresentOrRedirectAsync();

        _deviceService.Devices.Clear();
        _notifier.RaiseChanged();
        await Task.Delay(50);

        Assert.Equal(1, _redirectCount);
    }

    [Fact]
    public async Task NotifierChanged_OtherDevicesPresent_InvokesInvalidateCallback_DoesNotRedirect()
    {
        _deviceService.Devices.Add(MakePhone());
        var invalidateCount = 0;
        using var watcher = NewWatcher(() => { invalidateCount++; return Task.CompletedTask; });
        await watcher.EnsurePresentOrRedirectAsync();

        _deviceService.Devices.Add(MakePhone());
        _notifier.RaiseChanged();
        await Task.Delay(50);

        Assert.Equal(0, _redirectCount);
        Assert.Equal(1, invalidateCount);
    }

    [Fact]
    public async Task NotifierChanged_AfterAlreadyRedirected_DoesNotRedirectAgain()
    {
        using var watcher = NewWatcher();
        await watcher.EnsurePresentOrRedirectAsync();

        _notifier.RaiseChanged();
        await Task.Delay(50);

        Assert.Equal(1, _redirectCount);
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromNotifier()
    {
        _deviceService.Devices.Add(MakePhone());
        var watcher = NewWatcher();
        await watcher.EnsurePresentOrRedirectAsync();

        watcher.Dispose();
        _deviceService.Devices.Clear();
        _notifier.RaiseChanged();
        await Task.Delay(50);

        Assert.Equal(0, _redirectCount);
    }

    [Fact]
    public async Task OnDevicesChanged_resolves_device_service_from_a_fresh_scope_each_call()
    {
        var deviceService = new Mock<IDeviceService>();
        deviceService.Setup(s => s.GetAllDevicesAsync()).ReturnsAsync(new[] { MakePhone() });
        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(IDeviceService))).Returns(deviceService.Object);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);

        using var watcher = new DeviceTypePresenceWatcher(
            DeviceType.AndroidPhone, factory.Object, _notifier,
            () => { _redirectCount++; return Task.CompletedTask; },
            () => Task.CompletedTask);

        await watcher.OnDevicesChangedAsync();
        await watcher.OnDevicesChangedAsync();

        // A fresh scope is created on each change — never a captured (disposable) request scope.
        factory.Verify(f => f.CreateScope(), Times.Exactly(2));
    }
}
