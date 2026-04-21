# Scanner extraction — design spec

**Date:** 2026-04-21
**Author:** brainstormed interactively after UX redesign final review
**Predecessor specs:** `2026-04-21-scanner-port-design.md` (initial port), `2026-04-21-scanner-ux-redesign.md` (modal shrink + page chip + dismiss)
**Scope:** carve the scanner state + finalize orchestration out of `DeviceManagement.razor` into a plain-service `ScanLifecycleHandler` and carve the Discovered panel into its own Razor component.

---

## 1. Motivation

Final code review of the UX redesign landed three of four backlog items on master at `d07ff7f`. The deferred fourth was this extraction: `DeviceManagement.razor` carries ~120 lines of scan event dispatch + completion orchestration (`OnScanEvent`, `FinalizeScanAsync`, and five supporting helpers) plus ~40 lines of Discovered panel markup and `NameFor` resolution. Together they push the page past 700 lines and make the scan pipeline hard to unit test (the only way in today is through bUnit, which was not wired up for this page).

The extraction targets two goals:

1. **Unit-testable scan pipeline.** Move the orchestration into a plain service with injected dependencies. Tests exercise it through xUnit + Moq, no Blazor host required.
2. **Smaller page, clearer responsibilities.** Page becomes the presentation shell: device CRUD, Quick Refresh, modal wiring, toast messages. Scanner internals hide behind a handler interface; Discovered UI hides behind a panel component.

A secondary goal — closing reviewer item **I-1**, the latent `_discovered`-across-awaits race in `FinalizeScanAsync` — folds in naturally because the handler owns `_discovered` and can re-filter dismissed addresses at apply time.

---

## 2. Decisions locked in during brainstorming

| # | Topic | Decision | Rationale |
|---|---|---|---|
| 1 | State ownership | **A1: handler owns all Discovered-panel state.** `Discovered`, `DismissedAddresses`, `StashedNamesByMac`, `Phase`, `LastProgress` live on the handler. Quick Refresh calls `ReplaceDiscovered(newList)` rather than mutating page state. | Single-writer story; no two-writer races between Quick Refresh and scan events. Quick Refresh already *replaces* the list wholesale, so the change is cosmetic for that flow. |
| 2 | I-1 race fix | **R2: re-filter at apply time.** `AppendAdbMergeRows` consults the current `_dismissedAddresses` before appending each row. `EnrichDiscoveredMacs` is already safe (iterates the live list; dismissed rows are already gone). | Three-line fix; keeps existing code shape. R1 (snapshot + diff) would introduce new semantics to solve one narrow window. R3 (locks) is wrong for Blazor's SynchronizationContext model. |
| 3 | Registered-devices dependency | **D1: handler injects `IDeviceService`.** Calls `GetAllDevicesAsync()` inside `FinalizeScanAsync`. | Handler stays self-contained for tests (inject a fake). One duplicate DB query per scan completion is trivial compared to the multi-second ARP+ping work it follows. |
| 4 | Handler lifetime | **Scoped (per Blazor circuit).** Forced by decision 1 — singleton would leak scan state across users. | Each circuit gets its own handler, subscribes independently to the singleton `INetworkScanService`, sees the same broadcast scan events. Matches the spectator-tab UX from the UX-redesign spec. |
| 5 | Public surface granularity | **M1: full surface.** Handler exposes `StartFullScanAsync`, `CancelScanAsync`, `Dismiss`, `ReplaceDiscovered` + read-only state properties + `OnStateChanged` event. Page stops injecting `INetworkScanService` entirely. | Page's only scanner dep is the handler; `INetworkScanService` becomes an internal dep. Cleaner for the page, no semantic cost. |
| 6 | Event marshaling | **Handler is Blazor-agnostic; page marshals.** Handler calls `OnStateChanged?.Invoke()` directly. Page subscribes via `handler.OnStateChanged += () => InvokeAsync(StateHasChanged)`. | Handler testable in plain xUnit (no SynchronizationContext required). One `InvokeAsync` per event at the page boundary, same UX. |
| 7 | Error surfacing | **Handler exposes `ConsumeLastError()` method** (one-shot semantic — returns the pending error string and clears it). Page calls it inside its `OnStateChanged` listener and routes non-null results to `ShowMessage`. | Handler has no UI concerns. One-shot semantic avoids showing the same toast twice across multiple state changes and avoids losing errors on re-render. |
| 8 | `DiscoveredDevice` location | **`src/ControlMenu/Services/Network/DiscoveredDevice.cs`.** Promoted from its nested spot in `DeviceManagement.razor` (line 189) to a public record. | Used by both handler (Services) and panel (UI); belongs with the other scanner types (`ScanHit`, `ScanEvent`, `ParsedSubnet`). |

