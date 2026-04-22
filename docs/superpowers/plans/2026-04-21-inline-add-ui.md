# Inline Add UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the modal Add flow from the Discovered panel with per-row inline editable fields. Rows become live forms. Clicking Add saves directly from the row — no modal round-trip. Manual Add Device and Edit still use the existing `DeviceForm.razor` modal.

**Architecture:** New `DiscoveredPanelRow.razor` component owns per-row state (Name, Type, Port, MAC, Serial, PIN) and runs its own ADB probes on mount. Parent `DiscoveredPanel.razor` delegates row rendering to this component and gains three new header columns. `DeviceManagement.razor` swaps `AddFromDiscovery` (deleted) for `HandleInlineAdd` that unpacks an `InlineAddPayload` into the existing `SaveDevice` flow. Button color semantics gain a new `.btn-success` CSS rule.

**Tech Stack:** Blazor Server (.NET 9), custom CSS theme tokens (`--success-color`, `--danger-color`). Existing patterns: `@bind` on form inputs, `EventCallback<T>` parameters, `@key` for list identity, per-component `OnInitializedAsync` for load-time work, `Task.WhenAll` parallel probes.

**Spec:** `docs/superpowers/specs/2026-04-21-inline-add-ui-design.md` (commit `b86b780`).

**Branch:** `master`. Each task commits directly.

**Working directory:** `C:/Users/jscha/source/repos/tools-menu/`

**Verification baseline:** 225 tests passing at HEAD `b86b780`.

---

## File Structure

### Files to create

| File | Role |
|---|---|
| `src/ControlMenu/Services/Network/InlineAddPayload.cs` | Public record carrying the row's Device + PIN + Source reference from the row component to the page. |
| `src/ControlMenu/Components/Shared/Scanner/DiscoveredPanelRow.razor` | Per-row Razor component: renders a `<tr>` with 9 cells (summary + 6 editable fields + actions), owns local state, runs on-mount ADB probes, emits `OnAdd` / `OnDismiss`. |

### Files to modify

| File | What changes |
|---|---|
| `src/ControlMenu/wwwroot/css/app.css` | Add `.btn-success` rule using `var(--success-color)` next to the existing `.btn-danger` rule. |
| `src/ControlMenu/Components/Shared/Scanner/DiscoveredPanel.razor` | Header gains 3 columns (Type, Serial, PIN). Body foreach delegates to `<DiscoveredPanelRow @key="...">`. `NameFor` helper is removed (moves into the row). `OnAdd` parameter type changes to `EventCallback<InlineAddPayload>`. |
| `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor` | `AddFromDiscovery(DiscoveredDevice)` deleted entirely. New `HandleInlineAdd(InlineAddPayload)` that sets `_formDevice` + `_formPin` from the payload and delegates to the existing `SaveDevice`. Panel element binding updated. |
| `CHANGELOG.md` | Bullet under `[Unreleased] ### Changed`. |

### No new tests

Per spec §5: manual verification only. Razor code path has no existing xUnit coverage, bUnit wouldn't earn its complexity for a single component.

---

## Task Ordering Rationale

Five tasks, ordered so every intermediate commit leaves the app compilable + functional:

1. **T1 — `InlineAddPayload` record.** New type. Compiles by itself. Unused — zero functional effect.
2. **T2 — `.btn-success` CSS rule.** Pure CSS addition. Unused selectors don't break anything.
3. **T3 — `DiscoveredPanelRow` component.** New Razor file, compiled but not referenced by any parent yet. App behavior unchanged.
4. **T4 — The atomic swap.** Panel delegates rows to the new component, header grows, page's `AddFromDiscovery` gets replaced with `HandleInlineAdd`. **This is the one user-visible commit.** Manual smoke pauses to user after commit.
5. **T5 — CHANGELOG bullet.** Documents the now-visible change.
6. **T6 — Manual QA (user-driven).** The 8 scenarios from spec §5.

T1-T3 are each small and can't break anything. T4 is the moment of truth. T5 lands docs after behavior.

---

## Task 1: `InlineAddPayload` record

**Files:**
- Create: `src/ControlMenu/Services/Network/InlineAddPayload.cs`

Pure type definition. No tests, no behavior.

- [ ] **Step 1: Create the file**

Write `src/ControlMenu/Services/Network/InlineAddPayload.cs`:

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

