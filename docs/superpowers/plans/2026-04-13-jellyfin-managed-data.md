# Jellyfin Managed Data Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Auto-configure Jellyfin settings from docker-compose.yml, manage backups and operation logs in app-local directories, make backup retention configurable, add sqlite3 as a managed dependency.

**Architecture:** A compose parser extracts container name and DB path from the user's docker-compose.yml. Backups and logs live under `{app-root}/jellyfin-data/`. Operation logging writes timestamped files during DB update and cast/crew runs. Settings UI shows compose-derived values and configurable retention.

**Tech Stack:** C# (.NET 9), Blazor Server, SQLite (EF Core), line-based YAML parsing

---

### Task 1: Managed Directories and Operation Logger

**Files:**
- Create: `src/ControlMenu/Modules/Jellyfin/Services/OperationLogger.cs`

This creates the `jellyfin-data/backups/` and `jellyfin-data/logging/` directories and provides a simple logger for operation runs.

- [ ] **Step 1: Create OperationLogger**

Create `src/ControlMenu/Modules/Jellyfin/Services/OperationLogger.cs`:

```csharp
namespace ControlMenu.Modules.Jellyfin.Services;

public class OperationLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly string _filePath;

    public string FilePath => _filePath;

    private OperationLogger(StreamWriter writer, string filePath)
    {
        _writer = writer;
        _filePath = filePath;
    }

    public static OperationLogger Create(string operation)
    {
        var logDir = Path.Combine(AppContext.BaseDirectory, "jellyfin-data", "logging");
        Directory.CreateDirectory(logDir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var filePath = Path.Combine(logDir, $"{operation}_{timestamp}.log");
        var writer = new StreamWriter(filePath, append: false) { AutoFlush = true };

        var logger = new OperationLogger(writer, filePath);
        logger.Log("START", operation);
        return logger;
    }

    public void Log(string level, string message)
    {
        var ts = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        _writer.WriteLine($"{ts} {level,-5} {message}");
    }

    public void Step(string message) => Log("STEP", message);
    public void Ok(string message) => Log("OK", message);
    public void Fail(string message) => Log("FAIL", message);
    public void Done(string message) => Log("DONE", message);

    public static string GetLogDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "jellyfin-data", "logging");

    public static string GetBackupDirectory()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "jellyfin-data", "backups");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static IReadOnlyList<OperationLogEntry> GetRecentLogs(int count = 10)
    {
        var logDir = GetLogDirectory();
        if (!Directory.Exists(logDir)) return [];

        return Directory.GetFiles(logDir, "*.log")
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .Take(count)
            .Select(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f);
                var parts = name.Split('_', 2);
                var lines = File.ReadAllLines(f);
                var lastLine = lines.LastOrDefault() ?? "";
                var success = lastLine.Contains("DONE") && !lastLine.Contains("FAIL");
                return new OperationLogEntry(
                    Operation: parts[0],
                    Timestamp: File.GetLastWriteTimeUtc(f),
                    Success: success,
                    FilePath: f,
                    Summary: lastLine
                );
            })
            .ToList();
    }

    public void Dispose() => _writer.Dispose();
}

public record OperationLogEntry(
    string Operation,
    DateTime Timestamp,
    bool Success,
    string FilePath,
    string Summary);
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/ControlMenu -o /tmp/cm-build
```

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/Modules/Jellyfin/Services/OperationLogger.cs
git commit -m "feat: add OperationLogger for Jellyfin managed logging"
```

---

### Task 2: Docker Compose Parser

**Files:**
- Create: `src/ControlMenu/Modules/Jellyfin/Services/ComposeParser.cs`

Simple line-based parser that extracts container name and config volume mount from a docker-compose.yml.

- [ ] **Step 1: Create ComposeParser**

Create `src/ControlMenu/Modules/Jellyfin/Services/ComposeParser.cs`:

```csharp
namespace ControlMenu.Modules.Jellyfin.Services;

public record ComposeParseResult(
    string? ContainerName,
    string? ConfigHostPath,
    string? DbPath,
    string? ErrorMessage);