---

## 3. Component responsibilities

### New: `IScanLifecycleHandler` / `ScanLifecycleHandler`

**File:** `src/ControlMenu/Services/Network/ScanLifecycleHandler.cs` (+ `IScanLifecycleHandler.cs` alongside).

**DI registration:** `builder.Services.AddScoped<IScanLifecycleHandler, ScanLifecycleHandler>()` in `Program.cs`.

**Dependencies (constructor-injected):**
- `INetworkScanService` — subscribed to on construction
- `IAdbService`
- `INetworkDiscoveryService`
- `IConfigurationService`
- `IDeviceService`

**Public surface:**

```csharp
public interface IScanLifecycleHandler : IDisposable
{
    IReadOnlyList<DiscoveredDevice> Discovered { get; }
    IReadOnlyDictionary<string, string> StashedNamesByMac { get; }
    ScanPhase Phase { get; }
    ScanProgressEvent? LastProgress { get; }
    event Action? OnStateChanged;

    string? ConsumeLastError();  // returns pending error and clears it

    Task StartFullScanAsync(IReadOnlyList<ParsedSubnet> subnets);
    Task CancelScanAsync();
    void Dismiss(DiscoveredDevice d);
    void ReplaceDiscovered(IEnumerable<DiscoveredDevice> devices);
}
```

**Behavior:**

- **Construction:** subscribes to `INetworkScanService.Subscribe(OnScanEvent)`; seeds `_phase` from `_scan.Phase`.
- **`OnScanEvent(ScanEvent)`** — ported verbatim from the page's current handler minus the `InvokeAsync` wrap:
  - `ScanStartedEvent` → clear `_lastProgress`
  - `ScanProgressEvent` → update `_lastProgress`
  - `ScanHitEvent` → append to `_discovered` unless address is in `_dismissedAddresses`
  - `ScanDrainingEvent` → no-op (phase reflects via `Phase` property)
  - `ScanCompleteEvent` → `await FinalizeScanAsync()`
  - `ScanCancelledEvent` → no-op (partial hits remain in place)
  - `ScanErrorEvent` → set internal `_lastError` (consumed via `ConsumeLastError()`)
  - After switch: `_phase = _scan.Phase; OnStateChanged?.Invoke();`
- **`FinalizeScanAsync`** — ported verbatim including the five helpers (`DetermineAdbMergeCandidatesAsync`, `BuildArpMapWithPingsAsync`, `EnrichDiscoveredMacs`, `AppendAdbMergeRows`, `PopulateStashedNamesAsync`). One behavioral change: `AppendAdbMergeRows` skips rows whose address is now in `_dismissedAddresses` (the R2 race fix).
- **`StartFullScanAsync(subnets)`** — clears `_discovered`, `_dismissedAddresses`, `_stashedNamesByMac`; raises `OnStateChanged`; awaits `_scan.StartScanAsync(subnets)`.
- **`CancelScanAsync()`** — awaits `_scan.CancelAsync()`. Phase change arrives via event stream.
- **`Dismiss(d)`** — removes `d` from `_discovered`; adds `AddressKey(d.Ip, d.Port)` to `_dismissedAddresses`; raises `OnStateChanged`.
- **`ReplaceDiscovered(devices)`** — assigns `_discovered = devices.ToList()`. Does *not* touch `_dismissedAddresses` or `_stashedNamesByMac`. Raises `OnStateChanged`. Called by Quick Refresh.
- **`Dispose()`** — disposes the scan subscription.

### New: `DiscoveredPanel.razor`

**File:** `src/ControlMenu/Components/Shared/Scanner/DiscoveredPanel.razor`.

**Parameters:**
- `IReadOnlyList<DiscoveredDevice> Discovered` (required)
- `IReadOnlyDictionary<string, string> StashedNamesByMac` (required)
- `IReadOnlyList<Device> Registered` (required)
- `EventCallback<DiscoveredDevice> OnAdd`
- `EventCallback<DiscoveredDevice> OnDismiss`

**Markup:** the entire `@if (_discovered.Count > 0) { <div class="settings-section"> ... </div> }` block (current lines 95-135 of `DeviceManagement.razor`). Renders nothing when `Discovered.Count == 0`.

