# Sidebar Fly-out Menu Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Click a section icon while the sidebar is collapsed → a floating fly-out panel opens next to the icon listing that module's nav entries; backdrop / Esc / link-click / uncollapse all close it.

**Architecture:** Inline rendering inside `Sidebar.razor` — fly-out panel is a `position: fixed` div positioned via JS interop (`getBoundingClientRect().top` of the clicked anchor, queried by `data-flyout-anchor` attribute). State lives on the Sidebar component (`_flyoutModuleId`, `_flyoutTopPx`). Window-resize closes the fly-out (simpler than recomputing). Numeric-token JS subscription pattern follows the project's `themeManager.subscribeBlazor` convention.

**Tech Stack:** Blazor Server (.NET 9), scoped Razor CSS, vanilla JS interop, bUnit + xUnit + Moq.

**Spec:** `docs/superpowers/specs/2026-05-05-sidebar-flyout-design.md`

---

## File structure

**New files:**
- `src/ControlMenu/wwwroot/js/sidebar-flyout.js` — interop helpers (rect query, resize subscription with numeric-token pattern)
- `tests/ControlMenu.Tests/Components/Layout/SidebarFlyoutTests.cs` — bUnit tests

**Modified files:**
- `src/ControlMenu/Components/App.razor` — add `<script>` reference to the new JS file
- `src/ControlMenu/Components/Layout/Sidebar.razor` — add state fields, OpenFlyout/CloseFlyout/HandleFlyoutKeydown/OnWindowResize methods, click handler change on group-header, fly-out markup + backdrop, `data-flyout-anchor` attribute on group-header, ToggleCollapsed updated to clear state
- `src/ControlMenu/Components/Layout/Sidebar.razor.css` — fly-out styles + backdrop + 200ms keyframe animation
- `CHANGELOG.md` — `[Unreleased]` Added entry
- `docs/manual-test-checklist.md` — fly-out test cases

**Outside-repo files (memory):**
- `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/todo_control_menu.md` — mark item 3 shipped after merge

---

## Task 1: JS interop helpers

**Files:**
- Create: `src/ControlMenu/wwwroot/js/sidebar-flyout.js`
- Modify: `src/ControlMenu/Components/App.razor` (add script reference)

- [ ] **Step 1: Create `wwwroot/js/sidebar-flyout.js`**

```javascript
// src/ControlMenu/wwwroot/js/sidebar-flyout.js
window.controlMenu = window.controlMenu || {};

/**
 * Returns the top offset (in CSS px) of the sidebar group-header element with
 * the given module-id, or 0 if not found.
 */
window.controlMenu.getModuleIconRectTop = (moduleId) => {
    const el = document.querySelector(`[data-flyout-anchor="${moduleId}"]`);
    return el ? el.getBoundingClientRect().top : 0;
};

/**
 * Numeric-token resize subscription. Returns a token that the caller passes to
 * unsubscribeFlyoutResize. Pattern mirrors themeManager.subscribeBlazor in
 * theme.js — avoids the IJSObjectReference marshaling issue documented in
 * feedback_blazor_jsinterop_marshaling.md.
 */
window.controlMenu._flyoutResizeHandlers = window.controlMenu._flyoutResizeHandlers || {};
window.controlMenu._flyoutResizeNextToken = window.controlMenu._flyoutResizeNextToken || 1;

window.controlMenu.subscribeFlyoutResize = (dotnetRef) => {
    const token = window.controlMenu._flyoutResizeNextToken++;
    const handler = () => {
        try {
            dotnetRef.invokeMethodAsync('OnWindowResize');
        } catch (e) {
            // dotnetRef may have been disposed (circuit ended). Swallow.
        }
    };
    window.addEventListener('resize', handler);
    window.controlMenu._flyoutResizeHandlers[token] = handler;
    return token;
};

window.controlMenu.unsubscribeFlyoutResize = (token) => {
    const handler = window.controlMenu._flyoutResizeHandlers[token];
    if (handler) {
        window.removeEventListener('resize', handler);
        delete window.controlMenu._flyoutResizeHandlers[token];
    }
};
```