public static class ComposeParser
{
    public static ComposeParseResult Parse(string composePath)
    {
        if (!File.Exists(composePath))
            return new(null, null, null, $"File not found: {composePath}");

        string[] lines;
        try
        {
            lines = File.ReadAllLines(composePath);
        }
        catch (Exception ex)
        {
            return new(null, null, null, $"Cannot read file: {ex.Message}");
        }

        string? containerName = null;
        string? configHostPath = null;
        bool inVolumes = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // container_name: jellyfin
            if (line.StartsWith("container_name:"))
            {
                containerName = line["container_name:".Length..].Trim().Trim('"', '\'');
            }

            // Detect volumes: section
            if (line == "volumes:")
            {
                inVolumes = true;
                continue;
            }

            // Exit volumes section on next non-indented or non-list line
            if (inVolumes && !rawLine.StartsWith(" ") && !rawLine.StartsWith("\t") && line.Length > 0 && !line.StartsWith("-"))
            {
                inVolumes = false;
            }

            // Parse volume mount: - /host/path:/config or - D:\path:/config
            if (inVolumes && line.StartsWith("-"))
            {
                var mount = line[1..].Trim().Trim('"', '\'');
                // Find the last :/container_path pattern
                // Handle Windows paths like D:\foo:/config by finding :/ after drive letter
                var colonIdx = FindMountSeparator(mount);
                if (colonIdx > 0)
                {
                    var hostSide = mount[..colonIdx];
                    var containerSide = mount[(colonIdx + 1)..].Split(':')[0]; // strip :rw/:ro
                    if (containerSide == "/config")
                    {
                        configHostPath = hostSide;
                    }
                }
            }
        }

        if (configHostPath is null)
            return new(containerName, null, null, "No volume mount to /config found in compose file");

