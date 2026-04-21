namespace ControlMenu.Services.Network;

/// <summary>
/// A device that appeared in the "Discovered on Network" panel but isn't yet
/// registered with Control Menu. Populated from three sources: live mDNS hits
/// during a Full Scan, adb-merge rows appended on scan completion, and mDNS
/// results from Quick Refresh.
/// </summary>
public sealed record DiscoveredDevice(
    string ServiceName,
    string Ip,
    int Port,
    string? Mac,
    string? Source = null);
