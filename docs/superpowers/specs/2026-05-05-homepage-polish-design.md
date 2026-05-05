# Homepage polish — Dashboard-first redesign

**Date:** 2026-05-05
**Status:** Approved (brainstorm complete; awaiting plan)
**Brainstorm artifacts:** `.superpowers/brainstorm/740-1778015012/content/` (current-home.html, structure-options.html, dashboard-v1.html, dashboard-final.html)
**Folds in:** TODO item 1 (Homepage polish) and sub-item 1a (Quick Scan buttons on Home)

## Problem

The current Home page is a static module-card grid with pill-link sub-navigation — a glorified nav menu. Two pieces of infrastructure shipped 2026-05-05 (camera ONVIF Quick Scan, Android mDNS Quick Scan) are buried in Settings pages even though they're the most direct entry point for new users finding devices on their network. Home should *flow with* the discovery experience, not sit beside it.

## Goal

Redesign Home as a discovery-first dashboard: scan-action band at top, live Discovered sections (Android + Cameras) middle, compact module nav at bottom. Reuse existing scan infrastructure and the inline-add component already shipped on Settings → Discovered panels — no new backend work, just a new container that hosts existing pieces in a new arrangement.

## Layout (top → bottom)

### 1. Header band — branding + status + scan buttons (single row)

- Left: small (36×36) app icon and `Control Menu` title.
- Center-left: status line summarizing current state — `N Android · M Cameras discovered · K registered · last scan Xs ago`. `N` and `M` count *all* hits in their respective Discovered lists (including hits that match an already-registered device); `K` counts the registered-devices DB total. Empty state (no scans run yet in this session): `Find devices and cameras on your network, then manage them.`
- Right: **three fixed-width scan buttons** in this order: `Scan Android` (blue), `Scan Cameras` (red), `Scan All` (grey). All three are the same width (~140px) and **never resize during state changes** — text changes inside the fixed footprint.

**Button states:**

| Button | Idle | Running | Done (~3s flash) |
|---|---|---|---|
| Scan Android | `⚡ Scan Android` | `⏳ Scanning Android…` | `✓ Scanned` → idle |
| Scan Cameras | `⚡ Scan Cameras` | `⏳ Scanning Cameras…` | `✓ Scanned` → idle |
| Scan All | `⚡ Scan All` | `⏳ Scanning All…` | `✓ Scanned` → idle |

**Composition rules:**