- [ ] **Step 2: Reference the script in `App.razor`**

Open `src/ControlMenu/Components/App.razor`. Find the existing script block (around lines 14–22 in the current file). Add a new line after the other JS script tags:

```html
<script src="js/sidebar-flyout.js"></script>
```

The exact placement doesn't matter (no inter-script ordering), but conventionally append at the end of the script list near `codec-support.js`.

- [ ] **Step 3: Verify build**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj -c Release --nologo`
Expected: 0 errors. Static asset gets bundled.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/wwwroot/js/sidebar-flyout.js src/ControlMenu/Components/App.razor
git commit -m "feat(sidebar): add sidebar-flyout JS interop helpers

Numeric-token resize subscription pattern matching themeManager;
getModuleIconRectTop queries the click anchor by data attribute.
Script registered in App.razor."
```

---

## Task 2: Sidebar state + handler scaffolding

**Files:**
- Modify: `src/ControlMenu/Components/Layout/Sidebar.razor`

This task adds state fields and methods WITHOUT yet rendering the fly-out. The new code paths are reachable but dormant — `_flyoutModuleId` stays null because no caller sets it yet (Task 3 wires the click handler).

- [ ] **Step 1: Add new state fields**

Open `src/ControlMenu/Components/Layout/Sidebar.razor`. Inside the existing `@code { ... }` block, near the other private fields (after `private bool _allExpanded;`), add:

```csharp
private string? _flyoutModuleId;
private double _flyoutTopPx;
private int? _resizeSubscriptionToken;
private DotNetObjectReference<Sidebar>? _selfRef;
private ElementReference _flyoutRef;
private bool _shouldFocusFlyout;
```

Add `using` if needed at top — `Microsoft.JSInterop` should already be in scope via the existing `IJSRuntime JS` injection; no new using required.

- [ ] **Step 2: Add OpenFlyout method**

Inside the `@code` block, near the other private async methods (after `ToggleAll`), add:

```csharp
private async Task OpenFlyout(Modules.IToolModule module)
{
    if (!Collapsed) return;
    if (!VisibleEntries(module).Any()) return;

    _flyoutTopPx = await JS.InvokeAsync<double>("controlMenu.getModuleIconRectTop", module.Id);
    _flyoutModuleId = module.Id;
    _shouldFocusFlyout = true;

    if (_resizeSubscriptionToken is null)
    {
        _selfRef ??= DotNetObjectReference.Create(this);
        _resizeSubscriptionToken = await JS.InvokeAsync<int>(
            "controlMenu.subscribeFlyoutResize", _selfRef);
    }

    StateHasChanged();
}
```

- [ ] **Step 3: Add CloseFlyout method**

```csharp
private async Task CloseFlyout()
{
    _flyoutModuleId = null;
    _shouldFocusFlyout = false;

    if (_resizeSubscriptionToken is not null)
    {
        try
        {
            await JS.InvokeVoidAsync("controlMenu.unsubscribeFlyoutResize", _resizeSubscriptionToken);
        }
        catch { /* JS context torn down — nothing to do */ }
        _resizeSubscriptionToken = null;
    }
}
```

- [ ] **Step 4: Add HandleFlyoutKeydown**

```csharp
private async Task HandleFlyoutKeydown(KeyboardEventArgs e)
{
    if (e.Key == "Escape")
    {
        await CloseFlyout();
    }
}
```

You'll need to add `using Microsoft.AspNetCore.Components.Web;` to the top of the file if `KeyboardEventArgs` isn't recognized.

- [ ] **Step 5: Add OnWindowResize JSInvokable**

```csharp
[JSInvokable]
public async Task OnWindowResize()
{
    // Simpler-than-recompute strategy from the spec: close the fly-out on resize.
    if (_flyoutModuleId is not null)
    {
        await CloseFlyout();
        await InvokeAsync(StateHasChanged);
    }
}
```

