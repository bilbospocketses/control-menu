# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **`backups/origina tools-menu backup/ControlMenu.ps1`** — archived copy of the legacy PowerShell tools menu that this project replaced, kept for historical paper trail. Hardcoded Pixel 9 unlock PIN on line 841 redacted before commit (`<REDACTED>` placeholder). Follow-up scrub on the same file: 3 hardware MAC addresses, 3 LAN IPs, and 1 Pixel 9 hardware serial number also redacted now that the repo is public.

### Changed

### Removed

### Fixed

## [1.0.0] - 2026-05-07

First official release.

### Added

- **Home page — discovery-first dashboard.** Replaces the prior hero+module-card grid layout. Header band with brand icon (44×44) + title + status line (`N Android · M Cameras discovered · X Androids and Y Cameras registered · last scan Xs ago`, or cold-state tagline `Find devices and cameras on your network, then manage them.`) + three fixed-width Quick Scan buttons (Scan Android / Scan Cameras / Scan All). Below the band, two stacked Discovered sections embed the existing shared `DiscoveredPanel` (Android) and `DiscoveredCamerasPanel` (Cameras). Compact 6-tile module-nav row pinned to viewport bottom (sticky-footer pattern). Setup-wizard guard preserved unchanged. Implementation lives in four new components under `Components/Pages/HomeSections/`, each with its own scoped `.razor.css`.
- **Quick Scan buttons with composition rules.** Three fixed-width buttons (Scan Android / Scan Cameras / Scan All) never resize through state transitions. Composition: clicking Scan All while a specific scan is running skips the in-flight scan and kicks off only the other in tandem; Scan All immediately enters Running state to represent "all three are or are about to be running." Buttons disable while their state is Running.
- **Module-tile nav row** routes each tile to its module's lowest-`SortOrder` visible `NavEntry` (predicate-aware), with `/settings/cameras` fallback when zero cameras are registered, and the Settings tile hardcoded to `/settings/general`. Responsive grid: 6 → 3 → 2 columns at 900px / 480px breakpoints.
- **Outlined placeholder cards** in empty Discovered sections. Both Android and Cameras sections always render their headers + count badges, with dashed-border outlined placeholder cards filling the body when no devices are present.
- **`IDeviceQuickScanService`** extracts the full Quick Refresh body (mDNS scan + ARP map + ping-then-re-read for missing IPs + dedupe + `Handler.ReplaceDiscovered`) from `DeviceManagement.QuickRefresh` so Settings → Android Devices and Home Quick Scan share one pipeline.
- **Setup Wizard — Cameras step.** First-run users can discover and register IP cameras during the Setup Wizard. Single `Scan Network` button auto-detects the LAN subnet via `SubnetDetectionClient` and runs ONVIF-only WS-Discovery. Discovered ONVIF cameras stream into the existing `DiscoveredCamerasPanel`: inline Add per row, optional shared-creds entry above the grid. Registered cameras render in a small summary table. WizardDone surfaces the camera count alongside the device count.
- **`ICameraScanService.StartOnvifOnlyScanAsync(IReadOnlyList<ParsedSubnet>, CancellationToken)`** — new ONVIF-only scan entry point. Same `Phase` transitions, event bus, and `Hits` accumulation as `StartScanAsync`, but skips the parallel TCP-554 sweep branch.
- **`WizardState.CamerasAdded`** for the Done-step summary line.
- **Background liveness probing for cameras and Android devices.** Two new hosted services (`CameraLivenessHostedService`, `AndroidLivenessHostedService`) periodically TCP-probe each registered camera (RTSP port) / device (ADB port) and bump `LastSeen` on success. Liveness path is per-target direct probing, never populates Discovered. Hot-reload via a new `IntervalChangeSignal` singleton.
- **Liveness Interval setting on Settings → Cameras** (top of page). New `cameras-liveness-interval-seconds` config key. Allowed values: `0` (disabled) or `300–3600` seconds. Default `300`.
- **Restore Default button** next to the Liveness Interval input on both Settings → Cameras and Settings → Android Devices.
- **Scan Now button** between the Liveness Interval input and Restore Default on both pages — triggers an immediate liveness tick without waiting for the next periodic cadence.
- **Settings → Cameras Quick Scan button.** Mirrors the Android Devices page's Quick Refresh / Scan Network split. Quick Scan auto-detects the LAN subnet and runs `StartOnvifOnlyScanAsync` directly — fast (~3-5s), ONVIF only, no modal.
- Newly-added cameras seed `LastSeen = DateTime.UtcNow` on insert (`CameraService.AddAsync`) so freshly-registered cameras don't render as "Never" while waiting for the first liveness tick.
- **Configurable Jellyfin backup and log directories.** New `jellyfin-backup-directory` and `jellyfin-log-directory` settings on the Jellyfin Settings page. Empty value falls back to derived defaults under `AppContext.BaseDirectory`. Saving a new path best-effort-migrates existing `*.db` (Backups) or `*.log` (Logs) files; per-file failures surface in the standard notification.
- **Reusable Settings grid components** (`SettingsSection`, `SettingsGrid`, `SettingsGridCell`) under `Components/Shared/Settings/`. Two-column grid pattern with optional Label / Hint slots and FullRow span.
- **External Dependencies section** on Settings → General. Two full-row configurable inputs: ws-scrcpy-web URL (always external; user runs the instance) and Docker executable directory path.
- **Docker version probe — Installed and Latest** on the Dependencies page. Docker dep registration declares an `InstallPath` so the version-probe pipeline runs `docker --version` against the user-configured path. New `VersionCheckUrl` against the moby/moby GitHub releases API populates the Latest column.
- **Camera network scanner + DB-backed camera CRUD.** New Settings → Cameras page replaces the indexed 8-slot panel. Cameras live in their own `Cameras` table with full CRUD; manage Add/Edit/Delete/Enable from the UI; deletion cleans up associated secrets. Stream key in `go2rtc.yaml` changed from `camera-{1..N}` to `camera-{guid:N}`. New scan flow: ONVIF WS-Discovery probe (multicast UDP 3702) finds ONVIF-conformant cameras; TCP-554 sweep finds non-ONVIF RTSP cameras; both run in parallel against the same subnet list. Discovered panel: ONVIF rows take inline username/password and validate by `GetStreamUri`; non-ONVIF rows open a fuller modal with stream path + RTSP `DESCRIBE` validation. Periodic background re-scan every 15 min (configurable via `cameras-scan-interval-minutes`).
- **Hikvision/LTS-OEM ISAPI integration** (post-Add enrichment). After ONVIF Add succeeds, fires a best-effort `GET /ISAPI/System/deviceInfo`. If the response parses as Hikvision-firmware DeviceInfo XML, populates Camera Number, MAC address (normalized), and overrides the auto-filled Name with `<deviceName>` unless the user manually edited Name. Behavior-based vendor detection — works for Hikvision, LTS, Honeywell Performance, W Box, Annke, etc. Auth supports both Basic (V5.7+) and Digest (V5.6.x).
- **Camera grid display fields as typed columns.** `Camera` entity gained `MacAddress`, `CameraNumber`, `FirmwareVersion`, `SerialNumber`, `HardwareId` columns; the speculative `Metadata` JSON column was removed. Configured-cameras grid sorted by Camera Number ascending (nulls last).
- **Bulk Delete All on Cameras and Devices settings pages.** Red "Delete All" toolbar button on each page (only rendered when ≥1 row); confirm dialog; atomic batch delete with single change-notifier fire.
- **Per-device scrcpy stream settings modal** on all device dashboards (feat(a5)). "Stream Settings" in quick-actions opens a settings modal (video codec, encoder, bitrate, max FPS, max resolution, audio toggle, audio source, audio codec) with probe-driven smart defaults. Settings persist in the database and pass to ws-scrcpy-web as URL params on the embed iframe.
- **Android Power Tools module** (sort order 2, between Android Devices and Jellyfin). Hosts ws-scrcpy-web's full home page via iframe at `/android-power-tools`. Page now performs an HTTP probe against the configured ws-scrcpy-web URL on initialization and renders a friendly warning (with Re-check button) when unreachable, instead of letting the iframe show a browser default error page.
- **`WsScrcpyService.ProbeAsync(CancellationToken)`** — HTTP HEAD probe with 2s timeout against `BaseUrl/`. Returns true on any response, false on connection refused / DNS failure / timeout. Used by AndroidPowerToolsPage to gate iframe rendering.
- **Custom camera SVG icon** for the Cameras module group + per-camera nav entries. Replaces the `bi-camera-video` Bootstrap icon for the module group header (Sidebar + HomeModuleTiles) and the camera emoji for individual camera entries. Tightened viewBox so the icon fills its 24×24 box at parity with android-logo and jellyfin-logo siblings.
- `LICENSE` — GPL-3.0-only (matches ws-scrcpy-web, the primary external dependency).
- `SECURITY.md` — private vulnerability reporting flow via GitHub Security Advisories, 72-hour acknowledgement SLA.
- `CONTRIBUTING.md` — setup, build/test commands, project structure, module architecture, code style, PR guidelines.
- `CHANGELOG.md` — Keep a Changelog format.
- `.gitignore` entries for `.claude/` (Claude Code session artifacts), `src/ControlMenu/dependencies/go2rtc/*`, and `src/ControlMenu/go2rtc.yaml` (runtime-generated config).
- `DeviceType` enum gained `AndroidTablet` and `AndroidWatch`. Persisted as string via EF; no migration needed.
- `ScrcpyMirror.razor` gained an optional `DeviceKind` parameter (`"phone"`, `"tablet"`, or `"tv"`). Forwarded to ws-scrcpy-web's `/embed.html` as `&deviceKind=<value>` so the stream toolbar's D-pad/Touch input-mode toggle seeds the right default.
- `IAdbService.ScanMdnsAsync()` — returns `MdnsAdbDevice` records by parsing `adb mdns services`.
- `IAdbService.DetectDeviceKindAsync(ip, port)` — classifies a device as `"phone"`, `"tablet"`, `"tv"`, or `"watch"` using five parallel shell probes.
- `IAdbService.GetPropAsync(ip, port, property)` — thin `adb shell getprop <name>` wrapper.
- **"Discovered on Network" panel** in Settings > Devices. After a Scan, mDNS-advertised devices that aren't yet registered show up with IP, advertised ADB port, MAC (resolved via ARP), and a one-click Add button.
- **Delete remembers the device's name.** When a device is deleted from Settings > Devices, its user-assigned name is stashed in the Settings table at `device-name-<mac>`. Restored on re-add via Discovery.
- **Network scanner modal** — Settings > Devices `Scan Network…` button opens a full scanner modal (CIDR/IP/range subnet input, gateway auto-detect, four-state progress chip, live hit stream, Add buttons per hit, 2,048-host large-subnet warning, subnet syntax cheat sheet at `/help/subnets.html`).
- **`NetworkScanService` (singleton)** — holds a `ClientWebSocket` to ws-scrcpy-web's `/ws-scan` endpoint, fans scan events to every Blazor circuit spectating a scan.
- **`SubnetParser`** — CIDR / IP / range parser mirroring ws-scrcpy-web's behaviour byte-for-byte.
- **Device SerialNumber auto-fill** on inline Add from Discovered panel — probes `ro.serialno` alongside kind and model probes.
- **Iframe theme bridge** — Control Menu's theme syncs bidirectionally with the embedded ws-scrcpy-web iframe on the Android Power Tools page. Toggling theme from either side updates both surfaces. Requires bundled ws-scrcpy-web `v0.1.24-beta.5` or later.
- `wwwroot/js/scrcpyThemeBridge.js` — vanilla-JS interop module that bridges `themeManager` to ws-scrcpy-web's `postMessage` theme protocol.
- `themeManager.subscribe(callback)` and `themeManager.subscribeBlazor(dotnetRef)` — JS subscriber API in `wwwroot/js/theme.js`. The Blazor variant uses a numeric token (returned to .NET, passed back to `unsubscribeBlazor`) to avoid `IJSObjectReference` marshaling pitfalls.
- `id="ws-scrcpy-iframe"` attribute on the iframe in `Modules/AndroidPowerTools/Pages/AndroidPowerToolsPage.razor`.
- `docs/superpowers/specs/2026-04-29-iframe-theme-bridge-design.md` and `docs/superpowers/plans/2026-04-29-iframe-theme-bridge.md`.
- **Sidebar fly-out menu** — clicking a section icon while the sidebar is collapsed opens a floating panel listing that module's nav entries (including device-type sub-links). Closes via click-outside, Esc, link click, or uncollapsing the sidebar.

