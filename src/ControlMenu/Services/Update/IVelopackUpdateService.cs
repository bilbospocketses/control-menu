namespace ControlMenu.Services.Update;

public interface IVelopackUpdateService
{
    Task<UpdateAvailability> CheckForUpdatesAsync(CancellationToken ct = default);
    Task DownloadUpdateAsync(CancellationToken ct = default);
    /// <summary>
    /// Sets exit code 75 + requests app shutdown. Launcher's ChildSupervisor
    /// reads exit-75 and (Phase 3) runs Velopack apply orchestration.
    /// Phase 1: launcher logs the request and exits.
    /// </summary>
    void RequestApplyUpdate();
}

public sealed record UpdateAvailability(bool HasUpdate, string? AvailableVersion, string? CurrentVersion);
