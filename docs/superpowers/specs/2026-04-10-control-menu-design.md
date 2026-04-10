# Control Menu — Design Specification

## Overview

Control Menu is a web-based graphical tool that replaces the existing PowerShell text-menu system (`ControlMenu.ps1`). It provides a centralized dashboard for managing Android devices, Jellyfin media server operations, and general utilities — with an extensible architecture designed for long-term growth.

**Current state:** A single PowerShell script with nested text menus, hardcoded device configs, plaintext secrets, and Windows-only operation.

**Target state:** A cross-platform (Windows + Linux) Blazor Server web application with modular architecture, encrypted secrets, persistent configuration, background job management, and dependency update tracking.

## Technology Stack

- **Framework:** ASP.NET Core + Blazor Server (.NET 9)
- **Database:** SQLite via Entity Framework Core / Microsoft.Data.Sqlite
- **Real-time UI:** SignalR (built into Blazor Server)
- **Secret encryption:** ASP.NET Data Protection API
- **Target platforms:** Windows, Linux
- **Access model:** Local-first (localhost), architected for future remote/server mode

## Architecture

Four layers, each with a clear responsibility boundary:

```
┌─────────────────────────────────────────────┐
│  Blazor Server UI                           │
│  - Sidebar navigation (auto-discovered)     │
│  - Theme switcher (dark/light/system)        │
│  - Real-time progress via SignalR            │
├─────────────────────────────────────────────┤
│  Module System (IToolModule interface)       │
│  - AndroidDevicesModule                      │
│  - JellyfinModule                            │
│  - UtilitiesModule                           │
│  - (future modules auto-discovered)          │
├─────────────────────────────────────────────┤
│  Core Services                               │
│  - CommandExecutor (platform-aware shell)    │
│  - ConfigurationService (device registry)    │
│  - SecretStore (Data Protection API)         │
│  - DependencyManager (version check/update)  │
│  - BackgroundJobService (worker launcher)    │
│  - NetworkDiscoveryService (ARP/ping)        │
├─────────────────────────────────────────────┤
│  Persistence                                 │
│  - SQLite (config, devices, jobs, deps)      │
│  - Worker processes (long-running tasks)     │
│  - Local file system (backups, logs)         │
└─────────────────────────────────────────────┘
```

**Key rules:**
- Modules never call external processes directly — they go through `CommandExecutor`
- The UI layer never talks to external processes — always through services
- All configuration comes from SQLite, never hardcoded
- Each module is self-contained and auto-discovered at startup

## Module System

### IToolModule Interface

```csharp
public interface IToolModule
{
    string Id { get; }                    // "android-devices", "jellyfin"
    string DisplayName { get; }           // "Android Devices", "Jellyfin"
    string Icon { get; }                  // CSS icon class or SVG
    int SortOrder { get; }                // Sidebar position

    IEnumerable<ModuleDependency> Dependencies { get; }
    IEnumerable<ConfigRequirement> ConfigRequirements { get; }
    IEnumerable<NavEntry> GetNavEntries();
    IEnumerable<BackgroundJobDefinition> GetBackgroundJobs();
}
```

### Module Folder Convention

```
Modules/
├── AndroidDevices/
│   ├── AndroidDevicesModule.cs          # IToolModule implementation
│   ├── Pages/
│   │   ├── DeviceSelector.razor
│   │   ├── GoogleTvDashboard.razor
│   │   ├── PixelDashboard.razor
│   │   └── DeviceSettings.razor
│   ├── Services/
│   │   └── AdbService.cs
│   └── Workers/
│       └── (none currently)
├── Jellyfin/
│   ├── JellyfinModule.cs
│   ├── Pages/
│   │   ├── DatabaseUpdate.razor
│   │   ├── CastCrewUpdate.razor
│   │   └── JellyfinSettings.razor
│   ├── Services/
│   │   └── JellyfinService.cs
│   └── Workers/
│       └── CastCrewUpdateWorker.cs
├── Utilities/
│   ├── UtilitiesModule.cs
│   ├── Pages/
│   │   ├── IconConverter.razor
│   │   └── FileUnblocker.razor
│   └── Services/
│       └── IconConversionService.cs
```

### Auto-Discovery

At startup, the app scans for all classes implementing `IToolModule` via reflection, registers their services in the DI container, builds the sidebar navigation, and validates their declared dependencies (showing warnings in the UI if any are missing).

Adding a new module requires only creating the folder structure and implementing `IToolModule`. No changes to core app code.

## Database Schema

### Devices Table

| Column | Type | Notes |
|--------|------|-------|
| Id | GUID | Primary key |
| Name | string | "Living Room Google TV" |
| Type | enum | GoogleTV, AndroidPhone, etc. |
| MacAddress | string | "b8-7b-d4-f3-ae-84" |
| SerialNumber | string? | Nullable, e.g., "47121FDAQ000WC" |
| LastKnownIp | string? | Auto-populated from ARP |
| AdbPort | int | Default 5555 |
| LastSeen | DateTime? | Last successful ping |
| ModuleId | string | "android-devices" |
| Metadata | JSON | Module-specific extras |