### Changed

- **Status line registered count split per-type.** From combined `K registered` to `X Androids and Y Cameras registered` so the inventory composition is self-documenting.
- **Home page brand icon enlarged** from 36×36 to 52×52 to read as a peer of the Quick Scan buttons rather than a chip; cold-state status-line font bumped from 0.6875rem to 1rem for readability.
- **Home section CSS tokens** — replaced undefined `--bg-elevated` / `--bg-card` / `--bg-card-hover` / `--border-subtle` with `--surface-recessed` / `--card-bg` / `--hover-bg` / `--border-color` (defined for both light + dark themes). Fixes light-mode dark-on-dark contrast bug where Discovered sections had dark backgrounds and module tile labels were nearly invisible.
- **Mirror panel background** set to `#000` on Pixel/Tablet/Watch dashboards so any aspect-ratio mismatch between the computed iframe ratio and ws-scrcpy-web's actual toolbar+content width blends as a uniform bezel rather than appearing as letterbox bars on top/left/right.
- **Sidebar subscribes to `ICameraChangeNotifier.CamerasChanged`** — camera CRUD (add/delete/enable-toggle/rename) now propagates to the sidebar in real-time, mirroring the Android device-type pattern. `CamerasModule.EnabledCameras` static list now correctly filters by `Enabled` so disabled cameras don't appear in the nav.
- **Android Power Tools page** — gates iframe rendering on a real HTTP probe (`WsScrcpyService.ProbeAsync`) rather than the misleadingly-named `IsRunning` flag. Shows a friendly warning + Re-check button when ws-scrcpy-web is unreachable.
- **Wizard Dependencies step** — docker grouped at the top of the External (installed separately) section alongside ws-scrcpy-web, matching Settings → Dependencies behavior.
- **Settings grid cell hover transition softened** from 120ms full-opacity to 280ms ease-out at 22% opacity (via color-mix). Subtle surface tint on General + Jellyfin pages instead of an abrupt color flip.
- **Android Power Tools warning + checking alerts** are now centered both horizontally and vertically in the page area, with larger 1.25rem font and a 720px max-width for readability when ws-scrcpy-web is unreachable.
- **Manual Add Camera form** — IP and Port fields are now editable on the manual-Add path (toolbar "Add Camera Manually" button). Previously hard-coded `disabled`, breaking manual entry. Discovered-row Add path still pre-fills these from the scan; user can adjust if needed.
- **WizardDevices intro paragraph** now explicitly calls out the mDNS-only discovery limitation and directs older Android devices to Settings → Android Devices post-wizard.
- **Settings sub-nav label "Devices" → "Android Devices"** (route identifier `devices` unchanged).
- **Android Devices `Discovery Interval` → `Liveness Interval`.** The setting was previously dead UI; it now drives the new `AndroidLivenessHostedService`. Validation tightened: allowed values are `0` (disabled) or `300–3600` seconds. Same `discovery-interval` config key.
- **Camera periodic background loop refactored from subnet-scan to per-camera liveness.** The previous `CameraScanHostedService` invoked `ICameraScanService.StartScanAsync` on every periodic tick — auto-populating the Discovered panel without user intent. The replacement `CameraLivenessHostedService` only probes registered cameras directly. Old `cameras-scan-interval-minutes` config key replaced by `cameras-liveness-interval-seconds`.
- `CameraService.UpdateLastSeenAsync` and `DeviceService.UpdateLastSeenAsync` fire their respective change-notifier so the Settings UI grid auto-refreshes Status/LastSeen when a liveness tick lands.
- **Local-Dependencies-Only audit (architectural sweep, 17 commits `f68f186..8be3f07`).** Eliminates every bare-name binary invocation that resolved through system `PATH`, replacing them with a new `IDependencyPathResolver` boundary that maps `(moduleId, name) → absolute path under dependencies/`. Migrated `AdbService` (~25 call sites) via private `AdbAsync` helper; `WsScrcpyService.SpawnProcess` resolves via fresh DI scope; `JellyfinService` sqlite3, `DependencyManagerService` `adb kill-server`, `Go2RtcService.FindExecutable` all routed through resolver. Replaced external `tar` with `System.Formats.Tar`. Removed system-PATH fallback and `Program.cs` PATH-prepend block. Net behavior change for users: a tool previously found on system PATH but never installed locally now reports "Not found" until installed via the dep manager or via a user-configured `dep-path-{name}` override.
- `MainLayout.razor` page-title switch gained a specific case for `/android-power-tools` above the generic `path.StartsWith("android")` fallback (prefix-match order is load-bearing).
- **Scan Network** (Settings > Devices) queries mDNS first via `adb mdns services`, enriches each hit with MAC from the ARP table, and falls back to ARP-only refresh for non-mDNS devices. Stale ADB ports are silently updated when mDNS reports a fresher value.
- **Adding a new device auto-removes it from the Discovered panel** so the user doesn't see a just-added device still listed.
- `ScrcpyMirror.razor` uses ws-scrcpy-web's `/embed.html?device=<udid>` wrapper URL (legacy hash routing removed upstream).
- `docs/TECHNICAL_GUIDE.md` — Stream URL section updated; Android Devices module description updated; AdbService method table loses `GetUsbDevicesAsync` and `ResetTcpPortAsync`, gains `ScanMdnsAsync`; dashboard descriptions note the `DeviceKind` hint.
- `docs/manual-test-checklist.md` — Section 5 rewritten into 5a/5b/5c/5d for the new mDNS-first device flow; Section 12 (Cameras > Camera View) rewritten for the post-scanner architecture; many drift-fix updates from the v1.0.0 manual smoke test.
- `README.md` — License section updated to GPL-3.0-only; new Security section.
- **`Settings › Devices`** — `Scan Network` button split into `⟳ Quick Refresh` (silent mDNS+ARP, unchanged) and `📡 Scan Network…` (new modal). "Discovered on Network" section mirrors the last scan: Full Scan replaces the list; Quick Refresh merges.
- **`WsScrcpyService`** — `BaseUrl` resolves at start time from the user-configured URL.
- **`DependencyManagerService.CheckHealthAsync`** — ws-scrcpy-web entry pings `{url}/`.
- **Scan UX redesign** — `Scan Network…` modal shrinks to a subnet picker + Start button. Chip, live hit stream, Cancel button, and per-row Add buttons live on the Settings → Devices page between the registered-devices table and the Discovered section. Each Discovered row gains a `×` Dismiss button.
- **Chip palette theme-aware** — `ScanProgressChip` swaps hardcoded hex for `color-mix` over `--info-color` / `--warning-color` / `--success-color` / `--danger-color` tokens.
- **`.source-badge` token-driven** — uses `color-mix` over `--warning-color` instead of hardcoded amber.
- **`ScanMergeHelper.AddressKey(ip, port)`** centralizes the `"ip:port"` format used across dismiss / exclude / dedupe sets.
- **Shared ARP snapshot on `scan.complete`** — `FinalizeScanAsync` orchestrates enrichment in one ARP pass. Worst-case ARP shells drop from 4 to 2.
- **Scanner extraction** — The 120-line scan event dispatch + finalize orchestration moves into `ScanLifecycleHandler`; the ~40-line Discovered panel markup moves into `DiscoveredPanel.razor`. Closes the `_discovered`-across-awaits race.
- **Discovered panel inline Add** — rows contain editable fields (Name, Type, Port, MAC, Serial, PIN) populated by on-mount ADB probes. Clicking Add saves directly. Source column shows a small `mdns`/`tcp`/`adb` badge.
- **Settings › Devices table alignment** — Device Management and Discovered tables share a scoped `.data-table-fixed { table-layout: fixed; }` class; column widths align pixel-for-pixel between tables.
- **Sidebar logo centered** — pill button is horizontally centered in the header instead of left-aligned. Collapse toggle moves to absolute positioning on the right edge.
- `themeManager.set(theme)` calls `scrcpyThemeBridge.notify(theme)` (existence-checked) before fanning out to subscribers, so iframe sees state changes before Blazor subscribers do.
- `Components/Layout/TopBar.razor` implements `IAsyncDisposable` and registers a `themeManager.subscribeBlazor` callback so the toggle button's icon/title stay in sync.
- **General Settings page rewritten with the SettingsGrid components.** General section reordered (Re-run Setup Wizard, Timezone, Theme); Theme cell gains a hint pointing at the global top-right toggle. Email (SMTP) and ws-scrcpy-web sections in 2-column grid.
- **Jellyfin Settings page rewritten with the SettingsGrid components.** All inputs auto-save on `@onchange` / `@onblur` matching the General SMTP pattern — only **Save & Parse** under Docker Compose remains as an explicit action button.
- **Settings sub-nav reordered.** New order: General, Jellyfin, Android Devices, Cameras, Dependencies.
- **`OperationLogger.GetBackupDirectory` / `GetLogDirectory` renamed to `GetDefaultBackupDirectory` / `GetDefaultLogDirectory`** to make their fallback nature explicit. Override-aware path resolution lives in the new `IJellyfinDirectoryResolver`.
- **Dependencies page — read-only Install Path for docker and ws-scrcpy-web.** Both rows render with the standard 7-column layout. Update / Install buttons removed from these two rows. Sort places docker + ws-scrcpy-web together at the top.

