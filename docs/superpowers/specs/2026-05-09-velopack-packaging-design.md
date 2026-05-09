# Velopack packaging — design spec

**Date:** 2026-05-09
**Status:** DRAFT (awaiting user review)
**Targets:** v1.0.1 (.NET 10 upgrade) + v1.1.0 (Velopack installer + auto-update + tray + service mode + signed releases)

---

## Context

Control Menu shipped v1.0.0 on 2026-05-07 as a source-only release (`dotnet run` from `src/ControlMenu/`). The active backlog item #6 (`todo_control_menu.md`) is Velopack installer packaging — turning Control Menu into a downloadable signed Setup.exe with auto-update, mirroring the architectural approach already battle-tested on `ws-scrcpy-web` through its v0.1.22 → v0.1.25-beta.3 chain.

This spec captures the full design for that work: the three-binary architecture, on-disk layout, phase plan, signing pipeline, and validation strategy.

## Approach choice

**Two-step shipment** (Approach B from brainstorming):

1. **`v1.0.1`** — `.NET 9 → .NET 10` upgrade alone, source-only release. De-risks the framework bump in isolation before tangling with Velopack hooks.
2. **`v1.1.0`** — Full Velopack stack on top of the .NET 10 baseline. Five sub-phases (Phase 1-5; Phase 0 was the v1.0.1 release).

Single Windows-only release. Cross-platform (.deb / .rpm / AppImage) deferred indefinitely; not in scope.

## Architecture overview

### Three-binary architecture

Mirrors `ws-scrcpy-web`'s Rust-launcher + Node-supervisor pattern, but both halves (and the tray helper) are .NET 10:

