# Wizard Camera Scanner — Design Spec

**Date:** 2026-05-05
**Branch:** `feature/wizard-camera-scanner`
**Related TODO:** `todo_control_menu.md` resume-banner pending wizard work
**Builds on:** `feature/camera-network-scanner` (merged to master `3ea78d7`, 2026-05-05) — ONVIF + TCP-554 scanner, DB-backed Camera CRUD, `DiscoveredCamerasPanel` shared component.

## Goal

First-run users land on the Setup Wizard's **Cameras** step (currently a stub) and can discover + register their IP cameras with the same low-friction feel they just experienced on the **Android Devices** step. ONVIF-conformant cameras (Hikvision, LTS, Dahua, Axis, Reolink, every OEM rebrand of Hikvision firmware) get auto-discovered and one-click added; non-ONVIF / RTSP-only cameras are deferred to Settings → Cameras after wizard completion via a clearly worded help note.

## Non-Goals

- TCP-554 RTSP sweep in the wizard scan path. The wizard is the "quick" / protocol-native discovery path — analogous to WizardDevices using mDNS-only via `adb mdns services`. Full RTSP sweep stays on the post-wizard Settings → Cameras `Scan Network` button.
- Manual `Add Camera Manually` button in the wizard. Mirrors WizardDevices, which has no `Add Device` button — all adds in the wizard go through the Discovered panel.
- `Delete All` button in the wizard (destructive op, no place in first-run setup).
- Edit/Delete actions on the registered-cameras table during the wizard. Read-only summary; full CRUD lives at Settings → Cameras.
- Subnet override UI in the wizard. Auto-detect or fail (with help text). Multi-subnet / segmented-LAN users can finish setup post-wizard.
- New backend functionality for the wizard step itself — everything except the ONVIF-only scan entry point already exists in the production Cameras settings page.

## Background

WizardDevices precedent (already shipped): single `Scan Network` button → calls `QuickRefresh` (mDNS-only via `adb mdns services`) → results stream into `DiscoveredPanel` → user clicks Add per row → registered table renders below when ≥1 device. No `Add Device` button. No subnet input (mDNS is link-local multicast, no subnet to choose).

`SubnetDetectionClient` (CM-side) is a thin HTTP client to ws-scrcpy-web's `GET /api/devices/scan/subnet` endpoint. The Node sidecar runs `SubnetDetector.detectSubnet()` which inspects the host's network adapters and returns the one with a default gateway in the same subnet as the adapter (the "real LAN" adapter, filtering out Hyper-V virtual switches, loopback, etc.). ws-scrcpy-web is already a wizard prerequisite because WizardDevices depends on `adb mdns` from the same process tree, so this is a consistent dependency.

`CameraScanService.StartScanAsync(subnets, ct)` currently runs ONVIF WS-Discovery + TCP-554 sweep in parallel against the supplied subnets and streams hits into a shared event bus that `DiscoveredCamerasPanel` already subscribes to. The wizard needs an ONVIF-only entry point.

## Architecture

### File touches

- **`src/ControlMenu/Components/Pages/Setup/WizardCameras.razor`** — replace the 12-line stub with full implementation modeled on `WizardDevices.razor`.
- **`src/ControlMenu/Components/Pages/Setup/WizardDevices.razor`** — update intro paragraph to explicitly call out the mDNS-only discovery limitation and direct older devices to Settings → Android Devices post-wizard.
- **`src/ControlMenu/Components/Pages/SetupWizard.razor`** — add `int CamerasAdded { get; set; }` to `WizardState` for the Done-step summary.
- **`src/ControlMenu/Components/Pages/Setup/WizardDone.razor`** — surface `State.CamerasAdded` alongside the device count.
- **`src/ControlMenu/Modules/Cameras/Network/ICameraScanService.cs`** — add `Task StartOnvifOnlyScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default)`.
- **`src/ControlMenu/Modules/Cameras/Network/CameraScanService.cs`** — implement the new method by reusing the existing scan plumbing but skipping the TCP-554 sweep branch.

### Reused components (no changes)

- `Components/Shared/Cameras/DiscoveredCamerasPanel.razor(.css)` — shared-creds entry above the grid, inline ONVIF rows with creds + per-row Add button.
- `Components/Shared/Scanner/ScanProgressChip.razor` — phase-aware progress indicator.
- `Modules/Cameras/Services/CameraService.cs` — `AddAsync` (seeds LastSeen on insert per the post-merge polish).
- `Services/Network/SubnetDetectionClient.cs` — auto-detect the LAN subnet via ws-scrcpy-web's endpoint.
- `Services/IntervalChangeSignal.cs` (no direct interaction; new cameras flow through `ICameraChangeNotifier` which is what the registered table subscribes to).