- [ ] **Step 2: Build**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj --nologo -c Release
```

Expected: `0 Warning(s), 0 Error(s)`.

- [ ] **Step 3: Run tests**

```bash
dotnet test --nologo --verbosity quiet
```

Expected: `Passed: 225`. No test changes; new type is unused.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Services/Network/InlineAddPayload.cs
git commit -m "feat(scanner): InlineAddPayload record for panel row → page callback

Carries the row component's edited Device + PIN + source DiscoveredDevice
reference up to DeviceManagement's HandleInlineAdd handler (landing in
T4). Type-only commit; no consumers yet."
```

---

## Task 2: `.btn-success` CSS rule

**Files:**
- Modify: `src/ControlMenu/wwwroot/css/app.css`

Mirror the existing `.btn-danger` pattern using the theme's `--success-color` variable.

- [ ] **Step 1: Read current state**

Open `src/ControlMenu/wwwroot/css/app.css`. Find the `.btn-danger` rule (around line 151):

```css
.btn-danger { background-color: var(--danger-color); color: #fff; }
.btn-danger:hover { opacity: 0.9; }
```

- [ ] **Step 2: Insert `.btn-success` block**

Add these two rules immediately after the `.btn-danger:hover` line:

```css
.btn-success { background-color: var(--success-color); color: #fff; }
.btn-success:hover { opacity: 0.9; }
```

The surrounding region ends up as:

```css
.btn-danger { background-color: var(--danger-color); color: #fff; }
.btn-danger:hover { opacity: 0.9; }

.btn-success { background-color: var(--success-color); color: #fff; }
.btn-success:hover { opacity: 0.9; }
```

- [ ] **Step 3: Build**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj --nologo -c Release
```

Expected: `0 Warning(s), 0 Error(s)`. CSS isn't compiled but the Blazor build copies it; no errors either way.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/wwwroot/css/app.css
git commit -m "style(buttons): add .btn-success theme-token rule

Mirrors the existing .btn-danger pattern using var(--success-color).
T4's DiscoveredPanelRow uses btn-success for the Add button so the
green matches the rest of the theme rather than Bootstrap's default."
```

---

## Task 3: `DiscoveredPanelRow` component

**Files:**
- Create: `src/ControlMenu/Components/Shared/Scanner/DiscoveredPanelRow.razor`

The per-row component. Owns state, inputs, probes, emits callbacks. Not wired in yet (T4 does that) — compiles and sits unused.

- [ ] **Step 1: Create the file**

Write `src/ControlMenu/Components/Shared/Scanner/DiscoveredPanelRow.razor`:

```razor
@using ControlMenu.Data.Entities
@using ControlMenu.Data.Enums
@using ControlMenu.Modules.AndroidDevices.Services
@using ControlMenu.Services.Network
@inject IAdbService Adb
@inject ILogger<DiscoveredPanelRow> Logger

<tr>
    <td>
        <code>@Source.ServiceName</code>
        @if (!string.IsNullOrEmpty(Source.Source))
        {
            <span class="source-badge" title="Discovery source">@Source.Source</span>
        }
    </td>
    <td>
        <input class="form-control form-control-sm" @bind="_name" placeholder="Device name" />
    </td>
    <td>
        <select class="form-control form-control-sm" @bind="_type">
            @foreach (var t in Enum.GetValues<DeviceType>())
            {
                <option value="@t">@t</option>
            }
        </select>
    </td>
    <td>@Source.Ip</td>
    <td>
        <input type="number" class="form-control form-control-sm" min="1" max="65535" @bind="_port" />
    </td>
    <td>
        <input class="form-control form-control-sm" @bind="_mac" placeholder="aa-bb-cc-dd-ee-ff" />
    </td>
    <td>
        <input class="form-control form-control-sm" @bind="_serial" placeholder="serial" />
    </td>
    <td>
        <input type="password" class="form-control form-control-sm" value="@_pin" @oninput="OnPinInput" placeholder="PIN" />
    </td>
    <td class="actions">
        <button class="btn btn-success btn-sm" @onclick="HandleAdd" disabled="@(!IsValid)">Add</button>
        <button class="btn btn-danger btn-sm" @onclick="HandleDismiss" title="Dismiss — remove from this list">×</button>
    </td>
</tr>

@code {
    [Parameter, EditorRequired] public DiscoveredDevice Source { get; set; } = default!;
    [Parameter, EditorRequired] public IReadOnlyDictionary<string, string> StashedNamesByMac { get; set; } = default!;
    [Parameter, EditorRequired] public IReadOnlyList<Device> Registered { get; set; } = default!;
    [Parameter] public EventCallback<InlineAddPayload> OnAdd { get; set; }
    [Parameter] public EventCallback<DiscoveredDevice> OnDismiss { get; set; }

    private string _name = "";
    private DeviceType _type = DeviceType.AndroidPhone;
    private int _port = 5555;
    private string _mac = "";
    private string _serial = "";
    private string _pin = "";
    private bool _probeRan;

    private bool IsValid =>
        !string.IsNullOrWhiteSpace(_name) && !string.IsNullOrWhiteSpace(_mac);

    protected override Task OnInitializedAsync()
    {
        _port = Source.Port;
        _mac = Source.Mac ?? "";
        _name = ResolveInitialName();

        if (!string.IsNullOrEmpty(Source.Ip) && !_probeRan)
        {
            _probeRan = true;
            _ = RunProbesAsync();
        }
        return Task.CompletedTask;
    }

    // MAC-based initial name lookup. Priority:
    //   1. Registered device with the same MAC — restore the user's current name (edge case; Discovered typically excludes already-registered)
    //   2. Stashed name cache — restore the last user-assigned name for a re-added device
    //   3. "" — will be filled by ro.product.model probe (or user types)
    private string ResolveInitialName()
    {
        if (string.IsNullOrEmpty(Source.Mac)) return "";
        var match = Registered.FirstOrDefault(d =>
            string.Equals(d.MacAddress, Source.Mac, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match.Name;
        if (StashedNamesByMac.TryGetValue(Source.Mac, out var stashed)) return stashed;
        return "";
    }

    private async Task RunProbesAsync()
    {
        var connected = await Adb.ConnectAsync(Source.Ip, Source.Port);
        if (!connected)
        {
            Logger.LogDebug("Row probe skipped — ConnectAsync false for {Ip}:{Port}", Source.Ip, Source.Port);
            return;
        }

        var kindTask   = Adb.DetectDeviceKindAsync(Source.Ip, Source.Port);
        var modelTask  = Adb.GetPropAsync(Source.Ip, Source.Port, "ro.product.model");
        var serialTask = Adb.GetPropAsync(Source.Ip, Source.Port, "ro.serialno");
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
        // Only overwrite the default seeding value, not a user-edited type.
        if (_type == DeviceType.AndroidPhone) _type = mapped;

        var model = await modelTask;
        if (string.IsNullOrEmpty(_name) && !string.IsNullOrEmpty(model)) _name = model;

        var serial = await serialTask;
        if (string.IsNullOrEmpty(_serial) && !string.IsNullOrEmpty(serial)) _serial = serial;

        await InvokeAsync(StateHasChanged);
    }

    private void OnPinInput(ChangeEventArgs e) => _pin = e.Value?.ToString() ?? "";

    private async Task HandleAdd()
    {
        var device = new Device
        {
            Name = _name,
            Type = _type,
            AdbPort = _port,
            MacAddress = _mac,
            SerialNumber = string.IsNullOrWhiteSpace(_serial) ? null : _serial,
            LastKnownIp = Source.Ip,
            ModuleId = "android-devices"
        };
        var payload = new InlineAddPayload(Source, device, _pin);
        await OnAdd.InvokeAsync(payload);
    }

    private Task HandleDismiss() => OnDismiss.InvokeAsync(Source);
}
```

Notes on tricky details:
- `@bind="_type"` binds an enum-typed select. Blazor resolves the `<option value="@t">` string form by name. Works out-of-box for enums.
- `@bind="_port"` on `<input type="number">` handles int parse automatically.
- `@bind` defaults to on-change (blur); that's fine — `HandleAdd` reads current values so the Add click picks up typed values even if the final blur hasn't fired.
- PIN uses `@oninput` not `@bind` so keystrokes are captured immediately (matches the existing `DeviceForm.razor` PIN pattern).
- The `if (_type == DeviceType.AndroidPhone) _type = mapped;` guard only overwrites when the local value is still the default seed. If the user has manually changed the dropdown before the probe completes, their choice stays. Same pattern as current `AddFromDiscovery`.

