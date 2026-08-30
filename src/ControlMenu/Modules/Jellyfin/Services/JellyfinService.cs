using ControlMenu.Services;

namespace ControlMenu.Modules.Jellyfin.Services;

public class JellyfinService : IJellyfinService
{
    private readonly ICommandExecutor _executor;
    private readonly IConfigurationService _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IDependencyPathResolver _resolver;
    private readonly IJellyfinDirectoryResolver _directoryResolver;

    public JellyfinService(ICommandExecutor executor, IConfigurationService config, IHttpClientFactory httpFactory, IDependencyPathResolver resolver, IJellyfinDirectoryResolver directoryResolver)
    {
        _executor = executor;
        _config = config;
        _httpFactory = httpFactory;
        _resolver = resolver;
        _directoryResolver = directoryResolver;
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

    /// <summary>Log line Jellyfin emits once the server is actually serving.</summary>
    /// <remarks>
    /// Deliberately case-sensitive. The same log also carries "Core startup complete" (lowercase s)
    /// and plugin task lines like "MediaBar Startup Completed" (capital C); neither means the server
    /// is up, and neither matches this.
    /// </remarks>
    private const string StartupMarker = "Startup complete";

    /// <summary>
    /// Waits until the container is actually serving.
    /// </summary>
    /// <remarks>
    /// This used to poll <c>docker logs --since &lt;timestamp&gt;</c> for <see cref="StartupMarker"/>.
    /// That is unusable: on a long-lived container <c>--since</c> silently returns ZERO lines for a
    /// recent timestamp while returning the full log for an old one (measured on a container with
    /// 467k lines: <c>--since 2026-06-01</c> returned everything, <c>--since &lt;today&gt;</c> returned
    /// nothing), so readiness could never be observed even though Jellyfin logged the marker ~14s
    /// after start. Every step of the db-date-update routine would succeed and the run would still
    /// report as failed.
    ///
    /// Readiness is now decided by two independent signals, whichever arrives first:
    /// the container's own healthcheck (authoritative when the image defines one -- the Jellyfin
    /// image does), and failing that a <c>--tail</c> read whose docker timestamps are compared
    /// against the container's start time. The timestamp comparison is what <c>--since</c> was
    /// really there for: a long-lived log holds the marker from every previous start, and matching
    /// one of those would report ready while the server is still booting.
    ///
    /// The default budget is 120s rather than 60s because a healthcheck only flips to "healthy" on
    /// its first passing probe, and intervals of 30s are common (the Jellyfin image uses exactly that).
    /// </remarks>
    public async Task<bool> WaitForContainerReadyAsync(string containerId, int timeoutSeconds = 120, CancellationToken ct = default)
    {
        var startedAt = await GetContainerStartedAtAsync(containerId, ct);
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (!ct.IsCancellationRequested)
        {
            if (await IsContainerHealthyAsync(containerId, ct)) return true;
            if (await LogsReportStartupAsync(containerId, startedAt, ct)) return true;
            if (DateTime.UtcNow >= deadline) return false;

            try { await Task.Delay(2000, ct); }
            catch (OperationCanceledException) { return false; }
        }
        return false;
    }

    /// <summary>Container start time, used to reject a marker left over from an earlier start.</summary>
    private async Task<DateTimeOffset?> GetContainerStartedAtAsync(string containerId, CancellationToken ct)
    {
        var result = await _executor.ExecuteAsync(
            "docker", $"inspect -f {{{{.State.StartedAt}}}} {containerId}", null, ct);
        return result.ExitCode == 0 && TryParseTimestamp(result.StandardOutput.Trim(), out var started)
            ? started
            : null;
    }

    /// <summary>Reports the image's own healthcheck verdict; false when the image defines none.</summary>
    private async Task<bool> IsContainerHealthyAsync(string containerId, CancellationToken ct)
    {
        var result = await _executor.ExecuteAsync(
            "docker",
            $"inspect -f {{{{if .State.Health}}}}{{{{.State.Health.Status}}}}{{{{else}}}}none{{{{end}}}} {containerId}",
            null, ct);
        return result.ExitCode == 0
            && result.StandardOutput.Trim().Equals("healthy", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Looks for the startup marker in recent logs, ignoring anything older than this start.</summary>
    private async Task<bool> LogsReportStartupAsync(string containerId, DateTimeOffset? startedAt, CancellationToken ct)
    {
        var result = await _executor.ExecuteAsync("docker", $"logs -t --tail 200 {containerId}", null, ct);

        var combined = result.StandardOutput + Environment.NewLine + result.StandardError;
        foreach (var line in combined.Split('\r', '\n'))
        {
            if (!line.Contains(StartupMarker, StringComparison.Ordinal)) continue;

            // No start time available (inspect failed) -- the marker is the only signal we have.
            if (startedAt is null) return true;

            // `docker logs -t` prefixes each line with an RFC3339 timestamp.
            var split = line.IndexOf(' ');
            if (split > 0
                && TryParseTimestamp(line[..split], out var stamp)
                && stamp >= startedAt.Value)
                return true;
        }
        return false;
    }

    private static bool TryParseTimestamp(string value, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out parsed);

    public async Task<string?> BackupDatabaseAsync(OperationLogger? logger = null, CancellationToken ct = default)
    {
        var dbPath = await _config.GetSettingAsync("jellyfin-db-path");
        var backupDir = await _directoryResolver.GetBackupDirectoryAsync();
        Directory.CreateDirectory(backupDir);

        if (dbPath is null)
        {
            logger?.Fail("Backup failed: dbPath is not configured");
            return null;
        }

        if (!File.Exists(dbPath))
        {
            logger?.Fail($"Database file not found: {dbPath}");
            return null;
        }
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

        // Structured args: dbPath and the SQL are discrete ArgumentList elements, so a dbPath
        // derived from a compose file (ComposeParser) cannot inject extra sqlite3 arguments.
        var result = await _executor.ExecuteResolvedAsync(_resolver, "jellyfin", "sqlite3",
            new[] { dbPath, "UPDATE BaseItems SET DateCreated=PremiereDate WHERE PremiereDate IS NOT NULL;" },
            null, ct);
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
        var backupDir = await _directoryResolver.GetBackupDirectoryAsync();
        if (!Directory.Exists(backupDir)) return;

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
        // API key goes in a header, never the URL query string (query strings land in proxy/access
        // logs and request traces).
        client.DefaultRequestHeaders.Add("X-Emby-Token", apiKey);
        var json = await client.GetStringAsync($"{baseUrl}/emby/Persons", ct);

        var persons = new List<JellyfinPerson>();
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("Items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                var id = item.GetProperty("Id").GetString();
                var name = item.GetProperty("Name").GetString();
                if (id is null || name is null) continue;

                var hasImage = item.TryGetProperty("ImageTags", out var tags)
                    && tags.ValueKind == System.Text.Json.JsonValueKind.Object
                    && tags.EnumerateObject().Any();

                if (!hasImage)
                    persons.Add(new JellyfinPerson(id, name));
            }
        }

        return persons.DistinctBy(p => p.Id).ToList();
    }

    public async Task TriggerPersonImageDownloadAsync(string personId, CancellationToken ct = default)
    {
        var config = await GetApiConfigAsync();
        await TriggerPersonImageDownloadAsync(personId, config, ct);
    }

    public async Task<JellyfinApiConfig> GetApiConfigAsync()
    {
        var baseUrl = await _config.GetSettingAsync("jellyfin-base-url") ?? "http://127.0.0.1:8096";
        var apiKey = await _config.GetSecretAsync("jellyfin-api-key")
            ?? throw new InvalidOperationException("Jellyfin API key not configured");
        var userId = await _config.GetSettingAsync("jellyfin-user-id");
        return new JellyfinApiConfig(baseUrl, apiKey, userId);
    }

    public async Task TriggerPersonImageDownloadAsync(string personId, JellyfinApiConfig apiConfig, CancellationToken ct = default)
    {
        // POST /Items/{id}/Refresh is the only call that makes Jellyfin query its metadata
        // providers and download an image. This previously issued a GET against
        // /Users/{userId}/Items/{personId}, which merely READS the item — no provider is ever
        // contacted, so the job reported success while downloading nothing.
        //
        // Two deliberate choices:
        //  - No UserId guard. Refresh is not user-scoped, and the old guard silently turned the
        //    entire job into a no-op whenever jellyfin-user-id happened to be unset.
        //  - replaceAllImages=false so an existing image is never overwritten; this only fills gaps.
        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Emby-Token", apiConfig.ApiKey);
        var url = $"{apiConfig.BaseUrl}/Items/{Uri.EscapeDataString(personId)}/Refresh"
                  + "?metadataRefreshMode=FullRefresh"
                  + "&imageRefreshMode=FullRefresh"
                  + "&replaceAllImages=false";
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
        await client.PostAsync(url, content: null, timeoutCts.Token);
    }
}