### Jobs Table

| Column | Type | Notes |
|--------|------|-------|
| Id | GUID | Primary key |
| ModuleId | string | "jellyfin" |
| JobType | string | "cast-crew-update" |
| Status | enum | Queued, Running, Completed, Failed, Cancelled |
| Progress | int? | 0-100 |
| ProgressMessage | string? | "Processing person 1,247 of 8,432" |
| ProcessId | int? | OS PID of the worker |
| CancellationRequested | bool | App sets true; worker polls this to exit gracefully |
| StartedAt | DateTime? | |
| CompletedAt | DateTime? | |
| ErrorMessage | string? | |
| ResultData | JSON | Job-specific output |

### Dependencies Table

| Column | Type | Notes |
|--------|------|-------|
| Id | GUID | Primary key |
| ModuleId | string | |
| Name | string | "adb", "scrcpy" |
| InstalledVersion | string? | |
| LatestKnownVersion | string? | |
| DownloadUrl | string? | Current working URL |
| ProjectHomeUrl | string? | Fallback for user |
| LastChecked | DateTime? | |
| Status | enum | UpToDate, UpdateAvailable, UrlInvalid, CheckFailed |
| SourceType | enum | GitHub, DirectUrl, Manual |

### Settings Table

| Column | Type | Notes |
|--------|------|-------|
| Id | GUID | Primary key |
| ModuleId | string? | Null for global settings |
| Key | string | "theme", "jellyfin-api-key" |
| Value | string | Plaintext or encrypted |
| IsSecret | bool | Whether Value is encrypted via Data Protection |

## Background Jobs & Worker Processes

### Short Operations (seconds to minutes)

Run inline via `CommandExecutor` within the Blazor app process. The UI shows a real-time step-by-step progress component:

Example — Jellyfin DB Date Update:
1. "Stopping Docker container..." (spinner)
2. "Creating database backup..." (spinner)
3. "Running SQL update..." (spinner)
4. "Starting Docker container..." (spinner)
5. "Cleaning up old backups..." (spinner)
6. "Complete" (green check)

Device reboot: progress indicator with "Waiting for device to come back online..." polling ping until reply.

### Long-Running Operations (hours to days)

Managed through separate worker processes:

1. User triggers the operation in the UI
2. App writes a job record (Status = Queued) to SQLite
3. App launches a standalone worker process, passing the job ID as argument
4. Worker reads job config from SQLite, sets Status = Running
5. Worker updates `Progress` and `ProgressMessage` in SQLite as it works
6. Blazor UI polls the Jobs table every ~5 seconds and updates the progress meter via SignalR
7. User can close the app — worker keeps running independently
8. User reopens app — reads the Running job from SQLite, checks if PID is still alive, resumes showing progress
9. Worker finishes — sets Status = Completed (or Failed), optionally sends email notification
10. If PID is dead but Status = Running, the app marks it as Failed: "Worker process terminated unexpectedly"

**Cancellation:** The app sets `CancellationRequested = true` on the job record. The worker polls this flag periodically (e.g., between batches of work) and exits gracefully, setting Status = Cancelled.

## Dependency Management & Updates

### Dependency Definition

Each module declares its dependencies in code via the `ModuleDependency` class. Static configuration (how to check versions, where the project lives, which GitHub repo to query) lives in code. Runtime state (installed version, latest known version, working download URL, check timestamps) lives in the Dependencies DB table. This split means modules define *how* to manage a dependency, while the DB tracks *what state it's in*.

```csharp
public class ModuleDependency
{
    string Name { get; }                  // "adb", "scrcpy"
    string ExecutableName { get; }        // Platform-aware: "adb.exe" / "adb"
    string VersionCommand { get; }        // "adb --version"
    string VersionPattern { get; }        // Regex to extract version

    UpdateSourceType SourceType { get; }  // GitHub, DirectUrl, Manual
    string GitHubRepo { get; }            // "Genymobile/scrcpy" (if GitHub)
    string DownloadUrl { get; }           // Direct URL (if DirectUrl)
    string ProjectHomeUrl { get; }        // Fallback for user navigation
    string AssetPattern { get; }          // Regex to match release asset

    string InstallPath { get; }           // Where files live
    string[] RelatedFiles { get; }        // DLLs and co-files to replace together
}
```

### Update Check Flow

1. On app startup and configurable interval (default: daily), check each dependency
2. GitHub sources: hit GitHub Releases API, compare latest tag to installed version
3. Direct URL sources: HTTP HEAD to validate URL, download and compare version
4. Results update the Dependencies table; UI shows notification badge ("N updates available")

### Update Download Flow

1. User clicks "Update" on a dependency
2. Download to temp directory
3. Extract/copy new files alongside old (e.g., `adb.exe.new`)
4. Verify the new binary runs (e.g., `adb.exe.new --version`)
5. Replace old files with new, keeping old as `.bak` for one cycle
6. Update `InstalledVersion` in database

### Stale URL Handling

