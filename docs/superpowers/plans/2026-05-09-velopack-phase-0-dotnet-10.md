# Velopack Phase 0 — .NET 10 Upgrade Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade Control Menu's target framework from .NET 9 to .NET 10, ship as v1.0.1 release. No installer, no Velopack, no path migration. Source-only release for paper trail. Foundation for the v1.1.0 Velopack work that follows.

**Architecture:** Single-deliverable framework version bump. csproj `<TargetFramework>` net9.0 → net10.0; EF Core 9.* → 10.*; verify Blazor Server + EF Core 10 + SkiaSharp 3.119.2 compat via existing test suite + manual smoke.

**Tech Stack:** .NET 10 SDK, ASP.NET Core 10, Blazor Server, EF Core 10, SQLite, SkiaSharp 3.119.2, xUnit (existing 383-test suite).

**Spec reference:** `docs/superpowers/specs/2026-05-09-velopack-packaging-design.md` § "Phase 0 — v1.0.1"

---

## File Structure

**Files modified:**
- `src/ControlMenu/ControlMenu.csproj` — bump TargetFramework + Version + EF Core PackageReferences
- `tests/ControlMenu.Tests/ControlMenu.Tests.csproj` — bump TargetFramework + any EF Core test deps
- `README.md` — update prerequisites section (.NET 9 SDK → .NET 10 SDK), drop stale Node.js dep mention from auto-installable list (decoupled in v1.0.0 polish batch)
- `docs/TECHNICAL_GUIDE.md` — update any .NET 9 references to .NET 10
- `CHANGELOG.md` — add `[1.0.1] - <date>` section under `[Unreleased]`

**Files created:** none.

**Tests added:** none (existing 383-test suite is the validation gate).

---

## Pre-flight checks

Before starting Task 1, verify the user's machine has the prerequisites.

- [ ] **Check .NET 10 SDK is installed**

Run:
```powershell
dotnet --list-sdks
```

Expected: at least one entry starting with `10.` (e.g. `10.0.100 [C:\Program Files\dotnet\sdk]`). If absent, the user needs to install .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0 before proceeding.

- [ ] **Verify clean working tree**

Run:
```powershell
git -C "C:/Users/jscha/source/repos/control-menu" status --short
```

Expected: empty output (or only the pre-existing untracked items: `controlmenu.db.bak-*`, `probe-*.ps1` diagnostic scripts). If any tracked files are dirty, address them before starting (commit, stash, or revert depending on intent).

- [ ] **Verify on master, up to date with origin**

Run:
```powershell
git -C "C:/Users/jscha/source/repos/control-menu" rev-parse --abbrev-ref HEAD
git -C "C:/Users/jscha/source/repos/control-menu" status --short --branch
```

Expected: branch is `master`; status shows `## master...origin/master` with no `[ahead N]` or `[behind N]`. If diverged, sync first.

---

## Task 1: Create branch and bump csproj target framework

**Files:**
- Modify: `src/ControlMenu/ControlMenu.csproj` (lines 4 and 7 for TargetFramework + Version)
- Modify: `tests/ControlMenu.Tests/ControlMenu.Tests.csproj` (TargetFramework line — read first to confirm exact line)

- [ ] **Step 1: Create + check out the feature branch**

Run:
```powershell
git -C "C:/Users/jscha/source/repos/control-menu" checkout -b feature/dotnet-10-upgrade
```

Expected output: `Switched to a new branch 'feature/dotnet-10-upgrade'`

- [ ] **Step 2: Update `ControlMenu.csproj` TargetFramework + Version + AssemblyVersion + FileVersion**

Apply this Edit to `src/ControlMenu/ControlMenu.csproj`:

Replace:
```xml
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Version>1.0.0</Version>
    <AssemblyVersion>1.0.0.0</AssemblyVersion>
    <FileVersion>1.0.0.0</FileVersion>
```

With:
```xml
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Version>1.0.1</Version>
    <AssemblyVersion>1.0.1.0</AssemblyVersion>
    <FileVersion>1.0.1.0</FileVersion>
```

- [ ] **Step 3: Update test csproj TargetFramework**

Read `tests/ControlMenu.Tests/ControlMenu.Tests.csproj` first to confirm the TargetFramework value and surrounding context. Then Edit:

Replace:
```xml
<TargetFramework>net9.0</TargetFramework>
```

With:
```xml
<TargetFramework>net10.0</TargetFramework>
```