### Removed

- **Standalone `node` dependency declaration** from `AndroidDevicesModule`. Dead since the External-mode-only refactor removed the spawn path that needed it. Also deleted `src/ControlMenu/dependencies/node/` folder.
- **Indexed 8-slot camera config.** Legacy `camera-{1..N}-name|ip|port` settings + `camera-{1..N}-username|password` secrets purged on first launch via a one-shot, idempotent migration. Pre-migration users will need to re-add their cameras through the new scanner.
- **`build.log`** — stale build output containing old `tools-menu` paths; added to `.gitignore`.
- **`IScanLifecycleHandler.StashedNamesByMac`** — cache was always empty at row-mount time. Stashed-name restoration is handled by `DiscoveredPanelRow.RunProbesAsync` reading `device-name-<mac>` directly.
- `UsbSetupWizard.razor` and its stylesheet. mDNS-based discovery replaces the USB-cable handshake. `IAdbService.GetUsbDevicesAsync` and `ResetTcpPortAsync` deleted along with it.
- **Backup Directory row** from the Docker Compose parse-result table on Jellyfin Settings. Path is now configured in the Logging, Backup & Retention section.
- **Backup & Retention** and **Managed Directories** standalone sections — merged into the new combined **Logging, Backup & Retention** section.
- **Managed mode for ws-scrcpy-web.** Control Menu no longer spawns or supervises the ws-scrcpy-web node process. `WsScrcpyService` is now an external-only URL holder.
- **Obsolete settings: `wsscrcpy-mode`, `ws_scrcpy_web_path`** (under module `android-devices`). Deleted on app startup by a one-time idempotent cleanup hosted service.

