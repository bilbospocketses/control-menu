using ControlMenu.Data.Entities;

namespace ControlMenu.Services.Network;

/// <inheritdoc cref="IDeviceRegistrationService"/>
public sealed class DeviceRegistrationService : IDeviceRegistrationService
{
    private readonly IDeviceService _devices;
    private readonly IConfigurationService _config;
    private readonly IScanLifecycleHandler _handler;

    public DeviceRegistrationService(
        IDeviceService devices,
        IConfigurationService config,
        IScanLifecycleHandler handler)
    {
        _devices = devices;
        _config = config;
        _handler = handler;
    }

    public async Task<Device> RegisterDiscoveredAsync(InlineAddPayload payload)
    {
        var device = payload.Device;
        device.MacAddress = NetworkDiscoveryService.NormalizeMac(device.MacAddress);

        var saved = await _devices.AddDeviceAsync(device);

        if (!string.IsNullOrEmpty(payload.Pin))
            await _config.SetSecretAsync($"device-pin-{saved.Id}", payload.Pin);

        // Drop the now-claimed MAC from the Discovered panel so the same device isn't
        // offered for adding a second time.
        _handler.ReplaceDiscovered(_handler.Discovered
            .Where(d => !string.Equals(d.Mac, saved.MacAddress, StringComparison.OrdinalIgnoreCase)));

        return saved;
    }
}
