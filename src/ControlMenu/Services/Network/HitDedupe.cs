namespace ControlMenu.Services.Network;

public static class HitDedupe
{
    /// <summary>
    /// Collapses a sequence of raw scan hits into unique devices.
    /// Dedupe key preference: MAC > IP (when MAC null) > serial placeholder.
    /// Last hit wins for each key (later hits usually have richer data —
    /// e.g. MAC arrives after TCP probe because ARP resolves post-touch).
    /// </summary>
    public static IReadOnlyList<ScanHit> Collapse(IEnumerable<ScanHit> hits)
    {
        var byKey = new Dictionary<string, ScanHit>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in hits)
        {
            var key = h.Mac
                ?? (string.IsNullOrEmpty(h.Serial) ? h.Address : $"serial:{h.Serial}");
            byKey[key] = h;
        }
        return byKey.Values.ToList();
    }
}
