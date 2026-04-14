using ControlMenu.Services;

namespace ControlMenu.Modules.AndroidDevices.Services;

public class AdbService : IAdbService
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

    public async Task ResetTcpPortAsync(int port, CancellationToken ct = default)
    {
        await _executor.ExecuteAsync("adb", $"tcpip {port}", null, ct);
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

    public async Task<IReadOnlyList<string>> GetUsbDevicesAsync(CancellationToken ct = default)
    {
        var devices = await GetConnectedDevicesAsync(ct);
        // USB serials don't contain ':' (network devices look like 192.168.1.100:5555)
        return devices.Where(d => !d.Contains(':')).ToList();
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

}
