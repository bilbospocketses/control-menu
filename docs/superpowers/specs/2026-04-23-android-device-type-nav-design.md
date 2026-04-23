# Android device-type-aware navigation and dashboards

**Date:** 2026-04-23
**Status:** Design — ready for implementation plan
**Scope:** Control Menu Android Devices module
**Related:** `todo_control_menu.md` items A1 and A4

---

## Motivation

`AndroidDevicesModule.GetNavEntries()` currently ships three hardcoded sidebar entries: Device List, Google TV, Android Phone. The `DeviceType` enum was extended on 2026-04-18 to include `AndroidTablet` and `AndroidWatch`, but no corresponding dashboards or nav entries exist. Users can register tablet or watch devices through the scanner, but have nowhere to interact with them.

Beyond that, the hardcoded nav is wrong even for the existing entries: a fresh install with zero devices still shows "Google TV" and "Android Phone" nav links that lead to empty-state pages. The sidebar should reflect the user's actual device inventory.

The Setup Wizard has a second, related problem. Its Devices step (`WizardDevices.razor`) was built before the network scanner existed and duplicates a hand-rolled Name/Type/Port/MAC form. That form is now redundant with the `DiscoveredPanel` scanner UI shipped on 2026-04-21.

This spec covers A1 (Tablet + Watch dashboards + dynamic nav) and A4 (wizard Devices step rework) together, since they share device-type-awareness infrastructure.

---

## Scope

**In scope:**
- Two new device dashboards: `TabletDashboard.razor` and `WatchDashboard.razor`, each a near-clone of `PixelDashboard.razor`.
- Dynamic sidebar filtering — nav entries for device types hide when no device of that type is registered, appear as soon as the first device of that type is added.
- Auto-redirect from a dashboard route to `/android/devices` when the last device of that type is deleted.
- Rewritten Setup Wizard Devices step using the existing scanner panel instead of the hand-rolled form.
- Five custom SVG icons for the Android Devices sidebar group (`device-list.svg`, `smart-tv.svg`, `smart-phone.svg`, `tablet.svg`, `smart-watch.svg`) replacing the current emoji + clipboard icons.

**Out of scope:**
- Cross-tab / cross-browser real-time sync. Reactivity is scoped to a single Blazor circuit.
- Watch-specific UX divergence from the Phone dashboard. The Watch dashboard ships as a Phone clone; device-specific refinements are a separate follow-up.
- Automatic "pick another device of same type" when the current `DeviceId` is deleted but others of the same type still exist. The existing "No device selected" fallback page is acceptable.
- Changes to `PixelDashboard.razor`, `/android/phone`, or `/android/pixel` behavior beyond composing the new `DeviceTypePresenceWatcher`.
- Changes to the setup wizard's other steps (Welcome, Cameras, Jellyfin, Email, Dependencies, Done).

---

## Design

### 1. Architecture overview

The feature splits into five units, each with one clear responsibility:

1. **`NavEntry.IsVisible` predicate** — optional `Func<IServiceProvider, bool>` on the cross-cutting `NavEntry` record. Evaluated by the Sidebar only; modules remain stateless and parameterless.
2. **`IDeviceService.DevicesChanged` event** — raised after every mutation through the service. One event source for all observers.
3. **`IDeviceTypeCache` scoped service** — owns the "which device types are present" fact. Subscribes to `DevicesChanged`, caches results from `GetAllDevicesAsync()`, exposes a synchronous `HasDevicesOfType(DeviceType)` query.
4. **Sidebar reactive flow** — primes the cache on mount, subscribes to `CacheUpdated`, maintains a per-entry visibility cache that's cleared on every update, re-renders.
5. **Device dashboards + `DeviceTypePresenceWatcher`** — Tablet and Watch dashboards are near-clones of Phone. Each dashboard (including Phone) composes a watcher that redirects to `/android/devices` if zero devices of the dashboard's type exist.

