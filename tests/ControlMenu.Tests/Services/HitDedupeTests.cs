using ControlMenu.Services.Network;

namespace ControlMenu.Tests.Services;

public class HitDedupeTests
{
    [Fact]
    public void Collapse_MacPrimary_TwoHitsSameMacCollapseToOne()
    {
        var a = new ScanHit(DiscoverySource.Mdns, "192.168.1.5:5555", "SER1", "", "", "aa:bb:cc:dd:ee:ff");
        var b = new ScanHit(DiscoverySource.Tcp, "192.168.1.5:5555", "SER1", "", "", "aa:bb:cc:dd:ee:ff");
        var collapsed = HitDedupe.Collapse(new[] { a, b });
        Assert.Single(collapsed);
    }

    [Fact]
    public void Collapse_MacCaseInsensitive()
    {
        var a = new ScanHit(DiscoverySource.Mdns, "192.168.1.5:5555", "SER1", "", "", "AA:BB:CC:DD:EE:FF");
        var b = new ScanHit(DiscoverySource.Tcp, "192.168.1.5:5555", "SER1", "", "", "aa:bb:cc:dd:ee:ff");
        var collapsed = HitDedupe.Collapse(new[] { a, b });
        Assert.Single(collapsed);
    }

    [Fact]
    public void Collapse_NullMacWithSerial_FallsBackToSerialPlaceholder()
    {
        var a = new ScanHit(DiscoverySource.Mdns, "192.168.1.5:5555", "SER1", "", "", null);
        var b = new ScanHit(DiscoverySource.Tcp, "192.168.1.5:5555", "SER1", "", "", null);
        var collapsed = HitDedupe.Collapse(new[] { a, b });
        Assert.Single(collapsed);
    }

    [Fact]
    public void Collapse_NullMacNoSerial_FallsBackToAddress()
    {
        var a = new ScanHit(DiscoverySource.Mdns, "192.168.1.5:5555", "", "", "", null);
        var b = new ScanHit(DiscoverySource.Tcp, "192.168.1.5:5555", "", "", "", null);
        var collapsed = HitDedupe.Collapse(new[] { a, b });
        Assert.Single(collapsed);
    }

    [Fact]
    public void Collapse_LastHitWins_RicherDataReplacesEarlier()
    {
        var first = new ScanHit(DiscoverySource.Tcp, "192.168.1.5:5555", "SER1", "adb-SER1", "", "aa:bb:cc:dd:ee:ff");
        var second = new ScanHit(DiscoverySource.Mdns, "192.168.1.5:5555", "SER1", "Jamie's phone", "Jamie's phone", "aa:bb:cc:dd:ee:ff");
        var collapsed = HitDedupe.Collapse(new[] { first, second });
        Assert.Single(collapsed);
        Assert.Equal("Jamie's phone", collapsed[0].Name);
        Assert.Equal("Jamie's phone", collapsed[0].Label);
    }

    [Fact]
    public void Collapse_DifferentMacs_KeepsBoth()
    {
        var a = new ScanHit(DiscoverySource.Tcp, "192.168.1.5:5555", "S1", "", "", "aa:bb:cc:dd:ee:01");
        var b = new ScanHit(DiscoverySource.Tcp, "192.168.1.6:5555", "S2", "", "", "aa:bb:cc:dd:ee:02");
        var collapsed = HitDedupe.Collapse(new[] { a, b });
        Assert.Equal(2, collapsed.Count);
    }

    [Fact]
    public void Collapse_MacAndNullMacForSameDevice_DocumentedAsTwoCards()
    {
        // Known limitation: if the first hit has null MAC (TCP probe landed before
        // ARP resolved) and the second hit has a real MAC (ARP filled in),
        // the two hits key to different buckets (serial placeholder vs MAC),
        // so the user sees two cards briefly. Mitigation: DeviceManagement merges
        // by MAC after an explicit ARP refresh post-scan. Pinning as "two cards"
        // here so an intentional future fix would flag as a test failure.
        var earlyNoMac = new ScanHit(DiscoverySource.Tcp, "192.168.1.5:5555", "SER1", "", "", null);
        var laterWithMac = new ScanHit(DiscoverySource.Mdns, "192.168.1.5:5555", "SER1", "", "", "aa:bb:cc:dd:ee:ff");
        var collapsed = HitDedupe.Collapse(new[] { earlyNoMac, laterWithMac });
        Assert.Equal(2, collapsed.Count);
    }

    [Fact]
    public void Collapse_Empty_ReturnsEmpty()
    {
        var collapsed = HitDedupe.Collapse(Array.Empty<ScanHit>());
        Assert.Empty(collapsed);
    }
}