- [ ] **Step 2: Build**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj --nologo -c Release
```

Expected: `0 Warning(s), 0 Error(s)`. The component compiles but isn't referenced anywhere yet.

If the build fails with `CS0246: The type or namespace name 'DiscoveredDevice' could not be found`, the `@using ControlMenu.Services.Network` directive at the top isn't resolving — check the file saved correctly.

If the build fails with `CS0246: The type or namespace name 'Device' could not be found`, verify `@using ControlMenu.Data.Entities` is at the top.

- [ ] **Step 3: Run tests**

```bash
dotnet test --nologo --verbosity quiet
```

Expected: `Passed: 225`. No test changes; component is not yet wired in.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Components/Shared/Scanner/DiscoveredPanelRow.razor
git commit -m "feat(scanner): DiscoveredPanelRow component for inline Add UI

Per-row Razor component: renders a full editable form for one
discovered device (Name, Type, Port, MAC, Serial, PIN) with summary
cells (Service, IP) and Add/Dismiss actions. OnInitializedAsync
seeds _port/_mac/_name from Source + stashed-name cache, then fires
fire-and-forget kind+model+serial ADB probes in parallel. Empty-check
guards on probe results prevent overwriting user edits made during
the probe window.

Add button uses .btn-success (theme-green, landed in T2); × button
uses .btn-danger (theme-red). InlineAddPayload (from T1) carries
Device + PIN + Source up to the parent's callback.

Not wired into DiscoveredPanel yet — T4 does the atomic swap."
```

---

## Task 4: Atomic swap — wire the row component + page handler

**Files:**
- Modify: `src/ControlMenu/Components/Shared/Scanner/DiscoveredPanel.razor`
- Modify: `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor`

This is the one user-visible commit. The panel stops rendering its own rows and delegates to `DiscoveredPanelRow`. The header grows by three columns. The page's `AddFromDiscovery` method is deleted; `HandleInlineAdd` replaces it.

- [ ] **Step 1: Modify `DiscoveredPanel.razor` — replace the body**

Open `src/ControlMenu/Components/Shared/Scanner/DiscoveredPanel.razor`. Current full content:

```razor
@using ControlMenu.Data.Entities
@using ControlMenu.Services.Network

@if (Discovered.Count > 0)
{
    <div class="settings-section">
        <h2>Discovered on Network</h2>
        <p>ADB-advertising devices on the local network that aren't yet registered. Click Add — IP, port, MAC, suggested name, and device type are pre-filled from the scan and an ADB probe.</p>
        <table class="data-table">
            <thead>
                <tr>
                    <th>Service</th>
                    <th>Name</th>
                    <th>IP</th>
                    <th>ADB Port</th>
                    <th>MAC</th>
                    <th style="text-align:right;">Actions</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var d in Discovered)
                {
                    <tr>
                        <td>
                            <code>@d.ServiceName</code>
                            @if (!string.IsNullOrEmpty(d.Source))
                            {
                                <span class="source-badge" title="Discovery source">@d.Source</span>
                            }
                        </td>
                        <td>@NameFor(d)</td>
                        <td>@d.Ip</td>
                        <td>@d.Port</td>
                        <td><code>@(d.Mac ?? "—")</code></td>
                        <td class="actions">
                            <button class="btn btn-primary btn-sm" @onclick="() => OnAdd.InvokeAsync(d)" disabled="@(d.Mac is null)">Add</button>
                            <button class="btn btn-secondary btn-sm" @onclick="() => OnDismiss.InvokeAsync(d)" title="Dismiss — remove from this list">×</button>
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    </div>
}

@code {
    [Parameter, EditorRequired] public IReadOnlyList<DiscoveredDevice> Discovered { get; set; } = default!;
    [Parameter, EditorRequired] public IReadOnlyDictionary<string, string> StashedNamesByMac { get; set; } = default!;
    [Parameter, EditorRequired] public IReadOnlyList<Device> Registered { get; set; } = default!;
    [Parameter] public EventCallback<DiscoveredDevice> OnAdd { get; set; }
    [Parameter] public EventCallback<DiscoveredDevice> OnDismiss { get; set; }

    // MAC-based lookup. Priority:
    //  1. Registered device — user has a live name.
    //  2. Stashed name — user deleted the device but kept the name for future rediscovery
    //     (the handler populates StashedNamesByMac during scan finalization).
    //  3. "Unknown".
    private string NameFor(DiscoveredDevice d)
    {
        if (string.IsNullOrEmpty(d.Mac)) return "Unknown";
        var match = Registered.FirstOrDefault(dev =>
            string.Equals(dev.MacAddress, d.Mac, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match.Name;
        if (StashedNamesByMac.TryGetValue(d.Mac, out var stashed)) return stashed;
        return "Unknown";
    }
}
```