Plus the A4 wizard rewrite (embeds the existing scanner panel) and the icon asset integration (5 SVG files + a new Sidebar rendering branch).

**Flow when a user adds the first tablet:**
```
Scanner.AddDevice()
  → IDeviceService.AddDeviceAsync()
  → DevicesChanged event fires
  → IDeviceTypeCache re-queries, updates _typesPresent, raises CacheUpdated
  → Sidebar clears _visibilityCache, re-renders
  → AndroidTablet nav entry appears; no page reload
```

**Flow when a user deletes the last phone while on `/android/phone`:**
```
DeviceList.Delete()
  → IDeviceService.DeleteDeviceAsync()
  → DevicesChanged event fires
  → [Sidebar side] cache updates, AndroidPhone entry filtered out, re-renders
  → [PixelDashboard side] DeviceTypePresenceWatcher re-queries, finds 0 phones,
    NavigateTo("/android/devices", replace: true)
```

### 2. Data and contracts

**`NavEntry` record** (`Modules/NavEntry.cs`) gains an optional predicate:

```csharp
public record NavEntry(
    string Title,
    string Href,
    string? Icon = null,
    int SortOrder = 0,
    Func<IServiceProvider, bool>? IsVisible = null);
```

Backward compatible — every existing entry in every module that omits the parameter defaults to always-visible.

**`IDeviceService`** (`Services/IDeviceService.cs`) gains one event:

```csharp
public interface IDeviceService
{
    Task<IReadOnlyList<Device>> GetAllDevicesAsync();
    Task<Device?> GetDeviceAsync(Guid id);
    Task<Device> AddDeviceAsync(Device device);
    Task UpdateDeviceAsync(Device device);
    Task DeleteDeviceAsync(Guid id);
    Task UpdateLastSeenAsync(Guid id, string ipAddress);

    event Action DevicesChanged;
}
```

Implementation raises `DevicesChanged` exactly once after `AddDeviceAsync`, `UpdateDeviceAsync`, and `DeleteDeviceAsync` succeed. `UpdateLastSeenAsync` does NOT raise the event — IP changes happen frequently during polling and would create render noise. The event has no args: subscribers treat it as a "something changed, re-query" signal.

**`IDeviceTypeCache`** (new: `Services/IDeviceTypeCache.cs`) — scoped wrapper:

```csharp
public interface IDeviceTypeCache
{
    bool HasDevicesOfType(DeviceType t);
    event Action? CacheUpdated;
    Task RefreshAsync();
}

internal sealed class DeviceTypeCache : IDeviceTypeCache, IDisposable
{
    private readonly IDeviceService _deviceService;
    private readonly ReaderWriterLockSlim _lock = new();
    private HashSet<DeviceType> _typesPresent = new();

    public event Action? CacheUpdated;

    public DeviceTypeCache(IDeviceService deviceService)
    {
        _deviceService = deviceService;
        _deviceService.DevicesChanged += OnDevicesChanged;
    }

    public bool HasDevicesOfType(DeviceType t)
    {
        _lock.EnterReadLock();
        try { return _typesPresent.Contains(t); }
        finally { _lock.ExitReadLock(); }
    }

    public async Task RefreshAsync()
    {
        var devices = await _deviceService.GetAllDevicesAsync();
        var newSet = devices.Select(d => d.Type).ToHashSet();
        _lock.EnterWriteLock();
        try { _typesPresent = newSet; }
        finally { _lock.ExitWriteLock(); }
        CacheUpdated?.Invoke();
    }

    private async void OnDevicesChanged()
    {
        try { await RefreshAsync(); }
        catch (Exception ex) { /* log and swallow — async void event handler */ }
    }

    public void Dispose() => _deviceService.DevicesChanged -= OnDevicesChanged;
}
```

Registered scoped:
```csharp
// Program.cs
builder.Services.AddScoped<IDeviceTypeCache, DeviceTypeCache>();
```

**`AndroidDevicesModule.GetNavEntries()`** updated:

```csharp
public IEnumerable<NavEntry> GetNavEntries() =>
[
    new NavEntry("Device List",    "/android/devices",  "/images/devices/device-list.svg", 0),
    new NavEntry("Google TV",      "/android/googletv", "/images/devices/smart-tv.svg",    1, HasDevicesOfType(DeviceType.GoogleTV)),
    new NavEntry("Android Phone",  "/android/phone",    "/images/devices/smart-phone.svg", 2, HasDevicesOfType(DeviceType.AndroidPhone)),
    new NavEntry("Android Tablet", "/android/tablet",   "/images/devices/tablet.svg",      3, HasDevicesOfType(DeviceType.AndroidTablet)),
    new NavEntry("Android Watch",  "/android/watch",    "/images/devices/smart-watch.svg", 4, HasDevicesOfType(DeviceType.AndroidWatch)),
];

private static Func<IServiceProvider, bool> HasDevicesOfType(DeviceType t) =>
    sp => sp.GetRequiredService<IDeviceTypeCache>().HasDevicesOfType(t);
```

**Scope caveat:** `IDeviceTypeCache` is scoped per Blazor circuit, as is `IDeviceService`. Reactivity works within a single browser tab. Cross-tab real-time sync is out of scope (see Scope section).

### 3. Sidebar reactive flow

`Components/Layout/Sidebar.razor` additions:

```razor
@inject ModuleDiscoveryService ModuleDiscovery
@inject IJSRuntime JS
@inject IDeviceTypeCache DeviceTypeCache
@inject IServiceProvider ServiceProvider
@implements IDisposable
```

```csharp
@code {
    private readonly Dictionary<NavEntry, bool> _visibilityCache = new();

    protected override async Task OnInitializedAsync()
    {
        await DeviceTypeCache.RefreshAsync();
        DeviceTypeCache.CacheUpdated += OnCacheUpdated;
    }

    private async void OnCacheUpdated()
    {
        await InvokeAsync(() =>
        {
            _visibilityCache.Clear();
            StateHasChanged();
        });
    }

    private IEnumerable<NavEntry> VisibleEntries(IToolModule module) =>
        module.GetNavEntries()
              .Where(entry => entry.IsVisible is null
                              || _visibilityCache.GetValueOrDefault(entry, EvaluateVisibility(entry)))
              .OrderBy(e => e.SortOrder);

    private bool EvaluateVisibility(NavEntry entry)
    {
        var visible = entry.IsVisible!.Invoke(ServiceProvider);
        _visibilityCache[entry] = visible;
        return visible;
    }

    public void Dispose() => DeviceTypeCache.CacheUpdated -= OnCacheUpdated;
}
```

Razor markup — one line change in the existing module-group loop:

```razor
@foreach (var entry in VisibleEntries(module))  // was: module.GetNavEntries().OrderBy(e => e.SortOrder)
{
    <NavLink ...>
}
```

**Timing and threading:**
- `OnInitializedAsync` runs once per circuit before the first render; cache is populated before any predicate is evaluated.
- `CacheUpdated` may fire from a non-UI thread (the async void handler in `DeviceTypeCache`). `InvokeAsync(...)` marshals back to the Blazor render context.
- `_visibilityCache` is cleared on every event, so predicates re-evaluate once per change and then get cached for reuse across renders until the next change.

**Collapsed-sidebar interaction:** none. The existing `Collapsed` flag gates label visibility; the filter is orthogonal.

### 4. Tablet and Watch dashboards

Two new files in `Modules/AndroidDevices/Pages/`:
- `TabletDashboard.razor`
- `WatchDashboard.razor`

Each is a near-copy of `PixelDashboard.razor` with six targeted differences:

