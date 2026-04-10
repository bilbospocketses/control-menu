# Phase 6 — Dependency Management

Design spec for Phase 6 of Control Menu. Covers dependency version checking, update downloads, the Dependencies settings tab, SkiaSharp migration for cross-platform icon conversion, and Jellyfin cast/crew worker optimization.

First-run wizard is deferred to Phase 7.

## Scope

1. **DependencyManagerService** — core service for version checking, DB sync, and update orchestration
2. **DependencyCheckHostedService** — background timer for periodic version checks
3. **Update download flow** — auto-select with confirmation for deps with `InstallPath`; link-only for system-managed deps
4. **Stale URL handling** — redirect prompts and 404 fallback to `ProjectHomeUrl`
5. **Dependencies settings tab** — new tab on `/settings/dependencies`
6. **SkiaSharp migration** — replace `System.Drawing.Common` in `IconConversionService`, delete `ConvertTo-Ico.ps1`
7. **Jellyfin cast/crew worker** — replace `Jellyfin-Cast-Update.ps1` with a C# worker using controlled parallelism and resume capability

## Dependency Inventory

| Dep | Module | Source Type | Auto-Download | Version Check Method |
|-----|--------|-------------|---------------|----------------------|
| scrcpy | Android Devices | GitHub | Yes — `AssetPattern` regex + platform detection | GitHub Releases API (unauthenticated) |
| adb | Android Devices | DirectUrl | Yes — Google's stable per-platform URLs | Google `repository2-3.xml` XML endpoint |
| docker | Jellyfin | Manual | No — system-managed, link only | `docker --version` local only |
| sqlite3 | Jellyfin | Manual | No — system-managed, link only | `sqlite3 --version` local only |

### GitHub API

Unauthenticated (60 req/hr limit). With ~2 GitHub-sourced deps checked once daily, this is not a concern. PAT support can be added later if needed.

### Platform-Specific Asset Selection

For GitHub deps, `AssetPattern` is combined with `RuntimeInformation` to auto-select the correct release asset:

| Runtime | scrcpy Asset Token |
|---------|-------------------|
| Windows x64 | `win64` |
| Windows x86 | `win32` |
| Linux x64 | `linux-x86_64` |

scrcpy's naming convention (`scrcpy-{platform}-v{version}.{ext}`) is consistent across releases.

For DirectUrl deps (adb), Google provides deterministic per-platform URLs:
- Windows: `https://dl.google.com/android/repository/platform-tools-latest-windows.zip`
- Linux: `https://dl.google.com/android/repository/platform-tools-latest-linux.zip`

Version check via `https://dl.google.com/android/repository/repository2-3.xml` — parse `<revision>` from the `platform-tools` remote package element.

## 1. DependencyManagerService

### Interface

```csharp
public interface IDependencyManagerService
{
    // Startup sync — reconcile module declarations with DB
    Task SyncDependenciesAsync();

    // Version checking
    Task<DependencyCheckResult> CheckDependencyAsync(Guid dependencyId);
    Task<IReadOnlyList<DependencyCheckResult>> CheckAllAsync();

    // Update operations
    Task<AssetMatch> ResolveDownloadAssetAsync(Guid dependencyId);
    Task<UpdateResult> DownloadAndInstallAsync(Guid dependencyId, AssetMatch asset);

    // Query
    Task<int> GetUpdateAvailableCountAsync();
}
```

### Registration

Scoped service (matches existing patterns — `ConfigurationService`, `DeviceService`, `BackgroundJobService` are all scoped). Uses `IHttpClientFactory` for external API calls.

### Sync-on-Startup

Called from `Program.cs` after EF Core migration, before the app starts accepting requests:

1. Collect all `ModuleDependency` declarations from all discovered `IToolModule` instances
2. For each declared dependency, upsert into the `Dependencies` table:
   - Insert if new (module added a dependency)
   - Update static fields (`VersionCommand`, `GitHubRepo`, `AssetPattern`, `ProjectHomeUrl`, `SourceType`) from code if changed