Replace the ENTIRE file content with:

```razor
@using ControlMenu.Data.Entities
@using ControlMenu.Services.Network

@if (Discovered.Count > 0)
{
    <div class="settings-section">
        <h2>Discovered on Network</h2>
        <p>ADB-advertising devices on the local network that aren't yet registered. Adjust any pre-filled field (from mDNS + ADB probe) and click Add to register. × dismisses the row for this scan session.</p>
        <table class="data-table">
            <thead>
                <tr>
                    <th>Service</th>
                    <th>Name</th>
                    <th>Type</th>
                    <th>IP</th>
                    <th>ADB Port</th>
                    <th>MAC</th>
                    <th>Serial</th>
                    <th>PIN</th>
                    <th style="text-align:right;">Actions</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var d in Discovered)
                {
                    <DiscoveredPanelRow @key="@($"{d.Ip}:{d.Port}")"
                                        Source="d"
                                        StashedNamesByMac="StashedNamesByMac"
                                        Registered="Registered"
                                        OnAdd="OnAdd"
                                        OnDismiss="OnDismiss" />
                }
            </tbody>
        </table>
    </div>
}

@code {
    [Parameter, EditorRequired] public IReadOnlyList<DiscoveredDevice> Discovered { get; set; } = default!;
    [Parameter, EditorRequired] public IReadOnlyDictionary<string, string> StashedNamesByMac { get; set; } = default!;
    [Parameter, EditorRequired] public IReadOnlyList<Device> Registered { get; set; } = default!;
    [Parameter] public EventCallback<InlineAddPayload> OnAdd { get; set; }
    [Parameter] public EventCallback<DiscoveredDevice> OnDismiss { get; set; }
}
```

Notes:
- Header row grows from 6 columns to 9 (added Type, Serial, PIN).
- Body foreach loops over `Discovered` but renders `<DiscoveredPanelRow>` instead of inline `<tr>`.
- `@key="@($"{d.Ip}:{d.Port}")"` on the row component preserves per-row state across re-renders.
- `NameFor` helper is deleted — the row component owns that logic now.
- `OnAdd` parameter type changed: `EventCallback<DiscoveredDevice>` → `EventCallback<InlineAddPayload>`.
- `OnDismiss` is unchanged — still emits the `DiscoveredDevice`.
- Intro `<p>` text updated to reflect the new "edit before Add" affordance.

- [ ] **Step 2: Modify `DeviceManagement.razor` — swap `AddFromDiscovery` for `HandleInlineAdd`**