- [ ] **Step 4: Bump EF Core PackageReference versions in `ControlMenu.csproj`**

Replace:
```xml
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.*">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.*" />
```

With:
```xml
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.*">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.*" />
```

- [ ] **Step 5: Bump EF Core PackageReference versions in test csproj (if present)**

Read `tests/ControlMenu.Tests/ControlMenu.Tests.csproj`. If it has `Microsoft.EntityFrameworkCore.*` PackageReferences with `Version="9.*"`, bump them to `Version="10.*"` via Edit. If the test project only references the production csproj via `<ProjectReference>` and pulls EF Core transitively, no change needed.

- [ ] **Step 6: Restore + build**

Run from `C:/Users/jscha/source/repos/control-menu`:
```powershell
dotnet restore
dotnet build -c Release --no-restore 2>&1 | tail -50
```

Expected: build completes successfully, exit code 0. Output ends with `Build succeeded.` followed by warning + error counts.

If errors: address each per its message before proceeding. Common breaking changes between .NET 9 → 10:
- Nullable reference type tightening (`warning CS8600` etc.) — usually fixable by adding `?` to nullable params or `!` for null-forgiving asserts where the surrounding logic guarantees non-null
- Newly `[Obsolete]` API surface — replace with the recommended successor named in the obsolete attribute message
- EF Core internal type signature shifts — most likely impact: any test that mocks `IDbContextFactory` internals; usually fixable by switching to behavioral asserts on a real in-memory DB

If a breaking change is non-trivial, halt and surface it to the user before continuing.

If warnings only (no errors): note the warning count for the commit message but proceed.

- [ ] **Step 7: Commit**

```powershell
git -C "C:/Users/jscha/source/repos/control-menu" add src/ControlMenu/ControlMenu.csproj tests/ControlMenu.Tests/ControlMenu.Tests.csproj
git -C "C:/Users/jscha/source/repos/control-menu" commit -m @'
feat(framework): upgrade target framework from .NET 9 to .NET 10

- ControlMenu.csproj TargetFramework net9.0 -> net10.0
- ControlMenu.Tests.csproj TargetFramework net9.0 -> net10.0
- Microsoft.EntityFrameworkCore.Design Version 9.* -> 10.*
- Microsoft.EntityFrameworkCore.Sqlite Version 9.* -> 10.*
- Bump csproj Version 1.0.0 -> 1.0.1

Build clean (Release).
'@
```

---

## Task 2: Verify all existing tests pass under .NET 10

**Files:** none modified; verification only.

- [ ] **Step 1: Run the full test suite**

Run from repo root:
```powershell
dotnet test -c Release --nologo 2>&1 | tail -10
```

Expected output (last few lines):
```
Passed!  - Failed:     0, Passed:   383, Skipped:     0, Total:   383, Duration: <N>s - ControlMenu.Tests.dll (net10.0)
```

