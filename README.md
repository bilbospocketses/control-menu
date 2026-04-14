<p align="center">
  <img src="src/ControlMenu/wwwroot/icon-512.png" alt="Control Menu" width="128" height="128" />
</p>

<h1 align="center">Control Menu</h1>

<p align="center">
  A web-based tool for managing Android devices, Jellyfin media server, and system utilities from one place.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet" alt=".NET 9" />
  <img src="https://img.shields.io/badge/Blazor-Server-512BD4?logo=blazor" alt="Blazor Server" />
  <img src="https://img.shields.io/badge/SQLite-EF_Core-003B57?logo=sqlite" alt="SQLite" />
</p>

---

## What It Does

Control Menu replaces a collection of PowerShell scripts with a cross-platform web UI. It manages:

- **Android Devices** &mdash; Connect, reboot, toggle power/screensaver, manage ADB settings, and screen mirror Google TVs and Pixel phones via [ws-scrcpy-web](https://github.com/ANG-DEVELOPERS/ws-scrcpy-web)
- **Jellyfin Media Server** &mdash; Database date updates, cast & crew image refresh (background worker with resume support), Docker container management, automated backups with retention
- **Utilities** &mdash; PNG-to-ICO icon conversion (via SkiaSharp), Windows Zone.Identifier file unblocker
- **Dependency Management** &mdash; Tracks tool versions (ADB, scrcpy, Docker, sqlite3), checks GitHub/direct URLs for updates, downloads and installs updates in-place

## Features

- **Modular architecture** &mdash; `IToolModule` interface with auto-discovery via reflection
- **First-run wizard** &mdash; Guided setup for devices, services, and dependencies
- **Dark/light theme** &mdash; OAO grey palette with system-aware toggle
- **Cross-platform** &mdash; `CommandExecutor` strategy pattern abstracts Windows vs Linux commands
- **Encrypted secrets** &mdash; ASP.NET Data Protection API for API keys and passwords
- **Background jobs** &mdash; Long-running tasks with progress tracking, cancellation, and resume
- **Self-contained dependencies** &mdash; Bundled tools folder with PATH injection at startup

## Quick Start

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js](https://nodejs.org/) (for ws-scrcpy-web screen mirroring, optional)
- [ADB / Platform Tools](https://developer.android.com/tools/releases/platform-tools) (for Android device management)
- [Docker](https://docs.docker.com/get-docker/) (for Jellyfin management)

### Run

```bash
cd src/ControlMenu
dotnet run
```

Open http://localhost:5159 in your browser. The first-run wizard will guide you through setup.

### Test

```bash
dotnet test
```

128 tests covering services, modules, and integrations.

## Architecture

```
src/ControlMenu/
  Components/           # Blazor pages and layouts
    Layout/             #   MainLayout, Sidebar, TopBar
    Pages/              #   Home, Settings, Setup Wizard
    Shared/             #   ScrcpyMirror component
  Data/                 # EF Core entities, enums, migrations
  Modules/              # Pluggable tool modules
    AndroidDevices/     #   ADB service, Google TV & Pixel dashboards
    Jellyfin/           #   Docker ops, DB updates, Cast/Crew worker
    Utilities/          #   Icon converter, File unblocker
  Services/             # Core services (config, secrets, jobs, dependencies)
  wwwroot/              # Static assets, CSS, theme
tests/ControlMenu.Tests/
```

### Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Blazor Server (not WASM) | Needs direct access to ADB, Docker, filesystem |
| `IDbContextFactory` | Prevents stale EF change tracker in long-lived Blazor circuits |
| `IServiceScopeFactory` for background work | Workers outlive the Blazor circuit that started them |
| SQLite | Single-file DB, no external database server needed |
| SkiaSharp for images | Cross-platform replacement for System.Drawing.Common |
| ws-scrcpy-web via iframe | Screen mirroring without native scrcpy binary dependency |

## Module System

Modules implement `IToolModule` and are discovered at startup:

```csharp
public interface IToolModule
{
    string Id { get; }
    string DisplayName { get; }
    string Icon { get; }
    int SortOrder { get; }
    IEnumerable<ModuleDependency> Dependencies { get; }
    IEnumerable<ConfigRequirement> ConfigRequirements { get; }
    IEnumerable<NavEntry> GetNavEntries();
    IEnumerable<BackgroundJobDefinition> GetBackgroundJobs();
}
```

Modules must have parameterless constructors for auto-discovery.

## License

Private project.
