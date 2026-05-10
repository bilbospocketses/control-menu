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
}
