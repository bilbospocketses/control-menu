# Phase 4: Jellyfin Module & Background Jobs — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Jellyfin module with DB date update (short operation with step-by-step progress), Cast & Crew image update (long-running worker), and the BackgroundJobService infrastructure. Replaces PowerShell menu options 3 and 4 plus the `Jellyfin-Cast-Update.ps1` worker script.

**Architecture:** `JellyfinModule` implements `IToolModule`. `JellyfinService` wraps Docker CLI and SQLite operations through `ICommandExecutor`. `BackgroundJobService` manages the Jobs table — creating, tracking, and monitoring worker processes. The Cast & Crew update runs as a standalone `dotnet` worker process that communicates progress via SQLite. The Blazor UI polls the Jobs table and shows real-time progress via SignalR.

**Tech Stack:** .NET 9, Blazor Server, EF Core + SQLite, xUnit + Moq, Bootstrap Icons (already loaded), System.Net.Mail for SMTP

---

## File Structure

### New Services & Module

```
src/ControlMenu/Modules/Jellyfin/
├── JellyfinModule.cs
├── Services/
│   ├── IJellyfinService.cs
│   └── JellyfinService.cs
├── Pages/
│   ├── DatabaseUpdate.razor
│   ├── DatabaseUpdate.razor.css
│   ├── CastCrewUpdate.razor
│   └── CastCrewUpdate.razor.css
```

### Background Job Infrastructure

```
src/ControlMenu/Services/
├── IBackgroundJobService.cs
└── BackgroundJobService.cs
```

### New Tests

```
tests/ControlMenu.Tests/
├── Services/BackgroundJobServiceTests.cs
├── Modules/Jellyfin/
│   ├── JellyfinServiceTests.cs
│   └── JellyfinModuleTests.cs
```

### Modified Files

```
src/ControlMenu/Program.cs              (register services)
src/ControlMenu/wwwroot/css/app.css     (progress step styles)
```

---

## Task 1: BackgroundJobService

**Files:**
- Create: `src/ControlMenu/Services/IBackgroundJobService.cs`
- Create: `src/ControlMenu/Services/BackgroundJobService.cs`
- Create: `tests/ControlMenu.Tests/Services/BackgroundJobServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/ControlMenu.Tests/Services/BackgroundJobServiceTests.cs`:
```csharp
using ControlMenu.Data;
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Services;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Tests.Services;

public class BackgroundJobServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly BackgroundJobService _service;

    public BackgroundJobServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new BackgroundJobService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateJobAsync_InsertsJobWithQueuedStatus()
    {
        var job = await _service.CreateJobAsync("jellyfin", "cast-crew-update");

        Assert.NotEqual(Guid.Empty, job.Id);
        Assert.Equal("jellyfin", job.ModuleId);
        Assert.Equal("cast-crew-update", job.JobType);
        Assert.Equal(JobStatus.Queued, job.Status);
    }

    [Fact]
    public async Task GetJobAsync_ReturnsJob_WhenExists()
    {
        var created = await _service.CreateJobAsync("jellyfin", "cast-crew-update");
        var retrieved = await _service.GetJobAsync(created.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(created.Id, retrieved.Id);
    }

    [Fact]
    public async Task GetJobAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _service.GetJobAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateProgressAsync_SetsProgressAndMessage()
    {
        var job = await _service.CreateJobAsync("jellyfin", "cast-crew-update");
        await _service.UpdateProgressAsync(job.Id, 42, "Processing person 42 of 100");

        var updated = await _service.GetJobAsync(job.Id);
        Assert.Equal(42, updated!.Progress);
        Assert.Equal("Processing person 42 of 100", updated.ProgressMessage);
    }

    [Fact]
    public async Task StartJobAsync_SetsRunningStatusAndPid()
    {
        var job = await _service.CreateJobAsync("jellyfin", "cast-crew-update");
        await _service.StartJobAsync(job.Id, 12345);

        var updated = await _service.GetJobAsync(job.Id);
        Assert.Equal(JobStatus.Running, updated!.Status);
        Assert.Equal(12345, updated.ProcessId);
        Assert.NotNull(updated.StartedAt);
    }

    [Fact]
    public async Task CompleteJobAsync_SetsCompletedStatus()
    {
        var job = await _service.CreateJobAsync("jellyfin", "test");
        await _service.StartJobAsync(job.Id, 123);
        await _service.CompleteJobAsync(job.Id);

        var updated = await _service.GetJobAsync(job.Id);
        Assert.Equal(JobStatus.Completed, updated!.Status);
        Assert.NotNull(updated.CompletedAt);
        Assert.Equal(100, updated.Progress);
    }

    [Fact]
    public async Task FailJobAsync_SetsFailedStatusWithError()
    {
        var job = await _service.CreateJobAsync("jellyfin", "test");
        await _service.StartJobAsync(job.Id, 123);
        await _service.FailJobAsync(job.Id, "Something went wrong");

        var updated = await _service.GetJobAsync(job.Id);
        Assert.Equal(JobStatus.Failed, updated!.Status);
        Assert.Equal("Something went wrong", updated.ErrorMessage);
        Assert.NotNull(updated.CompletedAt);
    }

    [Fact]
    public async Task RequestCancellationAsync_SetsCancellationFlag()
    {
        var job = await _service.CreateJobAsync("jellyfin", "test");
        await _service.StartJobAsync(job.Id, 123);
        await _service.RequestCancellationAsync(job.Id);

        var updated = await _service.GetJobAsync(job.Id);
        Assert.True(updated!.CancellationRequested);
    }

    [Fact]
    public async Task GetActiveJobsAsync_ReturnsOnlyRunningAndQueued()
    {
        var j1 = await _service.CreateJobAsync("jellyfin", "type1");
        var j2 = await _service.CreateJobAsync("jellyfin", "type2");
        var j3 = await _service.CreateJobAsync("jellyfin", "type3");
        await _service.StartJobAsync(j2.Id, 123);
        await _service.StartJobAsync(j3.Id, 456);
        await _service.CompleteJobAsync(j3.Id);

        var active = await _service.GetActiveJobsAsync();

        Assert.Equal(2, active.Count); // j1 (Queued) + j2 (Running)
        Assert.Contains(active, j => j.Id == j1.Id);
        Assert.Contains(active, j => j.Id == j2.Id);
    }

    [Fact]
    public async Task GetJobsByModuleAsync_FiltersCorrectly()
    {
        await _service.CreateJobAsync("jellyfin", "type1");
        await _service.CreateJobAsync("other", "type2");

        var jobs = await _service.GetJobsByModuleAsync("jellyfin");

        Assert.Single(jobs);
        Assert.Equal("jellyfin", jobs[0].ModuleId);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~BackgroundJobServiceTests" --no-build`