- Click an individual button → that button alone enters Running state. The other two stay Idle and remain clickable.
- Click Scan All → all three buttons enter Running state simultaneously (Scan All shows `Scanning All…`; the two specific scans show `Scanning Android…` / `Scanning Cameras…`).
- If one specific scan is already running and the user clicks the other specific scan → both kick off in tandem. Scan All button stays Idle (it didn't trigger this composition).
- If one specific scan is already running and the user clicks Scan All → Scan All immediately enters Running state (it represents *all three are or are about to be running*); the as-yet-unrun specific scan kicks off in tandem; the already-running scan continues.
- A button in Running state is non-clickable until its scan resolves.
- Done flash duration: ~3 seconds, then return to Idle.
- Errors: show toast notification per existing UI-consistency convention; button returns to Idle (no separate error state in the button itself).

### 2. Discovered sections — two stacked panels

Below the header band, render two separate sections in this order:

- **DISCOVERED — ANDROID** (green count badge)
- **DISCOVERED — CAMERAS** (red count badge)

Each section:
- Section label + count badge at top (uppercase tracking-wide label, count badge with the module's accent color).
- A list of rows, each showing icon · primary line (device/camera name) · secondary line (IP, OS / vendor info, dedupe hint) · trailing action.
- Trailing action is `+ Add` button (accent-colored) for unregistered hits, or a muted `✓ registered` label for hits matching an already-registered device.
- Hidden entirely when the corresponding list is empty AND no scan has been run for that type. Once a scan has been run for that type and produced zero results, render the section header with a small `No devices found` placeholder row so the user can confirm the scan executed.

**Data sources (no new backend work):**
- Android: existing `IScanLifecycleHandler.Discovered` (the same source `DeviceManagement.razor` consumes for its Discovered panel).
- Cameras: existing camera scan handler's discovered list (same source `CameraSettings.razor` consumes).
- Registered-match dedupe: same logic the Settings pages use today (per-device-type lookup against the registered devices DB by IP/MAC for Android, IP for cameras).

**Persistence on navigate-away:** Home reads the same singleton handler state that Settings pages read. Whatever the Settings pages currently retain across same-session nav, Home retains identically. (TODO item 20 — `Handler.Discovered` carry-over staleness — applies equally to Home and is tracked separately. Home does not introduce new persistence or new staleness.)

**Real-time updates:** Home subscribes to `IDeviceChangeNotifier.Changed` (Android) and the camera-side equivalent (`ICameraChangeNotifier` / camera scan-event channel) so background-tick scans + manual scans both refresh the on-screen list without a page reload — same pattern shipped 2026-05-05 in item 18.

### 3. Add behavior — embed the existing panels directly

Home embeds the shipped `DiscoveredPanel.razor` (Android) and `DiscoveredCamerasPanel.razor` (Cameras) **as-is**, fed from the same `IScanLifecycleHandler.Discovered` and camera scan-handler data the Settings pages consume. + Add behavior, inline-edit fields, ONVIF bulk-credentials panel, dismiss/× behavior — all unchanged from Settings. Home becomes a host for these components, not a divergent UI.

This means each Discovered section on Home is a wide table when populated (10 columns for Android, 8 for Cameras), not the slim row-summary look from the brainstorm mockup. The mockup's slim look is **aspirational polish, not in scope** for this iteration. If table-heaviness feels wrong on Home after smoke-testing, a follow-up task can add a `Compact` parameter to those panels — but that's a separate concern.

Why this approach: zero divergence cost, no new modal plumbing, no slim/wide variants to maintain. Spec rejects both the slim-Home-only-form alternative and the modal-wrapper alternative.

On successful Add: the Discovered row disappears (the existing panels handle this via dedupe-on-next-render); status line at top updates the registered count via the change-notifier subscription.

### 4. Module nav — compact 6-tile row at the bottom

Replaces the current pill-link card grid. Six equal tiles in a single row (responsive: drop to 3-per-row on narrow viewports, eventually 2-per-row on mobile):

- Android Devices · Power Tools · Jellyfin · Cameras · Utilities · Settings

Each tile: icon (bi or SVG image, matching the existing module icon dictionary) + module display name. **Click target = the module's first nav entry** (the lowest-`SortOrder` `NavEntry` from `IToolModule.GetNavEntries()`).

Resolved targets at time of writing:
- Android Devices → `/android/devices` (Device List, SortOrder 0)
- Power Tools → `/android-power-tools`
- Jellyfin → `/jellyfin/db-update` (DB Date Update, SortOrder 0)
- Cameras → `/cameras/{firstEnabledCameraId}` — **edge case:** if no cameras are enabled, `GetNavEntries()` yields nothing. **Fallback:** `/settings/cameras` so the user can register one. Spec'd explicitly because this is the only module whose nav entries are user-data-dependent.
- Utilities → `/utilities/icon-converter` (Icon Converter, SortOrder 0)
- Settings → `/settings/general` (Settings is not an `IToolModule` — hard-code the first Settings sub-page, which is General after the 2026-05-05 sub-nav reorder).

**Conditional nav-entry visibility:** `NavEntry` records have an optional visibility predicate (e.g., Android device-type entries only show when `HasDevicesOfType(...)` is true). The module-tile resolver MUST honor the same predicate — pick the lowest-SortOrder entry whose predicate is currently true. For Android, `Device List` has no predicate so it always wins; this is mostly a safety rule for future modules.

## Components to add

All new code lives in `src/ControlMenu/Components/Pages/Home.razor` plus a small set of focused child components for testability:

1. **`HomeScanBand.razor`** — the three-button row + status line. Owns the button-state machine (idle / running / done flash). Takes scan-trigger callbacks as parameters; emits no domain logic itself.
2. **`HomeDiscoveredAndroid.razor`** — Android section wrapper. Subscribes to `IDeviceChangeNotifier.Changed`, reads `IScanLifecycleHandler.Discovered` and the registered-devices DB, renders the section header (label + count badge or empty/post-scan placeholder), and embeds the existing `<DiscoveredPanel Discovered=... Registered=... OnAdd=... OnDismiss=... />` for the actual rows.
3. **`HomeDiscoveredCameras.razor`** — Cameras section wrapper. Subscribes to `ICameraChangeNotifier`, renders the section header, and embeds the existing `<DiscoveredCamerasPanel />` for the actual rows. (Camera panel manages its own data subscription internally — wrapper just hosts and adds the section chrome.)
4. **`HomeModuleTiles.razor`** — the 6-tile bottom row. Reads `ModuleDiscoveryService.Modules`, resolves each module's first nav entry per the rules above, renders tiles.

`Home.razor` becomes a thin composition: `<HomeScanBand ... /> <HomeDiscoveredAndroid /> <HomeDiscoveredCameras /> <HomeModuleTiles />` plus the existing setup-wizard guard (`_setupDone`) that wraps everything.

Per `feedback_blazor_scoped_css_cloning.md`, every new `.razor` ships with its own `.razor.css` — scoped styles attach via per-component `b-xxxxx` and don't follow a `.razor`-only clone.

Per `feedback_razor_quotes.md`, no string literals in `@on*` inline lambdas — extract to dedicated handler methods.

Per `feedback_blazor_jsinterop_marshaling.md`, if the button-state-machine timing needs JS interop (e.g., for the done-flash timeout), use the numeric-token handle pattern, not bare functions returned from `IJSObjectReference`.

## Backend touchpoints (existing — no new services)

- `IAdbService.ScanMdnsAsync` — Android Quick Scan, ~2-3s.
- `ICameraScanService.StartOnvifOnlyScanAsync` — Cameras Quick Scan, ~3-5s.
- `IScanLifecycleHandler` (Android) and the camera scan handler — provide `Discovered` lists.
- `IDeviceChangeNotifier.Changed` (Android) and camera notifier — provide real-time refresh.
- `ModuleDiscoveryService.Modules` — provides the module list and `GetNavEntries()` per module.

The "Scan All" button is a UI-level composition: it invokes `ScanMdnsAsync` and `StartOnvifOnlyScanAsync` in parallel and tracks both completion signals to drive the button states. No new "scan-all service" abstraction.

## Setup-wizard guard

The existing `_setupDone` check in `Home.razor` stays. If setup hasn't completed, render `<SetupWizard />` exactly as today and skip the dashboard entirely. The dashboard is for post-setup users.

## Testing

Per the `superpowers:test-driven-development` discipline, bUnit tests precede implementation for each new component (the project's existing 354-test bUnit suite is the precedent).

- **`HomeScanBand`**: idle → running → done state per button; Scan All composes correctly with already-running specific scans; buttons remain fixed-width through state transitions; running buttons are non-clickable.
- **`HomeDiscoveredAndroid` / `HomeDiscoveredCameras`**: empty list (no scan run) hides the section entirely; empty list (post-scan) shows section header + `No devices found` placeholder; populated list delegates row rendering to the embedded existing panel; subscription to change notifier triggers wrapper re-render. Tests assert the wrapper's render-decision logic, not the embedded panel's internals (those are tested separately).
- **`HomeModuleTiles`**: each tile resolves to the lowest-SortOrder visible nav entry; Cameras-with-zero-cameras falls back to `/settings/cameras`; conditional-visibility predicates are honored.
- **`Home.razor`** integration: setup-not-done shows `<SetupWizard />`; setup-done composes the four sections in order.

Target: unit tests for each component + one integration test for Home composition. No E2E required for this change (Playwright E2E is item 15, separately tracked).

## Out of scope

- Setup wizard changes — spec leaves the existing guard untouched.
- Sidebar changes — sidebar fly-out shipped 2026-05-05 (item 3); not touching it.
- New scan types or new modules — Home renders what's already shipped.
- Theming/dark-mode adjustments — Home inherits the existing theme system unchanged.
- TODO item 20 (`Handler.Discovered` staleness) — spec'd separately. Home inherits the bug; doesn't introduce it; doesn't fix it. If item 20 is implemented later, Home benefits automatically.
- TODO item 12 (ws-scrcpy-web Local-Dependencies-Only conversion) — separate item.

## Migration notes

- The existing module-card grid + pill links in `Home.razor` are removed in their entirety. The `ModuleImageMap` dictionary moves into `HomeModuleTiles.razor` (or a shared module-icon helper) since that's the only consumer.
- The hero block (large 96px icon + `Control Menu` title + tagline) is replaced by the slim header band — the 36×36 icon + title + status line. The tagline becomes the empty-state status line.
- `archive/project_control_menu_homepage.md` should gain a brief note pointing at this redesign as the successor to the 2026-04-14 hero+module-cards layout.

## Risks

1. **Discovered-section render perf** if a large network produces dozens of hits. Mitigation: existing handlers already cap or sort their `Discovered` collections; no new perf surface introduced. Validate in manual smoke.
2. **Visual hierarchy after stacking three full-width sections** (header band → Android Discovered → Cameras Discovered → module tiles). Mitigation: section labels with uppercase tracking + alternating background tone (the mockup uses `#1a1a1a` for the Discovered surfaces against `#1e1e1e` body). Validate in manual smoke against both light and dark themes.
3. **Setup-wizard regression** if `Home.razor` rewrite accidentally drops the `_setupDone` guard. Mitigation: integration test asserts the guard renders SetupWizard.

## Acceptance criteria

- Home renders the four-section layout (header band / Android Discovered / Cameras Discovered / module tiles) in that order.
- Three scan buttons stay fixed-width through every state transition.
- Scan All composition rules behave per the table above.
- + Add opens the existing inline-add component in a modal; success removes the row and updates the status line.
- Module tiles route to the lowest-SortOrder visible nav entry per module; Cameras-with-zero-cameras lands on `/settings/cameras`.
- All new components have scoped `.razor.css` files.
- bUnit tests pass; existing 354 tests stay green.
- Manual smoke covers: cold state, single scan, parallel scans via Scan All, parallel scans via two specific buttons, + Add round-trip, navigate-away-and-return persistence (matches Settings behavior), setup-wizard guard.
