using System.Text.RegularExpressions;

namespace ControlMenu.Services;

public partial class NetworkDiscoveryService : INetworkDiscoveryService
{
    private readonly ICommandExecutor _executor;

    public NetworkDiscoveryService(ICommandExecutor executor)
    {
        _executor = executor;
    }

    public async Task<IReadOnlyList<ArpEntry>> GetArpTableAsync(CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("arp", "-a", cancellationToken: ct);
        if (result.ExitCode != 0) return [];
        return ParseArpOutput(result.StandardOutput);
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
