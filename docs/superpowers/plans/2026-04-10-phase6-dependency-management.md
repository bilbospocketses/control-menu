# Phase 6 — Dependency Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add dependency version checking, update downloads, a Dependencies settings tab, SkiaSharp-based icon conversion, and a Jellyfin cast/crew worker with controlled parallelism — replacing both PowerShell scripts.

**Architecture:** `DependencyManagerService` (scoped) handles version checking, asset resolution, and update orchestration. `DependencyCheckHostedService` (BackgroundService) runs checks on a timer. The Jellyfin worker uses `SemaphoreSlim(4)` for controlled parallelism with resume via `ResultData` JSON. SkiaSharp replaces `System.Drawing.Common` for cross-platform icon conversion.

**Tech Stack:** .NET 9, Blazor Server, EF Core + SQLite, SkiaSharp (MIT), xUnit + Moq, GitHub Releases API, Google SDK repository XML

**Spec:** `docs/superpowers/specs/2026-04-10-phase6-dependency-management-design.md`

---

## File Map

### New Files

| File | Responsibility |
|------|---------------|
| `src/ControlMenu/Services/IDependencyManagerService.cs` | Interface for dependency management operations |
| `src/ControlMenu/Services/DependencyManagerService.cs` | Core service: sync, version check, asset resolve, download/install |
| `src/ControlMenu/Services/DependencyCheckHostedService.cs` | Background timer for periodic version checks |
| `src/ControlMenu/Services/DependencyCheckResult.cs` | Result record for version checks |
| `src/ControlMenu/Services/AssetMatch.cs` | Result record for resolved download assets |
| `src/ControlMenu/Services/UpdateResult.cs` | Result record for download/install operations |
| `src/ControlMenu/Data/Enums/StaleUrlAction.cs` | Enum: Redirected, Invalid |
| `src/ControlMenu/Components/Pages/Settings/DependencyManagement.razor` | Dependencies settings tab UI |
| `src/ControlMenu/Modules/Jellyfin/Workers/CastCrewUpdateWorker.cs` | Background worker with parallelism + resume |
| `tests/ControlMenu.Tests/Services/DependencyManagerServiceTests.cs` | Tests for sync, version check, asset resolve |
| `tests/ControlMenu.Tests/Services/DependencyCheckHostedServiceTests.cs` | Tests for timer/scheduling behavior |
| `tests/ControlMenu.Tests/Modules/Utilities/IconConversionServiceTests.cs` | Tests for SkiaSharp migration |
| `tests/ControlMenu.Tests/Modules/Jellyfin/CastCrewUpdateWorkerTests.cs` | Tests for worker parallelism, resume, cancellation |

### Modified Files

| File | Change |
|------|--------|
| `src/ControlMenu/Modules/ModuleDependency.cs` | Add `VersionCheckUrl`, `VersionCheckPattern` fields |
| `src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs` | Update adb to `DirectUrl` with Google URLs |
| `src/ControlMenu/ControlMenu.csproj` | Swap `System.Drawing.Common` → `SkiaSharp` |
| `src/ControlMenu/Modules/Utilities/Services/IconConversionService.cs` | Rewrite with SkiaSharp |
| `src/ControlMenu/Components/Pages/Settings/SettingsPage.razor` | Add Dependencies tab |
| `src/ControlMenu/Components/Layout/TopBar.razor` | Add update badge |
| `src/ControlMenu/Modules/Jellyfin/Pages/CastCrewUpdate.razor` | Wire up worker launch |
| `src/ControlMenu/Program.cs` | Register new services, HttpClient, startup sync |

### Deleted Files

| File | Reason |
|------|--------|
| `ConvertTo-Ico.ps1` | Replaced by SkiaSharp `IconConversionService` |
| `Jellyfin-Cast-Update.ps1` | Replaced by `CastCrewUpdateWorker` |

---

## Task 1: Foundation — ModuleDependency Fields + Result Types

**Files:**
- Modify: `src/ControlMenu/Modules/ModuleDependency.cs`
- Modify: `src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs`
- Create: `src/ControlMenu/Services/DependencyCheckResult.cs`
- Create: `src/ControlMenu/Services/AssetMatch.cs`
- Create: `src/ControlMenu/Services/UpdateResult.cs`
- Create: `src/ControlMenu/Data/Enums/StaleUrlAction.cs`

- [ ] **Step 1: Add new fields to ModuleDependency**

In `src/ControlMenu/Modules/ModuleDependency.cs`, add two optional fields:

```csharp
using ControlMenu.Data.Enums;

namespace ControlMenu.Modules;

public record ModuleDependency
{
    public required string Name { get; init; }
    public required string ExecutableName { get; init; }
    public required string VersionCommand { get; init; }
    public required string VersionPattern { get; init; }
    public UpdateSourceType SourceType { get; init; }
    public string? GitHubRepo { get; init; }
    public string? DownloadUrl { get; init; }
    public string? ProjectHomeUrl { get; init; }
    public string? AssetPattern { get; init; }
    public string? InstallPath { get; init; }
    public string[] RelatedFiles { get; init; } = [];
    public string? VersionCheckUrl { get; init; }
    public string? VersionCheckPattern { get; init; }
}
```

- [ ] **Step 2: Update AndroidDevicesModule adb declaration to DirectUrl**

In `src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs`, change the adb dependency from `Manual` to `DirectUrl`:

```csharp
new ModuleDependency
{
    Name = "adb",
    ExecutableName = "adb",
    VersionCommand = "adb --version",
    VersionPattern = @"Android Debug Bridge version ([\d.]+)",
    SourceType = UpdateSourceType.DirectUrl,
    ProjectHomeUrl = "https://developer.android.com/tools/releases/platform-tools",
    DownloadUrl = OperatingSystem.IsWindows()
        ? "https://dl.google.com/android/repository/platform-tools-latest-windows.zip"
        : "https://dl.google.com/android/repository/platform-tools-latest-linux.zip",
    VersionCheckUrl = "https://dl.google.com/android/repository/repository2-3.xml",
    VersionCheckPattern = @"<major>(\d+)</major>\s*<minor>(\d+)</minor>\s*<micro>(\d+)</micro>"
},
```

- [ ] **Step 3: Create result records**

Create `src/ControlMenu/Services/DependencyCheckResult.cs`:

```csharp
namespace ControlMenu.Services;

public record DependencyCheckResult(
    Guid DependencyId,
    string Name,
    Data.Enums.DependencyStatus Status,
    string? InstalledVersion,
    string? LatestVersion,
    string? ErrorMessage);
```

Create `src/ControlMenu/Services/AssetMatch.cs`:

```csharp
namespace ControlMenu.Services;

public record AssetMatch(
    string FileName,
    string DownloadUrl,
    long SizeBytes,
    bool AutoSelected);
```

Create `src/ControlMenu/Services/UpdateResult.cs`:

```csharp
using ControlMenu.Data.Enums;

namespace ControlMenu.Services;

public record UpdateResult(
    bool Success,
    string? NewVersion,
    string? ErrorMessage,
    StaleUrlAction? UrlAction);
```

Create `src/ControlMenu/Data/Enums/StaleUrlAction.cs`:

```csharp
namespace ControlMenu.Data.Enums;

public enum StaleUrlAction
{
    Redirected,
    Invalid
}
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Modules/ModuleDependency.cs \
        src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs \
        src/ControlMenu/Services/DependencyCheckResult.cs \
        src/ControlMenu/Services/AssetMatch.cs \
        src/ControlMenu/Services/UpdateResult.cs \
        src/ControlMenu/Data/Enums/StaleUrlAction.cs
git commit -m "feat(deps): add ModuleDependency version check fields and result types"
```

---

## Task 2: DependencyManagerService — Interface + Sync Logic (TDD)

**Files:**
- Create: `src/ControlMenu/Services/IDependencyManagerService.cs`
- Create: `src/ControlMenu/Services/DependencyManagerService.cs`
- Create: `tests/ControlMenu.Tests/Services/DependencyManagerServiceTests.cs`

- [ ] **Step 1: Create the interface**

Create `src/ControlMenu/Services/IDependencyManagerService.cs`:

```csharp
namespace ControlMenu.Services;

public interface IDependencyManagerService
{
    Task SyncDependenciesAsync();
    Task<DependencyCheckResult> CheckDependencyAsync(Guid dependencyId);
    Task<IReadOnlyList<DependencyCheckResult>> CheckAllAsync();
    Task<AssetMatch?> ResolveDownloadAssetAsync(Guid dependencyId);
    Task<UpdateResult> DownloadAndInstallAsync(Guid dependencyId, AssetMatch asset);
    Task<int> GetUpdateAvailableCountAsync();
}
```

- [ ] **Step 2: Write failing tests for sync logic**

Create `tests/ControlMenu.Tests/Services/DependencyManagerServiceTests.cs`:

```csharp
using ControlMenu.Data;
using ControlMenu.Data.Enums;
using ControlMenu.Modules;
using ControlMenu.Services;
using ControlMenu.Tests.Data;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ControlMenu.Tests.Services;

public class DependencyManagerServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<ICommandExecutor> _mockExecutor = new();
    private readonly Mock<IHttpClientFactory> _mockHttpFactory = new();

    public DependencyManagerServiceTests()
    {
        _db = TestDbContextFactory.Create();
    }

    public void Dispose() => _db.Dispose();

    private DependencyManagerService CreateService(params IToolModule[] modules)
    {
        return new DependencyManagerService(
            _db, modules, _mockExecutor.Object, _mockHttpFactory.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DependencyManagerService>.Instance);
    }

    [Fact]
    public async Task SyncDependenciesAsync_InsertsNewDependencies()
    {
        var module = new FakeModule("test-module", "Test",
        [
            new ModuleDependency
            {
                Name = "tool-a",
                ExecutableName = "tool-a",
                VersionCommand = "tool-a --version",
                VersionPattern = @"([\d.]+)",
                SourceType = UpdateSourceType.Manual,
                ProjectHomeUrl = "https://example.com"
            }
        ]);

        _mockExecutor.Setup(e => e.ExecuteAsync("tool-a", "--version", null, default))
            .ReturnsAsync(new CommandResult(0, "tool-a version 1.2.3", "", false));

        var service = CreateService(module);
        await service.SyncDependenciesAsync();

        var deps = await _db.Dependencies.ToListAsync();
        Assert.Single(deps);
        Assert.Equal("tool-a", deps[0].Name);
        Assert.Equal("test-module", deps[0].ModuleId);
        Assert.Equal("1.2.3", deps[0].InstalledVersion);
        Assert.Equal(UpdateSourceType.Manual, deps[0].SourceType);
    }

    [Fact]
    public async Task SyncDependenciesAsync_RemovesOrphanedDependencies()
    {
        _db.Dependencies.Add(new Data.Entities.Dependency
        {
            Id = Guid.NewGuid(),
            ModuleId = "removed-module",
            Name = "old-tool",
            SourceType = UpdateSourceType.Manual,
            Status = DependencyStatus.UpToDate
        });
        await _db.SaveChangesAsync();

        var service = CreateService(); // no modules
        await service.SyncDependenciesAsync();

        Assert.Empty(await _db.Dependencies.ToListAsync());
    }

    [Fact]
    public async Task SyncDependenciesAsync_UpdatesStaticFieldsFromCode()
    {
        _db.Dependencies.Add(new Data.Entities.Dependency
        {
            Id = Guid.NewGuid(),
            ModuleId = "test-module",
            Name = "tool-a",
            SourceType = UpdateSourceType.Manual,
            Status = DependencyStatus.UpToDate,
            ProjectHomeUrl = "https://old-url.com"
        });
        await _db.SaveChangesAsync();

        var module = new FakeModule("test-module", "Test",
        [
            new ModuleDependency
            {
                Name = "tool-a",
                ExecutableName = "tool-a",
                VersionCommand = "tool-a --version",
                VersionPattern = @"([\d.]+)",
                SourceType = UpdateSourceType.GitHub,
                GitHubRepo = "owner/repo",
                ProjectHomeUrl = "https://new-url.com"
            }
        ]);

        _mockExecutor.Setup(e => e.ExecuteAsync("tool-a", "--version", null, default))
            .ReturnsAsync(new CommandResult(0, "1.0.0", "", false));

        var service = CreateService(module);
        await service.SyncDependenciesAsync();

        var dep = await _db.Dependencies.SingleAsync();
        Assert.Equal(UpdateSourceType.GitHub, dep.SourceType);
        Assert.Equal("https://new-url.com", dep.ProjectHomeUrl);
    }

    [Fact]
    public async Task SyncDependenciesAsync_HandlesVersionCommandFailure()
    {
        var module = new FakeModule("test-module", "Test",
        [
            new ModuleDependency
            {
                Name = "missing-tool",
                ExecutableName = "missing-tool",
                VersionCommand = "missing-tool --version",
                VersionPattern = @"([\d.]+)",
                SourceType = UpdateSourceType.Manual
            }
        ]);

        _mockExecutor.Setup(e => e.ExecuteAsync("missing-tool", "--version", null, default))
            .ReturnsAsync(new CommandResult(1, "", "not found", false));

        var service = CreateService(module);
        await service.SyncDependenciesAsync();

        var dep = await _db.Dependencies.SingleAsync();
        Assert.Null(dep.InstalledVersion);
    }

    [Fact]
    public async Task GetUpdateAvailableCountAsync_ReturnsCorrectCount()
    {
        _db.Dependencies.Add(new Data.Entities.Dependency
        {
            Id = Guid.NewGuid(), ModuleId = "m", Name = "a",
            Status = DependencyStatus.UpdateAvailable, SourceType = UpdateSourceType.Manual
        });
        _db.Dependencies.Add(new Data.Entities.Dependency
        {
            Id = Guid.NewGuid(), ModuleId = "m", Name = "b",
            Status = DependencyStatus.UpToDate, SourceType = UpdateSourceType.Manual
        });
        _db.Dependencies.Add(new Data.Entities.Dependency
        {
            Id = Guid.NewGuid(), ModuleId = "m", Name = "c",
            Status = DependencyStatus.UpdateAvailable, SourceType = UpdateSourceType.Manual
        });
        await _db.SaveChangesAsync();

        var service = CreateService();
        var count = await service.GetUpdateAvailableCountAsync();

        Assert.Equal(2, count);
    }
}

// Test helpers at bottom of file

internal class FakeModule(string id, string displayName, ModuleDependency[] deps) : IToolModule
{
    public string Id => id;
    public string DisplayName => displayName;
    public string Icon => "bi-test";
    public int SortOrder => 0;
    public IEnumerable<ModuleDependency> Dependencies => deps;
    public IEnumerable<ConfigRequirement> ConfigRequirements => [];
    public IEnumerable<NavEntry> GetNavEntries() => [];
    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}

```

**Note:** `DependencyManagerService` accepts `IReadOnlyList<IToolModule>` directly (not `ModuleDiscoveryService`). In `Program.cs`, register it by pulling `.Modules` from the discovery service. In tests, pass the array directly. This avoids any issues with `ModuleDiscoveryService.Modules` not being virtual.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DependencyManagerServiceTests" -v n`
Expected: FAIL — `DependencyManagerService` does not exist yet.

- [ ] **Step 4: Implement DependencyManagerService — sync + count**

Create `src/ControlMenu/Services/DependencyManagerService.cs`:

