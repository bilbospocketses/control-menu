# Scanner UX redesign — design spec

**Date:** 2026-04-21
**Author:** brainstormed interactively after T21 manual QA
**Predecessor spec:** `2026-04-21-scanner-port-design.md` (the initial port)
**Scope:** redesign the Full Scan UX to mirror ws-scrcpy-web's native Network Discovery panel — modal shrinks to a subnet picker, results live on the page.

---

## 1. Motivation

The initial scanner port (T14–T21) landed the full ws-scan integration inside a single composite `ScanNetworkModal`: subnet picker, chip, live hit table, Cancel button — all within the modal. QA feedback on T21 flagged the UX: keeping hits trapped inside a modal forces the user to hold the modal open to watch the scan, and closing the modal loses visibility entirely. ws-scrcpy-web's own `NetworkDiscoveryPanel` splits the workflow cleanly — modal manages subnets and triggers the scan, then closes; page holds the chip, live hits, and per-hit actions. This spec lifts that split into Control Menu.

A second motivation carries over from the T21 diagnostic pass: the Name column and the adb-merge (both already shipped in commits `7773ba1` and `cf6068e` respectively) remain in place. This spec does not re-describe them — it only notes where they integrate into the new layout.

---

## 2. Decisions locked in during brainstorming

| # | Topic | Decision | Rationale |
|---|---|---|---|
| 1 | Quick Refresh vs Full Scan contract | **Preserve.** Quick Refresh stays silent merger; Full Scan replaces Discovered + chip + live hits + adb-merge | Two-buttons-two-intents model from T17; cheap-and-quiet Quick Refresh is a distinct UX contract worth keeping |
| 2 | Hit streaming | **Live + late merge.** Each `scan.hit` appends to Discovered as it arrives; adb-merge runs once on `scan.complete` | Matches ws-scrcpy-web's live experience; single `adb devices` call keeps integration simple |
| 3 | Chip placement | **Own row between devices table and Discovered section.** Full-width row, shown only while phase ≠ Idle | Hard to miss; explains the live rows appearing below; keeps toolbar stable |
| 4 | Modal behavior during active scan | **Opens with full editing.** Subnet list mutable (edits apply to next scan); Start disabled with "Scan in Progress" label | Running scan already captured its subnets at StartScan time; no harm in letting user queue changes |
| 5 | Per-row dismiss | **Each Discovered row gets `×` button** beside Add. Dismiss removes the row and records the address in an in-memory set; subsequent `scan.hit` / adb-merge for dismissed addresses skip | Prevents stale rows cluttering the panel between scans; avoids the auto-hide + "user didn't have time to click Add" trap |
| 6 | Subnet list state | **Stays modal-local** (loaded from `scan-subnets` setting on each open, saved on Add/Remove) | Subnets are only rendered inside the modal — no reason to lift the state to the page |
| 7 | Dismissed-addresses reset | **Cleared on next Full Scan start.** Quick Refresh does not clear | New scan = new intent = fresh slate; Quick Refresh is a merge, should respect existing dismissals |

---

## 3. Component responsibilities

### `ScanNetworkModal.razor` — shrinks

**Removes:**
- `ScanProgressChip` instance inside modal header
- Hit table (`<tbody>` with rows)
- Cancel button (moves to page)
- Subscription to `INetworkScanService` (`_subscription` field, `OnScanEvent` handler, snapshot replay on init)
- `_hits` / `_lastProgress` / `_phase` local state
- `OnAddHit` and `OnScanComplete` event callbacks
- `ScanCompleteEvent` handling and `HitDedupe.Collapse` call site

**Retains:**
- `Detected:` gateway suggestion (calls `SubnetDetectionClient.DetectAsync`)
- Subnet list management (`_subnets`, Add via nested `AddSubnetModal`, Remove, `SaveSubnetsAsync` to settings)
- `LargeSubnetWarningModal` for >2048 hosts
- Subnet syntax help link
- Close button
- Start button — now triggers a new `[Parameter] public EventCallback<IReadOnlyList<ParsedSubnet>> OnStart { get; set; }` and closes the modal

**Active-scan handling:**
- Modal accepts a new `[Parameter] public bool ScanInProgress { get; set; }` from the page
- When `ScanInProgress == true`: Start button disabled, label becomes "Scan in Progress — cancel on the page"
- Subnet list remains fully editable

### `DeviceManagement.razor` — grows

