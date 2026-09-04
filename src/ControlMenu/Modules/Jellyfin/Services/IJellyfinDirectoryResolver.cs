namespace ControlMenu.Modules.Jellyfin.Services;

public interface IJellyfinDirectoryResolver
{
    /// <summary>The folder under the backup directory that holds library-card backups.</summary>
    const string MediaCardsFolder = "media-cards";

    Task<string> GetBackupDirectoryAsync();
    Task<string> GetLogDirectoryAsync();

    /// <summary>
    /// Best-effort move of files matching <paramref name="searchPattern"/> from
    /// <paramref name="oldDir"/> to <paramref name="newDir"/>. Creates <paramref name="newDir"/>
    /// if missing. Per-file failures (e.g. file locks) do not abort the batch.
    /// </summary>
    Task<DirectoryMigrationResult> MigrateFilesAsync(string oldDir, string newDir, string searchPattern);

    /// <summary>
    /// Moves a whole Jellyfin backup directory: the <c>*.db</c> database backups at its root
    /// and the <c>media-cards/</c> folder of library-card backups beneath it. Migrating only
    /// <c>*.db</c> left the card backups orphaned at the old path, where the Media Cards page's
    /// Restore -- which reads the current path -- could not see them.
    /// </summary>
    Task<DirectoryMigrationResult> MigrateBackupsAsync(string oldDir, string newDir);

    /// <summary>
    /// What the backup directory holds: the <c>*.db</c> files at its root plus every card backup
    /// under <c>media-cards/</c>. Counting only <c>*.db</c> hid the disk the cards occupied.
    /// </summary>
    BackupDirectoryStats GetBackupStats(string backupDir);
}

/// <param name="FileCount">Database backups plus card backups.</param>
/// <param name="TotalBytes">Their combined size on disk.</param>
public sealed record BackupDirectoryStats(int FileCount, long TotalBytes);
