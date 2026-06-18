using System.Net;
using ControlMenu.Services.Network;

namespace ControlMenu.Tests.Services;

public class SubnetMathTests
{
    private static ParsedSubnet S(string normalized) => new(normalized, normalized, 0);

    // ---- Contains: O(1) mask/range compare ----

    [Theory]
    [InlineData("192.168.1.0/24", "192.168.1.50", true)]
    [InlineData("192.168.1.0/24", "192.168.1.1", true)]
    [InlineData("192.168.1.0/24", "192.168.1.255", true)]   // membership includes network/broadcast
    [InlineData("192.168.1.0/24", "192.168.2.1", false)]
    [InlineData("192.168.1.0/24", "10.0.0.1", false)]
    [InlineData("192.168.1.5/32", "192.168.1.5", true)]
    [InlineData("192.168.1.5/32", "192.168.1.6", false)]
    public void Contains_Cidr(string subnet, string ip, bool expected)
    {
        Assert.Equal(expected, SubnetMath.Contains(S(subnet), IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("1.2.3.4")]
    [InlineData("255.255.255.255")]
    [InlineData("0.0.0.0")]
    public void Contains_Slash0_MatchesEverything(string ip)
    {
        // The /0 mask is 0, so (ip & 0) == (network & 0) for all addresses. This is the case the
        // old `1u << 32` enumerate path got wrong (it yielded nothing instead of everything).
        Assert.True(SubnetMath.Contains(S("0.0.0.0/0"), IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("192.168.1.10", true)]
    [InlineData("192.168.1.15", true)]
    [InlineData("192.168.1.20", true)]
    [InlineData("192.168.1.9", false)]
    [InlineData("192.168.1.21", false)]
    public void Contains_Range_IsInclusive(string ip, bool expected)
    {
        Assert.Equal(expected, SubnetMath.Contains(S("192.168.1.10-192.168.1.20"), IPAddress.Parse(ip)));
    }

    // ---- Enumerate: scannable hosts ----

    [Fact]
    public void Enumerate_Cidr24_Yields254Hosts_SkippingNetworkAndBroadcast()
    {
        var hosts = SubnetMath.Enumerate(S("192.168.1.0/24")).Select(a => a.ToString()).ToList();
        Assert.Equal(254, hosts.Count);
        Assert.Equal("192.168.1.1", hosts[0]);
        Assert.Equal("192.168.1.254", hosts[^1]);
        Assert.DoesNotContain("192.168.1.0", hosts);
        Assert.DoesNotContain("192.168.1.255", hosts);
    }

    [Fact]
    public void Enumerate_Cidr30_YieldsTwoUsableHosts()
    {
        var hosts = SubnetMath.Enumerate(S("192.168.1.0/30")).Select(a => a.ToString()).ToList();
        Assert.Equal(new[] { "192.168.1.1", "192.168.1.2" }, hosts);
    }

    [Fact]
    public void Enumerate_Slash31_YieldsBothHosts_PerRfc3021()
    {
        var hosts = SubnetMath.Enumerate(S("192.168.1.0/31")).Select(a => a.ToString()).ToList();
        Assert.Equal(new[] { "192.168.1.0", "192.168.1.1" }, hosts);
    }

    [Fact]
    public void Enumerate_Slash32_YieldsSingleHost()
    {
        // The old code yielded NOTHING for /32 (count = 1u<<0 = 1, loop bound 1<count-1=0).
        var hosts = SubnetMath.Enumerate(S("192.168.1.5/32")).Select(a => a.ToString()).ToList();
        Assert.Equal(new[] { "192.168.1.5" }, hosts);
    }

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("10.0.0.0/8")]
    [InlineData("172.16.0.0/15")]
    public void Enumerate_RejectsSubnetsLargerThanSlash16(string subnet)
    {
        // Tripwire against a future caller bypassing SubnetParser (which caps at /16) — the old
        // code would have hung trying to materialize the block.
        Assert.Throws<ArgumentOutOfRangeException>(() => SubnetMath.Enumerate(S(subnet)).ToList());
    }

    [Fact]
    public void Enumerate_Range_IsInclusive()
    {
        var hosts = SubnetMath.Enumerate(S("192.168.1.10-192.168.1.12")).Select(a => a.ToString()).ToList();
        Assert.Equal(new[] { "192.168.1.10", "192.168.1.11", "192.168.1.12" }, hosts);
    }

    // ---- Primitives (shared with SubnetParser) ----

    [Theory]
    [InlineData("0.0.0.0", 0u)]
    [InlineData("255.255.255.255", 4294967295u)]
    [InlineData("192.168.1.1", 3232235777u)]
    public void IpToInt_RoundTrips(string ip, uint expected)
    {
        Assert.Equal(expected, SubnetMath.IpToInt(ip));
        Assert.Equal(ip, SubnetMath.IntToIp(expected));
    }
}
