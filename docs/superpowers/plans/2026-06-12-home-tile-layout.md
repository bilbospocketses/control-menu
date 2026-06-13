# Home Page Modular Tile Layout — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Re-lay-out the home page's category cards as a uniform modular tile grid where content-heavy cards span multiple tile-units and align with stacked single-unit cards, minimizing dead space.

**Architecture:** CSS grid with a fixed `grid-auto-rows` tile-unit; each `.module-card` spans `var(--cm-tile-span)` rows. The span is seeded server-side (a coarse estimate, for a clean first paint) and then measured-and-snapped to the exact whole-unit value by a small client script (`home-tiles.js`) on render and on window resize. The hero is slimmed to a horizontal band and card headers are left-aligned. All colors come from the existing theme CSS variables, so it works in light and dark mode.

**Tech Stack:** Blazor Server (.NET 10), Razor + scoped CSS, vanilla JS via the app's existing `window.controlMenu.*` interop convention (NOT ES modules — see `theme.js`/`sidebar-flyout.js`), xUnit + bUnit + Moq.

**Spec:** `docs/superpowers/specs/2026-06-12-home-tile-layout-design.md`

---

## File structure

- **Create** `src/ControlMenu/Components/Pages/HomeTileLayout.cs` — static `InitialSpan(int)` first-paint estimate (pure, unit-tested).
- **Create** `src/ControlMenu/wwwroot/js/home-tiles.js` — `window.controlMenu.layoutHomeTiles()` measure-and-snap pass + numeric-token resize subscription (mirrors `sidebar-flyout.js`).
- **Modify** `src/ControlMenu/Components/App.razor` — add `<script src="js/home-tiles.js">`.
- **Modify** `src/ControlMenu/Components/Pages/Home.razor` — slim hero markup; per-card `--cm-tile-span` seed; `OnAfterRenderAsync` interop + `IAsyncDisposable` cleanup.
- **Modify** `src/ControlMenu/Components/Pages/Home.razor.css` — slim hero, tile-grid rows + vars, left-aligned headers, span var, responsive.
- **Create** `tests/ControlMenu.Tests/Components/Pages/HomeTileLayoutTests.cs` — TDD for `InitialSpan`.
- **Modify** `tests/ControlMenu.Tests/Components/Pages/HomeTests.cs` — loose JSInterop + a card-span-seed assertion; existing assertions stay green (class names are preserved).

`Home.razor` keeps every structural class the existing tests assert (`.hero`, `.module-grid`, `.module-card`, `.module-header h3`, `.module-links a.pill-link`, `.empty-state`), so the redesign is CSS + a seed attribute + JS, not a DOM rewrite.

---

## Task 1: Server-side span estimate (`HomeTileLayout.InitialSpan`)

**Files:**
- Create: `tests/ControlMenu.Tests/Components/Pages/HomeTileLayoutTests.cs`
- Create: `src/ControlMenu/Components/Pages/HomeTileLayout.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/ControlMenu.Tests/Components/Pages/HomeTileLayoutTests.cs`:

```csharp
using ControlMenu.Components.Pages;

namespace ControlMenu.Tests.Components.Pages;

public class HomeTileLayoutTests
{
    [Theory]
    [InlineData(0, 1)]   // defensive: never less than one unit
    [InlineData(1, 1)]
    [InlineData(3, 1)]   // up to 3 links fit in one tile-unit
    [InlineData(4, 2)]
    [InlineData(6, 2)]   // Imaging Tools today
    [InlineData(7, 3)]
    [InlineData(10, 4)]  // future tall card
    public void InitialSpan_RoundsUpToWholeTileUnits(int entries, int expected)
    {
        Assert.Equal(expected, HomeTileLayout.InitialSpan(entries));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests/ControlMenu.Tests.csproj" --filter "FullyQualifiedName~HomeTileLayoutTests"`
Expected: FAIL — build error, `HomeTileLayout` does not exist.

