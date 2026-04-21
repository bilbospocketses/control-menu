using ControlMenu.Services.Network;

namespace ControlMenu.Tests.Services;

public class ScanMergeHelperTests
{
    [Fact]
    public void FindUnregisteredAdbConnected_ReturnsEntriesNotInExclude()
    {
        var adb = new[] { "192.168.1.10:5555", "192.168.1.20:5555", "192.168.1.30:42391" };
        var exclude = new[] { "192.168.1.20:5555" };
        var result = ScanMergeHelper.FindUnregisteredAdbConnected(adb, exclude);
        Assert.Equal(2, result.Count);
        Assert.Contains(("192.168.1.10", 5555), result);
        Assert.Contains(("192.168.1.30", 42391), result);
    }

    [Fact]
    public void FindUnregisteredAdbConnected_SkipsUsbSerials()
    {
        // adb devices emits bare serials (no colon) for USB-connected devices —
        // those aren't network-addressable and have no role in the discovered panel.
        var adb = new[] { "ABC123DEF", "192.168.1.10:5555" };
        var result = ScanMergeHelper.FindUnregisteredAdbConnected(adb, Array.Empty<string>());
        Assert.Single(result);
        Assert.Equal(("192.168.1.10", 5555), result[0]);
    }

    [Fact]
    public void FindUnregisteredAdbConnected_SkipsMalformed()
    {
        var adb = new[] { "", "   ", ":5555", "192.168.1.10:", "192.168.1.10:abc" };
        var result = ScanMergeHelper.FindUnregisteredAdbConnected(adb, Array.Empty<string>());
        Assert.Empty(result);
    }

    [Fact]
    public void FindUnregisteredAdbConnected_ExcludeMatchIsCaseInsensitive()
    {
        // Hosts can appear as uppercase or lowercase when a hostname-style entry
        // reaches adb. IPs are numeric so mixed-case wouldn't happen, but defensive
        // matching is cheap.
        var adb = new[] { "192.168.1.10:5555" };
        var exclude = new[] { "192.168.1.10:5555" };
        var result = ScanMergeHelper.FindUnregisteredAdbConnected(adb, exclude);
        Assert.Empty(result);
    }

    [Fact]
    public void FindUnregisteredAdbConnected_HandlesIpv6LastColonSplit()
    {
        // LastIndexOf(':') means IPv6-literal style "[::1]:5555" splits cleanly
        // on the port colon, not on an address colon. The host carries the
        // brackets/colons; parsing it back out is the caller's job.
        var adb = new[] { "[::1]:5555" };
        var result = ScanMergeHelper.FindUnregisteredAdbConnected(adb, Array.Empty<string>());
        Assert.Single(result);
        Assert.Equal(("[::1]", 5555), result[0]);
    }

    [Fact]
    public void FindUnregisteredAdbConnected_EmptyInputs_ReturnsEmpty()
    {
        var result = ScanMergeHelper.FindUnregisteredAdbConnected(
            Array.Empty<string>(), Array.Empty<string>());
        Assert.Empty(result);
    }

    [Fact]
    public void AddressKey_BuildsIpPortString()
    {
        Assert.Equal("192.168.1.10:5555", ScanMergeHelper.AddressKey("192.168.1.10", 5555));
        Assert.Equal("10.0.0.5:42391", ScanMergeHelper.AddressKey("10.0.0.5", 42391));
    }
}
