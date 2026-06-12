# Home Page — Restore Menu-Sections Layout (Remove Discovery Dashboard)

- **Date:** 2026-06-12
- **Status:** Design approved — implementation pending
- **Branch:** `revert/home-menu-sections`
- **Supersedes:** `docs/superpowers/specs/2026-05-05-homepage-polish-design.md` (+ its plan)

## Context

The home page (`/`) was rewritten in `ed147da` (*"Home.razor rewrite — discovery dashboard composition"*) into a live **discovery dashboard**: a scan band (Scan Android / Scan Cameras / Scan All) plus "Discovered Android" and "Discovered Cameras" panels, with a compact `HomeModuleTiles` band for module navigation. That scan UI duplicates functionality that already exists in **Settings → Device Management**, **Settings → Cameras**, and the **setup Wizard** — the home-page scan handlers literally comment that they "Mirror Settings → Device Management → Quick Scan".

A live-scanning surface doesn't fit the app's role. Control Menu is a launcher/manager; the home page should be a clean menu of what the app can do. The prior layout — a hero plus one card per module with its sub-pages as pill links — was last seen at `6cdda9b` (the commit immediately before the rewrite). That is the desired home page.

## Goal

Restore the original menu-sections home page: **hero + one card per registered module (sub-pages as pill links) + a Settings card.** Remove all scanner UI from the home page.

## Non-Goals

- **Do NOT remove device/camera scanning from the app.** The scan services and the Settings/Wizard scan UIs stay exactly as-is. Only the home-page scanner surface is removed.
- No redesign beyond restoring the prior layout and reconciling it with the modules/routes added since `6cdda9b`.

## Approach

**Restore-from-diff + reconcile** (chosen). Recover `Home.razor` + `Home.razor.css` from `6cdda9b`, then adapt to current code (see Deviations).

Rejected alternatives:
- **One-shot `git revert ed147da`** — the discovery dashboard arrived across ~14 commits interleaved with camera/liveness/module/Item-21 work we keep; a blanket revert would clobber wanted changes and wouldn't remove the separately-added component files.
- **Hand-rebuild from scratch** — reintroduces bugs the original already solved, with no benefit.

## Design

### Restored layout (data-driven)

When setup is complete, `Home.razor` renders:
- A **hero** (app icon `/icon-512.png` + title + subtitle).
- A **`module-grid`**: for each module in `ModuleDiscovery.Modules`, a `module-card` with a header (brand logo or Bootstrap icon + `DisplayName`) and a `module-links` list of pill links — one per **visible** nav entry, ordered by `NavEntry.SortOrder`.
- A hardcoded **Settings** card.
- The existing `setup-completed` gate (renders `<SetupWizard />` until setup is done).

Modules today (by `SortOrder`):

| # | Card | Pills (visible nav entries) | Logo |
|---|------|------------------------------|------|
| 1 | Android Devices | Device List (+ Google TV / Phone / Tablet / Watch *when such devices exist*) | android-logo.svg |
| 2 | Android Power Tools | Power Tools | `bi-wrench` |
| 3 | Jellyfin | DB Date Update · Cast & Crew | jellyfin-logo.svg |
| 4 | Utilities | File Unblocker | `bi-tools` |
| 5 | Cameras | one pill per registered camera — **card hidden when none** | cameras-logo.svg |
| 6 | Imaging Tools | Icon Converter · Format Converter · Image Resize · SVG Rasterize · Magic Wand · Tracing | `bi-image` |
| — | Settings | General · Jellyfin · Android Devices · Cameras · Dependencies | `bi-gear` |

Imaging Tools (added after `6cdda9b`) appears automatically — the grid loops registered modules, so no hand-wiring is needed.

### Deviations from the verbatim `6cdda9b` markup (deliberate)

1. **Filter nav entries by `IsVisible`.** `NavEntry.IsVisible` is `Func<IServiceProvider, bool>?`. The `6cdda9b` markup rendered *all* entries unconditionally — which would wrongly show "Google TV / Android Phone / Android Tablet / Android Watch" pills when no such device is registered. Mirror the filter `HomeModuleTiles` already uses:
   `GetNavEntries().Where(e => e.IsVisible is null || e.IsVisible(ServiceProvider)).OrderBy(e => e.SortOrder)`.
   Requires `@inject IServiceProvider ServiceProvider`.
