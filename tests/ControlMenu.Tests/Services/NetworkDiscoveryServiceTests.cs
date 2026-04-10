using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Services;

public class NetworkDiscoveryServiceTests
{
    private readonly Mock<ICommandExecutor> _mockExecutor = new();
    private NetworkDiscoveryService CreateService() => new(_mockExecutor.Object);

    [Fact]
    public async Task GetArpTableAsync_ParsesWindowsOutput()
    {
        var windowsOutput = "Interface: 192.168.1.100 --- 0x4\r\n  Internet Address      Physical Address      Type\r\n  192.168.1.1           a0-b1-c2-d3-e4-f5     dynamic\r\n  192.168.1.50          b8-7b-d4-f3-ae-84     dynamic\r\n  192.168.1.255         ff-ff-ff-ff-ff-ff     static\r\n";
        _mockExecutor.Setup(e => e.ExecuteAsync("arp", "-a", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, windowsOutput, "", false));
        var service = CreateService();
        var entries = await service.GetArpTableAsync();
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.IpAddress == "192.168.1.1" && e.MacAddress == "a0-b1-c2-d3-e4-f5");
        Assert.Contains(entries, e => e.IpAddress == "192.168.1.50" && e.MacAddress == "b8-7b-d4-f3-ae-84");
    }

    [Fact]
    public async Task GetArpTableAsync_ParsesLinuxArpOutput()
    {
        var linuxOutput = "? (192.168.1.1) at a0:b1:c2:d3:e4:f5 [ether] on eth0\n? (192.168.1.50) at b8:7b:d4:f3:ae:84 [ether] on eth0\n";
        _mockExecutor.Setup(e => e.ExecuteAsync("arp", "-a", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, linuxOutput, "", false));
        var service = CreateService();
        var entries = await service.GetArpTableAsync();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.IpAddress == "192.168.1.1" && e.MacAddress == "a0-b1-c2-d3-e4-f5");
        Assert.Contains(entries, e => e.IpAddress == "192.168.1.50" && e.MacAddress == "b8-7b-d4-f3-ae-84");
    }

    [Fact]
    public async Task GetArpTableAsync_EmptyOutput_ReturnsEmptyList()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("arp", "-a", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, "", "", false));
        var service = CreateService();
        var entries = await service.GetArpTableAsync();
        Assert.Empty(entries);
    }

    [Fact]
    public async Task ResolveIpFromMacAsync_FindsMatchingEntry()
    {
        var output = "Interface: 192.168.1.100 --- 0x4\r\n  Internet Address      Physical Address      Type\r\n  192.168.1.50          b8-7b-d4-f3-ae-84     dynamic\r\n";
        _mockExecutor.Setup(e => e.ExecuteAsync("arp", "-a", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, output, "", false));
        var service = CreateService();
        var ip = await service.ResolveIpFromMacAsync("B8-7B-D4-F3-AE-84");
        Assert.Equal("192.168.1.50", ip);
    }

    [Fact]
    public async Task ResolveIpFromMacAsync_NormalizesColonFormat()
    {
        var output = "Interface: 192.168.1.100 --- 0x4\r\n  Internet Address      Physical Address      Type\r\n  192.168.1.50          b8-7b-d4-f3-ae-84     dynamic\r\n";
        _mockExecutor.Setup(e => e.ExecuteAsync("arp", "-a", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, output, "", false));
        var service = CreateService();
        var ip = await service.ResolveIpFromMacAsync("b8:7b:d4:f3:ae:84");
        Assert.Equal("192.168.1.50", ip);
    }

    [Fact]
    public async Task ResolveIpFromMacAsync_NotFound_ReturnsNull()
    {
        var output = "Interface: 192.168.1.100 --- 0x4\r\n  Internet Address      Physical Address      Type\r\n  192.168.1.50          b8-7b-d4-f3-ae-84     dynamic\r\n";
        _mockExecutor.Setup(e => e.ExecuteAsync("arp", "-a", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, output, "", false));
        var service = CreateService();
        var ip = await service.ResolveIpFromMacAsync("00-00-00-00-00-00");
        Assert.Null(ip);
    }

    [Fact]
    public async Task PingAsync_SuccessfulPing_ReturnsTrue()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("ping", It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, "Reply from 192.168.1.50", "", false));
        var service = CreateService();
        var result = await service.PingAsync("192.168.1.50");
        Assert.True(result);
    }

    [Fact]
    public async Task PingAsync_FailedPing_ReturnsFalse()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("ping", It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(1, "Request timed out", "", false));
        var service = CreateService();
        var result = await service.PingAsync("192.168.1.50");
        Assert.False(result);
    }

    [Fact]
    public void NormalizeMac_ConvertsFormats()
    {
        Assert.Equal("b8-7b-d4-f3-ae-84", NetworkDiscoveryService.NormalizeMac("B8-7B-D4-F3-AE-84"));
        Assert.Equal("b8-7b-d4-f3-ae-84", NetworkDiscoveryService.NormalizeMac("b8:7b:d4:f3:ae:84"));
        Assert.Equal("b8-7b-d4-f3-ae-84", NetworkDiscoveryService.NormalizeMac("B8:7B:D4:F3:AE:84"));
    }
}
