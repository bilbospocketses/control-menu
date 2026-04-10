namespace ControlMenu.Services;

public interface INetworkDiscoveryService
{
    Task<IReadOnlyList<ArpEntry>> GetArpTableAsync(CancellationToken ct = default);
    Task<string?> ResolveIpFromMacAsync(string macAddress, CancellationToken ct = default);
    Task<bool> PingAsync(string ipAddress, CancellationToken ct = default);
}
