# Device Serial Capture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Populate `Device.SerialNumber` automatically when a user adds a device from the Discovered panel, by probing `ro.serialno` alongside the existing kind/model probes in `AddFromDiscovery`.

**Architecture:** A single third parallel ADB probe in `AddFromDiscovery` feeds the result into `_formDevice.SerialNumber` before save. The existing save path already persists the value. No backfill for existing devices (B3 scope). No UI changes (`DeviceForm.razor` already has the manual input). No migration (`SerialNumber` column already exists since `InitialCreate`). Two doc-comment additions disambiguate `ScanHit.Serial` (mDNS-advertised) from `Device.SerialNumber` (ADB-canonical).

**Tech Stack:** Blazor Server (.NET 9). Existing patterns: `AdbService.GetPropAsync(ip, port, prop)` for `ro.*` getprop calls, parallel `Task.WhenAll` for fire-together probes, empty-check guards to avoid overwriting user edits mid-probe.

**Spec:** `docs/superpowers/specs/2026-04-21-device-serial-capture-design.md` (commit `42a96cf`).

**Branch:** `master`. Each task commits directly. Feature is three small files — no branch isolation needed.

**Working directory:** `C:/Users/jscha/source/repos/tools-menu/`

**Verification baseline:** 225 tests passing at HEAD `42a96cf`.

---

## File Structure

### Files to modify

| File | What changes |
|---|---|
| `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor` | `AddFromDiscovery` gains a third parallel `GetPropAsync` probe (`ro.serialno`); result pre-fills `_formDevice.SerialNumber` when non-empty AND the form field isn't already populated. Mirrors the existing Name auto-fill pattern. |
| `src/ControlMenu/Data/Entities/Device.cs` | XML doc comment on `SerialNumber` property clarifying it's the canonical ADB serial and cross-referencing `ScanHit.Serial`. |
| `src/ControlMenu/Services/Network/ScanHit.cs` | XML doc comment on the record clarifying `Serial` is the mDNS-advertised value (used for dedupe/logging only), cross-referencing `Device.SerialNumber` as the persistent canonical source. |
| `CHANGELOG.md` | New bullet under `[Unreleased] ### Added`. |

### No new files

Everything is an addition to existing files.

### No test project changes

Per spec §4, no new xUnit tests. Manual verification only. The existing 225 tests must continue to pass (they shouldn't be touched at all).

---

## Task Ordering Rationale

Three tasks, in this order:

1. **T1 — Probe + pre-fill.** The behavioral change. Ships the feature.
2. **T2 — Doc comments on both serial fields.** Pure documentation; safe to land after the behavior exists.
3. **T3 — CHANGELOG bullet.** Landmark doc once the feature is real.

T1 could theoretically be split into "add probe" and "add pre-fill guard" but they're genuinely one logical change (a probe you don't use is pointless). Keep combined.

Manual QA is not a separate task — it's part of T1's verification step. The spec's 5 scenarios map to "build + run + click through."

---

## Task 1: `AddFromDiscovery` probes `ro.serialno` and pre-fills the form

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor`

The current `AddFromDiscovery` method runs two parallel probes (kind + model) after `ConnectAsync` succeeds. We add a third probe for `ro.serialno` and, after all three complete, fill `_formDevice.SerialNumber` following the same empty-check pattern the Name auto-fill uses.

- [ ] **Step 1: Read current state of `AddFromDiscovery`**

Open `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor` and confirm the method shape. As of commit `42a96cf`, the relevant block is (line numbers approximate):

```csharp
    private async Task AddFromDiscovery(DiscoveredDevice d)
    {
        // ... form pre-fill from stashed name ...
        _formDevice = new Device
        {
            Name = rememberedName ?? "",
            MacAddress = d.Mac ?? "",
            Type = DeviceType.AndroidPhone,
            AdbPort = d.Port,
            LastKnownIp = d.Ip,
            ModuleId = "android-devices"
        };
        _formPin = "";
        _isEditing = false;
        _showForm = true;
        StateHasChanged();

        // Best-effort refinement via ADB. Two probes in parallel: device kind
        // (phone/tablet/tv/watch — five shell signals, same as ws-scrcpy-web
        // plus a watch probe) and product model (for a friendlier default Name
        // than the raw mDNS serial).
        var connected = await AdbService.ConnectAsync(d.Ip, d.Port);
        if (!connected) return;

        var kindTask = AdbService.DetectDeviceKindAsync(d.Ip, d.Port);
        var modelTask = AdbService.GetPropAsync(d.Ip, d.Port, "ro.product.model");
        await Task.WhenAll(new Task[] { kindTask, modelTask });

        var kind = await kindTask;
        var model = await modelTask;

        var mapped = kind switch
        {
            "tv" => DeviceType.GoogleTV,
            "tablet" => DeviceType.AndroidTablet,
            "watch" => DeviceType.AndroidWatch,
            "phone" => DeviceType.AndroidPhone,
            _ => _formDevice.Type,
        };
        var changed = false;
        if (_formDevice.Type != mapped)
        {
            _formDevice.Type = mapped;
            changed = true;
        }
        // Only auto-fill Name when we don't already have one from the DB.
        // Empty name + model probe succeeded → prefill.
        if (string.IsNullOrEmpty(_formDevice.Name) && !string.IsNullOrEmpty(model))
        {
            _formDevice.Name = model;
            changed = true;
        }
        if (changed) StateHasChanged();
    }
