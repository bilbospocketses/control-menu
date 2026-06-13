# Home Page — Modular Tile Layout (design)

- **Date:** 2026-06-12
- **Status:** Approved in brainstorming; pending implementation plan
- **Repo / branch:** control-menu · `feat/home-tile-layout` (off master `1aced98`)
- **Scope:** Presentation-layer redesign of the home page (`/`) only.

## Goal

Re-lay-out the home page's category cards so the grid reads as **uniform, aligned tiles** while keeping **dead space to a minimum**. The "one card per category, links as pills" concept stays — the user likes it. What changes is the arrangement.

Today (`Home.razor` + `Home.razor.css`) the grid is `repeat(auto-fit, minmax(340px, 1fr))` with cards that stretch to the tallest card in their row. Consequences the redesign fixes:

- Sparse cards (e.g. Android Devices with a single link) stretch to match a tall neighbor, carrying large empty areas.
- The hero is a 120px centered icon + stacked title that pushes the cards well down the page.

## Approved visual design

Validated against the running app and through iterative mockups in brainstorming. Decisions:

1. **Slim hero.** A compact horizontal band — app icon, "Control Menu" title, and subtitle grouped in one row with a divider beneath — replacing the tall centered icon + stacked title.
2. **Left-aligned card headers.** Icon chip + category name on the left of each card (today they are centered).
3. **Modular tile grid.** A uniform **tile-unit** height. Each card occupies a whole number of tile-units — its **span**:
   - A single-link category is **1 unit**.
   - Imaging Tools (6 links) is **2 units**.
   - A future category with more links spans **3, 4, … units** as needed.
   - A multi-unit card lines up exactly with that many single-unit cards stacked beside it, so card tops and bottoms align across the grid.
   - Remaining dead space is confined to the bottom of multi-unit cards — explicitly accepted in exchange for the uniform grid.
4. **Theme-aware.** Uses the existing CSS theme variables (`--card-bg`, `--border-color`, `--text-primary`, `--accent-color`, etc.) so it works in both light and dark mode. (Brainstorm mockups were rendered dark only to match the running app.)
5. **Pills unchanged.** The existing pill-link rendering (Bootstrap-icon / image / emoji + label) is retained as-is.

## Span determination — measure-and-snap (JS)

**Decided:** each card's span is measured at runtime and snapped to a whole number of tile-units. This is what makes any future card "just work" (including the 3- and 4-row cases the user called out) and stay correct across responsive widths. Chosen over a server-side `entryCount` estimate because pill wrapping is width-dependent, so only a real measurement is reliable.

Mechanics:

- **CSS.** `.module-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(340px, 1fr)); grid-auto-rows: var(--tile-unit); gap: var(--tile-gap); }` (keeps today's responsive column rule). Cards no longer force equal height; each receives `grid-row-end: span N`.
- **Tile unit.** The height of a single-link card (header + one pill row + padding), expressed as a tunable `--tile-unit` CSS variable (starting value ~7.5rem, the measured single-card height); `--tile-gap` for the gap (1.5rem, matching today's grid).
- **JS module** (`wwwroot/js/home-tiles.js`, ES module): for each `.module-card`, read its natural content height and set `span = max(1, ceil((height + gap) / (unit + gap)))` via `style.gridRowEnd`. Run once after render and again on a **debounced** `window.resize`.
- **Blazor wiring.** `Home.razor` imports the module as an `IJSObjectReference` in `OnAfterRenderAsync`, invokes the layout pass, registers the resize listener, and disposes both on teardown (`IAsyncDisposable`). Follows the JS-interop guidance in `master_blazor_patterns` (module import + handle, explicit disposal; no returning bare JS functions across interop).
- **First-paint mitigation.** Before JS runs, set an initial **server-side span estimate** per card (`ceil(entryCount / 3)` heuristic — roughly three pills per tile-unit, tunable) as the default `grid-row-end`, so the first server-rendered paint is close; the JS pass then refines each card to its measured value. This avoids a visible overlap/reflow on load (and degrades gracefully if interop is delayed).

## Files touched

- `src/ControlMenu/Components/Pages/Home.razor` — slim hero markup; grid container; per-card initial span estimate; `OnAfterRenderAsync` interop + `IAsyncDisposable` cleanup.
- `src/ControlMenu/Components/Pages/Home.razor.css` — slim hero styles; `.module-grid` tile rows + gap + `--tile-unit`/`--tile-gap`; `.module-card` (drop `align-items: center`, left-align `.module-header`, remove forced stretch); responsive breakpoints.
- `src/ControlMenu/wwwroot/js/home-tiles.js` (new) — measure-and-snap pass + debounced resize handler, exported as an ES module.

No binary or runtime dependencies are involved (front-end CSS/JS only), so the Local-Dependencies-Only rule does not apply here.

## Preserved behavior (unchanged)

- Module discovery: one card per module that has at least one **visible** nav entry; the Cameras card stays hidden when it has none (current behavior — confirmed in the running app).
- The hardcoded **Settings** card and its links.
- The setup-wizard gate (`_setupDone`), the no-modules empty state, and pill-link icon rendering.

## Responsive behavior

- Columns remain responsive (wide → 3, then 2, then 1), via the existing `auto-fit`/`minmax` (or explicit breakpoints if the unit math needs it).
- Spans are re-measured on resize, so they stay correct as cards re-wrap at narrower widths.
- Single column (mobile): each card simply stacks at its unit-multiple height; the uniform aesthetic still holds.

## Edge cases

- **Tall future card (3–4+ units):** handled automatically by measure-and-snap.
- **Dynamic content** (e.g. a camera added so the Cameras card appears): the component re-renders and the measure pass re-runs.
- **Last row not full:** a trailing empty cell or two is acceptable — this is the "minimal dead space" the user signed off on.
- **Pre-interop / JS delayed:** the server-side span estimate gives a close first paint; full accuracy once the module loads.

## Testing

- **bUnit (`Home`):** a card renders per module-with-entries; the Settings card is present; the Cameras card is absent when it has no entries; hero + subtitle render; the empty state shows when no modules load. (bUnit has no real layout engine, so the JS snap itself is not bUnit-verifiable.)
- **Span calc:** keep it a small isolated JS function so it is reviewable and can be exercised by the future Playwright E2E suite (todo #15); verified by manual visual smoke for now.
- **Manual visual smoke:** wide / medium / narrow widths; confirm Imaging spans 2 units and tiles align; confirm both light and dark themes.

## Out of scope

- Module nav-entry content, the sidebar, breadcrumb, and theme toggle.
- Any module or service logic. This is a home-page presentation change only.
