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

    /// <summary>
    /// Lists the My Media tiles from <c>/UserViews</c> -- libraries and generated views alike --
    /// with whether each has a card today and whether Jellyfin can rebuild it.
    /// </summary>
    Task<IReadOnlyList<MediaCardTarget>> GetMediaCardTargetsAsync(JellyfinApiConfig apiConfig, CancellationToken ct = default);

    /// <summary>
    /// Downloads a library's current card into the Jellyfin backup directory. Returns the file
    /// written, or <c>null</c> when the library has no card to preserve.
    /// </summary>
    Task<string?> BackupLibraryCardAsync(string libraryId, string libraryName, JellyfinApiConfig apiConfig, CancellationToken ct = default);

    Task DeleteLibraryCardAsync(string libraryId, JellyfinApiConfig apiConfig, CancellationToken ct = default);

    /// <summary>Asks Jellyfin to regenerate the library's card, refreshing images only.</summary>
    Task RefreshLibraryCardAsync(string libraryId, JellyfinApiConfig apiConfig, CancellationToken ct = default);

    /// <summary>
    /// Uploads a previously backed-up card back onto the item. Deleting an image also drops its
    /// <c>BaseItemImageInfos</c> row, so this must go through the API -- copying the file back into
    /// the metadata folder would leave Jellyfin unaware of it.
    /// </summary>
    Task RestoreLibraryCardAsync(string libraryId, string backupPath, JellyfinApiConfig apiConfig, CancellationToken ct = default);

    /// <summary>
    /// Newest backed-up card for a library name, or <c>null</c> if none was ever taken. Lets a card
    /// deleted by an earlier failed run be put back, when there is no current card to roll back to.
    /// </summary>
    Task<string?> FindLatestCardBackupAsync(string libraryName);

    Task<bool> HasLibraryCardAsync(string libraryId, JellyfinApiConfig apiConfig, CancellationToken ct = default);
}