| Binary | Role | Lifetime |
|--------|------|----------|
| `ControlMenuLauncher.exe` | Velopack-managed supervisor: handles `--veloapp-*` hooks, applies install-root ACL grant, spawns `ControlMenu.exe` child, hosts tray **in user-launched mode only** (skipped in service mode per Session 0 limitation) | Lives in `current\` |
| `ControlMenu.exe` | Blazor Server + Kestrel web host, completely Velopack-unaware | Lives in `current\` |
| `ControlMenuTray.exe` | Separate tray helper for **service mode only**. Runs in user's interactive session via HKLM Run key. Communicates with web host via HTTP healthcheck and Servy CLI for service control | Lives in `current\`, but registered under HKLM Run so it relaunches at every user login, surviving Velopack updates |

**Why split-binary, not single-binary:** Service-mode under Servy hits real timing risks with single-binary. Velopack's swap step renames `current\` mid-flight (10-15s); if Servy restarts the Velopack stub during the swap window, it tries to launch `current\ControlMenu.exe` while `current\` is mid-rename and races. Split-binary mirrors `ws-scrcpy-web`'s proven pattern. Cost: one new tiny .NET console project + one tiny WinForms tray helper. Worth it for service-mode reliability.

**Why a separate tray helper, not just a flag in launcher:** Service runs as Local System in Session 0 — no interactive desktop. Tray icon needs an interactive session. `ws-scrcpy-web`'s solution (`launcher/src/tray.rs:7-11`): in service mode the launcher skips its own tray, and a separate `ws-scrcpy-web-tray.exe` registered under HKLM Run launches in the user's session at login.

### On-disk layout

| Path | Update behavior |
|------|-----------------|
| `C:\Program Files\ControlMenu\ControlMenu.exe` (Velopack stub → forwards to `current\ControlMenuLauncher.exe`) | Set once at install |
| `C:\Program Files\ControlMenu\Update.exe` | Velopack updater |
| `C:\Program Files\ControlMenu\current\ControlMenuLauncher.exe` | Wiped + replaced on every update |
| `C:\Program Files\ControlMenu\current\ControlMenu.exe` | Wiped + replaced on every update |
| `C:\Program Files\ControlMenu\current\ControlMenuTray.exe` | Wiped + replaced on every update |
| `C:\Program Files\ControlMenu\current\Servy.exe` | Wiped + replaced on every update (allows Servy version bumps via in-app update) |
| `C:\Program Files\ControlMenu\current\` (rest) | Self-contained .NET 10 runtime, Blazor static assets, SkiaSharp natives |
| `C:\Program Files\ControlMenu\packages\` | Velopack `*.nupkg` deltas, Velopack-managed |
| `C:\ProgramData\ControlMenu\config\controlmenu.db` | Survives updates |
| `C:\ProgramData\ControlMenu\config\app-config.json` | Survives updates |
| `C:\ProgramData\ControlMenu\dependencies\{platform-tools, scrcpy, sqlite3, go2rtc}\` | Survives updates (no Node — decoupled in v1.0.0 polish batch; README needs doc-drift fix) |
| `C:\ProgramData\ControlMenu\logs\` | Survives updates |
| `C:\ProgramData\ControlMenu\keys\` | Survives updates (ASP.NET Data Protection keys) |
| `C:\ProgramData\ControlMenu\jellyfin-backups\` | Survives updates (default Jellyfin DB backup location) |

### Path resolution

`IDataPathResolver` interface in `src/ControlMenu.Common/`:

```
IDataPathResolver
├── GetInstallRoot()        → C:\Program Files\ControlMenu
├── GetCurrentDir()         → C:\Program Files\ControlMenu\current
├── GetDataRoot()           → C:\ProgramData\ControlMenu
├── GetConfigDir()          → C:\ProgramData\ControlMenu\config
├── GetDbPath()             → C:\ProgramData\ControlMenu\config\controlmenu.db
├── GetAppConfigPath()      → C:\ProgramData\ControlMenu\config\app-config.json
├── GetDependenciesDir()    → C:\ProgramData\ControlMenu\dependencies
├── GetLogsDir()            → C:\ProgramData\ControlMenu\logs
├── GetKeysDir()            → C:\ProgramData\ControlMenu\keys
└── GetJellyfinBackupsDir() → C:\ProgramData\ControlMenu\jellyfin-backups
```

Two implementations selected at composition root via Velopack-mode detection (`..\..\Update.exe` exists?):

- `VelopackDataPathResolver` — roots at `C:\ProgramData\ControlMenu\`
- `DevDataPathResolver` — roots at `AppContext.BaseDirectory` (preserves `dotnet run` workflow)

All path consumers (`IConfigurationService`, `IDependencyPathResolver`, `OperationLogger`, `IJellyfinDirectoryResolver`, `AddDataProtection().PersistKeysToFileSystem(...)`) inject `IDataPathResolver` instead of computing paths from `BaseDirectory` directly.

### `app-config.json` schema

```json
{
  "installMode": "user" | "service",
  "webPort": 5159,
  "version": "1.1.0",
  "serviceName": "ControlMenu",
  "trayHelperPath": "C:\\Program Files\\ControlMenu\\current\\ControlMenuTray.exe"
}
```

Written by the install-as-service flow; read by launcher and tray helper.

### Velopack PerMachine lessons applied

From `feedback_velopack_permachine_lessons.md`:

- ✅ **Gotcha 1** (auto-apply default true) — `VelopackApp.Build().SetAutoApplyOnStartup(false).Run(args)` in launcher's `Program.cs`. Skipping causes UAC loops or post-apply re-fire loops.
- ✅ **Gotcha 2 + 3** (Program Files writability + MSI strips ACL) — launcher applies `Authenticated Users:Modify (OI)(CI)` via runas-elevated `icacls.exe` child on first non-hook startup. UAC dismissal logged + swallowed (app degrades to no-auto-update, doesn't block startup).
- ✅ **Gotcha 4** (`--veloapp-obsolete` undocumented) — catch-all hook handler in launcher exits cleanly on any unknown `--veloapp-*` flag.
- ✅ **Gotcha 5** (auto-prerelease excludes from `/releases/latest`) — release workflow defaults `prerelease=false` for ALL tag pushes including betas.
- ✅ **Gotcha 9** (daemon cwd inheritance) — adb / scrcpy / sqlite3 / go2rtc all live under `C:\ProgramData\ControlMenu\dependencies\` so they're outside `current\` by construction. Pre-apply hygiene (`adb kill-server` + `taskkill /F /IM adb.exe /T` + 250ms settle) before triggering Velopack apply.
- ✅ **Gotcha 10** (service-mode log location) — Velopack's own log stays at `C:\Windows\System32\config\systemprofile\AppData\Local\velopack\velopack_ControlMenu.log` in service mode (locator config doesn't redirect log path); documented in TECHNICAL_GUIDE.
- N/A **Gotcha 6** (restart-target argv0 for Node) — no Node entry point.
- N/A **Gotcha 7 + 8** (Job Object on Node child) — no Job Object pattern; launcher doesn't wrap child.
- N/A **Gotcha 11** (webpack mangles createRequire) — no webpack.

### Cross-session spawn

For the "Install as Service" flow's UAC-elevated child to spawn a tray helper in the user's session immediately (no logout required), port `launcher/src/user_session_spawn.rs` to `src/ControlMenu.Common/Win32/UserSessionSpawn.cs`:

- `EnableCrossSessionSpawnPrivileges()` — `AdjustTokenPrivileges` on `SE_TCB_NAME`, `SE_ASSIGNPRIMARYTOKEN_NAME`, `SE_INCREASE_QUOTA_NAME` (Servy/NSSM keep these present-but-disabled on the LocalSystem token)
- `FindActiveUserSessionId()` — `WTSEnumerateSessionsW` walking for `State == WTSActive` AND non-empty username, fallback to `WTSGetActiveConsoleSessionId` (handles RDP / Hyper-V Enhanced Session)
- `SpawnInActiveUserSession()` — `WTSQueryUserToken` + `CreateEnvironmentBlock` + `CreateProcessAsUserW` with `lpDesktop = "winsta0\\default"`

P/Invoke-heavy. Reused if any future flow needs to spawn in user session from elevated context.

## Phase plan

### Phase 0 — v1.0.1 (.NET 9 → .NET 10 upgrade)

**Branch:** `feature/dotnet-10-upgrade` off master.

**Steps:**

1. csproj `<TargetFramework>net9.0</TargetFramework>` → `net10.0` in `ControlMenu.csproj` and `ControlMenu.Tests.csproj`. Bump `<Version>` 1.0.0 → 1.0.1.
2. Package references: `Microsoft.EntityFrameworkCore.Design` and `.Sqlite` `Version="9.*"` → `"10.*"`. Verify SkiaSharp 3.119.2 .NET 10 compat.
3. `dotnet restore` + `dotnet build` clean. Address any breaking-change warnings.
4. `dotnet test` — expect 383/383 passing. Investigate any failures.
5. Manual smoke: `dotnet run` → http://localhost:5159 → click through every module's main page.
6. README prereq update (`.NET 9 SDK` → `.NET 10 SDK`); drop stale Node.js dep mention while we're touching docs.
7. TECHNICAL_GUIDE updates for any .NET 9 references.
8. CHANGELOG entry under `[1.0.1] - <date>`.
9. `do that thing` wrap-up → tag `v1.0.1` → GitHub Release.

**Validation gate:** manual smoke (step 5).

**Risk profile:** low. .NET 9→10 is an LTS bump with mostly internal changes. Risks: EF Core 10 internal type signatures, Blazor Server 10 quirks. Surfaces in step 4 or 5.

**Estimate:** 1 working session (~2-4 hours).

### Phase 1 — v1.1.0 Phase 1 (Velopack core + path migration)

**Goal:** First end-to-end Velopack install + auto-update works on a fresh Win11 VM. No tray, no Servy, no signing yet.

**Sub-deliverables:**
- New project skeletons in `ControlMenu.sln`: `src/ControlMenuLauncher/`, `src/ControlMenuTray/` (stub), `src/ControlMenu.Common/`
- `IDataPathResolver` + two implementations + DI wiring + first-run-migration heuristic (re-evaluate during impl whether ANY migration is appropriate; default to "fresh install" semantics)
- Refactor existing path consumers to inject the resolver
- Velopack hook dispatch + auto-apply disable in launcher
- Single-instance guard (`Global\ControlMenuLauncher.SingleInstance` mutex)
- Install-root ACL grant via runas-elevated icacls
- Child supervisor spawning `ControlMenu.exe`; exit-code-75 dispatch
- `VelopackUpdateService` in ControlMenu.exe with `CheckForUpdatesAsync` / `DownloadUpdateAsync` / `RequestApplyUpdate`
- "Check for updates" UI in `Settings → General`
- adb / scrcpy / sqlite3 / go2rtc spawn cwd-anchoring audit
- Pre-apply daemon hygiene
- `vpk.config` for `--instLocation PerMachine`, GitHub source feed
- Local pack workflow scripts

**Fresh-VM smoke #1:** Setup.exe installs to Program Files; ProgramData paths created; manual update flow works (no auto-relaunch yet — user manually relaunches via Start Menu post-update).

**Estimate:** 2-3 working sessions (~6-12 hours).

### Phase 2 — v1.1.0 Phase 2 (Tray icon + auto-launch + console hide)

**Goal:** Polished user-launched UX. Tray as kill switch, console hidden, browser auto-opens.

**Tray library:** built-in `System.Windows.Forms.NotifyIcon`. Zero external deps; sufficient for the 3-item menu (Open / Restart / Quit). Migration path to `H.NotifyIcon.WinForms` exists if future needs (toast notifications, longer tooltip, async commands) emerge.

**Sub-deliverables:**
- Tray code in `src/ControlMenu.Common/Tray/` shared library (used by launcher in Phase 2; reused by tray helper in Phase 3)
- Launcher gains `<UseWindowsForms>true</UseWindowsForms>` + `<OutputType>WinExe</OutputType>` (hides console window automatically)
- `[STAThread]` Main + tray on dedicated thread (matching `ws-scrcpy-web/launcher/src/tray.rs:31-39`)
- Tray menu actions wired
- Named pipe IPC for graceful shutdown (`\\.\pipe\ControlMenu.Shutdown` per-PID, ACL-restricted to current user)
- `.kestrel-ready` marker file written by ControlMenu.exe after Kestrel binds; launcher polls every 250ms with 30s timeout to fire browser launch
- "Don't auto-launch browser" preference in Settings → General

**Fresh-VM smoke #2:** No console window; tray appears; auto-launch browser; tray menu Open/Restart/Quit work; in-app update from Phase 1 baseline still works (manual relaunch).

**Estimate:** 1-2 working sessions (~4-8 hours).

### Phase 3 — v1.1.0 Phase 3 (Servy bundling + service mode + tray helper)

**Goal:** "Install as Service" button works end-to-end. Tray helper provides UI in user session despite Session 0 service. Apply-update is fully automated (no manual relaunch).

**Sub-deliverables:**
- `scripts/fetch-servy.ps1` build-time fetch from upstream Servy GitHub Releases (pinned version)
- `ControlMenuTray.exe` populated: WinForms host reusing `ControlMenu.Common/Tray/`, healthcheck loop, single-instance guard
- Service-mode detection in launcher (read `app-config.json`'s `installMode`, skip own tray if `service`)
- "Install as Service" button + flow in `Settings → General`:
  - Confirm dialog
  - Elevated child via ShellExecuteEx runas
  - `Servy install` with target = Velopack stub at `C:\Program Files\ControlMenu\ControlMenu.exe`, `--account LocalSystem`, `--startupType Auto`, `--recoveryAction restart --recoveryDelaySeconds 60`
  - HKLM Run key registration for `ControlMenuTray.exe`
  - Write `installMode="service"` to `app-config.json`
  - `Servy start ControlMenu`
  - Cross-session-spawn `ControlMenuTray.exe` into the user's session immediately (no logout needed)
- "Uninstall Service" flow (mirror)
- HTTP healthcheck endpoint `GET /api/internal/ping` on ControlMenu.exe (`127.0.0.1` only)
- Apply-update orchestration in service mode:
  - ControlMenu.exe pre-apply hygiene → exit 75
  - Launcher → Velopack apply with `restart: false` → exit non-zero
  - Update.exe finishes swap (~10-15s)
  - Servy restart-delay (60s) → restart Velopack stub → new launcher cycle
  - Tray helper healthcheck recovers
  - Total downtime: ~75-90s (60s Servy delay dominant)

**Fresh-VM smoke #3:** Install-as-service flow; tray helper appears immediately; reboot survival; apply-update auto-restarts cleanly; uninstall-service flow.

**Estimate:** 3-4 working sessions (~12-16 hours). Most cost: cross-session-spawn P/Invoke port + apply-update orchestration testing.

### Phase 4 — v1.1.0 Phase 4 (CI + Azure Trusted Signing)

**Goal:** Tag-triggered release pipeline. `git push origin v1.1.0` produces a signed Setup.exe with no SmartScreen friction.

**Pre-flight (user side, one-time):**
- Verify Azure Trusted Signing account provisioned (done as of late April/early May 2026)
- Create `Public Trust` certificate profile under the Trusted Signing account
- Configure Azure AD app with **OIDC federated credentials** trusting `repo:bilbospocketses/control-menu:ref:refs/tags/v*`
- Add GitHub Secrets: `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `SIGNING_ENDPOINT_URL`, `SIGNING_ACCOUNT_NAME`, `SIGNING_PROFILE_NAME` (no client-secret with OIDC)

