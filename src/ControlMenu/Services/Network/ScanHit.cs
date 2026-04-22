namespace ControlMenu.Services.Network;

public enum DiscoverySource { Mdns, Tcp, Adb }

/// <summary>
/// A device observed during a scan. <see cref="Address"/> is <c>"IP:port"</c>
/// exactly as emitted by ws-scan's scan.hit message. <see cref="Mac"/> is null
/// until ARP resolves the IP post-TCP-touch.
/// </summary>
/// <param name="Source">Which discovery channel produced this hit.</param>
/// <param name="Address">Canonical <c>ip:port</c> string (see <see cref="ScanMergeHelper.AddressKey"/>).</param>
/// <param name="Serial">
/// ADB serial as advertised in the mDNS service name (the
/// <c>adb-&lt;serial&gt;._adb-tls-connect._tcp.local.</c> form). Used for scan-result
/// dedupe (see <see cref="HitDedupe"/>) and logging only. For persisting to a
/// registered device, a live <c>ro.serialno</c> probe in <c>AddFromDiscovery</c>
/// is authoritative — see <see cref="Data.Entities.Device.SerialNumber"/>.
/// </param>
/// <param name="Name">The raw mDNS service label, e.g. <c>adb-ABC123._adb-tls-connect._tcp.local.</c>.</param>
/// <param name="Label">Human-friendly label from scan output (often empty).</param>
/// <param name="Mac">MAC address resolved from ARP, or null if unresolved.</param>
public sealed record ScanHit(
    DiscoverySource Source,
    string Address,
    string Serial,
    string Name,
    string Label,
    string? Mac);
