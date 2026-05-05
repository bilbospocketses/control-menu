# Sidebar Fly-out Menu — Design Spec

**Date:** 2026-05-05
**Branch:** `feature/sidebar-flyout`
**Origin TODO:** `todo_control_menu.md` item 3 (Collapsed sidebar — section-header icons are dead clicks)

## Goal

When the sidebar is collapsed (narrow rail at 56px), clicking a section-header icon (Android Devices, Android Power Tools, Cameras, Jellyfin, Utilities, etc.) opens a floating fly-out panel anchored next to the icon. The panel lists the same nav entries that would appear in the expanded sidebar for that module — including conditionally-visible device-type entries (Phone, Tablet, TV, Watch) under Android Devices.

This solves two related issues:

1. **Dead clicks** — clicking a section icon currently toggles `_expandedGroups` for that module, but the rendering is gated on `IsGroupExpanded(module.Id) && !Collapsed`, so the toggle has no visible effect when the sidebar is collapsed.
2. **Sub-link discoverability** — device-type sub-links and other module entries are only reachable by uncollapsing the sidebar, which forces a layout change for what the user wants to be a quick navigation.

The fly-out keeps the sidebar collapsed (preserving the user's spatial preference) while restoring full per-module navigability.

## Scope

In scope:

- New fly-out panel rendered next to the clicked section icon when the sidebar is collapsed.
- Click-outside, click-link, Esc-key, and uncollapse-while-open dismissal.
- One fly-out at a time; clicking another section icon swaps.
- JS interop helper to position the panel next to the clicked icon (`getBoundingClientRect().top`).
- Window-resize listener while open to recompute panel position.
- bUnit smoke test for the basic open/close flow.
- CHANGELOG entry, manual-checklist additions.

Out of scope:

- Hover preview / quick-peek triggers.
- Per-fly-out entry filtering or search.
- Touch-screen long-press dismissal.
- Animation polish beyond a basic 100ms fade-in.
- Changes to the expanded-sidebar behavior or to the Settings footer (single-nav-link, no fly-out needed).

## Behavior

### Trigger

- **Click** on a `.sidebar-group-header` icon while `Collapsed == true` opens the fly-out for that module.
- Hover does NOT trigger (deliberate; click matches existing affordance and avoids accidental triggers).
- If the module has zero visible entries (rare — `VisibleEntries(module)` returns empty), the click is a no-op (no empty panel).

### Single-instance

- Only one fly-out is open at any time.
- Clicking a different section icon closes the current panel and opens the new one in a single render pass.

### Dismissal

- **Click outside the panel** — covered by an invisible `.sidebar-flyout-backdrop` overlay covering the entire viewport (z-index between the page content and the panel). Clicking the backdrop closes the fly-out.
- **Click a link inside the panel** — `<NavLink>` navigates; the fly-out's `OnLinkClicked` handler clears state.
- **Esc key** — keydown listener on the panel (`@onkeydown`) closes when `e.Key == "Escape"`. Panel auto-focuses on render to receive the key.
- **Sidebar uncollapses** — `ToggleCollapsed` clears `_flyoutModuleId` if the new state is expanded.

### Position

- `position: fixed`.
- `top` = clicked icon's `getBoundingClientRect().top` (recomputed on window resize).
- `left: 56px` (immediately right of the collapsed sidebar; matches `.sidebar.collapsed { width: 56px }`).
- `width: 220px` (rough match to the expanded sidebar's content width, minus padding).
- `z-index: 100` (above page content; backdrop sits at z-index 99).

### Content

The fly-out renders three regions vertically:

1. **Header** — module icon (same `ModuleImageMap`-or-`bi-` resolution as the sidebar group header), module display name, optional close `×` button. Bordered bottom. Mirrors the visual weight of an expanded sidebar group header.
2. **Nav entries** — the result of `VisibleEntries(module)`, rendered with the same `<NavLink class="sidebar-link" Match="NavLinkMatch.All">` pattern used in the expanded sidebar. Active route highlighting via `NavLink` works automatically.
3. (No footer.)

## Component architecture

The fly-out lives inside `Sidebar.razor` (no new component file). Rationale: it's tightly coupled to the sidebar's state, lifetime, and DI scope — extracting it as a separate component would force prop-drilling for `ModuleDiscovery`, `_flyoutModuleId`, the click handlers, and JS interop. A new `<SidebarFlyout>` component would not be reused elsewhere; it would only be invoked from `Sidebar.razor`. Inline rendering keeps the boundary clean.

### State (new fields on `Sidebar.razor`)

```csharp
private string? _flyoutModuleId;
private double _flyoutTopPx;
private DotNetObjectReference<Sidebar>? _selfRef;  // for window-resize callback
```

### New methods

```csharp
private async Task OpenFlyout(Modules.IToolModule module, ElementReference iconRef)
{
    if (!Collapsed) return;
    if (!VisibleEntries(module).Any()) return;
    _flyoutTopPx = await JS.InvokeAsync<double>("controlMenu.getRectTop", iconRef);
    _flyoutModuleId = module.Id;
    StateHasChanged();
}

private void CloseFlyout() => _flyoutModuleId = null;

[JSInvokable]
public async Task OnWindowResize()
{
    // Recompute top for the open fly-out, if any.
    if (_flyoutModuleId is null) return;
    // We need the icon's ElementReference — captured via a Dictionary<string, ElementReference>
    // populated during the foreach render. See the implementation plan.
    // Fall back: close the fly-out on resize if recompute is impractical.
    // Implementation plan picks the simpler path that works.
}
```

The icon's `ElementReference` is captured per module via `@ref="..."` on the icon element inside the foreach. A `Dictionary<string, ElementReference> _iconRefs` holds them keyed by `module.Id`.

### Render shape (inside the @foreach loop)

```razor
<div class="sidebar-group">
    <div class="sidebar-group-header"
         @ref="_iconRefs.GetOrAdd(module.Id)"
         @onclick="@(() => Collapsed
             ? OpenFlyout(module, _iconRefs[module.Id])
             : ToggleGroup(module.Id))">
        <!-- existing icon + name + chevron rendering -->
    </div>

    @if (IsGroupExpanded(module.Id) && !Collapsed)
    {
        <!-- existing expanded sub-items rendering -->
    }
</div>
```

The render of the panel itself happens once at the end of the `<nav class="sidebar">`:

```razor
@if (Collapsed && _flyoutModuleId is not null)
{
    var module = ModuleDiscovery.Modules.FirstOrDefault(m => m.Id == _flyoutModuleId);
    if (module is not null)
    {
        <div class="sidebar-flyout-backdrop" @onclick="CloseFlyout"></div>
        <div class="sidebar-flyout"
             style="top: @(_flyoutTopPx)px;"
             tabindex="-1"
             @onkeydown="HandleFlyoutKeydown">
            <div class="sidebar-flyout-header">
                <!-- module icon + name -->
            </div>
            @foreach (var entry in VisibleEntries(module))
            {
                <NavLink class="sidebar-link" href="@entry.Href" Match="NavLinkMatch.All"
                         @onclick="CloseFlyout">
                    <!-- entry icon + title -->
                </NavLink>
            }
        </div>
    }
}
```

`HandleFlyoutKeydown` checks `e.Key == "Escape"` and calls `CloseFlyout`. Auto-focus on the panel via JS or `ElementReference.FocusAsync()` in `OnAfterRenderAsync` so Esc has a target.

### CSS (additions to `Sidebar.razor.css`)

```css
.sidebar-flyout {
    position: fixed;
    left: 56px;
    width: 220px;
    background: var(--card-bg);
    border: 1px solid var(--border-color);
    border-radius: 8px;
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.18);
    padding: 6px 0;
    z-index: 100;
    animation: sidebar-flyout-fade 100ms ease-out;
}

.sidebar-flyout:focus { outline: none; }

.sidebar-flyout-header {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 10px 14px;
    border-bottom: 1px solid var(--border-color);
    font-weight: 600;
    color: var(--text-primary);
}

.sidebar-flyout-backdrop {
    position: fixed;
    inset: 0;
    z-index: 99;
    background: transparent;
}

@keyframes sidebar-flyout-fade {
    from { opacity: 0; transform: translateX(-4px); }
    to   { opacity: 1; transform: translateX(0); }
}

::deep .sidebar-flyout .sidebar-link {
    /* Reuse existing sidebar-link styling. Override the deep left padding
       so it matches a flat list rather than the indented sub-item style. */
    padding-left: 14px;
}
```

### JS interop

A new file `wwwroot/js/sidebar-flyout.js` (or appended to an existing JS file — implementation chooses the cleaner option):

```javascript
window.controlMenu = window.controlMenu || {};
window.controlMenu.getRectTop = (el) => el?.getBoundingClientRect?.().top ?? 0;

window.controlMenu.subscribeWindowResize = (dotnetRef) => {
    const handler = () => dotnetRef.invokeMethodAsync('OnWindowResize');
    window.addEventListener('resize', handler);
    return { dispose: () => window.removeEventListener('resize', handler) };
};
```

The Blazor side captures the subscription handle returned by `subscribeWindowResize` and disposes on `Sidebar.Dispose()`. If implementation friction with the handle pattern is high (per `feedback_blazor_jsinterop_marshaling.md` — bare JS functions silently fail marshaling), the alternative is a numeric-token approach mirroring the existing `themeManager.subscribeBlazor` pattern in `wwwroot/js/theme.js`. Implementation plan chooses one based on simplicity.

If window-resize handling adds significant complexity, an acceptable v1 fallback is to **close the fly-out on resize** rather than recompute its position. Surface this trade-off in the implementation plan.

## Edge cases

- **Module has no visible entries** — `OpenFlyout` short-circuits without setting `_flyoutModuleId`. Click is silently ignored.
- **Sidebar uncollapses while fly-out is open** — `ToggleCollapsed` calls `CloseFlyout` before flipping `Collapsed`.
- **Two clicks on the same icon in rapid succession** — the second click's `OpenFlyout` is idempotent; state stays consistent.
- **Active route in a fly-out entry** — `NavLink` handles `.active` styling automatically; visual continuity preserved.
- **Window resize while fly-out is open** — preferred: recompute `_flyoutTopPx` via the JS-interop callback. Acceptable fallback: close the fly-out.

## Tests

bUnit test in `tests/ControlMenu.Tests/Components/Layout/SidebarFlyoutTests.cs`:

- `Collapsed_ClickIcon_OpensFlyoutForModule`
- `Collapsed_ClickDifferentIcon_SwapsActiveFlyout`
- `Collapsed_ClickBackdrop_ClosesFlyout`
- `NotCollapsed_ClickIcon_DoesNotOpenFlyout`
- `EmptyModule_ClickIcon_NoFlyout` (module with zero `VisibleEntries`)

(JS interop calls — `getRectTop`, window-resize subscription — are mocked or no-op'd via bUnit's `JSRuntimeMode.Loose` setup.)

Manual-test-checklist additions in `docs/manual-test-checklist.md`:

- Click section icon while sidebar is collapsed → fly-out opens next to icon
- Header in fly-out shows module name + icon
- Nav entries match expanded-sidebar entries (including device-type ones)
- Click another section icon → previous closes, new opens
- Click a link → navigates AND fly-out closes
- Click outside fly-out → closes
- Esc → closes
- Toggle sidebar from collapsed to expanded while fly-out is open → fly-out closes
- Resize window while fly-out is open → fly-out either repositions OR closes (depending on implementation choice)
- Active route highlights inside fly-out

## Branch and commits

Branch: `feature/sidebar-flyout`. Per the project's branch-by-default rule.

Commit shape (suggested grouping for the implementation plan):

1. JS interop helpers (`getRectTop`, optional resize subscription) + `wwwroot/js/sidebar-flyout.js`.
2. Sidebar.razor state + `OpenFlyout` / `CloseFlyout` / icon `@ref` capture (no UI change yet — the fly-out element exists but is gated by `_flyoutModuleId is not null` which stays null).
3. Sidebar.razor fly-out markup + scoped CSS.
4. Esc-key handling + auto-focus.
5. Window-resize behavior (recompute or close — pick one in plan).
6. `ToggleCollapsed` clears `_flyoutModuleId`.
7. bUnit tests.
8. CHANGELOG + manual-checklist + memory todo update.

CHANGELOG `[Unreleased]` Added: "Sidebar fly-out menu — clicking a section icon while the sidebar is collapsed opens a floating panel listing that module's nav entries (including device-type sub-links). Closes via click-outside, Esc, link click, or uncollapsing the sidebar."

## Open assumptions

- **JS interop pattern for window resize** — the spec leaves the choice between handle-return vs numeric-token to the implementation plan, biased toward whatever simpler pattern works without the marshaling pitfalls flagged in `feedback_blazor_jsinterop_marshaling.md`.
- **Fly-out width 220px** — picked to roughly match the visible content width of the expanded sidebar (260px wide minus padding/borders). Implementation can tweak this on first render if it looks off.
- **Fly-out animation** — 100ms slide-and-fade. If it feels twitchy with rapid icon clicks, implementation drops the animation entirely.
