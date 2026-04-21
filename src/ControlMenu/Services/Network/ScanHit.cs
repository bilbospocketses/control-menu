namespace ControlMenu.Services.Network;

public enum DiscoverySource { Mdns, Tcp, Adb }

/// <summary>
/// A device observed during a scan. <see cref="Address"/> is <c>"IP:port"</c>
/// exactly as emitted by ws-scan's scan.hit message. <see cref="Mac"/> is null
/// until ARP resolves the IP post-TCP-touch.
/// </summary>
public sealed record ScanHit(
    DiscoverySource Source,
    string Address,
    string Serial,
    string Name,
    string Label,
    string? Mac);
