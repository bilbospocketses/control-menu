using System.Globalization;
using System.Text.RegularExpressions;
using ControlMenu.Services;

namespace ControlMenu.Modules.AndroidDevices.Services;

public partial class AdbService : IAdbService
{
    private readonly ICommandExecutor _executor;
    private readonly IDependencyPathResolver _resolver;

    // Path-traversal / shell-metacharacter set rejected in values that adb forwards to the
    // device shell (a Projectivy backup filename). adb joins its post-`shell` arguments with
    // spaces and hands the result to the device's /system/bin/sh, so a value reaching the shell
    // must be free of separators and command metacharacters even though the host-side args are
    // already structured.
    private static readonly char[] ShellUnsafeChars =
        ['/', '\\', '"', '\'', ' ', '\t', '\r', '\n', ';', '&', '|', '$', '`', '<', '>', '*', '?'];

    public AdbService(ICommandExecutor executor, IDependencyPathResolver resolver)
    {
        _executor = executor;
        _resolver = resolver;
    }

    private static string Endpoint(string ip, int port) => $"{ip}:{port}";

    /// <summary>Builds adb argv for a device-targeted command: <c>["-s", "ip:port", ...rest]</c>.</summary>
    private static string[] Device(string ip, int port, params string[] rest) =>
        ["-s", Endpoint(ip, port), ..rest];

    // Structured-argument invocation: each token is a discrete ArgumentList element, so values
    // derived from network discovery (ip/port from `adb mdns services`) can never be split or
    // injected into extra host-side adb arguments.
    private Task<CommandResult> AdbAsync(IReadOnlyList<string> args, CancellationToken ct = default) =>
        _executor.ExecuteResolvedAsync(_resolver, "android-devices", "adb", args, null, ct);

    public async Task<bool> ConnectAsync(string ip, int port, CancellationToken ct = default)
    {
        var result = await AdbAsync(["connect", Endpoint(ip, port)], ct);
        return result.ExitCode == 0 && result.StandardOutput.Contains("connected");
    }

    public async Task DisconnectAsync(string ip, int port, CancellationToken ct = default)
    {
        await AdbAsync(["disconnect", Endpoint(ip, port)], ct);
    }

    public async Task<PowerState> GetPowerStateAsync(string ip, int port, CancellationToken ct = default)
    {
        var result = await AdbAsync(Device(ip, port, "shell", "dumpsys", "power"), ct);
        if (result.ExitCode != 0) return PowerState.Unknown;
        if (result.StandardOutput.Contains("mwakefulness=awake", StringComparison.OrdinalIgnoreCase)
            || result.StandardOutput.Contains("mWakefulness=Awake"))
            return PowerState.Awake;
        return PowerState.Asleep;
    }

    public async Task RebootAsync(string ip, int port, CancellationToken ct = default)
    {
        await AdbAsync(Device(ip, port, "shell", "reboot"), ct);
    }

    public async Task TogglePowerAsync(string ip, int port, CancellationToken ct = default)
    {
        await AdbAsync(Device(ip, port, "shell", "input", "keyevent", "KEYCODE_POWER"), ct);
    }

    public async Task<string> GetScreensaverAsync(string ip, int port, CancellationToken ct = default)
    {
        var result = await AdbAsync(Device(ip, port, "shell", "settings", "get", "secure", "screensaver_components"), ct);
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
        await AdbAsync(Device(ip, port, "shell", "settings", "put", "secure", "screensaver_components", component), ct);
    }

    public async Task<int> GetScreenTimeoutAsync(string ip, int port, CancellationToken ct = default)
    {
        var result = await AdbAsync(Device(ip, port, "shell", "settings", "get", "system", "screen_off_timeout"), ct);
        return int.TryParse(result.StandardOutput.Trim(), out var ms) ? ms : 0;
    }

    public async Task SetScreenTimeoutAsync(string ip, int port, int milliseconds, CancellationToken ct = default)
    {
        await AdbAsync(Device(ip, port, "shell", "settings", "put", "system", "screen_off_timeout",
            milliseconds.ToString(CultureInfo.InvariantCulture)), ct);
    }

    public async Task<bool> IsLauncherDisabledAsync(string ip, int port, CancellationToken ct = default)
    {
        var result = await AdbAsync(Device(ip, port, "shell", "pm", "list", "packages", "-d"), ct);
        return result.StandardOutput.Contains("com.google.android.apps.tv.launcherx");
    }

    public async Task SetLauncherEnabledAsync(string ip, int port, bool enabled, CancellationToken ct = default)
    {
        if (enabled)
        {
            await AdbAsync(Device(ip, port, "shell", "pm", "enable", "com.google.android.apps.tv.launcherx"), ct);
            await AdbAsync(Device(ip, port, "shell", "pm", "enable", "com.google.android.tungsten.setupwraith"), ct);
        }
        else
        {
            await AdbAsync(Device(ip, port, "shell", "pm", "disable-user", "--user", "0", "com.google.android.apps.tv.launcherx"), ct);
            await AdbAsync(Device(ip, port, "shell", "pm", "disable-user", "--user", "0", "com.google.android.tungsten.setupwraith"), ct);
        }
    }