3. Remove orphaned DB rows where the module or dependency name no longer exists in code
4. Run `VersionCommand` via `CommandExecutor` for each dependency to populate/refresh `InstalledVersion`

### Version Checking Strategies

**GitHub (`UpdateSourceType.GitHub`):**
1. `GET https://api.github.com/repos/{GitHubRepo}/releases/latest` with `Accept: application/vnd.github+json`
2. Parse `tag_name` (e.g., `v3.3.4`) — strip leading `v` for comparison
3. Compare to `InstalledVersion` using semantic version comparison
4. Update `LatestKnownVersion`, `Status`, `LastChecked` in DB

**DirectUrl (`UpdateSourceType.DirectUrl`):**
1. For adb: `GET {VersionCheckUrl}` (e.g., `https://dl.google.com/android/repository/repository2-3.xml`)
2. Parse response using `VersionCheckPattern` — for adb, XPath-style extraction of `<revision>` → `<major>.<minor>.<micro>`
3. Compare to `InstalledVersion`
4. Same DB updates

**Note:** `ModuleDependency` needs two new optional fields:
- `VersionCheckUrl` — URL to fetch for remote version checking (distinct from `DownloadUrl`)
- `VersionCheckPattern` — regex or parse hint for extracting the version from the response

**Manual (`UpdateSourceType.Manual`):**
1. Run `VersionCommand` via `CommandExecutor` to refresh `InstalledVersion`
2. No network calls, no `LatestKnownVersion`, `Status` stays `UpToDate`

### Result Types

```csharp
public record DependencyCheckResult(
    Guid DependencyId,
    string Name,
    DependencyStatus Status,
    string? InstalledVersion,
    string? LatestVersion,
    string? ErrorMessage);

public record AssetMatch(
    string FileName,
    string DownloadUrl,
    long SizeBytes,
    bool AutoSelected);

public record UpdateResult(
    bool Success,
    string? NewVersion,
    string? ErrorMessage,
    StaleUrlAction? UrlAction);

public enum StaleUrlAction { Redirected, Invalid }
```

## 2. DependencyCheckHostedService

A `BackgroundService` that runs version checks on a timer.

### Behavior

1. **On startup:** Wait 10 seconds (let the app finish initializing), then run `CheckAllAsync()`
2. **On timer:** Read `dep-check-interval` from the Settings table (default: 86400 seconds / 24 hours). Sleep for that interval, then run `CheckAllAsync()` again.
3. **Failure isolation:** If a check fails for one dependency, log it and continue checking the rest.
4. **No retry logic.** Failed checks set `Status = CheckFailed`; the next scheduled run tries again naturally.

### DI Pattern

Uses `IServiceScopeFactory` to create scoped `IDependencyManagerService` instances per check cycle (standard pattern for `BackgroundService` consuming scoped services).

## 3. Update Download Flow

Only applies to dependencies where `InstallPath` is set. System-managed deps show a "View project page" link to `ProjectHomeUrl` instead of an update button.

### Asset Resolution

**GitHub deps (scrcpy):**
1. Fetch latest release assets from GitHub API
2. Filter by `AssetPattern` from module declaration, further filtered by current `RuntimeInformation` (OS + architecture)
3. If exactly one match → auto-select, show confirmation: "Download `scrcpy-win64-v3.3.4.zip` (6.9 MB)? [Download] [Cancel]"
4. If zero or multiple matches → show filtered list for user to pick

**DirectUrl deps (adb):**
1. URL is deterministic per platform — no asset selection needed
2. Straight to confirmation dialog

### Download & Install Steps

1. Download to temp directory with progress (stream copy with byte counting)
2. Extract archive (ZIP on Windows, tar.gz on Linux)
3. Verify new binary: run `VersionCommand` against extracted binary (e.g., `temp/platform-tools/adb.exe --version`)
4. If verification succeeds:
   - Rename existing files at `InstallPath` to `.bak`
   - Move new files into `InstallPath`
   - Update `InstalledVersion` and `Status = UpToDate` in DB
