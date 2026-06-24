using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ControlMenu.Services;

/// <summary>
/// Reads the Windows ARP cache via the IP Helper API (<c>GetIpNetTable</c>) instead of shelling
/// out to <c>arp.exe</c> and scraping its locale-dependent text output.
/// </summary>
public sealed class WindowsArpTableProvider : IArpTableProvider
{
    [SupportedOSPlatform("windows")]
    public Task<IReadOnlyList<ArpEntry>> GetArpTableAsync(CancellationToken ct = default)
        => Task.FromResult(Read());

    [SupportedOSPlatform("windows")]
    internal static IReadOnlyList<ArpEntry> Read()
    {
        int size = 0;
        GetIpNetTable(IntPtr.Zero, ref size, false); // first call sizes the buffer
        if (size == 0) return [];

        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetIpNetTable(buffer, ref size, false) != 0) return []; // NO_ERROR == 0

            int count = Marshal.ReadInt32(buffer);
            if (count <= 0) return [];

            var rows = new List<(uint Addr, byte[] Mac6, int PhysLen, int Type)>(count);
            int rowSize = Marshal.SizeOf<MIB_IPNETROW>();
            IntPtr rowPtr = buffer + sizeof(int); // the table[] follows the dwNumEntries DWORD
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_IPNETROW>(rowPtr);
                var mac6 = new[] { row.Mac0, row.Mac1, row.Mac2, row.Mac3, row.Mac4, row.Mac5 };
                rows.Add((unchecked((uint)row.dwAddr), mac6, row.dwPhysAddrLen, row.dwType));
                rowPtr += rowSize;
            }
            return MapRows(rows);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Maps raw IP-Helper rows to <see cref="ArpEntry"/>. Pure (no P/Invoke) so it is unit-testable;
    /// skips non-Ethernet rows (a 6-byte physical address) to match the old <c>arp -a</c> parser,
    /// and emits the lowercase dash MAC + the same type vocabulary ("dynamic"/"static"/"other").
    /// </summary>
    internal static IReadOnlyList<ArpEntry> MapRows(IEnumerable<(uint Addr, byte[] Mac6, int PhysLen, int Type)> rows)
    {
        var list = new List<ArpEntry>();
        foreach (var (addr, mac6, physLen, type) in rows)
        {
            if (physLen != 6 || mac6.Length < 6) continue; // standard Ethernet MACs only
            var ip = new IPAddress(BitConverter.GetBytes(addr)).ToString();
            var mac = string.Join('-', mac6[..6].Select(b => b.ToString("x2")));
            var typeStr = type switch { 4 => "static", 3 => "dynamic", _ => "other" };
            list.Add(new ArpEntry(ip, mac, typeStr));
        }
        return list;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetIpNetTable(IntPtr pIpNetTable, ref int pdwSize, [MarshalAs(UnmanagedType.Bool)] bool bOrder);

    // MIB_IPNETROW: dwIndex, dwPhysAddrLen, bPhysAddr[8], dwAddr, dwType (24 bytes, 4-byte aligned).
    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_IPNETROW
    {
        public int dwIndex;
        public int dwPhysAddrLen;
        public byte Mac0, Mac1, Mac2, Mac3, Mac4, Mac5, Mac6, Mac7;
        public int dwAddr;
        public int dwType;
    }
}
