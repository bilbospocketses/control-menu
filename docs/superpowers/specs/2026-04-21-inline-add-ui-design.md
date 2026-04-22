# Inline Add UI for DiscoveredPanel — design spec

**Date:** 2026-04-21
**Author:** brainstormed interactively after A2 (device serial capture) shipped
**Scope:** replace the modal Add flow triggered from the Discovered panel with per-row inline editable fields. Clicking Add saves the row directly — no modal round-trip. Manual Add Device and Edit flows retain the existing `DeviceForm.razor` modal.

---

## 1. Motivation

After T11 of the scanner extraction, `DiscoveredPanel.razor` is a read-only table: Service / Name / IP / ADB Port / MAC / Actions. Clicking Add opens the `DeviceForm` modal via `AddFromDiscovery`, which runs three parallel ADB probes (kind, model, serial since A2) and populates the modal. The user then confirms, types/adjusts values, and saves.

Two frictions with that flow:

- **Modal context switch.** User focus jumps from the table row to the modal dialog, back to the row after save. On a typical Discovered panel with several rows the user intends to add, this is N modals.
- **Pre-filled data is hidden until Add.** The serial/model/kind only appear after the modal opens. Users can't see "which device is this" without committing to an Add interaction.

A3 moves the editable form inline into each discovered row. Fields are visible from the moment the row appears, filled in by the same probes, adjustable in place. Clicking Add saves directly from the row. Clicking × dismisses the row without any modal.

---

## 2. Decisions locked in during brainstorming

| # | Topic | Decision | Rationale |
|---|---|---|---|
| 1 | Layout | **L1: single wide row with ~9 columns.** All fields as compact inline inputs in the existing table structure. Horizontal scroll is accepted on narrow screens. If the look degrades badly in practice, fallback is L2 (two-row-per-device with the form fields in a colspan-full row below the summary). | User has adequate horizontal space. Simpler structurally than expand-row or card layouts. Reversible later if it's ugly. |
| 2 | Probe timing | **P1: row-side probe on mount.** Each `DiscoveredPanelRow` component runs its own `kind + model + serial` probes when it's first rendered. Row-local state drives the inputs. Probes happen per-row asynchronously. | Keeps the handler's responsibility narrow (discovery + lifecycle only). Row owns what it displays. Probes start immediately — user sees fields fill in within ~1 second. |
| 3 | Scope boundary | **Discovered path only.** `DeviceForm.razor` modal stays for manual Add Device (no Discovered row to expand on) and for Edit (clicking Edit on a registered device in the devices table). | Inline only makes sense when there's a row to inline on. Edit is its own flow; out of scope for A3. |
| 4 | PIN visibility | **Always visible, regardless of device Type.** | User: "make PIN a default field. TVs don't usually use them, but they can." |
| 5 | Component split | **Extract `DiscoveredPanelRow.razor`.** Per-row component owns probe state, input bindings, PIN binding, validation. Parent `DiscoveredPanel.razor` stays thin — header + foreach of rows. | Per-row state is too large to live in the parent's `@code` block without the file ballooning. Follows "one file, one responsibility" from other extractions in this codebase (scanner handler, etc.). |
| 6 | Row identity | **`@key="{ip}:{port}"`** on the `<DiscoveredPanelRow>` foreach. | Preserves row state across re-renders when the scanner adds/removes entries. Without `@key`, Blazor reuses rows positionally and half-typed values bleed into neighboring devices. |
| 7 | Button color semantics | **Add = `btn-success` (green); Dismiss × = `btn-danger` (red).** Requires adding a `.btn-success` rule to `app.css` using `var(--success-color)` to match the theme-token pattern of `.btn-danger` / `.btn-warning`. | Green = confirm/additive, red = destructive/remove. Matches the broader UI color semantic the project has already established. Also upgrades the current `btn-secondary` × button, which was mild neutral-gray today. |
| 8 | Tests | **Manual verification only.** No new xUnit tests; bUnit wouldn't pull its weight for a single row component. | Matches A2's approach. Keeps scope minimal. Regressions surface via manual QA scenarios (see §6). |

