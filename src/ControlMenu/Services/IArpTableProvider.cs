namespace ControlMenu.Services;

/// <summary>
/// Source of the system ARP table (IP ↔ MAC). Implemented by a managed Windows IP Helper
/// provider (<c>GetIpNetTable</c> — no shell-out, no locale-fragile parsing) and a shell
/// provider (<c>arp -a</c>) used on Linux, where there is no managed ARP API.
/// </summary>
public interface IArpTableProvider
{
    Task<IReadOnlyList<ArpEntry>> GetArpTableAsync(CancellationToken ct = default);
}