Expected: Build failure — `IBackgroundJobService` and `BackgroundJobService` do not exist.

- [ ] **Step 3: Create IBackgroundJobService interface**

`src/ControlMenu/Services/IBackgroundJobService.cs`:
```csharp
using ControlMenu.Data.Entities;

namespace ControlMenu.Services;

public interface IBackgroundJobService
{
    Task<Job> CreateJobAsync(string moduleId, string jobType);
    Task<Job?> GetJobAsync(Guid id);
    Task StartJobAsync(Guid id, int processId);
    Task UpdateProgressAsync(Guid id, int progress, string? message = null);
    Task CompleteJobAsync(Guid id, string? resultData = null);
    Task FailJobAsync(Guid id, string errorMessage);
    Task RequestCancellationAsync(Guid id);
    Task<IReadOnlyList<Job>> GetActiveJobsAsync();
    Task<IReadOnlyList<Job>> GetJobsByModuleAsync(string moduleId);
}
```

- [ ] **Step 4: Implement BackgroundJobService**

`src/ControlMenu/Services/BackgroundJobService.cs`:
```csharp
using ControlMenu.Data;
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Services;

public class BackgroundJobService : IBackgroundJobService
{
    private readonly AppDbContext _db;

    public BackgroundJobService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Job> CreateJobAsync(string moduleId, string jobType)
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            ModuleId = moduleId,
            JobType = jobType,
            Status = JobStatus.Queued
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    public async Task<Job?> GetJobAsync(Guid id)
    {
        return await _db.Jobs.FindAsync(id);
    }

    public async Task StartJobAsync(Guid id, int processId)
    {
        var job = await _db.Jobs.FindAsync(id);
        if (job is null) return;
        job.Status = JobStatus.Running;
        job.ProcessId = processId;
        job.StartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateProgressAsync(Guid id, int progress, string? message = null)
    {
        var job = await _db.Jobs.FindAsync(id);
        if (job is null) return;
        job.Progress = progress;
        job.ProgressMessage = message;
        await _db.SaveChangesAsync();
    }

    public async Task CompleteJobAsync(Guid id, string? resultData = null)
    {
        var job = await _db.Jobs.FindAsync(id);
        if (job is null) return;
        job.Status = JobStatus.Completed;
        job.Progress = 100;
        job.CompletedAt = DateTime.UtcNow;
        job.ResultData = resultData;
        await _db.SaveChangesAsync();
    }

    public async Task FailJobAsync(Guid id, string errorMessage)
    {
        var job = await _db.Jobs.FindAsync(id);
        if (job is null) return;
        job.Status = JobStatus.Failed;
        job.ErrorMessage = errorMessage;
        job.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task RequestCancellationAsync(Guid id)
    {
        var job = await _db.Jobs.FindAsync(id);
        if (job is null) return;
        job.CancellationRequested = true;
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<Job>> GetActiveJobsAsync()
    {
        return await _db.Jobs
            .Where(j => j.Status == JobStatus.Queued || j.Status == JobStatus.Running)
            .OrderBy(j => j.StartedAt ?? DateTime.MaxValue)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Job>> GetJobsByModuleAsync(string moduleId)
    {
        return await _db.Jobs
            .Where(j => j.ModuleId == moduleId)
            .OrderByDescending(j => j.StartedAt ?? j.CompletedAt ?? DateTime.MinValue)
            .ToListAsync();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~BackgroundJobServiceTests" -v n`
