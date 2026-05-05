using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ControlMenu.Services;

public partial class NetworkDiscoveryService : INetworkDiscoveryService
{
    private readonly ICommandExecutor _executor;
    private readonly ILogger<NetworkDiscoveryService> _logger;

    public NetworkDiscoveryService(ICommandExecutor executor, ILogger<NetworkDiscoveryService> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ArpEntry>> GetArpTableAsync(CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("arp", "-a", cancellationToken: ct);
        _logger.LogInformation("[ARP-DIAG] arp -a exit={Exit} stdoutLen={Len} stderrLen={ErrLen}",
            result.ExitCode, result.StandardOutput?.Length ?? 0, result.StandardError?.Length ?? 0);
        if (result.ExitCode != 0)
        {
            _logger.LogWarning("[ARP-DIAG] arp -a failed: {Stderr}", result.StandardError);
            return [];
        }
        var entries = ParseArpOutput(result.StandardOutput);
        _logger.LogInformation("[ARP-DIAG] parsed {Count} ARP entries; sample IPs: {Sample}",
            entries.Count, string.Join(", ", entries.Take(5).Select(e => $"{e.IpAddress}={e.MacAddress}")));
        return entries;
    }

    public async Task<string?> ResolveIpFromMacAsync(string macAddress, CancellationToken ct = default)
    {
        var normalized = NormalizeMac(macAddress);
        var entries = await GetArpTableAsync(ct);
        return entries.FirstOrDefault(e => e.MacAddress == normalized)?.IpAddress;
    }

    public async Task<bool> PingAsync(string ipAddress, CancellationToken ct = default)
    {
        var args = OperatingSystem.IsWindows()
            ? $"-n 1 -w 2000 {ipAddress}"
            : $"-c 1 -W 2 {ipAddress}";
        var result = await _executor.ExecuteAsync("ping", args, cancellationToken: ct);
        return result.ExitCode == 0;
    }

    public static string NormalizeMac(string mac)
    {
        return mac.ToLowerInvariant().Replace(':', '-');
    }

    private static List<ArpEntry> ParseArpOutput(string output)
    {
        var entries = new List<ArpEntry>();
        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries))
        {
            var windowsMatch = WindowsArpRegex().Match(line);
            if (windowsMatch.Success)
            {
                entries.Add(new ArpEntry(
                    windowsMatch.Groups["ip"].Value,
                    NormalizeMac(windowsMatch.Groups["mac"].Value),
                    windowsMatch.Groups["type"].Value));
                continue;
            }
            var linuxMatch = LinuxArpRegex().Match(line);
            if (linuxMatch.Success)
            {
                entries.Add(new ArpEntry(
                    linuxMatch.Groups["ip"].Value,
                    NormalizeMac(linuxMatch.Groups["mac"].Value),
                    "dynamic"));
            }
        }
        return entries;
    }

    [GeneratedRegex(@"(?<ip>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\s+(?<mac>[0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2})\s+(?<type>\w+)")]
    private static partial Regex WindowsArpRegex();

    [GeneratedRegex(@"\((?<ip>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\)\s+at\s+(?<mac>[0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2})")]
    private static partial Regex LinuxArpRegex();
}