| Aspect | Phone (existing) | Tablet | Watch |
|---|---|---|---|
| `@page` directives | `/android/phone`, `/android/pixel`, guid variants | `/android/tablet`, guid variant | `/android/watch`, guid variant |
| `PageTitle` prefix | `Android Phone` | `Android Tablet` | `Android Watch` |
| `<h1>` icon + text | `bi-phone` / "Android Phone" | `bi-tablet-landscape` / "Android Tablet" | `bi-smartwatch` / "Android Watch" |
| First-device fallback filter | `d.Type == DeviceType.AndroidPhone` | `d.Type == DeviceType.AndroidTablet` | `d.Type == DeviceType.AndroidWatch` |
| Unlock button label | "Unlock Phone" | "Unlock Tablet" | "Unlock Watch" |
| `ScrcpyMirror.DeviceKind` attribute | `phone` | `tablet` | `watch` |

Everything else (Quick Actions panel, ADB Connect row, mirror panel, status bar, CSS layout) is identical. No Unlock-action logic changes; the label is per-dashboard but the underlying codepath is shared and device-type-agnostic.

**Tablet/Watch `OnInitializedAsync` addition (also added to Phone for symmetry):**

```csharp
private DeviceTypePresenceWatcher? _presenceWatcher;

protected override async Task OnInitializedAsync()
{
    _presenceWatcher = new DeviceTypePresenceWatcher(
        DeviceType.AndroidTablet,          // per-file: Phone / Tablet / Watch
        DeviceService,
        Nav,
        () => InvokeAsync(StateHasChanged));

    if (await _presenceWatcher.EnsurePresentOrRedirectAsync())
        return;

    // ... existing device-loading code unchanged
}

public void Dispose()
{
    _presenceWatcher?.Dispose();
    // ... existing dispose logic unchanged
}
```

**Watch caveat:** no test hardware available per `user_test_devices.md`. Watch dashboard ships as verified-by-parity-with-Phone only. Release note: *"Android Watch dashboard is untested on real hardware; please report any issues."* Add a matching note to `docs/TECHNICAL_GUIDE.md`.

### 5. `DeviceTypePresenceWatcher`

New file: `Modules/AndroidDevices/Services/DeviceTypePresenceWatcher.cs`.

