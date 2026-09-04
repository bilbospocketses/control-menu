using System.Text.RegularExpressions;
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

        // Card backups are pruned by count, never by age. The newest is the only route back from
        // a bad regeneration and a library regenerated once a year must still have it; the older
        // ones exist in case the newest is itself a bad card. Before this they were invisible to
        // retention -- the glob above is *.db at the root -- and accumulated forever.
        var cardsDir = Path.Combine(backupDir, IJellyfinDirectoryResolver.MediaCardsFolder);
        if (!Directory.Exists(cardsDir)) return;

        var removedCards = 0;
        var byLibrary = new DirectoryInfo(cardsDir).GetFiles()
            .GroupBy(f => CardBackupLibraryKey(f.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var library in byLibrary)
        {
            foreach (var stale in library.OrderByDescending(f => f.LastWriteTimeUtc).Skip(CardBackupsKeptPerLibrary))
            {
                stale.Delete();
                removedCards++;
            }
        }

        logger?.Ok($"Removed {removedCards} card backup(s) beyond the newest {CardBackupsKeptPerLibrary} per library");
    }

    /// <summary>How many card backups retention keeps for each library.</summary>
    public const int CardBackupsKeptPerLibrary = 3;

    /// <summary>Card backups are named <c>&lt;sanitised library&gt;-yyyyMMdd-HHmmss.&lt;ext&gt;</c>.</summary>
    private static readonly Regex CardBackupStamp = new(@"-\d{8}-\d{6}$", RegexOptions.Compiled);

    /// <summary>
    /// The library a card backup belongs to, from its file name. A file without the timestamp
    /// suffix forms a group of its own, so retention never deletes something it did not write.
    /// </summary>
    private static string CardBackupLibraryKey(string fileName) =>
        CardBackupStamp.Replace(Path.GetFileNameWithoutExtension(fileName), "");

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

    // ---- My Media card regeneration -------------------------------------------------------
    //
    // The card is a collage Jellyfin builds itself (StripCollageBuilder), with the library name
    // baked into the pixels. Its provider only runs when the library has no primary image, which
    // is why regenerating one means delete-then-refresh rather than refresh alone.

    private HttpClient CreateApiClient(JellyfinApiConfig apiConfig)
    {
        var client = _httpFactory.CreateClient();
        // Header, never the query string -- query strings land in proxy logs and request traces.
        client.DefaultRequestHeaders.Add("X-Emby-Token", apiConfig.ApiKey);
        return client;
    }

    private static string CardUrl(JellyfinApiConfig apiConfig, string libraryId) =>
        $"{apiConfig.BaseUrl}/Items/{Uri.EscapeDataString(libraryId)}/Images/Primary";

    public async Task<IReadOnlyList<MediaCardTarget>> GetMediaCardTargetsAsync(JellyfinApiConfig apiConfig, CancellationToken ct = default)
    {
        // /UserViews is the My Media row itself. /Library/VirtualFolders reports only
        // CollectionFolders, so it silently omits generated views -- Playlists among them.
        var userId = await ResolveUserIdAsync(apiConfig, ct)
            ?? throw new InvalidOperationException("Jellyfin reported no users, so there is no My Media row to read");

        var client = CreateApiClient(apiConfig);
        var json = await client.GetStringAsync(
            $"{apiConfig.BaseUrl}/UserViews?userId={Uri.EscapeDataString(userId)}", ct);

        var targets = new List<MediaCardTarget>();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Items", out var items)) return targets;

        foreach (var item in items.EnumerateArray())
        {
            var id = StringProperty(item, "Id");
            var name = StringProperty(item, "Name");
            if (id is null || name is null) continue;

            var itemType = StringProperty(item, "Type");
            var collectionType = StringProperty(item, "CollectionType");

            var hasCard = item.TryGetProperty("ImageTags", out var tags)
                && tags.ValueKind == System.Text.Json.JsonValueKind.Object
                && tags.TryGetProperty("Primary", out _);

            var (canRegenerate, reason) = MediaCardSupport.Evaluate(itemType, collectionType);
            targets.Add(new MediaCardTarget(id, name, collectionType, itemType, hasCard, canRegenerate, reason));
        }

        return targets;
    }

    /// <summary>
    /// /UserViews needs a user, and an API key carries none. Prefer the configured user, then the
    /// first administrator -- <c>jellyfin-user-id</c> is often unset, and the cast &amp; crew job
    /// was silently a no-op for exactly that reason.
    /// </summary>
    private async Task<string?> ResolveUserIdAsync(JellyfinApiConfig apiConfig, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(apiConfig.UserId)) return apiConfig.UserId;

        var client = CreateApiClient(apiConfig);
        var json = await client.GetStringAsync($"{apiConfig.BaseUrl}/Users", ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        string? firstUser = null;
        foreach (var user in doc.RootElement.EnumerateArray())
        {
            var id = StringProperty(user, "Id");
            if (id is null) continue;
            firstUser ??= id;

            if (user.TryGetProperty("Policy", out var policy)
                && policy.TryGetProperty("IsAdministrator", out var isAdmin)
                && isAdmin.ValueKind == System.Text.Json.JsonValueKind.True)
            {
                return id;
            }
        }

        return firstUser;
    }

    private static string? StringProperty(System.Text.Json.JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;

    public async Task<string?> BackupLibraryCardAsync(string libraryId, string libraryName,
        JellyfinApiConfig apiConfig, CancellationToken ct = default)
    {
        var client = CreateApiClient(apiConfig);
        using var response = await client.GetAsync(CardUrl(apiConfig, libraryId), ct);
        if (!response.IsSuccessStatusCode) return null;

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length == 0) return null;

        var backupDir = Path.Combine(await _directoryResolver.GetBackupDirectoryAsync(), IJellyfinDirectoryResolver.MediaCardsFolder);
        Directory.CreateDirectory(backupDir);

        var extension = response.Content.Headers.ContentType?.MediaType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => ".img"
        };

        var fileName = $"{SanitiseForFileName(libraryName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}{extension}";
        var path = Path.Combine(backupDir, fileName);
        await File.WriteAllBytesAsync(path, bytes, ct);
        return path;
    }

    /// <summary>A library name is server-supplied text, so it never becomes a path component.</summary>
    private static string SanitiseForFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        return string.IsNullOrEmpty(cleaned.Trim('.', '_')) ? "library" : cleaned;
    }

    public async Task DeleteLibraryCardAsync(string libraryId, JellyfinApiConfig apiConfig, CancellationToken ct = default)
    {
        var client = CreateApiClient(apiConfig);
        using var response = await client.DeleteAsync(CardUrl(apiConfig, libraryId), ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RefreshLibraryCardAsync(string libraryId, JellyfinApiConfig apiConfig, CancellationToken ct = default)
    {
        // Both parameters are the way they are because of a real incident on 2026-08-30. Do not
        // "optimise" either one back.
        //
        // replaceAllImages=false: a refresh on a LIBRARY recurses into its children, and with
        // replaceAllImages=true it re-fetches every one of their images. Four ticked libraries
        // rewrote ~2,200 files across D:\Movies, D:\TV_Shows and the boxset folders. We delete the
        // card first, so the provider is filling an empty slot -- `true` buys nothing and costs the
        // whole library. With `false`, children that already have images are left alone.
        //
        // metadataRefreshMode=ValidationOnly, not None: in MetadataService.RefreshMetadata the
        // ImageProvider.RefreshImages call is nested inside `if (MetadataRefreshMode != None)`, so
        // None skips the image refresh entirely and the card can never come back. ValidationOnly
        // clears that gate while still not running metadata providers, which need >= Default.
        var url = $"{apiConfig.BaseUrl}/Items/{Uri.EscapeDataString(libraryId)}/Refresh"
                  + "?metadataRefreshMode=ValidationOnly"
                  + "&imageRefreshMode=FullRefresh"
                  + "&replaceAllImages=false";

        var client = CreateApiClient(apiConfig);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await client.PostAsync(url, content: null, timeoutCts.Token);
        response.EnsureSuccessStatusCode();
    }

    public async Task RestoreLibraryCardAsync(string libraryId, string backupPath, JellyfinApiConfig apiConfig, CancellationToken ct = default)
    {
        // Deleting the image also removes its BaseItemImageInfos row, so copying the file back into
        // the metadata folder would leave Jellyfin unaware of it. Uploading re-creates the row.
        var bytes = await File.ReadAllBytesAsync(backupPath, ct);
        var mimeType = Path.GetExtension(backupPath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };

        // The body must be BASE64 TEXT, not raw bytes. The OpenAPI document declares this endpoint
        // as `image/*` with `format: binary`, but ImageController.SetItemImage pipes the body
        // through a base64 decoder -- posting raw bytes returns 500 with
        // "System.FormatException ... ThrowBase64FormatException at ImageSaver.SaveImageToLocation".
        // Verified against the running server, 10.11.11.
        var client = CreateApiClient(apiConfig);
        using var content = new StringContent(Convert.ToBase64String(bytes));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
        using var response = await client.PostAsync(CardUrl(apiConfig, libraryId), content, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string?> FindLatestCardBackupAsync(string libraryName)
    {
        var directory = Path.Combine(await _directoryResolver.GetBackupDirectoryAsync(), IJellyfinDirectoryResolver.MediaCardsFolder);
        if (!Directory.Exists(directory)) return null;

        // Backups are named "<sanitised library>-yyyyMMdd-HHmmss.<ext>".
        var prefix = SanitiseForFileName(libraryName) + "-";
        return new DirectoryInfo(directory).GetFiles()
            .Where(f => f.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => f.FullName)
            .FirstOrDefault();
    }

    public async Task<bool> HasLibraryCardAsync(string libraryId, JellyfinApiConfig apiConfig, CancellationToken ct = default)
    {
        var client = CreateApiClient(apiConfig);
        // HEAD: we only care that the image exists, not what it contains.
        using var request = new HttpRequestMessage(HttpMethod.Head, CardUrl(apiConfig, libraryId));
        using var response = await client.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }
}