### Fixed

- **Manual Add Camera form** — IP and Port were hard-coded `disabled`, breaking manual entry from the toolbar "Add Camera Manually" button. Now editable with parameter pre-fill preserved for the Discovered-row inline-Add path.
- **Sidebar nav stale on camera CRUD.** Adds, deletes, enable-toggles, and renames now propagate to the sidebar in real-time without page reload, including cross-circuit (other browser tabs update without their own action).
- **Android Power Tools "ws-scrcpy-web isn't running" warning** is shown when the configured URL is actually unreachable — previously the iframe loaded unconditionally and the user got the browser's default localhost-refused error page.
- **Phone mirror uniform black bars on top/left/right** — defensive fix: mirror-panel background `#000` so any aspect-ratio mismatch blends as a uniform bezel rather than visible bars. Tighter aspect calibration via postMessage measurement of ws-scrcpy-web's actual toolbar width is a v1.0.1 follow-up.
- **go2rtc orphan on next startup** — Go2RtcService now registers cleanup on `IHostApplicationLifetime.ApplicationStopping` AND `AppDomain.ProcessExit` in addition to the existing `IHostedService.StopAsync`, catching non-graceful shutdowns (Ctrl+C in dev, debugger-stop, terminal close) that previously left the child running.
- **go2rtc runtime process leak from concurrent CRUD events** — bulk camera scans / rapid Add-Delete-Toggle sequences could leak hundreds of `go2rtc.exe` instances over a long session (one observed: 624 zombies eating 9.27 GB). Three compounding bugs: (1) `OnCamerasChanged() => _ = RegenerateConfigAsync()` was fire-and-forget, letting concurrent events race past the alive-check before any reached the lock; (2) `KillProcess` never `WaitForExit`'d, so the next `SpawnProcess` could race the dying process; (3) startup orphan-kill only targeted the one bound to port 1984, leaving hundreds of unbound stragglers. Fix: SemaphoreSlim serializes `RegenerateConfigAsync`; `KillProcess` now waits up to 3s for confirmed exit and falls back to a `KillAllOrphans()` sweep; `KillAllOrphans()` runs at startup as defensive cleanup of any accumulated leakage from prior sessions.
- **go2rtc steady-state restart loop from liveness-triggered notifier fires** — root cause of the leak above. `CameraService.UpdateLastSeenAsync` fires `CamerasChanged` on every successful liveness probe so the Settings UI status dots refresh; this triggered `RegenerateConfigAsync → kill+spawn go2rtc` even though `LastSeen` isn't part of the go2rtc YAML so the new config was byte-identical to the old. With N enabled cameras, that's N unnecessary kill+spawn cycles per liveness interval. Fix: `GenerateConfigAsync` now returns `bool` indicating whether the YAML actually changed, and `RegenerateConfigAsync` short-circuits when unchanged. Structural changes (add/delete/edit/enable-toggle) still trigger a real restart; status-dot UI refresh keeps working off the same notifier event.
- **go2rtc phantom respawn on every intentional kill** — final go2rtc bug surfaced by user testing after the previous two fixes. `_process.Exited += OnProcessExited` was wired in `SpawnProcess`; when `KillProcess` called `Kill()`, the dying process's `Exited` event fired, routing to `OnProcessExited`, which had no way to distinguish "we killed it" from "it crashed" and scheduled a phantom respawn 2 seconds later. The phantom couldn't bind port 1984 (the real replacement was already alive) but stayed running, leaking exactly +1 process per CRUD operation. Fix: detach the `Exited` handler immediately before calling `Kill()` in `KillProcess`. Crash-restart logic now only fires on genuinely unexpected exits.
- **GitHub API requests now include a `User-Agent` header.** `dependency-updates` and `github-api` named HttpClients default to `User-Agent: ControlMenu`. Without this, GitHub returns HTTP 403 — which silently failed the docker version-check.
- **moby/moby tag_name regex** matches the actual `docker-v29.4.2` tag format.
- **Stale `external` placeholder in `Dependencies.InstalledVersion`** for the ws-scrcpy-web row is cleared on every boot by the obsolete-settings cleanup hosted service.
- **Settings → Android Devices refreshes Status / Last Seen in-place after a liveness tick.** `DeviceManagement.razor` previously only subscribed to `IScanLifecycleHandler.OnStateChanged`, not `IDeviceChangeNotifier.Changed`. Page now subscribes to the notifier in `OnInitializedAsync`, mirroring `CameraSettings.razor`.
- **Scan-event subscribers handle `ObjectDisposedException` from `InvokeAsync` after disposal.** `ICameraScanService` is a singleton whose `Emit` fan-out can invoke a subscriber callback after the consumer component has disposed. `DiscoveredCamerasPanel.HandleEvent` and `WizardCameras.OnScanEvent` wrap `InvokeAsync` in `try/catch (ObjectDisposedException)`.
- **WizardCameras subnet-detect failure message** updated to call out "verify ws-scrcpy-web is running" explicitly.
- **`DiscoveredCamerasPanel` no longer surfaces just-registered cameras as still-discoverable across navigation.** Bootstrap path now filters `ScanService.Hits` against `CameraService.GetAllAsync()` before populating its local list.
- **`fix(jellyfin)` Backup directory persisted as stale `tools-menu` path.** `JellyfinService.BackupDatabaseAsync` and `CleanupOldBackupsAsync` now call `OperationLogger.GetDefaultBackupDirectory()` directly (runtime-derived, never persisted).
- **`fix(deps)` Dependency install-path overrides survive repo renames.** `SetInstallPathAsync` stores overrides relative to `DepsRoot` when the chosen path lives under it. `SyncDependenciesAsync` validates overrides at startup; any whose resolved parent doesn't exist is logged and cleared.
- **`fix(utilities)` Icon Converter file picker** — base64-encoded transport for the binary payload, raised Blazor Server `MaximumReceiveMessageSize` to 32 MB, wrapped in try/catch.
- **`fix(a5)` scrcpy probe action** — was sending `action=PROBE_DEVICE`, ws-scrcpy-web expected `action=probe`. Probes now save correctly.
- **`fix(a5)` codec/encoder dropdowns** filter to what the device supports via `DetectVideoCodecs` / `DetectAudioCodecs` / `EncoderMatchesCodec`.
- **`fix(a5)` Encoder dropdown** — hardware-preferred encoder auto-selection (`PickBestEncoder`) matching ws-scrcpy-web's vendor-prefix logic.
- **`fix(a5)` Audio source default** changed from `playback` to `output`.
- **`fix(a5)` Added `sdkInt` field to `ScrcpyProbeResult`.** Modal gates audio UI by SDK version.
- **`fix(b5)` Cross-circuit sidebar nav desync** — replaced scoped `IDeviceService.DevicesChanged` with singleton `IDeviceChangeNotifier` so `DeviceTypeCache` and `DeviceTypePresenceWatcher` receive notifications across Blazor Server circuits.
- **`fix(home)` Home page module-card sub-link pills now render SVG-path icons as `<img>` tags** instead of as literal path text.
- **`fix(tablet,watch)` cloned dashboards now have their `.razor.css` files** so scoped styles attach correctly.
- **`fix(wizard)` Setup wizard container `max-width: 960px → 1260px`** so the DiscoveredPanel table doesn't horizontally scroll.
- **`fix(scanner)` DiscoveredPanel ADB Port column `width: 70px → 105px`** to fit 5-digit ports.
- **`fix(tables)` `.data-table .actions`** uses `text-align: right` instead of `display: flex` on the `<td>` so row borders flow correctly.
- **`fix(theme)` TopBar theme toggle** stays in sync when theme changes via the iframe bridge.
- **`fix(theme)` Blazor subscriber pattern** — switched from returning a JS unsubscribe closure (broke the Blazor circuit) to a numeric-token handle pattern.

[Unreleased]: https://github.com/bilbospocketses/control-menu/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/bilbospocketses/control-menu/releases/tag/v1.0.0