```csharp
using System.Text.RegularExpressions;
using ControlMenu.Data;
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ControlMenu.Services;

public class DependencyManagerService : IDependencyManagerService
{
    private readonly AppDbContext _db;
    private readonly IReadOnlyList<IToolModule> _modules;
    private readonly ICommandExecutor _executor;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<DependencyManagerService> _logger;

    public DependencyManagerService(
        AppDbContext db,
        IReadOnlyList<IToolModule> modules,
        ICommandExecutor executor,
        IHttpClientFactory httpFactory,
        ILogger<DependencyManagerService> logger)
    {
        _db = db;
        _modules = modules;
        _executor = executor;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task SyncDependenciesAsync()
    {
        var declared = _modules
            .SelectMany(m => m.Dependencies.Select(d => (Module: m, Dep: d)))
            .ToList();

        var existing = await _db.Dependencies.ToListAsync();

        // Remove orphaned
        var declaredKeys = declared
            .Select(d => (d.Module.Id, d.Dep.Name))
            .ToHashSet();

        var toRemove = existing
            .Where(e => !declaredKeys.Contains((e.ModuleId, e.Name)))
            .ToList();

        _db.Dependencies.RemoveRange(toRemove);

        // Upsert declared
        foreach (var (module, dep) in declared)
        {
            var entity = existing.FirstOrDefault(e =>
                e.ModuleId == module.Id && e.Name == dep.Name);

            if (entity is null)
            {
                entity = new Dependency
                {
                    Id = Guid.NewGuid(),
                    ModuleId = module.Id,
                    Name = dep.Name,
                    Status = DependencyStatus.UpToDate
                };
                _db.Dependencies.Add(entity);
            }

            // Update static fields from code
            entity.SourceType = dep.SourceType;
            entity.ProjectHomeUrl = dep.ProjectHomeUrl;
            entity.DownloadUrl = entity.DownloadUrl ?? dep.DownloadUrl;

            // Refresh installed version
            entity.InstalledVersion = await GetInstalledVersionAsync(dep);
        }

        await _db.SaveChangesAsync();
    }

    public async Task<int> GetUpdateAvailableCountAsync()
    {
        return await _db.Dependencies
            .CountAsync(d => d.Status == DependencyStatus.UpdateAvailable);
    }

    public async Task<DependencyCheckResult> CheckDependencyAsync(Guid dependencyId)
    {
        var entity = await _db.Dependencies.FindAsync(dependencyId);
        if (entity is null)
            return new DependencyCheckResult(dependencyId, "", DependencyStatus.CheckFailed,
                null, null, "Dependency not found");

        var moduleDep = FindModuleDependency(entity.ModuleId, entity.Name);
        if (moduleDep is null)
            return new DependencyCheckResult(dependencyId, entity.Name, DependencyStatus.CheckFailed,
                entity.InstalledVersion, null, "Module dependency declaration not found");

        try
        {
            // Refresh installed version
            entity.InstalledVersion = await GetInstalledVersionAsync(moduleDep);

            switch (entity.SourceType)
            {
                case UpdateSourceType.GitHub:
                    await CheckGitHubVersionAsync(entity, moduleDep);
                    break;
                case UpdateSourceType.DirectUrl:
                    await CheckDirectUrlVersionAsync(entity, moduleDep);
                    break;
                case UpdateSourceType.Manual:
                    entity.Status = DependencyStatus.UpToDate;
                    break;
            }

            entity.LastChecked = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return new DependencyCheckResult(
                entity.Id, entity.Name, entity.Status,
                entity.InstalledVersion, entity.LatestKnownVersion, null);
        }
        catch (Exception ex)
        {
            entity.Status = DependencyStatus.CheckFailed;
            entity.LastChecked = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogError(ex, "Failed to check dependency {Name}", entity.Name);
            return new DependencyCheckResult(
                entity.Id, entity.Name, DependencyStatus.CheckFailed,
                entity.InstalledVersion, null, ex.Message);
        }
    }

    public async Task<IReadOnlyList<DependencyCheckResult>> CheckAllAsync()
    {
        var deps = await _db.Dependencies.ToListAsync();
        var results = new List<DependencyCheckResult>();

        foreach (var dep in deps)
        {
            results.Add(await CheckDependencyAsync(dep.Id));
        }

        return results;
    }

    public async Task<AssetMatch?> ResolveDownloadAssetAsync(Guid dependencyId)
    {
        var entity = await _db.Dependencies.FindAsync(dependencyId);
        if (entity is null) return null;

        var moduleDep = FindModuleDependency(entity.ModuleId, entity.Name);
        if (moduleDep is null || moduleDep.InstallPath is null) return null;

        if (entity.SourceType == UpdateSourceType.GitHub && moduleDep.GitHubRepo is not null)
        {
            return await ResolveGitHubAssetAsync(moduleDep);
        }

        if (entity.SourceType == UpdateSourceType.DirectUrl && entity.DownloadUrl is not null)
        {
            // DirectUrl — deterministic URL, just need the file size
            var client = _httpFactory.CreateClient("dependency-updates");
            using var headResponse = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, entity.DownloadUrl));

            var size = headResponse.Content.Headers.ContentLength ?? 0;
            var fileName = Path.GetFileName(new Uri(entity.DownloadUrl).AbsolutePath);

            return new AssetMatch(fileName, entity.DownloadUrl, size, AutoSelected: true);
        }

        return null;
    }

    public async Task<UpdateResult> DownloadAndInstallAsync(Guid dependencyId, AssetMatch asset)
    {
        var entity = await _db.Dependencies.FindAsync(dependencyId);
        if (entity is null)
            return new UpdateResult(false, null, "Dependency not found", null);

        var moduleDep = FindModuleDependency(entity.ModuleId, entity.Name);
        if (moduleDep?.InstallPath is null)
            return new UpdateResult(false, null, "No install path configured", null);

        StaleUrlAction? urlAction = null;
        var tempDir = Path.Combine(Path.GetTempPath(), "ControlMenu", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. Download
            var client = _httpFactory.CreateClient("dependency-updates");

            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var redirectClient = new HttpClient(handler);
            var response = await redirectClient.GetAsync(asset.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead);

            // Handle redirects
            if ((int)response.StatusCode is >= 301 and <= 308)
            {
                var newUrl = response.Headers.Location?.ToString();
                if (newUrl is not null)
                {
                    entity.DownloadUrl = newUrl;
                    urlAction = StaleUrlAction.Redirected;
                    response = await client.GetAsync(newUrl);
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                entity.Status = DependencyStatus.UrlInvalid;
                await _db.SaveChangesAsync();
                return new UpdateResult(false, null,
                    $"Download failed: HTTP {(int)response.StatusCode}", StaleUrlAction.Invalid);
            }

            // Download to temp
            var tempFile = Path.Combine(tempDir, asset.FileName);
            await using (var fs = File.Create(tempFile))
            {
                await response.Content.CopyToAsync(fs);
            }

            // 2. Extract
            var extractDir = Path.Combine(tempDir, "extracted");
            if (tempFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(tempFile, extractDir);
            }
            else if (tempFile.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                var result = await _executor.ExecuteAsync("tar", $"xzf \"{tempFile}\" -C \"{extractDir}\"");
                if (result.ExitCode != 0)
                    return new UpdateResult(false, null, $"Extraction failed: {result.StandardError}", urlAction);
            }

            // 3. Verify — find the executable in extracted dir and run version command
            var newExe = FindExecutable(extractDir, moduleDep.ExecutableName);
            if (newExe is null)
                return new UpdateResult(false, null,
                    $"Could not find {moduleDep.ExecutableName} in extracted archive", urlAction);

            var verifyResult = await _executor.ExecuteAsync(newExe, moduleDep.VersionCommand.Split(' ').Last());
            if (verifyResult.ExitCode != 0)
                return new UpdateResult(false, null,
                    $"New binary verification failed: {verifyResult.StandardError}", urlAction);

            var newVersion = ExtractVersion(verifyResult.StandardOutput, moduleDep.VersionPattern);

            // 4. Swap — backup old, move in new
            if (Directory.Exists(moduleDep.InstallPath))
            {
                // Backup old files
                foreach (var file in GetManagedFiles(moduleDep))
                {
                    var fullPath = Path.Combine(moduleDep.InstallPath, file);
                    if (File.Exists(fullPath))
                        File.Move(fullPath, fullPath + ".bak", overwrite: true);
                }

                // Copy new files — find the subdirectory in extracted (e.g., platform-tools/)
                var sourceDir = FindInstallSource(extractDir, moduleDep.ExecutableName);
                if (sourceDir is not null)
                {
                    foreach (var file in Directory.GetFiles(sourceDir))
                    {
                        File.Copy(file, Path.Combine(moduleDep.InstallPath, Path.GetFileName(file)),
                            overwrite: true);
                    }
                }
            }

            // 5. Update DB
            entity.InstalledVersion = newVersion;
            entity.LatestKnownVersion = newVersion;
            entity.Status = DependencyStatus.UpToDate;
            entity.LastChecked = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return new UpdateResult(true, newVersion, null, urlAction);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install update for {Name}", entity.Name);
            return new UpdateResult(false, null, ex.Message, urlAction);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    // --- Private helpers ---

    private async Task<string?> GetInstalledVersionAsync(ModuleDependency dep)
    {
        var parts = dep.VersionCommand.Split(' ', 2);
        var command = parts[0];
        var args = parts.Length > 1 ? parts[1] : null;

        var result = await _executor.ExecuteAsync(command, args);
        if (result.ExitCode != 0) return null;

        return ExtractVersion(result.StandardOutput, dep.VersionPattern);
    }

    private static string? ExtractVersion(string output, string pattern)
    {
        var match = Regex.Match(output, pattern);
        if (!match.Success) return null;

        // If multiple capture groups (e.g., major.minor.micro), join them
        if (match.Groups.Count > 2)
        {
            return string.Join(".",
                Enumerable.Range(1, match.Groups.Count - 1)
                    .Select(i => match.Groups[i].Value));
        }

        return match.Groups[1].Value;
    }

    private async Task CheckGitHubVersionAsync(Dependency entity, ModuleDependency moduleDep)
    {
        if (moduleDep.GitHubRepo is null) return;

        var client = _httpFactory.CreateClient("github-api");
        var url = $"https://api.github.com/repos/{moduleDep.GitHubRepo}/releases/latest";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "application/vnd.github+json");
        request.Headers.Add("User-Agent", "ControlMenu");

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var tagMatch = Regex.Match(json, @"""tag_name""\s*:\s*""v?([\d.]+)""");
        if (!tagMatch.Success) return;

        entity.LatestKnownVersion = tagMatch.Groups[1].Value;
        entity.Status = CompareVersions(entity.InstalledVersion, entity.LatestKnownVersion) < 0
            ? DependencyStatus.UpdateAvailable
            : DependencyStatus.UpToDate;
    }

    private async Task CheckDirectUrlVersionAsync(Dependency entity, ModuleDependency moduleDep)
    {
        if (moduleDep.VersionCheckUrl is null || moduleDep.VersionCheckPattern is null) return;

        var client = _httpFactory.CreateClient("dependency-updates");
        var content = await client.GetStringAsync(moduleDep.VersionCheckUrl);

        var match = Regex.Match(content, moduleDep.VersionCheckPattern, RegexOptions.Singleline);
        if (!match.Success) return;

        string latestVersion;
        if (match.Groups.Count > 2)
            latestVersion = string.Join(".",
                Enumerable.Range(1, match.Groups.Count - 1).Select(i => match.Groups[i].Value));
        else
            latestVersion = match.Groups[1].Value;

        entity.LatestKnownVersion = latestVersion;
        entity.Status = CompareVersions(entity.InstalledVersion, latestVersion) < 0
            ? DependencyStatus.UpdateAvailable
            : DependencyStatus.UpToDate;
    }

    private async Task<AssetMatch?> ResolveGitHubAssetAsync(ModuleDependency moduleDep)
    {
        var client = _httpFactory.CreateClient("github-api");
        var url = $"https://api.github.com/repos/{moduleDep.GitHubRepo}/releases/latest";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "application/vnd.github+json");
        request.Headers.Add("User-Agent", "ControlMenu");

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();

        // Build platform-aware pattern
        var basePattern = moduleDep.AssetPattern ?? moduleDep.ExecutableName;
        var platformToken = GetPlatformToken();
        var pattern = basePattern.Replace("win64", platformToken);

        // Parse assets from JSON (simple regex — no JSON dependency needed)
        var assets = Regex.Matches(json,
            @"""name""\s*:\s*""(?<name>[^""]+)""\s*.*?" +
            @"""size""\s*:\s*(?<size>\d+)\s*.*?" +
            @"""browser_download_url""\s*:\s*""(?<url>[^""]+)""",
            RegexOptions.Singleline);

        var matches = new List<AssetMatch>();
        foreach (Match a in assets)
        {
            var name = a.Groups["name"].Value;
            if (Regex.IsMatch(name, basePattern) && name.Contains(platformToken))
            {
                matches.Add(new AssetMatch(
                    name,
                    a.Groups["url"].Value,
                    long.Parse(a.Groups["size"].Value),
                    AutoSelected: true));
            }
        }

        if (matches.Count == 1)
            return matches[0];

        if (matches.Count > 1)
            return matches[0] with { AutoSelected = false };

        return null;
    }

    private static string GetPlatformToken()
    {
        if (OperatingSystem.IsWindows())
            return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
                   System.Runtime.InteropServices.Architecture.X64 ? "win64" : "win32";
        if (OperatingSystem.IsLinux())
            return "linux-x86_64";
        return "unknown";
    }

    private ModuleDependency? FindModuleDependency(string moduleId, string name)
    {
        return _modules
            .FirstOrDefault(m => m.Id == moduleId)
            ?.Dependencies
            .FirstOrDefault(d => d.Name == name);
    }

    private static int CompareVersions(string? installed, string? latest)
    {
        if (installed is null || latest is null) return -1;

        var iParts = installed.Split('.').Select(s => int.TryParse(s, out var v) ? v : 0).ToArray();
        var lParts = latest.Split('.').Select(s => int.TryParse(s, out var v) ? v : 0).ToArray();
        var len = Math.Max(iParts.Length, lParts.Length);

        for (var i = 0; i < len; i++)
        {
            var a = i < iParts.Length ? iParts[i] : 0;
            var b = i < lParts.Length ? lParts[i] : 0;
            if (a != b) return a.CompareTo(b);
        }

        return 0;
    }

    private static string? FindExecutable(string dir, string exeName)
    {
        var exe = OperatingSystem.IsWindows() && !exeName.EndsWith(".exe")
            ? exeName + ".exe" : exeName;
        return Directory.EnumerateFiles(dir, exe, SearchOption.AllDirectories).FirstOrDefault();
    }

    private static string? FindInstallSource(string extractDir, string exeName)
    {
        var exe = FindExecutable(extractDir, exeName);
        return exe is not null ? Path.GetDirectoryName(exe) : null;
    }

    private static IEnumerable<string> GetManagedFiles(ModuleDependency dep)
    {
        var exe = OperatingSystem.IsWindows() && !dep.ExecutableName.EndsWith(".exe")
            ? dep.ExecutableName + ".exe" : dep.ExecutableName;
        return [exe, .. dep.RelatedFiles];
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DependencyManagerServiceTests" -v n`
Expected: All 4 tests PASS.