```

If the method diverges significantly from this shape (e.g., extra probes already exist, or the parallel section has been refactored into a helper), stop and report — the plan needs revision before proceeding.

- [ ] **Step 2: Modify the probe-setup line**

Find the existing two-probe setup:

```csharp
        var kindTask = AdbService.DetectDeviceKindAsync(d.Ip, d.Port);
        var modelTask = AdbService.GetPropAsync(d.Ip, d.Port, "ro.product.model");
        await Task.WhenAll(new Task[] { kindTask, modelTask });
```

Replace with a three-probe setup:

```csharp
        var kindTask = AdbService.DetectDeviceKindAsync(d.Ip, d.Port);
        var modelTask = AdbService.GetPropAsync(d.Ip, d.Port, "ro.product.model");
        var serialTask = AdbService.GetPropAsync(d.Ip, d.Port, "ro.serialno");
        await Task.WhenAll(new Task[] { kindTask, modelTask, serialTask });
```

Update the comment immediately above if needed. The current comment says "Two probes in parallel" — change to "Three probes in parallel" and mention the serial:

```csharp
        // Best-effort refinement via ADB. Three probes in parallel: device kind
        // (phone/tablet/tv/watch — five shell signals, same as ws-scrcpy-web
        // plus a watch probe), product model (for a friendlier default Name
        // than the raw mDNS serial), and ro.serialno (for Device.SerialNumber
        // auto-fill; user may still override via the form input).
```

- [ ] **Step 3: Pull the serial result**

Find the existing result extraction:

```csharp
        var kind = await kindTask;
        var model = await modelTask;
```

Add a third line:

```csharp
        var kind = await kindTask;
        var model = await modelTask;
        var serial = await serialTask;
```

- [ ] **Step 4: Add the serial auto-fill guard**

Find the existing Name auto-fill block:

```csharp
        // Only auto-fill Name when we don't already have one from the DB.
        // Empty name + model probe succeeded → prefill.
        if (string.IsNullOrEmpty(_formDevice.Name) && !string.IsNullOrEmpty(model))
        {
            _formDevice.Name = model;
            changed = true;
        }
        if (changed) StateHasChanged();
```

Insert the serial auto-fill block immediately before `if (changed) StateHasChanged();` so both auto-fills share the single `StateHasChanged` call:

```csharp
        // Only auto-fill Name when we don't already have one from the DB.
        // Empty name + model probe succeeded → prefill.
        if (string.IsNullOrEmpty(_formDevice.Name) && !string.IsNullOrEmpty(model))
        {
            _formDevice.Name = model;
            changed = true;
        }
        // Same pattern for SerialNumber — fill only if the user hasn't typed
        // something into the form field during the ~1-second probe window.
        if (string.IsNullOrEmpty(_formDevice.SerialNumber) && !string.IsNullOrEmpty(serial))
        {
            _formDevice.SerialNumber = serial;
            changed = true;
        }
        if (changed) StateHasChanged();
```

- [ ] **Step 5: Build**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj --nologo -c Release
```

Expected: `0 Warning(s), 0 Error(s)`.

- [ ] **Step 6: Run all tests**

```bash
dotnet test --nologo --verbosity quiet
```

Expected: `Passed: 225`. No test count change — this is a behavior addition with no unit tests per spec §4.

- [ ] **Step 7: Manual smoke test (user-driven)**

Run the app:

```bash
dotnet run --project src/ControlMenu/ControlMenu.csproj -c Release --no-build
```

