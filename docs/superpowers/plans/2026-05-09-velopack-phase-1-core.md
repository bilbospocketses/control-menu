# Velopack Phase 1 — Core + Path Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** First end-to-end Velopack install + manual update works on a fresh Win11 VM. Three-binary skeleton (`ControlMenuLauncher.exe` + `ControlMenu.exe` + `ControlMenuTray.exe` stub) lands. State migrates from `AppContext.BaseDirectory` to `C:\ProgramData\ControlMenu\`. No tray UX yet (Phase 2), no Servy (Phase 3), no signing (Phase 4).

**Architecture:** Mirrors `ws-scrcpy-web`'s Rust-launcher / Node-supervisor split, but launcher and supervised child are both .NET 10. Launcher handles Velopack hooks + ACL grant + single-instance + child-process supervision. ControlMenu.exe is Velopack-unaware Blazor Server. `IDataPathResolver` abstracts ProgramData (Velopack mode) vs `BaseDirectory` (dev mode) selection at composition root.

**Tech Stack:** .NET 10, ASP.NET Core 10 (Blazor Server, EF Core 10, SQLite), Velopack NuGet (pin a specific version), `System.Threading.Mutex` for single-instance, `System.Diagnostics.Process` for child supervision and runas-elevated icacls, xUnit (existing 383-test suite + 2 new test projects).

**Spec reference:** `docs/superpowers/specs/2026-05-09-velopack-packaging-design.md` § "Phase 1 — v1.1.0 Phase 1 (Velopack core + path migration)" + § "Phase 1 sources (Velopack core + path migration)" + § "Mandatory plan-writing checklist".

**Phase 0 status:** SHIPPED 2026-05-09 (tag `v1.0.1` on origin; .NET 10 baseline). Phase 1 builds on master.

---

## Sources to port from

Per `feedback_legacy_port_section.md` SOP and the spec's mandatory plan-writing checklist (`docs/superpowers/specs/2026-05-09-velopack-packaging-design.md` § "Migration discipline"). Line ranges below were verified against `ws-scrcpy-web` HEAD `384c6fc` on 2026-05-09 at plan-writing time.

**Discipline rule:** every port task in this plan cites its legacy `<path>:<line-range>` in the task header AND includes the verbatim verification step:

> *"Diff your scaffold against `<legacy-path>:<line-range>`. For every difference, write a one-line rationale. If you can't justify a difference, change your scaffold to match legacy behavior."*

If you dispatch subagents to execute any of these tasks, every agent prompt MUST literally embed the legacy `<path>:<line>` string. Agents in isolation cannot recover context the dispatcher has but failed to give them.

| ws-scrcpy-web source (absolute) | Lines | Purpose | CM landing location | Phase 1 task |
|---|---|---|---|---|
| `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/paths.rs` | 1-172 (full file) | Path resolution helpers — install_root from current_exe, current/ derivation | `src/ControlMenu.Common/Paths/PathResolver.cs` | Task 2 |
| `C:/Users/jscha/source/repos/ws-scrcpy-web/common/src/config.rs` | 1-274 (full file) | `AppConfig` loader/writer; `data_root_from_env` (PROGRAMDATA-rooted on Windows); lenient + strict load entry points; `is_service_mode` helper | `src/ControlMenu.Common/Config/AppConfig.cs` | Task 3 |
| `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/log.rs` | 1-134 (full file) | Tagged logger + file rotation on startup; safe under hidden-console mode | `src/ControlMenu.Common/Logging/LauncherLogger.cs` | Task 4 |
| `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/single_instance.rs` | 1-220 (full file) | Named-mutex single-instance guard with elevated/non-elevated namespace split (Local\ scope) | `src/ControlMenuLauncher/SingleInstance.cs` | Task 7 |
| `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/install_acl.rs` | 1-170 (full file) | Install-root ACL grant via runas-elevated icacls (Velopack PerMachine Gotchas 2 + 3); writability sentinel-file probe; UAC dismissal swallowed | `src/ControlMenuLauncher/InstallAcl.cs` | Task 8 |
| `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/hooks.rs` | 1-607 (full file) | Velopack hook arg parser + handlers (`--veloapp-install` / `--veloapp-updated` / `--veloapp-uninstall` / `--veloapp-obsolete`) + catch-all for unknown `--veloapp-*` flags (Gotcha 4) | `src/ControlMenuLauncher/Hooks/VelopackHookDispatcher.cs` | Task 9 |
| `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/main.rs` | 45-56 + 68-75 + 77-108 + 110-133 + 135-156 + 198-204 (composition + ordering) | Launcher entry sequence: log start → argv log → hook dispatch → install_acl → single_instance → VelopackApp init (Gotcha 1) → supervisor::run | `src/ControlMenuLauncher/Program.cs` | Task 10 |
| `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/main.rs` | 156 (single line, verbatim ordering) | `VelopackApp::build().set_auto_apply_on_startup(false).run()` — MUST be first executable code on normal-launch branch (Gotcha 1 + SP3 P2 Contract 5) | same | Task 10 + 11 |

**Out of Phase 1 scope (Phase 2/3 sources, NOT to be touched in this plan):**
- `launcher/src/tray.rs` (83) + `common/src/tray.rs` (734) — Phase 2 (tray icon)
- `launcher/src/main.rs:158-196` — Phase 2 (tray spawn ordering)
- `launcher/src/supervisor.rs` (198) — Phase 3 expands; Phase 1 uses a stub
- `launcher/src/spawn.rs` (263) — Phase 3
- `launcher/src/elevated_runner.rs` (623) — Phase 3 (install-as-service flow)
- `launcher/src/user_session_spawn.rs` (446) — Phase 3 (cross-session spawn)
- `launcher/src/main.rs:18-43` (--print-active-session) — Phase 3 (service mode discovery)
- `launcher/src/main.rs:58-66` (elevated_runner dispatch) — Phase 3
- `launcher/src/job_object.rs` (180) — pattern reference only; CM does not adopt the Job Object pattern (no Node child)
- `tray/src/main.rs` (233) + `tray/src/single_instance.rs` (134) — Phase 3 (standalone tray helper)
- `common/src/control_marker.rs` (392) — possibly Phase 3 if HTTP-only proves insufficient

---

## Upstream verbatim transcripts (canonical — added 2026-05-10 per `feedback_legacy_port_section.md` update)

> **Why this section exists:** The legacy-port SOP was tightened on 2026-05-10. Plan-author's inline .NET code is now **presumed wrong** until proven correct against upstream byte-for-byte. The transcripts below are the canonical source of truth for tasks 2, 3, 4, 7, 8, 9, 10. Every port task's **Step 0** is now: produce a side-by-side byte-level diff between the `.NET` scaffold and the relevant upstream transcript section, surfacing every external-tool-call arg, constant, and API-shape difference. **Output of Step 0 is the diff itself**, not a "verified" summary.
>
> **What MUST match byte-for-byte:**
> - External-tool invocations: `icacls /grant *S-1-5-11:(OI)(CI)M /T /C /Q`, `taskkill /F /IM ...`, `adb kill-server`, `servy restart --name <NAME>`, etc.
> - Constants: mutex names, sentinel filenames, ACL SID strings, exit codes, hook flag literals (`--veloapp-install` etc.).
> - Win32 API arg packing: `ShellExecuteExW` field ordering, `CreateMutexW` flag values.
>
> **What MAY differ as deltas (must be visibly marked with reason comments):**
> - Result-type vs exception error model (Rust `anyhow::Result<T>` vs .NET throwing).
> - Owned-string vs span semantics, language-idiomatic naming.
> - Use of higher-level .NET primitives (e.g., `System.Threading.Mutex` wraps `CreateMutexW`).
>
> **Verified against ws-scrcpy-web HEAD:** `384c6fc fix(server): stage node-pty seed in dev mode so shell button enables` (2026-05-09).

### Plan-revision corrections — paraphrase bugs caught during the 2026-05-10 transcript audit

The plan's inline .NET examples in the original task bodies (above) contain THREE paraphrase divergences from upstream. The corrections below take precedence over those inline examples.