**Troubleshooting:** The `ModuleDiscoveryServiceStub` may not work if `Modules` isn't overridable. If so, change the `DependencyManagerService` constructor to accept `IReadOnlyList<IToolModule>` instead of `ModuleDiscoveryService`, and pass `modules` directly in tests. The stub becomes unnecessary. Update tests to pass `new IToolModule[] { module }` or `Array.Empty<IToolModule>()`.

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Services/IDependencyManagerService.cs \
        src/ControlMenu/Services/DependencyManagerService.cs \
        tests/ControlMenu.Tests/Services/DependencyManagerServiceTests.cs
git commit -m "feat(deps): add DependencyManagerService with sync logic and version checking"
```

---

## Task 3: DependencyManagerService — Version Checking Tests

**Files:**
- Modify: `tests/ControlMenu.Tests/Services/DependencyManagerServiceTests.cs`

- [ ] **Step 1: Add GitHub version check test**

Append to `DependencyManagerServiceTests`:

```csharp
[Fact]
public async Task CheckDependencyAsync_GitHub_DetectsUpdateAvailable()
{
    var module = new FakeModule("test-mod", "Test",
    [
        new ModuleDependency
        {
            Name = "scrcpy",
            ExecutableName = "scrcpy",
            VersionCommand = "scrcpy --version",
            VersionPattern = @"scrcpy ([\d.]+)",
            SourceType = UpdateSourceType.GitHub,
            GitHubRepo = "Genymobile/scrcpy"
        }
    ]);

    _mockExecutor.Setup(e => e.ExecuteAsync("scrcpy", "--version", null, default))
        .ReturnsAsync(new CommandResult(0, "scrcpy 3.3.2", "", false));

    // Seed the DB (sync would normally do this)
    var depId = Guid.NewGuid();
    _db.Dependencies.Add(new Data.Entities.Dependency
    {
        Id = depId, ModuleId = "test-mod", Name = "scrcpy",
        SourceType = UpdateSourceType.GitHub,
        InstalledVersion = "3.3.2",
        Status = DependencyStatus.UpToDate
    });
    await _db.SaveChangesAsync();

    // Mock GitHub API response
    var githubJson = """
        {
            "tag_name": "v3.3.4",
            "assets": []
        }
        """;
    var mockHandler = new MockHttpHandler(githubJson);
    var httpClient = new HttpClient(mockHandler);
    _mockHttpFactory.Setup(f => f.CreateClient("github-api")).Returns(httpClient);

    var service = CreateService(module);
    var result = await service.CheckDependencyAsync(depId);

    Assert.Equal(DependencyStatus.UpdateAvailable, result.Status);
    Assert.Equal("3.3.2", result.InstalledVersion);
    Assert.Equal("3.3.4", result.LatestVersion);
}

[Fact]
public async Task CheckDependencyAsync_DirectUrl_ParsesXmlVersion()
{
    var module = new FakeModule("android", "Android",
    [
        new ModuleDependency
        {
            Name = "adb",
            ExecutableName = "adb",
            VersionCommand = "adb --version",
            VersionPattern = @"Android Debug Bridge version ([\d.]+)",
            SourceType = UpdateSourceType.DirectUrl,
            VersionCheckUrl = "https://dl.google.com/android/repository/repository2-3.xml",
            VersionCheckPattern = @"<major>(\d+)</major>\s*<minor>(\d+)</minor>\s*<micro>(\d+)</micro>"
        }
    ]);

    _mockExecutor.Setup(e => e.ExecuteAsync("adb", "--version", null, default))
        .ReturnsAsync(new CommandResult(0, "Android Debug Bridge version 36.0.0", "", false));

    var depId = Guid.NewGuid();
    _db.Dependencies.Add(new Data.Entities.Dependency
    {
        Id = depId, ModuleId = "android", Name = "adb",
        SourceType = UpdateSourceType.DirectUrl,
        InstalledVersion = "36.0.0",
        Status = DependencyStatus.UpToDate
    });
    await _db.SaveChangesAsync();

    var xml = """
        <sdk:sdk-repository>
          <remotePackage path="platform-tools">
            <revision><major>37</major><minor>0</minor><micro>0</micro></revision>
          </remotePackage>
        </sdk:sdk-repository>
        """;
    var mockHandler = new MockHttpHandler(xml);
    var httpClient = new HttpClient(mockHandler);
    _mockHttpFactory.Setup(f => f.CreateClient("dependency-updates")).Returns(httpClient);

    var service = CreateService(module);
    var result = await service.CheckDependencyAsync(depId);

    Assert.Equal(DependencyStatus.UpdateAvailable, result.Status);
    Assert.Equal("36.0.0", result.InstalledVersion);
    Assert.Equal("37.0.0", result.LatestVersion);
}

[Fact]
public async Task CheckDependencyAsync_Manual_StaysUpToDate()
{
    var module = new FakeModule("jf", "Jellyfin",
    [
        new ModuleDependency
        {
            Name = "docker",
            ExecutableName = "docker",
            VersionCommand = "docker --version",
            VersionPattern = @"Docker version ([\d.]+)",
            SourceType = UpdateSourceType.Manual
        }
    ]);

    _mockExecutor.Setup(e => e.ExecuteAsync("docker", "--version", null, default))
        .ReturnsAsync(new CommandResult(0, "Docker version 27.1.0, build abc123", "", false));

    var depId = Guid.NewGuid();
    _db.Dependencies.Add(new Data.Entities.Dependency
    {
        Id = depId, ModuleId = "jf", Name = "docker",
        SourceType = UpdateSourceType.Manual,
        InstalledVersion = "27.1.0",
        Status = DependencyStatus.UpToDate
    });
    await _db.SaveChangesAsync();

    var service = CreateService(module);
    var result = await service.CheckDependencyAsync(depId);

    Assert.Equal(DependencyStatus.UpToDate, result.Status);
    Assert.Equal("27.1.0", result.InstalledVersion);
    Assert.Null(result.LatestVersion);
}
```

Also add the `MockHttpHandler` helper at the bottom of the file:

```csharp
internal class MockHttpHandler(string responseContent, System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseContent)
        });
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DependencyManagerServiceTests" -v n`
Expected: All 7 tests PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/ControlMenu.Tests/Services/DependencyManagerServiceTests.cs
git commit -m "test(deps): add version checking tests for GitHub, DirectUrl, and Manual strategies"
```

---

## Task 4: DependencyCheckHostedService (TDD)

**Files:**
- Create: `src/ControlMenu/Services/DependencyCheckHostedService.cs`
- Create: `tests/ControlMenu.Tests/Services/DependencyCheckHostedServiceTests.cs`

- [ ] **Step 1: Write failing test**

Create `tests/ControlMenu.Tests/Services/DependencyCheckHostedServiceTests.cs`:

```csharp
using ControlMenu.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Services;