Navigate to **Settings › Devices**. Either use an existing discovered device or run `Scan Network…` → Start to generate one. Click **Add** on a Discovered row. The device form opens. Watch the Serial Number field: within ~1 second of the form opening, it should populate with a value (e.g., `47121FDAQ000WC` for a Pixel). Save the device. Open the same device for **Edit**. Serial Number should persist.

If the field stays empty, check:
- Did `ConnectAsync` succeed? (Page shows no error toast either way — check browser console for ADB errors.)
- Does the device actually report `ro.serialno`? Try from a terminal: `adb -s <ip>:<port> shell getprop ro.serialno` — must return a non-empty value.

- [ ] **Step 8: Commit**

```bash
git add src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor
git commit -m "feat(devices): auto-fill SerialNumber from ro.serialno during Add

AddFromDiscovery already runs parallel ADB probes for device kind and
product model. Add a third probe for ro.serialno; apply the result to
_formDevice.SerialNumber using the same empty-check guard as the Name
auto-fill (don't overwrite a value the user typed during the ~1-second
probe window).

No backfill for existing devices (B3 scope per spec). No migration —
Device.SerialNumber already exists in the DB. No UI changes —
DeviceForm.razor already has the manual input. Manual QA only; no
new unit tests."
```

---

## Task 2: XML doc comments on `Device.SerialNumber` and `ScanHit.Serial`

**Files:**
- Modify: `src/ControlMenu/Data/Entities/Device.cs`
- Modify: `src/ControlMenu/Services/Network/ScanHit.cs`

Two short XML doc comments clarifying that both fields refer to the same concept (Android ADB serial number) but come from different sources — the persistent value should be the ADB-probed one.

- [ ] **Step 1: Doc-comment `Device.SerialNumber`**

Open `src/ControlMenu/Data/Entities/Device.cs`. The current definition (as of commit `42a96cf`):

```csharp
public class Device
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DeviceType Type { get; set; }
    public required string MacAddress { get; set; }
    public string? SerialNumber { get; set; }
    public string? LastKnownIp { get; set; }
    public int AdbPort { get; set; } = 5555;
    public DateTime? LastSeen { get; set; }
    public required string ModuleId { get; set; }
    public string? Metadata { get; set; }
}
```

Insert an XML doc comment immediately above the `SerialNumber` property:

```csharp
    /// <summary>
    /// Canonical ADB serial number for this device (value of <c>ro.serialno</c>).
    /// Populated automatically during <c>AddFromDiscovery</c> via a live ADB
    /// probe; can also be set manually via the Add/Edit form.
    /// </summary>
    /// <remarks>
    /// Same concept as <see cref="Services.Network.ScanHit.Serial"/> but
    /// authoritative — sourced from a connected ADB session rather than the
    /// mDNS service-name string. When both exist for the same device they
    /// should agree; this field is the persistent truth.
    /// </remarks>
    public string? SerialNumber { get; set; }
```

- [ ] **Step 2: Doc-comment `ScanHit.Serial`**

Open `src/ControlMenu/Services/Network/ScanHit.cs`. The current definition (as of commit `42a96cf`):

```csharp
namespace ControlMenu.Services.Network;

public enum DiscoverySource { Mdns, Tcp, Adb }

/// <summary>
/// A device observed during a scan. <see cref="Address"/> is <c>"IP:port"</c>
/// exactly as emitted by ws-scan's scan.hit message. <see cref="Mac"/> is null
/// until ARP resolves the IP post-TCP-touch.
/// </summary>
public sealed record ScanHit(
    DiscoverySource Source,
    string Address,
    string Serial,
    string Name,
    string Label,
    string? Mac);
```

