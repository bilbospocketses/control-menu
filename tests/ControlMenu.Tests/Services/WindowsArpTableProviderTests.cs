using ControlMenu.Services;

namespace ControlMenu.Tests.Services;

public class WindowsArpTableProviderTests
{
    [Fact]
    public void MapRows_MapsAddressMacAndType()
    {
        // The IP stored in MIB_IPNETROW.dwAddr is network-byte-order; BitConverter round-trips it.
        uint addr = BitConverter.ToUInt32([192, 168, 1, 50], 0);
        var rows = new[] { (addr, new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF }, 6, 3) }; // 3 = dynamic
        var entries = WindowsArpTableProvider.MapRows(rows);
        Assert.Single(entries);
        Assert.Equal("192.168.1.50", entries[0].IpAddress);
        Assert.Equal("aa-bb-cc-dd-ee-ff", entries[0].MacAddress);
        Assert.Equal("dynamic", entries[0].Type);
    }

    [Fact]
    public void MapRows_StaticType()
    {
        uint addr = BitConverter.ToUInt32([10, 0, 0, 2], 0);
        var rows = new[] { (addr, new byte[] { 1, 2, 3, 4, 5, 6 }, 6, 4) }; // 4 = static
        Assert.Equal("static", WindowsArpTableProvider.MapRows(rows)[0].Type);
    }

    [Fact]
    public void MapRows_SkipsNonEthernetRows()
    {
        uint addr = BitConverter.ToUInt32([10, 0, 0, 1], 0);
        var rows = new[] { (addr, new byte[] { 0, 0, 0, 0, 0, 0 }, 0, 1) }; // dwPhysAddrLen 0 → incomplete
        Assert.Empty(WindowsArpTableProvider.MapRows(rows));
    }

    [Fact]
    public void Read_OnWindows_MarshalsWithoutThrowing()
    {
        if (!OperatingSystem.IsWindows()) return; // P/Invoke smoke — exercises the real GetIpNetTable
        var entries = WindowsArpTableProvider.Read();
        Assert.NotNull(entries); // may be empty on a sparse runner, but must marshal without throwing
    }
}
