using System.Text.RegularExpressions;

namespace ControlMenu.Services;

/// <summary>
/// Reads the ARP table by shelling <c>arp -a</c> and parsing its output. Used on Linux (no managed
/// ARP API) and as a non-Windows fallback. Handles both the Windows (<c>ip  mac  type</c>) and the
/// Unix (<c>(ip) at mac</c>) <c>arp -a</c> layouts.
/// </summary>
public sealed partial class ShellArpTableProvider : IArpTableProvider
{
    private readonly ICommandExecutor _executor;

    public ShellArpTableProvider(ICommandExecutor executor) => _executor = executor;

    public async Task<IReadOnlyList<ArpEntry>> GetArpTableAsync(CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("arp", "-a", cancellationToken: ct);
        return result.ExitCode != 0 ? [] : Parse(result.StandardOutput);
    }

    /// <summary>Pure parse of <c>arp -a</c> output → entries. Unit-testable independent of OS.</summary>
    internal static IReadOnlyList<ArpEntry> Parse(string output)
    {
        var entries = new List<ArpEntry>();
        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries))
        {
            var win = WindowsArpRegex().Match(line);
            if (win.Success)
            {
                entries.Add(new ArpEntry(
                    win.Groups["ip"].Value,
                    NetworkDiscoveryService.NormalizeMac(win.Groups["mac"].Value),
                    win.Groups["type"].Value));
                continue;
            }
            var lin = LinuxArpRegex().Match(line);
            if (lin.Success)
            {
                entries.Add(new ArpEntry(
                    lin.Groups["ip"].Value,
                    NetworkDiscoveryService.NormalizeMac(lin.Groups["mac"].Value),
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
