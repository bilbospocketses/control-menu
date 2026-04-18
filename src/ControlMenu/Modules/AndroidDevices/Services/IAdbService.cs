namespace ControlMenu.Modules.AndroidDevices.Services;

public enum PowerState
{
    Awake,
    Asleep,
    Unknown
}

/// <summary>
/// A device discovered on the local network via <c>adb mdns services</c>.
/// ServiceName is the full <c>adb-&lt;serial&gt;-&lt;name&gt;._adb(-tls-connect)._tcp</c> label.
/// </summary>
public record MdnsAdbDevice(string ServiceName, string IpAddress, int Port);

public interface IAdbService
{
    Task<bool> ConnectAsync(string ip, int port, CancellationToken ct = default);
    Task DisconnectAsync(string ip, int port, CancellationToken ct = default);
    Task<PowerState> GetPowerStateAsync(string ip, int port, CancellationToken ct = default);
    Task RebootAsync(string ip, int port, CancellationToken ct = default);
    Task TogglePowerAsync(string ip, int port, CancellationToken ct = default);
    Task<string> GetScreensaverAsync(string ip, int port, CancellationToken ct = default);
    Task SetScreensaverAsync(string ip, int port, string screensaver, CancellationToken ct = default);
    Task<int> GetScreenTimeoutAsync(string ip, int port, CancellationToken ct = default);
    Task SetScreenTimeoutAsync(string ip, int port, int milliseconds, CancellationToken ct = default);
    Task<bool> IsLauncherDisabledAsync(string ip, int port, CancellationToken ct = default);
    Task SetLauncherEnabledAsync(string ip, int port, bool enabled, CancellationToken ct = default);
    Task StartShizukuAsync(string ip, int port, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListProjectivyBackupsAsync(string ip, int port, CancellationToken ct = default);
    Task RestoreProjectivyBackupAsync(string ip, int port, string filename, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetConnectedDevicesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MdnsAdbDevice>> ScanMdnsAsync(CancellationToken ct = default);
    /// <summary>
    /// Classifies an Android device as <c>"phone"</c>, <c>"tablet"</c>, <c>"tv"</c>, or
    /// <c>"watch"</c>. Extends ws-scrcpy-web's four-signal classifier with a fifth probe
    /// (<c>pm has-feature android.hardware.type.watch</c>) so Wear OS devices can be
    /// distinguished from phones. Returns <c>null</c> when every probe exited non-zero.
    /// </summary>
    Task<string?> DetectDeviceKindAsync(string ip, int port, CancellationToken ct = default);
    /// <summary>
    /// Reads a single <c>getprop &lt;name&gt;</c> value from the device. Returns an empty
    /// string if the property is unset or the probe exited non-zero.
    /// </summary>
    Task<string> GetPropAsync(string ip, int port, string property, CancellationToken ct = default);
    Task<(int Width, int Height)?> GetScreenSizeAsync(string ip, int port, CancellationToken ct = default);
    Task UnlockWithPinAsync(string ip, int port, string pin, CancellationToken ct = default);
    Task DisconnectAllAsync(CancellationToken ct = default);
}