| Task | What plan-paraphrase has | What upstream actually has | Required fix / classification |
|---|---|---|---|
| Task 3 (`config.rs`) `IsServiceMode()` | Checks `InstallMode.Equals("service", IgnoreCase)` | `is_service_mode` checks `install_mode.as_deref().is_some_and(|m| m.ends_with("-service"))` — accepts `user-service` AND `system-service` | **Delta-with-reason, NOT paraphrase-bug.** CM spec § "app-config.json schema" defines a narrower vocabulary: `installMode: "user" \| "service"` — only two modes, not upstream's two service-suffix variants. `IsServiceMode()` should match CM's spec value: `InstallMode == "service"` (case-sensitive, matches the spec literal). Mirroring upstream's `EndsWith("-service")` literally would FAIL for CM's actual stored value `"service"`. The classification deltas: (a) CM has its own schema vocabulary, (b) the comparison shape changes accordingly. Document both as deltas with reason comments in `AppConfig.IsServiceMode`. **Drop the case-insensitive flavor from the plan's original code** — CM spec values are written by CM code (the install-as-service flow in Phase 3), so they're guaranteed lowercase exact-match; case-insensitive compare is unnecessary noise. |
| Task 3 (`config.rs`) extra fields | Plan's `AppConfig` includes `version`, `serviceName`, `trayHelperPath` | Upstream `AppConfig` has only `install_mode`, `first_run_complete`, `web_port` | **Delta — defer to Phase 3.** The extra fields come from the spec § "app-config.json schema" but are written by the install-as-service flow which lands in Phase 3. Phase 1 `AppConfig` should mirror upstream's 3-field shape. Adding fields now without writers for them is dead schema. Remove from the Phase 1 DTO; re-add in Phase 3 with their writers. |
| Task 3 (`config.rs`) `Save()` method | Plan invented a `Save(string path)` method | Upstream has NO save/write API in `config.rs` | **Delta — defer to Phase 3.** No Phase 1 caller writes the config; the install-as-service flow that needs it is Phase 3. Drop from Task 3. |
| Task 7 (`single_instance.rs`) mutex name | `Local\ControlMenuLauncher.SingleInstance.{User,Admin}` (DOT-separated) | `Local\WsScrcpyWeb-SingleInstance-{User,Admin}` (HYPHEN-separated; `single_instance.rs:161`) | **Delta-with-reason.** CM equivalent: `Local\ControlMenu-SingleInstance-{User,Admin}` (HYPHEN to mirror upstream's separator convention). Update test assertions accordingly. |
| Task 4 (`log.rs`) shape | Plan has `Init()`, `_writer` cache, rotation-on-startup to `.prev.log`, `Reset()` test seam | Upstream has ZERO state — every call opens, appends, closes; logs to BOTH file AND stderr; **no rotation**; path resolved each call via `log_path()` helper that probes `data_root_from_env() + "logs/launcher.log"` then falls back to `<exe_dir>/launcher.log` | **Paraphrase-bug — fix.** Drop `Init`/`_writer`/rotation/Reset entirely. Stateless append-on-each-call. Log to BOTH file AND `Console.Error` so dev runs see entries. Path resolution is per-call (not cached). Hand-rolled timestamp NOT needed in .NET (use `DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff")`) — that's an acceptable env delta. |

### Task 9 deliberate Phase 1 deltas (NOT paraphrase bugs — explicitly marked here)

Phase 1 hook handlers are noop+log because their full upstream behavior depends on Phase 3 deliverables. These are scope deltas, not paraphrase divergences. The implementer subagent for Task 9 must include a comment block citing this addendum's section title in the source code so future-you sees the deferred work explicitly.

| Hook | Upstream behavior (from `hooks.rs` transcript) | Phase 1 delta | Phase that lands the full behavior |
|---|---|---|---|
| `on_install` | Creates `<dataRoot>` if missing; runs `icacls` to grant Authenticated Users:Modify on data_root AND install_root; writes skeleton `config.json` (with `installMode:null, firstRunComplete:false, autoUpdate:true, updateCheckIntervalMinutes:60, channel:"stable", githubOwner:"...", webPort:8000`) — see `hooks.rs:155-214` | noop + log | data_root creation: Task 6 resolver bootstrap (already done by composition root). ACL grant: Task 8's `InstallAcl.EnsureWritable` (launcher-side, not hook-side, per spec § "Velopack PerMachine lessons applied"). Skeleton `app-config.json`: Phase 3 install-as-service flow. |
| `on_updated` | If service mode (per `is_service_mode` ends-with `-service`): `current/servy-cli.exe restart --name WsScrcpyWeb`; else noop. See `hooks.rs:312-319` | noop + log | Phase 3 (Servy bundled). |
| `on_uninstall` | If service mode: `current/servy-cli.exe stop --name WsScrcpyWeb` + `current/servy-cli.exe uninstall --name WsScrcpyWeb`; clears HKCU Run-key for tray; `taskkill /F /IM ws-scrcpy-web-tray.exe`. ALWAYS preserves user data (config / deps / logs). Always exits 0. See `hooks.rs:321-374` | noop + log | Phase 3 (Servy + standalone tray helper land together). |
| `on_obsolete` | Log + exit 0 unconditionally — must NOT block Velopack's `current\` swap. See `hooks.rs:130-139` | identical to upstream (log + exit 0) | (Phase 1 matches upstream — no delta.) |
| `on_unknown` (catch-all) | Log via `error` (loud-warn) + exit 0 to break Update.exe respawn loop (Gotcha 4). See `hooks.rs:141-153` | identical to upstream (log + exit 0) | (Phase 1 matches upstream — no delta.) |

### Verbatim transcripts of upstream Rust source

These are presented as the source-of-truth for byte-level diffing. Implementer subagents will be given the relevant transcript section in their prompt verbatim. Tests + scaffold .NET code must match these for external-tool calls, constants, and API shapes; deltas (Result→exception, owned string→span, etc.) must be marked with comments.

#### `launcher/src/paths.rs` — Task 2 source-of-truth

(Verified 2026-05-10. Full 172-line file. CM's `IDataPathResolver` is a different abstraction — Task 2 ports only the `resolve_install_root` derivation; the `Paths` struct's per-field semantics (`data_root`, `deps_path`, `restart_marker`, `old_node`) are CM-specific in `IDataPathResolver` per the spec § "Path resolution" table. The `restart_marker` and `old_node` fields are ws-scrcpy-web-specific (their restart-marker mechanism, their Node binary swap path) and have NO CM equivalent.)

```rust
// Canonical path resolution for the install layout.
//
// Production layout (Phase 1 of Program Files migration):
//   <installRoot>/                        (binaries; admin-write only after Phase 4)
//     ws-scrcpy-web.exe                   (Velopack stub)
//     Update.exe                          (Velopack updater)
//     current/                            (Velopack-managed; wiped on update)
//       ws-scrcpy-web-launcher.exe        <-- exe_dir
//       dist/, seed/, ...
//
//   <dataRoot>/                           (writable state; Authenticated Users:Modify)
//     config.json                         (was at install_root pre-Phase-1)
//     ws-scrcpy-web-launcher.log
//     dependencies/                       (DEPS_PATH target — was at install_root pre-Phase-1)
//
// On Windows, dataRoot defaults to %PROGRAMDATA%\WsScrcpyWeb. On non-Windows
// (Linux AppImage), dataRoot collapses to install_root for now — there is no
// migration target until/unless a Linux Program-Files-equivalent flow is
// designed. The DEPS_PATH env var continues to override deps_path absolutely
// when set (used by tests, shared-deps installs, and the service-install
// envVars block in ServiceApi.handleInstall).
//
// Dev layout (target/debug or target/release):
//   target/debug/ws-scrcpy-web-launcher.exe    <-- exe_dir
//   <project>/                                 <-- exe_dir.parent().parent()

use anyhow::{Context, Result};
use std::path::{Path, PathBuf};

pub struct Paths {
    pub install_root: PathBuf,
    /// Writable state root — `<PROGRAMDATA>\WsScrcpyWeb` on Windows,
    /// equal to `install_root` on non-Windows (no migration there).
    pub data_root: PathBuf,
    pub deps_path: PathBuf,
    pub restart_marker: PathBuf,
    pub old_node: PathBuf,
}

impl Paths {
    /// Compute paths from a known exe directory plus optional DEPS_PATH and
    /// PROGRAMDATA overrides. `deps_override` matches the resolution priority
    /// in `spawn::resolve_node`. `programdata_override` lets tests inject the
    /// Windows ProgramData path without mutating process env.
    ///
    /// On non-Windows hosts the `programdata_override` is ignored and
    /// `data_root` collapses to `install_root` — Phase 1 doesn't migrate
    /// Linux paths.
    pub fn compute(
        exe_dir: &Path,
        deps_override: Option<&str>,
        programdata_override: Option<&str>,
    ) -> Result<Self> {
        let install_root = exe_dir
            .parent()
            .context("exe_dir has no parent (cannot derive install_root)")?
            .to_path_buf();

        let data_root = if cfg!(windows) {
            common::config::data_root_for_windows(programdata_override)
        } else {
            install_root.clone()
        };

        let deps_path = match deps_override {
            Some(p) => PathBuf::from(p),
            None => data_root.join("dependencies"),
        };

        let restart_marker = data_root.join(".restart");
        let old_node = deps_path.join("node").join("node.exe.old");

        Ok(Self {
            install_root,
            data_root,
            deps_path,
            restart_marker,
            old_node,
        })
    }

    /// Compute paths from process state.
    pub fn from_env() -> Result<Self> {
        let exe = std::env::current_exe().context("could not determine current exe path")?;
        let exe_dir = exe
            .parent()
            .context("exe has no parent dir")?
            .to_path_buf();
        let deps_override = std::env::var("DEPS_PATH").ok();
        let programdata = std::env::var("PROGRAMDATA").ok();
        Self::compute(&exe_dir, deps_override.as_deref(), programdata.as_deref())
    }
}
```

(Test module omitted from transcript — tests are reference for CM test design but not byte-diffable.)

#### `common/src/config.rs` — Task 3 source-of-truth

(Verified 2026-05-10. Full 274-line file. **`is_service_mode` requires the `ends_with("-service")` check.** Upstream has NO save/write API — Phase 3 will need one in CM but that's a delta, not a port.)

```rust
//! Read-only view of `<installRoot>/config.json`.
//!
//! Mirrors only the fields the Rust binaries (launcher + tray helper) need.
//! The TS source of truth is `src/server/Config.ts`.
//!
//! Two load entry points:
//!   - [`AppConfig::load`] — lenient: missing/malformed -> default. Never logs.
//!     Callers that want logging on the fallback path use `load_strict`.
//!   - [`AppConfig::load_strict`] — strict: missing -> Err, malformed -> Err.

use serde::Deserialize;
use std::fmt;
use std::path::{Path, PathBuf};

/// Pure resolver for the writable-state root on Windows. Mirrors
/// `resolveDataRoot` in `src/server/Config.ts` (Phase 1 of the Program
/// Files migration). Returns `<programdata>\WsScrcpyWeb`. The TS side
/// returns null on non-Windows; callers needing the cross-platform
/// "data root or install root fallback" semantic should compose this
/// with their install-root knowledge.
pub fn data_root_for_windows(programdata: Option<&str>) -> PathBuf {
    let pd = programdata
        .filter(|s| !s.is_empty())
        .unwrap_or("C:\\ProgramData");
    PathBuf::from(pd).join("WsScrcpyWeb")
}

/// Convenience wrapper around [`data_root_for_windows`] that reads
/// `PROGRAMDATA` from the process env. Returns `Some` on Windows, `None`
/// elsewhere — non-Windows callers should fall back to install-root for
/// data-root semantics until/unless a Linux migration target is defined.
pub fn data_root_from_env() -> Option<PathBuf> {
    if cfg!(windows) {
        let pd = std::env::var("PROGRAMDATA").ok();
        Some(data_root_for_windows(pd.as_deref()))
    } else {
        None
    }
}

#[derive(Debug, Deserialize, Default, PartialEq, Eq)]
#[serde(default)]
pub struct AppConfig {
    #[serde(rename = "installMode")]
    pub install_mode: Option<String>,
    #[serde(rename = "firstRunComplete")]
    pub first_run_complete: bool,
    #[serde(rename = "webPort")]
    pub web_port: Option<u16>,
}

/// Errors from [`AppConfig::load_strict`]. Lenient [`AppConfig::load`] never
/// returns errors — it always falls back to [`AppConfig::default`].
#[derive(Debug)]
pub enum ConfigError {
    /// `config.json` not present at the expected path.
    Missing,
    /// I/O failure while reading the file.
    Io(std::io::Error),
    /// JSON parse failure.
    Json(serde_json::Error),
}

impl fmt::Display for ConfigError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            ConfigError::Missing => write!(f, "config.json not found"),
            ConfigError::Io(e) => write!(f, "config.json read failed: {e}"),
            ConfigError::Json(e) => write!(f, "config.json parse failed: {e}"),
        }
    }
}

impl std::error::Error for ConfigError {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        match self {
            ConfigError::Missing => None,
            ConfigError::Io(e) => Some(e),
            ConfigError::Json(e) => Some(e),
        }
    }
}

impl AppConfig {
    /// `installMode` ends in `-service` (i.e., service-mode install).
    pub fn is_service_mode(&self) -> bool {
        self.install_mode
            .as_deref()
            .is_some_and(|m| m.ends_with("-service"))
    }

    /// Strict load from a specific path. Missing file or parse error -> Err.
    pub fn load_strict_from(path: &Path) -> Result<Self, ConfigError> {
        if !path.exists() {
            return Err(ConfigError::Missing);
        }
        let text = std::fs::read_to_string(path).map_err(ConfigError::Io)?;
        serde_json::from_str::<AppConfig>(&text).map_err(ConfigError::Json)
    }

    /// Strict load from `<install_root>/config.json`.
    pub fn load_strict(install_root: &Path) -> Result<Self, ConfigError> {
        Self::load_strict_from(&install_root.join("config.json"))
    }

    /// Lenient load from a specific path. Missing or malformed -> default.
    /// Never logs; callers that want feedback on the fallback path should
    /// use [`AppConfig::load_strict_from`] and log themselves.
    pub fn load_from(path: &Path) -> Self {
        Self::load_strict_from(path).unwrap_or_default()
    }

    /// Lenient load from `<install_root>/config.json`.
    pub fn load(install_root: &Path) -> Self {
        Self::load_strict(install_root).unwrap_or_default()
    }
}
```

(Test module + test helpers omitted from transcript.)

#### `launcher/src/log.rs` — Task 4 source-of-truth

(Verified 2026-05-10. Full 134-line file. **No `Init`, no caching, no rotation.** Path resolution is per-call. Logs to BOTH file AND stderr.)

```rust
// Minimal file logger for the Rust launcher.
//
// Release builds use `windows_subsystem = "windows"` and have no attached
// console, so stderr/stdout from `eprintln!` is invisible. We always also
// write to a launcher log file so failures during install/update/run can
// be diagnosed.
//
// v0.1.24-beta.3: log lives under `<dataRoot>/logs/launcher.log` on
// Windows. Earlier builds (v0.1.0–v0.1.23) wrote to
// `<dataRoot>/ws-scrcpy-web-launcher.log` — we moved it under a
// `logs/` subfolder to colocate with `server.log` (same change) and
// keep dataRoot navigable. The migration is one-directional; we don't
// read the legacy path. Old launcher.log files in the dataRoot root
// are stale and can be deleted by hand. On non-Windows, falls back to
// `<exe_dir>/launcher.log` (legacy path, dev convenience).
//
// Every line is prefixed with a UTC timestamp in
// `YYYY-MM-DD HH:MM:SS.fff` format. Without this, an after-the-fact log
// review can't tell whether two adjacent entries were a few seconds apart
// or hours — which made the v0.1.6 service-mode debugging slower than it
// should have been.

use std::fs::{self, OpenOptions};
use std::io::Write;
use std::path::PathBuf;
use std::time::{SystemTime, UNIX_EPOCH};

fn log_path() -> Option<PathBuf> {
    if let Some(data_root) = common::config::data_root_from_env() {
        let logs_dir = data_root.join("logs");
        // Best-effort directory create — if we can't create it (e.g.
        // ACL not yet set on a fresh install), fall back to exe_dir
        // below so we still get *some* logging.
        let _ = fs::create_dir_all(&logs_dir);
        if logs_dir.exists() {
            return Some(logs_dir.join("launcher.log"));
        }
    }
    let exe = std::env::current_exe().ok()?;
    let dir = exe.parent()?;
    Some(dir.join("launcher.log"))
}

// (Format-timestamp helper + civil_from_days math omitted from transcript —
// .NET has DateTimeOffset built-in; the hand-rolled version is a Rust env
// delta, not load-bearing for the port.)

fn append(prefix: &str, msg: &str) {
    let ts = format_timestamp_utc(SystemTime::now());
    if let Some(path) = log_path() {
        if let Ok(mut f) = OpenOptions::new().create(true).append(true).open(&path) {
            let _ = writeln!(f, "{ts} [{prefix}] {msg}");
        }
    }
    eprintln!("{ts} [{prefix}] {msg}");
}

pub fn info(msg: &str) {
    append("INFO", msg);
}

pub fn error(msg: &str) {
    append("ERROR", msg);
}
```

**Critical shape for .NET port:**
- `LauncherLogger.Info(msg)` and `LauncherLogger.Error(msg)` are the only public surface.
- Each call: open-append-close on the resolved path; ALSO write to `Console.Error`.
- Path resolution per-call (no caching): `IDataPathResolver.GetLogsDir()`-rooted on Velopack mode, `<exeDir>/launcher.log` fallback.
- Line format: `YYYY-MM-DD HH:MM:SS.fff [LEVEL] message` (LEVEL is INFO or ERROR, no padding).

#### `launcher/src/single_instance.rs` — Task 7 source-of-truth

(Verified 2026-05-10. Full 220-line file. **Mutex base name uses HYPHEN suffixes**, not dots: `Local\WsScrcpyWeb-SingleInstance-{User,Admin}`. CM equivalent: `Local\ControlMenu-SingleInstance-{User,Admin}`.)

```rust
// Single-instance guard for the launcher.
// (... full prologue comment block, see upstream lines 1-36 ...)

#[cfg(windows)]
mod imp {
    use anyhow::Result;
    use std::ffi::OsStr;
    use std::os::windows::ffi::OsStrExt;

    fn to_wide(s: &str) -> Vec<u16> {
        OsStr::new(s).encode_wide().chain(std::iter::once(0)).collect()
    }

    pub fn is_elevated() -> bool {
        use windows::Win32::Foundation::CloseHandle;
        use windows::Win32::Foundation::HANDLE;
        use windows::Win32::Security::{
            GetTokenInformation, TOKEN_ELEVATION, TOKEN_QUERY, TokenElevation,
        };
        use windows::Win32::System::Threading::{GetCurrentProcess, OpenProcessToken};

        unsafe {
            let mut token: HANDLE = HANDLE::default();
            if OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &mut token).is_err() {
                return false;
            }
            let mut elevation = TOKEN_ELEVATION::default();
            let mut size = 0u32;
            let ok = GetTokenInformation(
                token,
                TokenElevation,
                Some(&mut elevation as *mut _ as *mut std::ffi::c_void),
                std::mem::size_of::<TOKEN_ELEVATION>() as u32,
                &mut size,
            );
            let _ = CloseHandle(token);
            if ok.is_err() {
                return false;
            }
            elevation.TokenIsElevated != 0
        }
    }

    pub struct InstanceGuard {
        handle: windows::Win32::Foundation::HANDLE,
    }

    impl Drop for InstanceGuard {
        fn drop(&mut self) {
            unsafe {
                let _ = windows::Win32::Foundation::CloseHandle(self.handle);
            }
        }
    }

    pub fn acquire(name: &str) -> Result<Option<InstanceGuard>> {
        use windows::Win32::Foundation::{ERROR_ALREADY_EXISTS, GetLastError};
        use windows::Win32::System::Threading::CreateMutexW;
        use windows::core::PCWSTR;

        let wide = to_wide(name);
        let handle = unsafe {
            CreateMutexW(None, false, PCWSTR::from_raw(wide.as_ptr()))?
        };
        // CreateMutexW returns a valid handle EVEN when the mutex
        // already existed; GetLastError tells us which case we're in.
        let last = unsafe { GetLastError() };
        if last == ERROR_ALREADY_EXISTS {
            unsafe {
                let _ = windows::Win32::Foundation::CloseHandle(handle);
            }
            return Ok(None);
        }
        Ok(Some(InstanceGuard { handle }))
    }
}

pub use imp::acquire;
#[allow(unused_imports)]
pub use imp::InstanceGuard;

const MUTEX_BASE: &str = r"Local\WsScrcpyWeb-SingleInstance";

pub fn current_mutex_name() -> String {
    let suffix = if imp::is_elevated() { "Admin" } else { "User" };
    format!("{MUTEX_BASE}-{suffix}")
}
```

**Critical shape for .NET port:**
- Mutex namespace: `Local\` (NOT `Global\`).
- Base name: `Local\ControlMenu-SingleInstance` (CM equivalent of `Local\WsScrcpyWeb-SingleInstance`).
- Suffixes: `-User` and `-Admin` (HYPHEN, not dot).
- Acquisition semantics: `Acquire(name)` returns `null` when the mutex already existed (signal: another instance running, exit cleanly with code 0). Returns a disposable handle when we ARE the first instance.
- Elevation detection: in Rust, `OpenProcessToken` + `GetTokenInformation(TokenElevation)`. In .NET, `WindowsIdentity.GetCurrent()` + `WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)` is the natural equivalent (env delta — explicit comment required noting the API surface differs).
- The .NET `System.Threading.Mutex(bool, string)` constructor wraps `CreateMutexW`; the named-mutex semantics with `Local\` prefix are honored.

#### `launcher/src/install_acl.rs` — Task 8 source-of-truth

(Verified 2026-05-10. Full 170-line file. **External-tool call MUST match byte-for-byte:** `icacls.exe "<install_root>" /grant *S-1-5-11:(OI)(CI)M /T /C /Q`.)

```rust
// Install-root ACL grant for the running user (Windows-only).
// (... full prologue comment block, see upstream lines 1-23 ...)

use anyhow::{Context, Result, bail};
use std::ffi::OsStr;
use std::os::windows::ffi::OsStrExt;
use std::path::Path;

use windows::Win32::Foundation::CloseHandle;
use windows::Win32::System::Threading::{GetExitCodeProcess, INFINITE, WaitForSingleObject};
use windows::Win32::UI::Shell::{SEE_MASK_NOCLOSEPROCESS, SHELLEXECUTEINFOW, ShellExecuteExW};
use windows::core::PCWSTR;

const SENTINEL_FILE_NAME: &str = ".ws-scrcpy-write-test";
const ACL_SID_AUTH_USERS: &str = "*S-1-5-11";

pub fn is_writable(path: &Path) -> bool {
    let test_path = path.join(SENTINEL_FILE_NAME);
    match std::fs::write(&test_path, b"") {
        Ok(()) => {
            let _ = std::fs::remove_file(&test_path);
            true
        }
        Err(_) => false,
    }
}

pub fn ensure_writable(install_root: &Path) -> Result<()> {
    if is_writable(install_root) {
        return Ok(());
    }

    crate::log::info(
        "install-root not writable to current user; \
         requesting elevation to grant Authenticated Users:Modify (one-time UAC)",
    );

    let exit_code = run_icacls_elevated(install_root)?;
    if exit_code != 0 {
        bail!("elevated icacls exited with code {exit_code}");
    }

    if !is_writable(install_root) {
        bail!("icacls reported success but install-root still not writable to running user");
    }

    crate::log::info(&format!(
        "install-root grant applied on {install_root:?}; in-app updater should now function"
    ));
    Ok(())
}

fn run_icacls_elevated(install_root: &Path) -> Result<i32> {
    let install_root_str = install_root.to_string_lossy();
    let parameters = format!(
        "\"{}\" /grant {}:(OI)(CI)M /T /C /Q",
        install_root_str, ACL_SID_AUTH_USERS
    );

    let verb = to_wide("runas");
    let file = to_wide("icacls.exe");
    let params = to_wide(&parameters);

    let mut info = SHELLEXECUTEINFOW {
        cbSize: std::mem::size_of::<SHELLEXECUTEINFOW>() as u32,
        fMask: SEE_MASK_NOCLOSEPROCESS,
        lpVerb: PCWSTR(verb.as_ptr()),
        lpFile: PCWSTR(file.as_ptr()),
        lpParameters: PCWSTR(params.as_ptr()),
        nShow: 0, // SW_HIDE — don't flash an icacls console window
        ..Default::default()
    };

    unsafe {
        ShellExecuteExW(&mut info)
            .context("ShellExecuteExW failed (UAC declined or admin not available?)")?;
    }

    if info.hProcess.is_invalid() {
        bail!("ShellExecuteExW returned no process handle");
    }

    let proc = info.hProcess;
    unsafe {
        WaitForSingleObject(proc, INFINITE);
        let mut code: u32 = 1;
        let result = GetExitCodeProcess(proc, &mut code);
        let _ = CloseHandle(proc);
        result.context("GetExitCodeProcess failed")?;
        Ok(code as i32)
    }
}
```

**Critical shape for .NET port:**
- Sentinel filename: `.controlmenu-write-test` (CM equivalent of `.ws-scrcpy-write-test`).
- ACL SID literal: `*S-1-5-11` (Authenticated Users; locale-independent). MUST match upstream byte-for-byte.
- Args string template: `"<install_root>" /grant *S-1-5-11:(OI)(CI)M /T /C /Q`. Quotes around the path. `(OI)(CI)M` flags. `/T /C /Q` flags. **Char-for-char.**
- Wait timeout: upstream uses `INFINITE`. The plan currently caps at 30s for .NET — that IS a delta requiring an explicit comment in the code (rationale: don't hang the launcher indefinitely if icacls is wedged). If you want strict parity, use no timeout (block until exit).
- `nShow: 0` = `SW_HIDE` — hides the icacls console window. .NET equivalent: `ProcessWindowStyle.Hidden` + `CreateNoWindow = true`.
- UAC dismissal in upstream: `bail!("ShellExecuteExW failed ...")` — upstream propagates the error; CM's plan instead catches it and degrades gracefully (logs, returns). That's a delta. Comment required.

#### `launcher/src/hooks.rs` — Task 9 source-of-truth

(Verified 2026-05-10. Full 607-line file.) Key sections — flag constants, parser, dispatcher entry, handler bodies. The full file is too long to inline; the implementer must read the live file at execution time AND be given this transcript section. Critical bytes excerpted below; the implementer's Step 0 MUST diff against the full live file.

```rust
const FLAG_INSTALL: &str = "--veloapp-install";
const FLAG_UPDATED: &str = "--veloapp-updated";
const FLAG_UNINSTALL: &str = "--veloapp-uninstall";
const FLAG_OBSOLETE: &str = "--veloapp-obsolete";
const FLAG_PREFIX: &str = "--veloapp-";

#[derive(Debug, PartialEq, Eq)]
pub enum HookKind {
    Install,
    Updated,
    Uninstall,
    Obsolete,
    Unknown(String),  // captured raw flag string
}

pub fn parse_hook_flag(args: &[String]) -> Option<HookKind> {
    let mut unknown: Option<String> = None;
    for a in args {
        match a.as_str() {
            FLAG_INSTALL => return Some(HookKind::Install),
            FLAG_UPDATED => return Some(HookKind::Updated),
            FLAG_UNINSTALL => return Some(HookKind::Uninstall),
            FLAG_OBSOLETE => return Some(HookKind::Obsolete),
            other if other.starts_with(FLAG_PREFIX) && unknown.is_none() => {
                unknown = Some(other.to_string());
            }
            _ => {}
        }
    }
    unknown.map(HookKind::Unknown)
}

pub fn handle_velopack_hook(args: &[String]) -> Option<i32> {
    let kind = parse_hook_flag(args)?;
    log::info(&format!("hook: dispatching {:?}", kind));

    let install_root = match resolve_install_root() { /* ... */ };
    let data_root = common::config::data_root_from_env().unwrap_or_else(|| install_root.clone());

    let code = match kind {
        HookKind::Install => on_install(&install_root, &data_root),
        HookKind::Updated => on_updated(&install_root, &data_root),
        HookKind::Uninstall => on_uninstall(&install_root, &data_root),
        HookKind::Obsolete => on_obsolete(),
        HookKind::Unknown(flag) => on_unknown(&flag),
    };
    Some(code)
}

fn on_obsolete() -> i32 {
    log::info("hook(obsolete): exiting cleanly so Update.exe can swap current\\");
    0
}

fn on_unknown(flag: &str) -> i32 {
    log::error(&format!(
        "hook: unknown velopack flag {flag:?} — exiting 0 to avoid Update.exe respawn loop. \
         Add a handler in launcher/src/hooks.rs and ship a fix."
    ));
    0
}

// on_install / on_updated / on_uninstall: see the "Phase 1 deliberate deltas" table above.
// Phase 1 noop+log for all three. Phase 3 will port the full bodies.
```

**Critical bytes for .NET port (parser + flag dispatch — Phase 1 must match these exactly):**
- Flag literals: `--veloapp-install`, `--veloapp-updated`, `--veloapp-uninstall`, `--veloapp-obsolete`. Prefix: `--veloapp-`. Char-for-char.
- Parser invariant: known flags take precedence over unknown. Even if `--veloapp-future` appears BEFORE `--veloapp-install` in argv, `Install` wins. (See upstream `parse_hook_flag` — known returns immediately; unknown is buffered and only emitted if no known flag is found.)
- `Unknown(String)` carries the raw flag text — log messages must include it.
- Catch-all (`on_unknown`): logs at ERROR level, returns 0. Goal: break Update.exe respawn loop. Char-for-char on log message intent ("exiting 0 to avoid Update.exe respawn loop").
- `on_obsolete`: log at INFO level, return 0 unconditionally. MUST NOT block swap.

#### `launcher/src/main.rs:18-156` + `:198-204` — Task 10 source-of-truth

(Verified 2026-05-10. Lines 18-156 cover entry → log start → argv log → elevated_runner shortcut [Phase 3, NOT Phase 1] → Velopack hook dispatch → install_acl → single_instance → VelopackApp init. Lines 198-204 cover supervisor::run.)

```rust
// Lines 45-56: log launcher start + full argv
log::info(&format!(
    "ws-scrcpy-web-launcher v{} starting",
    env!("CARGO_PKG_VERSION")
));
log::info(&format!("argv: {:?}", args));

// Lines 68-75: Velopack hook dispatch (BEFORE VelopackApp::build().run())
if let Some(code) = hooks::handle_velopack_hook(&args) {
    log::info(&format!("hook handler exiting with code {code}"));
    std::process::exit(code);
}

// Lines 77-108: install_acl ensure-writable
#[cfg(windows)]
{
    match resolve_install_root() {
        Ok(install_root) => {
            if let Err(e) = install_acl::ensure_writable(&install_root) {
                log::error(&format!(
                    "install-root ACL grant failed; in-app updater will be degraded: {e:#}"
                ));
            }
        }
        Err(e) => {
            log::error(&format!(
                "could not resolve install_root for ACL check: {e:#}"
            ));
        }
    }
}

// Lines 110-133: single-instance guard
let mutex_name = single_instance::current_mutex_name();
let _instance_guard = match single_instance::acquire(&mutex_name) {
    Ok(Some(guard)) => Some(guard),
    Ok(None) => {
        log::info("another ws-scrcpy-web-launcher instance is already running; exiting");
        std::process::exit(0);
    }
    Err(e) => {
        log::error(&format!(
            "could not acquire single-instance mutex (proceeding without guard): {e:#}"
        ));
        None
    }
};

// Lines 135-156: VelopackApp init (Gotcha 1)
// Per SP3 P2 Contract 5: VelopackApp::build().run() MUST be the first
// executable code path on the normal-launch branch.
velopack::VelopackApp::build().set_auto_apply_on_startup(false).run();

// Lines 198-204: supervisor::run (Phase 1 stub)
let exit_code = match supervisor::run() {
    Ok(code) => code,
    Err(e) => {
        log::error(&format!("launcher failed: {e:#}"));
        1
    }
};
```

**Critical ordering invariants for .NET port:**
1. Log "starting" + argv FIRST (so any failure has a paper trail).
2. Hook dispatch BEFORE Velopack init (catches unknown `--veloapp-*` before VelopackApp consumes them).
3. install_acl BEFORE single-instance (the elevated icacls invocation is short-lived and may legitimately race with a normally-running instance).
4. single-instance BEFORE VelopackApp init (VelopackApp may exit/restart the process; single-instance should govern that lifetime).
5. **VelopackApp.Build().SetAutoApplyOnStartup(false).Run(args)** — first executable code on normal-launch branch. Char-for-char on `SetAutoApplyOnStartup(false)`. Skipping or omitting causes the v0.1.22-style Update.exe respawn loop (Gotcha 1).
6. Supervisor run AFTER VelopackApp.Run.

**Phase 1 stubs explicitly NOT to add:**
- Lines 18-43 (`--print-active-session` shortcut) — Phase 3.
- Lines 58-66 (elevated_runner dispatch) — Phase 3.
- Lines 158-196 (tray spawn) — Phase 2.
- Lines 183-194 (`--local-takeover` override) — Phase 3.
- Lines 206-220 (Job Object release) — out of scope; CM does not adopt the Job Object pattern.

Leave comment markers in CM's `Program.cs` showing where each future block will land, so future port-diffs see the placeholders.

### Per-task workflow update — Step 0 is now a byte-level diff

For each port task (2, 3, 4, 7, 8, 9, 10), the workflow is:

**Step 0 (NEW — must run BEFORE any code is written):**
> Open the upstream transcript section above for this task. Open your in-progress .NET scaffold (or, on first pass, the plan's inline scaffold). Produce a side-by-side diff of:
> - Every external-tool invocation (`icacls`, `taskkill`, `adb`, etc.)
> - Every constant (mutex names, sentinel filenames, ACL SIDs, exit codes, hook flag literals)
> - Every Win32 API call shape
> - Every method signature on the public surface
>
> For each difference, classify it as **delta-with-reason** (env-specific, documented inline as a comment) or **paraphrase-bug** (unjustified divergence). Output of Step 0 is the diff + classification table itself, NOT a "looks right" summary.
>
> If any paraphrase-bug is found, fix the .NET scaffold to match upstream BEFORE proceeding to Step 1 (the failing test).

**Step 1+ (existing TDD flow):** failing test → minimal impl → tests pass → final diff verification.

The "Step 5: Diff against legacy" in the original task bodies is NOW redundant with Step 0 — collapse them. If Step 0 is run cleanly, no separate Step 5 diff is needed.

### When subagents dispatch this task

Each implementer subagent's prompt MUST include the relevant transcript section (paths.rs for Task 2, config.rs for Task 3, etc.) embedded verbatim — NOT a "read the addendum" instruction. The subagent has no session context and cannot trust pointers; the transcript itself goes in the prompt.

---

## File Structure

**Files created:**
- `src/ControlMenu.Common/ControlMenu.Common.csproj` — shared library, `net10.0`, no UI deps
- `src/ControlMenu.Common/Paths/PathResolver.cs` — install-root derivation from `Process.GetCurrentProcess().MainModule.FileName`
- `src/ControlMenu.Common/Config/AppConfig.cs` — DTO + loader (lenient + strict)
- `src/ControlMenu.Common/Config/AppConfigPaths.cs` — `data_root_from_env` equivalent
- `src/ControlMenu.Common/Logging/LauncherLogger.cs` — tagged logger with rotation-on-startup
- `src/ControlMenu.Common/Paths/IDataPathResolver.cs` — interface (8 path-getter methods)
- `src/ControlMenu.Common/Paths/VelopackDataPathResolver.cs` — implementation rooted at `C:\ProgramData\ControlMenu`
- `src/ControlMenu.Common/Paths/DevDataPathResolver.cs` — implementation rooted at `AppContext.BaseDirectory`
- `src/ControlMenu.Common/Paths/DataPathResolverFactory.cs` — composition-root selector (probes for `..\..\Update.exe`)
- `src/ControlMenuLauncher/ControlMenuLauncher.csproj` — `net10.0`, `<OutputType>Exe</OutputType>` for now (Phase 2 changes to WinExe)
- `src/ControlMenuLauncher/Program.cs` — launcher entry; mirrors `main.rs:18-156` ordering
- `src/ControlMenuLauncher/SingleInstance.cs` — named-mutex acquire/release + elevation detection
- `src/ControlMenuLauncher/InstallAcl.cs` — sentinel-file writability probe + runas-elevated icacls
- `src/ControlMenuLauncher/Hooks/VelopackHookDispatcher.cs` — flag parser + handlers + catch-all
- `src/ControlMenuLauncher/Supervisor/ChildSupervisor.cs` — Phase 1 stub: spawn `ControlMenu.exe`, wait for exit, dispatch on exit-75
- `src/ControlMenuTray/ControlMenuTray.csproj` — Phase 1 stub project (`Main` returns 0; Phase 2 fills in)
- `src/ControlMenuTray/Program.cs` — empty Main
- `src/ControlMenu/Services/IDataPathResolver.cs` — re-export shim if needed (avoid breaking existing namespace)
- `src/ControlMenu/Services/Update/VelopackUpdateService.cs` — `Check` / `Download` / `RequestApply` (writes exit-75 marker)
- `src/ControlMenu/Services/Update/IVelopackUpdateService.cs`
- `tests/ControlMenu.Common.Tests/ControlMenu.Common.Tests.csproj` — xUnit, references `ControlMenu.Common`
- `tests/ControlMenu.Common.Tests/Paths/PathResolverTests.cs`
- `tests/ControlMenu.Common.Tests/Config/AppConfigTests.cs`
- `tests/ControlMenu.Common.Tests/Logging/LauncherLoggerTests.cs`
- `tests/ControlMenu.Common.Tests/Paths/DataPathResolverTests.cs`
- `tests/ControlMenuLauncher.Tests/ControlMenuLauncher.Tests.csproj` — xUnit, references `ControlMenuLauncher`
- `tests/ControlMenuLauncher.Tests/SingleInstanceTests.cs`
- `tests/ControlMenuLauncher.Tests/Hooks/VelopackHookDispatcherTests.cs`
- `vpk.config` — `--instLocation PerMachine` + GitHub source feed config
- `scripts/local-pack.ps1` — dotnet publish + `vpk pack` orchestration
- `scripts/fresh-vm-smoke.md` — runbook for smoke #1

**Files modified:**
- `ControlMenu.sln` — add 5 new project references (3 src + 2 tests)
- `src/ControlMenu/ControlMenu.csproj` — `<ProjectReference>` to `ControlMenu.Common`; bump `<Version>` to `1.1.0-alpha.1`; fix the un-indented `SkiaSharp` `<PackageReference>` (item #26)
- `src/ControlMenu/Program.cs` — DataProtection key path now via `IDataPathResolver.GetKeysDir()`; `DependenciesRoot` config via `IDataPathResolver.GetDependenciesDir()`; register `IDataPathResolver` + `VelopackUpdateService`
- `src/ControlMenu/Modules/Jellyfin/Services/OperationLogger.cs` — `GetDefaultLogDirectory` / `GetDefaultBackupDirectory` consume `IDataPathResolver`
- `src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs` — `FindDepsRoot` removed; deps root injected via `IDataPathResolver`
- `src/ControlMenu/Modules/Cameras/CamerasModule.cs` — same
- `src/ControlMenu/Modules/Jellyfin/JellyfinModule.cs` — same
- `src/ControlMenu/Modules/Cameras/Services/Go2RtcService.cs:45` — drop `?? AppContext.BaseDirectory` fallback
- `src/ControlMenu/Components/Pages/Settings/GeneralSettings.razor` — add "Check for updates" button + dialog
- `tests/ControlMenu.Tests/ControlMenu.Tests.csproj` — fix CRLF + UTF-8 BOM (item #25); rewrite as UTF-8-no-BOM + LF
- `CHANGELOG.md` — `[Unreleased]` → seed Velopack core entries
- `.gitignore` — add `Releases/`, `*.nupkg` if not already present

**Tests added:** ~30 new (PathResolver 4, AppConfig 6, LauncherLogger 3, DataPathResolver 5, SingleInstance 4, VelopackHookDispatcher 8). Existing 383 stay green.

---

## Pre-flight checks

- [ ] **Verify on master, clean working tree, up to date with origin**

```powershell
git -C C:/Users/jscha/source/repos/control-menu status --short --branch
git -C C:/Users/jscha/source/repos/control-menu log --oneline -3
```

Expected: branch `## master...origin/master`, no `[ahead]`/`[behind]`, no tracked diffs. Most recent commit is `89fb974 docs(changelog): add [1.0.1] section for .NET 10 upgrade`.

- [ ] **Verify .NET 10 SDK present**

```powershell
dotnet --list-sdks
```

Expected: at least one `10.*` entry.

- [ ] **Verify ws-scrcpy-web sources are at HEAD `384c6fc` or newer**

```powershell
git -C C:/Users/jscha/source/repos/ws-scrcpy-web rev-parse HEAD
git -C C:/Users/jscha/source/repos/ws-scrcpy-web log --oneline -1 -- launcher/src/main.rs launcher/src/hooks.rs launcher/src/install_acl.rs launcher/src/single_instance.rs launcher/src/paths.rs launcher/src/log.rs common/src/config.rs
```

Expected: HEAD prints; the per-file log shows recent activity but each file's last-touch commit is at or before HEAD. If any of those files moved or were rewritten since `384c6fc`, halt and re-verify the line ranges in the "Sources to port from" section against current state before proceeding — this is the discipline gate.

- [ ] **Verify baseline test count**

```powershell
dotnet test C:/Users/jscha/source/repos/control-menu -c Release --nologo 2>&1 | Select-String "Passed:|Failed:"
```

Expected: `Passed: 383, Failed: 0`. If different, halt and triage before starting Phase 1.

---

## Task 1: Create branch + scaffold solution structure

**Files:**
- Modify: `ControlMenu.sln`
- Create: `src/ControlMenu.Common/ControlMenu.Common.csproj`
- Create: `src/ControlMenuLauncher/ControlMenuLauncher.csproj`
- Create: `src/ControlMenuTray/ControlMenuTray.csproj`
- Create: `src/ControlMenuTray/Program.cs` (stub)
- Create: `tests/ControlMenu.Common.Tests/ControlMenu.Common.Tests.csproj`
- Create: `tests/ControlMenuLauncher.Tests/ControlMenuLauncher.Tests.csproj`

**Legacy reference:** None — this is solution-glue work, no port.

- [ ] **Step 1: Create + check out feature branch**

```powershell
git -C C:/Users/jscha/source/repos/control-menu checkout -b feature/velopack-phase-1
```

Expected: `Switched to a new branch 'feature/velopack-phase-1'`.

- [ ] **Step 2: Create `src/ControlMenu.Common/ControlMenu.Common.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Version>1.1.0-alpha.1</Version>
    <AssemblyVersion>1.1.0.0</AssemblyVersion>
    <FileVersion>1.1.0.0</FileVersion>
    <RootNamespace>ControlMenu.Common</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="ControlMenu.Common.Tests" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create `src/ControlMenuLauncher/ControlMenuLauncher.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Version>1.1.0-alpha.1</Version>
    <AssemblyVersion>1.1.0.0</AssemblyVersion>
    <FileVersion>1.1.0.0</FileVersion>
    <RootNamespace>ControlMenu.Launcher</RootNamespace>
    <AssemblyName>ControlMenuLauncher</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\ControlMenu.Common\ControlMenu.Common.csproj" />
    <InternalsVisibleTo Include="ControlMenuLauncher.Tests" />
  </ItemGroup>

</Project>
```

`<OutputType>Exe</OutputType>` (NOT WinExe) for Phase 1 — the launcher still has a console window, which is useful for debugging the supervisor. Phase 2 changes to `WinExe` once the tray icon takes over UX.

- [ ] **Step 4: Create `src/ControlMenuTray/ControlMenuTray.csproj` (Phase 1 stub)**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Version>1.1.0-alpha.1</Version>
    <AssemblyVersion>1.1.0.0</AssemblyVersion>
    <FileVersion>1.1.0.0</FileVersion>
    <RootNamespace>ControlMenu.Tray</RootNamespace>
    <AssemblyName>ControlMenuTray</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\ControlMenu.Common\ControlMenu.Common.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 5: Create `src/ControlMenuTray/Program.cs` (stub)**

```csharp
namespace ControlMenu.Tray;

internal static class Program
{
    private static int Main(string[] args)
    {
        // Phase 1 stub. Phase 3 fills this with WinForms + healthcheck loop.
        return 0;
    }
}
```

- [ ] **Step 6: Create `tests/ControlMenu.Common.Tests/ControlMenu.Common.Tests.csproj`**

Use UTF-8 without BOM, LF line endings. Read `tests/ControlMenu.Tests/ControlMenu.Tests.csproj` first to confirm the package versions used (xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk) — match those exact versions.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>ControlMenu.Common.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="<MATCH-EXISTING>" />
    <PackageReference Include="xunit" Version="<MATCH-EXISTING>" />
    <PackageReference Include="xunit.runner.visualstudio" Version="<MATCH-EXISTING>" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\ControlMenu.Common\ControlMenu.Common.csproj" />
  </ItemGroup>

</Project>
```

Replace `<MATCH-EXISTING>` with the versions from `tests/ControlMenu.Tests/ControlMenu.Tests.csproj`.

- [ ] **Step 7: Create `tests/ControlMenuLauncher.Tests/ControlMenuLauncher.Tests.csproj`**

Same pattern as Step 6 but referencing `ControlMenuLauncher`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>ControlMenu.Launcher.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="<MATCH-EXISTING>" />
    <PackageReference Include="xunit" Version="<MATCH-EXISTING>" />
    <PackageReference Include="xunit.runner.visualstudio" Version="<MATCH-EXISTING>" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ControlMenuLauncher\ControlMenuLauncher.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 8: Add the 5 new csprojs to `ControlMenu.sln`**

Use `dotnet sln`:

```powershell
$repo = "C:/Users/jscha/source/repos/control-menu"
dotnet sln "$repo/ControlMenu.sln" add `
  "$repo/src/ControlMenu.Common/ControlMenu.Common.csproj" `
  "$repo/src/ControlMenuLauncher/ControlMenuLauncher.csproj" `
  "$repo/src/ControlMenuTray/ControlMenuTray.csproj" `
  "$repo/tests/ControlMenu.Common.Tests/ControlMenu.Common.Tests.csproj" `
  "$repo/tests/ControlMenuLauncher.Tests/ControlMenuLauncher.Tests.csproj"
```

Expected: `Project ... added to the solution` for each.

- [ ] **Step 9: Restore + build solution**

```powershell
dotnet restore C:/Users/jscha/source/repos/control-menu
dotnet build C:/Users/jscha/source/repos/control-menu -c Release --no-restore 2>&1 | Select-String -Pattern "(Build succeeded|error|warning)" | Select-Object -First 20
```

Expected: `Build succeeded.` with no errors. Two new projects produce DLLs/EXEs:
- `src/ControlMenu.Common/bin/Release/net10.0/ControlMenu.Common.dll`
- `src/ControlMenuLauncher/bin/Release/net10.0/ControlMenuLauncher.exe`
- `src/ControlMenuTray/bin/Release/net10.0/ControlMenuTray.exe`

If errors: most likely a missing `<ItemGroup>` or test-package version mismatch. Address per the message; do NOT proceed until clean.

- [ ] **Step 10: Run all tests to confirm baseline + new test projects find zero tests**

```powershell
dotnet test C:/Users/jscha/source/repos/control-menu -c Release --no-build --nologo 2>&1 | Select-String "Passed:|Failed:|tests:"
```

Expected: 3 test projects discovered. ControlMenu.Tests still 383/0. ControlMenu.Common.Tests and ControlMenuLauncher.Tests show 0 tests passing (no test classes yet). No failures.

- [ ] **Step 11: Commit**

```powershell
git -C C:/Users/jscha/source/repos/control-menu add ControlMenu.sln src/ControlMenu.Common/ControlMenu.Common.csproj src/ControlMenuLauncher/ControlMenuLauncher.csproj src/ControlMenuTray/ControlMenuTray.csproj src/ControlMenuTray/Program.cs tests/ControlMenu.Common.Tests/ControlMenu.Common.Tests.csproj tests/ControlMenuLauncher.Tests/ControlMenuLauncher.Tests.csproj
git -C C:/Users/jscha/source/repos/control-menu commit -m @'
feat(velopack): scaffold three-binary solution structure for Phase 1

- src/ControlMenu.Common/ — shared library (no UI deps)
- src/ControlMenuLauncher/ — Velopack supervisor (Phase 1 OutputType=Exe;
  Phase 2 changes to WinExe)
- src/ControlMenuTray/ — service-mode tray helper (Phase 1 stub; Phase 3
  fills in)
- tests/ControlMenu.Common.Tests/ — xUnit test project
- tests/ControlMenuLauncher.Tests/ — xUnit test project

Build clean. Existing 383 ControlMenu.Tests stay green; the two new test
projects exist with zero tests (filled in tasks 2-9).
'@
```

---

## Task 2: Port `paths.rs` → `PathResolver.cs` (TDD)

**Files:**
- Create: `tests/ControlMenu.Common.Tests/Paths/PathResolverTests.cs`
- Create: `src/ControlMenu.Common/Paths/PathResolver.cs`

**Legacy reference:** `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/paths.rs:1-172`

`paths.rs` derives the install root by walking up from `current_exe()`:
- Production: `<root>/current/launcher.exe` → install root is exe.parent().parent()
- Dev: `target/<profile>/launcher.exe` → caller falls back gracefully (still a valid path; no config.json in dev tree is fine)

The .NET equivalent uses `Process.GetCurrentProcess().MainModule.FileName` (so it reflects the launcher's actual on-disk location, not the test runner's, even when called from inside test assemblies).

- [ ] **Step 1: Write the failing tests**

`tests/ControlMenu.Common.Tests/Paths/PathResolverTests.cs`:

```csharp
using ControlMenu.Common.Paths;
using Xunit;

namespace ControlMenu.Common.Tests.Paths;

public class PathResolverTests
{
    [Fact]
    public void DeriveInstallRoot_FromExeUnderCurrentSubdir_ReturnsParentOfCurrent()
    {
        var exe = @"C:\Program Files\ControlMenu\current\ControlMenuLauncher.exe";
        var root = PathResolver.DeriveInstallRoot(exe);
        Assert.Equal(@"C:\Program Files\ControlMenu", root);
    }

    [Fact]
    public void DeriveInstallRoot_FromExeUnderArbitraryDevPath_ReturnsParentDir()
    {
        // Dev mode: exe lives at <repo>/src/ControlMenuLauncher/bin/Release/net10.0/ControlMenuLauncher.exe.
        // The "install root" semantic is "exe.parent().parent()" — for dev that
        // resolves to the bin folder's grandparent (Release/), which is fine
        // because no AppConfig lives there and the lenient loader returns
        // defaults in dev. Mirrors paths.rs:226-238 (resolve_install_root).
        var exe = @"C:\repo\src\ControlMenuLauncher\bin\Release\net10.0\ControlMenuLauncher.exe";
        var root = PathResolver.DeriveInstallRoot(exe);
        Assert.Equal(@"C:\repo\src\ControlMenuLauncher\bin\Release", root);
    }

    [Fact]
    public void DeriveInstallRoot_NullOrEmpty_Throws()
    {
        Assert.Throws<ArgumentException>(() => PathResolver.DeriveInstallRoot(null!));
        Assert.Throws<ArgumentException>(() => PathResolver.DeriveInstallRoot(string.Empty));
    }

    [Fact]
    public void DeriveInstallRoot_ExeWithNoGrandparent_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => PathResolver.DeriveInstallRoot(@"C:\foo.exe"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Common.Tests --filter PathResolverTests --nologo 2>&1 | Select-String "Passed:|Failed:|error"
```

Expected: build error — `PathResolver` type not defined.

- [ ] **Step 3: Implement `PathResolver.cs`**

`src/ControlMenu.Common/Paths/PathResolver.cs`:

```csharp
using System.Diagnostics;

namespace ControlMenu.Common.Paths;

public static class PathResolver
{
    /// <summary>
    /// Derive the install root from the running launcher executable path.
    /// In production, exe lives at <c>&lt;root&gt;\current\ControlMenuLauncher.exe</c>
    /// — install root is two parents up. In dev, callers should treat the
    /// returned path as "best effort" — AppConfig.Load is lenient and
    /// returns defaults when no config file exists at the resolved path.
    /// </summary>
    /// <param name="exePath">Absolute path to the launcher EXE; typically
    /// <c>Process.GetCurrentProcess().MainModule!.FileName</c>.</param>
    public static string DeriveInstallRoot(string exePath)
    {
        if (string.IsNullOrEmpty(exePath))
            throw new ArgumentException("exePath must be non-empty", nameof(exePath));

        var exeDir = Path.GetDirectoryName(exePath)
            ?? throw new InvalidOperationException("exe has no parent dir");
        var installRoot = Path.GetDirectoryName(exeDir)
            ?? throw new InvalidOperationException("exeDir has no parent (cannot derive install_root)");

        return installRoot;
    }

    /// <summary>
    /// Convenience wrapper using the current process's main module path.
    /// </summary>
    public static string DeriveInstallRootFromCurrentProcess()
    {
        var exe = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("could not determine current process exe path");
        return DeriveInstallRoot(exe);
    }

    public static string GetCurrentDir(string installRoot) => Path.Combine(installRoot, "current");
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Common.Tests --filter PathResolverTests --nologo 2>&1 | Select-String "Passed:|Failed:"
```

Expected: `Passed: 4, Failed: 0`.

- [ ] **Step 5: Diff against legacy**

> Diff your scaffold against `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/paths.rs:1-172`. For every difference, write a one-line rationale. If you can't justify a difference, change your scaffold to match legacy behavior.

Specifically check:
- Rust uses `current_exe()` which on Windows resolves through any symlinks; .NET `MainModule.FileName` is equivalent. ✓
- Rust returns `anyhow::Result<PathBuf>`; .NET throws `InvalidOperationException`. Different error model is idiomatic; document if any caller assumed Result-style swallowing.
- Rust `paths.rs` may have additional helpers beyond `resolve_install_root` (e.g., `current_dir()`, `data_root_or_install_root()`). Read the full file. Replicate any helpers Phase 1 needs into `PathResolver.cs`; defer Phase 2/3-specific ones.

If any difference can't be justified: change the scaffold. Commit any rationales as a comment block at the top of `PathResolver.cs`.

- [ ] **Step 6: Commit**

```powershell
git -C C:/Users/jscha/source/repos/control-menu add src/ControlMenu.Common/Paths/PathResolver.cs tests/ControlMenu.Common.Tests/Paths/PathResolverTests.cs
git -C C:/Users/jscha/source/repos/control-menu commit -m "feat(common): port paths.rs install-root derivation to PathResolver"
```

---

## Task 3: Port `common/src/config.rs` → `AppConfig.cs` (TDD)

**Files:**
- Create: `tests/ControlMenu.Common.Tests/Config/AppConfigTests.cs`
- Create: `src/ControlMenu.Common/Config/AppConfig.cs`
- Create: `src/ControlMenu.Common/Config/AppConfigPaths.cs`

**Legacy reference:** `C:/Users/jscha/source/repos/ws-scrcpy-web/common/src/config.rs:1-274`

Two load entry points:
- `Load(path)` — lenient: missing/malformed → returns default. Never throws.
- `LoadStrict(path)` — strict: missing → `FileNotFoundException`, malformed → `JsonException`.

Plus `data_root_from_env()` equivalent — reads `PROGRAMDATA` env, returns `<programdata>\ControlMenu` (note: ws-scrcpy-web uses `WsScrcpyWeb`; CM uses `ControlMenu`).

- [ ] **Step 1: Write failing tests**

`tests/ControlMenu.Common.Tests/Config/AppConfigTests.cs`:

```csharp
using System.Text.Json;
using ControlMenu.Common.Config;
using Xunit;

namespace ControlMenu.Common.Tests.Config;

public class AppConfigTests : IDisposable
{
    private readonly string _tempDir;

    public AppConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm-cfg-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var path = Path.Combine(_tempDir, "missing.json");
        var cfg = AppConfig.Load(path);
        Assert.Null(cfg.InstallMode);
        Assert.False(cfg.FirstRunComplete);
        Assert.Null(cfg.WebPort);
        Assert.False(cfg.IsServiceMode());
    }

    [Fact]
    public void Load_MalformedJson_ReturnsDefaults()
    {
        var path = Path.Combine(_tempDir, "broken.json");
        File.WriteAllText(path, "{not json");
        var cfg = AppConfig.Load(path);
        Assert.Null(cfg.InstallMode);
        Assert.False(cfg.IsServiceMode());
    }

    [Fact]
    public void Load_ServiceMode_ReturnsTrue_FromIsServiceMode()
    {
        var path = Path.Combine(_tempDir, "cfg.json");
        File.WriteAllText(path, """{"installMode":"service","webPort":5159}""");
        var cfg = AppConfig.Load(path);
        Assert.Equal("service", cfg.InstallMode);
        Assert.Equal(5159, cfg.WebPort);
        Assert.True(cfg.IsServiceMode());
    }

    [Fact]
    public void Load_UserMode_IsServiceModeFalse()
    {
        var path = Path.Combine(_tempDir, "cfg.json");
        File.WriteAllText(path, """{"installMode":"user"}""");
        var cfg = AppConfig.Load(path);
        Assert.False(cfg.IsServiceMode());
    }

    [Fact]
    public void LoadStrict_MissingFile_Throws()
    {
        var path = Path.Combine(_tempDir, "missing.json");
        Assert.Throws<FileNotFoundException>(() => AppConfig.LoadStrict(path));
    }

    [Fact]
    public void LoadStrict_MalformedJson_Throws()
    {
        var path = Path.Combine(_tempDir, "broken.json");
        File.WriteAllText(path, "{not json");
        Assert.Throws<JsonException>(() => AppConfig.LoadStrict(path));
    }

    [Fact]
    public void DataRootFromEnv_OnWindows_ReadsProgramDataAndAppendsControlMenu()
    {
        var root = AppConfigPaths.DataRootForWindows(@"C:\ProgramData");
        Assert.Equal(@"C:\ProgramData\ControlMenu", root);
    }

    [Fact]
    public void DataRootFromEnv_NullProgramData_FallsBackToDefault()
    {
        var root = AppConfigPaths.DataRootForWindows(null);
        Assert.Equal(@"C:\ProgramData\ControlMenu", root);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Common.Tests --filter AppConfigTests --nologo
```

Expected: build errors — `AppConfig`, `AppConfigPaths` not defined.

- [ ] **Step 3: Implement `AppConfig.cs`**

`src/ControlMenu.Common/Config/AppConfig.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ControlMenu.Common.Config;

public sealed class AppConfig
{
    [JsonPropertyName("installMode")]
    public string? InstallMode { get; init; }

    [JsonPropertyName("firstRunComplete")]
    public bool FirstRunComplete { get; init; }

    [JsonPropertyName("webPort")]
    public int? WebPort { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("serviceName")]
    public string? ServiceName { get; init; }

    [JsonPropertyName("trayHelperPath")]
    public string? TrayHelperPath { get; init; }

    public bool IsServiceMode() =>
        !string.IsNullOrEmpty(InstallMode)
        && InstallMode.Equals("service", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lenient load: missing or malformed file returns a default-valued
    /// AppConfig. Never throws. Mirrors common/src/config.rs:AppConfig::load.
    /// </summary>
    public static AppConfig Load(string path)
    {
        try
        {
            return LoadStrict(path);
        }
        catch
        {
            return new AppConfig();
        }
    }

    /// <summary>
    /// Strict load: missing file throws FileNotFoundException; malformed
    /// JSON throws JsonException. Mirrors common/src/config.rs:AppConfig::load_strict.
    /// </summary>
    public static AppConfig LoadStrict(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"AppConfig file not found: {path}", path);

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppConfig>(json)
            ?? throw new JsonException($"AppConfig deserialized to null: {path}");
    }

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }
}
```

- [ ] **Step 4: Implement `AppConfigPaths.cs`**

`src/ControlMenu.Common/Config/AppConfigPaths.cs`:

```csharp
namespace ControlMenu.Common.Config;

public static class AppConfigPaths
{
    private const string AppDataDirName = "ControlMenu";
    private const string DefaultProgramData = @"C:\ProgramData";

    /// <summary>
    /// Return <c>&lt;programdata&gt;\ControlMenu</c>. Mirrors
    /// common/src/config.rs:data_root_for_windows.
    /// </summary>
    public static string DataRootForWindows(string? programData)
    {
        var pd = string.IsNullOrEmpty(programData) ? DefaultProgramData : programData;
        return Path.Combine(pd, AppDataDirName);
    }

    /// <summary>
    /// Read PROGRAMDATA env var, return <c>&lt;programdata&gt;\ControlMenu</c>
    /// on Windows; null on non-Windows. Mirrors data_root_from_env.
    /// </summary>
    public static string? DataRootFromEnv()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var pd = Environment.GetEnvironmentVariable("PROGRAMDATA");
        return DataRootForWindows(pd);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```powershell
dotnet test C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Common.Tests --filter AppConfigTests --nologo
```

Expected: 8 passed, 0 failed.

- [ ] **Step 6: Diff against legacy**

> Diff your scaffold against `C:/Users/jscha/source/repos/ws-scrcpy-web/common/src/config.rs:1-274`. For every difference, write a one-line rationale. If you can't justify a difference, change your scaffold to match legacy behavior.

Specifically reconcile:
- The Rust struct uses `serde(rename = "...")` — match each rename in `[JsonPropertyName(...)]`.
- The Rust `is_service_mode()` checks `install_mode == Some("service")`. .NET version uses case-insensitive match. Rationale: `JsonSerializer` defaults to case-sensitive deserialization but config files written by humans (during install) might be inconsistent — log if you change behavior. (Decision: keep case-insensitive match for IsServiceMode but rely on JsonSerializer for property name matching.)
- The Rust `ConfigError` enum has `Missing` / `Io` / `Parse` variants. .NET version uses `FileNotFoundException` / `IOException` / `JsonException`. Different error idiom but equivalent surface.
- Any save/write path in the Rust file? If `config.rs` exposes a writer, port that too — Phase 3 needs it for the install-as-service flow that writes `installMode="service"`. If it's TS-side only in ws-scrcpy-web, our `Save` method above is net-new and that's fine; flag in the rationale comment.

- [ ] **Step 7: Commit**

```powershell
git -C C:/Users/jscha/source/repos/control-menu add src/ControlMenu.Common/Config/AppConfig.cs src/ControlMenu.Common/Config/AppConfigPaths.cs tests/ControlMenu.Common.Tests/Config/AppConfigTests.cs
git -C C:/Users/jscha/source/repos/control-menu commit -m "feat(common): port common/src/config.rs to AppConfig + AppConfigPaths"
```

---

## Task 4: Port `log.rs` → `LauncherLogger.cs` (TDD)

**Files:**
- Create: `tests/ControlMenu.Common.Tests/Logging/LauncherLoggerTests.cs`
- Create: `src/ControlMenu.Common/Logging/LauncherLogger.cs`

**Legacy reference:** `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/log.rs:1-134`

Tagged file logger with rotation-on-startup. Used by both launcher and (Phase 3) tray helper. Static API (`LauncherLogger.Info(msg)`, `LauncherLogger.Error(msg)`) for terseness in launcher Program.cs.

- [ ] **Step 1: Write failing tests**

`tests/ControlMenu.Common.Tests/Logging/LauncherLoggerTests.cs`:

```csharp
using ControlMenu.Common.Logging;
using Xunit;

namespace ControlMenu.Common.Tests.Logging;

public class LauncherLoggerTests : IDisposable
{
    private readonly string _logDir;

    public LauncherLoggerTests()
    {
        _logDir = Path.Combine(Path.GetTempPath(), "cm-log-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_logDir);
    }

    public void Dispose()
    {
        LauncherLogger.Reset();
        if (Directory.Exists(_logDir)) Directory.Delete(_logDir, recursive: true);
    }

    [Fact]
    public void Init_CreatesLogFile()
    {
        var path = Path.Combine(_logDir, "launcher.log");
        LauncherLogger.Init(path);
        LauncherLogger.Info("hello");
        LauncherLogger.Flush();

        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("hello", content);
        Assert.Contains("INFO", content);
    }

    [Fact]
    public void Init_RotatesExistingLogOnStartup()
    {
        var path = Path.Combine(_logDir, "launcher.log");
        File.WriteAllText(path, "previous run\n");

        LauncherLogger.Init(path);
        LauncherLogger.Info("fresh start");
        LauncherLogger.Flush();

        var prevPath = Path.Combine(_logDir, "launcher.prev.log");
        Assert.True(File.Exists(prevPath), "expected rotation to launcher.prev.log");
        Assert.Contains("previous run", File.ReadAllText(prevPath));
        Assert.DoesNotContain("previous run", File.ReadAllText(path));
        Assert.Contains("fresh start", File.ReadAllText(path));
    }

    [Fact]
    public void Info_BeforeInit_DoesNotThrow()
    {
        // The launcher logs from main() entry — even before Init() can wire
        // a path, we must not crash. Buffer-or-drop semantics; both fine.
        LauncherLogger.Info("preinit msg");
    }
}
```

- [ ] **Step 2: Run tests; verify failure** (`LauncherLogger` undefined).

- [ ] **Step 3: Implement `LauncherLogger.cs`**

`src/ControlMenu.Common/Logging/LauncherLogger.cs`:

```csharp
namespace ControlMenu.Common.Logging;

/// <summary>
/// File logger for the Velopack launcher and tray helper. Static so the
/// launcher's main() entry can call <see cref="Info"/> immediately. Mirrors
/// launcher/src/log.rs (lines 1-134).
/// </summary>
public static class LauncherLogger
{
    private static readonly object _lock = new();
    private static StreamWriter? _writer;
    private static string? _logPath;

    /// <summary>
    /// Wire the logger to a file path. Rotates an existing log file at the
    /// path to <c>&lt;name&gt;.prev.log</c> before opening fresh. Safe to
    /// call multiple times; later calls re-rotate.
    /// </summary>
    public static void Init(string path)
    {
        lock (_lock)
        {
            try { _writer?.Flush(); _writer?.Dispose(); } catch { /* best effort */ }

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            if (File.Exists(path))
            {
                var prev = Path.Combine(
                    Path.GetDirectoryName(path)!,
                    Path.GetFileNameWithoutExtension(path) + ".prev" + Path.GetExtension(path));
                try
                {
                    if (File.Exists(prev)) File.Delete(prev);
                    File.Move(path, prev);
                }
                catch
                {
                    // If rotation fails (locked file, ACL), open in append
                    // mode rather than refusing to log.
                }
            }

            _writer = new StreamWriter(path, append: true) { AutoFlush = false };
            _logPath = path;
        }
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    public static void Flush()
    {
        lock (_lock) { _writer?.Flush(); }
    }

    /// <summary>Test seam — discard state. Not for production use.</summary>
    public static void Reset()
    {
        lock (_lock)
        {
            try { _writer?.Flush(); _writer?.Dispose(); } catch { }
            _writer = null;
            _logPath = null;
        }
    }

    private static void Write(string level, string msg)
    {
        var ts = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var line = $"{ts} {level,-5} {msg}";
        lock (_lock)
        {
            if (_writer is null)
            {
                // Pre-init: drop. Phase 3 may add a ring-buffer for these.
                return;
            }
            try { _writer.WriteLine(line); _writer.Flush(); } catch { /* swallow */ }
        }
    }
}
```

- [ ] **Step 4: Run tests; verify they pass.**

- [ ] **Step 5: Diff against legacy**

> Diff your scaffold against `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/log.rs:1-134`. For every difference, write a one-line rationale. If you can't justify a difference, change your scaffold to match legacy behavior.

Specifically check:
- The Rust version's rotation pattern — is it `.prev.log` or numbered (`launcher.1.log`, `launcher.2.log`)? Match exactly. If unclear, mirror Rust verbatim.
- The Rust version may have `warn!` and `debug!` macros in addition to info/error. Add them if Phase 1 needs them; otherwise log them as a "deferred" rationale.
- Time format — does Rust use UTC or local? Use UTC (matches `DateTimeOffset.UtcNow` here). If Rust is local, change to local in the .NET version.

- [ ] **Step 6: Commit**

```powershell
git -C C:/Users/jscha/source/repos/control-menu add src/ControlMenu.Common/Logging/LauncherLogger.cs tests/ControlMenu.Common.Tests/Logging/LauncherLoggerTests.cs
git -C C:/Users/jscha/source/repos/control-menu commit -m "feat(common): port log.rs tagged file logger to LauncherLogger"
```

---

## Task 5: Define `IDataPathResolver` + 2 implementations + selector (TDD)

**Files:**
- Create: `src/ControlMenu.Common/Paths/IDataPathResolver.cs`
- Create: `src/ControlMenu.Common/Paths/VelopackDataPathResolver.cs`
- Create: `src/ControlMenu.Common/Paths/DevDataPathResolver.cs`
- Create: `src/ControlMenu.Common/Paths/DataPathResolverFactory.cs`
- Create: `tests/ControlMenu.Common.Tests/Paths/DataPathResolverTests.cs`

**Legacy reference:** None — this is a CM-specific abstraction. The pattern is informed by `paths.rs` + `config.rs:data_root_from_env`, but the IDataPathResolver interface is net-new for CM.

- [ ] **Step 1: Write failing tests**

`tests/ControlMenu.Common.Tests/Paths/DataPathResolverTests.cs`:

```csharp
using ControlMenu.Common.Paths;
using Xunit;

namespace ControlMenu.Common.Tests.Paths;

public class DataPathResolverTests
{
    [Fact]
    public void Velopack_GetDataRoot_RootsAtProgramDataControlMenu()
    {
        var r = new VelopackDataPathResolver(installRoot: @"C:\Program Files\ControlMenu", programData: @"C:\ProgramData");
        Assert.Equal(@"C:\ProgramData\ControlMenu", r.GetDataRoot());
        Assert.Equal(@"C:\ProgramData\ControlMenu\config", r.GetConfigDir());
        Assert.Equal(@"C:\ProgramData\ControlMenu\config\controlmenu.db", r.GetDbPath());
        Assert.Equal(@"C:\ProgramData\ControlMenu\config\app-config.json", r.GetAppConfigPath());
        Assert.Equal(@"C:\ProgramData\ControlMenu\dependencies", r.GetDependenciesDir());
        Assert.Equal(@"C:\ProgramData\ControlMenu\logs", r.GetLogsDir());
        Assert.Equal(@"C:\ProgramData\ControlMenu\keys", r.GetKeysDir());
        Assert.Equal(@"C:\ProgramData\ControlMenu\jellyfin-backups", r.GetJellyfinBackupsDir());
    }

    [Fact]
    public void Velopack_GetInstallRoot_AndCurrent_ReflectInputs()
    {
        var r = new VelopackDataPathResolver(installRoot: @"C:\Program Files\ControlMenu", programData: @"C:\ProgramData");
        Assert.Equal(@"C:\Program Files\ControlMenu", r.GetInstallRoot());
        Assert.Equal(@"C:\Program Files\ControlMenu\current", r.GetCurrentDir());
    }

    [Fact]
    public void Dev_RootsAtBaseDirectory()
    {
        var baseDir = @"C:\repo\src\ControlMenu\bin\Release\net10.0";
        var r = new DevDataPathResolver(baseDir);
        Assert.Equal(baseDir, r.GetDataRoot());
        Assert.Equal(Path.Combine(baseDir, "controlmenu.db"), r.GetDbPath());
        Assert.Equal(Path.Combine(baseDir, "dependencies"), r.GetDependenciesDir());
        Assert.Equal(Path.Combine(baseDir, "logs"), r.GetLogsDir());
        Assert.Equal(Path.Combine(baseDir, "keys"), r.GetKeysDir());
    }

    [Fact]
    public void Factory_DetectsVelopackMode_WhenUpdateExeAdjacentToInstallRoot()
    {
        var tempInstall = Path.Combine(Path.GetTempPath(), "vmode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempInstall);
        Directory.CreateDirectory(Path.Combine(tempInstall, "current"));
        File.WriteAllText(Path.Combine(tempInstall, "Update.exe"), "stub");

        try
        {
            var fakeExe = Path.Combine(tempInstall, "current", "ControlMenuLauncher.exe");
            var resolver = DataPathResolverFactory.Create(fakeExe, programData: @"C:\ProgramData");
            Assert.IsType<VelopackDataPathResolver>(resolver);
        }
        finally { Directory.Delete(tempInstall, recursive: true); }
    }

    [Fact]
    public void Factory_DetectsDevMode_WhenNoAdjacentUpdateExe()
    {
        var devExe = Path.Combine(Path.GetTempPath(), "dev-" + Guid.NewGuid().ToString("N"), "bin", "Release", "net10.0", "ControlMenu.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(devExe)!);
        File.WriteAllText(devExe, "stub");
        try
        {
            var resolver = DataPathResolverFactory.Create(devExe, programData: @"C:\ProgramData");
            Assert.IsType<DevDataPathResolver>(resolver);
        }
        finally { Directory.Delete(Path.GetDirectoryName(devExe)!, recursive: true); }
    }
}
```

- [ ] **Step 2: Run tests; verify failure.**

- [ ] **Step 3: Implement the four files**

`src/ControlMenu.Common/Paths/IDataPathResolver.cs`:

```csharp
namespace ControlMenu.Common.Paths;

public interface IDataPathResolver
{
    string GetInstallRoot();
    string GetCurrentDir();
    string GetDataRoot();
    string GetConfigDir();
    string GetDbPath();
    string GetAppConfigPath();
    string GetDependenciesDir();
    string GetLogsDir();
    string GetKeysDir();
    string GetJellyfinBackupsDir();
}
```

`src/ControlMenu.Common/Paths/VelopackDataPathResolver.cs`:

```csharp
using ControlMenu.Common.Config;

namespace ControlMenu.Common.Paths;

public sealed class VelopackDataPathResolver : IDataPathResolver
{
    private readonly string _installRoot;
    private readonly string _dataRoot;

    public VelopackDataPathResolver(string installRoot, string? programData = null)
    {
        _installRoot = installRoot;
        _dataRoot = AppConfigPaths.DataRootForWindows(programData ?? Environment.GetEnvironmentVariable("PROGRAMDATA"));
    }

    public string GetInstallRoot() => _installRoot;
    public string GetCurrentDir() => Path.Combine(_installRoot, "current");
    public string GetDataRoot() => _dataRoot;
    public string GetConfigDir() => Path.Combine(_dataRoot, "config");
    public string GetDbPath() => Path.Combine(_dataRoot, "config", "controlmenu.db");
    public string GetAppConfigPath() => Path.Combine(_dataRoot, "config", "app-config.json");
    public string GetDependenciesDir() => Path.Combine(_dataRoot, "dependencies");
    public string GetLogsDir() => Path.Combine(_dataRoot, "logs");
    public string GetKeysDir() => Path.Combine(_dataRoot, "keys");
    public string GetJellyfinBackupsDir() => Path.Combine(_dataRoot, "jellyfin-backups");
}
```

`src/ControlMenu.Common/Paths/DevDataPathResolver.cs`:

```csharp
namespace ControlMenu.Common.Paths;

public sealed class DevDataPathResolver : IDataPathResolver
{
    private readonly string _baseDir;

    public DevDataPathResolver(string baseDir) { _baseDir = baseDir; }

    public string GetInstallRoot() => _baseDir;
    public string GetCurrentDir() => _baseDir;
    public string GetDataRoot() => _baseDir;
    public string GetConfigDir() => _baseDir;
    public string GetDbPath() => Path.Combine(_baseDir, "controlmenu.db");
    public string GetAppConfigPath() => Path.Combine(_baseDir, "app-config.json");
    public string GetDependenciesDir() => Path.Combine(_baseDir, "dependencies");
    public string GetLogsDir() => Path.Combine(_baseDir, "logs");
    public string GetKeysDir() => Path.Combine(_baseDir, "keys");
    public string GetJellyfinBackupsDir() => Path.Combine(_baseDir, "jellyfin-backups");
}
```

`src/ControlMenu.Common/Paths/DataPathResolverFactory.cs`:

```csharp
namespace ControlMenu.Common.Paths;

public static class DataPathResolverFactory
{
    /// <summary>
    /// Probe for an <c>Update.exe</c> sibling-to-install-root to detect Velopack mode.
    /// In Velopack PerMachine layout the launcher exe lives at
    /// <c>&lt;installRoot&gt;\current\ControlMenuLauncher.exe</c> and Update.exe
    /// lives at <c>&lt;installRoot&gt;\Update.exe</c>. If we find that pattern,
    /// emit a <see cref="VelopackDataPathResolver"/>; otherwise fall back to
    /// <see cref="DevDataPathResolver"/> rooted at the exe's directory.
    /// </summary>
    public static IDataPathResolver Create(string exePath, string? programData = null)
    {
        var exeDir = Path.GetDirectoryName(exePath) ?? throw new InvalidOperationException("exe has no parent dir");
        var maybeInstallRoot = Path.GetDirectoryName(exeDir);
        if (maybeInstallRoot is not null
            && File.Exists(Path.Combine(maybeInstallRoot, "Update.exe")))
        {
            return new VelopackDataPathResolver(maybeInstallRoot, programData);
        }

        return new DevDataPathResolver(exeDir);
    }

    public static IDataPathResolver CreateFromCurrentProcess(string? programData = null)
    {
        var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("could not determine current process exe path");
        return Create(exe, programData);
    }
}
```

- [ ] **Step 4: Run tests; verify they pass.**

- [ ] **Step 5: Diff against legacy + spec**

> Diff your scaffold against the spec § "Path resolution" table (`docs/superpowers/specs/2026-05-09-velopack-packaging-design.md` lines 60-82) and the design's intent (`paths.rs` + `config.rs:data_root_from_env`). For every difference, write a one-line rationale. If you can't justify a difference, change your scaffold to match.

Specifically: every entry in the spec's "On-disk layout" table (lines 41-58) must be representable via this resolver. Walk the table; if any path isn't reachable from a method, add the method.

- [ ] **Step 6: Commit**

```powershell
git -C C:/Users/jscha/source/repos/control-menu add src/ControlMenu.Common/Paths/IDataPathResolver.cs src/ControlMenu.Common/Paths/VelopackDataPathResolver.cs src/ControlMenu.Common/Paths/DevDataPathResolver.cs src/ControlMenu.Common/Paths/DataPathResolverFactory.cs tests/ControlMenu.Common.Tests/Paths/DataPathResolverTests.cs
git -C C:/Users/jscha/source/repos/control-menu commit -m "feat(common): add IDataPathResolver with Velopack + dev implementations"
```

---

## Task 6: Refactor existing CM path consumers to inject `IDataPathResolver`

**Files:**
- Modify: `src/ControlMenu/ControlMenu.csproj` — add `<ProjectReference>` to `ControlMenu.Common`; bump `<Version>` to `1.1.0-alpha.1`; fix un-indented SkiaSharp PackageReference (item #26)
- Modify: `src/ControlMenu/Program.cs` — register `IDataPathResolver`; rewrite DataProtection key path; rewrite `DependenciesRoot` config wiring; pass connection string through resolver
- Modify: `src/ControlMenu/appsettings.json` — drop hardcoded `Data Source=controlmenu.db` from `ConnectionStrings:DefaultConnection`
- Modify: `src/ControlMenu/Modules/Jellyfin/Services/OperationLogger.cs` — accept `IDataPathResolver` for log + backup dirs
- Modify: `src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs` — `DepsRoot` becomes a constructor parameter (or `IToolModule.DependenciesDir` setter wired post-DI; pick simpler path)
- Modify: `src/ControlMenu/Modules/Cameras/CamerasModule.cs` — same
- Modify: `src/ControlMenu/Modules/Jellyfin/JellyfinModule.cs` — same
- Modify: `src/ControlMenu/Modules/Cameras/Services/Go2RtcService.cs:45` — drop `?? AppContext.BaseDirectory` fallback (resolver guarantees a value)
- Modify: `tests/ControlMenu.Tests/ControlMenu.Tests.csproj` — fix CRLF + UTF-8 BOM (item #25); rewrite as UTF-8 without BOM + LF line endings

**Legacy reference:** None — refactor of existing CM code, not a port.

- [ ] **Step 1: Add Common project reference + fix csproj hygiene**

Read `src/ControlMenu/ControlMenu.csproj`. Apply Edit:

Replace:
```xml
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.*" />
<PackageReference Include="SkiaSharp" Version="3.119.2" />
  </ItemGroup>

</Project>
```

With:
```xml
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.*" />
    <PackageReference Include="SkiaSharp" Version="3.119.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ControlMenu.Common\ControlMenu.Common.csproj" />
  </ItemGroup>

</Project>
```

(Indents the SkiaSharp line to 4 spaces — closes item #26 from the TODO. Adds the ProjectReference to `ControlMenu.Common`.)

Then bump version. Replace:
```xml
    <Version>1.0.1</Version>
    <AssemblyVersion>1.0.1.0</AssemblyVersion>
    <FileVersion>1.0.1.0</FileVersion>
```

With:
```xml
    <Version>1.1.0-alpha.1</Version>
    <AssemblyVersion>1.1.0.0</AssemblyVersion>
    <FileVersion>1.1.0.0</FileVersion>
```

- [ ] **Step 2: Re-encode `tests/ControlMenu.Tests/ControlMenu.Tests.csproj` as UTF-8 without BOM + LF**

Read the file first. Then write it back via the Write tool — the Write tool emits UTF-8 without BOM and LF line endings by default, which fixes both encoding gaps in one shot. Closes TODO item #25.

```powershell
# Verify after the rewrite:
Format-Hex -Path C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests/ControlMenu.Tests.csproj -Count 8
```

Expected: leading bytes are `3C 3F 78 6D 6C` (`<?xml`) — no `EF BB BF` BOM prefix.

```powershell
git -C C:/Users/jscha/source/repos/control-menu diff --check tests/ControlMenu.Tests/ControlMenu.Tests.csproj
```

Expected: no "trailing whitespace" warnings.

- [ ] **Step 3: Refactor `Program.cs` to register and consume `IDataPathResolver`**

Read `src/ControlMenu/Program.cs` first.

Replace the existing DependenciesRoot + DataProtection blocks (lines 18-44):

```csharp
var builder = WebApplication.CreateBuilder(args);

// ContentRootPath = project dir in dev, published root in production
var depsRoot = Path.Combine(builder.Environment.ContentRootPath, "dependencies");
builder.Configuration["DependenciesRoot"] = depsRoot;

// Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        // Icon Converter ships image bytes (base64) over the SignalR circuit;
        // default 32KB cap rejects anything but a tiny image.
        options.MaximumReceiveMessageSize = 32 * 1024 * 1024; // 32 MB
    });

// Database — factory pattern required for Blazor Server (avoids stale change-tracker state)
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Data Protection (used by SecretStore for encrypting settings)
var keysPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "ControlMenu", "keys");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("ControlMenu");
```

With:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Resolve all writable-state paths through IDataPathResolver. Velopack mode
// roots at C:\ProgramData\ControlMenu; dev mode roots at AppContext.BaseDirectory.
// Selector probes for ..\..\Update.exe — present in Velopack installs.
var dataPathResolver = ControlMenu.Common.Paths.DataPathResolverFactory.CreateFromCurrentProcess();
Directory.CreateDirectory(dataPathResolver.GetConfigDir());
Directory.CreateDirectory(dataPathResolver.GetLogsDir());
Directory.CreateDirectory(dataPathResolver.GetKeysDir());
Directory.CreateDirectory(dataPathResolver.GetDependenciesDir());
builder.Services.AddSingleton<ControlMenu.Common.Paths.IDataPathResolver>(dataPathResolver);
builder.Configuration["DependenciesRoot"] = dataPathResolver.GetDependenciesDir();

// Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        // Icon Converter ships image bytes (base64) over the SignalR circuit;
        // default 32KB cap rejects anything but a tiny image.
        options.MaximumReceiveMessageSize = 32 * 1024 * 1024; // 32 MB
    });

// Database — factory pattern required for Blazor Server (avoids stale change-tracker state).
// Connection string built from the resolver, not appsettings.json — appsettings still
// declares an entry but the resolver overrides at composition.
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dataPathResolver.GetDbPath()}"));

// Data Protection — keys directory must exist BEFORE PersistKeysToFileSystem
// (call to CreateDirectory above guarantees this).
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataPathResolver.GetKeysDir()))
    .SetApplicationName("ControlMenu");
```

- [ ] **Step 4: Refactor `OperationLogger.cs`**

The existing API is static `GetDefaultLogDirectory()` / `GetDefaultBackupDirectory()`. Convert to non-static so callers can DI an `IDataPathResolver`. Or: keep static but accept the resolver as a parameter from each call site. Pick the simpler one — static-with-injected-arg avoids re-plumbing every test.

Apply Edit. Replace lines 56-64:

```csharp
    public static string GetDefaultLogDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "jellyfin-data", "logging");

    public static string GetDefaultBackupDirectory()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "jellyfin-data", "backups");
        Directory.CreateDirectory(dir);
        return dir;
    }