    public async Task StartShizukuAsync(string ip, int port, CancellationToken ct = default)
    {
        await AdbAsync(Device(ip, port, "shell", "sh", "/storage/emulated/0/Android/data/moe.shizuku.privileged.api/start.sh"), ct);
    }

    public async Task<IReadOnlyList<string>> ListProjectivyBackupsAsync(string ip, int port, CancellationToken ct = default)
    {
        var result = await AdbAsync(Device(ip, port, "shell", "ls", "/storage/emulated/0/Projectivy-Backups"), ct);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            return [];
        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    public async Task RestoreProjectivyBackupAsync(string ip, int port, string filename, CancellationToken ct = default)
    {
        // filename comes from an on-device `ls` (attacker-influenceable: anything that can write a
        // file into that directory controls the name). Reject traversal + shell metacharacters
        // before it reaches the device shell via the file:// URI.
        if (string.IsNullOrEmpty(filename)
            || filename.IndexOfAny(ShellUnsafeChars) >= 0
            || filename.Contains(".."))
        {
            throw new ArgumentException("Invalid Projectivy backup filename.", nameof(filename));
        }

        await AdbAsync(Device(ip, port, "shell", "am", "start", "-a", "android.intent.action.VIEW",
            "-d", $"file:///storage/emulated/0/Projectivy-Backups/{filename}",
            "-n", "com.spocky.projengmenu/.ui.launcherActivities.ImportSettingsActivity"), ct);
    }

    public async Task<IReadOnlyList<string>> GetConnectedDevicesAsync(CancellationToken ct = default)
    {
        var result = await AdbAsync(["devices"], ct);
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
    /// adb-&lt;serial&gt;    _adb._tcp           192.168.1.43:5555
    /// adb-&lt;serial&gt;    _adb-tls-connect._tcp  192.168.1.169:43423
    /// </code>
    /// A "List of discovered mdns services" header line is silently skipped
    /// (lacks three tab-separated columns).
    /// </summary>
    public async Task<IReadOnlyList<MdnsAdbDevice>> ScanMdnsAsync(CancellationToken ct = default)
    {
        var result = await AdbAsync(["mdns", "services"], ct);
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
        var result = await AdbAsync(Device(ip, port, "shell", "wm", "size"), ct);
        if (result.ExitCode != 0) return null;
        // Parse "Physical size: 1080x2424" or "Override size: 1080x2424"
        var match = WmSizeRegex().Match(result.StandardOutput);
        if (!match.Success) return null;
        return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
    }

    public async Task UnlockWithPinAsync(string ip, int port, string pin, CancellationToken ct = default)
    {
        // The PIN is forwarded to the device shell via `input text`. Restrict it to digits so a
        // value like "1234;reboot" can neither inject a device-side command nor (with the
        // structured host args below) split into extra adb arguments.
        if (string.IsNullOrEmpty(pin) || !pin.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("PIN must be numeric.", nameof(pin));
        }

        // Exact sequence from the original PowerShell script — no delays, separate adb calls
        await AdbAsync(Device(ip, port, "shell", "input", "keyevent", "26"), ct);
        await AdbAsync(Device(ip, port, "shell", "input", "keyevent", "82"), ct);
        await AdbAsync(Device(ip, port, "shell", "input", "text", pin), ct);
        await AdbAsync(Device(ip, port, "shell", "input", "keyevent", "66"), ct);
    }

    public async Task DisconnectAllAsync(CancellationToken ct = default)
    {
        var devices = await GetConnectedDevicesAsync(ct);
        foreach (var device in devices)
        {
            await AdbAsync(["disconnect", device], ct);
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
        var probes = await Task.WhenAll(
            SafeShellAsync(ip, port, ["getprop", "ro.build.characteristics"], ct),
            SafeShellAsync(ip, port, ["pm", "has-feature", "android.software.leanback"], ct),
            SafeShellAsync(ip, port, ["pm", "has-feature", "android.hardware.type.watch"], ct),
            SafeShellAsync(ip, port, ["wm", "size"], ct),
            SafeShellAsync(ip, port, ["wm", "density"], ct));
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

    private async Task<string> SafeShellAsync(string ip, int port, IReadOnlyList<string> shellArgs, CancellationToken ct)
    {
        try
        {
            string[] argv = ["-s", Endpoint(ip, port), "shell", ..shellArgs];
            var r = await AdbAsync(argv, ct);
            return r.ExitCode == 0 ? r.StandardOutput : "";
        }
        catch
        {
            return "";
        }
    }

    public async Task<string> GetPropAsync(string ip, int port, string property, CancellationToken ct = default)
    {
        var raw = await SafeShellAsync(ip, port, ["getprop", property], ct);
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
