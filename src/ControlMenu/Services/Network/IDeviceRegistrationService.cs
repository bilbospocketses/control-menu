using ControlMenu.Data.Entities;

namespace ControlMenu.Services.Network;

/// <summary>
/// Registers a device chosen from a Discovered panel's inline-add flow. Centralizes the body
/// previously duplicated across the Home, Wizard, and Settings discovery panels: normalize the
/// MAC, persist the device, store the optional PIN secret, and drop the now-claimed row from
/// <see cref="IScanLifecycleHandler.Discovered"/>.
/// </summary>
public interface IDeviceRegistrationService
{
    /// <summary>
    /// Persists <paramref name="payload"/> as a new device and removes its claimed MAC from the
    /// scan handler's Discovered list. Returns the saved device (with Id assigned).
    /// </summary>
    Task<Device> RegisterDiscoveredAsync(InlineAddPayload payload);
}