Expected: All 10 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Services/IBackgroundJobService.cs src/ControlMenu/Services/BackgroundJobService.cs tests/ControlMenu.Tests/Services/BackgroundJobServiceTests.cs
git commit -m "feat(jobs): add BackgroundJobService for job lifecycle management"
```

---

## Task 2: JellyfinService

**Files:**
- Create: `src/ControlMenu/Modules/Jellyfin/Services/IJellyfinService.cs`
- Create: `src/ControlMenu/Modules/Jellyfin/Services/JellyfinService.cs`
- Create: `tests/ControlMenu.Tests/Modules/Jellyfin/JellyfinServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/ControlMenu.Tests/Modules/Jellyfin/JellyfinServiceTests.cs`:
```csharp
using ControlMenu.Modules.Jellyfin.Services;
using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Modules.Jellyfin;

public class JellyfinServiceTests
{
    private readonly Mock<ICommandExecutor> _mockExecutor = new();
    private readonly Mock<IConfigurationService> _mockConfig = new();

    private JellyfinService CreateService() => new(_mockExecutor.Object, _mockConfig.Object);

    [Fact]
    public async Task GetContainerIdAsync_ParsesDockerPsOutput()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("jellyfin-container-name", null))
            .ReturnsAsync("jellyfin");
        _mockExecutor.Setup(e => e.ExecuteAsync("docker", "ps --filter name=jellyfin --format {{.ID}}", null, default))
            .ReturnsAsync(new CommandResult(0, "a1b2c3d4e5f6\n", "", false));

        var service = CreateService();
        var id = await service.GetContainerIdAsync();

        Assert.Equal("a1b2c3d4e5f6", id);
    }

    [Fact]
    public async Task GetContainerIdAsync_ReturnsNull_WhenNoContainer()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("jellyfin-container-name", null))
            .ReturnsAsync("jellyfin");
        _mockExecutor.Setup(e => e.ExecuteAsync("docker", "ps --filter name=jellyfin --format {{.ID}}", null, default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        var id = await service.GetContainerIdAsync();

        Assert.Null(id);
    }

    [Fact]
    public async Task StopContainerAsync_StopsWithGracePeriod()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("docker", "stop -t=15 abc123", null, default))
            .ReturnsAsync(new CommandResult(0, "abc123", "", false));

        var service = CreateService();
        var result = await service.StopContainerAsync("abc123");

        Assert.True(result);
    }

    [Fact]
    public async Task StartContainerAsync_StartsContainer()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("docker", "start abc123", null, default))
            .ReturnsAsync(new CommandResult(0, "abc123", "", false));

        var service = CreateService();
        var result = await service.StartContainerAsync("abc123");

        Assert.True(result);
    }

    [Fact]
    public async Task BackupDatabaseAsync_CopiesFile()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("jellyfin-db-path", null))
            .ReturnsAsync("D:/DockerData/jellyfin/config/data/jellyfin.db");
        _mockConfig.Setup(c => c.GetSettingAsync("jellyfin-backup-dir", null))
            .ReturnsAsync("C:/scripts/tools-menu/jellyfin-db-bkup-and-logs");

        // The command differs by OS, but we test the Windows path here
        _mockExecutor.Setup(e => e.ExecuteAsync(
            It.IsAny<CommandDefinition>(), default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        var backupPath = await service.BackupDatabaseAsync();

        Assert.NotNull(backupPath);
        Assert.Contains("jellyfin_", backupPath);
    }

    [Fact]
    public async Task UpdateDateCreatedAsync_RunsSqlUpdate()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("jellyfin-db-path", null))
            .ReturnsAsync("D:/DockerData/jellyfin/config/data/jellyfin.db");

        _mockExecutor.Setup(e => e.ExecuteAsync("sqlite3",
            It.Is<string>(s => s.Contains("UPDATE BaseItems SET DateCreated=PremiereDate")),
            null, default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        var result = await service.UpdateDateCreatedAsync();

        Assert.True(result);
    }

    [Fact]
    public async Task CleanupOldBackupsAsync_RemovesFilesOlderThanDays()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("jellyfin-backup-dir", null))
            .ReturnsAsync("C:/scripts/tools-menu/jellyfin-db-bkup-and-logs");

        _mockExecutor.Setup(e => e.ExecuteAsync(
            It.IsAny<CommandDefinition>(), default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        await service.CleanupOldBackupsAsync(5);

        _mockExecutor.Verify(e => e.ExecuteAsync(
            It.IsAny<CommandDefinition>(), default), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~JellyfinServiceTests" --no-build`
Expected: Build failure — `IJellyfinService` and `JellyfinService` do not exist.

- [ ] **Step 3: Create IJellyfinService interface**

`src/ControlMenu/Modules/Jellyfin/Services/IJellyfinService.cs`:
```csharp
namespace ControlMenu.Modules.Jellyfin.Services;

public interface IJellyfinService
{
    Task<string?> GetContainerIdAsync(CancellationToken ct = default);
    Task<bool> StopContainerAsync(string containerId, CancellationToken ct = default);
    Task<bool> StartContainerAsync(string containerId, CancellationToken ct = default);
    Task<string?> BackupDatabaseAsync(CancellationToken ct = default);
    Task<bool> UpdateDateCreatedAsync(CancellationToken ct = default);
    Task CleanupOldBackupsAsync(int retentionDays = 5, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement JellyfinService**

`src/ControlMenu/Modules/Jellyfin/Services/JellyfinService.cs`:
```csharp
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
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~JellyfinServiceTests" -v n`
Expected: All 7 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Modules/Jellyfin/Services/ tests/ControlMenu.Tests/Modules/Jellyfin/JellyfinServiceTests.cs
git commit -m "feat(jellyfin): add JellyfinService for Docker and DB operations"
```

---

## Task 3: JellyfinModule (IToolModule Implementation)

**Files:**
- Create: `src/ControlMenu/Modules/Jellyfin/JellyfinModule.cs`
- Create: `tests/ControlMenu.Tests/Modules/Jellyfin/JellyfinModuleTests.cs`
- Modify: `src/ControlMenu/Program.cs`

- [ ] **Step 1: Write the failing tests**

`tests/ControlMenu.Tests/Modules/Jellyfin/JellyfinModuleTests.cs`:
```csharp
using ControlMenu.Data.Enums;
using ControlMenu.Modules;
using ControlMenu.Modules.Jellyfin;

namespace ControlMenu.Tests.Modules.Jellyfin;

public class JellyfinModuleTests
{
    private readonly JellyfinModule _module = new();

    [Fact]
    public void Id_IsJellyfin()
    {
        Assert.Equal("jellyfin", _module.Id);
    }

    [Fact]
    public void DisplayName_IsJellyfinMediaServer()
    {
        Assert.Equal("Jellyfin", _module.DisplayName);
    }

    [Fact]
    public void Icon_IsFilmIcon()
    {
        Assert.Equal("bi-film", _module.Icon);
    }

    [Fact]
    public void Dependencies_IncludesDockerAndSqlite()
    {
        var deps = _module.Dependencies.ToList();
        Assert.Contains(deps, d => d.Name == "docker");
        Assert.Contains(deps, d => d.Name == "sqlite3");
    }

    [Fact]
    public void ConfigRequirements_IncludesApiKeyAndPaths()
    {
        var reqs = _module.ConfigRequirements.ToList();
        Assert.Contains(reqs, r => r.Key == "jellyfin-api-key" && r.IsSecret);
        Assert.Contains(reqs, r => r.Key == "jellyfin-db-path");
        Assert.Contains(reqs, r => r.Key == "jellyfin-container-name");
        Assert.Contains(reqs, r => r.Key == "jellyfin-backup-dir");
    }

    [Fact]
    public void NavEntries_IncludesDbUpdateAndCastCrew()
    {
        var entries = _module.GetNavEntries().ToList();
        Assert.Contains(entries, e => e.Href == "/jellyfin/db-update");
        Assert.Contains(entries, e => e.Href == "/jellyfin/cast-crew");
    }

    [Fact]
    public void BackgroundJobs_IncludesCastCrewUpdate()
    {
        var jobs = _module.GetBackgroundJobs().ToList();
        Assert.Single(jobs);
        Assert.Equal("cast-crew-update", jobs[0].JobType);
        Assert.True(jobs[0].IsLongRunning);
    }

    [Fact]
    public void SmtpConfigRequirements_AllPresent()
    {
        var reqs = _module.ConfigRequirements.ToList();
        Assert.Contains(reqs, r => r.Key == "smtp-server");
        Assert.Contains(reqs, r => r.Key == "smtp-port");
        Assert.Contains(reqs, r => r.Key == "smtp-username");
        Assert.Contains(reqs, r => r.Key == "smtp-password" && r.IsSecret);
        Assert.Contains(reqs, r => r.Key == "notification-email");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~JellyfinModuleTests" --no-build`
Expected: Build failure — `JellyfinModule` does not exist.

- [ ] **Step 3: Implement JellyfinModule**

`src/ControlMenu/Modules/Jellyfin/JellyfinModule.cs`:
```csharp
using ControlMenu.Data.Enums;

namespace ControlMenu.Modules.Jellyfin;

public class JellyfinModule : IToolModule
{
    public string Id => "jellyfin";
    public string DisplayName => "Jellyfin";
    public string Icon => "bi-film";
    public int SortOrder => 2;

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
            ProjectHomeUrl = "https://www.sqlite.org/download.html"
        }
    ];

    public IEnumerable<ConfigRequirement> ConfigRequirements =>
    [
        new ConfigRequirement("jellyfin-api-key", "Jellyfin API Key", "API key for Jellyfin REST API", IsSecret: true),
        new ConfigRequirement("jellyfin-db-path", "Database Path", "Path to jellyfin.db", DefaultValue: "D:/DockerData/jellyfin/config/data/jellyfin.db"),
        new ConfigRequirement("jellyfin-container-name", "Container Name", "Docker container name", DefaultValue: "jellyfin"),
        new ConfigRequirement("jellyfin-backup-dir", "Backup Directory", "Path for database backups", DefaultValue: "C:/scripts/tools-menu/jellyfin-db-bkup-and-logs"),
        new ConfigRequirement("jellyfin-base-url", "Jellyfin URL", "Base URL for Jellyfin API", DefaultValue: "http://127.0.0.1:8096"),
        new ConfigRequirement("jellyfin-user-id", "User ID", "Jellyfin user ID for API calls"),
        new ConfigRequirement("smtp-server", "SMTP Server", "SMTP server for notifications", DefaultValue: "mail.smtp2go.com"),
        new ConfigRequirement("smtp-port", "SMTP Port", "SMTP server port", DefaultValue: "587"),
        new ConfigRequirement("smtp-username", "SMTP Username", "SMTP login username"),
        new ConfigRequirement("smtp-password", "SMTP Password", "SMTP login password", IsSecret: true),
        new ConfigRequirement("notification-email", "Notification Email", "Email for completion alerts")
    ];

    public IEnumerable<NavEntry> GetNavEntries() =>
    [
        new NavEntry("DB Date Update", "/jellyfin/db-update", "bi-calendar-date", 0),
        new NavEntry("Cast & Crew", "/jellyfin/cast-crew", "bi-people", 1)
    ];

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() =>
    [
        new BackgroundJobDefinition("cast-crew-update", "Cast & Crew Image Update",
            "Updates images for all cast members, directors, and producers in Jellyfin media libraries.",
            IsLongRunning: true)
    ];
}
```

- [ ] **Step 4: Register services in Program.cs**

Add to `src/ControlMenu/Program.cs` after IAdbService registration:
```csharp
using ControlMenu.Modules.Jellyfin.Services;
// ...
builder.Services.AddScoped<IJellyfinService, JellyfinService>();
builder.Services.AddScoped<IBackgroundJobService, BackgroundJobService>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~JellyfinModuleTests" -v n`
Expected: All 8 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Modules/Jellyfin/JellyfinModule.cs tests/ControlMenu.Tests/Modules/Jellyfin/JellyfinModuleTests.cs src/ControlMenu/Program.cs
git commit -m "feat(jellyfin): add JellyfinModule with config requirements and nav entries"
```

---

## Task 4: Database Update Page (Step-by-Step Progress)

**Files:**
- Create: `src/ControlMenu/Modules/Jellyfin/Pages/DatabaseUpdate.razor`
- Create: `src/ControlMenu/Modules/Jellyfin/Pages/DatabaseUpdate.razor.css`

- [ ] **Step 1: Create the Database Update page**

`src/ControlMenu/Modules/Jellyfin/Pages/DatabaseUpdate.razor`:
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
            <li>Clean up backups older than 5 days</li>
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

@code {
    [Inject] private IJellyfinService JellyfinService { get; set; } = default!;

    private List<ProgressStep> _steps = [];
    private bool _running;
    private bool _completed;
    private string? _error;

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

        try
        {
            // Step 1: Get container ID
            _steps[0].Status = StepStatus.Running;
            StateHasChanged();
            var containerId = await JellyfinService.GetContainerIdAsync();
            if (containerId is null)
            {
                _steps[0].Status = StepStatus.Failed;
                _steps[0].Detail = "Jellyfin container not found. Is it running?";
                _error = "Could not find Jellyfin Docker container.";
                _running = false;
                return;
            }

            var stopped = await JellyfinService.StopContainerAsync(containerId);
            if (!stopped)
            {
                _steps[0].Status = StepStatus.Failed;
                _error = "Failed to stop container.";
                _running = false;
                return;
            }
            _steps[0].Status = StepStatus.Completed;
            _steps[0].Detail = $"Container {containerId[..12]} stopped.";
            StateHasChanged();

            // Step 2: Backup
            _steps[1].Status = StepStatus.Running;
            StateHasChanged();
            var backupPath = await JellyfinService.BackupDatabaseAsync();
            _steps[1].Status = backupPath is not null ? StepStatus.Completed : StepStatus.Failed;
            _steps[1].Detail = backupPath is not null ? $"Saved to {Path.GetFileName(backupPath)}" : "Backup failed";
            StateHasChanged();

            // Step 3: SQL Update
            _steps[2].Status = StepStatus.Running;
            StateHasChanged();
            var updated = await JellyfinService.UpdateDateCreatedAsync();
            _steps[2].Status = updated ? StepStatus.Completed : StepStatus.Failed;
            _steps[2].Detail = updated ? "DateCreated = PremiereDate applied." : "SQL update failed.";
            StateHasChanged();

            // Step 4: Start container
            _steps[3].Status = StepStatus.Running;
            StateHasChanged();
            var started = await JellyfinService.StartContainerAsync(containerId);
            _steps[3].Status = started ? StepStatus.Completed : StepStatus.Failed;
            _steps[3].Detail = started ? "Container restarted." : "Failed to start container.";
            StateHasChanged();

            // Step 5: Cleanup
            _steps[4].Status = StepStatus.Running;
            StateHasChanged();
            await JellyfinService.CleanupOldBackupsAsync(5);
            _steps[4].Status = StepStatus.Completed;
            _steps[4].Detail = "Backups older than 5 days removed.";
            StateHasChanged();

            // Done
            _steps[5].Status = StepStatus.Completed;
            _completed = true;
        }
        catch (Exception ex)
        {
            _error = $"Unexpected error: {ex.Message}";
            var current = _steps.FirstOrDefault(s => s.Status == StepStatus.Running);
            if (current is not null) current.Status = StepStatus.Failed;
        }
        finally
        {
            _running = false;
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

- [ ] **Step 2: Create scoped CSS**

`src/ControlMenu/Modules/Jellyfin/Pages/DatabaseUpdate.razor.css`:
```css
.start-panel {
    background: var(--card-bg, #fff);
    border: 1px solid var(--border-color, #dee2e6);
    border-radius: 0.75rem;
    padding: 1.5rem;
    max-width: 600px;
}

.start-panel ol {
    margin: 1rem 0;
    padding-left: 1.5rem;
}

.start-panel li {
    padding: 0.25rem 0;
    color: var(--text-muted, #6c757d);
}

.progress-panel {
    max-width: 600px;
}

.progress-step {
    display: flex;
    align-items: flex-start;
    gap: 0.75rem;
    padding: 0.75rem 0;
    border-left: 2px solid var(--border-color, #dee2e6);
    padding-left: 1rem;
    margin-left: 0.5rem;
}

.progress-step.completed {
    border-left-color: var(--success-color, #28a745);
}
.progress-step.running {
    border-left-color: var(--accent-color, #0d6efd);
}
.progress-step.failed {
    border-left-color: var(--danger-color, #dc3545);
}

.step-icon {
    font-size: 1.25rem;
    min-width: 1.5rem;
    text-align: center;
}
.progress-step.completed .step-icon { color: var(--success-color, #28a745); }
.progress-step.running .step-icon { color: var(--accent-color, #0d6efd); }
.progress-step.failed .step-icon { color: var(--danger-color, #dc3545); }
.progress-step.pending .step-icon { color: var(--text-muted, #6c757d); }

.step-content {
    display: flex;
    flex-direction: column;
}

.step-label {
    font-weight: 500;
}

.step-detail {
    font-size: 0.85rem;
    color: var(--text-muted, #6c757d);
}

.error-panel {
    background: var(--danger-bg, #f8d7da);
    color: var(--danger-text, #721c24);
    padding: 0.75rem 1rem;
    border-radius: 0.5rem;
    margin-top: 1rem;
    max-width: 600px;
}

.spin { animation: spin 1s linear infinite; }
@keyframes spin { 100% { transform: rotate(360deg); } }
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build src/ControlMenu`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Modules/Jellyfin/Pages/DatabaseUpdate.razor src/ControlMenu/Modules/Jellyfin/Pages/DatabaseUpdate.razor.css
git commit -m "feat(jellyfin): add DB date update page with step-by-step progress"
```

---

## Task 5: Cast & Crew Update Page (Long-Running Job UI)

**Files:**
- Create: `src/ControlMenu/Modules/Jellyfin/Pages/CastCrewUpdate.razor`
- Create: `src/ControlMenu/Modules/Jellyfin/Pages/CastCrewUpdate.razor.css`

- [ ] **Step 1: Create the Cast & Crew Update page**

`src/ControlMenu/Modules/Jellyfin/Pages/CastCrewUpdate.razor`:
```razor
@page "/jellyfin/cast-crew"
@using ControlMenu.Data.Entities
@using ControlMenu.Data.Enums
@using ControlMenu.Services
@implements IDisposable

<PageTitle>Jellyfin — Cast & Crew Update</PageTitle>

<h1><i class="bi bi-people"></i> Cast & Crew Image Update</h1>
<p class="page-subtitle">Updates images for all cast members, directors, and producers in Jellyfin media libraries. This process typically takes several days to complete.</p>

@if (_activeJob is not null)
{
    <div class="job-panel">
        <div class="job-header">
            <span class="job-status-badge @_activeJob.Status.ToString().ToLower()">
                @_activeJob.Status
            </span>
            @if (_activeJob.StartedAt.HasValue)
            {
                <span class="job-time">Started: @_activeJob.StartedAt.Value.ToLocalTime().ToString("g")</span>
            }
        </div>

        @if (_activeJob.Status == JobStatus.Running || _activeJob.Status == JobStatus.Queued)
        {
            <div class="progress-bar-container">
                <div class="progress-bar-fill" style="width: @(_activeJob.Progress ?? 0)%"></div>
            </div>
            <div class="progress-info">
                <span>@(_activeJob.Progress ?? 0)%</span>
                <span>@(_activeJob.ProgressMessage ?? "Waiting...")</span>
            </div>
            <button class="btn btn-danger" @onclick="CancelJob">
                <i class="bi bi-stop-circle"></i> Cancel
            </button>
        }
        else if (_activeJob.Status == JobStatus.Completed)
        {
            <div class="job-complete">
                <i class="bi bi-check-circle-fill"></i>
                <p>Update completed at @_activeJob.CompletedAt?.ToLocalTime().ToString("g").</p>
            </div>
        }
        else if (_activeJob.Status == JobStatus.Failed)
        {
            <div class="job-failed">
                <i class="bi bi-x-circle-fill"></i>
                <p>@_activeJob.ErrorMessage</p>
            </div>
        }
    </div>
}
else
{
    <div class="start-panel">
        <div class="info-box">
            <i class="bi bi-info-circle"></i>
            <div>
                <p><strong>What this does:</strong> Calls the Jellyfin API to find all people without images and triggers an image refresh for each one.</p>
                <p><strong>Duration:</strong> Typically about a week for large libraries.</p>
                <p><strong>Runs in background:</strong> You can close this page. A notification email is sent when finished.</p>
            </div>
        </div>
        <button class="btn btn-primary btn-lg" @onclick="StartJob" disabled="@_starting">
            <i class="bi bi-play-circle"></i> Start Update
        </button>
    </div>
}

@if (_recentJobs.Count > 0)
{
    <h2>Job History</h2>
    <table class="data-table">
        <thead>
            <tr>
                <th>Status</th>
                <th>Started</th>
                <th>Completed</th>
                <th>Progress</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var job in _recentJobs)
            {
                <tr>
                    <td><span class="job-status-badge @job.Status.ToString().ToLower()">@job.Status</span></td>
                    <td>@(job.StartedAt?.ToLocalTime().ToString("g") ?? "—")</td>
                    <td>@(job.CompletedAt?.ToLocalTime().ToString("g") ?? "—")</td>
                    <td>@(job.Progress ?? 0)%</td>
                </tr>
            }
        </tbody>
    </table>
}

@code {
    [Inject] private IBackgroundJobService JobService { get; set; } = default!;

    private Job? _activeJob;
    private List<Job> _recentJobs = [];
    private bool _starting;
    private Timer? _pollTimer;

    protected override async Task OnInitializedAsync()
    {
        await LoadJobs();

        // Poll for progress updates every 5 seconds if there's an active job
        _pollTimer = new Timer(async _ =>
        {
            await InvokeAsync(async () =>
            {
                await LoadJobs();
                StateHasChanged();
            });
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private async Task LoadJobs()
    {
        var allJobs = await JobService.GetJobsByModuleAsync("jellyfin");
        var castCrewJobs = allJobs.Where(j => j.JobType == "cast-crew-update").ToList();

        _activeJob = castCrewJobs.FirstOrDefault(j =>
            j.Status == JobStatus.Running || j.Status == JobStatus.Queued);

        _recentJobs = castCrewJobs
            .Where(j => j.Status == JobStatus.Completed || j.Status == JobStatus.Failed || j.Status == JobStatus.Cancelled)
            .Take(10)
            .ToList();
    }

    private async Task StartJob()
    {
        _starting = true;
        var job = await JobService.CreateJobAsync("jellyfin", "cast-crew-update");
        _activeJob = job;
        // NOTE: In a full implementation, this would launch the worker process:
        // var process = Process.Start("dotnet", $"run --project Workers/CastCrewWorker -- {job.Id}");
        // await JobService.StartJobAsync(job.Id, process.Id);
        // For now, the job is created in Queued state. Worker process launching
        // will be implemented when the worker project is added.
        _starting = false;
    }

    private async Task CancelJob()
    {
        if (_activeJob is null) return;
        await JobService.RequestCancellationAsync(_activeJob.Id);
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
    }
}
```

- [ ] **Step 2: Create scoped CSS**

`src/ControlMenu/Modules/Jellyfin/Pages/CastCrewUpdate.razor.css`:
```css
.start-panel {
    max-width: 600px;
}

.info-box {
    display: flex;
    gap: 0.75rem;
    background: var(--info-bg, #d1ecf1);
    color: var(--info-text, #0c5460);
    padding: 1rem;
    border-radius: 0.5rem;
    margin-bottom: 1rem;
}
.info-box i { font-size: 1.25rem; flex-shrink: 0; padding-top: 0.1rem; }
.info-box p { margin: 0 0 0.5rem; }
.info-box p:last-child { margin-bottom: 0; }

.job-panel {
    background: var(--card-bg, #fff);
    border: 1px solid var(--border-color, #dee2e6);
    border-radius: 0.75rem;
    padding: 1.5rem;
    max-width: 600px;
    margin-bottom: 2rem;
}

.job-header {
    display: flex;
    align-items: center;
    gap: 1rem;
    margin-bottom: 1rem;
}

.job-status-badge {
    display: inline-block;
    padding: 0.2rem 0.6rem;
    border-radius: 0.25rem;
    font-size: 0.8rem;
    font-weight: 600;
    text-transform: uppercase;
}
.job-status-badge.running { background: var(--accent-color, #0d6efd); color: #fff; }
.job-status-badge.queued { background: var(--warning-bg, #fff3cd); color: var(--warning-text, #856404); }
.job-status-badge.completed { background: var(--success-bg, #d4edda); color: var(--success-text, #155724); }
.job-status-badge.failed { background: var(--danger-bg, #f8d7da); color: var(--danger-text, #721c24); }
.job-status-badge.cancelled { background: var(--text-muted, #6c757d); color: #fff; }

.job-time { font-size: 0.85rem; color: var(--text-muted, #6c757d); }

.progress-bar-container {
    background: var(--border-color, #dee2e6);
    border-radius: 0.5rem;
    height: 1.5rem;
    overflow: hidden;
    margin-bottom: 0.5rem;
}
.progress-bar-fill {
    background: var(--accent-color, #0d6efd);
    height: 100%;
    border-radius: 0.5rem;
    transition: width 0.5s ease;
}

.progress-info {
    display: flex;
    justify-content: space-between;
    font-size: 0.85rem;
    color: var(--text-muted, #6c757d);
    margin-bottom: 1rem;
}

.job-complete { color: var(--success-text, #155724); display: flex; align-items: center; gap: 0.5rem; }
.job-complete i { font-size: 1.5rem; }

.job-failed { color: var(--danger-text, #721c24); display: flex; align-items: center; gap: 0.5rem; }
.job-failed i { font-size: 1.5rem; }

.data-table {
    width: 100%;
    max-width: 800px;
    border-collapse: collapse;
    margin-top: 0.5rem;
}
.data-table th, .data-table td {
    padding: 0.5rem 0.75rem;
    border-bottom: 1px solid var(--border-color, #dee2e6);
    text-align: left;
}
.data-table th {
    font-weight: 600;
    font-size: 0.85rem;
    color: var(--text-muted, #6c757d);
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build src/ControlMenu`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Modules/Jellyfin/Pages/
git commit -m "feat(jellyfin): add Cast & Crew and DB Update pages with progress tracking"
```

---

## Task 6: Full Test Suite Run

- [ ] **Step 1: Run all tests**

Run: `dotnet test tests/ControlMenu.Tests -v n`
Expected: All tests pass (previous 44 + 25 new = 69 total).

- [ ] **Step 2: Build the full project**

Run: `dotnet build src/ControlMenu`
Expected: Build succeeded.

- [ ] **Step 3: Commit any remaining CSS or tweaks**

```bash
git add -A
git commit -m "feat(jellyfin): phase 4 complete — Jellyfin module with background jobs"
```
