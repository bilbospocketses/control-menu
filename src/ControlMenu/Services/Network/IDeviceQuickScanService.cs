using ControlMenu.Data.Entities;

namespace ControlMenu.Services.Network;

/// <summary>
/// Result of a Quick Scan: counts the caller can use to render a
/// confirmation toast / status line. Hits are merged into
/// <see cref="IScanLifecycleHandler.Discovered"/> as a side effect.
/// </summary>
/// <param name="RegisteredFound">Number of caller-supplied registered devices that
/// were observed (via mDNS or ARP-fallback) and had their LastSeen updated.</param>
/// <param name="PortsUpdated">Number of registered devices whose AdbPort was changed
/// to match the live mDNS-advertised port.</param>
/// <param name="NewOnNetwork">Number of new (unregistered, undismissed, deduped)
/// devices appended to Handler.Discovered as a side effect.</param>
public sealed record DeviceQuickScanResult(int RegisteredFound, int PortsUpdated, int NewOnNetwork);

public interface IDeviceQuickScanService
{
    /// <summary>
    /// Runs the canonical mDNS + ARP merge pipeline used by the Settings
    /// Device Management page's Quick Refresh and the Home page's Scan Android
    /// button. Mutates the <see cref="IScanLifecycleHandler"/> Discovered list,
    /// updates IP/port for matching registered devices in the database, and
    /// returns aggregate counts.
    /// </summary>
    Task<DeviceQuickScanResult> RunMdnsAndMergeAsync(
        IReadOnlyList<Device> registered,
        CancellationToken ct = default);
}
