using System.Text.RegularExpressions;
using ControlMenu.Services;

namespace ControlMenu.Modules.AndroidDevices.Services;

public partial class AdbService : IAdbService
{
    private readonly ICommandExecutor _executor;

    public AdbService(ICommandExecutor executor)
    {
        _executor = executor;
    }

    private string DeviceArg(string ip, int port) => $"-s {ip}:{port}";

    public async Task<bool> ConnectAsync(string ip, int port, CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("adb", $"connect {ip}:{port}", null, ct);
        return result.ExitCode == 0 && result.StandardOutput.Contains("connected");
    }

    public async Task DisconnectAsync(string ip, int port, CancellationToken ct = default)
    {
        await _executor.ExecuteAsync("adb", $"disconnect {ip}:{port}", null, ct);
    }

    public async Task<PowerState> GetPowerStateAsync(string ip, int port, CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell dumpsys power", null, ct);
        if (result.ExitCode != 0) return PowerState.Unknown;
        if (result.StandardOutput.Contains("mwakefulness=awake", StringComparison.OrdinalIgnoreCase)
            || result.StandardOutput.Contains("mWakefulness=Awake"))
            return PowerState.Awake;
        return PowerState.Asleep;
    }

    public async Task RebootAsync(string ip, int port, CancellationToken ct = default)
    {
        await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell reboot", null, ct);
    }

    public async Task TogglePowerAsync(string ip, int port, CancellationToken ct = default)
    {
        await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell input keyevent KEYCODE_POWER", null, ct);
    }

    public async Task<string> GetScreensaverAsync(string ip, int port, CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell settings get secure screensaver_components", null, ct);
        var output = result.StandardOutput.Trim();
        if (string.IsNullOrEmpty(output) || result.ExitCode != 0)
            return "Unknown";
        if (output.Contains("skyfolio", StringComparison.OrdinalIgnoreCase))
            return "SkyFolio";
        if (output.Contains("google", StringComparison.OrdinalIgnoreCase) || output.Contains("Backdrop", StringComparison.OrdinalIgnoreCase))
            return "Google";
        return "Unknown";
    }

    public async Task SetScreensaverAsync(string ip, int port, string screensaver, CancellationToken ct = default)
    {
        var component = screensaver switch
        {
            "SkyFolio" => "com.snapwood.skyfolio/com.snapwood.skyfolio.DreamService",
            _ => "com.google.android.apps.tv.dreamx/.service.Backdrop"
        };
        await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell settings put secure screensaver_components {component}", null, ct);
    }

    public async Task<int> GetScreenTimeoutAsync(string ip, int port, CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell settings get system screen_off_timeout", null, ct);
        return int.TryParse(result.StandardOutput.Trim(), out var ms) ? ms : 0;
    }

    public async Task SetScreenTimeoutAsync(string ip, int port, int milliseconds, CancellationToken ct = default)
    {
        await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell settings put system screen_off_timeout {milliseconds}", null, ct);
    }

    public async Task<bool> IsLauncherDisabledAsync(string ip, int port, CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell pm list packages -d", null, ct);
        return result.StandardOutput.Contains("com.google.android.apps.tv.launcherx");
    }

    public async Task SetLauncherEnabledAsync(string ip, int port, bool enabled, CancellationToken ct = default)
    {
        if (enabled)
        {
            await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell pm enable com.google.android.apps.tv.launcherx", null, ct);
            await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell pm enable com.google.android.tungsten.setupwraith", null, ct);
        }
        else
        {
            await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell pm disable-user --user 0 com.google.android.apps.tv.launcherx", null, ct);
            await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell pm disable-user --user 0 com.google.android.tungsten.setupwraith", null, ct);
        }
    }

    public async Task StartShizukuAsync(string ip, int port, CancellationToken ct = default)
    {
        await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell sh /storage/emulated/0/Android/data/moe.shizuku.privileged.api/start.sh", null, ct);
    }

