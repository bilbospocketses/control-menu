using ControlMenu.Data.Enums;
using ControlMenu.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ControlMenu.Modules.AndroidDevices.Services;

/// <summary>
/// Redirects a device-type dashboard back to the device list when no device of its type is
/// present, and re-checks whenever the device set changes. The change notification fires from a
/// background thread, so this type must NOT touch the renderer (NavigationManager) directly or
/// query a captured request-scoped service — it resolves <see cref="IDeviceService"/> from a
/// fresh scope per call and routes the redirect/invalidate through dispatcher-marshalled
/// callbacks supplied by the component (#9-rem).
/// </summary>
public sealed class DeviceTypePresenceWatcher : IDisposable
{
    private readonly DeviceType _type;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeviceChangeNotifier _notifier;
    private readonly Func<Task> _onRedirectAsync;
    private readonly Func<Task>? _onInvalidateAsync;
    // Read on the renderer (EnsurePresentOrRedirectAsync) and written on the notifier's background
    // thread (OnDevicesChangedAsync); volatile for cross-thread visibility. Worst case is a benign
    // extra presence check / idempotent redirect.
    private volatile bool _redirected;

    public DeviceTypePresenceWatcher(
        DeviceType type,
        IServiceScopeFactory scopeFactory,
        IDeviceChangeNotifier notifier,
        Func<Task> onRedirectAsync,
        Func<Task>? onInvalidateAsync)
    {
        _type = type;
        _scopeFactory = scopeFactory;
        _notifier = notifier;
        _onRedirectAsync = onRedirectAsync;
        _onInvalidateAsync = onInvalidateAsync;
        _notifier.Changed += OnDevicesChanged;
    }

    public async Task<bool> EnsurePresentOrRedirectAsync()
    {
        if (!await IsTypePresentAsync())
        {
            _redirected = true;
            await _onRedirectAsync();
            return true;
        }
        return false;
    }

    // async void is forced by the Action-typed Changed event. OnDevicesChangedAsync owns the
    // try/catch as its first real work, so nothing can escape this handler and tear down the
    // circuit / crash the process.
    private async void OnDevicesChanged() => await OnDevicesChangedAsync();

    internal async Task OnDevicesChangedAsync()
    {
        if (_redirected) return;
        try
        {
            if (!await IsTypePresentAsync())
            {
                _redirected = true;
                await _onRedirectAsync();
            }
            else if (_onInvalidateAsync is not null)
            {
                await _onInvalidateAsync();
            }
        }
        catch
        {
            // Fire-and-forget continuation off the notifier's thread: an escaping exception
            // would tear down the circuit / crash the process, so swallow (#9-rem).
        }
    }

    // Resolve IDeviceService from a fresh scope per call: the watcher outlives any single request
    // and the notifier fires from a background thread, so a captured request-scoped service (and
    // its non-thread-safe DbContext) could be disposed or used concurrently (#9-rem).
    private async Task<bool> IsTypePresentAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var deviceService = scope.ServiceProvider.GetRequiredService<IDeviceService>();
        var devices = await deviceService.GetAllDevicesAsync();
        return devices.Any(d => d.Type == _type);
    }

    public void Dispose() => _notifier.Changed -= OnDevicesChanged;
}