5. If verification fails: delete temp files, show error, leave current install untouched
6. `.bak` files cleaned up on the next successful update (one-cycle retention)

### Stale URL Handling

- **HTTP 301/302 redirect:** Prompt: "Download URL for [name] has moved to [new URL]. Update saved URL?" If accepted, persist to `Dependencies.DownloadUrl`.
- **HTTP 404/410 or connection failure:** Warning dialog with link to `ProjectHomeUrl`. Set `Status = UrlInvalid`.

## 4. Dependencies Settings Tab

New tab on the existing Settings page at `/settings/dependencies`.

### Table Columns

| Column | Content |
|--------|---------|
| Name | Dependency name (e.g., "scrcpy") |
| Module | Parent module display name |
| Installed | Current installed version |
| Latest | Latest known version (blank for Manual deps) |
| Status | Badge: Up to Date (green), Update Available (amber), URL Invalid (red), Check Failed (red/muted) |
| Source | GitHub / Direct / Manual |
| Actions | Context-dependent buttons |

### Actions Per Dependency

- **[Update]** — Shown when `InstallPath` is set AND `Status = UpdateAvailable`. Opens confirmation dialog.
- **[Check]** — Runs `CheckDependencyAsync()`. Spinner while checking.
- **[Project Page]** — Opens `ProjectHomeUrl` in new tab. Shown for all deps; primary action for system-managed deps.
- **[Check All]** — Button above the table. Runs `CheckAllAsync()`.

### Update Dialog

1. "Resolving download..." spinner
2. Asset confirmation: filename + size, [Download] / [Cancel]
3. Download progress bar (bytes downloaded / total)
4. Success message with new version, or error with details
5. If multiple assets matched: radio-button list to pick from

### Top Status Bar Badge

Badge in the existing top status bar showing count of deps with `Status = UpdateAvailable`. Clicking navigates to `/settings/dependencies`. Count sourced from `GetUpdateAvailableCountAsync()`, refreshed after each check cycle by the hosted service.

## 5. SkiaSharp Migration

Replace `System.Drawing.Common` with SkiaSharp in `IconConversionService` for cross-platform support. `System.Drawing.Common` throws `PlatformNotSupportedException` on Linux since .NET 7.

### NuGet Changes

- Remove: `System.Drawing.Common`
- Add: `SkiaSharp` (MIT license, maintained by Microsoft/Xamarin team)

### Code Changes in `IconConversionService.cs`

**`ConvertToIcoAsync()`:**
- `new Bitmap(sourcePath)` → `SKBitmap.Decode(sourcePath)`
- `resized.Save(ms, ImageFormat.Png)` → `resized.Encode(SKEncodedImageFormat.Png, 100).SaveTo(ms)`
- BinaryWriter ICO block (ICONDIR, ICONDIRENTRY, PNG blobs) — unchanged, already library-agnostic

**`ResizeImage()`:**
- `Bitmap` + `Graphics` → `SKBitmap` + `SKCanvas`
- `InterpolationMode.HighQualityBicubic` → `SKFilterQuality.High` / `SKSamplingOptions` with bicubic
- `PixelFormat.Format32bppArgb` → `SKColorType.Rgba8888` with `SKAlphaType.Premul`
- Same aspect-ratio-preserving letterbox logic

### Unchanged

- `IIconConversionService` interface
- `IconConverter.razor` page
- ICO binary format writing
- Default sizes (64, 128, 256)
- Non-square image handling

### Cleanup

- Delete `ConvertTo-Ico.ps1` from repo root

## 6. Jellyfin Cast/Crew Worker

Replace `Jellyfin-Cast-Update.ps1` with a C# background worker using the existing `BackgroundJobService` infrastructure.

### Background

