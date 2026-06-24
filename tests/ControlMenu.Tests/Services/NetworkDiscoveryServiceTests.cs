using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Services;

public class NetworkDiscoveryServiceTests
{
    private static NetworkDiscoveryService WithArp(params ArpEntry[] entries)
    {
        var provider = new Mock<IArpTableProvider>();
        provider.Setup(p => p.GetArpTableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);
        return new NetworkDiscoveryService(provider.Object);
    }

    [Fact]
    public async Task GetArpTableAsync_DelegatesToProvider()
    {
        var result = await WithArp(new ArpEntry("192.168.1.50", "aa-bb-cc-dd-ee-ff", "dynamic")).GetArpTableAsync();
        Assert.Single(result);
        Assert.Equal("192.168.1.50", result[0].IpAddress);
    }

    [Fact]
    public async Task ResolveIpFromMacAsync_FindsMatchingEntry()
    {
        var svc = WithArp(new ArpEntry("192.168.1.50", "aa-bb-cc-dd-ee-ff", "dynamic"));
        Assert.Equal("192.168.1.50", await svc.ResolveIpFromMacAsync("AA-BB-CC-DD-EE-FF"));
    }

    [Fact]
    public async Task ResolveIpFromMacAsync_NormalizesColonFormat()
    {
        var svc = WithArp(new ArpEntry("192.168.1.50", "aa-bb-cc-dd-ee-ff", "dynamic"));
        Assert.Equal("192.168.1.50", await svc.ResolveIpFromMacAsync("aa:bb:cc:dd:ee:ff"));
    }

    [Fact]
    public async Task ResolveIpFromMacAsync_NotFound_ReturnsNull()
    {
        var svc = WithArp(new ArpEntry("192.168.1.50", "aa-bb-cc-dd-ee-ff", "dynamic"));
        Assert.Null(await svc.ResolveIpFromMacAsync("00-00-00-00-00-00"));
    }

    [Fact]
    public void NormalizeMac_ConvertsFormats()
    {
        Assert.Equal("aa-bb-cc-dd-ee-ff", NetworkDiscoveryService.NormalizeMac("AA-BB-CC-DD-EE-FF"));
        Assert.Equal("aa-bb-cc-dd-ee-ff", NetworkDiscoveryService.NormalizeMac("aa:bb:cc:dd:ee:ff"));
        Assert.Equal("aa-bb-cc-dd-ee-ff", NetworkDiscoveryService.NormalizeMac("AA:BB:CC:DD:EE:FF"));
    }

    [Fact]
    public async Task PingAsync_Loopback_ReturnsTrue()
    {
        var svc = new NetworkDiscoveryService(Mock.Of<IArpTableProvider>());
        Assert.True(await svc.PingAsync("127.0.0.1"));
    }

    [Fact]
    public async Task PingAsync_UnroutableAddress_ReturnsFalse()
    {
        // 192.0.2.1 is RFC 5737 TEST-NET-1 — guaranteed never to respond, so the ping times out.
        var svc = new NetworkDiscoveryService(Mock.Of<IArpTableProvider>());
        Assert.False(await svc.PingAsync("192.0.2.1"));
    }
}
