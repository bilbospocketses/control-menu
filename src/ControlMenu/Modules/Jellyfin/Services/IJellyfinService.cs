namespace ControlMenu.Modules.Jellyfin.Services;

public interface IJellyfinService
{
    Task<string?> GetContainerIdAsync(CancellationToken ct = default);
    Task<bool> StopContainerAsync(string containerId, CancellationToken ct = default);
    Task<bool> StartContainerAsync(string containerId, CancellationToken ct = default);
    Task<string?> BackupDatabaseAsync(CancellationToken ct = default);
    Task<bool> UpdateDateCreatedAsync(CancellationToken ct = default);
    Task CleanupOldBackupsAsync(int retentionDays = 5, CancellationToken ct = default);
    Task<IReadOnlyList<JellyfinPerson>> GetPersonsMissingImagesAsync(CancellationToken ct = default);
    Task TriggerPersonImageDownloadAsync(string personId, CancellationToken ct = default);
}
