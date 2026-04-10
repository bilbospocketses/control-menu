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
