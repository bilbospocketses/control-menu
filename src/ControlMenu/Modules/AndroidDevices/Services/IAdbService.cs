namespace ControlMenu.Modules.AndroidDevices.Services;

public enum PowerState
{
    Awake,
    Asleep,
    Unknown
}

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
    Task ResetTcpPortAsync(int port, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetConnectedDevicesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetUsbDevicesAsync(CancellationToken ct = default);
    Task UnlockWithPinAsync(string ip, int port, string pin, CancellationToken ct = default);
    Task DisconnectAllAsync(CancellationToken ct = default);
}