        var dbPath = Path.Combine(configHostPath, "data", "jellyfin.db");
        return new(containerName, configHostPath, dbPath, null);
    }

    private static int FindMountSeparator(string mount)
    {
        // For Windows paths like D:\DockerData\jellyfin\config:/config
        // The separator is the colon that's followed by / and NOT preceded by \
        // For Linux paths like /data/jellyfin:/config, it's the first colon
        for (int i = 1; i < mount.Length - 1; i++)
        {
            if (mount[i] == ':' && mount[i + 1] == '/')
            {
                // Skip drive letter colons (e.g., D:\ — single char before colon)
                if (i == 1 && char.IsLetter(mount[0]))
                    continue;
                return i;
            }
        }
        // Fallback: last colon
        return mount.LastIndexOf(':');
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/ControlMenu -o /tmp/cm-build
```

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/Modules/Jellyfin/Services/ComposeParser.cs
git commit -m "feat: add docker-compose parser for Jellyfin config auto-detection"
```

---

### Task 3: Update JellyfinModule ConfigRequirements and Dependencies

**Files:**
- Modify: `src/ControlMenu/Modules/Jellyfin/JellyfinModule.cs`

Replace hardcoded config paths with compose-path-based approach. Add sqlite3 InstallPath. Remove settings that are now derived.

- [ ] **Step 1: Update ConfigRequirements**

Replace the `ConfigRequirements` property (lines 34-47) with:

```csharp
public IEnumerable<ConfigRequirement> ConfigRequirements =>
[
    new ConfigRequirement("jellyfin-compose-path", "Docker Compose Path", "Path to Jellyfin docker-compose.yml"),
    new ConfigRequirement("jellyfin-api-key", "Jellyfin API Key", "API key for Jellyfin REST API", IsSecret: true),
    new ConfigRequirement("jellyfin-base-url", "Jellyfin URL", "Base URL for Jellyfin API", DefaultValue: "http://127.0.0.1:8096"),
    new ConfigRequirement("jellyfin-user-id", "User ID", "Jellyfin user ID for API calls"),
    new ConfigRequirement("jellyfin-backup-retention-days", "Backup Retention (days)", "Days to keep database backups", DefaultValue: "5"),
    new ConfigRequirement("smtp-server", "SMTP Server", "SMTP server for notifications", DefaultValue: "mail.smtp2go.com"),
    new ConfigRequirement("smtp-port", "SMTP Port", "SMTP server port", DefaultValue: "587"),
    new ConfigRequirement("smtp-username", "SMTP Username", "SMTP login username"),
    new ConfigRequirement("smtp-password", "SMTP Password", "SMTP login password", IsSecret: true),
    new ConfigRequirement("notification-email", "Notification Email", "Email for completion alerts")
];
```

- [ ] **Step 2: Add InstallPath to sqlite3 dependency**

Replace the `Dependencies` property (lines 12-32) with:

```csharp
private static string DepsRoot => Path.Combine(AppContext.BaseDirectory, "dependencies");

public IEnumerable<ModuleDependency> Dependencies =>
[
    new ModuleDependency
    {
        Name = "docker",
        ExecutableName = "docker",
        VersionCommand = "docker --version",
        VersionPattern = @"Docker version ([\d.]+)",
        SourceType = UpdateSourceType.Manual,
        ProjectHomeUrl = "https://docs.docker.com/get-docker/"
    },
    new ModuleDependency
    {
        Name = "sqlite3",
        ExecutableName = "sqlite3",
        VersionCommand = "sqlite3 --version",
        VersionPattern = @"([\d.]+)",
        SourceType = UpdateSourceType.Manual,
        ProjectHomeUrl = "https://www.sqlite.org/download.html",
        InstallPath = Path.Combine(DepsRoot, "sqlite3")
    }
];
```

- [ ] **Step 3: Create sqlite3 dependencies folder**

```bash
mkdir -p src/ControlMenu/dependencies/sqlite3
touch src/ControlMenu/dependencies/sqlite3/.gitkeep
```

- [ ] **Step 4: Verify build**

```bash
dotnet build src/ControlMenu -o /tmp/cm-build
```

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Modules/Jellyfin/JellyfinModule.cs src/ControlMenu/dependencies/sqlite3/
git commit -m "feat: update Jellyfin config to compose-based, add sqlite3 InstallPath"
```

---

### Task 4: Update JellyfinService to Use Compose Parser and Logger

**Files:**
- Modify: `src/ControlMenu/Modules/Jellyfin/Services/JellyfinService.cs`
- Modify: `src/ControlMenu/Modules/Jellyfin/Services/IJellyfinService.cs`

Update the service to derive DB path and container name from the compose file, use managed backup directory, log operations, and use configurable retention.

- [ ] **Step 1: Add new methods to IJellyfinService**

Replace the contents of `IJellyfinService.cs`:

```csharp
using ControlMenu.Modules.Jellyfin.Services;

namespace ControlMenu.Modules.Jellyfin.Services;

public interface IJellyfinService
{
    Task<string?> GetContainerIdAsync(CancellationToken ct = default);
    Task<bool> StopContainerAsync(string containerId, CancellationToken ct = default);
    Task<bool> StartContainerAsync(string containerId, CancellationToken ct = default);
    Task<string?> BackupDatabaseAsync(OperationLogger? logger = null, CancellationToken ct = default);
    Task<bool> UpdateDateCreatedAsync(OperationLogger? logger = null, CancellationToken ct = default);
    Task CleanupOldBackupsAsync(OperationLogger? logger = null, CancellationToken ct = default);
    Task<ComposeParseResult> ParseComposeFileAsync(CancellationToken ct = default);
    Task<IReadOnlyList<JellyfinPerson>> GetPersonsMissingImagesAsync(CancellationToken ct = default);
    Task TriggerPersonImageDownloadAsync(string personId, CancellationToken ct = default);
}
```

- [ ] **Step 2: Update JellyfinService implementation**

Replace the contents of `JellyfinService.cs`:

```csharp
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

        // Store derived values in DB
        if (result.ContainerName is not null)
            await _config.SetSettingAsync("jellyfin-container-name", result.ContainerName);
        if (result.DbPath is not null)
            await _config.SetSettingAsync("jellyfin-db-path", result.DbPath);

        // Backup dir is always app-local
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
```

Key changes:
- `BackupDatabaseAsync` uses `File.Copy` instead of shelling out to `cmd/cp`
- `CleanupOldBackupsAsync` uses native C# file operations instead of PowerShell/find
- `UpdateDateCreatedAsync` adds `WHERE PremiereDate IS NOT NULL` to avoid nulling valid dates
- All methods accept optional `OperationLogger` for logging
- New `ParseComposeFileAsync` reads compose and stores derived settings

- [ ] **Step 3: Verify build**

```bash
dotnet build src/ControlMenu -o /tmp/cm-build
```

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Modules/Jellyfin/Services/JellyfinService.cs src/ControlMenu/Modules/Jellyfin/Services/IJellyfinService.cs
git commit -m "feat: update JellyfinService with compose parser, native backups, operation logging"
```

---

### Task 5: Update DatabaseUpdate Page with Logging

**Files:**
- Modify: `src/ControlMenu/Modules/Jellyfin/Pages/DatabaseUpdate.razor`

Wire up the OperationLogger to each step and add a "Recent Operations" section.

- [ ] **Step 1: Update the RunUpdate method and add recent operations**

Replace the entire contents of `DatabaseUpdate.razor`:

```razor
@page "/jellyfin/db-update"
@using ControlMenu.Modules.Jellyfin.Services

<PageTitle>Jellyfin — DB Date Update</PageTitle>

<h1><i class="bi bi-calendar-date"></i> Database Date Update</h1>
<p class="page-subtitle">Updates the DateCreated field in the Jellyfin database to match the premiere date of each show or movie.</p>

@if (!_running && !_completed)
{
    <div class="start-panel">
        <p>This will:</p>
        <ol>
            <li>Stop the Jellyfin Docker container</li>
            <li>Create a database backup</li>
            <li>Run the SQL update</li>
            <li>Restart the container</li>
            <li>Clean up old backups</li>
        </ol>
        <button class="btn btn-primary btn-lg" @onclick="RunUpdate">
            <i class="bi bi-play-circle"></i> Start Update
        </button>
    </div>
}
else
{
    <div class="progress-panel">
        @foreach (var step in _steps)
        {
            <div class="progress-step @step.Status.ToString().ToLower()">
                <div class="step-icon">
                    @if (step.Status == StepStatus.Running)
                    {
                        <i class="bi bi-arrow-repeat spin"></i>
                    }
                    else if (step.Status == StepStatus.Completed)
                    {
                        <i class="bi bi-check-circle-fill"></i>
                    }
                    else if (step.Status == StepStatus.Failed)
                    {
                        <i class="bi bi-x-circle-fill"></i>
                    }
                    else
                    {
                        <i class="bi bi-circle"></i>
                    }
                </div>
                <div class="step-content">
                    <span class="step-label">@step.Label</span>
                    @if (!string.IsNullOrEmpty(step.Detail))
                    {
                        <span class="step-detail">@step.Detail</span>
                    }
                </div>
            </div>
        }
    </div>
}

@if (!string.IsNullOrEmpty(_error))
{
    <div class="error-panel">
        <i class="bi bi-exclamation-triangle"></i> @_error
    </div>
}

@if (_recentLogs.Count > 0)
{
    <h2 style="margin-top:2rem;">Recent Operations</h2>
    <table class="data-table">
        <thead>
            <tr>
                <th>Operation</th>
                <th>Date</th>
                <th>Status</th>
                <th>Summary</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var log in _recentLogs)
            {
                <tr>
                    <td>@log.Operation</td>
                    <td>@log.Timestamp.ToLocalTime().ToString("g")</td>
                    <td>
                        <span class="status-badge @(log.Success ? "status-ok" : "status-error")">
                            @(log.Success ? "Success" : "Failed")
                        </span>
                    </td>
                    <td style="font-size:0.85rem;color:var(--text-secondary);">@log.Summary</td>
                </tr>
            }
        </tbody>
    </table>
}

@code {
    [Inject] private IJellyfinService JellyfinService { get; set; } = default!;

    private List<ProgressStep> _steps = [];
    private bool _running;
    private bool _completed;
    private string? _error;
    private List<OperationLogEntry> _recentLogs = [];

    protected override void OnInitialized()
    {
        _recentLogs = OperationLogger.GetRecentLogs(10)
            .Where(l => l.Operation == "db-date-update")
            .ToList();
    }

    private async Task RunUpdate()
    {
        _running = true;
        _error = null;
        _steps =
        [
            new("Stopping Docker container..."),
            new("Creating database backup..."),
            new("Running SQL update..."),
            new("Starting Docker container..."),
            new("Cleaning up old backups..."),
            new("Complete")
        ];

        using var logger = OperationLogger.Create("db-date-update");

        try
        {
            // Step 1: Get container ID and stop
            _steps[0].Status = StepStatus.Running;
            StateHasChanged();
            logger.Step("Finding Jellyfin container");
            var containerId = await JellyfinService.GetContainerIdAsync();
            if (containerId is null)
            {
                _steps[0].Status = StepStatus.Failed;
                _steps[0].Detail = "Jellyfin container not found. Is it running?";
                _error = "Could not find Jellyfin Docker container.";
                logger.Fail("Jellyfin container not found");
                logger.Done("Failed");
                _running = false;
                return;
            }

            logger.Step($"Stopping container {containerId[..12]}");
            var stopped = await JellyfinService.StopContainerAsync(containerId);
            if (!stopped)
            {
                _steps[0].Status = StepStatus.Failed;
                _error = "Failed to stop container.";
                logger.Fail("Failed to stop container");
                logger.Done("Failed");
                _running = false;
                return;
            }
            _steps[0].Status = StepStatus.Completed;
            _steps[0].Detail = $"Container {containerId[..12]} stopped.";
            logger.Ok($"Container {containerId[..12]} stopped");
            StateHasChanged();

            // Step 2: Backup
            _steps[1].Status = StepStatus.Running;
            StateHasChanged();
            logger.Step("Creating backup");
            var backupPath = await JellyfinService.BackupDatabaseAsync(logger);
            _steps[1].Status = backupPath is not null ? StepStatus.Completed : StepStatus.Failed;
            _steps[1].Detail = backupPath is not null ? $"Saved to {Path.GetFileName(backupPath)}" : "Backup failed";
            StateHasChanged();

            // Step 3: SQL Update
            _steps[2].Status = StepStatus.Running;
            StateHasChanged();
            logger.Step("Running SQL update");
            var updated = await JellyfinService.UpdateDateCreatedAsync(logger);
            _steps[2].Status = updated ? StepStatus.Completed : StepStatus.Failed;
            _steps[2].Detail = updated ? "DateCreated = PremiereDate applied." : "SQL update failed.";
            StateHasChanged();

            // Step 4: Start container
            _steps[3].Status = StepStatus.Running;
            StateHasChanged();
            logger.Step("Starting container");
            var started = await JellyfinService.StartContainerAsync(containerId);
            _steps[3].Status = started ? StepStatus.Completed : StepStatus.Failed;
            _steps[3].Detail = started ? "Container restarted." : "Failed to start container.";
            logger.Log(started ? "OK" : "FAIL", started ? "Container restarted" : "Failed to start container");
            StateHasChanged();

            // Step 5: Cleanup
            _steps[4].Status = StepStatus.Running;
            StateHasChanged();
            logger.Step("Cleaning up old backups");
            await JellyfinService.CleanupOldBackupsAsync(logger);
            _steps[4].Status = StepStatus.Completed;
            StateHasChanged();

            // Done
            _steps[5].Status = StepStatus.Completed;
            _completed = true;
            logger.Done("Completed successfully");
        }
        catch (Exception ex)
        {
            _error = $"Unexpected error: {ex.Message}";
            logger.Fail($"Unexpected error: {ex.Message}");
            logger.Done("Failed with exception");
            var current = _steps.FirstOrDefault(s => s.Status == StepStatus.Running);
            if (current is not null) current.Status = StepStatus.Failed;
        }
        finally
        {
            _running = false;
            _recentLogs = OperationLogger.GetRecentLogs(10)
                .Where(l => l.Operation == "db-date-update")
                .ToList();
        }
    }

    private enum StepStatus { Pending, Running, Completed, Failed }

    private class ProgressStep(string label)
    {
        public string Label { get; set; } = label;
        public StepStatus Status { get; set; } = StepStatus.Pending;
        public string? Detail { get; set; }
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/ControlMenu -o /tmp/cm-build
```

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/Modules/Jellyfin/Pages/DatabaseUpdate.razor
git commit -m "feat: wire operation logging into DB date update page"
```

---

### Task 6: Jellyfin Settings UI with Compose Parser

**Files:**
- Create: `src/ControlMenu/Modules/Jellyfin/Pages/JellyfinSettings.razor`
- Modify: `src/ControlMenu/Modules/Jellyfin/JellyfinModule.cs` (add nav entry)

A settings panel for Jellyfin config that shows compose-derived values and lets users configure the compose path and retention.

- [ ] **Step 1: Create JellyfinSettings page**

Create `src/ControlMenu/Modules/Jellyfin/Pages/JellyfinSettings.razor`:

```razor
@page "/jellyfin/settings"
@using ControlMenu.Services
@using ControlMenu.Modules.Jellyfin.Services

<PageTitle>Jellyfin — Settings</PageTitle>

<h1><i class="bi bi-gear"></i> Jellyfin Settings</h1>

<div class="settings-section">
    <h2>Docker Compose</h2>
    <p>Point Control Menu to your Jellyfin docker-compose.yml to auto-detect container name and database path.</p>

    <div class="form-row">
        <label>Compose File Path</label>
        <div class="input-group">
            <input type="text" class="form-control" @bind="_composePath" placeholder="e.g., D:\DockerData\jellyfin\docker-compose.yml" />
            <button class="btn btn-primary" @onclick="SaveAndParse" disabled="@_parsing">
                <i class="bi bi-arrow-repeat"></i> @(_parsing ? "Parsing..." : "Save & Parse")
            </button>
        </div>
    </div>

    @if (_parseResult is not null)
    {
        <div class="derived-values" style="margin-top:1rem;">
            @if (_parseResult.ErrorMessage is not null)
            {
                <div class="alert alert-danger">
                    <i class="bi bi-exclamation-triangle"></i> @_parseResult.ErrorMessage
                </div>
            }
            else
            {
                <table class="data-table" style="max-width:600px;">
                    <tr>
                        <td><strong>Container Name</strong></td>
                        <td><code>@(_parseResult.ContainerName ?? "—")</code></td>
                        <td>@StatusIcon(_parseResult.ContainerName is not null)</td>
                    </tr>
                    <tr>
                        <td><strong>Database Path</strong></td>
                        <td><code>@(_parseResult.DbPath ?? "—")</code></td>
                        <td>@StatusIcon(_parseResult.DbPath is not null && File.Exists(_parseResult.DbPath))</td>
                    </tr>
                    <tr>
                        <td><strong>Backup Directory</strong></td>
                        <td><code>@OperationLogger.GetBackupDirectory()</code></td>
                        <td>@StatusIcon(true)</td>
                    </tr>
                </table>
            }
        </div>
    }
</div>

<div class="settings-section" style="margin-top:2rem;">
    <h2>Backup Retention</h2>
    <div class="form-row">
        <label>Keep backups for</label>
        <div class="input-group" style="max-width:200px;">
            <input type="number" class="form-control" @bind="_retentionDays" min="1" max="365" />
            <span class="input-group-text">days</span>
        </div>
        <button class="btn btn-secondary" @onclick="SaveRetention" style="margin-left:8px;">Save</button>
    </div>
</div>

<div class="settings-section" style="margin-top:2rem;">
    <h2>Managed Directories</h2>
    <table class="data-table" style="max-width:600px;">
        <tr>
            <td><strong>Backups</strong></td>
            <td><code>@OperationLogger.GetBackupDirectory()</code></td>
            <td>@_backupCount files, @FormatSize(_backupSize)</td>
        </tr>
        <tr>
            <td><strong>Logs</strong></td>
            <td><code>@OperationLogger.GetLogDirectory()</code></td>
            <td>@_logCount files</td>
        </tr>
    </table>
</div>

@if (!string.IsNullOrEmpty(_message))
{
    <div class="alert @(_messageIsError ? "alert-danger" : "alert-success")" style="margin-top:1rem;">
        @_message
    </div>
}

@code {
    [Inject] private IConfigurationService Config { get; set; } = default!;
    [Inject] private IJellyfinService JellyfinService { get; set; } = default!;

    private string _composePath = "";
    private int _retentionDays = 5;
    private bool _parsing;
    private ComposeParseResult? _parseResult;
    private string? _message;
    private bool _messageIsError;
    private int _backupCount;
    private long _backupSize;
    private int _logCount;

    protected override async Task OnInitializedAsync()
    {
        _composePath = await Config.GetSettingAsync("jellyfin-compose-path") ?? "";
        var retStr = await Config.GetSettingAsync("jellyfin-backup-retention-days");
        _retentionDays = int.TryParse(retStr, out var d) ? d : 5;

        RefreshDirectoryStats();

        // Auto-parse if compose path is set
        if (!string.IsNullOrEmpty(_composePath))
        {
            _parseResult = await JellyfinService.ParseComposeFileAsync();
        }
    }

    private async Task SaveAndParse()
    {
        _parsing = true;
        _message = null;
        StateHasChanged();

        await Config.SetSettingAsync("jellyfin-compose-path", _composePath);
        _parseResult = await JellyfinService.ParseComposeFileAsync();

        if (_parseResult.ErrorMessage is null)
        {
            _message = "Compose file parsed. Container name and database path configured.";
            _messageIsError = false;
        }
        else
        {
            _message = _parseResult.ErrorMessage;
            _messageIsError = true;
        }

        _parsing = false;
    }

    private async Task SaveRetention()
    {
        await Config.SetSettingAsync("jellyfin-backup-retention-days", _retentionDays.ToString());
        _message = $"Backup retention set to {_retentionDays} days.";
        _messageIsError = false;
    }

    private void RefreshDirectoryStats()
    {
        var backupDir = OperationLogger.GetBackupDirectory();
        if (Directory.Exists(backupDir))
        {
            var files = Directory.GetFiles(backupDir, "*.db");
            _backupCount = files.Length;
            _backupSize = files.Sum(f => new FileInfo(f).Length);
        }

        var logDir = OperationLogger.GetLogDirectory();
        if (Directory.Exists(logDir))
        {
            _logCount = Directory.GetFiles(logDir, "*.log").Length;
        }
    }

    private static RenderFragment StatusIcon(bool ok) => builder =>
    {
        builder.OpenElement(0, "i");
        builder.AddAttribute(1, "class", ok ? "bi bi-check-circle text-success" : "bi bi-x-circle text-danger");
        builder.CloseElement();
    };

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
```

- [ ] **Step 2: Add nav entry to JellyfinModule**

In `JellyfinModule.cs`, update the `GetNavEntries` method to add the settings page:

```csharp
public IEnumerable<NavEntry> GetNavEntries() =>
[
    new NavEntry("DB Date Update", "/jellyfin/db-update", "bi-calendar-date", 0),
    new NavEntry("Cast & Crew", "/jellyfin/cast-crew", "bi-people", 1),
    new NavEntry("Settings", "/jellyfin/settings", "bi-gear", 2)
];
```

- [ ] **Step 3: Verify build**

```bash
dotnet build src/ControlMenu -o /tmp/cm-build
```

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Modules/Jellyfin/Pages/JellyfinSettings.razor src/ControlMenu/Modules/Jellyfin/JellyfinModule.cs
git commit -m "feat: add Jellyfin settings page with compose parser and directory stats"
```
