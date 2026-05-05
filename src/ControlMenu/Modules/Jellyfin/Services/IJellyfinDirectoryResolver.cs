namespace ControlMenu.Modules.Jellyfin.Services;

public interface IJellyfinDirectoryResolver
{
    Task<string> GetBackupDirectoryAsync();
    Task<string> GetLogDirectoryAsync();

    /// <summary>
    /// Best-effort move of files matching <paramref name="searchPattern"/> from
    /// <paramref name="oldDir"/> to <paramref name="newDir"/>. Creates <paramref name="newDir"/>
    /// if missing. Per-file failures (e.g. file locks) do not abort the batch.
    /// </summary>
    Task<DirectoryMigrationResult> MigrateFilesAsync(string oldDir, string newDir, string searchPattern);
}