    public async Task<IReadOnlyList<string>> ListProjectivyBackupsAsync(string ip, int port, CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell ls /storage/emulated/0/Projectivy-Backups", null, ct);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            return [];
        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    public async Task RestoreProjectivyBackupAsync(string ip, int port, string filename, CancellationToken ct = default)
    {
        await _executor.ExecuteAsync("adb",
            $"{DeviceArg(ip, port)} shell am start -a android.intent.action.VIEW -d \"file:///storage/emulated/0/Projectivy-Backups/{filename}\" -n com.spocky.projengmenu/.ui.launcherActivities.ImportSettingsActivity",
            null, ct);
    }

    public async Task<IReadOnlyList<string>> GetConnectedDevicesAsync(CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("adb", "devices", null, ct);
        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(1) // skip "List of devices attached" header
            .Where(line => line.Contains('\t'))
            .Select(line => line.Split('\t')[0])
            .ToList();
    }

    /// <summary>
    /// Discovers ADB-advertising devices on the local network via <c>adb mdns services</c>.
    /// Output format (tab-separated, one device per line):
    /// <code>
    /// adb-&lt;serial&gt;    _adb._tcp           192.168.86.43:5555
    /// adb-&lt;serial&gt;    _adb-tls-connect._tcp  192.168.86.169:43423
    /// </code>
    /// A "List of discovered mdns services" header line is silently skipped
    /// (lacks three tab-separated columns).
    /// </summary>
    public async Task<IReadOnlyList<MdnsAdbDevice>> ScanMdnsAsync(CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("adb", "mdns services", null, ct);
        if (result.ExitCode != 0) return [];

        var entries = new List<MdnsAdbDevice>();
        foreach (var rawLine in result.StandardOutput.Split('\n'))
        {
            var parts = rawLine.Split('\t');
            if (parts.Length < 3) continue;
            var name = parts[0].Trim();
            var addressPort = parts[2].Trim();
            var colonIdx = addressPort.LastIndexOf(':');
            if (colonIdx <= 0) continue;
            var ip = addressPort[..colonIdx];
            if (!int.TryParse(addressPort[(colonIdx + 1)..], out var port)) continue;
            entries.Add(new MdnsAdbDevice(name, ip, port));
        }
        return entries;
    }

    public async Task<(int Width, int Height)?> GetScreenSizeAsync(string ip, int port, CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell wm size", null, ct);
        if (result.ExitCode != 0) return null;
        // Parse "Physical size: 1080x2424" or "Override size: 1080x2424"
        var match = System.Text.RegularExpressions.Regex.Match(result.StandardOutput,
            @"(?:Override|Physical) size:\s*(\d+)x(\d+)");
        if (!match.Success) return null;
        return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
    }

    public async Task UnlockWithPinAsync(string ip, int port, string pin, CancellationToken ct = default)
    {
        var dev = DeviceArg(ip, port);
        // Exact sequence from the original PowerShell script — no delays, separate adb calls
        await _executor.ExecuteAsync("adb", $"{dev} shell input keyevent 26", null, ct);
        await _executor.ExecuteAsync("adb", $"{dev} shell input keyevent 82", null, ct);
        await _executor.ExecuteAsync("adb", $"{dev} shell input text {pin}", null, ct);
        await _executor.ExecuteAsync("adb", $"{dev} shell input keyevent 66", null, ct);
    }

    public async Task DisconnectAllAsync(CancellationToken ct = default)
    {
        var devices = await GetConnectedDevicesAsync(ct);
        foreach (var device in devices)
        {
            await _executor.ExecuteAsync("adb", $"disconnect {device}", null, ct);
        }
    }

    /// <summary>
    /// Port of ws-scrcpy-web's <c>classifyDeviceKind</c> (src/server/goog-device/deviceKind.ts),
    /// extended with a watch probe. Five probes run in parallel. Watch wins over TV wins over
    /// the tablet/phone smallestWidthDp split, so a Wear-on-TV (if that ever existed) would
    /// classify as "watch" — the hardware feature declarations are the most specific signal.
    /// </summary>
    public async Task<string?> DetectDeviceKindAsync(string ip, int port, CancellationToken ct = default)
    {
        var dev = DeviceArg(ip, port);
        var probes = await Task.WhenAll(
            SafeShellAsync(dev, "getprop ro.build.characteristics", ct),
            SafeShellAsync(dev, "pm has-feature android.software.leanback", ct),
            SafeShellAsync(dev, "pm has-feature android.hardware.type.watch", ct),
            SafeShellAsync(dev, "wm size", ct),
            SafeShellAsync(dev, "wm density", ct));
        var characteristics = probes[0];
        var leanback = probes[1];
        var watch = probes[2];
        var wmSize = probes[3];
        var wmDensity = probes[4];

        if (WatchCharacteristicsRegex().IsMatch(characteristics) || watch.Trim() == "true")
        {
            return "watch";
        }
        if (TvCharacteristicsRegex().IsMatch(characteristics) || leanback.Trim() == "true")
        {
            return "tv";
        }

        var sizeMatch = WmSizeRegex().Match(wmSize);
        var densityMatch = WmDensityRegex().Match(wmDensity);
        if (!sizeMatch.Success || !densityMatch.Success) return null;

        var width = int.Parse(sizeMatch.Groups[1].Value);
        var height = int.Parse(sizeMatch.Groups[2].Value);
        var density = int.Parse(densityMatch.Groups[1].Value);
        var smallestDp = Math.Min(width, height) / (density / 160.0);
        return smallestDp >= 600 ? "tablet" : "phone";
    }

    private async Task<string> SafeShellAsync(string deviceArg, string shellCmd, CancellationToken ct)
    {
        try
        {
            var r = await _executor.ExecuteAsync("adb", $"{deviceArg} shell {shellCmd}", null, ct);
            return r.ExitCode == 0 ? r.StandardOutput : "";
        }
        catch
        {
            return "";
        }
    }

    public async Task<string> GetPropAsync(string ip, int port, string property, CancellationToken ct = default)
    {
        var raw = await SafeShellAsync(DeviceArg(ip, port), $"getprop {property}", ct);
        return raw.Trim();
    }

    [GeneratedRegex(@"\btv\b", RegexOptions.IgnoreCase)]
    private static partial Regex TvCharacteristicsRegex();

    [GeneratedRegex(@"\bwatch\b", RegexOptions.IgnoreCase)]
    private static partial Regex WatchCharacteristicsRegex();

    [GeneratedRegex(@"(?:Override|Physical) size:\s*(\d+)x(\d+)")]
    private static partial Regex WmSizeRegex();

    [GeneratedRegex(@"(?:Override|Physical) density:\s*(\d+)")]
    private static partial Regex WmDensityRegex();
}
