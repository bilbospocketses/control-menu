# Audit MINOR Mop-up + Follow-ups Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline, per-PR checkpoints) to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking. Each "PR" is a branch cut off the latest `master` via the verified `git-new-branch.ps1`, serial per-finding TDD, whole-branch review, squash-merge — then the next PR branches off the new master.

**Goal:** Close the remaining 20 MINOR findings (#37–#57, excluding #41 done / #58 n/a) from the 2026-06-14 review plus 2 non-audit follow-ups (Item 45, Item 46), completing the audit tail.

**Architecture:** Five thematic PRs ordered low-risk → high-risk: (1) frontend/static, (2) backend correctness, (3) network managed rewrite, (4) concurrency/lifecycle, (5) build tooling. Findings are grouped so a reviewer can accept/reject coherent units.

**Tech Stack:** .NET 10, Blazor Server, EF Core 10 + SQLite, xUnit + bUnit, PowerShell build scripts, SharpCompress 0.49.1.

## Global Constraints

- **Multi-session cwd discipline:** ALL git via `git -C "C:/Users/jscha/source/repos/control-menu" …`; absolute paths in every file/shell op. No `cd` into the repo.
- **Branch creation:** `pwsh C:/Users/jscha/.claude/scripts/git-new-branch.ps1 -Repo C:/Users/jscha/source/repos/control-menu -Branch <name>` (refuses dirty tree, lands on latest `origin/HEAD`, asserts `HEAD == origin/<default>`).
- **Merge:** `gh pr merge --squash --delete-branch <N>` (signed-repo rule — never `--rebase`). Each subsequent PR branches off the freshly-merged master.
- **Local-Dependencies-Only:** no binary resolved via PATH/env; verify per-edit. Applies especially to PR-5 (SharpCompress.dll resolved from the app's own output, never `~/.nuget`).
- **Build/test:** `dotnet build ControlMenu.sln -c Release`; `dotnet test ControlMenu.sln -c Release`. Test count via `dotnet test --list-tests`. Three test projects: `tests/ControlMenu.Tests`, `tests/ControlMenu.Common.Tests`, `tests/ControlMenuLauncher.Tests`.
- **TDD:** failing test → implement → pass, for every behavioral change. Cosmetic/CSS/comment changes verify via build (+ targeted bUnit where a component renders the changed token).
- **CHANGELOG:** add `[Unreleased]` entries per PR (Keep a Changelog format).
- **Decisions (locked 2026-06-24):** #37 full managed (Ping + IP Helper `GetIpNetTable` P/Invoke on Windows, regex fallback Linux). #39 clarifying comment only — NEVER rename the migration class/file/Id. #46 eliminate `7zr.exe`, extract magick via bundled SharpCompress. #56 keep `localhost` bind + `AllowedHosts:"*"`; only remove the dead relative connection-string default. #57 vendor bootstrap-icons locally. #49 collapse ternary, keep `"go2rtc"` (no `.exe`). #54 DeviceForm only (CastCrewUpdate already guarded).
- **Build-output dirs are NOT edited:** `publish/`, `Releases/` are regenerated — only edit `src/`.

---

## PR 1 — Frontend & static asset cleanup

**Findings:** #42, #43, #44, #45, #46, #47, #57. Branch: `fix/audit-frontend-cleanup`. Mostly cosmetic; verify via `dotnet build -c Release` + targeted reasoning. No behavioral tests except where a component renders a token.

### Task 1.1 — #46 stale comment (trivial)
- Modify: `src/ControlMenu/Modules/Imaging/Pages/MagicWand.razor.css:51`
- Change the comment `/* 16px light/dark checkerboard via two crossed conic gradients. */` → `… via four crossed linear gradients.` (code is 4 `linear-gradient`s).
- [ ] Edit comment · [ ] `dotnet build -c Release` clean · [ ] commit.

### Task 1.2 — #42 subnets.html close-trap
- Modify: `src/ControlMenu/wwwroot/help/subnets.html:128`
- `onclick="window.close(); return false;"` → `onclick="if(window.opener){window.close();} return true;"` (lets the `href="../"` navigate as fallback when the tab wasn't script-opened). Do NOT edit `publish/wwwroot/...` (build output).
- [ ] Edit · [ ] build clean · [ ] commit.

### Task 1.3 — #46/#47 home-tiles.js layout-thrash + dead CSS
- Modify: `src/ControlMenu/wwwroot/js/home-tiles.js:22-29` — split the loop into two passes: pass 1 reads all `card.scrollHeight` into an array; pass 2 writes all `--cm-tile-span`. Eliminates read-after-write layout thrash.
- Modify: `src/ControlMenu/wwwroot/css/app.css:67-90` — delete the dead `.home-module-grid`/`.home-module-card`/`.home-module-card i`/`.home-module-card h3` selectors (no `.razor`/`.html` references `home-module`; live grid uses `.module-grid`/`.module-card`).
- [ ] Edit JS (two-pass) · [ ] Delete dead CSS block · [ ] build clean · [ ] manual: home page still lays out (Playwright or `dotnet run` spot-check at end of PR) · [ ] commit.

### Task 1.4 — #43 define theme tokens
- Modify: `src/ControlMenu/wwwroot/css/theme.css` — add `--success-bg/--danger-bg/--warning-bg/--info-bg` and `--success-text/--danger-text/--warning-text/--info-text` to BOTH the `:root` (light) and dark blocks. Light values match the existing hardcoded fallbacks (e.g. success-bg `#d4edda`/text `#155724`, danger `#f8d7da`/`#721c24`, warning `#fff3cd`/`#856404`, info `#cff4fc`/`#055160`); dark values: muted theme-consistent equivalents (semi-transparent accent tints over `--card-bg`). Exact values chosen at edit time to match light fallbacks and read correctly on dark `--card-bg`.
- 11 consumer files already reference these via `var(--success-bg, #fallback)` — defining the tokens makes dark mode resolve correctly instead of always using the light fallback. No consumer edits needed.
- [ ] Add 8 tokens × 2 themes · [ ] build clean · [ ] grep confirms tokens now defined · [ ] dark-mode spot-check at PR end · [ ] commit.

### Task 1.5 — #44 app.css dialog/modal dedup + theme-aware hover
- Modify: `src/ControlMenu/wwwroot/css/app.css:182-192` — collapse the byte-identical `.dialog*`/`.modal*` pairs into shared selector lists (`.dialog-overlay, .modal-overlay { … }`, `.dialog, .modal-dialog { … }`, etc.). Confirm `DependencyManagement.razor` (uses `modal-*` names) and dialog consumers still resolve.
- Modify: `app.css:206-209` — replace hardcoded `#157347`/`#0b5ed7` pill `:hover` hex with `color-mix(in srgb, var(--accent-color) 85%, black)` so hover tracks the theme accent (currently wrong in dark mode, where `--accent-color` is green).
- [ ] Merge selectors · [ ] color-mix hover · [ ] build clean · [ ] grep `modal-` usages still covered · [ ] commit.

### Task 1.6 — #45 ScrcpySettingsModal re-key --bs-* tokens
- Pre-check: read `src/ControlMenu/wwwroot/js/scrcpyThemeBridge.js` — confirm it does NOT inject `--bs-*` vars (if it does, re-keying is wrong; abort this task and flag). Verification report says these are unset → fallbacks used.
- Modify: `src/ControlMenu/Components/Shared/ScrcpySettingsModal.razor.css` — re-key `--bs-body-bg`→`--card-bg`, `--bs-body-color`→`--text-primary`, `--bs-border-color`→`--border-color`, `--bs-tertiary-bg`→`--hover-bg` (verify these app tokens exist in theme.css; use closest existing).
- [ ] Pre-check bridge · [ ] re-key · [ ] build clean · [ ] modal spot-check at PR end · [ ] commit.

### Task 1.7 — #57 vendor bootstrap-icons locally
- Fetch bootstrap-icons **1.11.3** (pinned): `bootstrap-icons.min.css` + `fonts/bootstrap-icons.woff2` + `fonts/bootstrap-icons.woff` from the jsDelivr/npm package. Verify integrity (size/known hash) before committing.
- Create: `src/ControlMenu/wwwroot/lib/bootstrap-icons/bootstrap-icons.min.css`, `.../fonts/bootstrap-icons.woff2`, `.../fonts/bootstrap-icons.woff`. The CSS references fonts via relative `url(./fonts/...)` — keep that structure so it resolves locally.
- Modify: `src/ControlMenu/Components/App.razor:10` — replace the jsDelivr `<link>` with `<link rel="stylesheet" href="lib/bootstrap-icons/bootstrap-icons.min.css" />` (or `~/`-rooted per app convention).
- [ ] Fetch + verify files · [ ] place under wwwroot/lib · [ ] update App.razor · [ ] build clean · [ ] `dotnet run` + DevTools Network: icons load from localhost, no jsDelivr request · [ ] icons render (visual) · [ ] commit.

### PR 1 close-out
- [ ] CHANGELOG `[Unreleased]` entries (theme tokens, modal dedup, vendored icons, home-tiles perf, etc.) · [ ] `dotnet build -c Release` + `dotnet test -c Release` green · [ ] `dotnet run` visual smoke (home, a dashboard, scrcpy modal, an Imaging page, dark mode) · [ ] whole-branch review (subagent) · [ ] address findings · [ ] push, open PR, CI green · [ ] squash-merge + delete branch.

---

## PR 2 — Backend correctness & small robustness

**Findings:** #38, #39, #40, #48, #49, #54, #55, #56 + Item 45. Branch: `fix/audit-backend-correctness` (off merged PR-1 master). Per-finding TDD.

### Task 2.1 — #39 migration clarifying comment
- Modify: `src/ControlMenu/Migrations/20260505032259_AddTypedDeviceFieldsRemoveMetadata.cs` — add a comment above `Up` (≈line 11): `// NOTE: Name is historical. This RENAMES Cameras.Metadata -> SerialNumber (data preserved) and adds typed device columns; it does NOT drop a column. The class name / migration Id must NOT change (EF matches by the immutable Id in __EFMigrationsHistory).`
- NEVER touch the class name, file name, or `[Migration(...)]` Id.
- [ ] Add comment · [ ] build clean (no migration model change → `dotnet ef migrations` not needed) · [ ] commit.

### Task 2.2 — #49 collapse no-op ternary
- Modify: `src/ControlMenu/Modules/Cameras/Services/Go2RtcService.cs:462` — `var exeName = OperatingSystem.IsWindows() ? "go2rtc" : "go2rtc";` → `var exeName = "go2rtc";` (no `.exe` — `GetProcessesByName` wants the bare name on both OSes).
- [ ] Existing `Go2RtcServiceTests` still pass (behavior identical) · [ ] edit · [ ] `dotnet test --filter Go2Rtc` green · [ ] commit.

### Task 2.3 — #48 SubnetParser byte.TryParse
- Test first: `tests/ControlMenu.Tests/Services/SubnetParserTests.cs` — add cases asserting rejection of `"256"`, `" 1"` (whitespace), `"+1"` (sign), and acceptance of `"0"`/`"255"`; confirm leading-zero behavior matches intent.
- Modify: `src/ControlMenu/Services/Network/SubnetParser.cs:70,112` — replace `Regex.IsMatch(x, @"^\d{1,3}$")` (+ the int range check at :113) with `byte.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out _)`. Drop `using System.Text.RegularExpressions;` if now unused.
- [ ] Write failing tests · [ ] run → fail · [ ] implement · [ ] run → pass · [ ] commit.

### Task 2.4 — #38 PIN clear → DeleteSettingAsync
- Test first: `ConfigurationService` test (find/add in `tests/ControlMenu.Tests/Services/`) — set a secret PIN, then clear via the code path; assert the setting row is GONE (not an empty string). `DeleteSettingAsync` deletes regardless of `IsSecret`.
- Modify: `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor:313` — `await Config.SetSettingAsync($"device-pin-{savedDevice.Id}", "");` → `await Config.DeleteSettingAsync($"device-pin-{savedDevice.Id}");`.
- [ ] Failing test · [ ] implement · [ ] pass · [ ] commit.

### Task 2.5 — #40 pure GetDefaultBackupDirectory
- Find the write call site(s) of `GetDefaultBackupDirectory` (Jellyfin backup write path) — they must create the dir if absent.
- Test first: `tests/ControlMenu.Tests/Modules/Jellyfin/...` — assert calling `GetDefaultBackupDirectory` does NOT create a directory (point resolver at a temp path, call, assert `!Directory.Exists`).
- Modify: `src/ControlMenu/Modules/Jellyfin/Services/OperationLogger.cs:59-64` — remove `Directory.CreateDirectory(dir)`; return the path only. Add `Directory.CreateDirectory` at the actual backup-write site.
- [ ] Failing purity test · [ ] move CreateDirectory to write site · [ ] pass + existing backup tests green · [ ] commit.

### Task 2.6 — #55 use AssetPattern not hardcoded sqlite regex
- Test first: `tests/ControlMenu.Tests/Services/DependencyManagerServiceTests.cs` (or new) — `BuildVersionedDownloadUrl` uses the passed `dep.AssetPattern` to match the asset (a fake dep with a custom pattern resolves; sqlite-shaped pattern still works as a fallback).
- Modify: `src/ControlMenu/Services/DependencyManagerService.cs:619` — replace the literal `$@"(sqlite-tools-{platform}-x64-\d+\.zip)"` with `dep.AssetPattern` (with a guarded fallback if null). `ModuleDependency.AssetPattern` at `src/ControlMenu/Modules/ModuleDependency.cs:16`.
- [ ] Failing test · [ ] implement · [ ] pass · [ ] commit.

### Task 2.7 — #54 DeviceForm in-flight guard
- Test first: `tests/ControlMenu.Tests/...DeviceForm...` (bUnit) — invoking OnSave while a save is in-flight does not start a second save; button disabled during save.
- Modify: `src/ControlMenu/Components/Pages/Settings/DeviceForm.razor` — add `private bool _saving;`; `disabled="@(!IsValid || _saving)"` (line 52); wrap `OnSave` body in `_saving = true; try { … } finally { _saving = false; }` + `StateHasChanged`.
- [ ] Failing test · [ ] implement · [ ] pass · [ ] commit.

### Task 2.8 — #56 remove dead connection-string default
- Confirm in `src/ControlMenu/Extensions/ServiceCollectionExtensions.cs` (or wherever `AddControlMenuServices` builds the `DbContext`) that the SQLite path comes from `IDataPathResolver`, NOT `Configuration.GetConnectionString`.
- Modify: `src/ControlMenu/appsettings.json:10` — remove the misleading relative `"DefaultConnection": "Data Source=controlmenu.db"` (or replace with a comment-documented empty placeholder if the key must exist). Keep `AllowedHosts:"*"` and the `localhost` bind (decision).
- [ ] Confirm resolver-sourced · [ ] remove dead entry · [ ] `dotnet run` confirms DB still resolves to dataRoot (or existing startup test) · [ ] commit.

### Task 2.9 — Item 45 imaging IDisposable + accumulation test
- Test first: `tests/ControlMenu.Tests/Modules/Imaging/MagicWandPageTests.cs` — (a) fire two previews; assert exactly one source + one preview `*.png` remain under the temp webroot (regression guard for `DeletePreviewWebCopy`); (b) after disposing the rendered component, assert tracked temp files are deleted. Reuse the existing `_webRoot` harness (lines 30-37).
- Modify: `src/ControlMenu/Modules/Imaging/Pages/MagicWand.razor` — `@implements IDisposable`; `Dispose()` calls `ClearImageWebCopy()` + `DeletePreviewWebCopy()`.
- Modify: `ImageResize.razor`, `FormatConverter.razor` — add a tracked temp-path field, `@implements IDisposable`, delete it on `Dispose()` (they currently rely solely on the 5-min timer).
- [ ] Failing tests · [ ] implement Dispose on all three · [ ] pass · [ ] commit.

### PR 2 close-out
- [ ] CHANGELOG entries · [ ] full `dotnet test -c Release` green · [ ] whole-branch review · [ ] address · [ ] PR + CI · [ ] squash-merge + delete.

---

## PR 3 — Network managed rewrite (#37)

**Finding:** #37. Branch: `fix/audit-network-managed` (off merged PR-2 master). Decision = FULL managed.

### Task 3.1 — managed ping
- Test first: extend `tests/ControlMenu.Tests/Services/NetworkDiscoveryServiceTests.cs` around a seam — abstract reachability behind an injectable `IPingChecker` (or virtual method); test that discovery uses the managed result, no string parsing.
- Modify: `src/ControlMenu/Services/NetworkDiscoveryService.cs` — replace the `ping` shell-out + locale-fragile reply regex with `System.Net.NetworkInformation.Ping.SendPingAsync(host, timeout)` and check `PingReply.Status == IPStatus.Success`. No output parsing.
- [ ] Failing/seam test · [ ] implement managed Ping · [ ] pass · [ ] commit.

### Task 3.2 — managed ARP (Windows IP Helper) + Linux fallback
- Add: `src/ControlMenu/Services/Network/` an `IArpTableProvider` abstraction with: a Windows impl using P/Invoke `iphlpapi.dll` `GetIpNetTable` (returns IP→MAC pairs), and a Linux impl that shells `arp -a` and parses via the existing `LinuxArpRegex` (kept as the only place regex remains).
- Test first: unit-test the IP→MAC mapping/normalization logic (the part that converts the table rows into the service's model) with synthetic rows; the P/Invoke itself is integration-verified by running.
- Modify: `NetworkDiscoveryService` to consume `IArpTableProvider`; register the OS-appropriate impl in DI (`OperatingSystem.IsWindows()` switch at registration). Local-Deps note: P/Invoke into the OS `iphlpapi.dll` is an OS system call, not a bundled-binary dependency — compliant.
- [ ] Failing mapping test · [ ] implement provider + DI · [ ] pass · [ ] `dotnet run` real scan: ARP table populated on Windows · [ ] commit.

### PR 3 close-out
- [ ] CHANGELOG · [ ] full test green · [ ] real network smoke (scan finds devices) · [ ] whole-branch review · [ ] PR + CI · [ ] squash-merge + delete.

---

## PR 4 — Concurrency & lifecycle

**Findings:** #50, #51, #52, #53. Branch: `fix/audit-concurrency` (off merged PR-3 master). #52 + #53 are coupled (the lost-wakeup latch is consumed by the liveness loops).

### Task 4.1 — #51 per-process sentinel
- Test first: `tests/ControlMenuLauncher.Tests/InstallAclTests.cs` — assert two `IsWritable` calls use distinct probe filenames (or that concurrent probes don't collide). 
- Modify: `src/ControlMenuLauncher/InstallAcl.cs:41,55` — replace the fixed `".controlmenu-write-test"` with a per-call unique name `$".controlmenu-write-test-{Guid.NewGuid():N}"`. Update the XML doc comment.
- [ ] Failing test · [ ] implement · [ ] pass · [ ] commit.

### Task 4.2 — #52 IntervalChangeSignal lost-wakeup latch
- Test first: new `tests/ControlMenu.Tests/Services/IntervalChangeSignalTests.cs` — `Trigger(key)` BEFORE any `WaitAsync(key)` → the next `WaitAsync` completes immediately (signal latched, not lost); a second `WaitAsync` after that blocks again.
- Modify: `src/ControlMenu/Services/IntervalChangeSignal.cs` — add a pending-flag latch (e.g. `ConcurrentDictionary<string,bool> _pending`): `Trigger` sets pending + completes any waiter; `WaitAsync` returns `Task.CompletedTask` and clears pending if it was set, else registers a TCS.
- [ ] Failing test · [ ] implement latch · [ ] pass · [ ] commit.

### Task 4.3 — #53 liveness loops (dead catch + abandoned TCS + barrier)
- Test first: add `tests/ControlMenu.Tests/Modules/AndroidDevices/.../AndroidLivenessHostedServiceTests.cs` (none exists) mirroring the Camera one, driving `RunOneTickForTestsAsync`; assert a tick runs and the signal-driven `_lastTick` reset works. (Camera test already exists.)
- Modify: `src/ControlMenu/Modules/AndroidDevices/Services/AndroidLivenessHostedService.cs` and `src/ControlMenu/Modules/Cameras/Network/CameraLivenessHostedService.cs` — (1) remove the unreachable `catch (OperationCanceledException)` after `await Task.WhenAny(...)` (cancellation handled by the `IsCancellationRequested` check + while condition); (2) ensure the per-iteration `WaitAsync` TCS isn't abandoned on shutdown (folds into #52's latch design — no leaked uncompleted TCS); (3) make `_lastTick` `volatile` (or guard via `Interlocked`/lock) since it's written on the loop thread and read under the test seam.
- [ ] Failing Android test · [ ] implement on both services · [ ] pass (both liveness tests) · [ ] commit.

### Task 4.4 — #50 explicit exit-75 return
- Note: ledger path was wrong — it's `src/ControlMenu/Services/Update/VelopackUpdateService.cs:98` (`Environment.ExitCode = ExitCodeApplyUpdate`), consumed at `src/ControlMenu/Program.cs:120` (`await app.RunAsync()`).
- Test first: `tests/ControlMenu.Tests/Services/Update/VelopackUpdateServiceTests.cs` — assert `RequestApplyUpdate()` sets a shared apply-requested state (and that `ExitCodeApplyUpdate == 75` stays in sync with `ChildSupervisor.ExitCodeApplyUpdate`).
- Modify: introduce a tiny injected state holder (e.g. `UpdateApplyState { bool ApplyRequested }` singleton); `RequestApplyUpdate` sets it + `StopApplication()` (drop the `Environment.ExitCode` write). `Program.cs` after `await app.RunAsync()`: `return state.ApplyRequested ? VelopackUpdateService.ExitCodeApplyUpdate : 0;` (top-level statement return).
- [ ] Failing test · [ ] implement state + explicit return · [ ] pass · [ ] confirm exit-code const sync · [ ] commit.

### PR 4 close-out
- [ ] CHANGELOG · [ ] full test green · [ ] whole-branch review (concurrency focus) · [ ] PR + CI · [ ] squash-merge + delete.

---

## PR 5 — Build: eliminate 7zr via bundled SharpCompress (Item 46)

**Finding:** Item 46 / audit #17 remainder. Branch: `fix/audit-eliminate-7zr` (off merged PR-4 master). No C# behavior change; build-tooling only.

### Task 5.1 — validate build ordering
- Read `.github/workflows/release.yml` (≈:103-105 `stage-seed`) and `scripts/local-pack.ps1` and `scripts/stage-seed.ps1` — determine whether `dotnet publish`/`restore` runs BEFORE `fetch-magick.ps1`, i.e. whether `SharpCompress.dll` is present in the app's published/restored output at the moment extraction is needed.
- Decide mechanism: (a) `Add-Type -Path <published SharpCompress.dll>` in `Expand-Cm7z`, resolving the DLL from the app's own publish output (Local-Deps compliant), OR (b) a tiny dedicated extraction step if ordering makes (a) fragile.
- [ ] Confirm ordering · [ ] choose mechanism · [ ] (no commit — analysis).

### Task 5.2 — managed extraction in Expand-Cm7z
- Modify: `scripts/dependencies/_Fetcher.ps1` — delete `Get-Cm7zr` (lines ~85-101, incl. the unversioned URL + TOFU SHA pin); rewrite `Expand-Cm7z` (lines ~103-117) to extract via SharpCompress using the chosen mechanism, mirroring `ArchiveExtractor.cs:28-41` (`SevenZipArchive.OpenArchive` → iterate entries → `WriteToDirectory` with zip-slip guard). Resolve `SharpCompress.dll` from the app's own output dir, NEVER `~/.nuget`.
- [ ] Implement · [ ] (verify next).

### Task 5.3 — verify magick extraction end-to-end
- Run locally: `pwsh -NoProfile -File scripts/stage-seed.ps1` (or the magick-only path) and confirm `ImageMagick-7.1.2-25-portable-Q8-x64.7z` extracts to the cache via the managed path — `magick.exe` present, no `7zr.exe` fetched.
- [ ] Run stage-seed · [ ] confirm magick extracted · [ ] confirm no 7zr download · [ ] commit.

### Task 5.4 — docs + CHANGELOG
- Update `docs/TECHNICAL_GUIDE.md` (the #73 `.7z`/7zr note) to reflect managed SharpCompress extraction at build time. CHANGELOG `[Unreleased]` entry.
- [ ] Docs · [ ] CHANGELOG · [ ] commit.

### PR 5 close-out
- [ ] full `dotnet build`/`test` green · [ ] a clean local pack still produces a working bundle with magick · [ ] whole-branch review · [ ] PR + CI (release.yml path exercised if feasible, else note) · [ ] squash-merge + delete.

---

## Post-batch
- [ ] Update `reference_control_menu_security_review.md` ledger: mark #37/#38/#40/#42–#57 SHIPPED, #39 documented, Item 45/46 done — only #58 (n/a) remains; audit fully closed.
- [ ] Update `todo_control_menu.md` Item 43 + Items 45/46; archive shipped.
- [ ] Refresh breadcrumb.
- [ ] Standing gate unchanged: v1.2.0 VM smoke (separate).

## Self-Review notes
- **Coverage:** every target finding (#37,38,39,40,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57 + Item 45 + Item 46) maps to a task. #41 (done #65), #58 (n/a) excluded by design.
- **Already-done guards:** #54 CastCrewUpdate untouched (only DeviceForm); #49 keeps `"go2rtc"`; #56 keeps bind/AllowedHosts.
- **Risk hot-spots:** #37 P/Invoke (integration-verify by running), #50 top-level-return plumbing, #52/#53 coupling, #46 build ordering — each has an explicit validate/verify step.