- [ ] **Step 3: Write the minimal implementation**

Create `src/ControlMenu/Components/Pages/HomeTileLayout.cs`:

```csharp
namespace ControlMenu.Components.Pages;

/// <summary>
/// First-paint helpers for the home page's modular tile grid. The authoritative
/// per-card span is measured client-side (wwwroot/js/home-tiles.js); this only
/// seeds a close initial value so the server-rendered first paint does not flash
/// before the client measure-and-snap pass runs.
/// </summary>
public static class HomeTileLayout
{
    // Roughly three pill-links fit in one tile-unit row. Round up so a card is
    // never seeded too short; the client pass corrects each card precisely.
    private const int PillsPerUnit = 3;

    /// <summary>Initial grid-row span for a card with the given visible-link count.</summary>
    public static int InitialSpan(int visibleEntryCount)
    {
        if (visibleEntryCount <= 0)
            return 1;
        return (visibleEntryCount + PillsPerUnit - 1) / PillsPerUnit; // integer ceil
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests/ControlMenu.Tests.csproj" --filter "FullyQualifiedName~HomeTileLayoutTests"`
Expected: PASS — 7 passed.

- [ ] **Step 5: Commit**

```bash
git -C "C:/Users/jscha/source/repos/control-menu" add src/ControlMenu/Components/Pages/HomeTileLayout.cs tests/ControlMenu.Tests/Components/Pages/HomeTileLayoutTests.cs
git -C "C:/Users/jscha/source/repos/control-menu" commit -m "feat(home): add tile-span first-paint estimate helper"
```

---

## Task 2: Client measure-and-snap script (`home-tiles.js`)

Browser layout logic; not bUnit-testable (no layout engine). Verified by build + manual visual smoke in Task 6. ASCII-only (paste-and-run script).

**Files:**
- Create: `src/ControlMenu/wwwroot/js/home-tiles.js`

- [ ] **Step 1: Create the script**

Create `src/ControlMenu/wwwroot/js/home-tiles.js`:

```javascript
window.controlMenu = window.controlMenu || {};

/**
 * Measure-and-snap pass for the home page modular tile grid. For each
 * .module-card, measure its natural content height and snap its grid-row span
 * to the smallest whole number of tile-units that fits, by writing the
 * --cm-tile-span custom property the CSS reads. Authoritative over the
 * server-seeded estimate.
 */
window.controlMenu.layoutHomeTiles = function () {
    var grid = document.querySelector('.module-grid');
    if (!grid) {
        return;
    }
    var gridStyle = getComputedStyle(grid);
    var unit = parseFloat(gridStyle.gridAutoRows); // px height of one tile-unit
    var gap = parseFloat(gridStyle.rowGap) || 0;   // px gap between rows
    if (!unit || isNaN(unit)) {
        return;
    }
    var cards = grid.querySelectorAll('.module-card');
    for (var i = 0; i < cards.length; i++) {
        var card = cards[i];
        // scrollHeight is the full content height even when overflow clips the
        // card to a shorter cell, so it is independent of the current span.
        var content = card.scrollHeight;
        var span = Math.max(1, Math.ceil((content + gap) / (unit + gap)));
        card.style.setProperty('--cm-tile-span', String(span));
    }
};

/**
 * Numeric-token resize subscription. Re-runs layoutHomeTiles on a debounced
 * window resize and returns a token the caller passes to
 * unsubscribeHomeTilesResize on disposal. Mirrors
 * controlMenu.subscribeFlyoutResize in sidebar-flyout.js (the numeric-token
 * pattern avoids the IJSObjectReference marshaling issue).
 */
window.controlMenu._homeTilesResizeHandlers = window.controlMenu._homeTilesResizeHandlers || {};
window.controlMenu._homeTilesResizeNextToken = window.controlMenu._homeTilesResizeNextToken || 1;

window.controlMenu.subscribeHomeTilesResize = function () {
    var token = window.controlMenu._homeTilesResizeNextToken++;
    var timer = null;
    var handler = function () {
        if (timer) {
            clearTimeout(timer);
        }
        timer = setTimeout(window.controlMenu.layoutHomeTiles, 100);
    };
    window.addEventListener('resize', handler);
    window.controlMenu._homeTilesResizeHandlers[token] = handler;
    return token;
};

window.controlMenu.unsubscribeHomeTilesResize = function (token) {
    var handler = window.controlMenu._homeTilesResizeHandlers[token];
    if (handler) {
        window.removeEventListener('resize', handler);
        delete window.controlMenu._homeTilesResizeHandlers[token];
    }
};
```

