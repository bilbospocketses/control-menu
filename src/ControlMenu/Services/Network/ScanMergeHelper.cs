namespace ControlMenu.Services.Network;

/// <summary>
/// Helpers that augment a Full Scan's result list with devices the ws-scrcpy-web
/// scanner deliberately excludes. The upstream <c>NetworkScanner</c> filters out
/// any IP:port already listed by <c>adb devices</c> so users only see "candidates
/// for adb connect". That leaves a gap when a device is already connected via
/// ws-scrcpy-web or scrcpy but has never been registered with Control Menu.
/// This helper surfaces those for Add.
/// </summary>
public static class ScanMergeHelper
{
    /// <summary>
    /// Canonical string form of an (ip, port) pair used as the dismiss key and
    /// the dedupe key across the Discovered panel. Every producer of a dismiss
    /// key (live hit, adb-merge, dismiss click) uses this helper so the key
    /// format stays symmetric even if upstream ws-scan emission ever drifts.
    /// </summary>
    public static string AddressKey(string ip, int port) => $"{ip}:{port}";

    /// <summary>
    /// Returns <c>(ip, port)</c> pairs from <paramref name="adbConnected"/> that
    /// are not present in <paramref name="excludeIpPorts"/>. Entries that don't
    /// look like <c>ip:port</c> (USB serials, empty lines) are ignored.
    /// Case-insensitive comparison on the exclude set.
    /// </summary>
    public static IReadOnlyList<(string Ip, int Port)> FindUnregisteredAdbConnected(
        IEnumerable<string> adbConnected,
        IEnumerable<string> excludeIpPorts)
    {
        var excluded = new HashSet<string>(excludeIpPorts, StringComparer.OrdinalIgnoreCase);
        var result = new List<(string Ip, int Port)>();
        foreach (var entry in adbConnected)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var colon = entry.LastIndexOf(':');
            if (colon <= 0 || colon == entry.Length - 1) continue;
            if (excluded.Contains(entry)) continue;
            var ip = entry[..colon];
            var portStr = entry[(colon + 1)..];
            if (!int.TryParse(portStr, out var port)) continue;
            result.Add((ip, port));
        }
        return result;
    }

    /// <summary>
    /// True if a discovered entry corresponds to an already-registered device — matched by its
    /// own MAC, by its IP's ARP-resolved MAC, or by its IP equalling a registered device's
    /// last-known IP. Used to drop carry-over Discovered rows once their device is registered so a
    /// re-scan doesn't resurface them as ghosts. The MAC and IP sets must compare case-insensitively.
    /// </summary>
    public static bool MatchesRegistered(
        DiscoveredDevice device,
        IReadOnlySet<string> registeredMacs,
        IReadOnlyDictionary<string, string> arpMap,
        IReadOnlySet<string> registeredIps)
    {
        if (device.Mac is not null && registeredMacs.Contains(device.Mac))
            return true;
        if (arpMap.TryGetValue(device.Ip, out var mac) && registeredMacs.Contains(mac))
            return true;
        return registeredIps.Contains(device.Ip);
    }
}