**Owned logic:** `NameFor(DiscoveredDevice)` — moved verbatim from page. Consults `Registered` (by MAC), then `StashedNamesByMac`, then returns "Unknown".

**No injected services.** The panel is pure markup + a helper method; all data flows in via parameters.

### Promoted: `DiscoveredDevice`

**File:** `src/ControlMenu/Services/Network/DiscoveredDevice.cs`.

**Definition:**

```csharp
namespace ControlMenu.Services.Network;

public record DiscoveredDevice(
    string ServiceName,
    string Ip,
    int Port,
    string? Mac,
    string? Source = null);
```

Nested definition at `DeviceManagement.razor:189` is deleted.

### Modified: `DeviceManagement.razor` — shrinks to shell

**Removes:**
- All `@inject` for `INetworkScanService` (now a handler dep only).
- Fields: `_discovered`, `_dismissedAddresses`, `_stashedNamesByMac`, `_phase`, `_lastProgress`, `_scanSubscription`.
- Nested `DiscoveredDevice` record.
- Methods: `OnScanEvent`, `FinalizeScanAsync`, `DetermineAdbMergeCandidatesAsync`, `BuildArpMapWithPingsAsync`, `EnrichDiscoveredMacs`, `AppendAdbMergeRows`, `PopulateStashedNamesAsync`, `DismissDiscovered`, `NameFor`, `CancelScan`.
- `IDisposable` implementation (handler disposes itself via DI scope).
- Discovered panel markup (current lines 95-135).

**Adds / changes:**
- `@inject IScanLifecycleHandler Handler`.
- In `OnInitializedAsync`: `Handler.OnStateChanged += HandleHandlerStateChanged;` where `HandleHandlerStateChanged` calls `InvokeAsync` to marshal `StateHasChanged` onto the UI thread, and routes `Handler.ConsumeLastError()` to `ShowMessage` when the returned value is non-null.
- `OnFullScanStart(subnets)` → `await Handler.StartFullScanAsync(subnets)`.
- Modal `ScanInProgress` binding reads `Handler.Phase` instead of `_phase`.
- Cancel button `@onclick` → `Handler.CancelScanAsync`.
- Chip row binds to `Handler.Phase`, `Handler.LastProgress`, `Handler.Discovered.Count`.
- Replace the inline Discovered block with `<DiscoveredPanel Discovered="Handler.Discovered" StashedNamesByMac="Handler.StashedNamesByMac" Registered="_devices" OnAdd="AddFromDiscovery" OnDismiss="Handler.Dismiss" />`.
- `QuickRefresh` — build the list as today, end with `Handler.ReplaceDiscovered(discovered)` instead of `_discovered = discovered`.
- `SaveDevice`'s "drop just-added MAC from Discovered" filter becomes a targeted `Handler.ReplaceDiscovered(Handler.Discovered.Where(...))`.

**Expected line count:** 700 → ~420.

### Unchanged

- `INetworkScanService` / `NetworkScanService` — architecture preserved.
- `ScanProgressChip`, `ScanNetworkModal` — no code changes.
- `ScanMergeHelper`, `HitDedupe`, `SubnetParser`, etc. — no changes.

---

## 4. R2 race fix — concrete change

**File:** `ScanLifecycleHandler.cs` (new), inside `AppendAdbMergeRows`.

Current page code (to be ported):

```csharp
foreach (var x in fromAdb)
{
    var mac = arpMap.TryGetValue(x.Ip, out var m) ? m : null;
    _discovered.Add(new DiscoveredDevice(...));
}
```

Ported and fixed:

```csharp
foreach (var x in fromAdb)
{
    // Re-filter at apply time. Between DetermineAdbMergeCandidatesAsync
    // (which captured _dismissedAddresses at t=0) and this loop, several
    // seconds of ARP+ping awaits passed during which the user may have
    // dismissed one of the addresses we're about to append.
    if (_dismissedAddresses.Contains(ScanMergeHelper.AddressKey(x.Ip, x.Port)))
        continue;
    var mac = arpMap.TryGetValue(x.Ip, out var m) ? m : null;
    _discovered.Add(new DiscoveredDevice(...));
}
```

`EnrichDiscoveredMacs` requires no change — it iterates `_discovered` directly, and `Dismiss` already removes from `_discovered` before the user's click returns. The only gap was adb-merge rows, which came from `fromAdb` (a snapshot from before the awaits).