- [ ] **Step 6: Update ToggleCollapsed to close the fly-out**

Find the existing `ToggleCollapsed` method:

```csharp
private void ToggleCollapsed() => Collapsed = !Collapsed;
```

Replace with:

```csharp
private async Task ToggleCollapsed()
{
    // Closing fly-out before flipping Collapsed: when transitioning collapsed → expanded
    // the fly-out is no longer relevant; when expanded → collapsed, no fly-out is open
    // anyway, so the call is a no-op.
    if (_flyoutModuleId is not null)
    {
        await CloseFlyout();
    }
    Collapsed = !Collapsed;
}
```

- [ ] **Step 7: Update Dispose to unsubscribe + dispose dotnetRef**

Find the existing `Dispose` method:

```csharp
public void Dispose() => DeviceTypeCache.CacheUpdated -= OnCacheUpdated;
```

Replace with:

```csharp
public void Dispose()
{
    DeviceTypeCache.CacheUpdated -= OnCacheUpdated;

    // Best-effort unsubscribe — JS context may already be torn down.
    if (_resizeSubscriptionToken is not null)
    {
        try
        {
            _ = JS.InvokeVoidAsync("controlMenu.unsubscribeFlyoutResize", _resizeSubscriptionToken);
        }
        catch { /* swallow */ }
    }

    _selfRef?.Dispose();
}
```

- [ ] **Step 8: Add OnAfterRenderAsync focus path**

Find the existing `OnAfterRenderAsync` method (handles localStorage init). Append focus logic at the end of the method body, BEFORE the final `StateHasChanged();` call inside the `if (firstRender)` branch. Then ALSO handle non-first-render focus by adding logic outside the `if (firstRender)` block:

Replace the existing `OnAfterRenderAsync` body with:

```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        var hasVisited = await JS.InvokeAsync<string?>("localStorage.getItem", "sidebar-initialized");
        var saved = await JS.InvokeAsync<string?>("localStorage.getItem", "sidebar-expanded-groups");
        if (hasVisited is not null)
        {
            _expandedGroups.Clear();
            if (!string.IsNullOrEmpty(saved))
            {
                foreach (var id in saved.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    _expandedGroups.Add(id);
            }
        }
        else
        {
            foreach (var m in ModuleDiscovery.Modules)
                _expandedGroups.Add(m.Id);
            await JS.InvokeVoidAsync("localStorage.setItem", "sidebar-initialized", "1");
            await PersistState();
        }
        UpdateAllExpandedFlag();
        StateHasChanged();
    }

    if (_shouldFocusFlyout && _flyoutModuleId is not null)
    {
        _shouldFocusFlyout = false;
        try
        {
            await _flyoutRef.FocusAsync();
        }
        catch { /* element may have been removed; ignore */ }
    }
}
```

- [ ] **Step 9: Verify build**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj -c Release --nologo`
Expected: 0 errors. (No tests yet — the new methods are not invoked by the markup, which Task 3 will wire up.)

- [ ] **Step 10: Commit**

```bash
git add src/ControlMenu/Components/Layout/Sidebar.razor
git commit -m "feat(sidebar): scaffold fly-out state and handlers (no UI yet)

Adds OpenFlyout/CloseFlyout/HandleFlyoutKeydown/OnWindowResize
methods + state fields. ToggleCollapsed now async and closes the
fly-out before flipping. Dispose unsubscribes the JS resize handler.
OnAfterRenderAsync focuses the fly-out element when it just opened.
No markup changes yet — Task 3 wires the click handler and renders
the panel."
```

---

## Task 3: Fly-out markup + click handler wiring

**Files:**
- Modify: `src/ControlMenu/Components/Layout/Sidebar.razor`

- [ ] **Step 1: Add data-flyout-anchor + dual-mode click handler**

Find the existing group-header div in the foreach loop:

```razor
<div class="sidebar-group-header" @onclick="() => ToggleGroup(module.Id)">
```

Replace with:

```razor
<div class="sidebar-group-header"
     data-flyout-anchor="@module.Id"
     @onclick="@(() => Collapsed ? OpenFlyout(module) : ToggleGroup(module.Id))">