```

With:

```csharp
    public static string GetDefaultLogDirectory(ControlMenu.Common.Paths.IDataPathResolver paths) =>
        Path.Combine(paths.GetLogsDir(), "jellyfin");

    public static string GetDefaultBackupDirectory(ControlMenu.Common.Paths.IDataPathResolver paths)
    {
        var dir = paths.GetJellyfinBackupsDir();
        Directory.CreateDirectory(dir);
        return dir;
    }
```

Also change line 21 in `Create` and line 61 in `GetRecentLogs` to require/accept the resolver, or to derive the dir from a passed-in absolute path. Read the entire file and update each call site to use the resolver-bound directory. Update tests in `tests/ControlMenu.Tests/Modules/Jellyfin/Services/OperationLoggerTests.cs` (read first to confirm filename) to inject a stub resolver.

Stub for tests:

```csharp
internal sealed class TestPathResolver : ControlMenu.Common.Paths.IDataPathResolver
{
    private readonly string _root;
    public TestPathResolver(string root) { _root = root; }
    public string GetInstallRoot() => _root;
    public string GetCurrentDir() => _root;
    public string GetDataRoot() => _root;
    public string GetConfigDir() => Path.Combine(_root, "config");
    public string GetDbPath() => Path.Combine(_root, "config", "controlmenu.db");
    public string GetAppConfigPath() => Path.Combine(_root, "config", "app-config.json");
    public string GetDependenciesDir() => Path.Combine(_root, "dependencies");
    public string GetLogsDir() => Path.Combine(_root, "logs");
    public string GetKeysDir() => Path.Combine(_root, "keys");
    public string GetJellyfinBackupsDir() => Path.Combine(_root, "jellyfin-backups");
}
```

- [ ] **Step 5: Refactor module dependency-root resolution**

Each of `AndroidDevicesModule`, `CamerasModule`, `JellyfinModule` has a `private static readonly string DepsRoot = FindDepsRoot();` pattern. The `IToolModule` interface returns `Dependencies` as a property — for Phase 1 we want each `InstallPath` rooted via `IDataPathResolver.GetDependenciesDir()`.

Two approaches; pick the smaller diff:

**Approach A** (lazy, requires re-touching every module): change `Dependencies` from `IEnumerable<ModuleDependency>` property-with-static-init to a method that takes `IDataPathResolver`. Update `IToolModule` interface and every implementation. Higher impact.

**Approach B** (preserve existing static init): change `FindDepsRoot()` in each module to read a singleton `DepsRoot.Path` set during DI bootstrap from `IDataPathResolver.GetDependenciesDir()`. Lower impact.

Pick Approach B. Implementation:

Create `src/ControlMenu/Services/DepsRootHolder.cs`:

```csharp
namespace ControlMenu.Services;