2. **Hide cards with no visible entries** *(approved decision)*. After filtering, if a module has zero visible nav entries, skip its card entirely. In practice this affects only **Cameras** (per-registered-camera entries; zero cameras → no card). Every other module has ≥1 static entry.
3. **`ModuleImageMap` += cameras.** Add `["cameras"] = "/images/cameras-logo.svg"` (asset exists, 119 KB). Android Power Tools / Utilities / Imaging have no brand SVG and fall back to their Bootstrap icons.
4. **Settings card.** Its five links are all still valid against the current `/settings/{Section}` route (`general · jellyfin · devices · cameras · dependencies`). Align order + labels to the current `SettingsPage` nav for consistency.
5. **Theme tokens.** Verify the restored `Home.razor.css` uses theme custom properties (`var(--…)`) so dark + light both read well; patch any hardcoded colors that regress light mode (per `feedback_ui_consistency`). The original CSS predates later light-mode token work.

### Files

| Action | Files |
|---|---|
| **Restore** from `6cdda9b` (then apply deviations) | `Components/Pages/Home.razor`, `Components/Pages/Home.razor.css` |
| **Delete** (home-only, orphaned) | `HomeScanBand`, `HomeDiscoveredAndroid`, `HomeDiscoveredCameras`, `HomeModuleTiles` — each `.razor` + `.razor.css`, under `Components/Pages/HomeSections/` |
| **Delete** (their tests) | `HomeScanBandTests.cs`, `HomeDiscoveredAndroidTests.cs`, `HomeDiscoveredCamerasTests.cs`, `HomeModuleTilesTests.cs` |
| **Rework** | `HomeIntegrationTests.cs` → assert the restored layout; relocate to `tests/ControlMenu.Tests/Components/Pages/HomeTests.cs` (the `HomeSections/` concept is gone) |
| **Keep, untouched** (shared) | `IDeviceQuickScanService`, `ICameraScanService`, `IScanLifecycleHandler` + all consumers: `WizardDevices`, `WizardCameras`, `Settings/DeviceManagement`, `Settings/CameraSettings`, shared `Components/Shared/Cameras/DiscoveredCamerasPanel`, `Program.cs` registrations |

After deletion the `Components/Pages/HomeSections/` source folder is empty and should be removed.

### Tests

- Delete the four component test files above.
- Rework `HomeIntegrationTests` (bUnit) to assert: hero present; a card per visible module; Settings card present; **Cameras card absent when no cameras, present when cameras exist**; pills point at the modules' nav `Href`s; and **no** scan band / discovered panels render.
- TDD order: write/adjust the home render tests first (red), then restore + reconcile `Home.razor` (green).
- Net: full-suite count drops from **457** (scanner tests removed). Expected — no capability lost.

### Docs

- Refresh the home-page section of `docs/TECHNICAL_GUIDE.md` (discovery dashboard → menu-sections).
- Add a "superseded by `2026-06-12-home-menu-sections-restore-design.md`" note atop `docs/superpowers/specs/2026-05-05-homepage-polish-design.md` and `docs/superpowers/plans/2026-05-05-homepage-polish.md` (keep as history).
- CHANGELOG `[Unreleased]`: note the home page reverted to menu-sections, scanner UI removed from home, scanning unchanged in Settings.

### TODO #22

Close as **superseded** *(approved decision)*. Its four items are all home-scanner polish that no longer applies once the scanners leave the home page: scan-button done-flash, `ScanAllAsync` test gap, Cameras count badge spec drift, and the `async void` Home / HomeDiscovered notifier handlers (deleted with the components). Archive #22 to `archive/todo_control_menu_shipped.md` with a "superseded by the home menu-sections restore (2026-06-12)" note.

## Verification

- `dotnet build` clean; full `dotnet test` green (with the reduced count).
- Manual (dev run → `http://localhost:5159`): home shows hero + module cards (Android Devices, Android Power Tools, Jellyfin, Utilities, Imaging Tools; Cameras **only** when cameras exist) + Settings card; every pill navigates; no scan band / discovered panels anywhere on home; dark + light both readable.
- Rides into the next release; can fold into the pending v1.2.0 VM smoke or a follow-up beta.

## Risks / edge cases

- **No modules registered:** the original "No modules loaded" empty-state still applies (unchanged).
- **Cameras card visibility** is now dynamic (appears/disappears with registered cameras) — covered by tests.
- **Light-mode regression** from the older CSS — mitigated by the theme-token check (Deviation 5).
- **`HomeSections` folder** becomes empty of components once the integration test is relocated — remove it.