Single-responsibility disposable. Each dashboard composes with it; it does one thing (redirect when the dashboard's device type has no instances) and nothing more.

```csharp
public sealed class DeviceTypePresenceWatcher : IDisposable
{
    private readonly DeviceType _type;
    private readonly IDeviceService _deviceService;
    private readonly NavigationManager _nav;
    private readonly Func<Task>? _onInvalidateAsync;
    private bool _redirected;

    public DeviceTypePresenceWatcher(
        DeviceType type,
        IDeviceService deviceService,
        NavigationManager nav,
        Func<Task>? onInvalidateAsync = null)
    {
        _type = type;
        _deviceService = deviceService;
        _nav = nav;
        _onInvalidateAsync = onInvalidateAsync;
        _deviceService.DevicesChanged += OnDevicesChanged;
    }

    /// Returns true if redirected (caller should abort its init).
    public async Task<bool> EnsurePresentOrRedirectAsync()
    {
        var devices = await _deviceService.GetAllDevicesAsync();
        if (!devices.Any(d => d.Type == _type))
        {
            _redirected = true;
            _nav.NavigateTo("/android/devices", replace: true);
            return true;
        }
        return false;
    }

    private async void OnDevicesChanged()
    {
        if (_redirected) return;
        try
        {
            var devices = await _deviceService.GetAllDevicesAsync();
            if (!devices.Any(d => d.Type == _type))
            {
                _redirected = true;
                _nav.NavigateTo("/android/devices", replace: true);
            }
            else if (_onInvalidateAsync is not null)
            {
                await _onInvalidateAsync();
            }
        }
        catch (Exception ex) { /* log + swallow */ }
    }

    public void Dispose() => _deviceService.DevicesChanged -= OnDevicesChanged;
}
```

**Responsibilities it explicitly does not take:**
- Does not handle "the specific `DeviceId` in the URL was deleted while other devices of type still exist" — the dashboard's existing "No device selected" fallback handles that case.
- Does not manage the dashboard's own state (device fetching, connection status).
- Does not coordinate with the sidebar cache; both are independent observers of the same `DevicesChanged` event.

**`_redirected` flag:** defensive guard against rapid repeated events causing double-navigate.

### 6. Wizard Devices step rewrite (A4)

Full rewrite of `Components/Pages/Setup/WizardDevices.razor`.

**Structure:**

```razor
@inject IDeviceService DeviceService

<div class="settings-section">
    <h2>Android Devices</h2>
    <p>Scan your network to discover Android devices on the same Wi-Fi, then add each one with a click. You can also add devices later from <a href="/settings/devices">Settings › Devices</a>.</p>

    <DiscoveredPanel />

    @if (_devices.Count > 0)
    {
        <h3 style="margin-top: 1.5rem;">Registered devices</h3>
        <table class="data-table">
            <thead>
                <tr><th>Name</th><th>Type</th><th>MAC</th><th>Port</th></tr>
            </thead>
            <tbody>
                @foreach (var device in _devices)
                {
                    <tr>
                        <td>@device.Name</td>
                        <td>@device.Type</td>
                        <td><code>@device.MacAddress</code></td>
                        <td>@device.AdbPort</td>
                    </tr>
                }
            </tbody>
        </table>
    }
</div>

@code {
    [Parameter] public SetupWizard.WizardState State { get; set; } = default!;
    private List<Device> _devices = [];

    protected override async Task OnInitializedAsync()
    {
        await RefreshAsync();
        DeviceService.DevicesChanged += OnDevicesChanged;
    }

    private async void OnDevicesChanged() => await InvokeAsync(RefreshAsync);

    private async Task RefreshAsync()
    {
        _devices = (await DeviceService.GetAllDevicesAsync()).ToList();
        State.DevicesAdded = _devices.Count;
        StateHasChanged();
    }

    public void Dispose() => DeviceService.DevicesChanged -= OnDevicesChanged;
}
```

**Key properties:**
- `<DiscoveredPanel />` is the existing scanner component (shipped 2026-04-21, see `project_control_menu_scanner_port.md`). It already owns: Scan Network trigger, discovered-row rendering, inline add UI (A3 2026-04-21), probe fallbacks, status indicators.
- No local form state, no custom add handler — the wizard step is now a thin wrapper around the panel plus a read-only list of what's been added so far.
- `State.DevicesAdded` kept for downstream wizard-step checks. Refresh-on-event pattern ensures the counter stays accurate.
- Remove button removed — additions are scan-driven in onboarding, removal belongs in Settings › Devices after setup.

**`SetupWizard.razor` is unchanged** — the `WizardStep.Devices` case still renders `<WizardDevices State="_state" />`.

**Implementation-time verification tasks** (not design blockers, but confirm during implementation):
1. `DiscoveredPanel`'s public surface — parameters it accepts, cascading values it reads. If tightly coupled to its current parent, a thin wrapper or new parameters may be needed.
2. Panel styling inside the wizard content area (`max-width: 960px`, 1.5rem padding). May need a wizard-specific CSS class if layout assumptions break.
3. Scanner initial-state behavior — does it auto-scan on mount or wait for a button click? Auto-scan is nicer for onboarding; if it currently waits, consider passing an `AutoScan="true"` parameter or adding one.

**Fallback plan** if `DiscoveredPanel` turns out to be tightly coupled to the settings page: drop to Q1 path (c) — a "skip-or-scan" landing page with a "Run Scanner" button that opens a modal or navigates to settings. Unlikely given A3's isolation work, but flagged.

### 7. Icon assets integration

Five SVG files move from `C:\Temp\tablets\` into the repo's static assets:

```
src/ControlMenu/wwwroot/images/devices/
├── device-list.svg    (Device List — list rows with thumbnails)
├── smart-tv.svg       (Google TV — glossy-black bezel, abstract blue screen content, reflection bands)
├── smart-phone.svg    (Android Phone — glossy-black body, camera dot, app grid, reflection bands)
├── tablet.svg         (Android Tablet — glossy-black bezel, app grid, reflection bands)
└── smart-watch.svg    (Android Watch — glossy-black body, dark-grey straps, app grid, reflection bands)
```

All five share a consistent visual language: glossy-black bezel gradients (where applicable), `#1296f9` primary blue screens, diagonal white reflection bands clipped to screen boundaries. Each uses a tight viewBox matching its natural aspect ratio — no transparent padding.

**Sidebar rendering branch** (`Components/Layout/Sidebar.razor`):

```razor
<NavLink class="sidebar-link" href="@entry.Href" Match="NavLinkMatch.All">
    @if (entry.Icon is not null)
    {
        @if (entry.Icon.StartsWith("bi-"))
        {
            <i class="bi @entry.Icon"></i>
        }
        else if (entry.Icon.StartsWith("/") || entry.Icon.EndsWith(".svg"))
        {
            <img src="@entry.Icon" alt="" class="sidebar-nav-icon" />
        }
        else
        {
            <span class="nav-emoji">@entry.Icon</span>
        }
    }
    <span>@entry.Title</span>
</NavLink>
```

**New CSS** (`Sidebar.razor.css`):

```css
.sidebar-nav-icon {
    width: 1.25em;
    height: 1.25em;
    object-fit: contain;
    flex-shrink: 0;
}
```

`em`-based sizing matches the bi-icon glyph size (scales with link font size). `object-fit: contain` preserves each SVG's natural aspect without distortion; letterboxing happens in the CSS box only, not baked into the SVG file.

**Build pipeline:** unchanged. SVG files in `wwwroot/images/` are served directly by ASP.NET static file middleware.

**Dark-mode verification:** the glossy-black icons read correctly against a dark sidebar (the rim stroke + gradient stops create edge contrast). The `device-list.svg` icon, with lighter blue/gray content, may have reduced contrast in some themes — verify during manual testing.

### 8. Testing strategy

**New unit tests:**

`DeviceTypeCacheTests.cs` — 6 tests:
1. `HasDevicesOfType` returns false for any type before `RefreshAsync` (empty cache).
2. After `RefreshAsync` with 2 phones + 1 tablet, `HasDevicesOfType` returns true for Phone and Tablet, false for GoogleTV and Watch.
3. `DevicesChanged` from `IDeviceService` triggers a re-read and raises `CacheUpdated`.
4. `CacheUpdated` fires exactly once per `DevicesChanged`.
5. After the last device of a type is deleted and `DevicesChanged` fires, `HasDevicesOfType` returns false on the next query.
6. `Dispose()` unsubscribes — a subsequent `DevicesChanged` from the mocked service does not cause re-read or `CacheUpdated`.

`DeviceTypePresenceWatcherTests.cs` — 6 tests:
1. `EnsurePresentOrRedirectAsync` with no devices of type: returns true, calls `NavigateTo("/android/devices", replace: true)` once.
2. `EnsurePresentOrRedirectAsync` with ≥ 1 device of type: returns false, no navigation.
3. `DevicesChanged` with last device of type just deleted: navigates exactly once.
4. `DevicesChanged` with other devices of type still present: invokes `onInvalidateAsync`, no navigation.
5. `DevicesChanged` after already redirected: `_redirected` flag prevents double-navigate.
6. `Dispose()` unhooks the event.

Uses a `FakeDeviceService` (in-memory list + public `RaiseChanged()` helper) and a `FakeNavigationManager` that records `NavigateTo` calls — follows the pattern of existing scanner tests.

`NavEntryTests.cs` — 2 tests:
1. Default-constructed `NavEntry` has `IsVisible == null` (backward compatibility check).
2. `IsVisible` predicate receives the `IServiceProvider` passed by the caller.

Component tests (if bUnit is wired up, otherwise fold into manual checklist):
- Entry with `IsVisible = null` renders.
- Entry with `IsVisible = _ => false` is filtered out.
- `CacheUpdated` event clears `_visibilityCache` and triggers a re-render.

**Existing tests to update:**

`WizardDevicesTests.cs` (if present) — the form-based tests (name/type/port/MAC validation, Add button gating, RemoveDevice behavior) are obsolete. Replace with: panel renders, `State.DevicesAdded` reflects device count on init, `DevicesChanged` refreshes `_devices` and updates the counter.

**Manual test checklist additions** — new section in `docs/manual-test-checklist.md`:

Section 5e — *A1+A4 dynamic nav and dashboards*:
1. Empty-inventory start: fresh install or all devices deleted. Sidebar shows only "Device List" under Android Devices. Direct navigation to `/android/phone` → redirected to `/android/devices`.
2. First-device emergence: on Device List, scan and add an Android Phone. Sidebar updates within one render cycle to show "Android Phone" entry. Click it → `/android/phone` loads the newly added device.
3. Adding a tablet while phone exists: both "Android Phone" and "Android Tablet" entries present in sidebar.
4. Last-device deletion while on its dashboard: be on `/android/tablet`, delete the only tablet from another browser tab or via the scanner. Current tab auto-redirects to `/android/devices`.
5. Watch dashboard cold load: register a watch device, navigate to `/android/watch`. Dashboard renders with "Unlock Watch" label. No hardware test possible; confirm page loads and `ScrcpyMirror` embed is wired correctly.
6. Wizard flow: from fresh install, walk the setup wizard. On Devices step, `DiscoveredPanel` renders, Scan Network works, discovered list populates, per-row add flow works. Complete wizard → `/` → sidebar shows the added device types.
7. Collapsed sidebar: toggle collapsed. Device type entries still render as icons (no text). Click the Android Phone SVG icon → navigates correctly.
8. Icon visual check in both themes: verify custom SVG icons render correctly in light and dark mode. Flag contrast issues.

**Regression checklist — do NOT break:**
- Existing sidebar group expand/collapse and localStorage persistence.
- Existing `ModuleImageMap` module-level `<img>` icons (android-devices, jellyfin).
- `PixelDashboard.razor` behavior — cloning into `TabletDashboard` / `WatchDashboard` must not change the existing `/android/phone` or `/android/pixel` routes' output.
- `DiscoveredPanel` behavior on its existing Settings › Devices page — the wizard embedding is a second usage site and must not regress the first.

---

## Open questions and verification tasks

These are not design blockers, but items to confirm during implementation:

1. **`DiscoveredPanel`'s public surface** — parameters, cascading value reads, whether it auto-scans on mount. Determines whether the wizard embed is a one-liner or needs a thin wrapper.
2. **Whether `TvDashboard.razor` already exists** for `/android/googletv`. If it does, A1 leaves it alone. If it doesn't, Google TV nav visibility depends on a dashboard that doesn't yet exist — flag and decide during planning.
3. **`sidebar-link` font size** — the `1.25em` icon sizing assumes a reasonable default. If bi-icons use a hardcoded size, match it.
4. **Bi-icon availability** — `bi-tablet-landscape` and `bi-smartwatch` should exist in the Bootstrap Icons version the project uses. Verify against the project's `bootstrap-icons.css` reference.

---

## References

- `todo_control_menu.md` — A1 (Tablet + Watch dashboards + dynamic nav) and A4 (wizard step rework)
- `project_control_menu.md` §"Updates 2026-04-18" — `DeviceType` enum extension background
- `project_control_menu_scanner_port.md` — scanner extraction context; `DiscoveredPanel` location
- `project_cm_inline_add_ui.md` — A3 inline add UI precedent
- `project_control_menu_homepage.md` — current sidebar design baseline
- `user_test_devices.md` — hardware inventory (no watch available for testing)