---

## 5. Test strategy

Tests added at `tests/ControlMenu.Tests/Services/ScanLifecycleHandlerTests.cs`. xUnit + Moq, matching existing suite convention.

**Fakes:**

- `FakeNetworkScanService : INetworkScanService` — new fake, needed because the handler subscribes and the tests dispatch events on demand. Exposes `Emit(ScanEvent)` test helper.
- `IAdbService`, `INetworkDiscoveryService`, `IConfigurationService`, `IDeviceService` — `Mock<>` per test.

**Test list (~12 tests):**

| # | Behavior |
|---|---|
| 1 | Constructor subscribes and seeds `Phase` from service |
| 2 | `ScanHitEvent` appends to `Discovered` when address not dismissed |
| 3 | `ScanHitEvent` skipped when address in `DismissedAddresses` |
| 4 | `ScanProgressEvent` updates `LastProgress` |
| 5 | `Dismiss(d)` removes from `Discovered` + records in `DismissedAddresses` + raises `OnStateChanged` |
| 6 | `StartFullScanAsync` clears all three state sets + raises `OnStateChanged` |
| 7 | `ReplaceDiscovered` replaces `_discovered` only; `DismissedAddresses` and `StashedNamesByMac` intact |
| 8 | `ScanCompleteEvent` triggers `FinalizeScanAsync`: null-MAC hits get enriched from ARP |
| 9 | `ScanCompleteEvent`: adb-merge rows appended with correct MAC |
| 10 | **R2 race:** adb-merge row dismissed during ARP await is skipped on apply. Gate the fake ARP call via a `TaskCompletionSource`; between dispatch and release, call `handler.Dismiss(...)`; release; assert not appended. |
| 11 | `ScanErrorEvent` populates error; `ConsumeLastError()` returns and clears it (second call returns null) |
| 12 | `Dispose` disposes the scan subscription |

**Out of scope:**

- bUnit tests for `DiscoveredPanel.razor`. The panel is markup + a pure `NameFor` method; its rendering parity is verified by manual QA. `ScanMergeHelper` tests already cover the MAC-keyed lookup semantics indirectly.
- `DeviceManagement` page-level tests — same as today; the extraction moves logic *out*, so no new coverage is needed at the page layer.

**Baseline:** 211 tests → target ~223 after extraction.

---

## 6. Migration order (informational — details in the plan)

The implementation plan will split this into task-by-task commits so the app stays working on each commit:

1. Promote `DiscoveredDevice` to a standalone type (mechanical; unblocks everything).
2. Create `IScanLifecycleHandler` + `ScanLifecycleHandler` with constructor, `Dispose`, read-only properties, and public methods (but no internal event logic yet). Register in DI. Handler compiles but does nothing useful yet.
3. Move `OnScanEvent` + `FinalizeScanAsync` + the five helpers into the handler. Include the R2 fix. Page still has its own copy — we're in a duplicate-state window for one commit.
4. Switch page to inject `IScanLifecycleHandler` and bind all scanner state/actions through it. Delete the duplicate state + methods from the page.
5. Carve `DiscoveredPanel.razor`; replace the inline block in `DeviceManagement.razor`; move `NameFor` with it.
6. Add the 12 handler tests. (TDD-minded readers: tests for the R2 fix and any new behavior are written before that behavior lands in step 3; remaining tests backfill after step 4.)
7. `CHANGELOG.md` entry under `[Unreleased] ### Changed`.
8. Manual QA.

---

## 7. Open items intentionally deferred

- **`M-7` (start `PopulateStashedNamesAsync` concurrently with ARP work).** Negligible wall-clock impact; untouched by this extraction. Would add concurrency reasoning to tests. Not worth it now.
- **Any additional bUnit coverage for the page or panel.** This extraction doesn't add gaps that require it; existing coverage + manual QA suffices.

---

## 8. Definition of done

- `ScanLifecycleHandler` ships with all 12 tests green.
- `DeviceManagement.razor` ≤ 450 lines; scanner-specific fields and methods all gone.
- `DiscoveredPanel.razor` exists and renders identically to the current inline block.
- R2 race fix covered by test 10.
- `CHANGELOG.md` updated.
- Manual QA items 16-21 from the UX-redesign spec still pass (no regression).
- Quick Refresh still behaves identically (silent, respects dismissals = no; rebuilds Discovered = yes).
- All existing 211 tests still pass.