**Adds:**
- `@inject INetworkScanService ScanService` + subscription in `OnInitializedAsync`
- `_phase`, `_lastProgress`, `_subscription`, `_dismissedAddresses: HashSet<string>` fields
- `OnScanEvent(ScanEvent)` handler (dispatches on ScanStarted / Progress / Hit / Draining / Complete / Cancelled / Error)
- `OnFullScanStart(IReadOnlyList<ParsedSubnet>)` — clears `_discovered`, clears `_dismissedAddresses`, calls `ScanService.StartScanAsync(subnets)`
- Chip row markup (conditional on `_phase != ScanPhase.Idle`) between devices table and Discovered section, with inline Cancel button calling `ScanService.CancelAsync()`
- Dismiss button (`×`) on each Discovered row; handler removes row and records address
- Live append on `ScanHitEvent` (respects `_dismissedAddresses`)
- adb-merge handler stays on `ScanCompleteEvent` (existing logic preserved; respects `_dismissedAddresses` for merged rows too)

**Removes:**
- `_showScanModal` bool — replaced by explicit open/close methods tied to the reshaped modal
- `AddFromScan` handler (no longer called — modal doesn't emit per-hit add events; Add happens on the page)
- `OnFullScanComplete` as a modal-callback — logic moves inline into `OnScanEvent`'s ScanCompleteEvent case

### `ScanProgressChip.razor` — unchanged

Rendered on the page instead of inside the modal. Component is portable. Only change: palette tokens.

### `NetworkScanService.cs` — unchanged

Its subscribe/dispatch fan-out already supports the subscriber moving from modal to page. Existing 203 tests continue to pass.

### `AddSubnetModal` / `LargeSubnetWarningModal` / `HitDedupe` / `SubnetParser` / `SubnetDetectionClient` — unchanged

### `ScanMergeHelper.cs` — extended

- Add `FilterDismissed(IEnumerable<ScanHit> hits, ISet<string> dismissedAddresses)` — pure function returning hits whose address is not in the dismissed set
- Unit tests added alongside existing `FindUnregisteredAdbConnected` tests

---

## 4. Data flow

```
User clicks [📡 Scan Network…]
  └─ ScanNetworkModal opens
       └─ loads scan-subnets from Settings
       └─ calls SubnetDetectionClient for the Detected suggestion

User edits subnets as desired, clicks [Start]
  └─ Modal invokes OnStart(subnets) and closes itself
       └─ Page's OnFullScanStart:
            • clears _discovered
            • clears _dismissedAddresses
            • calls INetworkScanService.StartScanAsync(subnets)

NetworkScanService opens WS to ws-scrcpy-web's /ws-scan
  └─ sends {"type":"scan.start", subnets:[...]}
  └─ receive loop dispatches events to all subscribers (the page is one)

Page's OnScanEvent handler:
  • ScanStartedEvent   → _phase=Scanning, chip appears, counter initialized
  • ScanProgressEvent  → update chip counter
  • ScanHitEvent       → if address in _dismissedAddresses, skip
                         else append as DiscoveredDevice to _discovered
                         StateHasChanged
  • ScanDrainingEvent  → _phase=Draining (chip shows "draining...")
  • ScanErrorEvent     → toast, _phase=Idle
  • ScanCompleteEvent  → run adb-merge
                         (filter dismissed addresses out of the merge too)
                         append adb rows to _discovered
                         _phase=Complete (chip auto-hides after 5s)
  • ScanCancelledEvent → _phase=Cancelled (chip auto-hides after 10s)
                         no adb-merge on cancel — partial state

User clicks [×] on a Discovered row
  └─ Page handler:
       • removes the row from _discovered
       • adds row.Address to _dismissedAddresses

User clicks [Cancel] next to chip
  └─ calls INetworkScanService.CancelAsync()
       └─ service sends {"type":"scan.cancel"} on WS
       └─ server emits scan.draining then scan.cancelled
```

**Second-tab spectator** (unchanged behavior): second tab's `DeviceManagement` subscribes in `OnInitialized`; `NetworkScanService` replays the last `ScanStarted` + `ScanProgress` + all buffered hits. Both tabs see the same chip state and the same live rows. Cancel from either tab cancels the shared scan. Dismissed-address state is per-tab (not shared) — a user-experience nuance, not a correctness issue.

---

## 5. Markup sketches

### Modal (shrunk)

```
┌─ Scan network ────────────────────────── × ┐
│                                            │
│ Subnets to scan                            │
│ ┌────────────────────────────────────────┐ │
│ │ Detected: 192.168.86.0/24 (254 hosts)  │ │
│ │           [use this]                   │ │
│ │                                        │ │
│ │ ● 192.168.1.0/24  254 hosts [Remove]   │ │
│ │ [+ Add subnet]                         │ │
│ └────────────────────────────────────────┘ │
│                                            │
│ Subnet syntax help ↗                       │
│                                            │
│                      [Close]  [▶ Start]    │
│                     (disabled if active;   │
│                     label: "Scan in        │
│                      Progress")            │
└────────────────────────────────────────────┘
```

### DeviceManagement page during a Full Scan

```
╭─ Device Management ───────────────────────────────╮
│ [+ Add Device] [⟳ Quick Refresh] [📡 Scan Network…]│
│                                                   │
│ Registered devices table ...                      │
╰───────────────────────────────────────────────────╯

  ● scanning — 42 / 254 — 3 found          [Cancel]   ← chip row (only while phase != Idle)

╭─ Discovered on Network (live) ────────────────────╮
│ Service         Name    IP        Port  MAC       │
│ adb-XYZ [mdns]  Jamie's 192..43   5555  aa.. [Add][×]
│ adb-ABC [mdns]  Unknown 192..50   5555  —    [Add][×]
│ 192..5555 [adb] Pixel9  192..60   5555  bb.. [Add][×]
│ (rows stream in live; dismissed rows removed)     │
╰───────────────────────────────────────────────────╯
```

---

## 6. CSS / palette

`ScanProgressChip.razor.css` currently uses hardcoded hex values (`#2563eb` scanning, `#d97706` draining, `#16a34a` complete, `#dc2626` cancelled). When the chip moves from modal surface to page surface, both dark and light themes must render legibly.

Approach: make the chip theme-aware using existing Control Menu CSS tokens.

- Text color uses the solid token directly:
  - `.scan-chip.scanning`  → `color: var(--info-color);`
  - `.scan-chip.draining`  → `color: var(--warning-color);`
  - `.scan-chip.complete`  → `color: var(--success-color);`
  - `.scan-chip.cancelled` → `color: var(--danger-color);`
- Background with soft transparency: use `color-mix(in srgb, var(--info-color) 15%, transparent)` for each variant. Fallback (older Edge / Safari) — add `background-color: rgba(...)` with a hardcoded neutral-gray first, `color-mix` overrides on supporting browsers.

Implementation-time call: if `color-mix` coverage is adequate for the target browsers Control Menu runs in, use it. Otherwise introduce named soft tokens in `app.css` (`--info-color-soft`, etc.) pre-defined for both themes and reference those.

QA verifies both themes before sign-off.

---

## 7. Testing strategy

### Unit tests (new)

- `ScanMergeHelper.FilterDismissed` — 3 tests:
  - Filters out hits whose address matches dismissed set
  - Case-insensitive matching
  - Empty dismissed set returns all hits

### Existing tests

All 209 current tests continue to pass with zero changes required. The split between page-subscriber and modal-subscriber is transparent to `NetworkScanService`.

### Blazor component logic

`DeviceManagement.razor`'s new responsibilities (OnScanEvent dispatch, dismiss button, adb-merge on complete) are bundled with UI — no bUnit in the project. The pure-function bits are extracted into `ScanMergeHelper` and tested there. Behavioral verification happens through manual QA.

### Manual QA additions to the T21 checklist

- **16.** Start in modal → modal closes → chip + live hits appear on page.
- **17.** Dismiss row → row removed; if same address re-emitted later in scan (shouldn't normally happen, but dedupe on the receive side isn't in our control), stays dismissed.
- **18.** Open modal during scan → Start disabled with "Scan in Progress" label; subnet edits allowed; edits apply to the next scan, not the running one.
- **19.** Cancel via page chip → chip transitions Scanning → Draining → Cancelled; no adb-merge runs; partial hits stay in Discovered.
- **20.** Second-tab spectator → joins mid-scan, sees chip + live hits; Cancel from either tab cancels shared scan.
- **21.** Chip legibility in dark AND light theme.

---

## 8. Scope boundaries — what this redesign does NOT do

- **No change to Quick Refresh flow.** Silent mDNS+ARP, merges new hits into Discovered, no chip.
- **No change to `/ws-scan` protocol** or `NetworkScanService` internals. Subscribe/dispatch is unchanged.
- **No change to `AddFromDiscovery`.** Add button behavior preserved.
- **No change to External/Managed deploy modes.**
- **No change to the Name column logic** (shipped 2026-04-21 in `7773ba1`). Preserved as-is.
- **No change to adb-merge logic** (shipped 2026-04-21 in `cf6068e`). Trigger point unchanged — still on `scan.complete`.
- **No change to CSS tokens outside the chip.** The `.source-badge` shipped with the adb-merge stays.

CHANGELOG entry for this redesign goes under `### Changed`, not `### Added`.

---

## 9. Dependencies

None outside the current worktree. All prerequisites shipped in prior commits on `feat/scanner-port`.

---

## 10. Open questions

None. All design decisions resolved in brainstorming.