**Workflow file: `.github/workflows/release.yml`**
- Trigger: push of `v*` tag
- Runner: `windows-latest` (Trusted Signing's signtool plugin is Windows-only)
- Permissions: `id-token: write` (OIDC federation), `contents: write` (release creation)
- Stages: checkout → setup-dotnet 10 → build + test → publish + Servy fetch → sign individual binaries (Azure/trusted-signing-action) → vpk pack → sign Setup.exe + Update.exe → softprops/action-gh-release with `prerelease=false`

**Service-principal client-secret fallback** documented for rollback if OIDC federation hits unexpected friction.

**Local pack workflow** (`scripts/local-release.ps1`) for unsigned Setup.exe iteration without burning Trusted Signing quota.

**Fresh-VM smoke #4:** Tag-triggered CI run completes; signed Setup.exe downloads; Right-click → Properties shows Microsoft Identity Verification Root Certificate Authority signature; Setup.exe runs **with no SmartScreen warning**; full Phase 3 flow regression passes.

**Doc refresh:** README "Installation" section; TECHNICAL_GUIDE for the CI workflow + secret rotation; `THIRD-PARTY-NOTICES.md` for Servy + Velopack attributions.

**Estimate:** 2-3 working sessions (~6-12 hours).

### Beta-tag convention

`v1.1.0-α.1`, `α.2`, … through validation. Once smoke #4 passes cleanly, tag `v1.1.0` (no `-α`). Per Gotcha 5 + workflow defaults, `prerelease=false` even for beta tags so Velopack's `/releases/latest` discovery doesn't break.

## Testing strategy

### Test layers

| Layer | What | Where |
|-------|------|-------|
| Unit (existing) | 383-test xUnit suite | `tests/ControlMenu.Tests/` |
| Unit (new, Phase 1) | `IDataPathResolver`, first-run-migration logic | `tests/ControlMenu.Tests/Common/DataPathResolverTests.cs` |
| Unit (new, Phase 1) | Velopack hook dispatcher (catch-all flag handling), single-instance mutex semantics, exit-code 75 detection | `tests/ControlMenuLauncher.Tests/` (new project) |
| Unit (new, Phase 2) | Tray menu actions, named-pipe shutdown handler validation | `tests/ControlMenu.Common.Tests/` (new project) |
| Unit (new, Phase 3) | `app-config.json` round-trip, install-mode detection, Servy CLI argument formatting | `tests/ControlMenuLauncher.Tests/` |
| Integration | None planned — Velopack swap, ACL grants, service install/start are external-system behavior; mocking creates false confidence | n/a |
| Manual fresh-VM smoke | Per-phase gate; full install + apply-update cycle on clean Win11 Hyper-V VM | Hyper-V snapshots |

Per `feedback_verify_install_on_fresh_vm.md`: "tests pass + CI green is NOT the ship gate; fresh-VM install + app-actually-starts is."

### VM provisioning

- Single `WIN11-CONTROL-MENU` Hyper-V VM (already in test inventory per `user_test_devices.md`)
- Baseline snapshot before smoke #1: clean Win11, no .NET runtime, no Visual C++ redists, default user account
- Roll back to baseline before each smoke gate
- Per-phase post-smoke snapshots ("post-smoke-1", "post-smoke-2", etc.) for upgrade-path testing

### Smoke-gate summary

| Phase | Smoke # | Verifies | Approx duration |
|-------|---------|----------|-----------------|
| 0 (v1.0.1) | n/a | `dotnet run` + click every page | 30-60 min |
| 1 | #1 | Setup.exe installs; ProgramData paths created; manual update flow works | 1-2 hours |
| 2 | #2 | No console; tray appears; auto-launch browser; tray menu actions | 1 hour |
| 3 | #3 | Install-as-service; service-mode tray; reboot survival; apply-update auto-restart; uninstall-service | 2-3 hours |
| 4 | #4 | Tag-triggered CI; signed Setup.exe; **no SmartScreen warning**; Phase 3 regression | 1-2 hours |

## Logging strategy

| Binary | Log file | Key events |
|--------|----------|------------|
| `ControlMenuLauncher.exe` | `<dataRoot>\logs\launcher.log` (rolling on startup) | Velopack hook dispatch, ACL grant attempts/results, child PID + exit code, single-instance acquisition, apply-update orchestration steps |
| `ControlMenu.exe` | `<dataRoot>\logs\controlmenu.log` (existing `OperationLogger`, just rooted at the new path) | Existing logging unchanged |
| `ControlMenuTray.exe` | `<dataRoot>\logs\tray.log` | Healthcheck loop transitions, tray menu invocations |
| Velopack itself | `%LOCALAPPDATA%\velopack\velopack_ControlMenu.log` (user-launched mode) OR `C:\Windows\System32\config\systemprofile\AppData\Local\velopack\velopack_ControlMenu.log` (service mode, per Gotcha 10) | Velopack apply staging, swap, restart attempts |

## Cross-cutting error handling

- Launcher: any unhandled exception → log to `launcher.log` with full stack → write `<dataRoot>\logs\launcher-crash-<timestamp>.log` snapshot → exit non-zero → Servy auto-restart picks up
- ControlMenu.exe: existing exception middleware in Blazor + global `AppDomain.CurrentDomain.UnhandledException` handler writes to `controlmenu.log`
- Tray helper: failed actions → balloon notification + log entry
- Velopack apply failure: launcher logs failure, retries once, surfaces in tray helper's badge state ("update failed — see logs" tray menu hint)

## Failure recovery per phase

| Phase | If smoke fails | Rollback strategy |
|-------|----------------|-------------------|
| 0 | Tests fail under .NET 10 | Stay on .NET 9 for v1.0.x; investigate breaking change before retry. Phase 0 ships nothing observable to users. |
| 1 | Setup.exe fails to install / app fails to start | Iterate locally on launcher / Velopack config / path resolver until smoke passes. Branch is local; no shipped state to roll back. |
| 2 | Tray doesn't show / auto-launch broken | Iterate on WinForms host + named-pipe IPC. Optional fallback: ship Phase 2 with console window visible if otherwise clean. |
| 3 | Service mode misbehaves | Tune Servy restart-delay timing OR disable "Install as Service" button entirely and ship v1.1.0 as user-launched-only (with tray + auto-launch from Phase 2). Service mode promoted to v1.2.0. ws-scrcpy-web took 24 betas to harden their service mode. |
| 4 | Signing pipeline broken | Push back the v1.1.0 tag; iterate on workflow YAML + Azure config. Local-pack escape hatch (unsigned Setup.exe) means we can hand a beta build to ourselves while debugging CI. |

## Estimates

| Phase | Working sessions | Hours |
|-------|------------------|-------|
| 0 | 1 | 2-4 |
| 1 | 2-3 | 6-12 |
| 2 | 1-2 | 4-8 |
| 3 | 3-4 | 12-16 |
| 4 | 2-3 | 6-12 |
| Buffer for inevitable smoke-cycle re-runs | 1-2 | 4-8 |
| **Total v1.0.1 + v1.1.0** | **10-15** | **~34-60** |

Roughly 3 weeks of working time across the full delivery.

## Risks called out for the implementation plan

- **Velopack version drift** — pin a specific Velopack release (npm `velopack` or `vpk` CLI version) to avoid surprises mid-development.
- **Servy CLI flag stability** — pin a specific Servy release; document in `THIRD-PARTY-NOTICES.md`.
- **Servy restart-delay tuning** — 60s is conservative; can be shortened with empirical evidence.
- **First-run migration heuristic** — re-evaluate during Phase 1 implementation whether ANY migration from dev mode → Velopack mode is appropriate. Default recommendation: NO migration, fresh install semantics. Dev users keep using `dotnet run`.
- **ASP.NET Data Protection key directory stability** — keys must land in their final directory before `AddDataProtection().PersistKeysToFileSystem()` runs.
- **Azure Trusted Signing reputation lag** — brand-new accounts may have a few days of "should be clean" before SmartScreen rep kicks in. Plan for first-week friction.
- **OIDC federation setup** — typically takes 2-3 attempts; have client-secret fallback ready.

## Out of scope

- Cross-platform (.deb / .rpm / AppImage / macOS) — explicitly deferred. May revisit in v2.0.0+.
- Beta channel via Velopack `--channel beta` — single stable channel for v1.1.0; can add later if release cadence requires.
- Toast notifications from tray — possible v1.2.0+ via H.NotifyIcon migration; not needed for v1.1.0.
- Service running as Local System with ACL-restricted secrets at rest — current ASP.NET Data Protection setup is acceptable for personal-tooling threat model. Hardening defer-able.
- Auto-launch on Windows boot in user-launched mode — not in scope; service mode already covers always-on.

## Cross-references

- `feedback_velopack_permachine_lessons.md` — 11 gotchas memory; primary reference for Velopack + PerMachine + Windows-MSI quirks
- `feedback_verify_install_on_fresh_vm.md` — fresh-VM-smoke ship-gate rule
- `feedback_no_low_priority_on_packaging_paths.md` — packaging-path findings are release-gating, not polish
- `feedback_windows_only_tray_pattern.md` — context on `Shell_NotifyIconW` direct usage in ws-scrcpy-web
- `reference_velopack_msi_deployment_tool.md` — `--msiDeploymentTool` is SCCM/Intune only; we stick with Setup.exe
- `archive/project_wsscrcpy_sp3_sizing.md` — original sizing context for ws-scrcpy-web's installer work
- `project_ws_scrcpy.md` — ws-scrcpy-web's full architecture record
- `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/*.rs` — canonical implementations to port to .NET (main.rs, hooks.rs, install_acl.rs, tray.rs, supervisor.rs, single_instance.rs, paths.rs, log.rs, user_session_spawn.rs)
