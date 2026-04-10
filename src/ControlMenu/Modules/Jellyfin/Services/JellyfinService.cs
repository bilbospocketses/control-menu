using ControlMenu.Services;

namespace ControlMenu.Modules.Jellyfin.Services;

public class JellyfinService : IJellyfinService
{
    private readonly ICommandExecutor _executor;
    private readonly IConfigurationService _config;

    public JellyfinService(ICommandExecutor executor, IConfigurationService config)
    {
        _executor = executor;
        _config = config;
    }

    public async Task<string?> GetContainerIdAsync(CancellationToken ct = default)
    {
        var containerName = await _config.GetSettingAsync("jellyfin-container-name") ?? "jellyfin";
        var result = await _executor.ExecuteAsync("docker", $"ps --filter name={containerName} --format {{{{.ID}}}}", null, ct);
        var id = result.StandardOutput.Trim();
        return string.IsNullOrEmpty(id) ? null : id;
    }

    public async Task<bool> StopContainerAsync(string containerId, CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("docker", $"stop -t=15 {containerId}", null, ct);
        return result.ExitCode == 0;
    }

    public async Task<bool> StartContainerAsync(string containerId, CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("docker", $"start {containerId}", null, ct);
        return result.ExitCode == 0;
    }

    public async Task<string?> BackupDatabaseAsync(CancellationToken ct = default)
    {
        var dbPath = await _config.GetSettingAsync("jellyfin-db-path");
        var backupDir = await _config.GetSettingAsync("jellyfin-backup-dir");
        if (dbPath is null || backupDir is null) return null;

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupFileName = $"jellyfin_{timestamp}.db";

        var definition = new CommandDefinition
        {
            WindowsCommand = "cmd",
            WindowsArguments = $"/c copy \"{dbPath}\" \"{Path.Combine(backupDir, backupFileName)}\"",
            LinuxCommand = "cp",
            LinuxArguments = $"\"{dbPath}\" \"{Path.Combine(backupDir, backupFileName)}\""
        };

        var result = await _executor.ExecuteAsync(definition, ct);
        return result.ExitCode == 0 ? Path.Combine(backupDir, backupFileName) : null;
    }

    public async Task<bool> UpdateDateCreatedAsync(CancellationToken ct = default)
    {
        var dbPath = await _config.GetSettingAsync("jellyfin-db-path");
        if (dbPath is null) return false;

        var result = await _executor.ExecuteAsync("sqlite3", $"\"{dbPath}\" \"UPDATE BaseItems SET DateCreated=PremiereDate;\"", null, ct);
        return result.ExitCode == 0;
    }

    public async Task CleanupOldBackupsAsync(int retentionDays = 5, CancellationToken ct = default)
    {
        var backupDir = await _config.GetSettingAsync("jellyfin-backup-dir");
        if (backupDir is null) return;

        var definition = new CommandDefinition
        {
            WindowsCommand = "powershell",
            WindowsArguments = $"-Command \"Get-ChildItem -Path '{backupDir}' -Filter '*.db' | Where-Object {{ $_.LastWriteTime -lt (Get-Date).AddDays(-{retentionDays}) }} | Remove-Item -Force\"",
            LinuxCommand = "find",
            LinuxArguments = $"\"{backupDir}\" -name '*.db' -mtime +{retentionDays} -delete"
        };

        await _executor.ExecuteAsync(definition, ct);
    }
}