---

## 3. Component responsibilities

### New: `DiscoveredPanelRow.razor`

**Path:** `src/ControlMenu/Components/Shared/Scanner/DiscoveredPanelRow.razor`

**Purpose:** render one Discovered row as an editable inline form. Owns local state for the six editable fields + PIN. Runs probes on mount. Emits `OnAdd` (with an `InlineAddPayload`) or `OnDismiss` (with the source `DiscoveredDevice`) to the parent.

**Parameters (flow in):**

```csharp
[Parameter, EditorRequired] public DiscoveredDevice Source { get; set; } = default!;
[Parameter, EditorRequired] public IReadOnlyDictionary<string, string> StashedNamesByMac { get; set; } = default!;
[Parameter, EditorRequired] public IReadOnlyList<Device> Registered { get; set; } = default!;
[Parameter] public EventCallback<InlineAddPayload> OnAdd { get; set; }
[Parameter] public EventCallback<DiscoveredDevice> OnDismiss { get; set; }
```

**Injected services:** `IAdbService`, `IConfigurationService`, `ILogger<DiscoveredPanelRow>`.

**Local state:**

```csharp
private string _name = "";
private DeviceType _type = DeviceType.AndroidPhone;
private int _port = 5555;
private string _mac = "";
private string _serial = "";
private string _pin = "";
private bool _probeRan;  // OnInitializedAsync one-shot guard
```

**Lifecycle:**