The `(net10.0)` at the end confirms the upgrade landed (vs. `(net9.0)` which would mean csproj wasn't picked up).

If any test fails: halt. Read the failure output. Most likely candidates:
- EF Core 10 internal type signature shifts breaking tests that mock `DbContext` or `DbSet` internals — usually fixable by switching to behavioral asserts on a real in-memory DB
- `JsonSerializer` behavior shifts (less common in 9→10, more in 8→9)
- `IAsyncEnumerable` / `await using` pattern tightening

Investigate root cause; fix the test (NOT the framework version); re-run; commit the fix as a separate commit referencing this task.

If all 383 tests pass: proceed.

- [ ] **Step 2: Note any new warnings introduced**

Run:
```powershell
dotnet build -c Release --no-restore 2>&1 | Select-String "warning"
```

If warning count differs from pre-upgrade baseline, document the new warnings in your task notes. They may be addressable in this PR or deferred to a follow-up TODO depending on volume + character. Do not commit warning suppressions without a solid rationale.

---

## Task 3: Manual smoke — click through every module

**Files:** none modified; verification only.

This is the v1.0.1 ship gate per `feedback_verify_install_on_fresh_vm.md`. No installer artifact for v1.0.1, so the smoke is "dotnet run on dev box, navigate every page."

- [ ] **Step 1: Launch the app**

Run from repo root:
```powershell
dotnet run --project src/ControlMenu/ControlMenu.csproj -c Release --no-build
```

Expected:
- Console output shows Kestrel binding to `http://localhost:5159` within ~5 seconds
- No unhandled exceptions in console output
- App stays running (don't Ctrl+C)

- [ ] **Step 2: Navigate every module's main page in a browser**

Open `http://localhost:5159` in the user's default browser. Click through:

1. **Home** — verify dashboard loads; Quick Scan buttons render; Discovered sections render (with placeholders if no devices/cameras registered)
2. **Settings → General** — verify form renders; Theme toggle works; ws-scrcpy-web URL field present
3. **Settings → Android Devices** — verify Discovery section + Liveness Interval field render
4. **Settings → Cameras** — verify camera config table renders + Liveness Interval field
5. **Settings → Jellyfin** — verify all 4 sections render (Docker Compose, API, Cast & Crew, Logging/Backup/Retention)
6. **Settings → Email** — verify SMTP form renders
7. **Settings → Dependencies** — verify the 5 auto-managed deps appear (platform-tools/scrcpy/sqlite3/go2rtc), version + install path columns populated
8. **Setup Wizard** — visit `/setup`; verify all 7 steps load (Welcome, Devices, Cameras, Jellyfin, Email, Dependencies, Done)
9. **Android Power Tools** — visit `/android-power-tools`; verify iframe loads (or shows "ws-scrcpy-web not running" message gracefully if ws-scrcpy-web isn't started)
10. **Cameras dashboard** — verify it loads (may show "no cameras configured" if user has none registered)
11. **Jellyfin dashboard** — verify it loads
12. **Utilities → Icon Converter** — verify the page renders + file picker button works
13. **Utilities → File Unblocker** — verify the page renders

Pass criteria: every page loads without an unhandled exception, every navigation completes within ~3 seconds, no broken Razor compilation errors visible in browser. Minor visual glitches are acceptable (.NET 10 might shift some tiny rendering details — flag them as v1.1.0+ follow-ups, NOT v1.0.1 blockers).

- [ ] **Step 3: Stop the app**

Press Ctrl+C in the console. Expected: graceful shutdown, no unhandled exceptions on stop.

---

## Task 4: Update documentation

**Files:**
- Modify: `README.md`
- Modify: `docs/TECHNICAL_GUIDE.md`

- [ ] **Step 1: Update README prerequisites**

Read `README.md` first to confirm exact text around the prereqs section (likely under "## Quick Start" → "### Prerequisites").

Apply Edit:

Replace:
```markdown
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js](https://nodejs.org/) (for ws-scrcpy-web screen mirroring, optional &mdash; auto-installable)
```

With:
```markdown
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
```

(Drops the Node.js line entirely — Node was removed as a CM-managed dep in the v1.0.0 polish batch but the README still listed it. This is a doc-drift fix piggybacking on the .NET 10 upgrade.)

- [ ] **Step 2: Update README "Auto-installable" dependencies table**

In the same `README.md`, find the "Auto-installable" dependencies table:

```markdown
**Auto-installable** (downloaded to `dependencies/` folder):
| Tool | Source | Purpose |
|------|--------|---------|
| ADB | Google (DirectUrl) | Android device management |
| scrcpy | GitHub (Genymobile/scrcpy) | Screen mirroring server binary |
| Node.js | nodejs.org (DirectUrl) | ws-scrcpy-web runtime |
| sqlite3 | sqlite.org (DirectUrl) | Jellyfin database operations |
| go2rtc | GitHub (AlexxIT/go2rtc) | RTSP-to-browser camera streaming |
```

Remove the `Node.js` row entirely. Result:

```markdown
**Auto-installable** (downloaded to `dependencies/` folder):
| Tool | Source | Purpose |
|------|--------|---------|
| ADB | Google (DirectUrl) | Android device management |
| scrcpy | GitHub (Genymobile/scrcpy) | Screen mirroring server binary |
| sqlite3 | sqlite.org (DirectUrl) | Jellyfin database operations |
| go2rtc | GitHub (AlexxIT/go2rtc) | RTSP-to-browser camera streaming |
```

- [ ] **Step 3: Update README badge for .NET version**

Find the badge line near the top of `README.md`:

```markdown
<img src="https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet" alt=".NET 9" />
```

Replace with:

```markdown
<img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet" alt=".NET 10" />
```

- [ ] **Step 4: Search TECHNICAL_GUIDE for any .NET 9 references**

Run:
```powershell
Select-String -Path "docs/TECHNICAL_GUIDE.md" -Pattern "\.NET 9|net9\.0"
```

For each match: read the surrounding context and update to `.NET 10` / `net10.0` as appropriate. If no matches: skip this step.

- [ ] **Step 5: Commit doc updates**

```powershell
git add README.md docs/TECHNICAL_GUIDE.md
git commit -m @'
docs: update prerequisites + dep list for .NET 10 + Node decoupling

- README "Prerequisites" -> .NET 10 SDK (was .NET 9 SDK)
- README "Auto-installable" deps table: drop stale Node.js row
  (Node was decoupled from CM in the v1.0.0 polish batch but the
  README still listed it as auto-installable)
- README badge: .NET 9.0 -> 10.0
- TECHNICAL_GUIDE: any net9.0 / .NET 9 references updated
'@
```

---

## Task 5: Update CHANGELOG and prepare release

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Read current `[Unreleased]` block**

Read `CHANGELOG.md` lines 1-20 to confirm the current `[Unreleased]` section structure.

- [ ] **Step 2: Add the `[1.0.1]` section**

The current `[Unreleased]` section already contains the legacy PS1 archive entries from earlier today's work and the codebase scrub. The .NET 10 upgrade is a separate concern that ships as `[1.0.1]`. Promote a focused subset to `[1.0.1]`, leaving the archive/scrub entries in `[Unreleased]` (those will go out with v1.1.0 alongside Velopack work).

Apply Edit. Find:

```markdown
## [Unreleased]

### Added

- **`backups/origina tools-menu backup/ControlMenu.ps1`** — archived copy of the legacy PowerShell tools menu...
```

Insert a new `[1.0.1]` section between `## [Unreleased]` and the existing content (with today's date — replace `2026-05-09` with `2026-05-09` or whatever date the release ships):

```markdown
## [Unreleased]

## [1.0.1] - 2026-05-09

### Changed

- **Target framework upgrade.** Bumped from .NET 9 to .NET 10 across `ControlMenu.csproj` and `ControlMenu.Tests.csproj`. Bumped `Microsoft.EntityFrameworkCore.Design` and `Microsoft.EntityFrameworkCore.Sqlite` PackageReferences from Version="9.*" to Version="10.*". No behavioral changes; runtime + EF Core + Razor compiler version-bumped only. 383/383 existing tests passing under net10.0; manual smoke clean across all module pages.
- **README prerequisites + dependency list refresh.** Required SDK now listed as .NET 10 SDK (was .NET 9 SDK). The "Auto-installable" deps table dropped the stale Node.js row (Node was decoupled from CM in the v1.0.0 polish batch but the README still listed it as auto-installable). The .NET shield badge updated to 10.0.

### Added

- **`backups/origina tools-menu backup/ControlMenu.ps1`** — archived copy of the legacy PowerShell tools menu...
```

(Keep all the existing `[Unreleased]` entries below the new `[1.0.1]` section — they're for v1.1.0.)

- [ ] **Step 3: Commit CHANGELOG**

```powershell
git add CHANGELOG.md
git commit -m "docs(changelog): add [1.0.1] section for .NET 10 upgrade"
```

---

## Task 6: Merge to master, tag, push, GitHub Release

**Files:** none modified; git operations only.

- [ ] **Step 1: Switch to master and fast-forward merge**

Run:
```powershell
git -C "C:/Users/jscha/source/repos/control-menu" checkout master
git -C "C:/Users/jscha/source/repos/control-menu" merge feature/dotnet-10-upgrade --ff-only
```

Expected: `Updating <oldsha>..<newsha>` followed by `Fast-forward` and the file change summary.

If the merge fails with "not possible to fast-forward, aborting": master diverged during this work. Investigate:
```powershell
git -C "C:/Users/jscha/source/repos/control-menu" log --oneline -5 master origin/master
```

If origin/master has commits we don't have locally: `git pull --ff-only` first, then retry the merge from feature branch (which may need rebasing). If the divergence is unexpected, halt and surface to the user.

- [ ] **Step 2: Push master to origin**

```powershell
git -C "C:/Users/jscha/source/repos/control-menu" push origin master
```

Expected: clean push, no force needed.

- [ ] **Step 3: Tag v1.0.1 and push tag**

```powershell
git -C "C:/Users/jscha/source/repos/control-menu" tag -a v1.0.1 -m "Release v1.0.1 - .NET 10 upgrade"
git -C "C:/Users/jscha/source/repos/control-menu" push origin v1.0.1
```

Expected: `* [new tag] v1.0.1 -> v1.0.1`

- [ ] **Step 4: Create GitHub Release**

Use the gh CLI:

```powershell
gh release create v1.0.1 `
  --title "v1.0.1 - .NET 10 upgrade" `
  --notes @'
## Changed

- **Target framework upgrade.** Bumped from .NET 9 to .NET 10. No behavioral changes; runtime + EF Core + Razor compiler version-bumped only. 383/383 existing tests passing under net10.0.
- **Prerequisites:** Now requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (was .NET 9 SDK). Update your dev environment before pulling this tag.

## Foundation for v1.1.0

This release is a source-only paper-trail step setting up the foundation for v1.1.0, which will introduce the Velopack-managed installer, auto-update, tray icon, and Servy-wrapped service mode. v1.0.1 itself ships no installer artifact — clone, restore, run as before.

Full architecture for the upcoming v1.1.0 stack is documented in [`docs/superpowers/specs/2026-05-09-velopack-packaging-design.md`](https://github.com/bilbospocketses/control-menu/blob/master/docs/superpowers/specs/2026-05-09-velopack-packaging-design.md).
'@
```

Expected: gh outputs the URL of the created release.

- [ ] **Step 5: Delete the feature branch**

```powershell
git -C "C:/Users/jscha/source/repos/control-menu" branch -d feature/dotnet-10-upgrade
```

Expected: `Deleted branch feature/dotnet-10-upgrade (was <sha>).`

- [ ] **Step 6: Verify ship state**

Run:
```powershell
git -C "C:/Users/jscha/source/repos/control-menu" log --oneline -5
git -C "C:/Users/jscha/source/repos/control-menu" tag --list "v*"
gh release list --limit 5
```

Expected:
- Recent commits include the .NET 10 upgrade commits
- `v1.0.0` and `v1.0.1` both appear in tag list
- `v1.0.1` appears in release list as Latest

---

## Memory sweep + wrap-up

After Task 6 completes successfully, perform the standard "do that thing" wrap-up. The user will trigger this with the codeword phrase; do NOT auto-execute — surface the recommendation and wait.

Recommended sweep targets:
- `todo_control_menu.md` — add v1.0.1 to "Recent shipments" section; nothing in Active sections is closed by this work (Phase 0 is foundation, item #6 Velopack is still open through Phases 1-4)
- `project_control_menu.md` — add an "Updates 2026-05-09 (.NET 10 upgrade, v1.0.1)" section
- `claude-config` repo — commit the memory updates per `feedback_do_that_thing.md` step 4's two-repo discipline

---

## Validation gate

Phase 0 is **shipped** when:
- ✅ `v1.0.1` tag exists on origin
- ✅ GitHub Release v1.0.1 is published as Latest
- ✅ Master branch points at the merged feature work
- ✅ 383/383 tests pass on master under .NET 10
- ✅ Manual smoke clean (Task 3) across all module pages
- ✅ README prereqs + badge + auto-installable deps table reflect .NET 10 + no Node
- ✅ CHANGELOG `[1.0.1]` section reflects the changes
- ✅ Memory sweep complete

After Phase 0 ships, the next plan to write is `2026-05-09-velopack-phase-1-core.md` (Velopack core + path migration to ProgramData). Per the master spec, that plan should be drafted via a fresh `superpowers:writing-plans` invocation when Phase 1 begins, so any discoveries from Phase 0 (e.g. unexpected EF Core 10 interactions) can inform the Phase 1 task structure.

---

## Risks called out for execution

- **EF Core 10 internal type signatures may break tests that mock `DbContext`/`DbSet` internals.** Usually fixable by switching to behavioral asserts on a real in-memory DB. If a non-trivial test breakage surfaces, halt and surface to the user — DO NOT silently rewrite tests to chase passing.
- **SkiaSharp 3.119.2 is the version locked in csproj.** Should be .NET 10 compatible, but if the build emits an `<unknown package warning>` or `<assembly version mismatch>` for SkiaSharp, the user should bump SkiaSharp to a known-net10-compatible version (likely 3.120+ when released, or whatever's current at execution time) — surface, do not silently bump.
- **The CHANGELOG `[Unreleased]` block already has entries from 2026-05-09's earlier PS1 scrub work.** Make sure to leave those in `[Unreleased]` (they ship with v1.1.0) and ONLY promote the .NET 10 upgrade entries to `[1.0.1]`. Don't sweep the archive entries into v1.0.1 by mistake.
