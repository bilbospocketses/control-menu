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
8. [Core Services](#8-core-services)
9. [Database Schema](#9-database-schema)
10. [Setup Wizard](#10-setup-wizard)
11. [Settings Architecture](#11-settings-architecture)
12. [Build and Deployment](#12-build-and-deployment)
13. [Testing](#13-testing)
14. [Known Issues and Fixes](#14-known-issues-and-fixes)

---

## 1. Architecture Overview

Control Menu is a .NET 9 Blazor Server web application that manages Android devices (Google TV Streamers, phones) via ADB, a Jellyfin media server via Docker, and assorted system utilities. It replaces a collection of PowerShell scripts with a cross-platform web UI.

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
|  - Jellyfin (sort 2)                                      |
|  - Utilities (sort 3)                                     |
|  - Cameras (sort 4)                                       |
|  - Auto-discovered via reflection at startup              |
+-----------------------------------------------------------+
|  Layer 3: Core Services                                   |
|  - CommandExecutor, ConfigurationService, SecretStore      |
|  - DependencyManager, BackgroundJobs, Email                |
|  - WsScrcpy, Go2Rtc, NetworkDiscovery, DeviceService      |
+-----------------------------------------------------------+
|  Layer 4: Persistence (SQLite via EF Core 9)              |
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
| Self-contained dependencies | 5 auto-managed tools in `dependencies/`; 2 external (Docker, ws-scrcpy-web) |

### Project Layout

```
src/ControlMenu/
  Program.cs                # Host builder, DI registration, startup
  Components/
    Layout/                 # MainLayout, Sidebar, TopBar
    Pages/                  # Home, Settings, Setup Wizard
      Settings/             # SettingsPage, tabs: General, Devices, Cameras, Jellyfin, Dependencies
      Setup/                # Wizard steps: Welcome, Android Devices, Cameras, Jellyfin, Email, Dependencies, Done
    Shared/                 # ScrcpyMirror
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
    Cameras/                 # Module class, Pages/, Services/ (Go2RtcService)
    Jellyfin/                # Module class, Pages/, Services/, Workers/
    Utilities/               # Module class, Pages/, Services/
  Services/                  # Core services (see section 7)
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
| Android Devices | `android-devices` | 1 | adb, scrcpy, node, ws-scrcpy-web | Device List, Google TV, Android Phone |
| Android Power Tools | `android-power-tools` | 2 | (none — shares ws-scrcpy-web with Android Devices) | Power Tools |
| Jellyfin | `jellyfin` | 3 | docker, sqlite3 | DB Date Update, Cast & Crew |
| Utilities | `utilities` | 4 | (none) | Icon Converter, File Unblocker |
| Cameras | `cameras` | 5 | (none) | Dynamic: one entry per configured camera |

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

The module declares four dependencies, each auto-managed in the `dependencies/` folder:

| Name | Source Type | Install Path | Purpose |
|------|-----------|--------------|---------|
| adb | DirectUrl (Google) | `dependencies/platform-tools` | Device management |
| scrcpy | GitHub (Genymobile/scrcpy) | `dependencies/scrcpy` | Screen mirroring server binary |
| node | DirectUrl (nodejs.org) | `dependencies/node` | ws-scrcpy-web runtime |
| ws-scrcpy-web | Manual | (user-configured) | Browser-based screen mirroring |

### Pages

- **DeviceSelector** (`/android/devices`) -- CRUD for device records, network discovery for IP resolution
- **GoogleTvDashboard** (`/android/googletv`) -- Power toggle, screensaver selector, screen timeout, launcher enable/disable, Projectivy backup list with restore, Shizuku start, screen mirror (passes `DeviceKind="tv"` to `ScrcpyMirror` so the ws-scrcpy-web toolbar defaults to D-pad mode)
- **PixelDashboard** (`/android/phone`) -- ADB connect, PIN unlock, portrait-mode screen mirror with aspect ratio from ADB screen dimensions (passes `DeviceKind="phone"` to `ScrcpyMirror` so the ws-scrcpy-web toolbar defaults to Touch mode)

### WsScrcpyService

`WsScrcpyService` is registered as both a singleton and an `IHostedService`. It manages the Node.js ws-scrcpy-web child process.

Lifecycle:
1. **StartAsync**: Reads `ws_scrcpy_web_path` setting, finds `dist/index.js`, kills any orphan process on port 8000, spawns the Node process, waits up to 15 seconds for HTTP readiness
2. **Health monitoring**: The `Exited` event handler restarts the process up to 2 times within a 30-second window before giving up
3. **Orphan cleanup**: On startup, checks if port 8000 is in use and kills the owning process (uses `netstat -ano` on Windows, `lsof -t -i :8000` on Linux)
4. **StopAsync**: Kills the process tree and marks service as not ready

The `Restart()` method is used during ADB dependency updates -- the dependency manager stops ws-scrcpy-web before updating ADB (since ws-scrcpy-web uses ADB), then restarts it after.

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
{BaseUrl}/embed.html?device={Udid}
```

`embed.html` is ws-scrcpy-web's dedicated iframe-friendly wrapper (shipped with the 1.0.0 stream API rewrite, April 2026). It renders the stream plus toolbar only, with a transparent background so iframe consumers can place any background behind the video. The legacy `#!action=stream&udid=...&embed=true` hash-routing URL was removed in the same release.

Additional URL parameters supported by `embed.html` — `host`, `port`, `secure`, `pathname`, `codec`, `encoder`, `bitrate`, `maxFps`, `maxSize`, `audio`, `keyboard` — all optional. See the ws-scrcpy-web TECHNICAL_GUIDE for the complete reference.

### Display Modes

**Google TV (Landscape)**: The mirror panel uses `width: 100%` and lets the iframe fill the available horizontal space.

**Android Phone (Portrait)**: Uses a `position: relative` container with an `absolute`-positioned iframe. The aspect ratio is calculated from ADB screen dimensions (via `GetScreenSizeAsync`) plus toolbar width compensation.

### Critical Bug Fix

The phone mirror panel required explicit sizing for iframe click handling to work correctly. Without `position: relative` on the container and `position: absolute` on the iframe, click events would not propagate through to the ws-scrcpy-web stream. This is documented further in [Known Issues and Fixes](#14-known-issues-and-fixes).

### Fallback Behavior

When `WsScrcpy.IsRunning` is false, the component renders a warning alert instead of the iframe. When `Inline` is false, it renders a "Screen Mirror" button that opens a popup window (`window.open` with specific dimensions and no browser chrome).

### Android Power Tools Module

Peer of Android Devices (sort order 2), added April 2026. Host for ws-scrcpy-web's full home page via iframe at `/android-power-tools`. Gives the user direct access to power-user workflows that aren't replicated in Control Menu's Android Devices UI: one-click shell (xterm modal), file browser (ListFilesModal with sticky header, reserved actions column, bulk selection, drag-and-drop upload, filter), ConfigureScrcpy stream parameters, network scan panel with mDNS + manual-add, and dependency updater.

Strictly additive — Android Devices module remains the primary device-management surface (registered-devices list, PIN unlock, power state, sleep/wake, screensaver, Projectivy backups). The Power Tools module is a thin wrapper: `AndroidPowerToolsPage.razor` is an iframe sourced at `{WsScrcpyService.BaseUrl}/` with `WsScrcpy.IsRunning` guard, and the module class itself declares no dependencies (they're already declared by AndroidDevices).

`MainLayout.razor`'s page-title switch needs a specific case for `/android-power-tools` that sits **above** the generic `path.StartsWith("android")` fallback — otherwise the breadcrumb shows "Android Devices" for what should be "Android Power Tools" (the prefix-match order is load-bearing).

All four ws-scrcpy-web modals — shell, list files, configure stream, connect — render inside the iframe's own document, so `showModal()`'s top-layer and backdrop are scoped to the iframe viewport. No JS-interop wiring; no library-bundle injection into Blazor; no cross-origin CORS concerns (ws-scrcpy-web serves both the bundle and the embedded page itself, same origin).

---

## 5. Jellyfin Module

The Jellyfin module manages a Jellyfin media server running in Docker. It handles container lifecycle, database operations, automated backups, and a long-running cast & crew image update worker.

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
| `CleanupOldBackupsAsync(logger)` | Remove backups exceeding retention count |
| `ParseComposeFileAsync()` | Extract container info from docker-compose.yml |
| `GetPersonsMissingImagesAsync()` | Query Jellyfin API for persons without images |
| `TriggerPersonImageDownloadAsync(id, config)` | Refresh a single person's images via API |
| `GetApiConfigAsync()` | Resolve Jellyfin API base URL, API key, and user ID |

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

`OperationLogger` writes timestamped log files to `jellyfin-data/logging/`. Each operation creates a new file named `{operation}_{yyyyMMdd_HHmmss}.log`.

Log levels: `START`, `STEP`, `OK`, `FAIL`, `DONE`

The logger respects the `app-timezone` setting for timestamp display. It also provides static methods:
- `GetRecentLogs(count)` -- Returns the N most recent log entries with status inference (reads last line for DONE/FAIL markers)
- `GetLogDirectory()` -- Returns `{BaseDirectory}/jellyfin-data/logging/`
- `GetBackupDirectory()` -- Returns `{BaseDirectory}/jellyfin-data/backups/`, creates if missing

### Pages

- **DatabaseUpdate** (`/jellyfin/db-update`) -- DateCreated update with backup, container stop/start, operation logging
- **CastCrewUpdate** (`/jellyfin/cast-crew`) -- Start/cancel cast & crew worker, progress tracking, job history

---

## 6. Utilities Module

The Utilities module provides standalone tools that do not depend on external services.

### IconConversionService

Converts images to ICO format using SkiaSharp. Supports PNG, JPG, BMP, GIF, WEBP, and TIFF input.

Default output sizes: 64x64, 128x128, 256x256.

Two entry points:
- `ConvertToIcoAsync(sourcePath, targetPath, sizes)` -- File-to-file conversion
- `ConvertToIcoBytesAsync(sourceBytes, sizes)` -- In-memory conversion (used with File System Access API)

The ICO file format is written manually using `BinaryWriter`:
1. ICONDIR header (6 bytes: reserved, type=1, count)
2. ICONDIRENTRY array (16 bytes each: width, height, palette, reserved, planes=1, bpp=32, size, offset)
3. PNG-encoded image data for each size

Aspect ratio is preserved during resizing using Mitchell cubic resampling. Non-square images are centered with transparent padding.

### FileUnblockService

Windows-only. Removes Zone.Identifier alternate data streams (the "downloaded from the internet" marker) from all files in a directory tree.

Implementation:
1. Check `OperatingSystem.IsWindows()` -- returns `IsSupported = false` on other platforms
2. Run PowerShell: enumerate files with Zone.Identifier ADS, count them, then `Unblock-File`
3. Return `UnblockResult(Success, FileCount, ErrorMessage)`

The PowerShell command counts blocked files before unblocking because `Unblock-File` has no `-PassThru` option.

### Pages

- **IconConverter** (`/utilities/icon-converter`) -- Drag-and-drop or File System Access API picker, live preview, download via browser
- **FileUnblocker** (`/utilities/file-unblocker`) -- Directory path input, one-click unblock, result count

---

## 7. Cameras Module

The Cameras module provides CCTV camera viewing for LTS/Hikvision cameras via [go2rtc](https://github.com/AlexxIT/go2rtc). go2rtc converts RTSP streams into browser-playable formats (MP4/WebRTC), eliminating the need for camera-specific web UI proxying or native plugins.

### Architecture

```
Camera (RTSP :554) --> go2rtc (localhost:1984) --> Browser (iframe MP4 stream)
```

go2rtc runs as a managed child process alongside the app. `Go2RtcService` generates a `go2rtc.yaml` config file from the camera settings, spawns the process, and monitors its health. Each camera view page embeds an iframe pointing at `http://localhost:1984/api/stream.mp4?src=camera-{N}`.

### CameraConfig

```csharp
public record CameraConfig(int Index, string Name, string IpAddress, int Port = 554);
```

A lightweight record representing a single camera's non-secret configuration. The `Index` is the camera's position (1-based) within the user's configured camera count.

### CameraService

`CameraService` implements `ICameraService` and uses `IConfigurationService` for all persistence. Camera settings are scoped to `moduleId = "cameras"`.

Settings per camera:
| Key Pattern | Type | Storage |
|-------------|------|---------|
| `camera-count` | int | Plain setting (default: 8) |
| `camera-{index}-name` | string | Plain setting |
| `camera-{index}-ip` | string | Plain setting |
| `camera-{index}-port` | int | Plain setting (default: 554) |
| `camera-{index}-username` | string | Secret (encrypted) |
| `camera-{index}-password` | string | Secret (encrypted) |

Key methods:
| Method | Purpose |
|--------|---------|
| `GetCameraCountAsync()` | Read `camera-count` setting, default 8 |
| `SetCameraCountAsync(count)` | Write `camera-count` setting |
| `GetCameraConfigAsync(index)` | Load name, IP, port for one camera |
| `GetAllCameraConfigsAsync()` | Load configs for all configured cameras |
| `SaveCameraConfigAsync(config)` | Write name, IP, port for one camera |
| `GetCredentialsAsync(index)` | Read username/password secrets |
| `SaveCredentialsAsync(index, user, pass)` | Write username/password secrets |
| `GetConfiguredCamerasAsync()` | Returns cameras with both name and IP set |

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
  camera-1: rtsp://admin:password@192.168.86.x:554
  camera-2: rtsp://admin:password@192.168.86.y:554
api:
  listen: ":1984"
```

**Crash recovery**: If go2rtc exits unexpectedly, the service restarts it up to 2 times within a 30-second window before giving up.

**Binary resolution** (`FindExecutable`): Checks the local dependency install path first (`dep-path-go2rtc` setting or default `dependencies/go2rtc/`), then falls back to system PATH.

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
- `GetNavEntries()`: Dynamically generates one `NavEntry` per configured camera using user-defined names (falls back to "Camera N" if unnamed). Camera count and names are preloaded into static properties at startup by `Program.cs` and updated when settings are saved.

Uses `FindDepsRoot()` to resolve the absolute path to the `dependencies/` folder, consistent with other modules.

### Pages

- **CameraView** (`/cameras/{Index:int}`) -- Embeds an iframe to `http://localhost:1984/api/stream.mp4?src=camera-{Index}`. Shows status messages when the camera is not configured or go2rtc is not running.

### Settings UI

- **CameraSettings** (Settings > Cameras tab) -- Configurable camera count with per-camera name, IP, port, username, and password fields. Saving triggers `RegenerateConfigAsync()` to update go2rtc with new camera URLs.

### Dependency Updates

go2rtc is auto-installable via the dependency manager. During updates, `DependencyManagerService` calls `IGo2RtcService.StopAsync()` before swapping the binary and `Restart()` after, preventing file lock conflicts. This mirrors the existing pattern for ADB/ws-scrcpy-web updates.

### Tests

- `CameraServiceTests` (8 tests) -- CRUD operations, credential storage, camera count management
- `CamerasModuleTests` (5 tests) -- Module metadata, dynamic nav entry generation

---

## 8. Core Services

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

Data Protection keys are persisted to `%LOCALAPPDATA%/ControlMenu/keys/` and scoped to application name `"ControlMenu"`. This means encrypted settings survive app restarts but are tied to the Windows user account.

### DependencyManagerService

Manages the lifecycle of external tool dependencies: version checking, downloading, extracting, and installing.

**Sync on startup**: `SyncDependenciesAsync()` runs during app initialization. It iterates all modules' declared dependencies, upserts corresponding `Dependency` entities in the database, removes orphaned entries, and refreshes installed versions.

**Version checking** supports three strategies:
- `UpdateSourceType.GitHub` -- Fetches `GET /repos/{owner}/{repo}/releases/latest` from GitHub API, extracts `tag_name`
- `UpdateSourceType.DirectUrl` -- Scrapes a web page using `VersionCheckUrl` and `VersionCheckPattern` regex
- `UpdateSourceType.Manual` -- Always reports `UpToDate` (user manages these externally)

**Download and install** (`DownloadAndInstallAsync`):
1. Download the asset to a temp directory
2. Extract (ZIP via `System.IO.Compression`, tar.gz via `tar` command)
3. Verify the extracted binary by running the version command
4. Stop dependent services if needed (ws-scrcpy-web and ADB server when updating `adb`; go2rtc when updating `go2rtc`)
5. Backup existing files (`.bak` suffix)
6. Copy new files into the install path
7. Update the database entity
8. Restart dependent services

**Installed version detection** prioritizes the local install path over the system PATH. This prevents a system-installed older version from masking the managed version and causing an update loop.

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

Resolves device IPs from MAC addresses using the system ARP table.

- `GetArpTableAsync()` -- Runs `arp -a` and parses output (handles both Windows and Linux formats via source-generated regexes)
- `ResolveIpFromMacAsync(mac)` -- Looks up a normalized MAC in the ARP table
- `PingAsync(ip)` -- Single ping with 2-second timeout (platform-aware arguments)
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

## 9. Database Schema

### Connection

SQLite file: `controlmenu.db` in the project directory. Connection string in `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=controlmenu.db"
  }
}
```

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

## 10. Setup Wizard

The setup wizard runs on first launch (when the `setup-completed` setting is absent). It has seven steps, each a separate Razor component under `Components/Pages/Setup/`:

| Step | Component | Purpose |
|------|-----------|---------|
| 1 | WizardWelcome | Introduction |
| 2 | WizardDevices | Add Android devices, run network discovery for MAC-to-IP resolution |
| 3 | WizardCameras | Configure CCTV cameras with collapsible per-camera slots |
| 4 | WizardJellyfin | Configure Jellyfin docker-compose path, validate compose file |
| 5 | WizardEmail | Configure SMTP settings, send test email |
| 6 | WizardDependencies | Scan for installed tools, install missing ones |
| 7 | WizardDone | Summary, sets `setup-completed` setting |

The `WizardStepper.razor` component provides the step indicator and navigation.

During the Android Devices step, `NetworkDiscoveryService.GetArpTableAsync()` is used to resolve device IPs from MAC addresses. This populates `Device.LastKnownIp` for ADB connections.

During the Cameras step, users configure camera count, names, IPs, ports, and credentials. Camera slots are collapsible to keep the UI manageable when many cameras are configured.

---

## 11. Settings Architecture

### Settings Page

`SettingsPage.razor` uses a tabbed layout with five sections:

| Tab | Component | Key Settings |
|-----|-----------|-------------|
| General | GeneralSettings | `smtp-server`, `smtp-port`, `smtp-username`, `smtp-password` (secret), `smtp-from-email`, `notification-email`, `app-timezone` |
| Devices | DeviceManagement | Device CRUD, `ws_scrcpy_web_path` (module: `android-devices`) |
| Cameras | CameraSettings | `camera-count`, per-camera name/IP/port, per-camera username/password (secrets) (module: `cameras`) |
| Jellyfin | JellyfinSettingsSection | `jellyfin-compose-path`, `jellyfin-api-key` (secret), `jellyfin-url`, `jellyfin-user-id` |
| Dependencies | DependencyManagement | Per-dependency install paths (`dep-path-{name}`), version check, install/update buttons, check interval (`dep-check-interval`) |

### Module-Scoped vs. Global Settings

Settings with a non-null `ModuleId` are scoped to that module. For example, `ws_scrcpy_web_path` with `ModuleId = "android-devices"` is an Android Devices module setting. SMTP settings have `ModuleId = null` (global).

### Secret Management

Settings marked as secrets go through `ConfigurationService.SetSecretAsync` which:
1. Calls `SecretStore.Encrypt(value)` to produce a DPAPI-protected ciphertext
2. Stores the ciphertext in `Setting.Value` with `IsSecret = true`
3. On read, `GetSecretAsync` checks `IsSecret` and decrypts transparently

The DPAPI keys in `%LOCALAPPDATA%/ControlMenu/keys/` must be preserved when migrating between machines. Loss of these keys means all encrypted settings become unreadable.

---

## 12. Build and Deployment

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

### PATH Injection

On startup, `Program.cs` prepends all subdirectories of `dependencies/` to the `PATH` environment variable. This allows the app to find managed tools (adb, scrcpy, node, sqlite3) without requiring system-wide installation:

```csharp
var depsRoot = Path.Combine(builder.Environment.ContentRootPath, "dependencies");
if (Directory.Exists(depsRoot))
{
    var depPaths = Directory.GetDirectories(depsRoot)
        .Where(d => !Path.GetFileName(d).StartsWith('.'));
    var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
    var newPath = string.Join(Path.PathSeparator, depPaths) + Path.PathSeparator + currentPath;
    Environment.SetEnvironmentVariable("PATH", newPath);
}
```

### External Requirements

- No external database server (SQLite is embedded)
- Docker is required only for Jellyfin module functionality
- All other dependencies are auto-managed in the `dependencies/` folder

### Data Locations

| Item | Path |
|------|------|
| SQLite database | `controlmenu.db` (project root) |
| DPAPI encryption keys | `%LOCALAPPDATA%/ControlMenu/keys/` |
| Managed dependencies | `dependencies/` (project root) |
| Jellyfin operation logs | `jellyfin-data/logging/` (relative to base directory) |
| Jellyfin backups | `jellyfin-data/backups/` (relative to base directory) |

---

## 13. Testing

### Framework

- **xUnit** -- test runner
- **Moq** -- mocking framework
- 143 tests across services, modules, and data layer

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
    AndroidDevices/               # AdbService tests
    Cameras/                      # CameraService, CamerasModule, CameraProxyMiddleware tests
    Jellyfin/                     # JellyfinService, ComposeParser, CastCrewUpdateWorker tests
    Utilities/                    # IconConversionService, FileUnblockService tests
    Fakes/                        # Test doubles
    ModuleDiscoveryServiceTests.cs
```

### Running Tests

```bash
dotnet test
```

All tests run in-process with no external dependencies. ADB, Docker, and other external tools are mocked via `ICommandExecutor`.

---

## 14. Known Issues and Fixes

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

### ws-scrcpy-web Orphan Process

**Problem**: If the app crashes or is force-killed, the ws-scrcpy-web Node process remains running and holds port 8000, preventing restart.

**Fix**: `WsScrcpyService.StartAsync` calls `KillOrphanOnPortAsync` before spawning a new process. This checks if port 8000 is in use, finds the PID via `netstat -ano` (Windows) or `lsof -t -i :8000` (Linux), and kills the orphan process tree.

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