Open `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor`. Find the `<DiscoveredPanel>` element (post-T11 it's a single-line element):

```razor
<DiscoveredPanel Discovered="Handler.Discovered"
                 StashedNamesByMac="Handler.StashedNamesByMac"
                 Registered="_devices"
                 OnAdd="AddFromDiscovery"
                 OnDismiss="Handler.Dismiss" />
```

Change `OnAdd="AddFromDiscovery"` to `OnAdd="HandleInlineAdd"`:

```razor
<DiscoveredPanel Discovered="Handler.Discovered"
                 StashedNamesByMac="Handler.StashedNamesByMac"
                 Registered="_devices"
                 OnAdd="HandleInlineAdd"
                 OnDismiss="Handler.Dismiss" />
```

- [ ] **Step 3: Delete `AddFromDiscovery` method entirely**

Find the `AddFromDiscovery(DiscoveredDevice d)` method in the `@code { }` block (post-A2 it's around lines 395-460). Delete the ENTIRE method, including:
- The method declaration
- All probe setup and awaits
- The kind/model/serial pull + mapped switch
- The Name / SerialNumber / Type auto-fill blocks
- The `if (changed) StateHasChanged();` tail
- Any leading comment block that introduces the method

The whole method disappears; nothing replaces it inline.

- [ ] **Step 4: Add `HandleInlineAdd` method**

In the same `@code { }` block, add this new method (put it where `AddFromDiscovery` used to live — roughly between `DeleteDevice` and `QuickRefresh`):

```csharp
    // Called when a DiscoveredPanelRow fires its OnAdd after the user clicks
    // the row's inline Add button. Unpacks the payload into _formDevice/_formPin
    // and delegates to the existing SaveDevice path — which handles MAC
    // normalization, AddDeviceAsync persistence, PIN secret-storage, and the
    // "drop added MAC from Handler.Discovered" filter.
    private async Task HandleInlineAdd(InlineAddPayload payload)
    {
        _formDevice = payload.Device;
        _formPin = payload.Pin;
        _isEditing = false;
        await SaveDevice();
    }
```

- [ ] **Step 5: Verify using block**

Confirm the top of `DeviceManagement.razor` already has `@using ControlMenu.Services.Network`. Post-T10 + T11 of the scanner extraction, it does (line 2 or thereabouts). `InlineAddPayload` lives in that namespace, so no new directive is required.

If for some reason the using is missing, add it. Otherwise `HandleInlineAdd`'s parameter type won't resolve.

- [ ] **Step 6: Build**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj --nologo -c Release
```

Expected: `0 Warning(s), 0 Error(s)`.

Likely failures and remedies:
- `CS0246: The type or namespace name 'AddFromDiscovery'` — you deleted it but a reference was missed. Grep for `AddFromDiscovery` in the file and remove.
- `CS1503: Argument ... cannot convert from 'method group' to 'EventCallback<DiscoveredDevice>'` — panel's `OnAdd` type is now `InlineAddPayload`; the page must bind `HandleInlineAdd`, not `AddFromDiscovery`.
- `CS0246: DiscoveredPanelRow` — T3 file didn't save. Retry T3.

- [ ] **Step 7: Run tests**

```bash
dotnet test --nologo --verbosity quiet
```

Expected: `Passed: 225`. No tests exercise the UI path.

- [ ] **Step 8: DO NOT perform manual smoke test**

The plan includes user-driven manual QA as T6. The implementer stops at the commit. The controller (me) will pause for the user.

- [ ] **Step 9: Commit**

```bash
git add src/ControlMenu/Components/Shared/Scanner/DiscoveredPanel.razor src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor
git commit -m "feat(scanner): inline Add UI replaces modal for Discovered rows

DiscoveredPanel header grows three columns (Type, Serial, PIN) and its
body delegates rows to DiscoveredPanelRow with @key='ip:port' preserving
per-row edit state across re-renders. OnAdd's payload type switches from
DiscoveredDevice to InlineAddPayload (Source + Device + Pin).

DeviceManagement.AddFromDiscovery is deleted — probe/form logic moved
into the row component. New HandleInlineAdd unpacks the payload into
_formDevice + _formPin and delegates to the existing SaveDevice flow,
preserving MAC normalization, AddDeviceAsync persistence, PIN
secret-storage, and the 'drop added MAC from Handler.Discovered' filter.

Manual Add Device button and Edit on registered devices still use
DeviceForm modal — A3 scope is the Discovered-panel path only.

Add button is theme-green (btn-success, landed in T2), × button is
theme-red (btn-danger). Visual upgrade from the previous blue/gray
treatment."
```

---

## Task 5: CHANGELOG update

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Append bullet to `[Unreleased] ### Changed`**

Open `CHANGELOG.md`. Find the `## [Unreleased]` section and its `### Changed` subsection. Append this bullet at the end of the existing `### Changed` bullets (before the next subsection like `### Removed`):

```markdown
- **Discovered panel inline Add** — rows now contain editable fields (Name, Type, Port, MAC, Serial, PIN) populated by an on-mount ADB probe. Clicking Add saves directly from the row — the per-device modal dialog is gone for this path. Manual Add Device and Edit still use the modal. Add button is theme-green; × button is theme-red (new `.btn-success` CSS rule mirrors the existing `.btn-danger` pattern using `var(--success-color)`).
```

- [ ] **Step 2: Commit**

```bash
git add CHANGELOG.md
git commit -m "docs(changelog): inline Add UI for Discovered panel"
```

---

## Task 6: Manual QA (user-driven)

Eight scenarios from spec §5. No code changes. Run through in the browser.

- [ ] **QA-1: Probe populates fields.** Start a scan, new row appears with empty Name/Type/Serial fields. Within ~1 second, probe completes and fields fill in (Name from `ro.product.model`, Type from `DetectDeviceKindAsync`, Serial from `ro.serialno`).

- [ ] **QA-2: User override during probe window.** Start a scan, new row appears. User immediately types a custom Name while the probe is still in flight. Probe completes; the user's typed value is preserved (empty-check guard held).

- [ ] **QA-3: Add click saves everything.** User clicks Add on a populated row. Toast confirms "Device added successfully." Device appears in the registered table with Name, Type, Port, MAC, Serial values. Click Edit on the device; Serial field shows the value (round-trip works — A2-fix ensures ShowEditForm clone carries Serial). PIN — if typed — is stored; verify by re-opening Edit and seeing the PIN input populate.

- [ ] **QA-4: Add disabled when invalid.** Row with empty Name or empty MAC: Add button stays disabled. Fill in both: button enables.

- [ ] **QA-5: Dismiss removes row.** Click × on a row; row disappears. Same address is recorded in the handler's dismissed set; a fresh scan doesn't re-add it (QuickRefresh merge fix from T13-fix).

- [ ] **QA-6: Multiple live rows independent.** Scan finds 3 devices. User types into row B's Name field. Scanner emits another hit (row D appears). Rows A/B/C keep their values; row D appears with its own probe in flight. Proves `@key="ip:port"` works.

- [ ] **QA-7: Color theme.** Add button renders green, × button renders red. Toggle theme (sun/moon icon); both remain legible in light AND dark modes.

- [ ] **QA-8: Manual Add Device still works.** Click the Add Device button above the devices table. `DeviceForm` modal opens with empty fields. User fills in manually (no probe — there's no Discovered row to probe from). Save. Device appears in registered table. Also: click Edit on any registered device; modal opens with all fields populated. No regression in the non-Discovered flows.

- [ ] **Final: update memory.** Mark A3 as shipped in `project_cm_inline_add_ui.md` with commit SHAs. Update MEMORY.md index line.

---

## Self-Review Checklist

Completed during plan authoring:

- **Spec coverage.**
  - Decision 1 (L1 wide row): T4 Step 1 adds 3 columns to header; T3 row renders all 9 cells. ✓
  - Decision 2 (P1 probe on mount): T3 `OnInitializedAsync` + `RunProbesAsync`. ✓
  - Decision 3 (scope: Discovered only): T4 leaves `DeviceForm.razor` untouched; manual Add + Edit continue to use it. ✓
  - Decision 4 (PIN always visible): T3 renders PIN input unconditionally. ✓
  - Decision 5 (`DiscoveredPanelRow` extracted): T3 creates the component. ✓
  - Decision 6 (`@key="ip:port"`): T4 Step 1 panel markup. ✓
  - Decision 7 (button colors + `.btn-success`): T2 adds CSS rule; T3 uses `btn-success` + `btn-danger`. ✓
  - Decision 8 (manual tests only, no new xUnit): no test project changes in any task. ✓
  - DoD items — all 8 covered across T1-T4.

- **Placeholder scan.** No "TBD" / "TODO" / "implement later" / "add validation" / "handle edge cases". Every step shows the exact code or exact command. Expected outputs are stated. T4 Step 6 lists three common build failure modes with remedies (not speculative — these are the compiler errors a botched swap would emit).

- **Type consistency.** `InlineAddPayload(DiscoveredDevice Source, Device Device, string Pin)` defined in T1, consumed in T3 (`new InlineAddPayload(Source, device, _pin)`) and T4 (`private async Task HandleInlineAdd(InlineAddPayload payload)`). `_formDevice`, `_formPin`, `_isEditing` all preserved at their existing types from pre-A3 DeviceManagement. `EventCallback<InlineAddPayload>` used consistently on both panel and row components.

- **Task ordering.** T1 (type) → T2 (CSS) → T3 (component, unused) → T4 (atomic swap, visible) → T5 (docs) → T6 (QA). T1-T3 each commit leaves the app functionally unchanged; T4 is the one-shot visible change.