## Component layout

```
┌─ Wizard chrome (SetupWizard.razor) ───────────────────────────┐
│  Stepper: Welcome · Devices · [Cameras] · Jellyfin · Email   │
│                                                                │
│  ┌─ WizardCameras.razor ──────────────────────────────────┐   │
│  │  H2: "Cameras"                                          │   │
│  │  Intro: "Scan your network to discover ONVIF-enabled    │   │
│  │  IP cameras and add each one with a click. Non-ONVIF    │   │
│  │  cameras (older or basic RTSP-only models) can be       │   │
│  │  added later from Settings → Cameras."                  │   │
│  │                                                         │   │
│  │  Toolbar:  [ Scan Network ]  (disabled while scanning)  │   │
│  │                                                         │   │
│  │  ScanProgressChip            (only when Phase != Idle)  │   │
│  │                                                         │   │
│  │  DiscoveredCamerasPanel      (reused, unchanged)        │   │
│  │   ├─ shared-creds entry above grid                      │   │
│  │   └─ inline ONVIF rows: creds + Add per row             │   │
│  │                                                         │   │
│  │  IF _cameras.Count > 0:                                 │   │
│  │    H3: "Registered cameras"                             │   │
│  │    table: Cam # · Name · Mfr / Model · Address          │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                │
│  Wizard nav:  [Back]  [Skip]  [Next →]                         │
└────────────────────────────────────────────────────────────────┘
```

## Data flow — Scan Network click

1. User clicks **Scan Network** on the wizard step.
2. `WizardCameras` calls `SubnetDetectionClient.DetectAsync()`. Result is one `DetectedSubnet?` (or null).
3. If null → inline alert (same `_message` pattern as WizardDevices): *"Could not auto-detect a network subnet. You can add cameras manually from Settings → Cameras after setup completes."* Button stays enabled. No state change. No fallback to manual subnet input in the wizard — keeps the path single.
4. If non-null → parse to `ParsedSubnet` via `SubnetParser.Parse(detected.Cidr)` (existing helper).
5. Call new `ICameraScanService.StartOnvifOnlyScanAsync([parsedSubnet])`. Service flips `Phase = Scanning`, fires multicast WS-Discovery on UDP port 3702, and streams discovered ONVIF hits through the existing event bus.
6. `DiscoveredCamerasPanel` is subscribed to the same event bus on the Settings page; it receives ONVIF rows and renders them (with credential inputs per row + the optional shared-creds entry above the grid).
7. The button is disabled while `Phase != Idle`. The `ScanProgressChip` renders during this time.
8. User enters credentials and clicks **Add** on a row. The Discovered panel's existing `OnCameraAdded` handler invokes `CameraService.AddAsync(...)` (which now seeds `LastSeen = UtcNow`), `ICameraChangeNotifier` fires, the wizard's local `_cameras` list refreshes, and `State.CamerasAdded` updates.
9. When the scan completes, `Phase` returns to `Idle`, the chip disappears, the button re-enables. User can scan again or move on.

## State / lifecycle

`WizardCameras` mirrors `WizardDevices` lifecycle:

- `OnInitializedAsync`: load registered cameras into `_cameras`, subscribe to `ICameraChangeNotifier.CamerasChanged`, subscribe to `ICameraScanService` state changes for chip-rendering and button-disable.
- `RefreshCamerasAsync`: reload from `CameraService.GetAllAsync()`, sort by `CameraNumber ?? int.MaxValue` then `Name`, update `State.CamerasAdded = _cameras.Count`, `StateHasChanged`.
- `Dispose`: unsubscribe both events.
- `SaveAsync()`: no-op. Adds persist immediately on click; the wizard's existing pre-Next-save hook in `SetupWizard.razor` for cameras becomes a no-op call. Match the current stub's `Task.CompletedTask` behavior.

`SetupWizard.WizardState` gains:

```csharp
public int CamerasAdded { get; set; }
```

`WizardDone.razor` reads `State.CamerasAdded` and includes a one-line summary, e.g., *"3 cameras registered."* Matches the existing `DevicesAdded` line shape.

## Error handling

