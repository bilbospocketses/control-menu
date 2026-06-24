using System.Net.NetworkInformation;

namespace ControlMenu.Services;

public class NetworkDiscoveryService : INetworkDiscoveryService
{
    private readonly IArpTableProvider _arpTable;

    public NetworkDiscoveryService(IArpTableProvider arpTable)
    {
        _arpTable = arpTable;
    }

    public Task<IReadOnlyList<ArpEntry>> GetArpTableAsync(CancellationToken ct = default)
        => _arpTable.GetArpTableAsync(ct);

    public async Task<string?> ResolveIpFromMacAsync(string macAddress, CancellationToken ct = default)
    {
        var normalized = NormalizeMac(macAddress);
        var entries = await _arpTable.GetArpTableAsync(ct);
        return entries.FirstOrDefault(e => e.MacAddress == normalized)?.IpAddress;
    }

    public async Task<bool> PingAsync(string ipAddress, CancellationToken ct = default)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ipAddress, TimeSpan.FromSeconds(2), cancellationToken: ct);
            return reply.Status == IPStatus.Success;
        }
        catch (OperationCanceledException)
        {
            throw; // honour cancellation rather than reporting "unreachable"
        }
        catch
        {
            // PingException (network failure) or a malformed address → treat as unreachable.
            return false;
        }
    }

    public static string NormalizeMac(string mac) => mac.ToLowerInvariant().Replace(':', '-');
}