/// <summary>
/// Static holder so module classes (which initialize Dependencies as a
/// readonly property) can read the resolver-derived deps root without
/// taking it via DI. Set once at composition root in Program.cs.
/// </summary>
internal static class DepsRootHolder
{
    private static string? _path;
    public static string Path
    {
        get => _path ?? throw new InvalidOperationException("DepsRootHolder.Path read before composition root set it");
        set => _path = value;
    }
}
```

In `Program.cs`, between path-resolver creation and service registration, add:

```csharp
ControlMenu.Services.DepsRootHolder.Path = dataPathResolver.GetDependenciesDir();
```

In each module file (`AndroidDevicesModule.cs`, `CamerasModule.cs`, `JellyfinModule.cs`), replace the `FindDepsRoot()` method body with:

```csharp
    private static readonly string DepsRoot = ControlMenu.Services.DepsRootHolder.Path;
```

…and remove the existing `FindDepsRoot()` method.

- [ ] **Step 6: Drop `Go2RtcService.cs:45` BaseDirectory fallback**

Read `src/ControlMenu/Modules/Cameras/Services/Go2RtcService.cs:40-50`. The current line 45 reads (approximately):

```csharp
var depsRoot = configValue ?? AppContext.BaseDirectory;
```

The `configValue` comes from `IConfiguration["DependenciesRoot"]`, which Program.cs now populates from the resolver. The fallback is dead code; drop it. Replace with:

```csharp
var depsRoot = configValue ?? throw new InvalidOperationException("DependenciesRoot not configured");
```

Read first to confirm the exact line content before editing.

- [ ] **Step 7: Build + run all existing tests**

```powershell
dotnet build C:/Users/jscha/source/repos/control-menu -c Release 2>&1 | Select-String "Build succeeded|error"
dotnet test C:/Users/jscha/source/repos/control-menu -c Release --no-build --nologo 2>&1 | Select-String "Passed:|Failed:"
```

Expected: build clean. ControlMenu.Tests still 383/0. ControlMenu.Common.Tests passing the new tests added in Tasks 2-5.

If any ControlMenu.Tests fails: most likely the `OperationLogger` test was assuming `AppContext.BaseDirectory` paths. Read the failure; pass an injected resolver via the test harness; do NOT silently rewrite expectations.

- [ ] **Step 8: Commit**

```powershell
git -C C:/Users/jscha/source/repos/control-menu add src/ControlMenu/ControlMenu.csproj src/ControlMenu/Program.cs src/ControlMenu/Modules/Jellyfin/Services/OperationLogger.cs src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs src/ControlMenu/Modules/Cameras/CamerasModule.cs src/ControlMenu/Modules/Jellyfin/JellyfinModule.cs src/ControlMenu/Modules/Cameras/Services/Go2RtcService.cs src/ControlMenu/Services/DepsRootHolder.cs tests/ControlMenu.Tests/ControlMenu.Tests.csproj
git -C C:/Users/jscha/source/repos/control-menu commit -m @'
refactor(paths): route all writable-state paths through IDataPathResolver