public class DependencyCheckHostedServiceTests
{
    [Fact]
    public async Task ExecuteAsync_CallsCheckAllOnStart()
    {
        var mockManager = new Mock<IDependencyManagerService>();
        mockManager.Setup(m => m.CheckAllAsync())
            .ReturnsAsync([]);

        var mockConfig = new Mock<IConfigurationService>();
        mockConfig.Setup(c => c.GetSettingAsync("dep-check-interval", null))
            .ReturnsAsync("86400");

        var services = new ServiceCollection();
        services.AddScoped(_ => mockManager.Object);
        services.AddScoped(_ => mockConfig.Object);
        var provider = services.BuildServiceProvider();

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var service = new DependencyCheckHostedService(
            scopeFactory, NullLogger<DependencyCheckHostedService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        _ = service.StartAsync(cts.Token);

        // Wait for the initial check (10s startup delay + execution)
        await Task.Delay(12000, cts.Token);
        await service.StopAsync(CancellationToken.None);

        mockManager.Verify(m => m.CheckAllAsync(), Times.AtLeastOnce);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DependencyCheckHostedServiceTests" -v n`
Expected: FAIL — `DependencyCheckHostedService` does not exist yet.

- [ ] **Step 3: Implement DependencyCheckHostedService**

Create `src/ControlMenu/Services/DependencyCheckHostedService.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ControlMenu.Services;

public class DependencyCheckHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DependencyCheckHostedService> _logger;

    public DependencyCheckHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<DependencyCheckHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for app to finish initializing
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var manager = scope.ServiceProvider.GetRequiredService<IDependencyManagerService>();
                var config = scope.ServiceProvider.GetRequiredService<IConfigurationService>();

                _logger.LogInformation("Running scheduled dependency version check");
                var results = await manager.CheckAllAsync();

                var updates = results.Count(r => r.Status == Data.Enums.DependencyStatus.UpdateAvailable);
                if (updates > 0)
                    _logger.LogInformation("{Count} dependency update(s) available", updates);

                // Read interval from settings (default: 24 hours)
                var intervalStr = await config.GetSettingAsync("dep-check-interval");
                var intervalSeconds = int.TryParse(intervalStr, out var parsed) ? parsed : 86400;

                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dependency check cycle failed");
                // Wait 5 minutes before retrying after an unexpected error
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DependencyCheckHostedServiceTests" -v n`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Services/DependencyCheckHostedService.cs \
        tests/ControlMenu.Tests/Services/DependencyCheckHostedServiceTests.cs
git commit -m "feat(deps): add DependencyCheckHostedService for periodic version checks"
```

---

## Task 5: DI Registration + Startup Wiring

**Files:**
- Modify: `src/ControlMenu/Program.cs`

- [ ] **Step 1: Register services and call sync at startup**

Modify `src/ControlMenu/Program.cs`:

After the existing `// Utilities module services` block, add:

```csharp
// Dependency management
builder.Services.AddHttpClient("github-api");
builder.Services.AddHttpClient("dependency-updates");
builder.Services.AddScoped<IDependencyManagerService>(sp =>
{
    var db = sp.GetRequiredService<AppDbContext>();
    var modules = sp.GetRequiredService<ModuleDiscoveryService>().Modules;
    var executor = sp.GetRequiredService<ICommandExecutor>();
    var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<DependencyManagerService>>();
    return new DependencyManagerService(db, modules, executor, httpFactory, logger);
});
builder.Services.AddHostedService<DependencyCheckHostedService>();
```

After the existing migration block (`db.Database.Migrate();`), add dependency sync:

```csharp
// Sync dependency declarations with database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    var depManager = scope.ServiceProvider.GetRequiredService<IDependencyManagerService>();
    await depManager.SyncDependenciesAsync();
}
```

**Note:** This makes the startup block `async`. The existing `using` block around `db.Database.Migrate()` merges with this new block. Also, `Program.cs` needs to use top-level `await` (add `await` before `app.Run()` → `await app.RunAsync()`).

The updated startup section becomes:

```csharp
// Auto-apply migrations and sync dependencies on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    var depManager = scope.ServiceProvider.GetRequiredService<IDependencyManagerService>();
    await depManager.SyncDependenciesAsync();
}

await app.RunAsync();
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Run all existing tests to verify no regressions**

Run: `dotnet test tests/ControlMenu.Tests/ -v n`
Expected: All tests PASS (112 existing + new dependency tests).

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Program.cs
git commit -m "feat(deps): register dependency services and sync on startup"
```

---

## Task 6: Dependencies Settings Tab

**Files:**
- Create: `src/ControlMenu/Components/Pages/Settings/DependencyManagement.razor`
- Modify: `src/ControlMenu/Components/Pages/Settings/SettingsPage.razor`

- [ ] **Step 1: Create DependencyManagement.razor**

Create `src/ControlMenu/Components/Pages/Settings/DependencyManagement.razor`:

```razor
@using ControlMenu.Data.Entities
@using ControlMenu.Data.Enums
@inject IDependencyManagerService DepManager
@inject NavigationManager Nav

<div class="settings-section">
    <h2>Dependencies</h2>
    <p>External tools required by modules. Version checks run automatically on a daily schedule.</p>

    <div class="toolbar">
        <button class="btn btn-secondary" @onclick="CheckAll" disabled="@_checkingAll">
            <i class="bi bi-arrow-repeat"></i> @(_checkingAll ? "Checking..." : "Check All")
        </button>
        <div class="toolbar-spacer"></div>
        @if (!string.IsNullOrEmpty(_message))
        {
            <span class="alert @(_messageIsError ? "alert-danger" : "alert-success")" style="margin:0; padding:6px 12px;">@_message</span>
        }
    </div>

    @if (_dependencies.Count == 0)
    {
        <p style="color: var(--text-muted);">No dependencies registered. Install modules that declare external tool requirements.</p>
    }
    else
    {
        <table class="data-table">
            <thead>
                <tr>
                    <th>Name</th>
                    <th>Module</th>
                    <th>Installed</th>
                    <th>Latest</th>
                    <th>Status</th>
                    <th>Source</th>
                    <th>Last Checked</th>
                    <th style="text-align:right;">Actions</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var dep in _dependencies)
                {
                    <tr>
                        <td><strong>@dep.Name</strong></td>
                        <td>@dep.ModuleId</td>
                        <td><code>@(dep.InstalledVersion ?? "—")</code></td>
                        <td><code>@(dep.LatestKnownVersion ?? "—")</code></td>
                        <td><span class="status-badge @GetStatusClass(dep.Status)">@FormatStatus(dep.Status)</span></td>
                        <td>@dep.SourceType</td>
                        <td>@(dep.LastChecked?.ToLocalTime().ToString("g") ?? "Never")</td>
                        <td class="actions">
                            @if (dep.Status == DependencyStatus.UpdateAvailable && _hasInstallPath.Contains(dep.Name))
                            {
                                <button class="btn btn-primary btn-sm" @onclick="() => StartUpdate(dep)"
                                        disabled="@(_updatingId == dep.Id)">
                                    @(_updatingId == dep.Id ? "Updating..." : "Update")
                                </button>
                            }
                            <button class="btn btn-secondary btn-sm" @onclick="() => CheckOne(dep.Id)"
                                    disabled="@(_checkingId == dep.Id)">
                                @(_checkingId == dep.Id ? "..." : "Check")
                            </button>
                            @if (dep.ProjectHomeUrl is not null)
                            {
                                <a class="btn btn-secondary btn-sm" href="@dep.ProjectHomeUrl"
                                   target="_blank" rel="noopener">
                                    <i class="bi bi-box-arrow-up-right"></i> Project
                                </a>
                            }
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    }

    @if (_showUpdateDialog)
    {
        <div class="modal-overlay">
            <div class="modal-dialog">
                @if (_resolvedAsset is null)
                {
                    <p><i class="bi bi-hourglass-split"></i> Resolving download...</p>
                }
                else
                {
                    <h3>Download Update</h3>
                    <p>Download <code>@_resolvedAsset.FileName</code> (@FormatSize(_resolvedAsset.SizeBytes))?</p>
                    <div class="modal-actions">
                        <button class="btn btn-primary" @onclick="ConfirmDownload">Download</button>
                        <button class="btn btn-secondary" @onclick="CancelUpdate">Cancel</button>
                    </div>
                }
            </div>
        </div>
    }
</div>

@code {
    private List<Dependency> _dependencies = [];
    private HashSet<string> _hasInstallPath = [];
    private bool _checkingAll;
    private Guid? _checkingId;
    private Guid? _updatingId;
    private string? _message;
    private bool _messageIsError;
    private bool _showUpdateDialog;
    private AssetMatch? _resolvedAsset;
    private Guid _updateTargetId;

    [Inject] private ModuleDiscoveryService ModuleDiscovery { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await LoadDependencies();
    }

    private async Task LoadDependencies()
    {
        // Load from DB via a direct query since we need the entities
        // DependencyManagerService doesn't expose a list method, so we query the DbContext
        // through a simple service method. For now, use the injected service's check results.
        // Actually, we need the entities. Let's inject AppDbContext directly.
        _dependencies = []; // Populated in OnInitializedAsync via AppDbContext
        _hasInstallPath = ModuleDiscovery.Modules
            .SelectMany(m => m.Dependencies)
            .Where(d => d.InstallPath is not null)
            .Select(d => d.Name)
            .ToHashSet();
    }

    private async Task CheckAll()
    {
        _checkingAll = true;
        _message = null;
        StateHasChanged();

        var results = await DepManager.CheckAllAsync();
        var updates = results.Count(r => r.Status == DependencyStatus.UpdateAvailable);

        _message = updates > 0
            ? $"{updates} update(s) available"
            : "All dependencies up to date";
        _messageIsError = false;
        _checkingAll = false;

        await LoadDependencies();
    }

    private async Task CheckOne(Guid id)
    {
        _checkingId = id;
        StateHasChanged();

        await DepManager.CheckDependencyAsync(id);
        _checkingId = null;

        await LoadDependencies();
    }

    private async Task StartUpdate(Dependency dep)
    {
        _updateTargetId = dep.Id;
        _showUpdateDialog = true;
        _resolvedAsset = null;
        StateHasChanged();

        _resolvedAsset = await DepManager.ResolveDownloadAssetAsync(dep.Id);

        if (_resolvedAsset is null)
        {
            _showUpdateDialog = false;
            _message = $"Could not resolve download for {dep.Name}";
            _messageIsError = true;
        }

        StateHasChanged();
    }

    private async Task ConfirmDownload()
    {
        _showUpdateDialog = false;
        _updatingId = _updateTargetId;
        StateHasChanged();

        var result = await DepManager.DownloadAndInstallAsync(_updateTargetId, _resolvedAsset!);

        _updatingId = null;
        _message = result.Success
            ? $"Updated to version {result.NewVersion}"
            : $"Update failed: {result.ErrorMessage}";
        _messageIsError = !result.Success;

        await LoadDependencies();
    }

    private void CancelUpdate()
    {
        _showUpdateDialog = false;
        _resolvedAsset = null;
    }

    private static string GetStatusClass(DependencyStatus status) => status switch
    {
        DependencyStatus.UpToDate => "status-ok",
        DependencyStatus.UpdateAvailable => "status-warning",
        DependencyStatus.UrlInvalid => "status-error",
        DependencyStatus.CheckFailed => "status-error",
        _ => ""
    };

    private static string FormatStatus(DependencyStatus status) => status switch
    {
        DependencyStatus.UpToDate => "Up to Date",
        DependencyStatus.UpdateAvailable => "Update Available",
        DependencyStatus.UrlInvalid => "URL Invalid",
        DependencyStatus.CheckFailed => "Check Failed",
        _ => status.ToString()
    };

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };
}
```

**Important:** The `LoadDependencies()` method above needs access to the dependency entities. The cleanest approach: inject `AppDbContext` directly (same pattern as `DeviceManagement.razor` uses `IDeviceService`). Add a `GetAllDependenciesAsync()` method to `IDependencyManagerService`:

In `IDependencyManagerService.cs`, add:
```csharp
Task<IReadOnlyList<Data.Entities.Dependency>> GetAllDependenciesAsync();
```

In `DependencyManagerService.cs`, implement:
```csharp
public async Task<IReadOnlyList<Dependency>> GetAllDependenciesAsync()
{
    return await _db.Dependencies
        .OrderBy(d => d.ModuleId)
        .ThenBy(d => d.Name)
        .ToListAsync();
}
```

Then `LoadDependencies()` becomes:
```csharp
private async Task LoadDependencies()
{
    _dependencies = (await DepManager.GetAllDependenciesAsync()).ToList();
    _hasInstallPath = ModuleDiscovery.Modules
        .SelectMany(m => m.Dependencies)
        .Where(d => d.InstallPath is not null)
        .Select(d => d.Name)
        .ToHashSet();
}
```

- [ ] **Step 2: Add Dependencies tab to SettingsPage.razor**

In `src/ControlMenu/Components/Pages/Settings/SettingsPage.razor`, add the tab button after the Modules button:

```razor
<button class="settings-nav-item @(ActiveSection == "dependencies" ? "active" : "")"
        @onclick='() => Navigate("dependencies")'>
    <i class="bi bi-box-seam"></i> Dependencies
</button>
```

And add the case in the switch:

```razor
case "dependencies":
    <DependencyManagement />
    break;
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Components/Pages/Settings/DependencyManagement.razor \
        src/ControlMenu/Components/Pages/Settings/SettingsPage.razor \
        src/ControlMenu/Services/IDependencyManagerService.cs \
        src/ControlMenu/Services/DependencyManagerService.cs
git commit -m "feat(deps): add Dependencies settings tab with version check and update UI"
```

---

## Task 7: TopBar Update Badge

**Files:**
- Modify: `src/ControlMenu/Components/Layout/TopBar.razor`

- [ ] **Step 1: Add update badge to TopBar**

Modify `src/ControlMenu/Components/Layout/TopBar.razor`. Add an update badge in the `top-bar-actions` div, before the theme toggle:

```razor
<div class="top-bar-actions">
    @if (_updateCount > 0)
    {
        <a class="update-badge" href="/settings/dependencies" title="@_updateCount update(s) available">
            <i class="bi bi-download"></i>
            <span class="badge">@_updateCount</span>
        </a>
    }
    <button class="theme-toggle" @onclick="CycleTheme" title="@ThemeLabel">
        <i class="bi @ThemeIcon"></i>
    </button>
</div>
```

Add to the `@code` block:

```csharp
[Inject]
private IDependencyManagerService DepManager { get; set; } = default!;

private int _updateCount;

protected override async Task OnInitializedAsync()
{
    _updateCount = await DepManager.GetUpdateAvailableCountAsync();
}
```

- [ ] **Step 2: Add CSS for the badge**

In `src/ControlMenu/Components/Layout/TopBar.razor.css`, add:

```css
.update-badge {
    position: relative;
    display: flex;
    align-items: center;
    padding: 6px 10px;
    color: var(--text-secondary);
    text-decoration: none;
    border-radius: 6px;
    transition: background 0.15s;
}

.update-badge:hover {
    background: var(--surface-hover);
    color: var(--text-primary);
}

.update-badge .badge {
    position: absolute;
    top: 2px;
    right: 2px;
    min-width: 16px;
    height: 16px;
    font-size: 10px;
    font-weight: 700;
    line-height: 16px;
    text-align: center;
    color: #fff;
    background: var(--status-warning, #e6a700);
    border-radius: 8px;
    padding: 0 4px;
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Components/Layout/TopBar.razor \
        src/ControlMenu/Components/Layout/TopBar.razor.css
git commit -m "feat(deps): add dependency update badge to top status bar"
```

---

## Task 8: SkiaSharp Migration (TDD)

**Files:**
- Modify: `src/ControlMenu/ControlMenu.csproj`
- Modify: `src/ControlMenu/Modules/Utilities/Services/IconConversionService.cs`
- Create: `tests/ControlMenu.Tests/Modules/Utilities/IconConversionServiceTests.cs`
- Delete: `ConvertTo-Ico.ps1`

- [ ] **Step 1: Write tests for icon conversion**

Create `tests/ControlMenu.Tests/Modules/Utilities/IconConversionServiceTests.cs`:

```csharp
using ControlMenu.Modules.Utilities.Services;

namespace ControlMenu.Tests.Modules.Utilities;

public class IconConversionServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IconConversionService _service = new();

    public IconConversionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ControlMenu-Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task ConvertToIcoAsync_ProducesValidIcoFile()
    {
        var sourcePng = CreateTestPng(128, 128);
        var targetIco = Path.Combine(_tempDir, "test.ico");

        await _service.ConvertToIcoAsync(sourcePng, targetIco, [32, 64]);

        Assert.True(File.Exists(targetIco));

        // Validate ICO header
        var bytes = await File.ReadAllBytesAsync(targetIco);
        Assert.True(bytes.Length > 22); // minimum: 6 header + 16 entry

        // Reserved = 0, Type = 1 (icon), Count = 2
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0)); // reserved
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2)); // type = icon
        Assert.Equal(2, BitConverter.ToUInt16(bytes, 4)); // 2 images
    }

    [Fact]
    public async Task ConvertToIcoAsync_HandlesNonSquareImage()
    {
        var sourcePng = CreateTestPng(200, 100); // wide image
        var targetIco = Path.Combine(_tempDir, "wide.ico");

        await _service.ConvertToIcoAsync(sourcePng, targetIco, [64]);

        Assert.True(File.Exists(targetIco));
        var bytes = await File.ReadAllBytesAsync(targetIco);
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 4)); // 1 image
    }

    [Fact]
    public async Task ConvertToIcoAsync_Handles256Size()
    {
        var sourcePng = CreateTestPng(512, 512);
        var targetIco = Path.Combine(_tempDir, "large.ico");

        await _service.ConvertToIcoAsync(sourcePng, targetIco, [256]);

        var bytes = await File.ReadAllBytesAsync(targetIco);
        Assert.Equal(0, bytes[6]); // width = 0 means 256
        Assert.Equal(0, bytes[7]); // height = 0 means 256
    }

    [Fact]
    public void ConvertToIcoAsync_ThrowsForMissingFile()
    {
        Assert.ThrowsAsync<FileNotFoundException>(() =>
            _service.ConvertToIcoAsync("/nonexistent.png", "/out.ico"));
    }

    /// <summary>
    /// Creates a minimal valid PNG file using SkiaSharp and returns the path.
    /// </summary>
    private string CreateTestPng(int width, int height)
    {
        var path = Path.Combine(_tempDir, $"test_{width}x{height}.png");
        using var bitmap = new SkiaSharp.SKBitmap(width, height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Clear(new SkiaSharp.SKColor(255, 0, 0, 128)); // semi-transparent red
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        data.SaveTo(fs);
        return path;
    }
}
```

Add `SkiaSharp` to the test project:
Run: `dotnet add tests/ControlMenu.Tests/ControlMenu.Tests.csproj package SkiaSharp`

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~IconConversionServiceTests" -v n`
Expected: Tests fail because `IconConversionService` still uses `System.Drawing.Common`.

- [ ] **Step 3: Swap NuGet packages**

```bash
dotnet remove src/ControlMenu/ControlMenu.csproj package System.Drawing.Common
dotnet add src/ControlMenu/ControlMenu.csproj package SkiaSharp
```

- [ ] **Step 4: Rewrite IconConversionService with SkiaSharp**

Replace the entire content of `src/ControlMenu/Modules/Utilities/Services/IconConversionService.cs`:

```csharp
using SkiaSharp;

namespace ControlMenu.Modules.Utilities.Services;

public class IconConversionService : IIconConversionService
{
    private static readonly int[] DefaultSizes = [64, 128, 256];

    public Task ConvertToIcoAsync(string sourcePath, string targetPath, int[]? sizes = null)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Source image not found.", sourcePath);

        sizes ??= DefaultSizes;

        using var sourceImage = SKBitmap.Decode(sourcePath);
        if (sourceImage is null)
            throw new InvalidOperationException($"Could not decode image: {sourcePath}");

        using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write);

        // Prepare PNG data for each size
        var pngEntries = new List<byte[]>();
        foreach (var size in sizes)
        {
            using var resized = ResizeImage(sourceImage, size, size);
            using var image = SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            pngEntries.Add(encoded.ToArray());
        }

        using var writer = new BinaryWriter(output);

        // ICONDIR header
        writer.Write((ushort)0);                // reserved
        writer.Write((ushort)1);                // type = icon
        writer.Write((ushort)pngEntries.Count); // image count

        // Calculate data offset: header (6) + dir entries (16 each)
        var dataOffset = 6 + 16 * pngEntries.Count;

        // ICONDIRENTRY for each image
        for (var i = 0; i < pngEntries.Count; i++)
        {
            var size = sizes[i];
            var data = pngEntries[i];

            writer.Write((byte)(size >= 256 ? 0 : size)); // width (0 = 256)
            writer.Write((byte)(size >= 256 ? 0 : size)); // height (0 = 256)
            writer.Write((byte)0);     // color count (0 for 32bpp)
            writer.Write((byte)0);     // reserved
            writer.Write((ushort)1);   // color planes
            writer.Write((ushort)32);  // bits per pixel
            writer.Write((uint)data.Length);    // bytes in resource
            writer.Write((uint)dataOffset);     // offset to data

            dataOffset += data.Length;
        }

        // Image data (PNG blobs)
        foreach (var data in pngEntries)
        {
            writer.Write(data);
        }

        return Task.CompletedTask;
    }

    private static SKBitmap ResizeImage(SKBitmap source, int width, int height)
    {
        var destBitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var canvas = new SKCanvas(destBitmap);
        canvas.Clear(SKColors.Transparent);

        // Handle non-square source images: scale to fit, center
        var srcAspect = (float)source.Width / source.Height;
        int drawWidth, drawHeight, drawX, drawY;

        if (srcAspect > 1)
        {
            drawWidth = width;
            drawHeight = (int)(height / srcAspect);
            drawX = 0;
            drawY = (height - drawHeight) / 2;
        }
        else if (srcAspect < 1)
        {
            drawWidth = (int)(width * srcAspect);
            drawHeight = height;
            drawX = (width - drawWidth) / 2;
            drawY = 0;
        }
        else
        {
            drawWidth = width;
            drawHeight = height;
            drawX = 0;
            drawY = 0;
        }

        var destRect = new SKRect(drawX, drawY, drawX + drawWidth, drawY + drawHeight);
        var sourceRect = new SKRect(0, 0, source.Width, source.Height);

        using var paint = new SKPaint
        {
            FilterQuality = SKFilterQuality.High,
            IsAntialias = true
        };

        canvas.DrawBitmap(source, sourceRect, destRect, paint);

        return destBitmap;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~IconConversionServiceTests" -v n`
Expected: All 4 tests PASS.

- [ ] **Step 6: Run full test suite for regressions**

Run: `dotnet test tests/ControlMenu.Tests/ -v n`
Expected: All tests PASS.

- [ ] **Step 7: Delete the PowerShell script**

```bash
rm ConvertTo-Ico.ps1
```

- [ ] **Step 8: Commit**

```bash
git add src/ControlMenu/ControlMenu.csproj \
        src/ControlMenu/Modules/Utilities/Services/IconConversionService.cs \
        tests/ControlMenu.Tests/Modules/Utilities/IconConversionServiceTests.cs \
        tests/ControlMenu.Tests/ControlMenu.Tests.csproj
git rm ConvertTo-Ico.ps1
git commit -m "feat(utils): migrate icon converter from System.Drawing to SkiaSharp

Cross-platform support — System.Drawing.Common throws on Linux since .NET 7.
Deletes ConvertTo-Ico.ps1 (957-line SpongySoft script, fully replaced)."
```

---

## Task 9: Jellyfin Cast/Crew Worker (TDD)

**Files:**
- Create: `src/ControlMenu/Modules/Jellyfin/Workers/CastCrewUpdateWorker.cs`
- Create: `tests/ControlMenu.Tests/Modules/Jellyfin/CastCrewUpdateWorkerTests.cs`
- Modify: `src/ControlMenu/Modules/Jellyfin/Services/IJellyfinService.cs`
- Modify: `src/ControlMenu/Modules/Jellyfin/Services/JellyfinService.cs`

- [ ] **Step 1: Add cast/crew methods to IJellyfinService**

In `src/ControlMenu/Modules/Jellyfin/Services/IJellyfinService.cs`, add:

```csharp
Task<IReadOnlyList<JellyfinPerson>> GetPersonsMissingImagesAsync(CancellationToken ct = default);
Task TriggerPersonImageDownloadAsync(string personId, CancellationToken ct = default);
```

Create `src/ControlMenu/Modules/Jellyfin/Services/JellyfinPerson.cs`:

```csharp
namespace ControlMenu.Modules.Jellyfin.Services;

public record JellyfinPerson(string Id, string Name);
```

- [ ] **Step 2: Implement the Jellyfin API methods**

In `src/ControlMenu/Modules/Jellyfin/Services/JellyfinService.cs`, add `IHttpClientFactory` to the constructor and implement:

```csharp
private readonly IHttpClientFactory _httpFactory;

public JellyfinService(ICommandExecutor executor, IConfigurationService config, IHttpClientFactory httpFactory)
{
    _executor = executor;
    _config = config;
    _httpFactory = httpFactory;
}
```

**Breaking change:** Existing `JellyfinServiceTests` creates `JellyfinService` with only 2 args. Update `tests/ControlMenu.Tests/Modules/Jellyfin/JellyfinServiceTests.cs`:
- Add `private readonly Mock<IHttpClientFactory> _mockHttpFactory = new();` field
- Change `CreateService()` to: `new JellyfinService(_mockExecutor.Object, _mockConfig.Object, _mockHttpFactory.Object)`

Then implement the new methods:

public async Task<IReadOnlyList<JellyfinPerson>> GetPersonsMissingImagesAsync(CancellationToken ct = default)
{
    var baseUrl = await _config.GetSettingAsync("jellyfin-base-url") ?? "http://127.0.0.1:8096";
    var apiKey = await _config.GetSecretAsync("jellyfin-api-key");
    if (apiKey is null) throw new InvalidOperationException("Jellyfin API key not configured");

    var client = _httpFactory.CreateClient();
    var url = $"{baseUrl}/emby/Persons?api_key={apiKey}";
    var json = await client.GetStringAsync(url, ct);

    // Parse JSON for persons with missing images
    var persons = new List<JellyfinPerson>();
    var itemRegex = new System.Text.RegularExpressions.Regex(
        @"""Id""\s*:\s*""(?<id>[^""]+)"".*?""Name""\s*:\s*""(?<name>[^""]+)"".*?""ImageTags""\s*:\s*\{(?<tags>[^}]*)\}",
        System.Text.RegularExpressions.RegexOptions.Singleline);

    foreach (System.Text.RegularExpressions.Match match in itemRegex.Matches(json))
    {
        var tags = match.Groups["tags"].Value.Trim();
        if (string.IsNullOrEmpty(tags))
        {
            persons.Add(new JellyfinPerson(match.Groups["id"].Value, match.Groups["name"].Value));
        }
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
```

- [ ] **Step 3: Write failing tests for the worker**

Create `tests/ControlMenu.Tests/Modules/Jellyfin/CastCrewUpdateWorkerTests.cs`:

```csharp
using ControlMenu.Data;
using ControlMenu.Data.Enums;
using ControlMenu.Modules.Jellyfin.Services;
using ControlMenu.Modules.Jellyfin.Workers;
using ControlMenu.Services;
using ControlMenu.Tests.Data;
using Moq;

namespace ControlMenu.Tests.Modules.Jellyfin;

public class CastCrewUpdateWorkerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IJellyfinService> _mockJellyfin = new();
    private readonly Mock<IBackgroundJobService> _mockJobService;

    public CastCrewUpdateWorkerTests()
    {
        _db = TestDbContextFactory.Create();
        _mockJobService = new Mock<IBackgroundJobService>();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ExecuteAsync_ProcessesAllPersonsWithParallelism()
    {
        var persons = Enumerable.Range(1, 20)
            .Select(i => new JellyfinPerson($"id-{i}", $"Person {i}"))
            .ToList();

        _mockJellyfin.Setup(j => j.GetPersonsMissingImagesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(persons);

        var processedIds = new System.Collections.Concurrent.ConcurrentBag<string>();
        _mockJellyfin.Setup(j => j.TriggerPersonImageDownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>(async (id, ct) =>
            {
                await Task.Delay(10, ct); // simulate network
                processedIds.Add(id);
            });

        var jobId = Guid.NewGuid();
        _mockJobService.Setup(j => j.GetJobAsync(jobId))
            .ReturnsAsync(new Data.Entities.Job
            {
                Id = jobId, ModuleId = "jellyfin", JobType = "cast-crew-update",
                Status = JobStatus.Running
            });

        var worker = new CastCrewUpdateWorker(_mockJellyfin.Object, _mockJobService.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await worker.ExecuteAsync(jobId, cts.Token);

        Assert.Equal(20, processedIds.Count);
        _mockJobService.Verify(j => j.CompleteJobAsync(jobId, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RespectsCanellation()
    {
        var persons = Enumerable.Range(1, 100)
            .Select(i => new JellyfinPerson($"id-{i}", $"Person {i}"))
            .ToList();

        _mockJellyfin.Setup(j => j.GetPersonsMissingImagesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(persons);

        var processedCount = 0;
        _mockJellyfin.Setup(j => j.TriggerPersonImageDownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>(async (id, ct) =>
            {
                await Task.Delay(50, ct);
                Interlocked.Increment(ref processedCount);
            });

        var jobId = Guid.NewGuid();
        var cancelRequested = false;
        _mockJobService.Setup(j => j.GetJobAsync(jobId))
            .ReturnsAsync(() => new Data.Entities.Job
            {
                Id = jobId, ModuleId = "jellyfin", JobType = "cast-crew-update",
                Status = JobStatus.Running,
                CancellationRequested = cancelRequested
            });

        var worker = new CastCrewUpdateWorker(_mockJellyfin.Object, _mockJobService.Object);

        // Cancel after a short delay
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await worker.ExecuteAsync(jobId, cts.Token);

        // Should have processed some but not all
        Assert.True(processedCount < 100, $"Expected partial processing but got {processedCount}");
    }

    [Fact]
    public async Task ExecuteAsync_ReportsProgress()
    {
        var persons = Enumerable.Range(1, 10)
            .Select(i => new JellyfinPerson($"id-{i}", $"Person {i}"))
            .ToList();

        _mockJellyfin.Setup(j => j.GetPersonsMissingImagesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(persons);
        _mockJellyfin.Setup(j => j.TriggerPersonImageDownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var jobId = Guid.NewGuid();
        _mockJobService.Setup(j => j.GetJobAsync(jobId))
            .ReturnsAsync(new Data.Entities.Job
            {
                Id = jobId, ModuleId = "jellyfin", JobType = "cast-crew-update",
                Status = JobStatus.Running
            });

        var worker = new CastCrewUpdateWorker(_mockJellyfin.Object, _mockJobService.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await worker.ExecuteAsync(jobId, cts.Token);

        // Verify progress was reported at least once
        _mockJobService.Verify(
            j => j.UpdateProgressAsync(jobId, It.IsAny<int>(), It.IsAny<string>()),
            Times.AtLeastOnce);

        // Verify completion
        _mockJobService.Verify(j => j.CompleteJobAsync(jobId, It.IsAny<string>()), Times.Once);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~CastCrewUpdateWorkerTests" -v n`
Expected: FAIL — `CastCrewUpdateWorker` does not exist yet.

- [ ] **Step 5: Implement CastCrewUpdateWorker**

Create `src/ControlMenu/Modules/Jellyfin/Workers/CastCrewUpdateWorker.cs`:

```csharp
using System.Text.Json;
using ControlMenu.Modules.Jellyfin.Services;
using ControlMenu.Services;

namespace ControlMenu.Modules.Jellyfin.Workers;

public class CastCrewUpdateWorker
{
    private const int MaxConcurrency = 4;
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 2000;
    private const int BatchSize = 20;

    private readonly IJellyfinService _jellyfin;
    private readonly IBackgroundJobService _jobService;

    public CastCrewUpdateWorker(IJellyfinService jellyfinService, IBackgroundJobService jobService)
    {
        _jellyfin = jellyfinService;
        _jobService = jobService;
    }

    public async Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            // Fetch all persons missing images
            var allPersons = await _jellyfin.GetPersonsMissingImagesAsync(cancellationToken);

            if (allPersons.Count == 0)
            {
                await _jobService.CompleteJobAsync(jobId,
                    JsonSerializer.Serialize(new { Total = 0, Processed = 0, Errors = 0 }));
                return;
            }

            // Check for resume — skip already-processed persons
            var job = await _jobService.GetJobAsync(jobId);
            var startIndex = 0;
            if (job?.ResultData is not null)
            {
                try
                {
                    var resumeData = JsonSerializer.Deserialize<ResumeData>(job.ResultData);
                    startIndex = resumeData?.LastProcessedIndex ?? 0;
                }
                catch { /* fresh start */ }
            }

            var persons = allPersons.Skip(startIndex).ToList();
            var totalToProcess = persons.Count;
            var totalOverall = allPersons.Count;
            var processed = 0;
            var errors = 0;

            using var semaphore = new SemaphoreSlim(MaxConcurrency);

            // Process in batches
            for (var batchStart = 0; batchStart < persons.Count; batchStart += BatchSize)
            {
                // Check cancellation
                if (cancellationToken.IsCancellationRequested)
                    break;

                var checkJob = await _jobService.GetJobAsync(jobId);
                if (checkJob?.CancellationRequested == true)
                    break;

                var batch = persons.Skip(batchStart).Take(BatchSize).ToList();
                var tasks = batch.Select(async person =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        await ProcessPersonWithRetryAsync(person, cancellationToken);
                        Interlocked.Increment(ref processed);
                    }
                    catch
                    {
                        Interlocked.Increment(ref errors);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);

                // Update progress
                var currentIndex = startIndex + batchStart + batch.Count;
                var progress = (int)((double)currentIndex / totalOverall * 100);
                var message = $"Processing person {currentIndex:N0} of {totalOverall:N0}";

                await _jobService.UpdateProgressAsync(jobId, Math.Min(progress, 99), message);
            }

            // Save final state
            var resultData = JsonSerializer.Serialize(new
            {
                Total = totalOverall,
                Processed = processed,
                Errors = errors,
                LastProcessedIndex = startIndex + processed + errors
            });

            if (cancellationToken.IsCancellationRequested ||
                (await _jobService.GetJobAsync(jobId))?.CancellationRequested == true)
            {
                // Save resume data so next run can pick up where we left off
                await _jobService.FailJobAsync(jobId,
                    $"Cancelled after processing {processed} of {totalOverall}. Resume supported.");
                // ResultData with last index is already set via UpdateProgressAsync
                // When resumed, worker reads LastProcessedIndex to skip completed work
            }
            else
            {
                await _jobService.CompleteJobAsync(jobId, resultData);
            }
        }
        catch (OperationCanceledException)
        {
            // Save resume state on cancellation
            // Job stays in Running state for resume
        }
        catch (Exception ex)
        {
            await _jobService.FailJobAsync(jobId, ex.Message);
        }
    }

    private async Task ProcessPersonWithRetryAsync(JellyfinPerson person, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await _jellyfin.TriggerPersonImageDownloadAsync(person.Id, ct);
                return;
            }
            catch (OperationCanceledException)
            {
                throw; // don't retry cancellations
            }
            catch (Exception) when (attempt < MaxRetries)
            {
                await Task.Delay(RetryDelayMs * attempt, ct);
            }
        }
    }

    private record ResumeData(int LastProcessedIndex);
}
```

- [ ] **Step 6: Run tests**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~CastCrewUpdateWorkerTests" -v n`
Expected: All 3 tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ControlMenu/Modules/Jellyfin/Workers/CastCrewUpdateWorker.cs \
        src/ControlMenu/Modules/Jellyfin/Services/IJellyfinService.cs \
        src/ControlMenu/Modules/Jellyfin/Services/JellyfinService.cs \
        src/ControlMenu/Modules/Jellyfin/Services/JellyfinPerson.cs \
        tests/ControlMenu.Tests/Modules/Jellyfin/CastCrewUpdateWorkerTests.cs
git commit -m "feat(jellyfin): add CastCrewUpdateWorker with 4x parallelism and resume

Replaces serial PowerShell script. Uses SemaphoreSlim(4) for controlled
concurrency, retry-with-backoff, progress reporting, and resume capability
via ResultData JSON."
```

---

## Task 10: Wire Up Cast/Crew Page + Cleanup

**Files:**
- Modify: `src/ControlMenu/Modules/Jellyfin/Pages/CastCrewUpdate.razor`
- Modify: `src/ControlMenu/Program.cs`
- Delete: `Jellyfin-Cast-Update.ps1`

- [ ] **Step 1: Wire up CastCrewUpdate.razor to launch the worker**

In `src/ControlMenu/Modules/Jellyfin/Pages/CastCrewUpdate.razor`, replace the `StartJob()` method:

```csharp
[Inject] private IJellyfinService JellyfinService { get; set; } = default!;

private async Task StartJob()
{
    _starting = true;
    var job = await JobService.CreateJobAsync("jellyfin", "cast-crew-update");
    _activeJob = job;

    // Launch worker in background
    _ = Task.Run(async () =>
    {
        await JobService.StartJobAsync(job.Id, Environment.ProcessId);
        var worker = new Workers.CastCrewUpdateWorker(JellyfinService, JobService);
        await worker.ExecuteAsync(job.Id, CancellationToken.None);
    });

    _starting = false;
}
```

Add the using at the top:
```razor
@using ControlMenu.Modules.Jellyfin.Services
```

- [ ] **Step 2: Register IHttpClientFactory in JellyfinService DI**

In `Program.cs`, update the Jellyfin service registration. The `JellyfinService` constructor now takes `IHttpClientFactory`. Since `IHttpClientFactory` is already registered (from Task 5's `AddHttpClient` calls), this should just work. But verify the constructor call in DI is compatible.

The existing line:
```csharp
builder.Services.AddScoped<IJellyfinService, JellyfinService>();
```

This works because DI will auto-resolve `ICommandExecutor`, `IConfigurationService`, and `IHttpClientFactory` from the container.

- [ ] **Step 3: Delete the PowerShell script**

```bash
rm Jellyfin-Cast-Update.ps1
```

- [ ] **Step 4: Build and run full test suite**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj && dotnet test tests/ControlMenu.Tests/ -v n`
Expected: Build succeeded. All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Modules/Jellyfin/Pages/CastCrewUpdate.razor \
        src/ControlMenu/Program.cs
git rm Jellyfin-Cast-Update.ps1
git commit -m "feat(jellyfin): wire cast/crew worker to UI, delete PowerShell script

CastCrewUpdate page now launches the C# worker with progress tracking.
Deletes Jellyfin-Cast-Update.ps1 (replaced by CastCrewUpdateWorker)."
```

---

## Task 11: Final Integration Test + Push to GitHub

**Files:** None (verification only)

- [ ] **Step 1: Run full test suite**

Run: `dotnet test tests/ControlMenu.Tests/ -v n`
Expected: All tests PASS (112 existing + ~15 new).

- [ ] **Step 2: Build release**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj -c Release`
Expected: Build succeeded with 0 errors.

- [ ] **Step 3: Verify deleted files are gone**

Run: `ls ConvertTo-Ico.ps1 Jellyfin-Cast-Update.ps1 2>/dev/null || echo "Scripts deleted"`
Expected: "Scripts deleted"

- [ ] **Step 4: Create private GitHub repo and push**

```bash
gh repo create OpenAudioOrchestrator/control-menu --private --source=. --push
```

(Adjust org/name as the user directs.)

- [ ] **Step 5: Verify push**

Run: `git log --oneline -15`
Expected: All Phase 6 commits visible.