```

The `data-flyout-anchor` attribute is what `controlMenu.getModuleIconRectTop` queries to measure the icon's top position.

- [ ] **Step 2: Render the fly-out panel + backdrop**

At the BOTTOM of the `<nav class="sidebar">…</nav>` element, AFTER the closing `</div>` of `.sidebar-footer` but INSIDE the `<nav>`, add:

```razor
@if (Collapsed && _flyoutModuleId is not null)
{
    var flyoutModule = ModuleDiscovery.Modules.FirstOrDefault(m => m.Id == _flyoutModuleId);
    if (flyoutModule is not null)
    {
        <div class="sidebar-flyout-backdrop" @onclick="CloseFlyout"></div>
        <div class="sidebar-flyout"
             style="top: @(_flyoutTopPx.ToString(System.Globalization.CultureInfo.InvariantCulture))px;"
             tabindex="-1"
             @ref="_flyoutRef"
             @onkeydown="HandleFlyoutKeydown">
            <div class="sidebar-flyout-header">
                @if (ModuleImageMap.TryGetValue(flyoutModule.Id, out var flyoutImg))
                {
                    <img src="@flyoutImg" alt="" class="sidebar-module-icon" />
                }
                else
                {
                    <i class="bi @flyoutModule.Icon"></i>
                }
                <span>@flyoutModule.DisplayName</span>
            </div>
            @foreach (var entry in VisibleEntries(flyoutModule))
            {
                <NavLink class="sidebar-link" href="@entry.Href" Match="NavLinkMatch.All"
                         @onclick="CloseFlyout">
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
            }
        </div>
    }
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj -c Release --nologo`
Expected: 0 errors. The markup uses members declared in Task 2 + the existing `ModuleImageMap` / `VisibleEntries` / `_flyoutRef` etc.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Components/Layout/Sidebar.razor
git commit -m "feat(sidebar): render fly-out panel + backdrop on collapsed-icon click

Group-header click now branches: when collapsed, opens fly-out for
that module; when expanded, toggles group as before. Panel renders
at the bottom of the sidebar nav with backdrop, header, and full
nav-entry list. Active route highlighting via NavLink works
automatically."
```

---

## Task 4: Fly-out CSS + animation

**Files:**
- Modify: `src/ControlMenu/Components/Layout/Sidebar.razor.css`

- [ ] **Step 1: Append fly-out styles**

Open `src/ControlMenu/Components/Layout/Sidebar.razor.css`. Append the following block at the end of the file:

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
    animation: sidebar-flyout-fade 200ms ease-out;
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
    /* Reuse existing sidebar-link styling; override the deep left padding so
       the flat fly-out list reads cleanly without the indented sub-item look. */
    padding-left: 14px;
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj -c Release --nologo`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/Components/Layout/Sidebar.razor.css
git commit -m "feat(sidebar): scoped CSS for fly-out panel + backdrop

position:fixed panel anchored at left:56px, top set inline by the
component. 200ms slide-and-fade keyframe animation. Backdrop catches
click-outside dismissal. ::deep override on sidebar-link padding so
fly-out entries render flat instead of indented."
```

---

## Task 5: bUnit tests

**Files:**
- Create: `tests/ControlMenu.Tests/Components/Layout/SidebarFlyoutTests.cs`

The Sidebar component has dependencies on `ModuleDiscoveryService`, `IJSRuntime`, `IDeviceTypeCache`, `IServiceProvider`. Tests need to mock these.

- [ ] **Step 1: Write the failing tests**

Create `tests/ControlMenu.Tests/Components/Layout/SidebarFlyoutTests.cs`:

```csharp
using Bunit;
using Bunit.TestDoubles;
using ControlMenu.Components.Layout;
using ControlMenu.Modules;
using ControlMenu.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ControlMenu.Tests.Components.Layout;

public class SidebarFlyoutTests : TestContext
{
    private readonly Mock<IDeviceTypeCache> _cache = new();
    private readonly ModuleDiscoveryService _discovery;

    public SidebarFlyoutTests()
    {
        // Two minimal fake modules so the foreach has rows to render.
        var module1 = new FakeModule("test-mod-1", "Test Module One", "bi-gear",
            new[] { new NavEntry("Page A", "/test-mod-1/a", "bi-circle", 0) });
        var module2 = new FakeModule("test-mod-2", "Test Module Two", "bi-box",
            new[] { new NavEntry("Page B", "/test-mod-2/b", "bi-square", 0) });
        _discovery = new ModuleDiscoveryService(new IToolModule[] { module1, module2 });

        Services.AddSingleton(_discovery);
        Services.AddSingleton(_cache.Object);
        Services.AddSingleton<IServiceProvider>(sp => sp);

        // Loose-mode JS interop: any call returns default; we explicitly stub the
        // calls we care about asserting against.
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<double>("controlMenu.getModuleIconRectTop", _ => true)
                 .SetResult(123.0);
        JSInterop.Setup<int>("controlMenu.subscribeFlyoutResize", _ => true)
                 .SetResult(1);
        JSInterop.SetupVoid("controlMenu.unsubscribeFlyoutResize", _ => true);
        // Sidebar's OnAfterRenderAsync calls localStorage; loose mode handles those.
    }

    [Fact]
    public void Collapsed_ClickIcon_OpensFlyoutForModule()
    {
        var cut = RenderComponent<Sidebar>();
        // Force collapsed state. Sidebar's Collapsed prop is private — toggle via the button.
        cut.Find(".sidebar-toggle").Click();

        // Click the first module's group header.
        cut.Find("[data-flyout-anchor=\"test-mod-1\"]").Click();

        // Fly-out renders for test-mod-1.
        var flyout = cut.Find(".sidebar-flyout");
        Assert.NotNull(flyout);
        Assert.Contains("Test Module One", flyout.TextContent);
        Assert.Contains("Page A", flyout.TextContent);
    }

    [Fact]
    public void Collapsed_ClickDifferentIcon_SwapsActiveFlyout()
    {
        var cut = RenderComponent<Sidebar>();
        cut.Find(".sidebar-toggle").Click();

        cut.Find("[data-flyout-anchor=\"test-mod-1\"]").Click();
        Assert.Contains("Test Module One", cut.Find(".sidebar-flyout-header").TextContent);

        cut.Find("[data-flyout-anchor=\"test-mod-2\"]").Click();
        Assert.Contains("Test Module Two", cut.Find(".sidebar-flyout-header").TextContent);

        // Only one fly-out at a time.
        Assert.Single(cut.FindAll(".sidebar-flyout"));
    }

    [Fact]
    public void Collapsed_ClickBackdrop_ClosesFlyout()
    {
        var cut = RenderComponent<Sidebar>();
        cut.Find(".sidebar-toggle").Click();
        cut.Find("[data-flyout-anchor=\"test-mod-1\"]").Click();

        Assert.NotEmpty(cut.FindAll(".sidebar-flyout"));

        cut.Find(".sidebar-flyout-backdrop").Click();

        Assert.Empty(cut.FindAll(".sidebar-flyout"));
    }

    [Fact]
    public void NotCollapsed_ClickIcon_DoesNotOpenFlyout()
    {
        var cut = RenderComponent<Sidebar>();
        // Sidebar starts expanded.
        cut.Find("[data-flyout-anchor=\"test-mod-1\"]").Click();

        Assert.Empty(cut.FindAll(".sidebar-flyout"));
    }

    [Fact]
    public void Collapsed_EmptyModule_ClickIcon_NoFlyout()
    {
        // Replace the discovery service with one whose first module has zero entries.
        Services.Remove(Services.First(d => d.ServiceType == typeof(ModuleDiscoveryService)));
        var emptyModule = new FakeModule("empty-mod", "Empty", "bi-x", Array.Empty<NavEntry>());
        Services.AddSingleton(new ModuleDiscoveryService(new IToolModule[] { emptyModule }));

        var cut = RenderComponent<Sidebar>();
        cut.Find(".sidebar-toggle").Click();
        cut.Find("[data-flyout-anchor=\"empty-mod\"]").Click();

        Assert.Empty(cut.FindAll(".sidebar-flyout"));
    }

    private sealed class FakeModule : IToolModule
    {
        public FakeModule(string id, string displayName, string icon, IEnumerable<NavEntry> entries)
        {
            Id = id;
            DisplayName = displayName;
            Icon = icon;
            _entries = entries.ToArray();
        }

        private readonly NavEntry[] _entries;

        public string Id { get; }
        public string DisplayName { get; }
        public string Icon { get; }
        public IEnumerable<ModuleDependency> Dependencies => Array.Empty<ModuleDependency>();
        public IEnumerable<ConfigRequirement> ConfigRequirements => Array.Empty<ConfigRequirement>();
        public IEnumerable<NavEntry> GetNavEntries() => _entries;
        public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => Array.Empty<BackgroundJobDefinition>();
        public void RegisterServices(IServiceCollection services) { }
    }
}
```

If the existing `ModuleDiscoveryService` constructor doesn't accept `IEnumerable<IToolModule>` directly, look at the existing test fakes for the right pattern. There's already a `FakeToolModule` somewhere in the test project — check `tests/ControlMenu.Tests/Modules/Fakes/FakeToolModule.cs` and adapt.

Likewise, if `IToolModule`'s actual interface differs from what's typed above (e.g., method names), inspect `src/ControlMenu/Modules/IToolModule.cs` and adjust the FakeModule accordingly. The shape of the test logic is what matters — adapt the fake to fit.

- [ ] **Step 2: Run tests to verify they fail (or run to figure out interface mismatches)**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter "FullyQualifiedName~SidebarFlyoutTests" --nologo`

If they fail because of interface mismatches in `FakeModule` or `ModuleDiscoveryService` constructor — fix the fake to match the real interfaces and rerun. Once the tests build and run, all 5 should PASS (the implementation from Tasks 2-3 should make them pass).

- [ ] **Step 3: Run full test suite to verify nothing else broke**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj -c Release --nologo`
Expected: all tests pass (existing 349 + 5 new = 354).

- [ ] **Step 4: Commit**

```bash
git add tests/ControlMenu.Tests/Components/Layout/SidebarFlyoutTests.cs
git commit -m "test(sidebar): bUnit smoke tests for fly-out behavior

Five tests: open on collapsed-icon click, swap on different icon
click, close on backdrop click, no-op when sidebar expanded, no-op
when module has zero visible entries. JS interop calls stubbed via
bUnit loose mode."
```

---

## Task 6: Manual end-to-end smoke

The bUnit tests cover the state-machine. The actual rendering, animation, focus, and Esc-key only work in a real browser. Boot the dev server and walk a checklist.

- [ ] **Step 1: Boot the dev server**

Run: `dotnet run --project src/ControlMenu/ControlMenu.csproj -c Release`
Expected: server starts at http://localhost:5159.

(If port 5159 is already bound by a stale process, kill it first: PowerShell `Get-NetTCPConnection -LocalPort 5159 -State Listen | Stop-Process -Id $_.OwningProcess -Force`.)

- [ ] **Step 2: Walk the manual checklist**

Open http://localhost:5159 in a browser. Verify:

- Click the `<` toggle in the sidebar header to collapse.
- Click any section icon (e.g., Android Devices) — fly-out panel opens to the right of the icon, vertically aligned, with the module name + nav entries listed.
- Click another section icon (e.g., Cameras) — first fly-out closes, second opens.
- Click a link inside the fly-out — navigates AND closes.
- Click outside the fly-out (anywhere on the page background, the main content area, or another section icon) — closes.
- Press Esc with the fly-out open — closes.
- Click the `<` toggle to uncollapse the sidebar while a fly-out is open — fly-out disappears.
- Resize the browser window while a fly-out is open — fly-out closes (acceptable v1 behavior; spec allowed recompute as alternative).
- Active route highlighting: navigate to e.g. `/settings/general`, collapse sidebar, click Settings sub-link — confirm the active route still highlights inside the fly-out.
- Animation: 200ms slide + fade on open. Should feel smooth, not twitchy on rapid click.

If anything looks off, surface it and fix before moving on.

- [ ] **Step 3: Stop the dev server**

Ctrl+C the terminal running `dotnet run`.

(No commit on this task — it's smoke verification only.)

---

## Task 7: CHANGELOG + manual checklist + memory

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `docs/manual-test-checklist.md`

- [ ] **Step 1: CHANGELOG entry**

Open `CHANGELOG.md`. Under `## [Unreleased]` → `### Added` (find the existing list under that heading; do not disturb the surrounding entries), append:

```markdown
- **Sidebar fly-out menu** — clicking a section icon while the sidebar is collapsed opens a floating panel listing that module's nav entries (including device-type sub-links). The panel anchors to the right of the icon. Closes via click-outside (transparent backdrop), Esc key, link click, or uncollapsing the sidebar. Window resize while open closes the panel. Solves the "dead click" feel where collapsed-mode section icons toggled internal state with no visible effect.
```

- [ ] **Step 2: Manual test checklist additions**

Open `docs/manual-test-checklist.md`. Append a new section at the END of the file:

```markdown
## Sidebar Fly-out (2026-05-05)

- [ ] Collapse the sidebar via the `<` toggle.
- [ ] Click any section icon (Android Devices / Cameras / Jellyfin / etc.) → fly-out panel opens to the right of the icon, vertically aligned with it.
- [ ] Panel header shows module name + icon. Below it, nav entries match what the expanded sidebar shows for that module.
- [ ] Click a different section icon → previous fly-out closes; new one opens.
- [ ] Click a link inside the fly-out → navigates and closes.
- [ ] Click outside the fly-out (on the page background, main content, or another icon) → closes.
- [ ] Press Esc with the fly-out open → closes.
- [ ] Toggle sidebar back to expanded while fly-out is open → fly-out closes.
- [ ] Resize browser window while fly-out is open → fly-out closes (acceptable v1 behavior).
- [ ] Active route highlight survives inside the fly-out (current page is highlighted in the fly-out's link list).
- [ ] Animation: 200ms slide-and-fade-in on open; not twitchy on rapid icon clicks.
- [ ] Empty module (zero visible nav entries) — clicking its icon when collapsed is a no-op (no empty panel).
```

- [ ] **Step 3: Verify build still clean**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj -c Release --nologo && dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj -c Release --nologo`
Expected: build clean, 354 tests pass.

- [ ] **Step 4: Commit**

```bash
git add CHANGELOG.md docs/manual-test-checklist.md
git commit -m "docs: changelog + manual checklist for sidebar fly-out"
```

---

## Task 8: Push + merge + memory update

This task wraps up the branch — push, merge to master with --no-ff, delete branch, update todo memory. Same flow we used for the settings-grid and external-deps merges.

- [ ] **Step 1: Push branch to origin**

Run: `git push -u origin feature/sidebar-flyout`
Expected: branch published.

- [ ] **Step 2: Manual smoke gate**

Pause for the user to smoke the live build before merging. Confirm with the user that they're ready before proceeding to merge.

- [ ] **Step 3: Merge to master**

```bash
git checkout master
git merge feature/sidebar-flyout --no-ff -m "Merge branch 'feature/sidebar-flyout'

Sidebar fly-out menu — clicking a section icon while collapsed opens
a floating panel listing module's nav entries. Backdrop click / Esc /
link click / uncollapse all close. JS interop measures icon position
via getBoundingClientRect; panel uses position:fixed to escape the
sidebar's overflow:hidden. Resolves todo_control_menu.md item 3."
git push
```

- [ ] **Step 4: Delete branch (local + remote)**

```bash
git branch -d feature/sidebar-flyout
git push origin --delete feature/sidebar-flyout
```

- [ ] **Step 5: Update memory todo**

Edit `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/todo_control_menu.md`:

- Update the "Last consolidated" line to mention sidebar fly-out merged via the new merge commit SHA (capture the SHA from `git log master -1` after merging).
- Replace the existing item 3 body with a SHIPPED stub:
  ```markdown
  ## 3. ~~Collapsed sidebar — section-header icons are dead clicks (UX overhaul cluster)~~ — SHIPPED 2026-05-05

  Fly-out menu approach (option A from the original brainstorm): clicking a section icon while collapsed opens a floating panel anchored next to the icon listing that module's full nav entries (including conditionally-visible device-type sub-links). Backdrop click / Esc / link-click / uncollapse all dismiss. Solves both the dead-click feel AND the device-type sub-link discoverability question that the TODO had bundled in.

  See `docs/superpowers/specs/2026-05-05-sidebar-flyout-design.md` and `docs/superpowers/plans/2026-05-05-sidebar-flyout.md`.
  ```
- Update the resume banner at the top: "UX cluster items 1 (Homepage polish) still open" — item 3 is gone.

---

## Self-review notes

**Spec coverage:**
- Trigger / single-instance / dismissal: Tasks 2, 3, 6 ✓
- Position via getBoundingClientRect + data-anchor: Tasks 1, 2 ✓
- Content (header + nav entries): Task 3 ✓
- State (`_flyoutModuleId`, `_flyoutTopPx`, `_resizeSubscriptionToken`, `_selfRef`, `_flyoutRef`, `_shouldFocusFlyout`): Task 2 ✓
- Click-outside via backdrop: Task 3 ✓
- Esc key + auto-focus: Task 2 (HandleFlyoutKeydown + OnAfterRenderAsync focus) ✓
- Window resize closes fly-out: Task 2 (OnWindowResize + subscribeFlyoutResize JS) ✓
- CSS additions + 200ms animation: Task 4 ✓
- Edge cases (uncollapse-while-open, empty module, repeated clicks): Tasks 2 (ToggleCollapsed close), 2 (OpenFlyout short-circuit on empty), Tests in 5 ✓
- Tests: Task 5 ✓
- CHANGELOG + checklist: Task 7 ✓
- Branch + commits + merge: Task 8 ✓

**Type / method consistency:**
- `OpenFlyout(IToolModule)` / `CloseFlyout()` / `HandleFlyoutKeydown(KeyboardEventArgs)` / `OnWindowResize()` — same signatures across plan ✓
- `controlMenu.getModuleIconRectTop` / `subscribeFlyoutResize` / `unsubscribeFlyoutResize` — same JS names across Task 1 and Task 2 ✓
- `data-flyout-anchor` attribute — set in Task 3 markup, queried by JS in Task 1 ✓
- Numeric token type — `int` in C#, plain number in JS ✓
- `_flyoutTopPx` — `double` in C#, formatted with InvariantCulture in inline style to avoid comma vs dot decimal-separator issues across locales ✓

**Open assumptions flagged at implementation time:**
- `IToolModule` interface shape for the test fake — Task 5 instructs the implementer to inspect the real interface and adjust.
- bUnit `Bunit.TestDoubles` package — already pulled in by previous TDD work on the SettingsGrid components. Test project's csproj should already have the bUnit package reference.
- `ModuleDiscoveryService` constructor — Task 5 may need a small adjustment if the real constructor differs from `IEnumerable<IToolModule>`.