- **HTTP redirect:** Prompt user: "Download URL for [name] has moved. Update to [new URL]?"
- **HTTP 404/error:** Warning dialog: "Download URL for [name] is no longer valid." User can enter new URL. Link to `ProjectHomeUrl` provided so user can find the new location manually.
- All URL changes persist to the Dependencies table.

## UI Layout

### Collapsible Sidebar Navigation

- Left sidebar with module categories that expand/collapse to show sub-items
- Sidebar can collapse to icon-only mode for more screen space
- Categories grouped by type: Devices, Services, Tools
- Settings pinned at sidebar bottom
- Each module's `GetNavEntries()` populates its section

### Top Status Bar

- Breadcrumb showing current location (e.g., "Android Devices > Living Room TV")
- Device status indicators: green (online), yellow (IP resolved, not responding), red (not found)
- Dependency update badge with count
- Theme toggle (dark/light/system)

### Main Content Area

- Context-sensitive content based on sidebar selection
- Action cards in a responsive grid layout for device dashboards
- Forms for settings pages
- Progress components for running operations
- Log output panels where appropriate

### Theme System

User-selectable: Dark, Light, or System (follows OS preference). Implemented via CSS custom properties, matching the approach used in Open Audio Orchestrator.

## Cross-Platform Strategy

The `CommandExecutor` service uses a strategy pattern — each command registers both a Windows and Linux implementation. Modules call platform-agnostic methods.

| Concern | Windows | Linux | Approach |
|---------|---------|-------|----------|
| Shell commands | PowerShell / cmd | bash | `CommandExecutor` abstracts per-platform |
| ADB / scrcpy | `.exe` in local folder | Package manager or local binary | Dependency system handles path detection |
| Docker CLI | `docker` | `docker` | Identical, no abstraction needed |
| SQLite access | EF Core / Microsoft.Data.Sqlite | Same | Native .NET, no PowerShell module |
| ARP lookup | `arp -a` | `arp -a` or `ip neigh` | Platform-specific parsing in `NetworkDiscoveryService` |
| File paths | Config-driven | Config-driven | All paths from SQLite, never hardcoded |
| Admin elevation | UAC | `sudo` / capabilities | Per-command via `CommandExecutor` when needed |
| File unblocking | `Unblock-File` | N/A | Module conditionally shows based on OS |

## Settings & First-Run Experience

### Settings Pages

- **General:** Theme, app port, dependency check interval, device discovery interval
- **Device Management:** Table of devices with Add/Edit/Remove, "Scan Network" button for ARP discovery
- **Module Settings:** Auto-registered per module via `ConfigRequirements` (Jellyfin API key, SMTP config, device PINs — all encrypted)
- **Dependency Management:** Table of all dependencies with version info, update actions, URL management

### First-Run Wizard

Triggers when SQLite database doesn't exist (first launch). All steps skippable:

1. **Welcome** — Brief introduction
2. **Add Devices** — Form to register devices
3. **Configure Services** — Jellyfin connection, SMTP for notifications
4. **Locate Dependencies** — Auto-scan PATH and common locations for adb, scrcpy, etc. Show findings, let user correct
5. **Done** — Summary with links to Settings for anything skipped

The wizard writes to the same Settings/Devices tables as the regular Settings UI — no special data handling.

## Feature Mapping: Current Script to New App

| Current Script Function | New App Location | Notes |
|------------------------|------------------|-------|
| Google TV Sub Menu | AndroidDevices module > GoogleTvDashboard.razor | Per-device dashboard with action cards |
| Pixel 9 Sub Menu | AndroidDevices module > PixelDashboard.razor | Per-device dashboard |
| Google TV device selection | AndroidDevices module > DeviceSelector.razor | Or sidebar sub-items |
| Jellyfin DB Date Update | Jellyfin module > DatabaseUpdate.razor | Step-by-step progress UI |
| Jellyfin Cast & Crew Update | Jellyfin module > CastCrewUpdate.razor + Worker | Long-running worker with progress |
| Image to ICO | Utilities module > IconConverter.razor | File picker + conversion |
| Unblock Files | Utilities module > FileUnblocker.razor | Folder picker + recursive unblock (Windows only) |
| ADB connect/disconnect | Core AdbService, managed by device lifecycle | Automatic on device selection |
| MAC → IP resolution | Core NetworkDiscoveryService | Background polling on interval |
| Exit cleanup (ADB disconnect) | App shutdown hook | Graceful disconnection on app stop |
| Hardcoded MAC addresses | Devices table in SQLite | Managed via Settings UI |
| Hardcoded API keys/passwords | Settings table, encrypted | Managed via Settings UI with masked fields |

## Future Extensibility

The module architecture supports adding new capabilities without modifying core code:

- **New device types:** Create a new module or extend AndroidDevices with new device Type enum values
- **Audiobookshelf:** New module with its own pages, services, and dependencies
- **Additional Jellyfin operations:** New pages within the existing Jellyfin module
- **Remote/server mode:** The Blazor Server architecture already serves over HTTP; enabling remote access is primarily a configuration and authentication concern
- **New tool categories:** Each new module is a self-contained folder implementing `IToolModule`
