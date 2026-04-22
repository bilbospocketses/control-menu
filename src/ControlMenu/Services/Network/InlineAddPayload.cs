namespace ControlMenu.Services.Network;

/// <summary>
/// Payload emitted by <c>DiscoveredPanelRow</c>'s <c>OnAdd</c> callback.
/// Carries the user-edited device plus the separately-stored PIN string
/// (which <c>DeviceManagement.SaveDevice</c> encrypts into the secret store
/// keyed by the saved device's Id).
/// </summary>
/// <param name="Source">The original DiscoveredDevice — used by the parent to filter the row out of Handler.Discovered after save.</param>
/// <param name="Device">All the row's edited fields packed into a <c>Device</c> entity (no Id yet; SaveDevice assigns it).</param>
/// <param name="Pin">PIN string as typed, empty means "no PIN." Never logged.</param>
public sealed record InlineAddPayload(
    DiscoveredDevice Source,
    Data.Entities.Device Device,
    string Pin);
