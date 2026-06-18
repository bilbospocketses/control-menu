using System.Net;
using System.Net.Sockets;

namespace ControlMenu.Services.Network;

/// <summary>
/// IPv4 subnet arithmetic shared by <see cref="SubnetParser"/> and the camera scanner.
/// Membership is an O(1) mask/range compare — the camera scanner previously tested membership by
/// enumerating every address in the subnet and comparing (O(subnet size) per check).
/// </summary>
public static class SubnetMath
{
    public static uint IpToInt(string ip)
    {
        var p = ip.Split('.');
        return ((uint)byte.Parse(p[0]) << 24) | ((uint)byte.Parse(p[1]) << 16)
             | ((uint)byte.Parse(p[2]) << 8) | byte.Parse(p[3]);
    }

    public static string IntToIp(uint n) =>
        $"{(n >> 24) & 0xFF}.{(n >> 16) & 0xFF}.{(n >> 8) & 0xFF}.{n & 0xFF}";

    public static uint ToUInt32(IPAddress addr)
    {
        var b = addr.GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    public static IPAddress ToIPAddress(uint v) =>
        new(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });

    /// <summary>
    /// O(1) membership test against a parsed subnet's normalized form (CIDR <c>ip/prefix</c>,
    /// range <c>startIp-endIp</c>, or a single IP normalized to <c>/32</c>). Unlike the old
    /// enumerate-and-compare, this includes the network and broadcast addresses in the subnet.
    /// </summary>
    public static bool Contains(ParsedSubnet subnet, IPAddress addr)
    {
        if (addr.AddressFamily != AddressFamily.InterNetwork) return false;
        var ip = ToUInt32(addr);
        var n = subnet.Normalized;

        var slash = n.IndexOf('/');
        if (slash >= 0)
        {
            var network = IpToInt(n[..slash]);
            var prefix = int.Parse(n[(slash + 1)..]);
            var mask = MaskFor(prefix);
            return (ip & mask) == (network & mask);
        }

        var dash = n.IndexOf('-');
        if (dash >= 0)
        {
            var start = IpToInt(n[..dash]);
            var end = IpToInt(n[(dash + 1)..]);
            return ip >= start && ip <= end;
        }

        return ip == IpToInt(n);
    }

    /// <summary>
    /// Enumerates the scannable host addresses of a subnet: CIDR /0–/30 skip the network and
    /// broadcast addresses; /31 and /32 yield their literal hosts (RFC 3021); ranges are inclusive.
    /// Assumes parser-validated input (SubnetParser caps CIDR at /16 and ranges at 65,536), so it
    /// never tries to materialize an enormous block. Uses a 64-bit size to avoid the <c>1u &lt;&lt; 32</c>
    /// wrap that made /0 (and /31, /32) compute the wrong count.
    /// </summary>
    public static IEnumerable<IPAddress> Enumerate(ParsedSubnet subnet)
    {
        var n = subnet.Normalized;

        var slash = n.IndexOf('/');
        if (slash >= 0)
        {
            var prefix = int.Parse(n[(slash + 1)..]);
            // Tripwire, not reachable via SubnetParser (it rejects prefixes < 16). Enumerating a
            // shorter prefix would try to materialize millions-to-billions of addresses; fail loudly
            // rather than hang if a future caller hands us an unvalidated subnet.
            if (prefix < 16)
                throw new ArgumentOutOfRangeException(nameof(subnet),
                    $"Refusing to enumerate /{prefix} ({n}); subnets must be /16 or longer (≤65,534 hosts).");
            var baseNet = IpToInt(n[..slash]) & MaskFor(prefix);

            if (prefix >= 31)
            {
                var count = prefix == 32 ? 1u : 2u;  // /32 = 1 host, /31 = 2 (no net/broadcast)
                for (uint i = 0; i < count; i++) yield return ToIPAddress(baseNet + i);
                yield break;
            }

            ulong size = 1UL << (32 - prefix);
            for (ulong i = 1; i < size - 1; i++)   // skip network (offset 0) + broadcast (size-1)
                yield return ToIPAddress(baseNet + (uint)i);
            yield break;
        }

        var dash = n.IndexOf('-');
        if (dash >= 0)
        {
            var start = IpToInt(n[..dash]);
            var end = IpToInt(n[(dash + 1)..]);
            if (start > end) yield break;
            for (var i = start; ; i++)
            {
                yield return ToIPAddress(i);
                if (i == end) break;   // compare-after-yield avoids the uint wrap at end == 255.255.255.255
            }
            yield break;
        }

        yield return IPAddress.Parse(n);
    }

    /// <summary>The 32-bit network mask for a CIDR prefix; prefix 0 yields 0 (avoids the shift-by-32 wrap).</summary>
    private static uint MaskFor(int prefix) => prefix <= 0 ? 0u : 0xFFFFFFFFu << (32 - prefix);
}