- Program.cs: resolve DataProtection keys, deps root, EF connection string
  via DataPathResolverFactory.CreateFromCurrentProcess()
- OperationLogger: GetDefaultLogDirectory / GetDefaultBackupDirectory now
  take IDataPathResolver
- DepsRootHolder: static bootstrap-time setter so module Dependencies
  property initializers can read resolver paths without DI plumbing
- Go2RtcService: drop dead AppContext.BaseDirectory fallback
- ControlMenu.csproj: bump Version 1.0.1 -> 1.1.0-alpha.1; ProjectReference
  to ControlMenu.Common; fix SkiaSharp PackageReference indentation (TODO #26)
- ControlMenu.Tests.csproj: rewrite as UTF-8 without BOM + LF (TODO #25)

383/383 existing tests pass with injected TestPathResolver where needed.
'@
```

---

## Task 7: Port `single_instance.rs` → `SingleInstance.cs` (TDD)

**Files:**
- Create: `tests/ControlMenuLauncher.Tests/SingleInstanceTests.cs`
- Create: `src/ControlMenuLauncher/SingleInstance.cs`

**Legacy reference:** `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/single_instance.rs:1-220`

Named-mutex guard that admits ONE non-elevated launcher AND ONE elevated launcher concurrently (so a user can run the normal app and right-click → Run as administrator for service-uninstall). Mutex names:
- `Local\ControlMenuLauncher.SingleInstance.User` (medium integrity)
- `Local\ControlMenuLauncher.SingleInstance.Admin` (high integrity)

(Spec § "Phase 1 sub-deliverables" said `Global\ControlMenuLauncher.SingleInstance` — that's a one-line shortcut. The legacy uses `Local\` namespace + elevation suffix. Mirror legacy.)

- [ ] **Step 1: Write failing tests**

```csharp
using ControlMenu.Launcher;
using Xunit;

namespace ControlMenu.Launcher.Tests;

public class SingleInstanceTests
{
    [Fact]
    public void CurrentMutexName_IncludesElevationSuffix()
    {
        var name = SingleInstance.CurrentMutexName();
        Assert.StartsWith(@"Local\ControlMenuLauncher.SingleInstance.", name);
        Assert.True(name.EndsWith(".User") || name.EndsWith(".Admin"));
    }

    [Fact]
    public void Acquire_FirstCall_ReturnsHandle()
    {
        var name = "Local\\ControlMenuLauncher.SingleInstanceTest." + Guid.NewGuid().ToString("N");
        using var handle = SingleInstance.Acquire(name);
        Assert.NotNull(handle);
    }

    [Fact]
    public void Acquire_TwiceSameNameSameProcess_SecondReturnsNull()
    {
        var name = "Local\\ControlMenuLauncher.SingleInstanceTest." + Guid.NewGuid().ToString("N");
        using var first = SingleInstance.Acquire(name);
        Assert.NotNull(first);
        var second = SingleInstance.Acquire(name);
        Assert.Null(second);
    }

    [Fact]
    public void Acquire_AfterFirstReleased_SecondSucceeds()
    {
        var name = "Local\\ControlMenuLauncher.SingleInstanceTest." + Guid.NewGuid().ToString("N");
        var first = SingleInstance.Acquire(name);
        Assert.NotNull(first);
        first!.Dispose();
        using var second = SingleInstance.Acquire(name);
        Assert.NotNull(second);
    }
}
```

- [ ] **Step 2: Run; verify failure.**

- [ ] **Step 3: Implement `SingleInstance.cs`**

```csharp
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Threading;

namespace ControlMenu.Launcher;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly bool _ownsLock;

    private SingleInstance(Mutex mutex, bool ownsLock)
    {
        _mutex = mutex;
        _ownsLock = ownsLock;
    }

    public static string CurrentMutexName()
    {
        var suffix = IsElevated() ? "Admin" : "User";
        return $"Local\\ControlMenuLauncher.SingleInstance.{suffix}";
    }

    /// <summary>
    /// Try to acquire the named mutex. Returns a handle on success. Returns
    /// <c>null</c> if another instance already holds the mutex.
    /// Mirrors single_instance.rs:acquire — same return semantics
    /// (Some(guard) / None / panic→Err).
    /// </summary>
    public static SingleInstance? Acquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: false, name: name, createdNew: out _);
        bool ownsLock;
        try
        {
            ownsLock = mutex.WaitOne(TimeSpan.Zero, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            // Previous owner crashed without release. We "win" the mutex —
            // matches the Rust crate's behavior on Windows.
            ownsLock = true;
        }

        if (!ownsLock)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstance(mutex, ownsLock);
    }

    [SupportedOSPlatform("windows")]
    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_ownsLock) _mutex.ReleaseMutex();
        }
        catch { /* swallow — process is exiting */ }
        finally
        {
            _mutex.Dispose();
        }
    }
}
```

- [ ] **Step 4: Run tests; verify they pass.**

- [ ] **Step 5: Diff against legacy**

> Diff your scaffold against `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/single_instance.rs:1-220`. For every difference, write a one-line rationale. If you can't justify a difference, change your scaffold to match legacy behavior.

Specifically check:
- Mutex name suffix capitalization (`User` / `Admin`) and namespace (`Local\` vs `Global\`). Spec § "Phase 1 sub-deliverables" mentioned `Global\` — that contradicts legacy. Mirror legacy (`Local\`) and note the spec drift.
- The legacy uses raw Win32 `CreateMutexW` → `WaitForSingleObject`. .NET's `Mutex` class wraps the same underlying primitive; verify the named-mutex semantics align (`Local\` namespace prefix is honored by .NET's `Mutex(bool, string)` ctor).
- Elevation detection — legacy uses `OpenProcessToken` + `GetTokenInformation(TokenElevation)`. .NET's `WindowsPrincipal.IsInRole(Administrator)` is a near-equivalent. Confirm acceptable; note any edge cases (UAC-aware vs. true-elevated nuance).

- [ ] **Step 6: Commit**

```powershell
git -C C:/Users/jscha/source/repos/control-menu add src/ControlMenuLauncher/SingleInstance.cs tests/ControlMenuLauncher.Tests/SingleInstanceTests.cs
git -C C:/Users/jscha/source/repos/control-menu commit -m "feat(launcher): port single_instance.rs to SingleInstance"
```

---

## Task 8: Port `install_acl.rs` → `InstallAcl.cs`

**Files:**
- Create: `src/ControlMenuLauncher/InstallAcl.cs`

**Legacy reference:** `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/install_acl.rs:1-170`

Side-effect-only against the filesystem + Win32. No TDD — mocking `ShellExecuteEx` produces false confidence per the spec § "Test layers". Manual smoke (Task 16) covers it.

- [ ] **Step 1: Implement `InstallAcl.cs`**

```csharp
using System.Diagnostics;
using System.Runtime.Versioning;
using ControlMenu.Common.Logging;

