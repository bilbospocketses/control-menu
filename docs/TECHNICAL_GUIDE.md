# Control Menu -- Technical Guide

This document is a comprehensive technical reference for developers working on the Control Menu codebase. It covers architecture, module system, core services, data layer, and deployment. For a high-level feature overview, see the project [README](../README.md).

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Module System](#2-module-system)
3. [Android Devices Module](#3-android-devices-module)
4. [ws-scrcpy-web Integration](#4-ws-scrcpy-web-integration)
5. [Jellyfin Module](#5-jellyfin-module)
6. [Utilities Module](#6-utilities-module)
7. [Cameras Module](#7-cameras-module)
8. [Imaging Tools Module](#8-imaging-tools-module)
9. [Core Services](#9-core-services)
10. [Database Schema](#10-database-schema)
11. [Setup Wizard](#11-setup-wizard)
12. [Settings Architecture](#12-settings-architecture)
13. [Build and Deployment](#13-build-and-deployment)
14. [Testing](#14-testing)
15. [Known Issues and Fixes](#15-known-issues-and-fixes)

---

## 1. Architecture Overview

Control Menu is a .NET 10 Blazor Server web application that manages Android devices (Google TV Streamers, phones) via ADB, a Jellyfin media server via Docker, and assorted system utilities. It replaces a collection of PowerShell scripts with a cross-platform web UI.

### Four-Layer Architecture

```
+-----------------------------------------------------------+
|  Layer 1: Blazor Server UI (Razor Components)             |
|  - Auto-discovered sidebar navigation                     |
|  - Dark/light theme toggle                                |
|  - MainLayout, Sidebar, TopBar                            |
+-----------------------------------------------------------+
|  Layer 2: Module System (IToolModule)                     |
|  - AndroidDevices (sort 1)                                |
|  - AndroidPowerTools (sort 2)                             |
|  - Jellyfin (sort 3)                                      |
|  - Utilities (sort 4)                                     |
|  - Cameras (sort 5)                                       |
|  - Imaging (sort 6)                                       |
|  - Auto-discovered via reflection at startup              |
+-----------------------------------------------------------+
|  Layer 3: Core Services                                   |
|  - CommandExecutor, ConfigurationService, SecretStore      |
|  - DependencyManager, BackgroundJobs, Email                |
|  - WsScrcpy, Go2Rtc, NetworkDiscovery, DeviceService      |
+-----------------------------------------------------------+
|  Layer 4: Persistence (SQLite via EF Core 10)             |
|  - IDbContextFactory pattern for Blazor Server            |
|  - Tables: Devices, Jobs, Dependencies, Settings          |
|  - Auto-migrations on startup                             |
+-----------------------------------------------------------+
```

### Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Blazor Server (not WASM) | Needs direct access to ADB, Docker, and the filesystem |
| `IDbContextFactory` | Prevents stale EF change tracker in long-lived Blazor circuits |
| `IServiceScopeFactory` for background work | Workers outlive the Blazor circuit that started them |
| SQLite | Single-file database, no external server required |
| SkiaSharp for images | Cross-platform replacement for System.Drawing.Common |
| ws-scrcpy-web via iframe | Screen mirroring without native scrcpy binary dependency |
| Self-contained dependencies | 6 auto-managed tools in `dependencies/`; 2 external (Docker, ws-scrcpy-web) |

### Project Layout

```
src/ControlMenu/
  Program.cs                # Host builder, DI registration, startup
  Components/
    Layout/                 # MainLayout, Sidebar, TopBar
    Pages/                  # Home, Settings, Setup Wizard
      Home.razor            # Menu-sections page (hero + per-module cards + Settings card)
      Settings/             # SettingsPage, tabs: General, Devices, Cameras, Jellyfin, Dependencies
      Setup/                # Wizard steps: Welcome, Android Devices, Cameras, Jellyfin, Email, Dependencies, Done
    Shared/                 # ScrcpyMirror, Scanner/DiscoveredPanel, Cameras/DiscoveredCamerasPanel
  Data/
    AppDbContext.cs          # EF Core DbContext
    Entities/                # Device, Job, Dependency, Setting
    Enums/                   # DeviceType, JobStatus, DependencyStatus, UpdateSourceType, StaleUrlAction
  Migrations/                # EF Core migrations
  Modules/
    IToolModule.cs           # Module contract
    ModuleDiscoveryService.cs
    NavEntry.cs, BackgroundJobDefinition.cs, ModuleDependency.cs, ConfigRequirement.cs
    AndroidDevices/          # Module class, Pages/, Services/
    AndroidPowerTools/       # Module class (ws-scrcpy-web home-page iframe host)
    Cameras/                 # Module class, Pages/, Services/ (Go2RtcService)
    Jellyfin/                # Module class, Pages/, Services/, Workers/
    Imaging/                 # Module class, Pages/, Services/ (ImageService, TracingService), Resources/ (magick-policy.xml)
    Utilities/               # Module class, Pages/, Services/ (FileUnblockService)
  Services/                  # Core services (see section 9)
  wwwroot/                   # Static assets, CSS, theme, JS interop
tests/ControlMenu.Tests/
  Data/                      # TestDbContextFactory, AppDbContextTests
  Services/                  # Tests for all core services
  Modules/                   # Tests for all module services
```

---

## 2. Module System

The module system provides a plugin-like architecture where each functional area of the application is encapsulated as a self-contained module. Modules are discovered automatically at startup via reflection -- no explicit registration required.

### IToolModule Interface

```csharp
public interface IToolModule
{
    string Id { get; }                                    // Unique identifier, e.g. "android-devices"
    string DisplayName { get; }                           // Human-readable name for UI
    string Icon { get; }                                  // Bootstrap Icons class, e.g. "bi-phone"
    int SortOrder { get; }                                // Sidebar ordering (lower = higher)
    IEnumerable<ModuleDependency> Dependencies { get; }   // External tool requirements
    IEnumerable<ConfigRequirement> ConfigRequirements { get; }  // Required settings
    IEnumerable<NavEntry> GetNavEntries();                // Sidebar navigation items
    IEnumerable<BackgroundJobDefinition> GetBackgroundJobs();   // Registerable background tasks
}
```

### ModuleDiscoveryService

At startup, `ModuleDiscoveryService` scans all assemblies for types that:
- Are concrete (not abstract, not an interface)
- Implement `IToolModule`
- Have a parameterless constructor

Discovered modules are instantiated via `Activator.CreateInstance`, sorted by `SortOrder` then `DisplayName`, and stored as `IReadOnlyList<IToolModule>`.

```csharp
// Registration in Program.cs
builder.Services.AddSingleton(new ModuleDiscoveryService(
    [Assembly.GetExecutingAssembly()]));
```

### Supporting Records

**NavEntry** -- A sidebar navigation link:
```csharp
public record NavEntry(string Title, string Href, string? Icon = null, int SortOrder = 0);
```

**BackgroundJobDefinition** -- Metadata for a registerable long-running task:
```csharp
public record BackgroundJobDefinition(
    string JobType, string DisplayName, string Description, bool IsLongRunning = false);
```

**ModuleDependency** -- An external tool the module requires:
```csharp
public record ModuleDependency
{
    public required string Name { get; init; }
    public required string ExecutableName { get; init; }
    public required string VersionCommand { get; init; }
    public required string VersionPattern { get; init; }
    public UpdateSourceType SourceType { get; init; }  // GitHub, DirectUrl, or Manual
    public string? GitHubRepo { get; init; }
    public string? DownloadUrl { get; init; }
    public string? DownloadUrlTemplate { get; init; }  // URL with {version} placeholder
    public string? VersionCheckUrl { get; init; }
    public string? VersionCheckPattern { get; init; }
    public string? InstallPath { get; init; }
    public string[] RelatedFiles { get; init; } = [];
    // ...
}
```

**ConfigRequirement** -- A setting the module needs during setup:
```csharp
public record ConfigRequirement(
    string Key, string DisplayName, string Description, bool IsSecret = false, string? DefaultValue = null);
```

### Currently Registered Modules

| Module | Id | SortOrder | Dependencies | Nav Entries |
|--------|----|-----------|--------------|-------------|
| Android Devices | `android-devices` | 1 | adb, ws-scrcpy-web | Device List; Google TV / Phone / Tablet / Watch (each shown when ≥1 device of that type is registered) |
| Android Power Tools | `android-power-tools` | 2 | (none — shares ws-scrcpy-web with Android Devices) | Power Tools |
| Jellyfin | `jellyfin` | 3 | docker, sqlite3 | DB Date Update, Cast & Crew, Media Cards |
| Utilities | `utilities` | 4 | (none) | File Unblocker |
| Cameras | `cameras` | 5 | go2rtc | Dynamic: one entry per configured camera |
| Imaging Tools | `imaging` | 6 | magick, vtracer, potrace | Icon Converter, Format Converter, Image Resize, SVG Rasterize, Magic Wand, Tracing |

### Sidebar Integration

The `Sidebar.razor` component injects `ModuleDiscoveryService` and iterates over discovered modules. Each module becomes a collapsible group in the sidebar, with its `GetNavEntries()` rendered as sub-links. The sidebar is fully data-driven -- adding a new module automatically creates its navigation group. The sidebar header features a branded pill button with the app icon (30x30, `icon-192.png`) linking to the home page, with the collapse chevron pushed right via `justify-content: space-between`.

---

## 3. Android Devices Module

The Android Devices module manages Google TV Streamers and Android phones over ADB (Android Debug Bridge). It provides dashboards for power management, screensaver control, launcher toggling, Projectivy backup restoration, PIN unlock, and screen mirroring. Devices are assumed to already have wireless debugging enabled — the module discovers and connects to them over the network via mDNS (preferred) or ARP.

### AdbService

`AdbService` implements `IAdbService` and delegates all process execution to `ICommandExecutor`. It never calls `Process.Start` directly. Every ADB operation targets a specific device via the `-s {ip}:{port}` argument.

Key methods:

| Method | Purpose |
|--------|---------|
| `ConnectAsync(ip, port)` | `adb connect` and verify "connected" in output |
| `DisconnectAsync(ip, port)` | `adb disconnect` a specific device |
| `GetPowerStateAsync(ip, port)` | Parse `dumpsys power` for wakefulness state |
| `TogglePowerAsync(ip, port)` | Send `KEYCODE_POWER` key event |
| `RebootAsync(ip, port)` | `adb shell reboot` |
| `GetScreensaverAsync(ip, port)` | Read `screensaver_components` secure setting |
| `SetScreensaverAsync(ip, port, name)` | Write screensaver component (SkyFolio or Google Backdrop) |
| `GetScreenTimeoutAsync(ip, port)` | Read `screen_off_timeout` system setting |
| `SetScreenTimeoutAsync(ip, port, ms)` | Write screen timeout in milliseconds |
| `IsLauncherDisabledAsync(ip, port)` | Check if Google TV launcher is in disabled packages |
| `SetLauncherEnabledAsync(ip, port, enabled)` | Enable/disable launcher and setup wraith packages |
| `StartShizukuAsync(ip, port)` | Execute Shizuku start script on device |
| `ListProjectivyBackupsAsync(ip, port)` | List files in Projectivy-Backups directory |
| `RestoreProjectivyBackupAsync(ip, port, file)` | Launch Projectivy import activity with backup file |
| `GetScreenSizeAsync(ip, port)` | Parse `wm size` output for display dimensions |
| `UnlockWithPinAsync(ip, port, pin)` | Sequential key events: power, menu, text input, enter |
| `GetConnectedDevicesAsync()` | Parse `adb devices` output |
| `ScanMdnsAsync()` | Run `adb mdns services`, parse into `MdnsAdbDevice` records (service name + IP + advertised port) |
| `DisconnectAllAsync()` | Disconnect every connected device |

The PIN unlock sequence is notable: it uses plain sequential ADB calls without delays or digit-by-digit input. The entire PIN is sent as a single `input text` command.

### Device Entity

```csharp
public class Device
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DeviceType Type { get; set; }          // GoogleTV or AndroidPhone
    public required string MacAddress { get; set; } // Lowercase dashes: aa-bb-cc-dd-ee-ff
    public string? SerialNumber { get; set; }
    public string? LastKnownIp { get; set; }
    public int AdbPort { get; set; } = 5555;
    public DateTime? LastSeen { get; set; }
    public required string ModuleId { get; set; }
    public string? Metadata { get; set; }
}
```

MAC addresses are normalized to lowercase with dashes on startup (see `Program.cs`). The `NetworkDiscoveryService.NormalizeMac` method handles this: `mac.ToLowerInvariant().Replace(':', '-')`.

### Dependencies

The module declares two dependencies — `adb` (auto-managed in `dependencies/`) and `ws-scrcpy-web` (external, user-configured):

| Name | Source Type | Install Path | Purpose |
|------|-----------|--------------|---------|
| adb | DirectUrl (Google) | `dependencies/platform-tools` | Device management |
| ws-scrcpy-web | Manual | (user-configured) | Browser-based screen mirroring |

### Pages

- **DeviceSelector** (`/android/devices`) -- CRUD for device records, network discovery for IP resolution
- **GoogleTvDashboard** (`/android/googletv`) -- Power toggle, screensaver selector, screen timeout, launcher enable/disable, Projectivy backup list with restore, Shizuku start, screen mirror (passes `DeviceKind="tv"` to `ScrcpyMirror` so the ws-scrcpy-web toolbar defaults to D-pad mode)
- **DeviceDashboard** (Phone `/android/phone` + `/android/pixel`, Tablet `/android/tablet`, Watch `/android/watch`) -- the shared dashboard for the simple single-mirror device pages; the three route pages are thin wrappers that render it with their title/icon/device-type/device-kind. ADB connect, PIN unlock, portrait-mode screen mirror with aspect ratio from ADB screen dimensions. The device kind (`phone`/`tablet`/`watch`) is passed to `ScrcpyMirror` so the ws-scrcpy-web toolbar seeds the right input mode.

### WsScrcpyService

`WsScrcpyService` is registered as both a singleton and an `IHostedService`, but it is **external-only** — it does **not** spawn or supervise the ws-scrcpy-web process. The user runs their own ws-scrcpy-web instance; Control Menu only holds its URL and checks reachability on demand.

Lifecycle:
1. **StartAsync**: resolves the `wsscrcpy-url` setting once so the value is logged, and marks the service ready. No process is launched, and nothing is cached for consumers to read.
2. **`GetBaseUrlAsync(ct)`**: re-reads `wsscrcpy-url` from configuration on **every call** and repairs it — a blank or whitespace-only value becomes the default `http://localhost:8000`, and surrounding whitespace and a trailing slash are stripped (every consumer concatenates onto this, so a stored trailing slash produced `//embed-request`, a different path server-side that Node's URL parser rejects).
   There is deliberately **no public `BaseUrl` property**. One existed, refreshed on each resolve, and five consumers read it instead of calling this method — so changing the URL in Settings moved the Power Tools page while mirroring, subnet detection, device probes and the network scan all silently kept dialling the address resolved at startup, until the process restarted. The property was removed so that shape cannot come back.
3. **`IsRunning`**: indicates only that a URL was resolved at startup — **not** that ws-scrcpy-web is currently reachable.
4. **`CheckEmbedAsync(origin, ct)`**: the readiness gate for any UI that frames ws-scrcpy-web. One `GET` answers both questions at once, returning `EmbedCheck(Reachable, CanEmbed, Reason)`: an unreachable server, a non-2xx status (reported as such, so a `403` from the host allowlist is not mistaken for a renderable frame), or framing headers that refuse this origin. A CSP `frame-ancestors` directive wins where present — it supersedes `X-Frame-Options` — otherwise `SAMEORIGIN`/`DENY` means blocked.
5. **StopAsync**: clears the ready flag.

> Managed mode — CM spawning the Node process, health-monitoring, orphan-killing on port 8000, and a `Restart()` hook — was **removed in v1.0.0**. ws-scrcpy-web is configured under **Settings → General → External Dependencies**, and the legacy `ws_scrcpy_web_path` / `wsscrcpy-mode` settings are dropped on startup by a one-time cleanup service.

---

## 4. ws-scrcpy-web Integration (ScrcpyMirror.razor)

`ScrcpyMirror.razor` is a shared Blazor component used by both the Google TV and Android Phone dashboards. It provides inline or popup screen mirroring via an iframe pointing at the ws-scrcpy-web server.

### Component Parameters

```csharp
[Parameter, EditorRequired]
public string Udid { get; set; }  // ADB device identifier (ip:port)

[Parameter]
public bool Inline { get; set; }  // true = embedded iframe, false = popup button
```

### Stream URL

The iframe source URL follows this pattern:
```
{ws-scrcpy-web URL}/embed.html?device={Udid}
```

The base is resolved with `WsScrcpyService.GetBaseUrlAsync()` in `OnParametersSetAsync` — per render, not once at startup — so a URL changed in Settings takes effect the next time the mirror renders. (It used to read a `BaseUrl` property cached at `StartAsync`; that property is gone, see §3.)

`embed.html` is ws-scrcpy-web's dedicated iframe-friendly wrapper (shipped with the 1.0.0 stream API rewrite, April 2026). It renders the stream plus toolbar only, with a transparent background so iframe consumers can place any background behind the video. The legacy `#!action=stream&udid=...&embed=true` hash-routing URL was removed in the same release.

Additional URL parameters supported by `embed.html` — `host`, `port`, `secure`, `pathname`, `codec`, `encoder`, `bitrate`, `maxFps`, `maxSize`, `audio`, `keyboard` — all optional. See the ws-scrcpy-web TECHNICAL_GUIDE for the complete reference.

### Display Modes

**Google TV (Landscape)**: The mirror panel uses `width: 100%` and lets the iframe fill the available horizontal space.

**Android Phone (Portrait)**: Uses a `position: relative` container with an `absolute`-positioned iframe. The aspect ratio is calculated from ADB screen dimensions (via `GetScreenSizeAsync`) plus toolbar width compensation.

**Android Tablet / Android Watch**: Same dashboards as Android Phone, differing only in label, icon, `DeviceType` filter, and `DeviceKind` attribute passed to `ScrcpyMirror`. The `DeviceTypePresenceWatcher` in each dashboard redirects to `/android/devices` when the last device of its type is deleted.

### Watch dashboard — unverified on real hardware

The Android Watch dashboard (`/android/watch`) ships as a near-clone of the Android Phone dashboard and has not been verified against a physical Wear OS device (no test hardware available at release time). Code parity with Phone means ADB-connect, PIN unlock, and the scrcpy mirror all wire up identically; please report any watch-specific issues so we can iterate.

### Per-circuit reactivity — cross-tab updates via singleton notifier

`IDeviceService` and `IDeviceTypeCache` are scoped (one per Blazor circuit), but device mutations propagate across all open tabs in real-time via a singleton `IDeviceChangeNotifier`. When `DeviceService` completes an add, update, or delete, it calls `_notifier.NotifyChanged()` on the singleton. Every circuit's `DeviceTypeCache` and `DeviceTypePresenceWatcher` subscribe to the notifier's `Changed` event, so sidebar nav entries and dashboard redirect guards update within ~1 second across all open browser tabs. The notifier uses snapshot + per-handler try/catch invoke so a faulting circuit cannot block notifications to others.

### Critical Bug Fix

The phone mirror panel required explicit sizing for iframe click handling to work correctly. Without `position: relative` on the container and `position: absolute` on the iframe, click events would not propagate through to the ws-scrcpy-web stream. This is documented further in [Known Issues and Fixes](#15-known-issues-and-fixes).

### Fallback Behavior

The component has three non-iframe states, not one. `WsScrcpy.IsRunning` false renders the "configure ws-scrcpy-web" warning — it means only that a URL was resolved at startup, never that the server is reachable. A definite framing refusal (`CheckEmbedAsync` returns reachable but not embeddable) renders a second alert linking to the Power Tools page to request permission, because the browser reports a blocked frame as "refused to connect", which is indistinguishable from the server being down. An **unreachable** server deliberately falls through to the iframe and lets it show its own error, rather than hiding mirroring behind a wrong explanation. When `Inline` is false, it renders a "Screen Mirror" button that opens a popup window (`window.open` with specific dimensions and no browser chrome).

### Android Power Tools Module

Peer of Android Devices (sort order 2), added April 2026. Host for ws-scrcpy-web's full home page via iframe at `/android-power-tools`. Gives the user direct access to power-user workflows that aren't replicated in Control Menu's Android Devices UI: one-click shell (xterm modal), file browser (ListFilesModal with sticky header, reserved actions column, bulk selection, drag-and-drop upload, filter), ConfigureScrcpy stream parameters, network scan panel with mDNS + manual-add, and dependency updater.

Strictly additive — Android Devices module remains the primary device-management surface (registered-devices list, PIN unlock, power state, sleep/wake, screensaver, Projectivy backups). The Power Tools module is a thin wrapper: `AndroidPowerToolsPage.razor` resolves the URL with `WsScrcpyService.GetBaseUrlAsync()` on each visit and gates the iframe on `CheckEmbedAsync` (it does not consult `IsRunning` at all), and the module class itself declares no dependencies (they're already declared by AndroidDevices).

`MainLayout.razor`'s page-title switch needs a specific case for `/android-power-tools` that sits **above** the generic `path.StartsWith("android")` fallback — otherwise the breadcrumb shows "Android Devices" for what should be "Android Power Tools" (the prefix-match order is load-bearing).

All four ws-scrcpy-web modals — shell, list files, configure stream, connect — render inside the iframe's own document, so `showModal()`'s top-layer and backdrop are scoped to the iframe viewport. No JS-interop wiring and no library-bundle injection into Blazor: ws-scrcpy-web serves both the bundle and the embedded page, so nothing is fetched across origins.

Framing, however, **is** cross-origin and is the central constraint here. ws-scrcpy-web runs on its own port, so every Control Menu page that frames it is a different origin; ws-scrcpy-web sends `X-Frame-Options: SAMEORIGIN` and therefore refuses by default. The embed-permission handshake (`RequestEmbedPermissionAsync` → a human approves in ws-scrcpy-web → it writes this origin into `frameAncestors` and applies it live) is what makes the frame render, and `CheckEmbedAsync` is what decides before rendering whether it will. A URL naming a **host** rather than `localhost` or an IP address is refused earlier still, by ws-scrcpy-web's `allowedHosts` DNS-rebinding guard, which answers `403` to every request until that name is listed in its `config.json`.

**Theme sync.** The embedded ws-scrcpy-web iframe receives theme updates from Control Menu via cross-origin `postMessage`. The bridge lives in `wwwroot/js/scrcpyThemeBridge.js` and uses ws-scrcpy-web's public theme-bridge API (shipped in v0.1.24-beta.5+). Bidirectional: clicking ws-scrcpy-web's own toggle inside the iframe also updates CM's theme. Protocol details are in `docs/superpowers/specs/2026-04-29-iframe-theme-bridge-design.md`.

---

## 5. Jellyfin Module

The Jellyfin module manages a Jellyfin media server running in Docker. It handles container lifecycle, database operations, automated backups, and two long-running background workers: the cast & crew image update (`CastCrewUpdateWorker`, job type `cast-crew-update`) and the My Media card regeneration (`MediaCardRefreshWorker`, job type `media-card-refresh`). Both run in their own DI scope, re-read cancellation from the database so they survive a circuit disconnect, and report through `OperationLogger` plus an optional completion email.

### JellyfinService

`IJellyfinService` provides:

| Method | Purpose |
|--------|---------|
| `GetContainerIdAsync()` | Find the Jellyfin Docker container ID |
| `StopContainerAsync(id)` | `docker stop` the container |
| `StartContainerAsync(id)` | `docker start` the container |
| `WaitForContainerReadyAsync(id, timeout)` | Poll until container is healthy |
| `BackupDatabaseAsync(logger)` | Copy `jellyfin.db` to timestamped backup |
| `UpdateDateCreatedAsync(logger)` | Update DateCreated fields in Jellyfin DB |
| `CleanupOldBackupsAsync(logger)` | Remove `*.db` backups older than the retention window, and card backups under `media-cards/` beyond the newest three per library — by count, never by age, since the newest card backup is the only route back from a bad regeneration. Runs from the DB Date Update routine and at the end of every Media Cards run |
| `ParseComposeFileAsync()` | Extract container info from docker-compose.yml |
| `GetPersonsMissingImagesAsync()` | Query Jellyfin API for persons without images |
| `TriggerPersonImageDownloadAsync(id, config)` | Refresh a single person's images via API |
| `GetApiConfigAsync()` | Resolve Jellyfin API base URL, API key, and user ID |
| `GetMediaCardTargetsAsync(config)` | The My Media row from `/UserViews` (not `/Library/VirtualFolders`, which omits generated views such as Playlists): each tile with whether it has a card and whether Jellyfin can rebuild it (`MediaCardSupport.Evaluate` — every `CollectionFolder` qualifies; a `UserView` only for `movies` / `tvshows` / `playlists`, so Live TV is refused) |
| `BackupLibraryCardAsync(id, name, config)` | Download the current card to `<backups>/media-cards/<sanitised name>-yyyyMMdd-HHmmss.<ext>`; `null` when there is no card |
| `DeleteLibraryCardAsync(id, config)` | `DELETE /Items/{id}/Images/Primary` — the collage provider only runs when no image exists |
| `RefreshLibraryCardAsync(id, config)` | `POST /Items/{id}/Refresh` with `metadataRefreshMode=ValidationOnly` and `replaceAllImages=false` (see the #124 CHANGELOG entry for why both values are load-bearing) |
| `HasLibraryCardAsync(id, config)` | Whether a primary image exists now — the worker's wait polls this |
| `RestoreLibraryCardAsync(id, backupPath, config)` | Upload a backup via `POST /Items/{id}/Images/Primary` (body is **base64 text**, despite the OpenAPI `format: binary`); copying the file back would leave `BaseItemImageInfos` without a row |
| `FindLatestCardBackupAsync(name)` | Newest backup for a library name, for the page's per-row **Restore** |

### ComposeParser

`ComposeParser` is a static class that extracts three pieces of information from a `docker-compose.yml` file:

```csharp
public record ComposeParseResult(
    string? ContainerName,    // e.g. "jellyfin"
    string? ConfigHostPath,   // Host-side path mapped to /config
    string? DbPath,           // ConfigHostPath/data/jellyfin.db
    string? ErrorMessage);
```

The parser handles:
- Multi-service compose files (finds the service with a `/config` volume mount)
- Windows drive letter colons vs. mount separator colons (skips `C:/` style prefixes)
- Volume option suffixes (`:ro`, `:rw`)

### CastCrewUpdateWorker

The worker processes persons missing images from the Jellyfin API. It is a long-running background task with full lifecycle management.

Configuration constants:
```csharp
MaxConcurrency = 4       // Concurrent API requests
MaxRetries = 3           // Per-person retry limit
RetryDelayMs = 2000      // Base delay, multiplied by attempt number
BatchSize = 20           // Persons per batch
LogProgressEveryNBatches = 5  // Log frequency
```

Execution flow:
1. Resolve Jellyfin API configuration (base URL, API key, user ID)
2. Fetch all persons missing images
3. Check for resume data in `Job.ResultData` (JSON with `LastProcessedIndex`)
4. Process in batches of 20, with 4 concurrent requests per batch
5. Each person retried up to 3 times with exponential backoff (2s, 4s, 6s)
6. Poll `Job.CancellationRequested` between batches
7. On completion, send email notification with summary
8. Save `LastProcessedIndex` to `ResultData` for resume support on cancellation/failure

### OperationLogger

`OperationLogger` writes timestamped log files under `<dataRoot>/logs/jellyfin/` (resolved via `IDataPathResolver.GetLogsDir()`). Each operation creates a new file named `{operation}_{yyyyMMdd_HHmmss}.log`.

Log levels: `START`, `STEP`, `OK`, `FAIL`, `DONE`

The logger respects the `app-timezone` setting for timestamp display. It is constructed via `OperationLogger.Create(operation, utcOffset, IDataPathResolver)` and also provides static helpers:
- `GetRecentLogs(count, paths)` -- Returns the N most recent log entries with status inference (reads last line for DONE/FAIL markers)
- `GetDefaultLogDirectory(paths)` -- Returns `<dataRoot>/logs/jellyfin/`
- `GetDefaultBackupDirectory(paths)` -- Returns `<dataRoot>/jellyfin-backups/` (`GetJellyfinBackupsDir()`); pure — the backup-write path creates the directory, not this getter

### Pages

- **DatabaseUpdate** (`/jellyfin/db-update`) -- DateCreated update with backup, container stop/start, operation logging
- **CastCrewUpdate** (`/jellyfin/cast-crew`) -- Start/cancel cast & crew worker, progress tracking, job history
- **MediaCards** (`/jellyfin/media-cards`) -- Lists the My Media row with a Present / Missing / Hand-set badge per tile; tick the ones to rebuild (nothing ticked by default; a header checkbox selects every regenerable row) and start `MediaCardRefreshWorker`, which per library backs up the card, deletes it, requests the refresh and polls until the new card exists (20-minute cap, since the refresh queues behind a full library walk), rolling the backup back on failure. A per-row **Restore** re-uploads the newest backup. Cancel is honoured on every 3-second poll of the wait, not only between libraries. When a run ends the worker prunes card backups to the newest three per library.

### JellyfinDirectoryResolver

`IJellyfinDirectoryResolver` owns *where* the module's files live, so pages and the service never compute paths themselves:

| Member | Purpose |
|--------|---------|
| `MediaCardsFolder` (const) | `media-cards` — the sub-folder of the backup directory that holds card backups |
| `GetBackupDirectoryAsync()` | `jellyfin-backup-directory` override, else `<dataRoot>/jellyfin-backups/` |
| `GetLogDirectoryAsync()` | `jellyfin-log-directory` override, else `<dataRoot>/logs/jellyfin/` |
| `MigrateFilesAsync(old, new, pattern)` | Best-effort move of matching files; per-file failures do not abort the batch |
| `MigrateBackupsAsync(old, new)` | The whole backup directory: `*.db` at the root plus `media-cards/` beneath it. Moving only `*.db` orphaned the cards where the page's Restore could not see them |
| `GetBackupStats(dir)` | `BackupDirectoryStats(FileCount, TotalBytes)` over the databases and the cards, for the Settings-page stat |

---

## 6. Utilities Module

The Utilities module provides standalone tools that do not depend on external services.

> **Icon conversion moved.** Image-to-ICO conversion lived here as `IconConversionService` (SkiaSharp-based) through v1.1.1. It was migrated to the [Imaging Tools module](#8-imaging-tools-module) (now magick-backed) and the service was removed. The old `/utilities/icon-converter` route is preserved by `IconConverterRedirect.razor`, which `replace`-redirects to `/imaging/icon-converter`. Utilities now contains only the File Unblocker.

### FileUnblockService

Windows-only. Removes Zone.Identifier alternate data streams (the "downloaded from the internet" marker) from all files in a directory tree.

Implementation:
1. Check `OperatingSystem.IsWindows()` -- returns `IsSupported = false` on other platforms
2. Run PowerShell: enumerate files with Zone.Identifier ADS, count them, then `Unblock-File`
3. Return `UnblockResult(Success, FileCount, ErrorMessage)`

The PowerShell command counts blocked files before unblocking because `Unblock-File` has no `-PassThru` option.

### Pages

- **IconConverterRedirect** (`/utilities/icon-converter`) -- Legacy-route shim. `OnInitialized` calls `Nav.NavigateTo("/imaging/icon-converter", replace: true)` so old bookmarks land on the migrated tool.
- **FileUnblocker** (`/utilities/file-unblocker`) -- Directory path input, one-click unblock, result count

---

## 7. Cameras Module

The Cameras module provides CCTV camera viewing for LTS/Hikvision cameras via [go2rtc](https://github.com/AlexxIT/go2rtc). go2rtc converts RTSP streams into browser-playable formats (MP4/WebRTC), eliminating the need for camera-specific web UI proxying or native plugins.

### Architecture

```
Camera (RTSP :554) --> go2rtc (localhost:1984) --> Browser (iframe MP4 stream)
```

go2rtc runs as a managed child process alongside the app. `Go2RtcService` generates a `go2rtc.yaml` config file from the camera settings, spawns the process, and monitors its health. Each camera view page embeds an iframe pointing at `http://localhost:1984/api/stream.mp4?src=camera-{N}`.

### Camera entity (DB-backed)

```csharp
public class Camera
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string IpAddress { get; set; }
    public int Port { get; set; } = 554;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? RtspStreamUrl { get; set; }
    public string? OnvifServiceUrl { get; set; }
    public bool IsOnvif { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime? LastSeen { get; set; }
    public string? MacAddress { get; set; }
    public int? CameraNumber { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? SerialNumber { get; set; }
    public string? HardwareId { get; set; }
}
```

EF-mapped entity stored in the `Cameras` table. Replaces the legacy `camera-{index}-*` indexed-slot model (purged via `PurgeLegacyCameraSettingsMigration`). Per-camera credentials are stored as encrypted Settings under keys `camera-{guid:N}-username` and `camera-{guid:N}-password` (module: `cameras`); the entity itself never holds the password.

### CameraService

`CameraService` implements `ICameraService` over `AppDbContext` (EF) for the `Camera` rows + `IConfigurationService` for the credential secrets. Notifies `ICameraChangeNotifier` (singleton) on every mutation so subscribed UI re-renders.

Key methods:
| Method | Purpose |
|--------|---------|
| `GetAllAsync()` | All cameras, no tracking |
| `GetEnabledAsync()` | Cameras where `Enabled = true` |
| `GetAsync(id)` | Single camera by GUID |
| `AddAsync(camera, user, pass)` | Insert + seed `LastSeen = UtcNow` + persist creds + notify |
| `UpdateAsync(camera)` | Update non-secret fields + notify |
| `SetCredentialsAsync(id, user, pass)` | Update encrypted creds only |
| `GetCredentialsAsync(id)` | Decrypt and return (user, pass), or null if either missing |
| `DeleteAsync(id)` | Remove row + delete creds + notify |
| `UpdateLastSeenAsync(id)` | Bump `LastSeen` + notify (called by liveness probe) |
| `DeleteAllAsync()` | Atomic batch delete + single notify fire (no per-row regen storms) |

### Camera scanner + liveness

`ICameraScanService` runs on-demand network scans across configured subnets and emits `CameraScanEvent`s through a fan-out subscriber bus. Two public entry points share a private `RunScanAsync(subnets, includeRtspSweep, ct)` helper:

| Method | Behavior |
|--------|----------|
| `StartScanAsync(subnets, ct)` | Full scan: ONVIF WS-Discovery (UDP 3702 multicast) + TCP-554 sweep in parallel against each subnet. Used by Settings → Cameras `Scan Network` button. |
| `StartOnvifOnlyScanAsync(subnets, ct)` | ONVIF WS-Discovery only, skips TCP-554 sweep. Used by the Setup Wizard's Cameras step (parallel to WizardDevices using mDNS-only quick discovery). |

ONVIF responses surface manufacturer/model/service-URL inline; non-ONVIF rows return only "TCP 554 is open" and are added via the `AddTcpCameraModal` (manual stream URL + RTSP `DESCRIBE` validation). After a successful ONVIF Add, the Hikvision/LTS-OEM ISAPI client (`IHikvisionIsapiClient`) makes a best-effort `GET /ISAPI/System/deviceInfo` call to enrich the row with Camera Number, MAC, firmware, and the user-set device name from the camera's web UI.

`CameraLivenessHostedService` (BackgroundService) wakes every 30s, reads the `cameras-liveness-interval-seconds` setting (0 = disabled, 300–3600s, default 300), and if enough time has elapsed, TCP-probes each enabled camera's RTSP port directly (no subnet sweep). On hit it bumps `LastSeen` via `CameraService.UpdateLastSeenAsync`. It does NOT populate the Discovered panel — that surface is reserved for user-initiated `StartScanAsync` calls. Hot-reload via `IntervalChangeSignal.CameraLiveness` so the Settings page's Liveness Interval input + Scan Now + Restore Default buttons take effect immediately rather than after the current Task.Delay window.

### Go2RtcService

Hosted service (`IHostedService`) that manages the go2rtc child process. Registered as a singleton implementing `IGo2RtcService`.

**Lifecycle:**
1. On startup, generates `go2rtc.yaml` from camera settings (RTSP URLs with credentials)
2. Kills any orphan process on port 1984
3. Spawns go2rtc with the generated config
4. Polls `http://localhost:1984` until ready (up to 15 seconds)

**Config generation** (`GenerateConfigAsync`):
```yaml
streams:
  camera-1: rtsp://admin:password@192.168.1.x:554
  camera-2: rtsp://admin:password@192.168.1.y:554
api:
  listen: ":1984"
```

**Crash recovery**: If go2rtc exits unexpectedly, the service restarts it up to 2 times within a 30-second window before giving up.

**Binary resolution** (`FindExecutableAsync`): Resolves through `IDependencyPathResolver.ResolveAsync("cameras", "go2rtc")` (via `IServiceScopeFactory`); returns `null` on `DependencyNotInstalledException`. The resolver itself applies the `dep-path-go2rtc` user override on top of the default `dependencies/go2rtc/`. No system PATH fallback — local-only per CLAUDE.md "Local Dependencies Only" rule.

**Interface:**
```csharp
public interface IGo2RtcService
{
    bool IsRunning { get; }
    string BaseUrl { get; }          // "http://localhost:1984"
    Task RegenerateConfigAsync();    // Regenerate config and restart
    Task StopAsync();                // Stop process (used by dependency updater)
    void Restart();                  // Restart after binary update
}
```

### CamerasModule

Implements `IToolModule` with:
- `Id`: `"cameras"`
- `SortOrder`: `4`
- `Icon`: `"bi-camera-video"`
- `Dependencies`: go2rtc (GitHub source: `AlexxIT/go2rtc`, asset: `go2rtc_win64.zip`)
- `GetNavEntries()`: Dynamically generates one `NavEntry` per registered enabled camera, preferring `Name` (falls back to "Camera N" using `CameraNumber`). Loaded from the `Cameras` DB table at startup and refreshed when `ICameraChangeNotifier` fires.

Uses `FindDepsRoot()` to resolve the absolute path to the `dependencies/` folder, consistent with other modules.

### Pages

- **CameraView** (`/cameras/{Id:guid}`) -- Embeds an iframe to `http://localhost:1984/api/stream.mp4?src=camera-{guid:N}`. Shows status messages when the camera is not configured or go2rtc is not running.

### Settings UI

- **CameraSettings** (Settings → Cameras) -- Liveness Interval input + Scan Now + Restore Default buttons at the top; toolbar with `Add Camera Manually` (opens `AddTcpCameraModal`), `Quick Scan…` (auto-detected subnet, ONVIF-only via `StartOnvifOnlyScanAsync`), `Scan Network…` (full ONVIF + TCP-554 sweep with subnet-selection modal), and `Delete All` (when ≥1 camera). Toolbar shape mirrors Settings → Android Devices for parity. Registered cameras render in a sortable table (Status / Cam # / Name / Mfr / Model / Address / MAC / Enabled / Last Seen / Actions) and the `DiscoveredCamerasPanel` surfaces unregistered scan hits below for inline-Add. Mutations regenerate `go2rtc.yaml` via `Go2RtcService.RegenerateConfigAsync()` to keep streams in sync.

### Dependency Updates

go2rtc is auto-installable via the dependency manager. During updates, `DependencyManagerService` calls `IGo2RtcService.StopAsync()` before swapping the binary and `Restart()` after, preventing file lock conflicts. This mirrors the existing pattern for ADB/ws-scrcpy-web updates.

### Tests

- `CameraServiceTests` (9 tests) -- CRUD operations, credential storage, camera count management
- `CamerasModuleTests` (5 tests) -- Module metadata, dynamic nav entry generation
- Scanner / network coverage: `CameraScanServiceTests`, `SubnetMathTests`, `OnvifClientTests`, `RtspProbeClientTests`, `HikvisionIsapiClientTests`, `CameraLivenessHostedServiceTests`, `PurgeLegacyCameraSettingsMigrationTests`

---

## 8. Imaging Tools Module

The Imaging Tools module (`Modules/Imaging/`) is a top-level sidebar section (SortOrder `6`, after Cameras) bundling six image utilities. It is the home of the migrated Icon Converter plus five new tools. The heavy lifting is done by three bundled binaries resolved through `IDependencyPathResolver` (Local-Dependencies-Only) and one in-process NuGet (`Svg.Skia`).

### ImagingModule

```csharp
Id          => "imaging"
DisplayName => "Imaging Tools"
Icon        => "bi-image"
SortOrder   => 6              // after Cameras (5); see code comment re: PR #39 renumber
```

`GetNavEntries()` returns six static entries (no dynamic visibility):

| Nav entry | Route | Backing |
|-----------|-------|---------|
| Icon Converter | `/imaging/icon-converter` | magick (`icon:auto-resize`) |
| Format Converter | `/imaging/format-converter` | magick |
| Image Resize | `/imaging/image-resize` | magick |
| SVG Rasterize | `/imaging/svg-rasterize` | Svg.Skia (in-process) |
| Magic Wand | `/imaging/magic-wand` | SkiaSharp preview + magick apply |
| Tracing | `/imaging/tracing` | vtracer (color) / potrace (mono) |

`ConfigRequirements` and `GetBackgroundJobs()` are both empty. `MainLayout.razor`'s page-title switch has a dedicated `imaging/*` case per route for the breadcrumb.

### Dependencies

The module declares three auto-managed `ModuleDependency` entries (all pre-seeded into the MSI, resolved locally):

| Name | Source | Pinned version | Asset / URL | Install leaf |
|------|--------|----------------|-------------|--------------|
| `magick` | GitHub (`ImageMagick/ImageMagick`) | 7.1.2-25 (portable Q8 x64) | `ImageMagick-*-portable-Q8-x64.7z` | `dependencies/magick/` |
| `vtracer` | GitHub (`visioncortex/vtracer`) | 0.6.4 (no `v` prefix) | `vtracer-x86_64-pc-windows-msvc.zip` | `dependencies/vtracer/` |
| `potrace` | DirectUrl (SourceForge) | 1.16 | `potrace-1.16.win64.zip` | `dependencies/potrace/` |

Notes:
- ImageMagick ships its Windows portables as `.7z` on GitHub (the imagemagick.org `.zip` archive path 404s for current builds), so the fetcher uses the `Expand-Cm7z` helper rather than `Expand-Archive`. `Expand-Cm7z` extracts via the bundled **SharpCompress** library (the same one the runtime dependency updater uses, `ArchiveExtractor`) through the small `tools/Cm7zExtract` build tool (`dotnet run`) — never a PATH-resolved 7z or a separately-vendored extractor binary, per Local-Dependencies-Only. The version pattern matches `ImageMagick ([\d.]+-\d+)`.
- vtracer's `--version` prints `visioncortex VTracer 0.6.4` (capital `VTracer`), so the version pattern is `VTracer ([\d.]+)`, not the lowercase executable name.
- potrace is pinned (no version-check URL); its zip extracts to a nested `potrace-1.16.win64/` dir, which the fetcher flattens so `potrace.exe` lands at the leaf root.

### Hardened ImageMagick policy

`magick.exe` reads `policy.xml` from its own directory. The source lives at `Modules/Imaging/Resources/magick-policy.xml` (copied to the build output via a `<Content>` item) and is staged next to the seeded binary by `fetch-magick.ps1`, overwriting the portable's permissive default — no `MAGICK_CONFIGURE_PATH` env var needed. The override is deny-by-default:

- `coder` rights `none` for `*`, then an explicit read|write allowlist: `PNG, JPG, JPEG, WEBP, AVIF, TIFF, HEIC, BMP, GIF, ICO`.
- `SVG` is read-only (the SVG render path goes through Svg.Skia, not magick MSVG).
- Known-CVE-historical coders (`MVG, MSL, XBM, EPHEMERAL, LABEL`) are denied explicitly (defense in depth).
- Resource caps: memory 512MiB, map 1GiB, area 256MP (overridable per-invocation via `-limit`).

### IImageService / ImageService

`ImageService` (singleton) drives magick via `ICommandExecutor.ExecuteResolvedAsync(resolver, "imaging", "magick", …)` — never a bare `magick` invocation. Per-call work goes to a temp workdir that is cleaned up in a `finally`. SVG rasterization is the exception: it renders **in-process** with `Svg.Skia` (no magick), then encodes to PNG/ICO with SkiaSharp.

```csharp
public interface IImageService
{
    Task<byte[]> ConvertFormatAsync(byte[] input, string targetFormat, ConvertFormatOptions? options = null, CancellationToken ct = default);
    Task<byte[]> ResizeAsync(byte[] input, ResizeOptions options, CancellationToken ct = default);
    Task<byte[]> ConvertToIcoAsync(byte[] input, int[] sizes, IcoOptions? options = null, CancellationToken ct = default);
    Task<byte[]> RemoveBackgroundAsync(byte[] input, BackgroundRemoveOptions options, CancellationToken ct = default);
    Task<byte[]> RasterizeSvgAsync(byte[] svgBytes, RasterizeOptions options, CancellationToken ct = default);
    Task<ImageInfo> GetInfoAsync(byte[] input, CancellationToken ct = default);
}
```

- **Format Converter** exposes only this Q8 build's verified encoders: `PNG, JPG, WEBP, AVIF, TIFF, BMP, GIF`. **HEIC is intentionally excluded from the target dropdown** — this build has no HEIC encoder and would silently write PNG into a `.heic` file. (HEIC is still in the policy allowlist for *decode*.) A quality slider shows only for lossy targets (JPG/WEBP/AVIF).
- **Magic Wand** removes a background by seed-click + tolerance. `FloodFillPreviewEngine` (in-process SkiaSharp) produces the responsive per-keystroke **preview** over the SignalR circuit; the bytes the user saves always come from magick (`RemoveBackgroundAsync`), never from the preview. The fuzz metric mirrors magick's `-fuzz` (Euclidean RGB distance normalized by √(3·255²)); Contiguous mode maps to magick `-floodfill`, Global to `-transparent`. The preview works on a downscaled copy (longest side ≤ 800px) for snappiness.

### ITracingService / TracingService

Tracing is kept in a **separate** service (`TracingService`, singleton) because it drives different bundled binaries than magick:

```csharp
Task<byte[]> TraceColorAsync(byte[] input, TraceColorOptions options, CancellationToken ct = default);       // vtracer
Task<byte[]> TraceMonochromeAsync(byte[] input, TraceMonochromeOptions options, CancellationToken ct = default); // magick → potrace
```

- **Color**: vtracer reads the PNG directly and emits SVG. `TraceColorOptions` models the v1 subset of vtracer flags (`--colormode`, `--hierarchical`, `--mode`, `--filter_speckle`, `--color_precision`, `--path_precision`), defaulting to vtracer's own defaults.
- **Monochrome**: potrace cannot read PNG, so magick first rasterizes the input to a bilevel **BMP** (the policy-allowed intermediate; PNM is denied by `policy.xml`), then potrace traces the BMP to SVG.

All tracing methods return the SVG document as UTF-8 bytes.

### Pages

The six pages live under `Modules/Imaging/Pages/`. Browser-side, they use the File System Access API (Chrome/Edge) for native open/save dialogs — a generic `filePickerSaveAs()` JS helper derives the `accept` type from the output extension (the Icon Converter keeps its `.ico`-locked `filePickerSave`). The **Tracing** page additionally renders an inert, always-disabled "Open in svgedit" button (`title="Coming soon — opens when svgedit is embedded in Control Menu."`) — a placeholder for a future svgedit-embedding task.

### Tests

68 imaging tests under `tests/ControlMenu.Tests/Modules/Imaging/`. The `ImageService.*` and `TracingService*` integration tests drive the **real** bundled binaries via a collection fixture that copies `publish/seed/dependencies/{magick,vtracer,potrace}` into a temp deps dir and substitutes the `IDependencyPathResolver`; they are `SkippableFact`s (skipped when the seed is absent). Page render tests use bUnit. The test project adds `Xunit.SkippableFact`.

---

## 9. Core Services

### CommandExecutor

The cross-platform process execution abstraction. All external tool invocations (ADB, Docker, PowerShell, etc.) go through this service.

Two overloads:
1. **Simple**: `ExecuteAsync(command, arguments, workingDirectory, cancellationToken)` -- Runs a process directly
2. **Cross-platform**: `ExecuteAsync(CommandDefinition, cancellationToken)` -- Selects Windows or Linux command/arguments based on `OperatingSystem.IsWindows()`

```csharp
public record CommandDefinition
{
    public required string WindowsCommand { get; init; }
    public required string LinuxCommand { get; init; }
    public string? WindowsArguments { get; init; }
    public string? LinuxArguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public TimeSpan? Timeout { get; init; }
}
```

When a `Timeout` is specified, a linked `CancellationTokenSource` is created. If the timeout expires before the user-provided token is cancelled, the result has `TimedOut: true`.

```csharp
public record CommandResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);
```

Registered as singleton: `builder.Services.AddSingleton<ICommandExecutor, CommandExecutor>()`.

### ConfigurationService

Database-backed key-value settings with module scoping and transparent encryption for secrets.

```csharp
public interface IConfigurationService
{
    Task<string?> GetSettingAsync(string key, string? moduleId = null);
    Task SetSettingAsync(string key, string value, string? moduleId = null);
    Task<string?> GetSecretAsync(string key, string? moduleId = null);
    Task SetSecretAsync(string key, string value, string? moduleId = null);
    Task DeleteSettingAsync(string key, string? moduleId = null);
    Task<IReadOnlyList<Setting>> GetModuleSettingsAsync(string moduleId);
}
```

The split between `GetSettingAsync`/`SetSettingAsync` and `GetSecretAsync`/`SetSecretAsync` is important:
- **Settings** are stored in plaintext. Read/write directly.
- **Secrets** are encrypted via `ISecretStore` before writing and decrypted after reading. The `Setting.IsSecret` flag tracks which values are encrypted.

Settings are scoped by a nullable `ModuleId`. A setting with `ModuleId = null` is global. The database enforces a unique index on `(ModuleId, Key)`.

### SecretStore

Wraps ASP.NET Data Protection API (DPAPI on Windows) for symmetric encryption of sensitive settings.

```csharp
public class SecretStore : ISecretStore
{
    private readonly IDataProtector _protector;

    public SecretStore(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("ControlMenu.Settings");
    }

    public string Encrypt(string plaintext) => _protector.Protect(plaintext);
    public string Decrypt(string ciphertext) => _protector.Unprotect(ciphertext);
}
```

Data Protection keys are persisted under `<dataRoot>/keys/` (`IDataPathResolver.GetKeysDir()`), scoped to application name `"ControlMenu"`, and on Windows additionally encrypted at rest with DPAPI (`ProtectKeysWithDpapi`). `<dataRoot>` is `C:\ProgramData\ControlMenu` in an installed (Velopack) build and the app base directory under `dotnet run`. These keys must be preserved when migrating machines — losing them makes all encrypted settings unreadable.

### DependencyManagerService

Manages the lifecycle of external tool dependencies: version checking, downloading, extracting, and installing.

**Sync on startup**: `SyncDependenciesAsync()` runs during app initialization. It iterates all modules' declared dependencies, upserts corresponding `Dependency` entities in the database, removes orphaned entries, and refreshes installed versions.

**Version checking** supports three strategies:
- `UpdateSourceType.GitHub` -- Fetches `GET /repos/{owner}/{repo}/releases/latest` from GitHub API, extracts `tag_name`
- `UpdateSourceType.DirectUrl` -- Two paths. With `VersionCheckUrl` + `VersionCheckPattern` (adb, sqlite3): scrapes that page with the regex. Without them (potrace, pinned): the version in `DownloadUrl`'s file name *is* the latest — `VersionFromFileName`, digits and interior dots only — and is written to `LatestKnownVersion` on every check, derived rather than remembered
- `UpdateSourceType.Manual` -- Always reports `UpToDate` (user manages these externally)

**Download and install** (`DownloadAndInstallAsync`):
1. Download the asset to a temp directory over a **transport hard-gate** (HTTPS only; the final URL after redirects must resolve to an allowlisted host)
2. **Verify the downloaded asset's integrity before extracting or running anything** — the tiered gate below; a failure aborts the install
3. Extract via managed **SharpCompress** (`.zip` / `.tar.gz` / `.7z`), with an `IsWithinRoot` zip-slip guard on every entry — no external `tar` binary required
4. Verify the extracted binary by running the version command
5. Stop dependent services if needed (ws-scrcpy-web and ADB server when updating `adb`; go2rtc when updating `go2rtc`)
6. Backup existing files (`.bak` suffix)
7. Copy new files into the install path
8. Update the database entity
9. Restart dependent services

**Runtime download integrity** (`IArtifactVerifier`): the updater executes freshly-downloaded third-party binaries, so every asset is verified *before* it is extracted or run. The gate is tiered — the first tier that can render a verdict wins:
- **T1 — pinned SHA-256.** `KnownHashes` maps `name@version` to a known-good SHA-256; an exact match passes. Seeded for the current binaries and refreshed via `scripts/update-dependency-hashes.ps1` as upstream releases (a newer-than-seeded version falls through to T2/Tier-4 by design). The version key must match the resolved `LatestKnownVersion` string exactly (GitHub tags are stored `TrimStart('v')`), or T1 silently misses. A pinned `DirectUrl` dependency — one with no `VersionCheckUrl` — derives `LatestKnownVersion` from the version in its download URL on every check (`VersionFromFileName`: digits and interior dots only), so the key for `potrace-1.16.win64.zip` is `1.16`, not the `1.16.` the previous filename pattern produced.
- **T2 — upstream-published checksum.** sqlite's SHA3-256 download-page value; ImageMagick's `.intoto.jsonl` (in-toto attestation) SHA-256.
- **T3 — Authenticode.** `adb.exe` is Authenticode-signed by Google; the chain is verified and the leaf pinned to `CN=Google LLC`. Revocation is **offline-tolerant** (`WTD_REVOKE_WHOLECHAIN`): a definitively-revoked cert is rejected, but an unreachable revocation server still trusts the signature so legitimate offline updates work (`WindowsAuthenticodeInspector`).
- **Tier-4 — explicit user confirmation.** Assets with no verifiable provenance (go2rtc and vtracer are `NotSigned`) require a confirmation dialog before install.

Extraction moved to SharpCompress specifically so ImageMagick's portable **`.7z`** (LZMA2+BCJ) actually applies — the previous `.zip`/`.tar.gz`-only extractor left magick's runtime auto-update silently inert.

**Installed version detection** is local-only: `GetInstalledVersionAsync` resolves the configured `InstallPath` (with optional `dep-path-{name}` override), checks for the binary on disk, and runs its version command. Returns `null` if the binary isn't installed locally — never falls back to system PATH. This is enforced by the architectural rule, not just convention; see `IDependencyPathResolver` below.

**`IDependencyPathResolver`** (`Services/DependencyPathResolver.cs`) is the single supported way to obtain a path to a bundled binary. `ResolveAsync(moduleId, name, ct)` returns the absolute local path or throws `DependencyNotInstalledException`. All consumers go through `ICommandExecutor.ExecuteResolvedAsync(resolver, moduleId, name, args, …)` (an extension method); the raw `ICommandExecutor.ExecuteAsync(string command, …)` overload is reserved for the OS-builtin allowlist (`docker`, `powershell`, `arp`, `ping`) — XML-documented at the call site. Bare-name calls to bundled binaries are an anti-pattern this boundary makes structurally impossible.

**Install-path override storage** (`dep-path-{name}` Setting): paths under the module's `DepsRoot` are stored as **relative segments** (e.g. `"platform-tools"` rather than `C:\...\dependencies\platform-tools`); paths outside `DepsRoot` are stored as absolute. `InstallPathResolver` (in `Services/`) handles encode/decode against the current `DepsRoot`. This means folder/repo renames don't strand the override at the old absolute path. On startup `SyncDependenciesAsync` calls `ValidateInstallPathOverridesAsync`, which clears any override whose resolved parent directory no longer exists (defensive self-heal for absolute overrides pointing at vanished locations).

**Periodic background checks**: `DependencyCheckHostedService` runs as a `BackgroundService`, checking all dependency versions on a configurable interval (default: 24 hours, setting key: `dep-check-interval`). It waits 10 seconds after app start before the first check.

### BackgroundJobService

Manages the lifecycle of long-running jobs with database-backed state.

```
Job lifecycle: Queued --> Running --> Completed | Failed | Cancelled
```

Key operations:
- `CreateJobAsync(moduleId, jobType)` -- Creates a `Queued` job
- `StartJobAsync(id, processId)` -- Marks `Running` with timestamp
- `UpdateProgressAsync(id, progress, message)` -- 0-100 progress with optional message
- `CompleteJobAsync(id, resultData)` -- Marks `Completed` at 100%
- `FailJobAsync(id, errorMessage, resultData)` -- Marks `Failed` with error
- `RequestCancellationAsync(id)` -- Sets `CancellationRequested` flag (cooperative)

Cancellation is cooperative: workers poll `Job.CancellationRequested` between batches. The `ResultData` field stores JSON state for resume support (e.g., `LastProcessedIndex` in the cast & crew worker).

### NetworkDiscoveryService

Resolves device IPs from MAC addresses using the system ARP table, via an injected `IArpTableProvider` (OS-selected at registration).

- `GetArpTableAsync()` -- Delegates to `IArpTableProvider`: `WindowsArpTableProvider` reads the ARP cache via the IP Helper API (`iphlpapi!GetIpNetTable` P/Invoke — no shell-out, no locale-fragile parsing); `ShellArpTableProvider` runs `arp -a` and parses both layouts via source-generated regexes (non-Windows / fallback)
- `ResolveIpFromMacAsync(mac)` -- Looks up a normalized MAC in the ARP table
- `PingAsync(ip)` -- Single reachability check via managed `System.Net.NetworkInformation.Ping` (2-second timeout)
- `NormalizeMac(mac)` -- Converts to lowercase dashes: `AA:BB:CC:DD:EE:FF` becomes `aa-bb-cc-dd-ee-ff`

### DeviceService

Standard CRUD operations for `Device` entities. Uses `IDbContextFactory` for all database access. Notable method: `UpdateLastSeenAsync(id, ip)` updates both `LastKnownIp` and `LastSeen` timestamp.

### EmailService

SMTP email with settings-driven configuration.

Required settings:
- `smtp-server` -- SMTP host
- `smtp-port` -- Port (default: 587)
- `smtp-username` -- Login username
- `smtp-password` -- Login password (stored encrypted via `GetSecretAsync`)
- `smtp-from-email` -- Authorized sender address
- `notification-email` -- Default recipient

`SendTestAsync()` sends a test email to the configured notification address with a UTC timestamp.

---

## 10. Database Schema

### Connection

SQLite database file `controlmenu.db`. Its path is resolved at startup via `IDataPathResolver.GetDbPath()` → `<dataRoot>/config/controlmenu.db` (installed: `C:\ProgramData\ControlMenu\config\`; dev: under the app base directory), and `Program.cs` builds the EF Core connection string from that resolved path.

### Factory Pattern

The app uses `IDbContextFactory<AppDbContext>` (not direct `AppDbContext` injection) because Blazor Server circuits are long-lived. Each database operation creates a short-lived context:

```csharp
using var db = await _dbFactory.CreateDbContextAsync();
// ... use db ...
// context is disposed at end of scope
```

This prevents stale change tracker state that would accumulate in a single context shared across the circuit's lifetime.

### Tables

**Devices**
| Column | Type | Notes |
|--------|------|-------|
| Id | Guid (PK) | |
| Name | string (required) | |
| Type | string (enum) | `"GoogleTV"` or `"AndroidPhone"` |
| MacAddress | string (required) | Lowercase dashes, e.g. `aa-bb-cc-dd-ee-ff` |
| SerialNumber | string? | |
| LastKnownIp | string? | |
| AdbPort | int | Default: 5555 |
| LastSeen | DateTime? | UTC |
| ModuleId | string (required) | |
| Metadata | string? | JSON for extensible data |

**Jobs**
| Column | Type | Notes |
|--------|------|-------|
| Id | Guid (PK) | |
| ModuleId | string (required) | |
| JobType | string (required) | e.g. `"cast-crew-update"` |
| Status | string (enum) | `Queued`, `Running`, `Completed`, `Failed`, `Cancelled` |
| Progress | int? | 0-100 |
| ProgressMessage | string? | |
| ProcessId | int? | OS process ID when running |
| CancellationRequested | bool | Cooperative cancellation flag |
| StartedAt | DateTime? | |
| CompletedAt | DateTime? | |
| ErrorMessage | string? | |
| ResultData | string? | JSON (used for resume state) |

**Dependencies**
| Column | Type | Notes |
|--------|------|-------|
| Id | Guid (PK) | |
| ModuleId | string (required) | |
| Name | string (required) | e.g. `"adb"`, `"docker"` |
| InstalledVersion | string? | |
| LatestKnownVersion | string? | |
| DownloadUrl | string? | Resolved during version check |
| ProjectHomeUrl | string? | |
| LastChecked | DateTime? | |
| Status | string (enum) | `UpToDate`, `UpdateAvailable`, `UrlInvalid`, `CheckFailed` |
| SourceType | string (enum) | `GitHub`, `DirectUrl`, `Manual` |

**Settings**
| Column | Type | Notes |
|--------|------|-------|
| Id | Guid (PK) | |
| ModuleId | string? | null = global setting |
| Key | string (required) | |
| Value | string (required) | Encrypted ciphertext when `IsSecret = true` |
| IsSecret | bool | Determines if Value is encrypted |

Unique index: `(COALESCE(ModuleId, ''), Key)` -- enforced at the SQLite level to handle NULL ModuleId correctly.

### Migrations

Auto-applied on startup in `Program.cs`:
```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}
```

Enum columns are stored as strings (`HasConversion<string>()`), not integers, for readability and forward compatibility.

---

## 11. Setup Wizard

The setup wizard runs on first launch (when the `setup-completed` setting is absent). It has seven steps, each a separate Razor component under `Components/Pages/Setup/`:

| Step | Component | Purpose |
|------|-----------|---------|
| 1 | WizardWelcome | Introduction |
| 2 | WizardDevices | Scan for Android devices via mDNS + ARP, inline-Add discovered devices |
| 3 | WizardCameras | Auto-detect LAN subnet + ONVIF-only scan, inline-Add discovered cameras |
| 4 | WizardJellyfin | Configure Jellyfin docker-compose path, validate compose file |
| 5 | WizardEmail | Configure SMTP settings, send test email |
| 6 | WizardDependencies | Scan for installed tools, install missing ones |
| 7 | WizardDone | Summary (devices + cameras + jellyfin + email + deps), sets `setup-completed` |

The `WizardStepper.razor` component provides the step indicator and navigation. `WizardState` is passed to every step that contributes to the Done summary (`DevicesAdded`, `CamerasAdded`, `JellyfinConfigured`, `SmtpConfigured`, `DependenciesFound`, `DependenciesTotal`).

During the **Android Devices** step, `IAdbService.ScanMdnsAsync()` queries `adb mdns services` for advertised devices and `NetworkDiscoveryService.GetArpTableAsync()` resolves MACs from IPs. The intro paragraph explicitly notes this discovery path is mDNS-only (modern Android); older devices need Settings → Android Devices post-wizard.

During the **Cameras** step, `SubnetDetectionClient.DetectAsync()` calls ws-scrcpy-web's `/api/devices/scan/subnet` endpoint to find the active LAN subnet (the adapter with a default gateway in the same subnet, filtering out Hyper-V virtual switches etc.), then `ICameraScanService.StartOnvifOnlyScanAsync` fires WS-Discovery against that subnet. The shared `DiscoveredCamerasPanel` handles inline-Add per row + bulk-creds entry. Non-ONVIF / RTSP-only cameras are deferred to Settings → Cameras post-wizard via the intro paragraph callout.

---

## 12. Settings Architecture

### Settings Page

`SettingsPage.razor` uses a tabbed layout with five sections:

| Tab | Component | Key Settings |
|-----|-----------|-------------|
| General | GeneralSettings | `smtp-server`, `smtp-port`, `smtp-username`, `smtp-password` (secret), `smtp-from-email`, `notification-email`, `app-timezone`; **External Dependencies**: `wsscrcpy-url` (ws-scrcpy-web URL) + docker executable path |
| Android Devices | DeviceManagement | Device CRUD (the `Devices` table). The ws-scrcpy-web URL lives on General → External Dependencies (`wsscrcpy-url`), not here. |
| Cameras | CameraSettings | `cameras-liveness-interval-seconds`, `cameras-scan-subnets`, per-camera username/password as `camera-{guid:N}-username/-password` (secrets) (module: `cameras`). Camera rows live in the `Cameras` DB table, not in Settings. |
| Jellyfin | JellyfinSettingsSection | `jellyfin-compose-path`, `jellyfin-api-key` (secret), `jellyfin-base-url`, `jellyfin-user-id`, `jellyfin-castcrew-notify-email`; **Logging, Backup & Retention**: `jellyfin-backup-directory`, `jellyfin-log-directory`, `jellyfin-backup-retention-days` (default 5; the DB Date Update page and `CleanupOldBackupsAsync` both read it) |
| Dependencies | DependencyManagement | Per-dependency install paths (`dep-path-{name}`), version check, install/update buttons, check interval (`dep-check-interval`) |

### Module-Scoped vs. Global Settings

Settings with a non-null `ModuleId` are scoped to that module. For example, a camera's `camera-{guid:N}-username` secret carries `ModuleId = "cameras"`. SMTP settings have `ModuleId = null` (global). (`ws_scrcpy_web_path`, the example this paragraph used to give, is a legacy key that `ObsoleteSettingsCleanupService` deletes on startup — see §3.)

### Secret Management

Settings marked as secrets go through `ConfigurationService.SetSecretAsync` which:
1. Calls `SecretStore.Encrypt(value)` to produce a DPAPI-protected ciphertext
2. Stores the ciphertext in `Setting.Value` with `IsSecret = true`
3. On read, `GetSecretAsync` checks `IsSecret` and decrypts transparently

The Data Protection keys under `<dataRoot>/keys/` (`C:\ProgramData\ControlMenu\keys\` in an installed build, DPAPI-encrypted at rest on Windows) must be preserved when migrating between machines. Loss of these keys means all encrypted settings become unreadable.

---

## 13. Build and Deployment

### Package Versions (Central Package Management)

Every NuGet version for the solution is declared once in **`Directory.Packages.props`** at the repo root; the project files carry versionless `<PackageReference>` entries. `ManagePackageVersionsCentrally` is `true`, so a `Version=` attribute on a `PackageReference` is a build **error**, not a warning.

This was adopted because versions had drifted between projects — `coverlet.collector` at 6.0.2 in one test project and 10.0.1 in the others, `Microsoft.NET.Test.Sdk` split across two versions. Dependabot now updates the single `<PackageVersion>` and every project moves together, which is also why the nuget updates arrive as one grouped PR rather than one per project.

`CentralPackageFloatingVersionsEnabled` is `true` so `Microsoft.EntityFrameworkCore.Design` and `.Sqlite` can keep floating on `10.*`, as they did before CPM. That float is load-bearing: it is what silently resolved the SQLitePCLRaw NU1903 advisory when EF Core 10.0.11 moved onto the patched 2.1.12.

### Development

```bash
cd src/ControlMenu
dotnet run
```

The app starts on http://localhost:5159. The first-run wizard guides through initial setup.

### Published Release

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

For Linux:
```bash
dotnet publish -c Release -r linux-x64 --self-contained
```

### Packaging & Distribution (Velopack)

Shipping builds are packaged with [Velopack](https://github.com/velopack/velopack) into a **PerMachine MSI** (`vpk pack --msi --instLocation PerMachine`), installing to `C:\Program Files\ControlMenu\`. The installed app is a **three-binary** layout:

| Binary | Role |
|--------|------|
| `ControlMenuLauncher.exe` | Velopack supervisor — runs `VelopackApp.Build().Run()`, hydrates seeded dependencies, spawns the host, and orchestrates updates (apply-on-exit-75). |
| `ControlMenu.exe` | The Blazor Server host (this codebase). Also calls `VelopackApp.Build().SetAutoApplyOnStartup(false).Run()` so it initializes its own `VelopackLocator`. |
| `ControlMenuTray.exe` | Phase 1 stub (tray UI is a later phase). |

**Path resolution.** All writable state routes through `IDataPathResolver` (`ControlMenu.Common.Paths`), chosen at startup by `DataPathResolverFactory`:
- **Installed (Velopack)** → `VelopackDataPathResolver`, rooted at `C:\ProgramData\ControlMenu\` (detected by probing for `..\..\Update.exe`).
- **Dev (`dotnet run`)** → `DevDataPathResolver`, rooted at `AppContext.BaseDirectory`.

Under `<dataRoot>`: `config/controlmenu.db`, `logs/` (+ `logs/jellyfin/`), `keys/` (DataProtection), `dependencies/`, `jellyfin-backups/`.

**Dependency pre-seeding.** The MSI bundles pinned runtime binaries (adb, sqlite3, go2rtc, magick, vtracer, potrace) under `seed/dependencies/`; on launch `SeedHydrator` copies any missing leaf into `<dataRoot>/dependencies/` (idempotent, preserves a user-updated version). CI stages the seed via `scripts/stage-seed.ps1` + `scripts/dependencies/fetch-*.ps1` (auto-discovered) between `dotnet publish` and `vpk pack`. The shared `_Fetcher.ps1` does pinned-URL + SHA-256 download, deterministic extract (`Expand-CmZip` for `.zip`, `Expand-Cm7z` for ImageMagick's `.7z` via the bundled SharpCompress library through the `tools/Cm7zExtract` build tool — no vendored 7-Zip binary), and idempotent caching; `fetch-magick.ps1` additionally stages the hardened `magick-policy.xml` next to `magick.exe`.

**In-app updates.** Settings → General → "Check for updates" uses `VelopackUpdateService` (`Velopack.UpdateManager` + `GithubSource`) against the GitHub Releases feed. Apply signals the launcher via a shared `UpdateApplyState` flag that `Program` returns as exit code 75 from `Main` (+ `StopApplication`); the launcher performs the Velopack swap and relaunches. The tag-triggered release pipeline lives in `.github/workflows/release.yml`.

### Dependency Path Resolution

Managed tools are resolved by **absolute local path**, never via the system `PATH`. At startup `Program.cs` seeds a holder from the data-path resolver, before module discovery runs:

```csharp
ControlMenu.Services.DepsRootHolder.Path = dataPathResolver.GetDependenciesDir();
```

Each module then builds its tool paths directly off that root — e.g. `AndroidDevicesModule` resolves `Path.Combine(DepsRoot, "platform-tools")` and `CamerasModule` resolves `Path.Combine(DepsRoot, "go2rtc")`. Runtime resolution and version checks flow through `IDependencyPathResolver` (honouring any `dep-path-{name}` override) and **never fall back to a system-`PATH` lookup** — enforced by the "Local Dependencies Only" architectural rule, not just convention.

> An earlier build prepended every `dependencies/` subdirectory to the `PATH` environment variable. That approach was removed in the April 2026 Local-Dependencies-Only audit in favour of explicit absolute-path resolution; the app no longer reads or mutates `PATH` for tool discovery.

### External Requirements

- No external database server (SQLite is embedded)
- Docker is required only for Jellyfin module functionality
- All other dependencies are auto-managed in the `dependencies/` folder

### Data Locations

All paths resolve under `<dataRoot>` via `IDataPathResolver` — `C:\ProgramData\ControlMenu\` in an installed (Velopack) build, or `AppContext.BaseDirectory` under `dotnet run`.

| Item | Resolver method | Path under `<dataRoot>` |
|------|-----------------|--------------------------|
| SQLite database | `GetDbPath()` | `config/controlmenu.db` |
| DataProtection keys | `GetKeysDir()` | `keys/` (DPAPI-encrypted on Windows) |
| Managed dependencies | `GetDependenciesDir()` | `dependencies/` |
| Jellyfin operation logs | `GetLogsDir()` | `logs/jellyfin/` |
| Jellyfin backups | `GetJellyfinBackupsDir()` | `jellyfin-backups/` |

---

## 14. Testing

### Framework

- **xUnit** -- test runner
- **Moq** -- mocking framework
- **bunit** -- Blazor (Razor) component testing
- **820 tests** (all green on net10.0) across three projects — `ControlMenu.Tests` (app), `ControlMenu.Common.Tests`, and `ControlMenuLauncher.Tests` — run together via `ControlMenu.sln`

### Test Database

`TestDbContextFactory` provides in-memory SQLite databases for tests. Two modes:

1. **Single context**: `TestDbContextFactory.Create()` -- Returns one `AppDbContext` backed by an in-memory SQLite connection. Good for simple tests.

2. **Factory pattern**: `TestDbContextFactory.CreateFactory()` -- Returns an `InMemoryDbContextFactory` (implements `IDbContextFactory<AppDbContext>`) where every `CreateDbContext()` call returns a new context pointing at the same shared in-memory database. This mirrors the production `IDbContextFactory` pattern.

Both modes apply a workaround for SQLite's NULL handling in unique indexes: the `(ModuleId, Key)` index on Settings is recreated with `COALESCE(ModuleId, '')` so that multiple settings with `ModuleId = NULL` and different keys are correctly treated as distinct.

### Test Organization

```
tests/ControlMenu.Tests/
  Data/
    AppDbContextTests.cs          # Schema and entity tests
    TestDbContextFactory.cs       # Shared test infrastructure
  Services/
    BackgroundJobServiceTests.cs
    CommandExecutorTests.cs
    ConfigurationServiceTests.cs
    DependencyCheckHostedServiceTests.cs
    DependencyManagerServiceTests.cs
    DependencyScanTests.cs
    DeviceServiceTests.cs
    NetworkDiscoveryServiceTests.cs
    SecretStoreTests.cs
  Modules/
    AndroidDevices/               # AdbService, liveness tests
    AndroidPowerTools/            # AndroidPowerToolsPage (bUnit; the approval countdown on a FakeTimeProvider)
    Cameras/                      # CameraService, CamerasModule, CameraProxyMiddleware tests
    Jellyfin/                     # JellyfinService (+ media cards), JellyfinDirectoryResolver, ComposeParser, CastCrewUpdateWorker, MediaCardRefreshWorker tests
    Imaging/                      # ImageService (real-magick integration), TracingService, page render, ImagingModule (68 tests)
    Utilities/                    # FileUnblockService tests
    Fakes/                        # Test doubles
    ModuleDiscoveryServiceTests.cs
```

Two further test projects sit alongside this one and run together via `ControlMenu.sln`: **`ControlMenu.Common.Tests`** (path / config / seeding helpers in `ControlMenu.Common`) and **`ControlMenuLauncher.Tests`** (the Velopack launcher — single-instance, hook dispatch, child supervisor, install-ACL).

### Running Tests

```bash
dotnet test
```

All tests run in-process with no external dependencies. ADB, Docker, and other external tools are mocked via `ICommandExecutor`.

---

## 15. Known Issues and Fixes

### Phone Mirror Panel Click Handling

**Problem**: In the Android Phone dashboard, the ws-scrcpy-web iframe embedded in the mirror panel did not respond to mouse clicks. The video stream would display but interaction was impossible.

**Root Cause**: The mirror panel `<div>` had no explicit sizing, causing the iframe to have zero effective area for pointer event handling despite being visually rendered.

**Fix**: The phone mirror panel uses `position: relative` on the container with explicit dimensions, and `position: absolute` on the iframe. This gives the iframe a concrete layout rectangle that receives pointer events correctly.

### ADB Unlock Sequence

**Problem**: Early implementations of PIN unlock used digit-by-digit `keyevent` input or added delays between commands, causing unreliable behavior.

**Fix**: Use plain sequential ADB calls with `input text` for the full PIN. No delays, no keyevent digit splitting:
```csharp
await _executor.ExecuteAsync("adb", $"{dev} shell input keyevent 26", null, ct);  // power
await _executor.ExecuteAsync("adb", $"{dev} shell input keyevent 82", null, ct);  // menu
await _executor.ExecuteAsync("adb", $"{dev} shell input text {pin}", null, ct);   // PIN
await _executor.ExecuteAsync("adb", $"{dev} shell input keyevent 66", null, ct);  // enter
```

### MAC Address Normalization

**Problem**: MAC addresses could be stored with colons or mixed case, causing ARP table lookups to fail.

**Fix**: On startup, `Program.cs` normalizes all existing MAC addresses in the database:
```csharp
var devicesWithBadMac = db.Devices
    .AsEnumerable()
    .Where(d => d.MacAddress != NetworkDiscoveryService.NormalizeMac(d.MacAddress))
    .ToList();
foreach (var device in devicesWithBadMac)
    device.MacAddress = NetworkDiscoveryService.NormalizeMac(device.MacAddress);
```

### SQLite NULL Uniqueness

**Problem**: SQLite treats NULL values as distinct in unique indexes. The `(ModuleId, Key)` index on Settings would allow duplicate global settings (where `ModuleId = NULL`).

**Fix**: Tests recreate the index with `COALESCE`: `CREATE UNIQUE INDEX ... ON "Settings" (COALESCE("ModuleId", ''), "Key")`. This makes NULL ModuleId behave like an empty string for uniqueness purposes.

### Dependency Update Loop

**Problem**: When a managed dependency (e.g., adb in `dependencies/platform-tools`) was updated, the version check would still find the old system-installed version on PATH, reporting it as outdated and triggering another update.

**Fix**: `GetInstalledVersionAsync` now prioritizes the local install path over system PATH for dependencies that have an `InstallPath` configured. If the local binary exists, it checks only that binary. If it does not exist, it returns null (not installed) rather than falling back to a stale system PATH version.

### ws-scrcpy-web Orphan Process (historical — no longer applicable)

Earlier builds spawned ws-scrcpy-web as a managed child and had to kill orphans holding port 8000 on restart. Since v1.0.0, ws-scrcpy-web is **external** (the user runs it), so CM no longer spawns or orphan-kills it. The analogous live mechanism today is `Go2RtcService`, which kills orphan `go2rtc` processes on startup (`KillAllOrphans`) and waits for confirmed exit on stop — see the [Cameras module](#7-cameras-module).

### ComposeParser Windows Drive Letters

**Problem**: Parsing Docker volume mounts like `C:/jellyfin/config:/config` would incorrectly split on the drive letter colon.

**Fix**: `FindMountSeparator` skips colons at position 1 when preceded by a single letter (the Windows drive letter pattern `X:/`):
```csharp
if (mount[i] == ':' && mount[i + 1] == '/')
{
    if (i == 1 && char.IsLetter(mount[0]))
        continue;  // Skip Windows drive letter
    return i;
}
```
