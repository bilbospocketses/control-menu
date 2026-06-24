using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Services;

public class ShellArpTableProviderTests
{
    [Fact]
    public void Parse_WindowsOutput_ReturnsEntries()
    {
        var output = "Interface: 192.168.1.100 --- 0x4\r\n  Internet Address      Physical Address      Type\r\n  192.168.1.1           a0-b1-c2-d3-e4-f5     dynamic\r\n  192.168.1.50          aa-bb-cc-dd-ee-ff     dynamic\r\n  192.168.1.255         ff-ff-ff-ff-ff-ff     static\r\n";
        var entries = ShellArpTableProvider.Parse(output);
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.IpAddress == "192.168.1.1" && e.MacAddress == "a0-b1-c2-d3-e4-f5");
        Assert.Contains(entries, e => e.IpAddress == "192.168.1.50" && e.MacAddress == "aa-bb-cc-dd-ee-ff");
    }

    [Fact]
    public void Parse_LinuxOutput_ReturnsEntries()
    {
        var output = "? (192.168.1.1) at a0:b1:c2:d3:e4:f5 [ether] on eth0\n? (192.168.1.50) at aa:bb:cc:dd:ee:ff [ether] on eth0\n";
        var entries = ShellArpTableProvider.Parse(output);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.IpAddress == "192.168.1.1" && e.MacAddress == "a0-b1-c2-d3-e4-f5");
        Assert.Contains(entries, e => e.IpAddress == "192.168.1.50" && e.MacAddress == "aa-bb-cc-dd-ee-ff");
    }

    [Fact]
    public void Parse_Empty_ReturnsEmpty() => Assert.Empty(ShellArpTableProvider.Parse(""));

    [Fact]
    public async Task GetArpTableAsync_NonZeroExit_ReturnsEmpty()
    {
        var exec = new Mock<ICommandExecutor>();
        exec.Setup(e => e.ExecuteAsync("arp", "-a", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(1, "", "error", false));
        Assert.Empty(await new ShellArpTableProvider(exec.Object).GetArpTableAsync());
    }

    [Fact]
    public async Task GetArpTableAsync_ParsesExecutorOutput()
    {
        var exec = new Mock<ICommandExecutor>();
        exec.Setup(e => e.ExecuteAsync("arp", "-a", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, "? (192.168.1.7) at de:ad:be:ef:00:11 [ether] on eth0\n", "", false));
        var entries = await new ShellArpTableProvider(exec.Object).GetArpTableAsync();
        Assert.Single(entries);
        Assert.Equal("de-ad-be-ef-00-11", entries[0].MacAddress);
    }
}