- [ ] **Step 2: Commit**

```bash
git -C "C:/Users/jscha/source/repos/control-menu" add src/ControlMenu/wwwroot/js/home-tiles.js
git -C "C:/Users/jscha/source/repos/control-menu" commit -m "feat(home): add measure-and-snap tile layout script"
```

---

## Task 3: Load the script in the host page

**Files:**
- Modify: `src/ControlMenu/Components/App.razor` (after the `sidebar-flyout.js` line, currently line 23)

- [ ] **Step 1: Add the script tag**

In `src/ControlMenu/Components/App.razor`, immediately after:

```html
    <script src="js/sidebar-flyout.js"></script>
```

add:

```html
    <script src="js/home-tiles.js"></script>
```

- [ ] **Step 2: Commit**

```bash
git -C "C:/Users/jscha/source/repos/control-menu" add src/ControlMenu/Components/App.razor
git -C "C:/Users/jscha/source/repos/control-menu" commit -m "chore(home): load home-tiles.js in App.razor"
```

---

## Task 4: Restyle the home page (`Home.razor.css`)

Visual change; verified by build + manual smoke in Task 6. Replaces the whole file (slim hero, tile grid, left-aligned headers, span var). Pill-link styling is unchanged from the original.

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Home.razor.css`

- [ ] **Step 1: Replace the file contents**

Replace all of `src/ControlMenu/Components/Pages/Home.razor.css` with:

```css
/* Home page - slim hero + modular tile grid with pill-button navigation */

.home-container {
    display: flex;
    flex-direction: column;
    padding: 2rem 1.5rem;
    max-width: 1200px;
    margin: 0 auto;
}