- Jellyfin's built-in "Refresh People" scheduled task does not download missing images (known bug across issues #8103, #8288, #8447, #9182)
- The documented workaround is `GET /Users/{userId}/Items/{personId}` which forces Jellyfin to fetch the image from TMDb
- Jellyfin has no API rate limits; TMDb rate-limits at ~40 req/10 seconds
- The PowerShell script processes people serially with no parallelism or progress tracking

### Worker Design

1. **Fetch people missing images:** `GET /Persons` from Jellyfin API, filter for empty `ImageTags` (server-side via `HasImage=false` if supported, otherwise client-side)
2. **Paginate** with `StartIndex` + `Limit` parameters for large libraries
3. **Controlled parallelism:** `SemaphoreSlim(4)` — 4 concurrent requests. Each calls `GET /Users/{userId}/Items/{personId}` to trigger image download
4. **Progress reporting:** After each batch, update `Progress` (0-100%) and `ProgressMessage` ("Processing person 1,247 of 8,432") in the Jobs table via `BackgroundJobService`
5. **Cancellation:** Poll `CancellationRequested` flag between batches
6. **Retry with backoff:** On failure (timeout, 5xx), wait 2 seconds and retry up to 3 times. No blanket delays.
7. **Resume capability:** Store last processed index in the job's `ResultData` JSON. On restart after crash/cancel, skip already-processed people.
8. **Completion:** Set `Status = Completed`, store summary in `ResultData` (total processed, images found, errors). Send email notification if SMTP is configured.

### Expected Performance

| Approach | ~10K People |
|----------|-------------|
| PowerShell (serial) | 3-7 days |
| C# with 4x parallelism + resume | 1-2 days |
| C# with 4x parallelism + server-side filtering | 18-36 hours |

### Integration

- Wire up `CastCrewUpdate.razor`'s `StartJob()` to launch the worker (currently a stub)
- Worker runs as a hosted worker process using the existing `BackgroundJobService` job tracking pattern

### Cleanup

- Delete `Jellyfin-Cast-Update.ps1` from repo root

## New Files

| File | Purpose |
|------|---------|
| `Services/IDependencyManagerService.cs` | Interface |
| `Services/DependencyManagerService.cs` | Implementation |
| `Services/DependencyCheckHostedService.cs` | Background timer |
| `Services/StaleUrlAction.cs` | Enum |
| `Services/DependencyCheckResult.cs` | Result record |
| `Services/AssetMatch.cs` | Result record |
| `Services/UpdateResult.cs` | Result record |
| `Components/Pages/Settings/DependencyManagement.razor` | Settings tab |
| `Modules/Jellyfin/Workers/CastCrewUpdateWorker.cs` | Background worker |

## Modified Files

| File | Change |
|------|--------|
| `ControlMenu.csproj` | Remove `System.Drawing.Common`, add `SkiaSharp` |
| `Modules/Utilities/Services/IconConversionService.cs` | SkiaSharp migration |
| `Components/Pages/Settings/SettingsPage.razor` | Add Dependencies tab |
| `Components/Layout/TopStatusBar.razor` | Add update badge |
| `Modules/Jellyfin/Pages/CastCrewUpdate.razor` | Wire up worker launch |
| `Program.cs` | Register new services, call `SyncDependenciesAsync()` at startup |

## Deleted Files

| File | Reason |
|------|--------|
| `ConvertTo-Ico.ps1` | Replaced by SkiaSharp-based `IconConversionService` |
| `Jellyfin-Cast-Update.ps1` | Replaced by `CastCrewUpdateWorker` |

## Testing

- **DependencyManagerService:** Unit tests for sync logic, version comparison, asset resolution
- **DependencyCheckHostedService:** Verify timer behavior, failure isolation
- **Update flow:** Mock `HttpClient` for GitHub/Google API responses, verify download/extract/verify/swap sequence
- **Stale URL handling:** Mock redirect and 404 responses
- **SkiaSharp IconConversionService:** Verify output ICO is byte-compatible with original, test non-square images
- **CastCrewUpdateWorker:** Test parallelism, resume, cancellation, retry logic
- **Dependencies settings tab:** Verify table rendering, badge count, action buttons