Extend the existing `<summary>` with an explicit paragraph about `Serial` (because record parameters can't each have their own XML doc, the canonical pattern is adding `<param>` elements):

```csharp
namespace ControlMenu.Services.Network;

public enum DiscoverySource { Mdns, Tcp, Adb }

/// <summary>
/// A device observed during a scan. <see cref="Address"/> is <c>"IP:port"</c>
/// exactly as emitted by ws-scan's scan.hit message. <see cref="Mac"/> is null
/// until ARP resolves the IP post-TCP-touch.
/// </summary>
/// <param name="Source">Which discovery channel produced this hit.</param>
/// <param name="Address">Canonical <c>ip:port</c> string (see <see cref="ScanMergeHelper.AddressKey"/>).</param>
/// <param name="Serial">
/// ADB serial as advertised in the mDNS service name (the
/// <c>adb-&lt;serial&gt;._adb-tls-connect._tcp.local.</c> form). Used for scan-result
/// dedupe (see <see cref="HitDedupe"/>) and logging only. For persisting to a
/// registered device, a live <c>ro.serialno</c> probe in <c>AddFromDiscovery</c>
/// is authoritative — see <see cref="Data.Entities.Device.SerialNumber"/>.
/// </param>
/// <param name="Name">The raw mDNS service label, e.g. <c>adb-ABC123._adb-tls-connect._tcp.local.</c>.</param>
/// <param name="Label">Human-friendly label from scan output (often empty).</param>
/// <param name="Mac">MAC address resolved from ARP, or null if unresolved.</param>
public sealed record ScanHit(
    DiscoverySource Source,
    string Address,
    string Serial,
    string Name,
    string Label,
    string? Mac);
```

The `<see cref="ScanMergeHelper.AddressKey"/>` and `<see cref="HitDedupe"/>` references resolve because both live in the same `ControlMenu.Services.Network` namespace as `ScanHit`. The `<see cref="Data.Entities.Device.SerialNumber"/>` uses the full relative path from the current namespace.

- [ ] **Step 3: Build**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj --nologo -c Release
```

Expected: `0 Warning(s), 0 Error(s)`. XML doc comments on `record` parameters can sometimes trigger `CS1573` if the compiler thinks a parameter is undocumented — the full `<param>` set above covers all six, so no warnings should fire. If one does, it's likely a typo in the `name=` attribute; compare against the positional params exactly.

- [ ] **Step 4: Run tests**

```bash
dotnet test --nologo --verbosity quiet
```

Expected: `Passed: 225`. Doc comments have no runtime effect.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Data/Entities/Device.cs src/ControlMenu/Services/Network/ScanHit.cs
git commit -m "docs(devices): clarify Device.SerialNumber vs ScanHit.Serial

Both fields refer to the same concept — the Android ADB serial number —
but come from different sources:
  - Device.SerialNumber: ro.serialno via live ADB probe (authoritative)
  - ScanHit.Serial: parsed from the mDNS service-name advertisement,
                    used for scan-result dedupe and logging only.

Cross-referencing XML doc comments make the relationship explicit so
future readers don't wonder which is canonical."
```

---

## Task 3: CHANGELOG update

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Append to `[Unreleased] ### Added`**

Open `CHANGELOG.md`. Find the `## [Unreleased]` section and its `### Added` subsection (the first subsection after the `[Unreleased]` header). Append this bullet at the end of the existing bullets:

```markdown
- **Device SerialNumber auto-fill** — when a user adds a device from the Discovered panel, `AddFromDiscovery` now probes `ro.serialno` alongside the existing kind and model probes and pre-fills the Serial Number form field. Users who type a value during the probe window keep their entry (empty-check guard, same pattern as the existing Name auto-fill). Existing registered devices without a serial stay unchanged — backfill is intentionally out of scope.
```

- [ ] **Step 2: Commit**

```bash
git add CHANGELOG.md
git commit -m "docs(changelog): auto-fill Device.SerialNumber during Add"
```

---

## Self-Review Checklist

Completed during plan authoring:

- **Spec coverage.**
  - Decision 1 (S1 always-probe): T1 Step 2 adds the `ro.serialno` probe. ✓
  - Decision 2 (B3 no backfill): neither T1 nor T2 nor T3 touches Quick Refresh, `FinalizeScanAsync`, or any backfill path. ✓
  - Decision 3 (doc comments, no rename): T2 covers both files. ✓
  - Decision 4 (manual verification, no new tests): T1 Step 7 walks the manual flow; no test project changes anywhere. ✓
  - DoD items — probe in `AddFromDiscovery` (T1), empty-check guard (T1 Step 4), doc comments (T2), CHANGELOG (T3), 225 tests still pass (Step 6 verifies). ✓

- **Placeholder scan.** No "TBD" / "TODO" / "implement later" / "add validation" phrases. Every step shows the exact code or the exact command. Expected outputs are stated. Step 1 of T1 shows the pre-change state so the engineer can sanity-check before editing.

- **Type consistency.** `_formDevice.SerialNumber` (string?) used consistently across T1 steps. `AdbService.GetPropAsync(string ip, int port, string prop)` matches the existing call shape in the method. No new types introduced. XML doc comments reference real symbols (`Device.SerialNumber`, `ScanMergeHelper.AddressKey`, `HitDedupe`) that all exist at the plan's base commit.

- **Task ordering.** T1 (behavior) → T2 (docs for behavior) → T3 (CHANGELOG for behavior). Each commit leaves the app building and tests green.
