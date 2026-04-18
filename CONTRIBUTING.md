# Contributing to Control Menu

Thanks for your interest. This document covers the essentials for getting a development environment running, the code-style bar, and how to land changes.

## Prerequisites

- **.NET 9 SDK** — install from [dotnet.microsoft.com](https://dotnet.microsoft.com/download)
- **SQLite 3** — runtime library; auto-installed by the app's dependency manager at first run, or install manually
- **ADB** (Android Debug Bridge) — required for Android device management; auto-installed by the dependency manager
- **scrcpy** — required for the Android devices module; auto-installed
- **go2rtc** — required for the Cameras module (RTSP → WebRTC streaming); auto-installed
- **ws-scrcpy-web** — required for screen mirroring; separate repo at [bilbospocketses/ws-scrcpy-web](https://github.com/bilbospocketses/ws-scrcpy-web). Runs as a child process managed by Control Menu
- **A modern Chromium-based browser** (Chrome, Edge, Brave) for the Blazor Server UI. Firefox works but has reduced support for File System Access API in the Icon Converter

## Setup

```bash
git clone https://github.com/bilbospocketses/control-menu.git
cd control-menu
dotnet restore
dotnet build
dotnet run --project src/ControlMenu
```

The app listens on `http://localhost:5000` (or the port shown in console output). Open it in a browser and walk through the first-run wizard.

## Development Workflow

```bash
dotnet build                                                   # incremental build
dotnet run --project src/ControlMenu                           # run the app
dotnet test                                                    # run the full test suite
dotnet test --filter FullyQualifiedName~SomeTest               # single test
dotnet ef database update --project src/ControlMenu            # apply EF migrations
dotnet ef migrations add <Name> --project src/ControlMenu      # new migration
```

## Project Structure

```
src/ControlMenu/
├── Components/          Shared Blazor components and page layouts
├── Data/                EF Core DbContext, entities, migrations
├── Modules/             Tool modules (AndroidDevices, Jellyfin, Cameras, Utilities)
│   └── <Module>/
│       ├── Pages/       Module-specific Razor pages
│       ├── Services/    Module-specific services
│       └── <Module>Module.cs   IToolModule implementation
├── Services/            Cross-cutting services (DB, config, deps, notifications)
├── wwwroot/             Static assets
└── Program.cs           Startup, DI container, module discovery
tests/                   xUnit test project
docs/                    TECHNICAL_GUIDE.md, manual-test-checklist.md
```

## Module Architecture

Modules implement the `IToolModule` interface and are discovered at startup via reflection. Each module:

- Declares its menu entry (name, icon, sort order)
- Registers its own services via `ConfigureServices(IServiceCollection)`
- Runs its own initialization via `InitializeAsync()`
- Contributes Razor pages under `Modules/<Name>/Pages/`

To add a new module, create a folder under `src/ControlMenu/Modules/`, add a class implementing `IToolModule`, and it will be picked up automatically.

## Code Style

- **C# 12 / .NET 9** features welcome — pattern matching, records, file-scoped namespaces, primary constructors
- **Nullable reference types** enabled project-wide; don't suppress nullability warnings without a justifying comment
- **Razor**: use partial classes (`*.razor.cs`) for page logic longer than a handful of lines; keep the `.razor` file for markup + minimal `@code`
- **Dependency injection** for services; no static singletons unless genuinely required
- **No `Console.WriteLine`** — use the configured `ILogger<T>` through DI. Logs go to both console and file
- **No PowerShell string interpolation into shell commands** — use `CommandExecutor` with an argument list
- **SQLite access via EF Core** — no raw SQL unless there's a measured reason

## Tests

Tests use **xUnit** and live in `tests/`. Target coverage is the service layer — DB interactions, command execution, module discovery, network scanning. UI is smoke-tested manually via `docs/manual-test-checklist.md`.

Any PR that changes persistence, external command execution, or inter-service contracts MUST include or update a test.

## Specs and Plans

Larger features go through a spec → plan → implementation cycle:

- **Specs:** `docs/superpowers/specs/YYYY-MM-DD-<topic>-design.md`
- **Plans:** `docs/superpowers/plans/YYYY-MM-DD-<topic>.md`

Existing specs and plans in `docs/` are a useful read before proposing architectural changes. They're frozen snapshots — don't retroactively edit them.

## Commit Messages

Follow conventional-commit-style prefixes: `feat:`, `fix:`, `refactor:`, `docs:`, `style:`, `chore:`, `build:`, `test:`.

Keep the subject line short and imperative. Wrap the body at 72 columns. Reference issue numbers when applicable.

Do not include AI-generated attribution lines in commit messages.

## Pull Requests

- Keep PRs focused on one concern. Big refactors are easier to review as a series of small commits than one sprawling patch.
- Update `CHANGELOG.md` under `[Unreleased]` for any user-visible change.
- Update `docs/TECHNICAL_GUIDE.md` or `README.md` when behavior the user sees changes.
- Update `docs/manual-test-checklist.md` when adding or changing user-facing flows.

## Branch Strategy

`master` is the development branch. Maintainer commits directly; contributors submit PRs from forks.

## Reporting Bugs

Open an issue on GitHub with:

- Expected vs actual behavior
- OS (Windows 10/11, Linux distro + version), .NET runtime version
- Browser + version
- Relevant excerpt from the Control Menu logs (Settings → Logs, or `logs/` directory)
- If the bug is in a module's interaction with an external tool (ADB, scrcpy, Jellyfin, go2rtc, ws-scrcpy-web), include the version of that tool

## Reporting Security Issues

Do **not** file a public issue. See `SECURITY.md` for the private reporting flow.

## License

By contributing you agree your contributions are licensed under the project's GPL-3.0-only license.
