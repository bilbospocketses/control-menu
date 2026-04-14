using ControlMenu.Services;

namespace ControlMenu.Modules.Jellyfin.Services;

public class JellyfinService : IJellyfinService
{
    private readonly ICommandExecutor _executor;
    private readonly IConfigurationService _config;
    private readonly IHttpClientFactory _httpFactory;

    public JellyfinService(ICommandExecutor executor, IConfigurationService config, IHttpClientFactory httpFactory)
    {
        _executor = executor;
        _config = config;
        _httpFactory = httpFactory;
    }

    public async Task<ComposeParseResult> ParseComposeFileAsync(CancellationToken ct = default)
    {
        var composePath = await _config.GetSettingAsync("jellyfin-compose-path");
        if (string.IsNullOrEmpty(composePath))
            return new(null, null, null, "jellyfin-compose-path not configured");

        var result = ComposeParser.Parse(composePath);

        if (result.ContainerName is not null)
            await _config.SetSettingAsync("jellyfin-container-name", result.ContainerName);
        if (result.DbPath is not null)
            await _config.SetSettingAsync("jellyfin-db-path", result.DbPath);

        await _config.SetSettingAsync("jellyfin-backup-dir", OperationLogger.GetBackupDirectory());

        return result;
    }

    public async Task<string?> GetContainerIdAsync(CancellationToken ct = default)
    {
        var containerName = await _config.GetSettingAsync("jellyfin-container-name") ?? "jellyfin";
        var result = await _executor.ExecuteAsync("docker", $"ps -a --filter name=^/{containerName}$ --format {{{{.ID}}}}", null, ct);
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

    public async Task<bool> WaitForContainerReadyAsync(string containerId, int timeoutSeconds = 60, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var result = await _executor.ExecuteAsync("docker", $"logs --tail 20 {containerId}", null, ct);
            if (result.StandardOutput.Contains("Startup complete"))
                return true;
            await Task.Delay(2000, ct);
        }
        return false;
    }

    public async Task<string?> BackupDatabaseAsync(OperationLogger? logger = null, CancellationToken ct = default)
    {
        var dbPath = await _config.GetSettingAsync("jellyfin-db-path");
        var backupDir = await _config.GetSettingAsync("jellyfin-backup-dir");

        if (dbPath is null || backupDir is null)
        {
            logger?.Fail($"Backup failed: dbPath={dbPath ?? "null"}, backupDir={backupDir ?? "null"}");
            return null;
        }

        if (!File.Exists(dbPath))
        {
            logger?.Fail($"Database file not found: {dbPath}");
            return null;
        }

        Directory.CreateDirectory(backupDir);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupFileName = $"jellyfin_{timestamp}.db";
        var backupPath = Path.Combine(backupDir, backupFileName);

        try
        {
            File.Copy(dbPath, backupPath, overwrite: true);
            logger?.Ok($"Backup saved: {backupFileName}");
            return backupPath;
        }
        catch (Exception ex)
        {
            logger?.Fail($"Backup failed: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> UpdateDateCreatedAsync(OperationLogger? logger = null, CancellationToken ct = default)
    {
        var dbPath = await _config.GetSettingAsync("jellyfin-db-path");
        if (dbPath is null)
        {
            logger?.Fail("SQL update failed: jellyfin-db-path not configured");
            return false;
        }

        var result = await _executor.ExecuteAsync("sqlite3", $"\"{dbPath}\" \"UPDATE BaseItems SET DateCreated=PremiereDate WHERE PremiereDate IS NOT NULL;\"", null, ct);
        if (result.ExitCode == 0)
        {
            logger?.Ok("SQL update applied: DateCreated = PremiereDate");
            return true;
        }

        logger?.Fail($"SQL update failed (exit {result.ExitCode}): {result.StandardError}");
        return false;
    }

    public async Task CleanupOldBackupsAsync(OperationLogger? logger = null, CancellationToken ct = default)
    {
        var backupDir = await _config.GetSettingAsync("jellyfin-backup-dir");
        if (backupDir is null || !Directory.Exists(backupDir)) return;

        var retentionStr = await _config.GetSettingAsync("jellyfin-backup-retention-days");
        var retentionDays = int.TryParse(retentionStr, out var d) ? d : 5;
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var removed = 0;

        foreach (var file in Directory.GetFiles(backupDir, "*.db"))
        {
            if (File.GetLastWriteTimeUtc(file) < cutoff)
            {
                File.Delete(file);
                removed++;
            }
        }

        logger?.Ok($"Removed {removed} backup(s) older than {retentionDays} days");
    }

    public async Task<IReadOnlyList<JellyfinPerson>> GetPersonsMissingImagesAsync(CancellationToken ct = default)
    {
        var baseUrl = await _config.GetSettingAsync("jellyfin-base-url") ?? "http://127.0.0.1:8096";
        var apiKey = await _config.GetSecretAsync("jellyfin-api-key");
        if (apiKey is null) throw new InvalidOperationException("Jellyfin API key not configured");

        var client = _httpFactory.CreateClient();
        var url = $"{baseUrl}/emby/Persons?api_key={apiKey}";
        var json = await client.GetStringAsync(url, ct);

        var persons = new List<JellyfinPerson>();
        var itemRegex = new System.Text.RegularExpressions.Regex(
            @"""Id""\s*:\s*""(?<id>[^""]+)"".*?""Name""\s*:\s*""(?<name>[^""]+)"".*?""ImageTags""\s*:\s*\{(?<tags>[^}]*)\}",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        foreach (System.Text.RegularExpressions.Match match in itemRegex.Matches(json))
        {
            var tags = match.Groups["tags"].Value.Trim();
            if (string.IsNullOrEmpty(tags))
                persons.Add(new JellyfinPerson(match.Groups["id"].Value, match.Groups["name"].Value));
        }

        return persons.DistinctBy(p => p.Id).ToList();
    }

    public async Task TriggerPersonImageDownloadAsync(string personId, CancellationToken ct = default)
    {
        var baseUrl = await _config.GetSettingAsync("jellyfin-base-url") ?? "http://127.0.0.1:8096";
        var apiKey = await _config.GetSecretAsync("jellyfin-api-key");
        var userId = await _config.GetSettingAsync("jellyfin-user-id");
        if (apiKey is null || userId is null) return;

        var client = _httpFactory.CreateClient();
        var url = $"{baseUrl}/Users/{userId}/Items/{personId}?api_key={apiKey}";
        await client.GetAsync(url, ct);
    }
}
