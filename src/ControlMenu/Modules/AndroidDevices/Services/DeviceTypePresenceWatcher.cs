using ControlMenu.Data.Enums;
using ControlMenu.Services;
using Microsoft.AspNetCore.Components;

namespace ControlMenu.Modules.AndroidDevices.Services;

public sealed class DeviceTypePresenceWatcher : IDisposable
{
    private readonly DeviceType _type;
    private readonly IDeviceService _deviceService;
    private readonly IDeviceChangeNotifier _notifier;
    private readonly NavigationManager _nav;
    private readonly Func<Task>? _onInvalidateAsync;
    private bool _redirected;

    public DeviceTypePresenceWatcher(
        DeviceType type,
        IDeviceService deviceService,
        IDeviceChangeNotifier notifier,
        NavigationManager nav,
        Func<Task>? onInvalidateAsync)
    {
        _type = type;
        _deviceService = deviceService;
        _notifier = notifier;
        _nav = nav;
        _onInvalidateAsync = onInvalidateAsync;
        _notifier.Changed += OnDevicesChanged;
    }

    public async Task<bool> EnsurePresentOrRedirectAsync()
    {
        var devices = await _deviceService.GetAllDevicesAsync();
        if (!devices.Any(d => d.Type == _type))
        {
            _redirected = true;
            _nav.NavigateTo("/android/devices", replace: true);
            return true;
        }
        return false;
    }

    private async void OnDevicesChanged()
    {
        if (_redirected) return;
        try
        {
            var devices = await _deviceService.GetAllDevicesAsync();
            if (!devices.Any(d => d.Type == _type))
            {
                _redirected = true;
                _nav.NavigateTo("/android/devices", replace: true);
            }
            else if (_onInvalidateAsync is not null)
            {
                await _onInvalidateAsync();
            }
        }
        catch
        {
            // Async-void event handler: swallow to avoid process termination.
        }
    }

    public void Dispose() => _notifier.Changed -= OnDevicesChanged;
}