namespace ControlMenu.Launcher;

[SupportedOSPlatform("windows")]
public static class InstallAcl
{
    private const string SentinelFileName = ".controlmenu-write-test";
    private const string AclSidAuthUsers = "*S-1-5-11";

    /// <summary>
    /// Test whether the running user can write to <paramref name="path"/> by
    /// creating + deleting a sentinel file. Distinct filename from Velopack's
    /// own probe so concurrent self-tests don't race.
    /// </summary>
    public static bool IsWritable(string path)
    {
        var testPath = Path.Combine(path, SentinelFileName);
        try
        {
            File.WriteAllBytes(testPath, []);
            File.Delete(testPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ensure the install root has Authenticated Users:Modify (OI)(CI). If
    /// already writable, returns without prompting. Otherwise invokes
    /// icacls.exe via runas-elevated ShellExecuteEx; UAC prompt fires.
    /// Failure (UAC dismissed, no admin available) is logged and swallowed —
    /// the app works without this grant; only the in-app updater is
    /// degraded (Velopack writability self-test fails → falls back to
    /// LocalAppData → silent swap failures).
    ///
    /// Mirrors install_acl.rs:ensure_writable.
    /// </summary>
    public static void EnsureWritable(string installRoot)
    {
        if (IsWritable(installRoot))
        {
            LauncherLogger.Info($"install-root already writable: {installRoot}");
            return;
        }

        LauncherLogger.Info($"install-root not writable; requesting elevated icacls grant: {installRoot}");

        var args = $"\"{installRoot}\" /grant {AclSidAuthUsers}:(OI)(CI)M /T /C /Q";
        var psi = new ProcessStartInfo
        {
            FileName = "icacls.exe",
            Arguments = args,
            Verb = "runas",  // triggers UAC
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var p = Process.Start(psi);
            if (p is null)
            {
                LauncherLogger.Error("icacls.exe Process.Start returned null");
                return;
            }
            p.WaitForExit(TimeSpan.FromSeconds(30));
            if (p.ExitCode != 0)
            {
                LauncherLogger.Error($"icacls exited {p.ExitCode}; install-root may still lack Authenticated Users:Modify");
            }
            else
            {
                LauncherLogger.Info("install-root ACL grant succeeded");
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when ((uint)ex.NativeErrorCode == 0x800704C7 /* ERROR_CANCELLED */)
        {
            LauncherLogger.Error("UAC prompt was dismissed; in-app updater will be degraded until user re-grants");
        }
        catch (Exception ex)
        {
            LauncherLogger.Error($"install-root ACL grant failed: {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

```powershell
dotnet build C:/Users/jscha/source/repos/control-menu/src/ControlMenuLauncher -c Release 2>&1 | Select-String "Build succeeded|error"
```

Expected: clean build.

- [ ] **Step 3: Diff against legacy**

> Diff your scaffold against `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/install_acl.rs:1-170`. For every difference, write a one-line rationale. If you can't justify a difference, change your scaffold to match legacy behavior.

Specifically:
- Sentinel filename — Rust uses `.ws-scrcpy-write-test`. CM uses `.controlmenu-write-test`. Distinct per-app → fine.
- ACL string format — Rust writes `*S-1-5-11:(OI)(CI)M`. .NET version uses identical string. Match.
- UAC dismissal handling — Rust catches the specific `ERROR_CANCELLED` (winerror 1223 = `0x800704C7`); .NET catches `Win32Exception` with the same code. Confirm by reading the legacy.
- Wait timeout — Rust uses `INFINITE`. .NET uses 30s. Trade-off: legacy will hang if icacls hangs; .NET version forces a deadline. Rationale: 30s is more than enough for icacls on a typical Program Files dir; if a user has a bizarre setup that takes longer, log and continue (degraded updater) rather than freeze the launcher. Keep 30s.

- [ ] **Step 4: Commit**

```powershell
git -C C:/Users/jscha/source/repos/control-menu add src/ControlMenuLauncher/InstallAcl.cs
git -C C:/Users/jscha/source/repos/control-menu commit -m "feat(launcher): port install_acl.rs runas-elevated icacls grant"
```

---

## Task 9: Port `hooks.rs` → `VelopackHookDispatcher.cs` (TDD)

**Files:**
- Create: `tests/ControlMenuLauncher.Tests/Hooks/VelopackHookDispatcherTests.cs`
- Create: `src/ControlMenuLauncher/Hooks/VelopackHookDispatcher.cs`

**Legacy reference:** `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/hooks.rs:1-607`

Parses `--veloapp-*` flags BEFORE `VelopackApp.Build().Run()`, runs each side effect synchronously, exits. Catch-all (Gotcha 4) ensures unknown lifecycle flags don't trigger the v0.1.22 spawn-loop.

- [ ] **Step 1: Write failing tests** (focus on the parser; handler bodies are mostly process-side-effect-only and covered by smoke #1).

```csharp
using ControlMenu.Launcher.Hooks;
using Xunit;

namespace ControlMenu.Launcher.Tests.Hooks;

public class VelopackHookDispatcherTests
{
    [Theory]
    [InlineData(new[] { "--veloapp-install", "1.1.0" }, HookKind.Install)]
    [InlineData(new[] { "--veloapp-updated", "1.1.0" }, HookKind.Updated)]
    [InlineData(new[] { "--veloapp-uninstall", "1.1.0" }, HookKind.Uninstall)]
    [InlineData(new[] { "--veloapp-obsolete", "1.0.99" }, HookKind.Obsolete)]
    public void ParseHookFlag_KnownFlag_ReturnsKind(string[] args, HookKind expected)
    {
        var kind = VelopackHookDispatcher.ParseHookFlag(args);
        Assert.NotNull(kind);
        Assert.Equal(expected, kind!.Kind);
    }

    [Fact]
    public void ParseHookFlag_UnknownVeloappFlag_ReturnsUnknownWithFlagText()
    {
        var kind = VelopackHookDispatcher.ParseHookFlag(["--veloapp-future-thing", "v"]);
        Assert.NotNull(kind);
        Assert.Equal(HookKind.Unknown, kind!.Kind);
        Assert.Equal("--veloapp-future-thing", kind.RawFlag);
    }

    [Fact]
    public void ParseHookFlag_NoVeloappFlag_ReturnsNull()
    {
        Assert.Null(VelopackHookDispatcher.ParseHookFlag(["--some-other-flag", "value"]));
        Assert.Null(VelopackHookDispatcher.ParseHookFlag([]));
    }

    [Fact]
    public void ParseHookFlag_KnownFlagPrecedesUnknown_ReturnsKnown()
    {
        // Mirrors the legacy invariant: "Recognized flags take precedence
        // over Unknown — we never catch-all over a known flag."
        var kind = VelopackHookDispatcher.ParseHookFlag(["--veloapp-future-thing", "--veloapp-install", "v"]);
        Assert.NotNull(kind);
        Assert.Equal(HookKind.Install, kind!.Kind);
    }
}
```

- [ ] **Step 2: Run; verify failure.**

- [ ] **Step 3: Implement parser + dispatcher**

```csharp
using ControlMenu.Common.Logging;

namespace ControlMenu.Launcher.Hooks;

public enum HookKind { Install, Updated, Uninstall, Obsolete, Unknown }

public sealed class HookFlag
{
    public HookKind Kind { get; init; }
    public string? RawFlag { get; init; }
    public string? VersionArg { get; init; }
}

public static class VelopackHookDispatcher
{
    private const string FlagInstall = "--veloapp-install";
    private const string FlagUpdated = "--veloapp-updated";
    private const string FlagUninstall = "--veloapp-uninstall";
    private const string FlagObsolete = "--veloapp-obsolete";
    private const string FlagPrefix = "--veloapp-";

    /// <summary>
    /// Pure parser. Scans args for a Velopack hook flag. Recognized flags
    /// take precedence over Unknown — we never catch-all over a known flag.
    /// Mirrors hooks.rs:parse_hook_flag.
    /// </summary>
    public static HookFlag? ParseHookFlag(IReadOnlyList<string> args)
    {
        string? unknown = null;
        for (int i = 0; i < args.Count; i++)
        {
            var a = args[i];
            switch (a)
            {
                case FlagInstall:   return new HookFlag { Kind = HookKind.Install,   RawFlag = a, VersionArg = NextOrNull(args, i) };
                case FlagUpdated:   return new HookFlag { Kind = HookKind.Updated,   RawFlag = a, VersionArg = NextOrNull(args, i) };
                case FlagUninstall: return new HookFlag { Kind = HookKind.Uninstall, RawFlag = a, VersionArg = NextOrNull(args, i) };
                case FlagObsolete:  return new HookFlag { Kind = HookKind.Obsolete,  RawFlag = a, VersionArg = NextOrNull(args, i) };
                default:
                    if (a.StartsWith(FlagPrefix, StringComparison.Ordinal) && unknown is null)
                        unknown = a;
                    break;
            }
        }
        return unknown is null ? null : new HookFlag { Kind = HookKind.Unknown, RawFlag = unknown };
    }

    private static string? NextOrNull(IReadOnlyList<string> args, int i) =>
        (i + 1 < args.Count) ? args[i + 1] : null;

    /// <summary>
    /// Public entry. If argv contains a Velopack hook flag, handle the side
    /// effect synchronously and return an exit code. Otherwise return null
    /// (caller proceeds to normal launch).
    /// Mirrors hooks.rs:handle_velopack_hook.
    /// </summary>
    public static int? HandleVelopackHook(IReadOnlyList<string> args, string installRoot)
    {
        var flag = ParseHookFlag(args);
        if (flag is null) return null;

        LauncherLogger.Info($"hook: dispatching {flag.Kind} (raw: {flag.RawFlag})");

        return flag.Kind switch
        {
            HookKind.Install   => HandleInstall(installRoot, flag.VersionArg),
            HookKind.Updated   => HandleUpdated(installRoot, flag.VersionArg),
            HookKind.Uninstall => HandleUninstall(installRoot, flag.VersionArg),
            HookKind.Obsolete  => HandleObsolete(installRoot, flag.VersionArg),
            HookKind.Unknown   => HandleUnknown(flag.RawFlag!),
            _ => 0,
        };
    }

    private static int HandleInstall(string installRoot, string? version)
    {
        // Phase 1: log and exit cleanly. Phase 3 may add config.json
        // skeleton write here. Mirrors hooks.rs:handle_install (line refs
        // to be filled in during port-diff in Step 5).
        LauncherLogger.Info($"hook install: version={version}; install_root={installRoot} (Phase 1: noop)");
        return 0;
    }

    private static int HandleUpdated(string installRoot, string? version)
    {
        // Phase 1: log and exit cleanly. Phase 3 will gain "if service mode:
        // servy-cli restart" logic.
        LauncherLogger.Info($"hook updated: version={version}; install_root={installRoot} (Phase 1: noop)");
        return 0;
    }

    private static int HandleUninstall(string installRoot, string? version)
    {
        // Phase 1: log and exit cleanly. Phase 3 adds servy stop + uninstall.
        // Always preserve user data (config / deps / logs) per spec.
        LauncherLogger.Info($"hook uninstall: version={version}; install_root={installRoot} (Phase 1: noop)");
        return 0;
    }

    private static int HandleObsolete(string installRoot, string? version)
    {
        // Velopack invokes the OLD launcher with --veloapp-obsolete <old-version>
        // immediately before swapping current\ to the new version. Hook is a
        // chance to clean up state specific to the old version. Mirrors
        // hooks.rs:HookKind::Obsolete docs.
        LauncherLogger.Info($"hook obsolete: old version {version} retiring; install_root={installRoot} (Phase 1: noop)");
        return 0;
    }

    private static int HandleUnknown(string rawFlag)
    {
        // Catch-all per Gotcha 4. Without this, an unknown --veloapp-* flag
        // would fall through to VelopackApp.Build().Run() which might
        // silently consume it and exit, triggering the v0.1.22-style
        // Update.exe respawn loop. Log loudly + exit 0 → Update.exe sees
        // success and stops retrying.
        LauncherLogger.Error($"hook unknown: caught unrecognized {rawFlag}; logging + exiting 0 to break Update.exe respawn loop. Add a real handler in next release.");
        return 0;
    }
}
```

- [ ] **Step 4: Run tests; verify they pass.**

- [ ] **Step 5: Diff against legacy**

> Diff your scaffold against `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/hooks.rs:1-607`. For every difference, write a one-line rationale. If you can't justify a difference, change your scaffold to match legacy behavior.

Specifically reconcile each handler body:
- `HandleInstall` — ws-scrcpy-web's writes a skeleton `config.json` if absent. CM's Phase 1 says noop — verify whether CM needs a similar skeleton-config write at install time. If yes, port that logic now (the resolver gives us `GetAppConfigPath()`).
- `HandleUpdated` — ws-scrcpy-web's logic includes "if service mode: servy-cli restart". For CM Phase 1 (no Servy yet) the noop is correct. Note explicitly with a `// Phase 3` comment.
- `HandleUninstall` — ws-scrcpy-web's path includes "stop servy + uninstall service" + "preserve user data". For CM Phase 1, noop is correct (no service yet). Note explicitly.
- `HandleObsolete` — pure logging in legacy as well; aligned.
- `HandleUnknown` (catch-all) — verify the legacy catch-all also returns 0 (not the unknown-flag's error code).

- [ ] **Step 6: Commit**

```powershell
git -C C:/Users/jscha/source/repos/control-menu add src/ControlMenuLauncher/Hooks/VelopackHookDispatcher.cs tests/ControlMenuLauncher.Tests/Hooks/VelopackHookDispatcherTests.cs
git -C C:/Users/jscha/source/repos/control-menu commit -m "feat(launcher): port hooks.rs Velopack hook dispatcher with catch-all (Gotcha 4)"
```

---

## Task 10: Add Velopack NuGet + wire launcher `Program.cs` ordering + child supervisor stub

**Files:**
- Modify: `src/ControlMenuLauncher/ControlMenuLauncher.csproj` — add `<PackageReference Include="Velopack" Version="0.0.x" />` (pin specific version)
- Create: `src/ControlMenuLauncher/Program.cs`
- Create: `src/ControlMenuLauncher/Supervisor/ChildSupervisor.cs`

**Legacy reference:** `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/main.rs:45-56` (entry log + argv) + `:68-75` (hook dispatch) + `:77-108` (install_acl) + `:110-133` (single_instance) + `:135-156` (VelopackApp init — Gotcha 1) + `:198-204` (supervisor::run); `launcher/src/supervisor.rs:1-198` (Phase 1 stub portion only — full port deferred to Phase 3)

- [ ] **Step 1: Pin Velopack NuGet version**

The spec § "Risks called out for the implementation plan" says:
> "Velopack version drift — pin a specific Velopack release (npm `velopack` or `vpk` CLI version) to avoid surprises mid-development."

Run:

```powershell
dotnet package search Velopack --take 1 --format json
```

Capture the `latestVersion` from the output. Lock that version. As of plan-writing the .NET package is `Velopack` on NuGet.org. (If the package is renamed/moved, re-resolve at execution time.)

Edit `src/ControlMenuLauncher/ControlMenuLauncher.csproj` to add (replace the picked version verbatim):

```xml
  <ItemGroup>
    <PackageReference Include="Velopack" Version="<PINNED-VERSION>" />
  </ItemGroup>
```

- [ ] **Step 2: Implement `ChildSupervisor.cs` (Phase 1 stub)**

```csharp
using System.Diagnostics;
using ControlMenu.Common.Logging;
using ControlMenu.Common.Paths;

namespace ControlMenu.Launcher.Supervisor;

/// <summary>
/// Phase 1 stub. Spawns ControlMenu.exe child, waits for exit, dispatches on
/// exit-code 75 (Velopack apply requested by ControlMenu.exe). Phase 3
/// expands this with proper restart-loop + crash-recovery from supervisor.rs.
/// </summary>
public static class ChildSupervisor
{
    /// <summary>Special exit code: ControlMenu.exe asks the launcher to apply a Velopack update.</summary>
    public const int ExitCodeApplyUpdate = 75;

    public static int Run(IDataPathResolver paths)
    {
        var childExe = Path.Combine(paths.GetCurrentDir(), "ControlMenu.exe");
        if (!File.Exists(childExe))
        {
            LauncherLogger.Error($"child not found: {childExe}");
            return 1;
        }

        LauncherLogger.Info($"spawning child: {childExe}");
        var psi = new ProcessStartInfo
        {
            FileName = childExe,
            WorkingDirectory = paths.GetCurrentDir(),
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        try
        {
            using var p = Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start returned null");
            LauncherLogger.Info($"child PID: {p.Id}");
            p.WaitForExit();
            var code = p.ExitCode;
            LauncherLogger.Info($"child exited with code {code}");

            if (code == ExitCodeApplyUpdate)
            {
                LauncherLogger.Info("child requested apply-update via exit-75; Phase 1: log + exit (Velopack apply orchestration lands in Phase 3)");
                // Phase 1 ends here. Phase 3 will: pre-apply daemon hygiene
                // → invoke UpdateManager.ApplyUpdatesAndExit → Servy
                // restart-delay handles relaunch.
            }
            return code;
        }
        catch (Exception ex)
        {
            LauncherLogger.Error($"supervisor failed: {ex.Message}");
            return 1;
        }
    }
}
```

- [ ] **Step 3: Implement `Program.cs` mirroring `main.rs:18-156` ordering**

```csharp
using System.Diagnostics;
using ControlMenu.Common.Logging;
using ControlMenu.Common.Paths;
using ControlMenu.Launcher;
using ControlMenu.Launcher.Hooks;
using ControlMenu.Launcher.Supervisor;

namespace ControlMenu.Launcher;

internal static class Program
{
    private const string Version = "1.1.0-alpha.1";

    private static int Main(string[] args)
    {
        // Composition root: derive paths first so logger has somewhere to land.
        IDataPathResolver paths;
        try
        {
            paths = DataPathResolverFactory.CreateFromCurrentProcess();
            Directory.CreateDirectory(paths.GetLogsDir());
        }
        catch (Exception ex)
        {
            // Last-resort: write to %TEMP% so we have a paper trail before
            // exiting. main.rs uses a similar fallback.
            var fallbackLog = Path.Combine(Path.GetTempPath(), "ControlMenuLauncher-bootstrap-failure.log");
            File.AppendAllText(fallbackLog, $"{DateTime.UtcNow:o} bootstrap failed: {ex}\n");
            return 1;
        }

        LauncherLogger.Init(Path.Combine(paths.GetLogsDir(), "launcher.log"));
        LauncherLogger.Info($"ControlMenuLauncher v{Version} starting");
        LauncherLogger.Info($"argv: [{string.Join(", ", args.Select(a => $"\"{a}\""))}]");

        // 1. Velopack lifecycle hook dispatch — BEFORE VelopackApp.Build().Run().
        //    Mirrors main.rs:68-75. We catch unknown --veloapp-* flags here too
        //    (Gotcha 4) so VelopackApp.Run() never silently consumes them.
        var hookExit = VelopackHookDispatcher.HandleVelopackHook(args, paths.GetInstallRoot());
        if (hookExit is int code)
        {
            LauncherLogger.Info($"hook handler exiting with code {code}");
            return code;
        }

        // 2. Install-root ACL grant via runas-elevated icacls.
        //    Mirrors main.rs:77-108. Velopack PerMachine Gotchas 2 + 3.
        if (OperatingSystem.IsWindows())
        {
            try
            {
                InstallAcl.EnsureWritable(paths.GetInstallRoot());
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"ACL grant top-level failure (in-app updater degraded): {ex.Message}");
            }
        }

        // 3. Single-instance guard. Acquired AFTER hook dispatch (hooks are
        //    short-lived single-shots that legitimately race with a running
        //    instance). Mirrors main.rs:110-133.
        var mutexName = SingleInstance.CurrentMutexName();
        var instance = SingleInstance.Acquire(mutexName);
        if (instance is null)
        {
            LauncherLogger.Info("another ControlMenuLauncher instance is already running; exiting");
            return 0;
        }

        try
        {
            // 4. VelopackApp init. MUST be the first executable code path on
            //    the normal-launch branch per SP3 P2 Contract 5 + Gotcha 1
            //    (auto-apply default true → infinite Update.exe re-fire loop
            //    after a successful apply, because the .nupkg stays in
            //    packages\). Mirror main.rs:135-156 verbatim.
            Velopack.VelopackApp.Build()
                .SetAutoApplyOnStartup(false)
                .Run(args);

            // 5. Phase 1: directly run child supervisor. Phase 2 inserts tray
            //    spawn here (main.rs:158-196). Phase 3 expands supervisor with
            //    crash-restart loop.
            return ChildSupervisor.Run(paths);
        }
        finally
        {
            instance.Dispose();
            LauncherLogger.Info("ControlMenuLauncher exiting");
            LauncherLogger.Flush();
        }
    }
}
```

- [ ] **Step 4: Build to verify**

```powershell
dotnet build C:/Users/jscha/source/repos/control-menu/src/ControlMenuLauncher -c Release 2>&1 | Select-String "Build succeeded|error"
```

Expected: clean build. Resolves the `Velopack` reference; compiles `Program.cs`.

If the package is unavailable or the API surface differs (e.g. `VelopackApp.Build()` is now `VelopackApp.Configure()` or similar): use Context7 to resolve the current Velopack .NET API:

```powershell
# Check current API via context7 MCP server
# (use ToolSearch to load mcp__plugin_context7_context7__* tools first)
```

Reconcile API drift; commit the version pin update separately if you bumped Velopack.

- [ ] **Step 5: Diff against legacy**

> Diff your scaffold against `C:/Users/jscha/source/repos/ws-scrcpy-web/launcher/src/main.rs:18-156` and `:198-204`, AND against `launcher/src/supervisor.rs:1-198` (Phase 1 stub portion). For every difference, write a one-line rationale.

Specifically:
- Ordering of the 5 numbered steps must match `main.rs` exactly. Hook BEFORE ACL BEFORE single-instance BEFORE VelopackApp BEFORE supervisor. Verify each.
- The legacy has an `--print-active-session` shortcut at the very top (`main.rs:18-43`) — Phase 3 (service detection). DO NOT add it now; instead leave a `// Phase 3: --print-active-session shortcut goes here, before LauncherLogger.Info` comment so the future port-diff sees the marker.
- The legacy has `elevated_runner::handle(args)` between hook check and Velopack hook (`main.rs:58-66`). Phase 3. Same comment-marker treatment.
- The legacy's `--local-takeover` override (`main.rs:183-194`) — Phase 3 (post-uninstall handoff). Same comment-marker.

- [ ] **Step 6: Commit**

```powershell
git -C C:/Users/jscha/source/repos/control-menu add src/ControlMenuLauncher/ControlMenuLauncher.csproj src/ControlMenuLauncher/Program.cs src/ControlMenuLauncher/Supervisor/ChildSupervisor.cs
git -C C:/Users/jscha/source/repos/control-menu commit -m @'
feat(launcher): wire main.rs:18-156 ordering + Phase 1 child supervisor stub

- Velopack NuGet pinned at <VERSION>
- Program.cs mirrors main.rs entry: hook dispatch -> install_acl ->
  single_instance -> VelopackApp.Build().SetAutoApplyOnStartup(false).Run(args)
  -> ChildSupervisor.Run()
- ChildSupervisor: spawn ControlMenu.exe, wait, log on exit-75 (Phase 3
  expands with apply-update orchestration)
- Comment markers left for Phase 2 (tray spawn) and Phase 3
  (--print-active-session, elevated_runner, --local-takeover)
'@
```

---

## Task 11: `VelopackUpdateService` in `ControlMenu.exe` + Settings → General "Check for updates" UI

**Files:**
- Create: `src/ControlMenu/Services/Update/IVelopackUpdateService.cs`
- Create: `src/ControlMenu/Services/Update/VelopackUpdateService.cs`
- Modify: `src/ControlMenu/ControlMenu.csproj` — add `Velopack` PackageReference (pinned to same version as launcher)
- Modify: `src/ControlMenu/Program.cs` — register `IVelopackUpdateService`
- Modify: `src/ControlMenu/Components/Pages/Settings/GeneralSettings.razor` — add "Check for updates" button + status display

**Legacy reference:** None — this is the .NET `Velopack.UpdateManager` consumer-side; no Rust equivalent in ws-scrcpy-web (their Node-side checks updates via `velopack` npm package, but the .NET API for this is canonical).

- [ ] **Step 1: Confirm Velopack .NET API surface for `UpdateManager`**

Use Context7 to check the current API. The expected surface is approximately:

```csharp
var mgr = new UpdateManager(new GithubSource("https://github.com/owner/repo", null, false));
var info = await mgr.CheckForUpdatesAsync();      // -> UpdateInfo? (null = no update)
await mgr.DownloadUpdatesAsync(info);              // downloads to packages\
mgr.ApplyUpdatesAndExit(info);                     // synchronous; process exits 75 then Velopack restarts
```

Use `mcp__plugin_context7_context7__query-docs` with library ID for Velopack to confirm.

- [ ] **Step 2: Implement service interface + body**

`src/ControlMenu/Services/Update/IVelopackUpdateService.cs`:

```csharp
namespace ControlMenu.Services.Update;

public interface IVelopackUpdateService
{
    Task<UpdateAvailability> CheckForUpdatesAsync(CancellationToken ct = default);
    Task DownloadUpdateAsync(CancellationToken ct = default);
    /// <summary>Sets exit code 75 + requests app shutdown. Launcher's supervisor sees the code and runs Velopack apply.</summary>
    void RequestApplyUpdate();
}

public sealed record UpdateAvailability(bool HasUpdate, string? AvailableVersion, string? CurrentVersion);
```

`src/ControlMenu/Services/Update/VelopackUpdateService.cs`:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace ControlMenu.Services.Update;

public sealed class VelopackUpdateService : IVelopackUpdateService
{
    private const string GitHubRepo = "https://github.com/bilbospocketses/control-menu";
    private readonly UpdateManager _manager;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<VelopackUpdateService> _log;
    private UpdateInfo? _pending;

    public VelopackUpdateService(IHostApplicationLifetime lifetime, ILogger<VelopackUpdateService> log)
    {
        _lifetime = lifetime;
        _log = log;
        _manager = new UpdateManager(new GithubSource(GitHubRepo, accessToken: null, prerelease: false));
    }

    public async Task<UpdateAvailability> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        if (!_manager.IsInstalled)
        {
            _log.LogInformation("Velopack not installed (running from dev tree); update check skipped");
            return new UpdateAvailability(false, null, null);
        }

        try
        {
            _pending = await _manager.CheckForUpdatesAsync().WaitAsync(ct);
            var current = _manager.CurrentVersion?.ToString();
            if (_pending is null) return new UpdateAvailability(false, null, current);
            return new UpdateAvailability(true, _pending.TargetFullRelease.Version.ToString(), current);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CheckForUpdatesAsync failed");
            return new UpdateAvailability(false, null, _manager.CurrentVersion?.ToString());
        }
    }

    public async Task DownloadUpdateAsync(CancellationToken ct = default)
    {
        if (_pending is null) throw new InvalidOperationException("No pending update; call CheckForUpdatesAsync first");
        await _manager.DownloadUpdatesAsync(_pending).WaitAsync(ct);
    }

    /// <summary>
    /// Exit-75 is the contract between ControlMenu.exe and ControlMenuLauncher.exe:
    /// the launcher's <c>ChildSupervisor.ExitCodeApplyUpdate</c> reads this code
    /// to know to run pre-apply hygiene + (Phase 3) Velopack apply orchestration.
    /// Hardcoded here because ControlMenu.csproj does NOT reference
    /// ControlMenuLauncher.csproj — the supervisor is the parent in the runtime
    /// graph. Keep the constant value (75) in sync between this file and
    /// <c>src/ControlMenuLauncher/Supervisor/ChildSupervisor.cs</c>.
    /// </summary>
    public void RequestApplyUpdate()
    {
        if (_pending is null) throw new InvalidOperationException("No downloaded update to apply");
        _log.LogInformation("Requesting Velopack apply via exit-75");
        Environment.ExitCode = 75;
        _lifetime.StopApplication();
    }
}
```

- [ ] **Step 3: Add Velopack NuGet to ControlMenu.csproj + register service**

Edit `src/ControlMenu/ControlMenu.csproj`:

Replace:
```xml
    <PackageReference Include="SkiaSharp" Version="3.119.2" />
  </ItemGroup>
```

With:
```xml
    <PackageReference Include="SkiaSharp" Version="3.119.2" />
    <PackageReference Include="Velopack" Version="<PINNED-VERSION>" />
  </ItemGroup>
```

(Use the same version pinned in launcher.)

In `src/ControlMenu/Program.cs`, in the service-registration block, add:

```csharp
builder.Services.AddSingleton<ControlMenu.Services.Update.IVelopackUpdateService, ControlMenu.Services.Update.VelopackUpdateService>();
```

- [ ] **Step 4: Add UI to `GeneralSettings.razor`**

Read the current `GeneralSettings.razor`. Identify the existing Settings section pattern (uses `<SettingsSection>` / `<SettingsGrid>` per item #2 in the TODO).

Add a new section near the bottom:

```razor
@inject IVelopackUpdateService UpdateService

<!-- Existing sections above this stay as-is. -->

<SettingsSection Title="Updates" Icon="bi-arrow-repeat">
    <SettingsGrid>
        <SettingsGridCell FullRow="true">
            <Label>Application updates</Label>
            <ChildContent>
                <button class="btn btn-secondary" @onclick="HandleCheckForUpdates" disabled="@_checking">
                    <i class="bi bi-arrow-repeat"></i> @(_checking ? "Checking…" : "Check for updates")
                </button>
                @if (_lastResult is { } r)
                {
                    @if (r.HasUpdate)
                    {
                        <div class="mt-2">Update available: <strong>@r.AvailableVersion</strong> (current: @r.CurrentVersion)</div>
                        <button class="btn btn-primary mt-2" @onclick="HandleDownloadAndApply" disabled="@_applying">
                            @(_applying ? "Applying…" : "Download and apply (app will restart)")
                        </button>
                    }
                    else
                    {
                        <div class="mt-2 text-muted">No updates available. Current: @r.CurrentVersion</div>
                    }
                }
                @if (_error is not null)
                {
                    <div class="mt-2 text-danger">@_error</div>
                }
            </ChildContent>
            <Hint>Checks GitHub releases for a newer Control Menu. Apply will restart the app.</Hint>
        </SettingsGridCell>
    </SettingsGrid>
</SettingsSection>

@code {
    private bool _checking;
    private bool _applying;
    private UpdateAvailability? _lastResult;
    private string? _error;

    private async Task HandleCheckForUpdates()
    {
        _checking = true; _error = null; _lastResult = null;
        StateHasChanged();
        try
        {
            _lastResult = await UpdateService.CheckForUpdatesAsync();
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _checking = false; StateHasChanged(); }
    }

    private async Task HandleDownloadAndApply()
    {
        _applying = true; _error = null;
        StateHasChanged();
        try
        {
            await UpdateService.DownloadUpdateAsync();
            UpdateService.RequestApplyUpdate();
            // RequestApplyUpdate calls _lifetime.StopApplication; the
            // SignalR circuit will tear down. No further StateHasChanged
            // matters past this point.
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            _applying = false;
            StateHasChanged();
        }
    }
}
```

Per `feedback_razor_quotes.md` — no string literals in `@on*` lambdas. We've used dedicated `HandleCheckForUpdates` / `HandleDownloadAndApply` methods. ✓

- [ ] **Step 5: Build + run the existing test suite**

```powershell
dotnet build C:/Users/jscha/source/repos/control-menu -c Release 2>&1 | Select-String "Build succeeded|error"
dotnet test C:/Users/jscha/source/repos/control-menu -c Release --no-build --nologo 2>&1 | Select-String "Passed:|Failed:"
```

Expected: clean build. 383 ControlMenu.Tests still pass + the new Common.Tests + Launcher.Tests stay green.

- [ ] **Step 6: Manual smoke — `dotnet run` and click the button**

Run the app:

```powershell
dotnet run --project C:/Users/jscha/source/repos/control-menu/src/ControlMenu -c Release
```

Open `http://localhost:5159/settings/general`. Scroll to the new "Updates" section. Click "Check for updates".

Expected behavior in dev mode (no Velopack install): the service logs `"Velopack not installed (running from dev tree); update check skipped"`, returns `HasUpdate=false`, UI shows "No updates available. Current: <empty>".

If it crashes or shows an unhandled exception: read `dataPathResolver.GetLogsDir()` → `controlmenu.log` for stack trace; address before proceeding.

- [ ] **Step 7: Commit**

```powershell
git -C C:/Users/jscha/source/repos/control-menu add src/ControlMenu/Services/Update/IVelopackUpdateService.cs src/ControlMenu/Services/Update/VelopackUpdateService.cs src/ControlMenu/ControlMenu.csproj src/ControlMenu/Program.cs src/ControlMenu/Components/Pages/Settings/GeneralSettings.razor
git -C C:/Users/jscha/source/repos/control-menu commit -m "feat(updates): VelopackUpdateService + Settings -> General check-for-updates UI"
```

---

## Task 12: Audit daemon spawn cwd-anchoring (Velopack PerMachine Gotcha 9)

**Files:**
- Read-only audit + targeted fixes if any spawn anchors under `current\`.

**Legacy reference:** N/A — CM-side hygiene check informed by `feedback_velopack_permachine_lessons.md` Gotcha 9. `launcher/src/job_object.rs` is the pattern reference for *why* this matters but CM does NOT adopt the Job Object pattern.

The spec § "Velopack PerMachine lessons applied" Gotcha 9:
> ✅ adb / scrcpy / sqlite3 / go2rtc all live under `C:\ProgramData\ControlMenu\dependencies\` so they're outside `current\` by construction.

But spawned **child processes** also need to NOT inherit a working directory under `current\` — Velopack's swap step renames `current\` mid-flight.

- [ ] **Step 1: Find all `Process.Start` / `ProcessStartInfo` sites**

```powershell
git -C C:/Users/jscha/source/repos/control-menu grep -n "Process\.Start\|new ProcessStartInfo\|ICommandExecutor"
```

Expected sites (review each):
- `src/ControlMenu/Services/CommandExecutor.cs` — the central `ICommandExecutor`. If `WorkingDirectory` is unset, default is `Environment.CurrentDirectory` — which when launched by Velopack stub is `current\`. ⚠
- `src/ControlMenu/Modules/AndroidDevices/Services/AdbService.cs`
- `src/ControlMenu/Modules/Cameras/Services/Go2RtcService.cs`
- `src/ControlMenu/Services/DependencyManagerService.cs`
- `src/ControlMenuLauncher/Supervisor/ChildSupervisor.cs` — already sets `WorkingDirectory = paths.GetCurrentDir()` for the ControlMenu.exe child, which IS under current\. That's intentional for the child, but the child's grandchildren must anchor elsewhere.

- [ ] **Step 2: Fix `CommandExecutor` to anchor at the binary's directory by default**

Read `src/ControlMenu/Services/CommandExecutor.cs`. Find the `ProcessStartInfo` construction. If `WorkingDirectory` is not explicitly set per call, default to:

```csharp
psi.WorkingDirectory = Path.GetDirectoryName(psi.FileName) ?? Environment.CurrentDirectory;
```

This anchors each spawn at the binary's own directory under `<dataRoot>\dependencies\<tool>\<version>\`, which is OUTSIDE `current\` and survives the Velopack swap.

- [ ] **Step 3: Spot-check each consumer**

For each of `AdbService`, `Go2RtcService`, `DependencyManagerService`: trace whether the call passes through `ICommandExecutor`. If yes, no further change needed. If a consumer constructs its own `ProcessStartInfo` (bypasses the executor), apply the same anchor rule there.

- [ ] **Step 4: Build + test**

```powershell
dotnet build C:/Users/jscha/source/repos/control-menu -c Release 2>&1 | Select-String "Build succeeded|error"
dotnet test C:/Users/jscha/source/repos/control-menu -c Release --no-build --nologo 2>&1 | Select-String "Passed:|Failed:"
```

Expected: clean. 383+ tests pass.

- [ ] **Step 5: Commit (if any changes)**

```powershell
git -C C:/Users/jscha/source/repos/control-menu add -u
git -C C:/Users/jscha/source/repos/control-menu commit -m @'
fix(spawn): anchor child-process working directory at binary's dir (Gotcha 9)

Velopack's swap step renames current\ mid-flight; any process holding a
working directory under current\ races the rename. Default cwd for spawned
processes is now the binary's own dir, which lives at
<dataRoot>\dependencies\<tool>\<version>\ — outside current\ by construction.

No-op if all sites already passed an explicit WorkingDirectory.
'@
```

If audit found nothing to change: skip the commit and add an "Audit finding: clean" note to the smoke #1 runbook entry.

---

## Task 13: Pre-apply daemon hygiene

**Files:**
- Modify: `src/ControlMenuLauncher/Supervisor/ChildSupervisor.cs` (extend Phase 1 stub) OR create `src/ControlMenuLauncher/Supervisor/PreApplyHygiene.cs`

**Legacy reference:** The behavior pattern is from `feedback_velopack_permachine_lessons.md` Gotcha 9 — pre-apply daemon hygiene. ws-scrcpy-web's implementation lives in its supervisor / supervisor-helpers; the CM Phase 1 implementation runs adb-kill-server + taskkill + 250ms settle on the launcher side BEFORE invoking Velopack apply.

For Phase 1, the apply orchestration itself is logged-but-not-fired (Phase 3). What we DO need now is the hygiene helper, ready to be wired in Phase 3 — and a unit test for the kill commands.

- [ ] **Step 1: Implement `PreApplyHygiene.cs`**

```csharp
using System.Diagnostics;
using System.Runtime.Versioning;
using ControlMenu.Common.Logging;
using ControlMenu.Common.Paths;

namespace ControlMenu.Launcher.Supervisor;

[SupportedOSPlatform("windows")]
public static class PreApplyHygiene
{
    /// <summary>
    /// Quiesce daemons that hold file handles under current\ before Velopack
    /// apply renames the directory. Mirror of the Gotcha 9 sequence:
    /// adb kill-server -> taskkill /F /IM adb.exe /T -> 250ms settle.
    /// All errors swallowed — best-effort hygiene; the apply attempt
    /// proceeds either way.
    /// </summary>
    public static async Task RunAsync(IDataPathResolver paths, CancellationToken ct = default)
    {
        LauncherLogger.Info("pre-apply hygiene: starting");

        var adbPath = Path.Combine(paths.GetDependenciesDir(), "platform-tools", "adb.exe");
        if (File.Exists(adbPath))
        {
            await TryRunAsync(adbPath, "kill-server", ct);
        }
        else
        {
            LauncherLogger.Info($"adb not at expected path {adbPath}; skipping kill-server");
        }

        // Belt-and-suspenders: taskkill any remaining adb processes.
        await TryRunAsync("taskkill.exe", "/F /IM adb.exe /T", ct);

        // 250ms settle so the kernel finishes releasing handles before Velopack
        // probes for current\.
        await Task.Delay(TimeSpan.FromMilliseconds(250), ct);

        LauncherLogger.Info("pre-apply hygiene: done");
    }

    private static async Task TryRunAsync(string exe, string args, CancellationToken ct)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (p is null)
            {
                LauncherLogger.Error($"hygiene: Process.Start returned null for {exe} {args}");
                return;
            }
            await p.WaitForExitAsync(ct);
            LauncherLogger.Info($"hygiene: {exe} {args} -> exit {p.ExitCode}");
        }
        catch (Exception ex)
        {
            LauncherLogger.Info($"hygiene: {exe} {args} threw {ex.GetType().Name}: {ex.Message} (continuing)");
        }
    }
}
```

- [ ] **Step 2: Wire stub call from `ChildSupervisor` (Phase 1: invoke + log only)**

In `ChildSupervisor.Run` after detecting exit-75, BEFORE the existing log-and-return:

```csharp
if (code == ExitCodeApplyUpdate)
{
    LauncherLogger.Info("child requested apply-update via exit-75; running pre-apply hygiene");
    if (OperatingSystem.IsWindows())
    {
        PreApplyHygiene.RunAsync(paths).GetAwaiter().GetResult();
    }
    LauncherLogger.Info("Phase 1: pre-apply hygiene complete; Velopack apply orchestration lands in Phase 3");
}
```

- [ ] **Step 3: Build + run launcher tests**

```powershell
dotnet build C:/Users/jscha/source/repos/control-menu -c Release 2>&1 | Select-String "Build succeeded|error"
dotnet test C:/Users/jscha/source/repos/control-menu/tests/ControlMenuLauncher.Tests -c Release --no-build --nologo 2>&1 | Select-String "Passed:|Failed:"
```

Expected: clean.

- [ ] **Step 4: Commit**

```powershell
git -C C:/Users/jscha/source/repos/control-menu add src/ControlMenuLauncher/Supervisor/PreApplyHygiene.cs src/ControlMenuLauncher/Supervisor/ChildSupervisor.cs
git -C C:/Users/jscha/source/repos/control-menu commit -m "feat(launcher): pre-apply daemon hygiene helper (Gotcha 9)"
```

---

## Task 14: `vpk.config` + GitHub feed + local pack scripts

**Files:**
- Create: `vpk.config` at repo root
- Create: `scripts/local-pack.ps1`
- Create: `scripts/fresh-vm-smoke.md`
- Modify: `.gitignore` — add `Releases/`, `*.nupkg`

**Legacy reference:** ws-scrcpy-web's `vpk.config` and `scripts/local-pack.ps1` (or equivalent). `launcher/src/main.rs` does NOT contain these — they're build-tooling. Pattern reference only; copy the structure, change the names.

- [ ] **Step 1: Locate ws-scrcpy-web's vpk config + local pack script**

```powershell
ls C:/Users/jscha/source/repos/ws-scrcpy-web/vpk.config 2>$null
ls C:/Users/jscha/source/repos/ws-scrcpy-web/scripts/ 2>$null
```

Read whichever pack-orchestration scripts exist. Capture:
- The `--instLocation PerMachine` flag location
- The GitHub source feed URL pattern
- The publish + pack invocation order (publish ControlMenu.exe, publish ControlMenuLauncher.exe, publish ControlMenuTray.exe, then `vpk pack` over the combined publish dir)

- [ ] **Step 2: Create `vpk.config`**

```
# Velopack pack configuration for Control Menu.
# Phase 1: PerMachine install, GitHub source feed.
# Reused by both local-pack.ps1 and (Phase 4) the CI release.yml workflow.

packId       = ControlMenu
packVersion  = ${VPK_VERSION}            # set by caller
packDir      = ${PUBLISH_DIR}            # set by caller (combined publish output)
mainExe      = ControlMenuLauncher.exe   # the Velopack supervisor — NOT ControlMenu.exe
icon         = src/ControlMenu/wwwroot/favicon.ico
title        = Control Menu
authors      = bilbospocketses
instLocation = PerMachine
url          = https://github.com/bilbospocketses/control-menu
```

(Actual `vpk.config` syntax may differ from the above. Check the Velopack docs via Context7 before locking; the legacy ws-scrcpy-web `vpk.config` is the canonical template.)

- [ ] **Step 3: Create `scripts/local-pack.ps1`**

```powershell
#!/usr/bin/env pwsh
# Local Velopack pack script. Builds Setup.exe locally without burning
# Trusted Signing quota. Used for fresh-VM smoke iteration.
#
# Usage: pwsh scripts/local-pack.ps1 -Version 1.1.0-alpha.1

param(
    [Parameter(Mandatory)] [string]$Version
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$publishDir = Join-Path $repo 'publish'
$releasesDir = Join-Path $repo 'Releases'

Write-Host "Cleaning publish + Releases dirs..."
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
if (Test-Path $releasesDir) { Remove-Item -Recurse -Force $releasesDir }

Write-Host "Publishing ControlMenu.exe (web host)..."
dotnet publish "$repo/src/ControlMenu/ControlMenu.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDir `
    -p:PublishSingleFile=false

Write-Host "Publishing ControlMenuLauncher.exe (Velopack supervisor)..."
dotnet publish "$repo/src/ControlMenuLauncher/ControlMenuLauncher.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDir `
    -p:PublishSingleFile=false

Write-Host "Publishing ControlMenuTray.exe (Phase 1 stub)..."
dotnet publish "$repo/src/ControlMenuTray/ControlMenuTray.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDir `
    -p:PublishSingleFile=false

Write-Host "Running vpk pack..."
$env:VPK_VERSION = $Version
$env:PUBLISH_DIR = $publishDir

vpk pack `
    --packId ControlMenu `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe ControlMenuLauncher.exe `
    --icon "$repo/src/ControlMenu/wwwroot/favicon.ico" `
    --title "Control Menu" `
    --authors "bilbospocketses" `
    --instLocation PerMachine `
    --url "https://github.com/bilbospocketses/control-menu" `
    --outputDir $releasesDir

Write-Host "Pack done. Output:"
Get-ChildItem $releasesDir | Format-Table Name, Length
```

- [ ] **Step 4: Create `scripts/fresh-vm-smoke.md`**

```markdown
# Fresh-VM smoke #1 runbook (Phase 1)

**VM:** WIN11-CONTROL-MENU (Hyper-V, baseline snapshot per `user_test_devices.md`).

**Pre-flight:**
- Roll VM back to baseline snapshot (clean Win11, no .NET runtime, no VC redists).
- Confirm test user is non-admin (for ACL grant smoke).

**Test steps:**

1. **Build local Setup.exe on dev machine.**
   ```powershell
   pwsh scripts/local-pack.ps1 -Version 1.1.0-alpha.1
   ```
   Output: `Releases/ControlMenuSetup.exe` (signed locally with self-signed dev cert if Velopack defaults that way; unsigned otherwise — SmartScreen warning expected).

2. **Copy Setup.exe to VM** via shared folder or scp.

3. **Run Setup.exe on VM.**
   - Click through SmartScreen → "More info" → "Run anyway" (expected for unsigned).
   - UAC prompt → Accept.
   - Velopack installs to `C:\Program Files\ControlMenu\`.

4. **Verify on-disk layout post-install:**
   - `C:\Program Files\ControlMenu\ControlMenu.exe` exists (Velopack stub).
   - `C:\Program Files\ControlMenu\Update.exe` exists.
   - `C:\Program Files\ControlMenu\current\ControlMenuLauncher.exe` exists.
   - `C:\Program Files\ControlMenu\current\ControlMenu.exe` exists.
   - `C:\ProgramData\ControlMenu\` does NOT exist yet (created on first launch).

5. **Launch via Start Menu shortcut.**
   - First launch fires UAC for ACL grant — accept it.
   - Browser auto-opens to `http://localhost:5159`. (If Phase 2 not yet shipped, manually open browser.)
   - App functions normally — every page loads, settings persist.
   - Verify `C:\ProgramData\ControlMenu\config\controlmenu.db` was created.
   - Verify `C:\ProgramData\ControlMenu\logs\launcher.log` shows the Phase 1 entry sequence.

6. **Build a v1.1.0-alpha.2 with a trivial change (e.g., bump CHANGELOG).**
   ```powershell
   pwsh scripts/local-pack.ps1 -Version 1.1.0-alpha.2
   ```

7. **Stage the alpha.2 release for the VM to consume.**
   - Either push the `Releases/` content to a test GitHub release, OR
   - Configure the running CM with a local pack feed via vpk's local-feed mode (see Velopack docs).

8. **In the VM's running CM, click Settings → General → "Check for updates".**
   - Update available banner shows v1.1.0-alpha.2.
   - Click "Download and apply".
   - App requests stop (exit 75) → launcher logs hygiene → Phase 1 logs "Phase 3 lands apply orchestration" and exits.
   - **Phase 1 manual step:** user manually relaunches via Start Menu (Phase 2 will auto-relaunch via tray).
   - On relaunch, `current\ControlMenu*.exe` files are the alpha.2 build (verify by file mtime or by checking the running app's version display).
   - User data (`controlmenu.db`, `dependencies\`) survives unchanged.

9. **Pass criteria:**
   - All checkpoints in steps 4-8 succeed.
   - No crashes in `controlmenu.log` or `launcher.log`.
   - Alpha.2 binaries are in place after manual relaunch.
   - User config (camera entries, jellyfin settings) is preserved.

10. **On first failure:** STOP. Snapshot the VM state. Diff against expected. Fix on dev branch. Roll VM back to baseline + retry.

**Snapshot after success:** `post-smoke-1` — checkpoint for Phase 2 to start from.
```

- [ ] **Step 5: Update `.gitignore`**

Read `.gitignore`. If not present, append:

```
# Velopack pack output
Releases/
publish/
*.nupkg
```

- [ ] **Step 6: Commit**

```powershell
git -C C:/Users/jscha/source/repos/control-menu add vpk.config scripts/local-pack.ps1 scripts/fresh-vm-smoke.md .gitignore
git -C C:/Users/jscha/source/repos/control-menu commit -m "build(velopack): vpk.config + local-pack.ps1 + fresh-VM smoke runbook"
```

---

## Task 15: Fresh-VM smoke #1

**Files:** none modified; verification only.

This is the Phase 1 ship gate per `feedback_verify_install_on_fresh_vm.md` ("tests pass + CI green is NOT the ship gate; fresh-VM install + app-actually-starts is").

- [ ] **Step 1: Roll WIN11-CONTROL-MENU VM back to baseline**

In Hyper-V Manager: select VM → Checkpoints → right-click `baseline` → Apply.

- [ ] **Step 2: Execute `scripts/fresh-vm-smoke.md` runbook end-to-end**

Follow the 10 steps verbatim. Capture timing for each step in the runbook (will inform Phase 3's apply-orchestration tuning).

- [ ] **Step 3: If smoke passes**

- Take Hyper-V snapshot `post-smoke-1`.
- Move to Task 16 (CHANGELOG + wrap-up).

- [ ] **Step 4: If smoke fails**

DO NOT proceed to Task 16. Per the spec § "Failure recovery per phase" Phase 1:
> "Iterate locally on launcher / Velopack config / path resolver until smoke passes. Branch is local; no shipped state to roll back."

Triage:
- Capture the first failing step number.
- Save `C:\ProgramData\ControlMenu\logs\launcher.log` and `controlmenu.log` from the VM to `<repo>/scratch/smoke-1-fail-<timestamp>/`.
- Roll VM back to baseline.
- Fix on dev branch.
- Retry from Step 1.

If 3+ retries fail with different symptoms each time: halt and surface to user. May indicate an architectural issue with the path resolver or hook ordering that needs spec amendment before continuing.

---

## Task 16: CHANGELOG + memory + wrap-up candidate

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Update `CHANGELOG.md` `[Unreleased]` block**

Read current `CHANGELOG.md` lines 1-50.

Add under `[Unreleased]`:

```markdown
### Added

- **Velopack Phase 1 (core + path migration).** Three-binary architecture lands: `ControlMenuLauncher.exe` (Velopack supervisor), `ControlMenu.exe` (Blazor Server, Velopack-unaware), `ControlMenuTray.exe` (Phase 1 stub; Phase 3 fills in). New `ControlMenu.Common` shared library with `IDataPathResolver` (Velopack mode roots at `C:\ProgramData\ControlMenu`; dev mode roots at `AppContext.BaseDirectory`).
- **Settings → General → Check for updates.** Manual update flow via `VelopackUpdateService` — Check / Download / Apply (exit-75 → launcher hygiene → Phase 3 orchestrates the apply itself). Phase 1 ships the UI + plumbing; Phase 3 makes the apply auto-restart.
- **Pre-apply daemon hygiene.** `adb kill-server` + `taskkill /F /IM adb.exe /T` + 250ms settle before Velopack apply (Gotcha 9).
- **Velopack hook dispatcher with catch-all.** Unknown `--veloapp-*` flags log + exit 0 instead of breaking the Update.exe respawn loop (Gotcha 4).
- **Single-instance guard** in launcher — `Local\ControlMenuLauncher.SingleInstance.{User,Admin}` named-mutex split admits one non-elevated + one elevated launcher.
- **Install-root ACL grant** via runas-elevated icacls on first non-hook startup (Gotchas 2 + 3).
- **`vpk.config` + `scripts/local-pack.ps1` + `scripts/fresh-vm-smoke.md`** for local Setup.exe iteration without burning Trusted Signing quota.

### Changed

- **All writable-state paths now route through `IDataPathResolver`.** `Program.cs` DataProtection keys, `EF Core SQLite` connection, `OperationLogger` jellyfin logs/backups, every module's deps root — all resolved at composition root via `DataPathResolverFactory.CreateFromCurrentProcess()`. Dev workflow unchanged (resolver detects no Velopack install → roots at `AppContext.BaseDirectory`).
- **`Process` spawn cwd default**: `CommandExecutor` now anchors child processes at the binary's own directory under `<dataRoot>\dependencies\<tool>\<version>\` to keep them outside `current\` during a Velopack apply swap.
- **`ControlMenu.csproj`**: bump `<Version>` to `1.1.0-alpha.1`. SkiaSharp `<PackageReference>` re-indented (closes TODO #26).
- **`ControlMenu.Tests.csproj`**: rewritten as UTF-8 without BOM + LF line endings (closes TODO #25).
```

- [ ] **Step 2: Commit CHANGELOG**

```powershell
git -C C:/Users/jscha/source/repos/control-menu add CHANGELOG.md
git -C C:/Users/jscha/source/repos/control-menu commit -m "docs(changelog): seed Velopack Phase 1 entries under [Unreleased]"
```

- [ ] **Step 3: Push branch (optional pre-merge)**

If keeping `feature/velopack-phase-1` available for review before merge:

```powershell
git -C C:/Users/jscha/source/repos/control-menu push -u origin feature/velopack-phase-1
```

- [ ] **Step 4: STOP — wrap-up requires user codeword**

Per `feedback_do_that_thing.md`, the merge-to-master + tag + memory-sweep + push sequence is the user's call (`do that thing` codeword). Surface to user:

> Phase 1 work is complete on `feature/velopack-phase-1`. Smoke #1 passed (snapshot `post-smoke-1` taken). Ready for `do that thing` wrap-up — that will:
> 1. Doc refresh: README + TECHNICAL_GUIDE for the new three-binary layout + ProgramData paths.
> 2. Optional: tag `v1.1.0-alpha.1` if you want a paper-trail for this milestone (Phase 2-4 still pending; alpha tag is internal-only, not user-facing).
> 3. Merge `feature/velopack-phase-1` to master fast-forward.
> 4. Memory sweep: append "Velopack Phase 1 SHIPPED 2026-MM-DD" to `todo_control_menu.md` Recent shipments; close TODO items #25 + #26 in the Shipped section; mark item #6 as "Phase 1 done; Phase 2 next".
> 5. Push master + (optional) alpha tag.
>
> Standing by for `do that thing` — or specify `make it so` to run only the merge + push without the alpha tag.

---

## Validation gate

Phase 1 is **shipped** when:

- ✅ `feature/velopack-phase-1` merged to master.
- ✅ All existing 383 + new ~30 tests pass on master.
- ✅ Fresh-VM smoke #1 passes end-to-end (steps 1-9 in runbook).
- ✅ User config + dependencies + jellyfin backups survive an alpha.1 → alpha.2 manual update cycle.
- ✅ CHANGELOG `[Unreleased]` reflects the Phase 1 changes.
- ✅ Memory sweep complete (todo_control_menu.md updated; item #6 progress notation).

After Phase 1 ships, the next plan is `2026-MM-DD-velopack-phase-2-tray.md` — written via a fresh `superpowers:writing-plans` invocation when Phase 2 begins, so any discoveries from Phase 1 (Velopack API drift, smoke timing, ACL surprises) inform Phase 2's task structure.

---

## Risks called out for execution

- **Velopack .NET API drift** vs the spec snippet (`VelopackApp.Build().SetAutoApplyOnStartup(false).Run(args)`). The spec was written 2026-05-09 against ws-scrcpy-web's Rust crate; the .NET package's API may have evolved. Run Context7 against `Velopack` library before locking the version; reconcile any naming differences before commit. **Halt + surface** if the API has been renamed or removed without an obvious replacement.
- **`Mutex` named-mutex semantics on .NET 10**. The `Local\` namespace prefix should be honored; if `SingleInstanceTests` fails on Windows specifically, dig in — the underlying primitive is `CreateMutexW` and the namespace is part of the name string. Don't swallow.
- **OperationLogger refactor blast radius** (Task 6 Step 4). Existing tests inject `IDbContextFactory` and similar; the new `IDataPathResolver` parameter lands in static method signatures and may break test compilation. Plan the test-side refactor BEFORE the production-side refactor; keep both halves in one commit so the test count never dips.
- **Velopack first-run UAC**. The ACL grant in Task 8 fires UAC on first launch post-install. If the user runs Setup.exe → declines UAC for the ACL grant → the in-app updater is degraded. Document this in the smoke runbook; manual icacls grant via the docs is the recovery.
- **First-run migration heuristic** (spec § "Risks called out for the implementation plan"). When a user with an existing dev-mode install (state under `AppContext.BaseDirectory`) runs the Velopack Setup.exe for the first time, do we copy `controlmenu.db` + `dependencies/` from the old location into `C:\ProgramData\ControlMenu\`? **Default decision per spec: NO migration** — fresh install semantics. Dev users keep using `dotnet run` from source; Velopack-installed users start with a clean DB. Re-evaluate during Task 5 implementation if any reproducible flow surfaces a real migration need; surface to user for explicit decision rather than silently adding migration logic.
- **`current\` swap timing**. Phase 1 doesn't auto-restart after apply (Phase 3 does). The user must manually relaunch via Start Menu after Apply. The runbook calls this out; review user flows before claiming Phase 1 ships.
- **Test project encoding fix (TODO #25)** is bundled into Task 6. If the rewrite changes a tracked-but-not-staged file in a way that surprises the user (e.g., they had local edits in flight), surface it before committing.

---

## Appendix: Mandatory plan-writing checklist (self-review)

Per spec § "Mandatory plan-writing checklist":

- [x] Plan has a top-level "Sources to port from" section. ← present at the top
- [x] The section lists `<legacy-path>:<line-range>` for every legacy file the phase ports. ← table at the top
- [x] Each task that creates a new CM file references its legacy counterpart by `<path>:<line>` in the task header. ← Tasks 2, 3, 4, 7, 8, 9, 10 all carry the "Legacy reference:" header. Tasks 1, 5, 6, 11, 12, 13, 14, 15, 16 either have no legacy port (CM-specific abstractions, build tooling, smoke) and call that out explicitly.
- [x] Each scaffolding task includes the verification step worded verbatim. ← every "Step 5: Diff against legacy" carries the boldface block-quoted verbatim text.
- [x] If subagents are dispatched, each agent prompt embeds the legacy `<path>:<line>` reference. ← this plan does not pre-author subagent prompts (left to the implementing agent's choice of `subagent-driven-development`); a sentence in "Sources to port from" makes the requirement explicit for whoever dispatches.