| Failure mode | Surface | Behavior |
|---|---|---|
| `SubnetDetectionClient` returns null | Inline alert message (the same `_message` / `_messageIsError` pattern WizardDevices uses, NOT a separate toast component) in wizard | Help text directs user to Settings → Cameras post-setup. Button stays enabled. No retry loop. |
| ws-scrcpy-web not running (subnet endpoint unreachable) | Same as above (DetectAsync returns null) | Same. WizardDevices already requires ws-scrcpy-web for `adb mdns`; if it's down, that step would have already failed. Wizard does not block on it. |
| ONVIF scan exception | Existing `ScanService` per-subnet error path | Wizard surfaces nothing extra. The scan service logs and the `Phase` returns to `Idle`. |
| Add fails (auth wrong, network blip) | Per-row error in `DiscoveredCamerasPanel` (existing) | No wizard-level handling. |
| User clicks Next mid-scan | Wizard advances; scan continues in background | On backtrack, `_cameras` list reflects whatever was added during the scan. Phase may still be `Scanning` or back to `Idle`. |
| User backs out of wizard mid-scan | Component disposes, event subscriptions unwind | The scan service is a singleton — it continues until natural completion or `CancelAsync`. Adds that completed before disposal are persisted. No leaks. |

## Help-text revisions

### WizardCameras intro

> Scan your network to discover ONVIF-enabled IP cameras and add each one with a click. **Non-ONVIF cameras (older or basic RTSP-only models) can be added later from [Settings → Cameras](/settings/cameras).**

### WizardDevices intro (existing line 17 update)

> Scan your network to discover Android devices, then add each one with a click. **Only modern Android devices that advertise over mDNS will appear in this scan; older devices can be added later from [Settings → Android Devices](/settings/devices).**

(Wording subject to copy-edit during implementation; the substantive change is calling out the protocol-scope limitation explicitly.)

## ONVIF-only scan service contract

```csharp
// ICameraScanService
Task StartOnvifOnlyScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default);
```

**Behavior:** identical to `StartScanAsync` except the parallel TCP-554 sweep branch is skipped. Same `Phase` transitions, same event bus, same `Hits` accumulation. `DiscoveredCamerasPanel` does not need to know which entry point was called — hits are hits.

**Implementation strategy:** factor the scan body into a private `RunScanAsync(subnets, includeRtspSweep, ct)` helper; both public methods delegate. Avoids code duplication, keeps the existing `StartScanAsync` semantics unchanged.

## Testing

### Unit

- `WizardCameras` Razor component test (`tests/ControlMenu.Tests/Components/Setup/WizardCamerasTests.cs`):
  - Renders empty state when 0 registered cameras (no "Registered cameras" header, no table).
  - Renders registered table when ≥1 camera.
  - Click Scan Network with valid subnet detection → `StartOnvifOnlyScanAsync` called with detected subnet.
  - Click Scan Network with null detection → toast surfaced, no scan call.
  - Button disabled while `Phase = Scanning`, `ScanProgressChip` rendered.
  - `State.CamerasAdded` matches `_cameras.Count` after refresh.

- `CameraScanService.StartOnvifOnlyScanAsync` test (`tests/ControlMenu.Tests/Modules/Cameras/Network/CameraScanServiceTests.cs` — extend existing file):
  - WS-Discovery client invoked.
  - RTSP probe client `ProbeTcpAsync` NOT invoked.
  - Phase transitions match `StartScanAsync`.

- `WizardDone.razor` summary test — gains a "cameras registered" line when `State.CamerasAdded > 0`.

### Manual smoke

- Reset the user's CM database state (`Cameras` table empty), run the wizard end-to-end against the 8-camera Hikvision/LTS fleet at home (subnet 192.168.86.0/24).
- Verify Scan Network click → progress chip appears → cameras populate Discovered panel within ~5s → enter shared admin password once via bulk-creds → click Add per row → registered table grows.
- Verify Skip from Cameras step → wizard advances cleanly with `CamerasAdded = 0`.
- Verify Back from Cameras → Devices → Cameras → registered table restored from DB.

### Pre-merge integration smoke

- Manually click through every wizard step end-to-end on a fresh DB.
- Re-confirm Settings → Cameras still works (per the post-merge smoke checklist) — unchanged but verify the shared component reuse didn't regress.

## Out of scope (post-wizard backlog)

These will be logged as new TODOs in `todo_control_menu.md` after this spec is approved:

1. **Settings → Cameras "Quick Scan" button** (ONVIF-only, mirrors Android Settings page's `Quick Refresh`). Sits next to the existing `Scan Network` (which stays full = ONVIF + TCP-554 sweep). Reuses the same `StartOnvifOnlyScanAsync` entry point this spec adds. Parallels the Android pattern: Quick = lightweight protocol-native, Scan Network = full sweep.
2. **Vendor-adapter pattern for non-Hikvision ONVIF cameras** (Dahua / Reolink / Axis) — pre-existing follow-up from the camera-scanner branch shipment, unaffected by wizard work.

## Approval

Pending user review of this written spec.