/* Hero - slim horizontal band with a divider beneath */
.hero {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 1rem;
    padding-bottom: 1.25rem;
    margin-bottom: 1.75rem;
    border-bottom: 1px solid var(--border-color, #dee2e6);
}

.hero-icon {
    width: 48px;
    height: 48px;
    border-radius: 0.75rem;
    flex-shrink: 0;
}

.hero-text {
    min-width: 0;
}

.hero h1 {
    margin: 0 0 0.15rem 0;
    font-size: 1.5rem;
    font-weight: 700;
    color: var(--text-primary);
    line-height: 1.15;
}

.hero-subtitle {
    margin: 0;
    font-size: 0.9rem;
    color: var(--text-secondary, #6c757d);
}

/* Modular tile grid - fixed tile-unit rows; cards span whole units */
.module-grid {
    --cm-tile-unit: 7.5rem;   /* height of one single-link tile; tunable */
    --cm-tile-gap: 1.5rem;
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(340px, 1fr));
    grid-auto-rows: var(--cm-tile-unit);
    gap: var(--cm-tile-gap);
    width: 100%;
}

/* Module card - spans var(--cm-tile-span) units (seeded server-side, snapped by JS) */
.module-card {
    grid-row: span var(--cm-tile-span, 1);
    overflow: hidden;
    background: var(--card-bg, #fff);
    border: 1px solid var(--border-color, #dee2e6);
    border-radius: 1rem;
    padding: 1.25rem;
    display: flex;
    flex-direction: column;
    gap: 0.85rem;
}

.module-header {
    display: flex;
    align-items: center;
    justify-content: flex-start;
    gap: 0.6rem;
}

.module-icon-img {
    width: 26px;
    height: 26px;
    flex-shrink: 0;
}

.module-icon-bi {
    font-size: 1.6rem;
    color: var(--accent-color, #10b981);
    flex-shrink: 0;
}

.module-header h3 {
    margin: 0;
    font-size: 1.05rem;
    font-weight: 600;
    color: var(--text-primary);
}

/* Pill button links - left-aligned, anchored to the top of the tile */
.module-links {
    display: flex;
    flex-wrap: wrap;
    justify-content: flex-start;
    align-content: flex-start;
    gap: 0.5rem;
}

.pill-link {
    display: inline-flex;
    align-items: center;
    gap: 0.35rem;
    padding: 0.35rem 0.7rem;
    border-radius: 8px;
    background: transparent;
    border: 1px solid var(--text-secondary, #6c757d);
    color: var(--text-primary);
    font-size: 0.82rem;
    font-weight: 500;
    text-decoration: none;
    transition: background 0.15s, transform 0.1s;
    white-space: nowrap;
}

.pill-link:hover {
    background: var(--hover-bg, rgba(255, 255, 255, 0.06));
    transform: translateY(-1px);
    text-decoration: none;
    color: var(--text-primary);
}

.pill-link:active {
    transform: translateY(0);
}

.pill-emoji {
    font-size: 1rem;
    line-height: 1;
}

.pill-link i.bi {
    font-size: 0.9rem;
}

.pill-icon-img {
    width: 1em;
    height: 1em;
    object-fit: contain;
    flex-shrink: 0;
}

/* Empty state */
.empty-state {
    text-align: center;
    padding: 3rem;
    color: var(--text-secondary, #6c757d);
}

.empty-state i {
    font-size: 3rem;
    margin-bottom: 1rem;
    display: block;
}

/* Responsive - single column on narrow screens */
@media (max-width: 640px) {
    .module-grid {
        grid-template-columns: 1fr;
    }

    .hero h1 {
        font-size: 1.3rem;
    }
}
```

- [ ] **Step 2: Build to verify the CSS compiles into the bundle**

Run: `dotnet build "C:/Users/jscha/source/repos/control-menu/src/ControlMenu/ControlMenu.csproj" -c Release --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git -C "C:/Users/jscha/source/repos/control-menu" add src/ControlMenu/Components/Pages/Home.razor.css
git -C "C:/Users/jscha/source/repos/control-menu" commit -m "feat(home): slim hero + modular tile grid styles"
```

---

## Task 5: Wire up `Home.razor` (seed span + interop + cleanup)

**Files:**
- Modify: `tests/ControlMenu.Tests/Components/Pages/HomeTests.cs`
- Modify: `src/ControlMenu/Components/Pages/Home.razor`

- [ ] **Step 1: Update the test fixture for JS interop and add the span-seed test**

In `tests/ControlMenu.Tests/Components/Pages/HomeTests.cs`, the constructor currently is:

```csharp
    public HomeTests()
    {
        _config.Setup(c => c.GetSettingAsync("setup-completed", null)).ReturnsAsync("true");
        Services.AddSingleton(_config.Object);
        // Home.razor injects IServiceProvider to evaluate NavEntry.IsVisible predicates.
        Services.AddSingleton<IServiceProvider>(sp => sp);
    }
```

Replace it with (adds loose JSInterop so the new `OnAfterRenderAsync` JS calls are no-ops in tests):

```csharp
    public HomeTests()
    {
        _config.Setup(c => c.GetSettingAsync("setup-completed", null)).ReturnsAsync("true");
        Services.AddSingleton(_config.Object);
        // Home.razor injects IServiceProvider to evaluate NavEntry.IsVisible predicates.
        Services.AddSingleton<IServiceProvider>(sp => sp);
        // Home.razor calls window.controlMenu.layoutHomeTiles / subscribeHomeTilesResize
        // from OnAfterRenderAsync; loose mode lets those unconfigured calls return default.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }
```

Then add this test (6 visible entries -> InitialSpan(6) == 2):

```csharp
    [Fact]
    public void ModuleCard_SeedsInitialTileSpan()
    {
        RegisterDiscovery(MakeModule("imaging", "Imaging Tools",
            new NavEntry("Icon Converter", "/imaging/icon-converter", null, 0),
            new NavEntry("Format Converter", "/imaging/format-converter", null, 1),
            new NavEntry("Image Resize", "/imaging/image-resize", null, 2),
            new NavEntry("SVG Rasterize", "/imaging/svg-rasterize", null, 3),
            new NavEntry("Magic Wand", "/imaging/magic-wand", null, 4),
            new NavEntry("Tracing", "/imaging/tracing", null, 5)));

        var cut = Render<ControlMenu.Components.Pages.Home>();

        var card = cut.FindAll(".module-card")
            .First(c => c.QuerySelector("h3")!.TextContent == "Imaging Tools");
        Assert.Contains("--cm-tile-span:2", card.GetAttribute("style"));
    }
```

Ensure the using block at the top of the file includes `using Bunit;` (already present).

- [ ] **Step 2: Run the new test to verify it fails**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests/ControlMenu.Tests.csproj" --filter "FullyQualifiedName~HomeTests.ModuleCard_SeedsInitialTileSpan"`
Expected: FAIL — the card has no `--cm-tile-span` in its `style` attribute yet (the assertion on style content fails).

- [ ] **Step 3: Update `Home.razor`**

In `src/ControlMenu/Components/Pages/Home.razor`:

**(a)** At the top, after the existing `@using` lines, add the interop directives:

```razor
@inject IJSRuntime JS
@implements IAsyncDisposable
```

**(b)** Replace the hero block:

```razor
        <div class="hero">
            <img src="/icon-512.png" alt="Control Menu" class="hero-icon" />
            <h1>Control Menu</h1>
            <p class="hero-subtitle">Manage your Android devices, media server, and utilities from one place.</p>
        </div>
```

with the slim two-part hero:

```razor
        <div class="hero">
            <img src="/icon-512.png" alt="Control Menu" class="hero-icon" />
            <div class="hero-text">
                <h1>Control Menu</h1>
                <p class="hero-subtitle">Manage your Android devices, media server, and utilities from one place.</p>
            </div>
        </div>
```

**(c)** Seed the span on the module card. Change the per-module card opening tag:

```razor
                        <div class="module-card">
```

to:

```razor
                        <div class="module-card" style="--cm-tile-span:@HomeTileLayout.InitialSpan(entries.Count)">
```

**(d)** Seed the span on the Settings card (it has 5 fixed links). Change:

```razor
                <div class="module-card">
                    <div class="module-header">
                        <i class="bi bi-gear module-icon-bi"></i>
                        <h3>Settings</h3>
                    </div>
```

to:

```razor
                <div class="module-card" style="--cm-tile-span:@HomeTileLayout.InitialSpan(5)">
                    <div class="module-header">
                        <i class="bi bi-gear module-icon-bi"></i>
                        <h3>Settings</h3>
                    </div>
```

**(e)** In the `@code` block, add the interop lifecycle (place after `OnInitializedAsync`):

```csharp
    private int? _resizeToken;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_setupDone)
            return;

        // Re-run on every render so JS-measured spans survive Blazor re-renders
        // (a re-render would otherwise reset --cm-tile-span to the server seed).
        await JS.InvokeVoidAsync("controlMenu.layoutHomeTiles");

        if (firstRender)
            _resizeToken = await JS.InvokeAsync<int>("controlMenu.subscribeHomeTilesResize");
    }

    public async ValueTask DisposeAsync()
    {
        if (_resizeToken is int token)
        {
            try
            {
                await JS.InvokeVoidAsync("controlMenu.unsubscribeHomeTilesResize", token);
            }
            catch
            {
                // Circuit may already be torn down on disposal; ignore.
            }
        }
    }
```

`HomeTileLayout` is in the same `ControlMenu.Components.Pages` namespace as `Home`, so no extra `@using` is needed.

- [ ] **Step 4: Run the home tests to verify they pass**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests/ControlMenu.Tests.csproj" --filter "FullyQualifiedName~HomeTests"`
Expected: PASS — all HomeTests pass, including `ModuleCard_SeedsInitialTileSpan` and the pre-existing structure tests (`.hero`, `.module-grid`, `.module-card`, Settings links, hidden-module, no-scanner-ui).

- [ ] **Step 5: Commit**

```bash
git -C "C:/Users/jscha/source/repos/control-menu" add src/ControlMenu/Components/Pages/Home.razor tests/ControlMenu.Tests/Components/Pages/HomeTests.cs
git -C "C:/Users/jscha/source/repos/control-menu" commit -m "feat(home): seed tile spans and wire measure-and-snap interop"
```

---

## Task 6: Full verification + manual smoke

**Files:** none (verification only).

- [ ] **Step 1: Full build + test suite**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests/ControlMenu.Tests.csproj" -c Release --nologo`
Expected: `Failed: 0` — the whole suite green (prior count + the 8 new assertions: 7 InitialSpan + 1 card-span-seed).

- [ ] **Step 2: Run the app and smoke the home page**

Run: `dotnet run --project "C:/Users/jscha/source/repos/control-menu/src/ControlMenu/ControlMenu.csproj" -c Release` and open `http://localhost:5159`.

Verify visually:
- Slim hero band (small icon + title + subtitle, divider beneath), not the tall centered block.
- Cards left-aligned headers; uniform tile heights; Imaging Tools spans two tile-units and lines up with two stacked single cards beside it.
- Resize the window narrower (to 2 columns, then 1): cards re-snap, no clipped pills, no overlaps.
- Toggle the theme (top-right): colors track light/dark via the theme variables.
- Confirm the Cameras card is absent when no cameras are registered (unchanged behavior).

- [ ] **Step 3: Stop the app**

Stop the `dotnet run` process (Ctrl+C in its terminal, or stop the background task).

---

## Self-review (author check against the spec)

- **Spec coverage:** slim hero (Task 4 + 5b); left-aligned headers (Task 4 `.module-header`/`.module-links`); modular tile grid with multi-unit spans (Task 4 grid + Task 5c/d seed + Task 2 JS snap); measure-and-snap JS with server first-paint seed (Tasks 1, 2, 5); theme-variable colors (Task 4); preserved behavior - module discovery / Cameras-hidden / Settings card / wizard gate / pill rendering (unchanged markup, asserted by existing HomeTests); responsive (Task 4 media query + JS resize re-snap); testing (Tasks 1, 5, 6). All spec sections map to a task.
- **Placeholder scan:** none — every code step is complete and copy-pasteable.
- **Type/name consistency:** `HomeTileLayout.InitialSpan(int)` defined in Task 1 and called in Task 5c/d; `--cm-tile-span` set by Task 5 + Task 2 and read by Task 4 CSS; `controlMenu.layoutHomeTiles` / `subscribeHomeTilesResize` / `unsubscribeHomeTilesResize` defined in Task 2 and called in Task 5e; `JSInterop.Mode = JSRuntimeMode.Loose` (Bunit) added in Task 5.
- **Known limitation:** the JS measure-and-snap is exercised by manual smoke (Task 6), not bUnit — bUnit has no layout engine. The `InitialSpan` estimate and the markup wiring are unit/bUnit covered; the future Playwright E2E (todo #15) would cover the rendered snap.
