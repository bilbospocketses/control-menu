using ControlMenu.Common.Paths;
using ControlMenu.Services;

namespace ControlMenu.Modules.Jellyfin.Services;

public sealed class JellyfinDirectoryResolver : IJellyfinDirectoryResolver
{
    private const string BackupDirectoryKey = "jellyfin-backup-directory";
    private const string LogDirectoryKey = "jellyfin-log-directory";

    private readonly IConfigurationService _config;
    private readonly IDataPathResolver _paths;

    public JellyfinDirectoryResolver(IConfigurationService config, IDataPathResolver paths)
    {
        _config = config;
        _paths = paths;
    }

    public async Task<string> GetBackupDirectoryAsync()
    {
        var overridePath = await _config.GetSettingAsync(BackupDirectoryKey);
        return string.IsNullOrWhiteSpace(overridePath)
            ? OperationLogger.GetDefaultBackupDirectory(_paths)
            : overridePath;
    }

    public async Task<string> GetLogDirectoryAsync()
    {
        var overridePath = await _config.GetSettingAsync(LogDirectoryKey);
        return string.IsNullOrWhiteSpace(overridePath)
            ? OperationLogger.GetDefaultLogDirectory(_paths)
            : overridePath;
    }

    public Task<DirectoryMigrationResult> MigrateFilesAsync(string oldDir, string newDir, string searchPattern)
    {
        try
        {
            Directory.CreateDirectory(newDir);
        }
        catch (Exception ex)
        {
            return Task.FromResult(DirectoryMigrationResult.Error(
                $"Could not create target directory: {ex.Message}"));
        }

        if (!Directory.Exists(oldDir))
        {
            return Task.FromResult(DirectoryMigrationResult.Ok(movedCount: 0, failedFiles: []));
        }

        var moved = 0;
        var failed = new List<string>();

        foreach (var src in Directory.GetFiles(oldDir, searchPattern))
        {
            var name = Path.GetFileName(src);
            var dst = Path.Combine(newDir, name);
            try
            {
                if (File.Exists(dst))
                {
                    // Same-named file already present at destination — skip and report as failed.
                    failed.Add(name);
                    continue;
                }
                File.Move(src, dst);
                moved++;
            }
            catch (IOException)
            {
                failed.Add(name);
            }
            catch (UnauthorizedAccessException)
            {
                failed.Add(name);
            }
        }

        return Task.FromResult(DirectoryMigrationResult.Ok(moved, failed));
    }

    public async Task<DirectoryMigrationResult> MigrateBackupsAsync(string oldDir, string newDir)
    {
        var databases = await MigrateFilesAsync(oldDir, newDir, "*.db");
        if (!databases.Success) return databases;

        // Only when there is something to move: MigrateFilesAsync creates its target first, and
        // an empty media-cards/ under the new path would be noise.
        var oldCards = Path.Combine(oldDir, IJellyfinDirectoryResolver.MediaCardsFolder);
        if (!Directory.Exists(oldCards)) return databases;

        var cards = await MigrateFilesAsync(oldCards, Path.Combine(newDir, IJellyfinDirectoryResolver.MediaCardsFolder), "*");
        if (!cards.Success) return cards;

        return DirectoryMigrationResult.Ok(
            databases.MovedCount + cards.MovedCount,
            [.. databases.FailedFiles, .. cards.FailedFiles.Select(f => Path.Combine(IJellyfinDirectoryResolver.MediaCardsFolder, f))]);
    }

    public BackupDirectoryStats GetBackupStats(string backupDir)
    {
        if (!Directory.Exists(backupDir)) return new BackupDirectoryStats(0, 0);

        var cardsDir = Path.Combine(backupDir, IJellyfinDirectoryResolver.MediaCardsFolder);
        var files = Directory.GetFiles(backupDir, "*.db")
            .Concat(Directory.Exists(cardsDir) ? Directory.GetFiles(cardsDir) : [])
            .ToArray();

        // A file can vanish between the listing and the stat (retention runs on its own
        // schedule); a missing one simply contributes nothing.
        var bytes = files.Sum(f => { try { return new FileInfo(f).Length; } catch (IOException) { return 0L; } });
        return new BackupDirectoryStats(files.Length, bytes);
    }
}
