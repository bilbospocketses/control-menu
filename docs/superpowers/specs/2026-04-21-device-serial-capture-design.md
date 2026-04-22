# Device serial capture — design spec

**Date:** 2026-04-21
**Author:** brainstormed interactively after scanner extraction shipped
**Scope:** populate `Device.SerialNumber` via a `ro.serialno` ADB probe during `AddFromDiscovery`. No backfill for existing devices. No UI changes beyond pre-fill of an already-existing form field.

---

## 1. Motivation

Every device registered in Control Menu is an ADB device — its serial number is trivially available via `adb shell getprop ro.serialno` once connected. The `Device` entity already has a `SerialNumber` property (nullable string, in the DB since `InitialCreate`), and `DeviceForm.razor` already has a manual input field for it. What's missing is the automatic capture: when a user clicks Add on a Discovered panel row, we connect to the device to probe kind and model — an additional `ro.serialno` probe is a natural extension. This gives us data hygiene (serials in the DB "keep everything lined up for reference") without user-visible friction.

---

## 2. Decisions locked in during brainstorming

| # | Topic | Decision | Rationale |
|---|---|---|---|
| 1 | Serial data source | **S1: always probe via ADB (`ro.serialno`).** Ignore `ScanHit.Serial` for persistence purposes. | Single canonical source. The probe already runs in parallel with the existing kind/model probes in `AddFromDiscovery`; marginal cost is just network bytes, not a sequential wait. S2 (mDNS fast-path) adds code surface for a barely-visible speedup. |
| 2 | Backfill policy | **B3: no backfill.** Only `AddFromDiscovery` populates `SerialNumber`. | Narrowest scope. Existing devices without a serial stay empty; user can edit manually or delete + re-add. Quick Refresh remains lightweight. |
| 3 | Naming disambiguation | **Doc comments only, no rename.** `ScanHit.Serial` and `Device.SerialNumber` refer to the same concept from different sources; cross-referencing XML doc comments make the relationship explicit. | Renaming either is disruptive for a small gain. `ScanHit.Serial` is used for dedupe/logging only; `Device.SerialNumber` is the persistent value — the distinction is about source, not about meaning. |
| 4 | Tests | **Manual verification only.** | The change is a ~5-line addition to Razor code that has no existing unit tests. A reusable probe helper would justify unit tests; this one-shot doesn't. |

---

## 3. Component responsibilities

### `DeviceManagement.razor` — `AddFromDiscovery` gets a third probe

**Current (pre-A2):**

```csharp
var kindTask  = AdbService.DetectDeviceKindAsync(d.Ip, d.Port);
var modelTask = AdbService.GetPropAsync(d.Ip, d.Port, "ro.product.model");
await Task.WhenAll(new Task[] { kindTask, modelTask });

var kind = await kindTask;
var model = await modelTask;
// ... kind/model assignment ...
if (changed) StateHasChanged();
```

**After A2:**

```csharp
var kindTask   = AdbService.DetectDeviceKindAsync(d.Ip, d.Port);
var modelTask  = AdbService.GetPropAsync(d.Ip, d.Port, "ro.product.model");
var serialTask = AdbService.GetPropAsync(d.Ip, d.Port, "ro.serialno");
await Task.WhenAll(new Task[] { kindTask, modelTask, serialTask });

var kind = await kindTask;
var model = await modelTask;
var serial = await serialTask;
// ... kind/model assignment ...

if (string.IsNullOrEmpty(_formDevice.SerialNumber) && !string.IsNullOrEmpty(serial))
{
    _formDevice.SerialNumber = serial;
    changed = true;
}
if (changed) StateHasChanged();
```

Pattern mirrors the existing Name auto-fill: only fill if the user hasn't already provided a value (guards against the probe overwriting an in-flight manual edit).

### Unchanged

- `Device` entity — `SerialNumber` already exists.
- DB schema — column already exists in `InitialCreate` migration. No new migration.
- `DeviceForm.razor` — already has the manual serial input (line 37). No UI changes.
- `SaveDevice` — already persists `_formDevice.SerialNumber` via the existing `AddDeviceAsync` path. No changes.
- `Quick Refresh` — untouched per B3.
- `FinalizeScanAsync` and all handler code — untouched per B3.
- `ScanHit.Serial`, `DiscoveredDevice`, `MdnsAdbDevice` — untouched per S1.

### Doc-comment additions

Two files gain XML doc comments clarifying the serial semantics:

- `src/ControlMenu/Data/Entities/Device.cs` — on the `SerialNumber` property.
- `src/ControlMenu/Services/Network/ScanHit.cs` — on the `Serial` positional parameter (via a `<summary>` in the record declaration or a `<param>`-style comment).

Wording captures the "same concept, different source" relationship:

> `Device.SerialNumber`: Canonical ADB serial number (`ro.serialno`). Populated automatically during `AddFromDiscovery` via a live ADB probe; can also be set manually via the Add/Edit form. Same concept as `ScanHit.Serial` but authoritative (sourced from a connected ADB session rather than an mDNS service name).

> `ScanHit.Serial`: ADB serial as advertised in the mDNS service name (`adb-<serial>._adb-tls-connect._tcp.local.`). Used for scan-result dedupe (see `HitDedupe`) and logging only. For persisting to a registered device, the live `ro.serialno` probe in `AddFromDiscovery` is authoritative — see `Device.SerialNumber`.

---

## 4. Test strategy

Manual verification via existing `docs/manual-test-checklist.md` flow. No new xUnit tests.

- **Discovered → Add.** From Settings › Devices, run a Full Scan, click Add on a row. Form opens with empty Serial Number field initially. After ~1 second (probe completes), field populates with the device's serial. Save. Open the device for edit — serial persists in DB.
- **Manual Add.** From Settings › Devices, click Add Device. Form opens with Serial Number field empty. User leaves it empty, saves. Field stays empty — no probe runs (no `AddFromDiscovery` path).
- **User overrides.** During the ~1-second probe window, user types a custom serial into the field. Probe completes; the empty-check guard prevents overwrite. User's value persists.
- **Probe failure.** Device not reachable mid-Add (rare — the initial `ConnectAsync` already succeeded). `GetPropAsync` returns empty string. Field stays empty. No error toast needed; consistent with existing kind/model probe failure behavior.
- **Existing device, no backfill.** Delete the newly-added device; re-add via Discovered. Serial re-populates. (Stashed-name flow already exists; serial doesn't need its own stashing since it's re-probed on re-add.)

---

## 5. Open items intentionally deferred

- **Backfill for existing devices.** B3 scope excludes this. If user pressure emerges later, a dedicated "Refresh from ADB" button on the device edit form is the natural add, probing kind + model + serial in one go.
- **Refactor probes into `ProbeDeviceInfoAsync` helper.** Three parallel probes in `AddFromDiscovery` is still readable. When A3 (inline Add UI) lands and needs the same probe results inline, a helper becomes justified. Not now.
- **Serial-based device identity/dedupe.** MAC remains the canonical identity key. Serial is reference data only. A future "merge by serial when MAC changes" flow is out of scope.

---

## 6. Definition of done

- `AddFromDiscovery` probes `ro.serialno` alongside kind + model, pre-fills `_formDevice.SerialNumber` when non-empty and the form field isn't already populated.
- XML doc comments on `Device.SerialNumber` and `ScanHit.Serial` clarifying source / canonical status.
- `CHANGELOG.md` bullet under `[Unreleased] ### Added` noting automatic serial capture during Add.
- Manual verification of the 5 scenarios in §4.
- All 225 existing tests still pass (no behavior change to anything else).