- `OnInitializedAsync`: seed `_port`, `_mac`, `_name` (via stashed-name lookup matching `NameFor` logic from the current panel), then fire-and-forget `RunProbesAsync()`.
- `RunProbesAsync`: `ConnectAsync` (bail if false), three parallel probes via `Task.WhenAll`, apply results with empty-check guards (don't overwrite user edits that occurred during the probe window), `InvokeAsync(StateHasChanged)`.

**Validation:**

- Computed `IsValid => !string.IsNullOrWhiteSpace(_name) && !string.IsNullOrWhiteSpace(_mac);`
- Add button `disabled="@(!IsValid)"`.
- Port: native `<input type="number" min="1" max="65535">`. No Blazor-side validation; SaveDevice will error loudly on invalid input.

**PIN rendering:**

- The PIN `<input type="password">` cell is rendered unconditionally — no `@if` on `_type`. Decision 4 changes the existing `DeviceForm` behavior (which hid PIN for TV/Watch) to "always visible" for the inline panel.
- Empty PIN is submitted as empty string and results in no PIN stored (matches existing `SaveDevice` behavior for `_formPin == ""`).

### New: `InlineAddPayload`

**Path:** `src/ControlMenu/Services/Network/InlineAddPayload.cs`

```csharp
namespace ControlMenu.Services.Network;

/// <summary>
/// Payload emitted by <c>DiscoveredPanelRow</c>'s <c>OnAdd</c> callback.
/// Carries the user-edited device plus the separately-stored PIN string
/// (which <c>DeviceManagement.SaveDevice</c> encrypts into the secret store
/// keyed by the saved device's Id).
/// </summary>
/// <param name="Source">The original DiscoveredDevice — used by the parent to filter the row out of Handler.Discovered after save.</param>
/// <param name="Device">All the row's edited fields packed into a <c>Device</c> entity (no Id yet; SaveDevice assigns it).</param>
/// <param name="Pin">PIN string as typed, empty means "no PIN." Never logged.</param>
public sealed record InlineAddPayload(
    DiscoveredDevice Source,
    Data.Entities.Device Device,
    string Pin);
```

### Modified: `DiscoveredPanel.razor`

- Table header gains three new columns: Type, Serial, PIN. Full column list: Service | Name | Type | IP | ADB Port | MAC | Serial | PIN | Actions.
- `<tbody>` foreach now renders `<DiscoveredPanelRow @key="@($"{d.Ip}:{d.Port}")" Source="d" StashedNamesByMac="StashedNamesByMac" Registered="Registered" OnAdd="OnAdd" OnDismiss="OnDismiss" />` instead of the inline `<tr>`.
- `NameFor` helper moves into `DiscoveredPanelRow` (since that's where the initial-name resolution happens now). `DiscoveredPanel.razor` no longer needs it.
- `OnAdd` callback parameter type changes from `EventCallback<DiscoveredDevice>` to `EventCallback<InlineAddPayload>`.

### Modified: `DeviceManagement.razor`

- `AddFromDiscovery(DiscoveredDevice d)` **deleted entirely** — the form-opening, probe-running, and PIN-handling logic all move out. The method shrinks to zero code; its replacement is the new handler below.
- New: `HandleInlineAdd(InlineAddPayload payload)`. Sets `_formDevice = payload.Device`, `_formPin = payload.Pin`, `_isEditing = false`, then calls the existing `SaveDevice` (which already handles MAC normalization, PIN secret-storage, and the "drop added MAC from Discovered" filter).
- The `<DiscoveredPanel>` element's `OnAdd="AddFromDiscovery"` becomes `OnAdd="HandleInlineAdd"`.

### New: `.btn-success` CSS rule

**Path:** `src/ControlMenu/wwwroot/css/app.css`, added near the existing `.btn-danger` rule (currently line 151):

```css
.btn-success { background-color: var(--success-color); color: #fff; }
.btn-success:hover { opacity: 0.9; }
```

Uses the existing `--success-color` theme variable (defined alongside `--danger-color` in the theme.css, reused from the scanner UX redesign work).

### Unchanged

- `DeviceForm.razor` — stays in place for manual Add and Edit flows.
- `ScanLifecycleHandler` — no probe responsibilities added.
- `DiscoveredDevice` record — no new fields; probe results live as row-local state in `DiscoveredPanelRow`.
- `Device` entity — already has all required properties.

---

## 4. Probe flow reference

Same shape as A2's `AddFromDiscovery` probes, just relocated into the row component.

```csharp
private async Task RunProbesAsync()
{
    var connected = await _adb.ConnectAsync(Source.Ip, Source.Port);
    if (!connected)
    {
        _logger.LogDebug("Row probe skipped — ConnectAsync false for {Ip}:{Port}", Source.Ip, Source.Port);
        return;
    }

    var kindTask   = _adb.DetectDeviceKindAsync(Source.Ip, Source.Port);
    var modelTask  = _adb.GetPropAsync(Source.Ip, Source.Port, "ro.product.model");
    var serialTask = _adb.GetPropAsync(Source.Ip, Source.Port, "ro.serialno");
    await Task.WhenAll(new Task[] { kindTask, modelTask, serialTask });

    var kind = await kindTask;
    var mapped = kind switch
    {
        "tv" => DeviceType.GoogleTV,
        "tablet" => DeviceType.AndroidTablet,
        "watch" => DeviceType.AndroidWatch,
        "phone" => DeviceType.AndroidPhone,
        _ => _type,
    };
    if (_type == DeviceType.AndroidPhone) _type = mapped;  // AndroidPhone is the default seeding value

    var model = await modelTask;
    if (string.IsNullOrEmpty(_name) && !string.IsNullOrEmpty(model)) _name = model;

    var serial = await serialTask;
    if (string.IsNullOrEmpty(_serial) && !string.IsNullOrEmpty(serial)) _serial = serial;

    await InvokeAsync(StateHasChanged);
}
```

Empty-check guards on all three assignments prevent user input during the probe window from being overwritten.

---

## 5. Test strategy

No new xUnit tests. Manual verification covers these scenarios:

1. **Probe populates fields.** Start a scan; row appears. Within ~1 sec, Name/Type/Serial populate without user action.
2. **User override survives probe.** Start a scan; row appears. User immediately types into Name field. Probe completes shortly after; user's typed Name is preserved.
3. **Add click saves device.** Click Add on a row with populated fields. Toast confirms. Device appears in registered table with all values (Name, Type, Port, MAC, Serial). Edit the device; values persist including Serial. PIN — if typed — is stored via the secret store (verify by editing and re-reading via the PIN field in the edit modal).
4. **Add disabled when invalid.** Row with empty Name or empty MAC: Add button stays disabled. Fill in both: button enables.
5. **Dismiss removes row.** Click × on a row; row disappears from panel. Address is recorded in `_dismissedAddresses` (won't re-appear on the same scan).
6. **Multiple live rows independent.** Scan finds 3 devices. User types into row B. Scanner emits another hit — row D appears. Rows A/B/C keep their values; row D appears with its own probe in flight. Proves `@key="ip:port"` behavior.
7. **Color theme.** Add button is theme-green. × button is theme-red. Swap theme (dark/light); both remain legible.
8. **Manual Add Device still works.** Top-of-table button opens `DeviceForm` modal as before. Edit on a registered device still opens the modal. Regression guard.

---

## 6. Migration notes

- **Pre-A2 data safety** — the A2-fix commit (`ad8db34`) already ensures `ShowEditForm` clones all Device fields including `SerialNumber`. No further clone-safety work needed for A3.
- **`AddFromDiscovery` deletion** — the method shrinks from ~50 lines to nothing. The three parallel probes it ran are replaced by the row's `RunProbesAsync`. The form-opening path is replaced by inline inputs.
- **Existing panel consumers** — the only consumer of `DiscoveredPanel` is `DeviceManagement.razor`; `OnAdd`'s signature change is an isolated edit there.

---

## 7. CHANGELOG bullet

Under `[Unreleased] ### Changed`:

> - **Discovered panel inline Add** — rows now contain editable fields (Name, Type, Port, MAC, Serial, PIN) populated by an on-mount ADB probe. Clicking Add saves directly from the row — the per-device modal dialog is gone for this path. Manual Add Device and Edit still use the modal. Add button is theme-green; × button is theme-red (new `.btn-success` CSS rule mirrors the existing `.btn-danger` pattern using `var(--success-color)`).

---

## 8. Open items intentionally deferred

- **bUnit tests for `DiscoveredPanelRow`.** Per decision 8, not now. If the component gains more branching logic (different layouts per type, validation variants), revisit.
- **Probe caching across sessions.** Probe results live only for the current row-component lifetime. If a user dismisses and a later scan re-adds the same device, the probe runs again. Cheap enough to ignore.
- **Retry on probe failure.** If `ConnectAsync` returns false, the row displays whatever the initial seed gave it. User can still type values manually and click Add. No automatic retry. If users complain, consider a small "Retry probe" link in the row.
- **Layout fallback to L2.** If L1 proves too cramped in practice, schedule a follow-up to convert to a two-row-per-device layout. No work happens in this session for that fallback.

---

## 9. Definition of done

- `DiscoveredPanelRow.razor` exists with the parameters, state, probe flow, and validation described in §3.
- `InlineAddPayload.cs` exists.
- `DiscoveredPanel.razor` delegates row rendering to the new component and carries three new header columns.
- `DeviceManagement.razor`'s `AddFromDiscovery` is gone; `HandleInlineAdd` is wired.
- `app.css` has a `.btn-success` rule using `var(--success-color)`.
- Add button is `btn-success btn-sm`; Dismiss × button is `btn-danger btn-sm`.
- `@key="{ip}:{port}"` on the row foreach.
- CHANGELOG bullet under `[Unreleased] ### Changed`.
- All 225 existing tests pass (no code paths they exercise should have changed).
- Manual QA scenarios 1-8 from §5 all pass.
