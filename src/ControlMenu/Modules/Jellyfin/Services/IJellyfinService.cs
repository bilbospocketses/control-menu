namespace ControlMenu.Modules.Jellyfin.Services;

public interface IJellyfinService
{
    Task<string?> GetContainerIdAsync(CancellationToken ct = default);
    Task<bool> StopContainerAsync(string containerId, CancellationToken ct = default);
    Task<bool> StartContainerAsync(string containerId, CancellationToken ct = default);
    Task<bool> WaitForContainerReadyAsync(string containerId, int timeoutSeconds = 60, CancellationToken ct = default);
    Task<string?> BackupDatabaseAsync(OperationLogger? logger = null, CancellationToken ct = default);
    Task<bool> UpdateDateCreatedAsync(OperationLogger? logger = null, CancellationToken ct = default);
    Task CleanupOldBackupsAsync(OperationLogger? logger = null, CancellationToken ct = default);
    Task<ComposeParseResult> ParseComposeFileAsync(CancellationToken ct = default);
    Task<IReadOnlyList<JellyfinPerson>> GetPersonsMissingImagesAsync(CancellationToken ct = default);
    Task TriggerPersonImageDownloadAsync(string personId, CancellationToken ct = default);
    Task<JellyfinApiConfig> GetApiConfigAsync();
    Task TriggerPersonImageDownloadAsync(string personId, JellyfinApiConfig apiConfig, CancellationToken ct = default);
}
