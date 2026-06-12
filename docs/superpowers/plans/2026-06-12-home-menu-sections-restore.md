# Home Menu-Sections Restore — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the discovery-dashboard home page with the original menu-sections page (hero + one card per module + a Settings card), and delete the home-only scanner UI. Device/camera scanning is untouched — it stays in Settings + the setup Wizard.

**Architecture:** Restore `Home.razor` + `Home.razor.css` from commit `6cdda9b` (the state just before the scanner rewrite `ed147da`), with three deliberate deviations: (1) filter nav entries by `IsVisible`; (2) hide a module's card when it has no visible entries; (3) add the Cameras brand logo. The page is data-driven off `ModuleDiscoveryService.Modules`, so all six current modules (incl. Imaging Tools, added after `6cdda9b`) render automatically. Delete the four home-only components (`HomeScanBand`, `HomeDiscoveredAndroid`, `HomeDiscoveredCameras`, `HomeModuleTiles`) and their four tests; rework `HomeIntegrationTests` into a menu-sections render test.

**Tech Stack:** Blazor Server (.NET 10), Razor components + scoped CSS, bUnit + xUnit + Moq.

**Spec:** `docs/superpowers/specs/2026-06-12-home-menu-sections-restore-design.md`
**Branch:** `revert/home-menu-sections` (already created off `origin/master` @ `3d07282`).
**Repo root (all commands self-scoped to it):** `C:/Users/jscha/source/repos/control-menu`

---

## Task 1: Swap home page to menu-sections + remove scanner UI

This task is atomic by necessity — `Home.razor` references the scanner components, so the component swap, the deletions, and the test rework land in one commit that ends green.

**Files:**
- Create: `tests/ControlMenu.Tests/Components/Pages/HomeTests.cs`
- Overwrite: `src/ControlMenu/Components/Pages/Home.razor`
- Restore (verbatim from `6cdda9b`): `src/ControlMenu/Components/Pages/Home.razor.css`
- Delete (components): `src/ControlMenu/Components/Pages/HomeSections/HomeScanBand.razor` (+`.css`), `HomeDiscoveredAndroid.razor` (+`.css`), `HomeDiscoveredCameras.razor` (+`.css`), `HomeModuleTiles.razor` (+`.css`)
- Delete (tests): `tests/ControlMenu.Tests/Components/HomeSections/HomeScanBandTests.cs`, `HomeDiscoveredAndroidTests.cs`, `HomeDiscoveredCamerasTests.cs`, `HomeModuleTilesTests.cs`, `HomeIntegrationTests.cs`

---

- [ ] **Step 1: Write the new home page render test**

Create `tests/ControlMenu.Tests/Components/Pages/HomeTests.cs`:

```csharp
using System.Reflection;
using System.Runtime.CompilerServices;
using Bunit;
using ControlMenu.Modules;
using ControlMenu.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ControlMenu.Tests.Components.Pages;

public class HomeTests : BunitContext
{
    private readonly Mock<IConfigurationService> _config = new();

    public HomeTests()
    {
        _config.Setup(c => c.GetSettingAsync("setup-completed", null)).ReturnsAsync("true");
        Services.AddSingleton(_config.Object);
        // Home.razor injects IServiceProvider to evaluate NavEntry.IsVisible predicates.
        Services.AddSingleton<IServiceProvider>(sp => sp);
    }

    private static IToolModule MakeModule(string id, string name, params NavEntry[] entries)
    {
        var m = new Mock<IToolModule>();
        m.SetupGet(x => x.Id).Returns(id);
        m.SetupGet(x => x.DisplayName).Returns(name);
        m.SetupGet(x => x.Icon).Returns("bi-box");
        m.SetupGet(x => x.SortOrder).Returns(0);
        m.Setup(x => x.GetNavEntries()).Returns(entries);
        return m.Object;
    }

    // Bypass the reflection-based ModuleDiscoveryService ctor by setting the
    // compiler-generated backing field directly (mirrors the deleted HomeModuleTilesTests).
    private static ModuleDiscoveryService MakeDiscovery(IEnumerable<IToolModule> modules)
    {
        var svc = (ModuleDiscoveryService)RuntimeHelpers.GetUninitializedObject(typeof(ModuleDiscoveryService));
        var field = typeof(ModuleDiscoveryService)
            .GetField("<Modules>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(svc, (IReadOnlyList<IToolModule>)modules.ToList());
        return svc;
    }

    private void RegisterDiscovery(params IToolModule[] modules)
        => Services.AddSingleton(MakeDiscovery(modules));

    [Fact]
    public void SetupComplete_NoModules_RendersHeroAndEmptyState()
    {
        RegisterDiscovery();

        var cut = Render<ControlMenu.Components.Pages.Home>();

        Assert.Single(cut.FindAll(".hero"));
        Assert.Single(cut.FindAll(".empty-state"));
        Assert.Empty(cut.FindAll(".module-grid"));
    }

    [Fact]
    public void Module_WithVisibleEntries_RendersCardWithPillPerEntry()
    {
        RegisterDiscovery(MakeModule("imaging", "Imaging Tools",
            new NavEntry("Icon Converter", "/imaging/icon-converter", "bi-image", 0),
            new NavEntry("Tracing", "/imaging/tracing", "bi-pencil", 1)));

        var cut = Render<ControlMenu.Components.Pages.Home>();

        var headings = cut.FindAll(".module-card h3").Select(e => e.TextContent).ToList();
        Assert.Contains("Imaging Tools", headings);

        var card = cut.FindAll(".module-card").First(c => c.QuerySelector("h3")!.TextContent == "Imaging Tools");
        var pills = card.QuerySelectorAll(".module-links a.pill-link").ToList();
        Assert.Equal(2, pills.Count);
        Assert.Equal("/imaging/icon-converter", pills[0].GetAttribute("href"));
        Assert.Equal("/imaging/tracing", pills[1].GetAttribute("href"));
    }

    [Fact]
    public void Module_WithNoVisibleEntries_IsHidden()
    {
        // Cameras-style: GetNavEntries() returns nothing when no cameras are registered.
        RegisterDiscovery(MakeModule("cameras", "Cameras"));

        var cut = Render<ControlMenu.Components.Pages.Home>();

        var headings = cut.FindAll(".module-card h3").Select(e => e.TextContent).ToList();
        Assert.DoesNotContain("Cameras", headings);
        // Only the always-present Settings card remains.
        Assert.Single(cut.FindAll(".module-card"));
    }

    [Fact]
    public void Module_AllEntriesHiddenByPredicate_IsHidden()
    {
        RegisterDiscovery(MakeModule("m", "Hidden Mod",
            new NavEntry("Nope", "/nope", null, 0, _ => false)));

        var cut = Render<ControlMenu.Components.Pages.Home>();

        var headings = cut.FindAll(".module-card h3").Select(e => e.TextContent).ToList();
        Assert.DoesNotContain("Hidden Mod", headings);
    }

    [Fact]
    public void Module_PartiallyHiddenEntries_RendersOnlyVisiblePills()
    {
        RegisterDiscovery(MakeModule("m", "Mod",
            new NavEntry("Hidden", "/hidden", null, 0, _ => false),
            new NavEntry("Shown", "/shown", null, 1)));

        var cut = Render<ControlMenu.Components.Pages.Home>();

        var card = cut.FindAll(".module-card").First(c => c.QuerySelector("h3")!.TextContent == "Mod");
        var pills = card.QuerySelectorAll(".module-links a.pill-link").ToList();
        Assert.Single(pills);
        Assert.Equal("/shown", pills[0].GetAttribute("href"));
    }

    [Fact]
    public void SettingsCard_LinksToCanonicalSections()
    {
        RegisterDiscovery(MakeModule("imaging", "Imaging Tools",
            new NavEntry("Icon Converter", "/imaging/icon-converter", null, 0)));

        var cut = Render<ControlMenu.Components.Pages.Home>();

        var settings = cut.FindAll(".module-card").First(c => c.QuerySelector("h3")!.TextContent == "Settings");
        var hrefs = settings.QuerySelectorAll("a.pill-link").Select(a => a.GetAttribute("href")).ToList();
        Assert.Equal(new[]
        {
            "/settings/general",
            "/settings/jellyfin",
            "/settings/devices",
            "/settings/cameras",
            "/settings/dependencies",
        }, hrefs);
    }

    [Fact]
    public void NoScannerUi_OnHome()
    {
        RegisterDiscovery(MakeModule("imaging", "Imaging Tools",
            new NavEntry("Icon Converter", "/imaging/icon-converter", null, 0)));

        var cut = Render<ControlMenu.Components.Pages.Home>();

        // Discovery-dashboard artifacts must be gone.
        Assert.Empty(cut.FindAll(".home-tiles-band"));
        Assert.Empty(cut.FindAll(".home-status"));
        Assert.Single(cut.FindAll(".module-grid"));
    }

    [Fact]
    public void SetupNotComplete_RendersWizardOnly()
    {
        _config.Setup(c => c.GetSettingAsync("setup-completed", null)).ReturnsAsync((string?)null);
        RegisterDiscovery();

        var cut = Render<ControlMenu.Components.Pages.Home>();

        Assert.Empty(cut.FindAll(".home-container"));
    }
}
```

- [ ] **Step 2: Run the new test to verify it fails**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests/ControlMenu.Tests.csproj" --filter "FullyQualifiedName~Components.Pages.HomeTests"`
Expected: FAIL — the current discovery-dashboard `Home.razor` renders `.home-tiles-band`/`.home-status` (not `.hero`/`.module-grid`) and requires scan services this harness doesn't register, so the renders throw / assertions fail. (Clean red — proves the new layout isn't in place yet.)

- [ ] **Step 3: Overwrite `Home.razor` with the menu-sections layout**

Write `src/ControlMenu/Components/Pages/Home.razor` with exactly this content:

```razor
@page "/"
@using ControlMenu.Modules
@using ControlMenu.Services

<PageTitle>Control Menu</PageTitle>

@if (!_setupDone)
{
    <SetupWizard />
}
else
{
    <div class="home-container">
        <div class="hero">
            <img src="/icon-512.png" alt="Control Menu" class="hero-icon" />
            <h1>Control Menu</h1>
            <p class="hero-subtitle">Manage your Android devices, media server, and utilities from one place.</p>
        </div>

        @if (ModuleDiscovery.Modules.Count == 0)
        {
            <div class="empty-state">
                <i class="bi bi-box-seam"></i>
                <h2>No modules loaded</h2>
                <p>No modules are currently loaded. Check your configuration or re-run the setup wizard.</p>
            </div>
        }
        else
        {
            <div class="module-grid">
                @foreach (var module in ModuleDiscovery.Modules)
                {
                    var entries = module.GetNavEntries()
                        .Where(e => e.IsVisible is null || e.IsVisible(ServiceProvider))
                        .OrderBy(e => e.SortOrder)
                        .ToList();
                    if (entries.Count > 0)
                    {
                        <div class="module-card">
                            <div class="module-header">
                                @if (ModuleImageMap.TryGetValue(module.Id, out var imagePath))
                                {
                                    <img src="@imagePath" alt="@module.DisplayName" class="module-icon-img" />
                                }
                                else
                                {
                                    <i class="bi @module.Icon module-icon-bi"></i>
                                }
                                <h3>@module.DisplayName</h3>
                            </div>
                            <div class="module-links">
                                @foreach (var entry in entries)
                                {
                                    <a href="@entry.Href" class="pill-link">
                                        @if (entry.Icon is not null)
                                        {
                                            @if (entry.Icon.StartsWith("bi-"))
                                            {
                                                <i class="bi @entry.Icon"></i>
                                            }
                                            else if (entry.Icon.StartsWith("/") || entry.Icon.EndsWith(".svg"))
                                            {
                                                <img src="@entry.Icon" alt="" class="pill-icon-img" />
                                            }
                                            else
                                            {
                                                <span class="pill-emoji">@entry.Icon</span>
                                            }
                                        }
                                        <span>@entry.Title</span>
                                    </a>
                                }
                            </div>
                        </div>
                    }
                }

                <div class="module-card">
                    <div class="module-header">
                        <i class="bi bi-gear module-icon-bi"></i>
                        <h3>Settings</h3>
                    </div>
                    <div class="module-links">
                        <a href="/settings/general" class="pill-link">
                            <i class="bi bi-sliders"></i>
                            <span>General</span>
                        </a>
                        <a href="/settings/jellyfin" class="pill-link">
                            <i class="bi bi-film"></i>
                            <span>Jellyfin</span>
                        </a>
                        <a href="/settings/devices" class="pill-link">
                            <i class="bi bi-phone"></i>
                            <span>Android Devices</span>
                        </a>
                        <a href="/settings/cameras" class="pill-link">
                            <i class="bi bi-camera-video"></i>
                            <span>Cameras</span>
                        </a>
                        <a href="/settings/dependencies" class="pill-link">
                            <i class="bi bi-box-seam"></i>
                            <span>Dependencies</span>
                        </a>
                    </div>
                </div>
            </div>
        }
    </div>
}

@code {
    [Inject]
    private ModuleDiscoveryService ModuleDiscovery { get; set; } = default!;

    [Inject]
    private IConfigurationService Config { get; set; } = default!;

    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = default!;

    private bool _setupDone = true;

    private static readonly Dictionary<string, string> ModuleImageMap = new()
    {
        ["android-devices"] = "/images/android-logo.svg",
        ["jellyfin"] = "/images/jellyfin-logo.svg",
        ["cameras"] = "/images/cameras-logo.svg"
    };

    protected override async Task OnInitializedAsync()
    {
        var flag = await Config.GetSettingAsync("setup-completed");
        if (flag != "true")
            _setupDone = false;
    }
}
```

- [ ] **Step 4: Restore `Home.razor.css` verbatim from `6cdda9b`**

`git checkout <commit> -- <path>` restores exact bytes and stages the file (no shell-redirect encoding pitfalls):

```bash
git -C "C:/Users/jscha/source/repos/control-menu" checkout 6cdda9b -- src/ControlMenu/Components/Pages/Home.razor.css
```

This CSS already uses theme tokens (`var(--text-primary)`, `var(--card-bg, #fff)`, etc.), so dark/light should both read; confirm visually at Step 9.

- [ ] **Step 5: Delete the four home-only scanner components**

```bash
git -C "C:/Users/jscha/source/repos/control-menu" rm \
  src/ControlMenu/Components/Pages/HomeSections/HomeScanBand.razor \
  src/ControlMenu/Components/Pages/HomeSections/HomeScanBand.razor.css \
  src/ControlMenu/Components/Pages/HomeSections/HomeDiscoveredAndroid.razor \
  src/ControlMenu/Components/Pages/HomeSections/HomeDiscoveredAndroid.razor.css \
  src/ControlMenu/Components/Pages/HomeSections/HomeDiscoveredCameras.razor \
  src/ControlMenu/Components/Pages/HomeSections/HomeDiscoveredCameras.razor.css \
  src/ControlMenu/Components/Pages/HomeSections/HomeModuleTiles.razor \
  src/ControlMenu/Components/Pages/HomeSections/HomeModuleTiles.razor.css
```

(These are referenced only by the old `Home.razor`, now replaced. The shared `Components/Shared/Scanner/DiscoveredPanel` and `Components/Shared/Cameras/DiscoveredCamerasPanel` are NOT in this list — they stay.)

- [ ] **Step 6: Delete the four scanner tests and the old integration test**

```bash
git -C "C:/Users/jscha/source/repos/control-menu" rm \
  tests/ControlMenu.Tests/Components/HomeSections/HomeScanBandTests.cs \
  tests/ControlMenu.Tests/Components/HomeSections/HomeDiscoveredAndroidTests.cs \
  tests/ControlMenu.Tests/Components/HomeSections/HomeDiscoveredCamerasTests.cs \
  tests/ControlMenu.Tests/Components/HomeSections/HomeModuleTilesTests.cs \
  tests/ControlMenu.Tests/Components/HomeSections/HomeIntegrationTests.cs
```

- [ ] **Step 7: Verify no stale references to the deleted components remain in source**

Run (Grep tool, or):
```bash
git -C "C:/Users/jscha/source/repos/control-menu" grep -nE "HomeScanBand|HomeDiscoveredAndroid|HomeDiscoveredCameras|HomeModuleTiles" -- "src/*" "tests/*"
```
Expected: **no matches** in `src/` or `tests/`. (Matches in `docs/` and `CHANGELOG.md` are prose, handled in Task 2.)

- [ ] **Step 8: Build + run the full test suite**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests/ControlMenu.Tests.csproj"`
Expected: build succeeds; all tests PASS. The new `HomeTests` (8 tests) pass; total count is lower than the pre-change 457 (the 4 scanner test classes + the old integration test are gone). No failures.

- [ ] **Step 9: Manual smoke (optional but recommended)**

Run: `dotnet run --project "C:/Users/jscha/source/repos/control-menu/src/ControlMenu/ControlMenu.csproj" -c Release` → open `http://localhost:5159`.
Confirm: hero + module cards (Android Devices, Android Power Tools, Jellyfin, Utilities, Imaging Tools; Cameras only if cameras are registered) + a Settings card; every pill navigates; no scan band / discovered panels; toggle dark/light — both readable. Stop the app when done.

- [ ] **Step 10: Commit**

```bash
git -C "C:/Users/jscha/source/repos/control-menu" add -A
git -C "C:/Users/jscha/source/repos/control-menu" commit -m "feat(home): restore menu-sections home page; remove home-page scanner UI"
```

---

## Task 2: Docs — guide, CHANGELOG, supersede the polish spec/plan

**Files:**
- Modify: `docs/TECHNICAL_GUIDE.md`
- Modify: `CHANGELOG.md`
- Modify: `docs/superpowers/specs/2026-05-05-homepage-polish-design.md`
- Modify: `docs/superpowers/plans/2026-05-05-homepage-polish.md`

---

- [ ] **Step 1: Update the component-tree section of `TECHNICAL_GUIDE.md`**

Replace these two lines (around line 81-82):

```
      Home.razor            # Discovery-dashboard composition (HomeSections children)
      HomeSections/         # HomeScanBand, HomeDiscoveredAndroid, HomeDiscoveredCameras, HomeModuleTiles
```

with this single line (the `HomeSections/` folder no longer exists):

```
      Home.razor            # Menu-sections page (hero + per-module cards + Settings card)
```

- [ ] **Step 2: Catch any remaining prose references in `TECHNICAL_GUIDE.md`**

Run (Grep tool, or):
```bash
git -C "C:/Users/jscha/source/repos/control-menu" grep -nE "discovery dashboard|HomeScanBand|HomeDiscovered|HomeModuleTiles|scan band" -- docs/TECHNICAL_GUIDE.md
```
For each match, rewrite the sentence to describe the menu-sections home (hero + per-module cards driven by `ModuleDiscoveryService`; scanning lives in Settings → Devices / Cameras and the setup Wizard). Leave references to the *shared* `Scanner/DiscoveredPanel` and `Cameras/DiscoveredCamerasPanel` intact — those components remain.

- [ ] **Step 3: Add a CHANGELOG `[Unreleased]` entry**

In `CHANGELOG.md`, under the empty `## [Unreleased]` (line 8), insert:

```markdown
## [Unreleased]

### Changed

- **Home page reverted to the menu-sections layout** — a hero plus one card per module (sub-pages as pill links) and a Settings card, driven by the module registry. The discovery-dashboard home (scan buttons + live "Discovered Android/Cameras" panels) is removed. Device and camera **scanning is unchanged** — it remains in Settings → Android Devices / Cameras and the setup Wizard.
```

- [ ] **Step 4: Mark the 2026-05-05 homepage-polish spec + plan as superseded**

At the top of `docs/superpowers/specs/2026-05-05-homepage-polish-design.md` (immediately after the `# Homepage polish — Dashboard-first redesign` H1), insert:

```markdown
> **SUPERSEDED (2026-06-12):** The dashboard-first home page described here was reverted to the menu-sections layout. See `docs/superpowers/specs/2026-06-12-home-menu-sections-restore-design.md`. Kept for history.
```

At the top of `docs/superpowers/plans/2026-05-05-homepage-polish.md` (immediately after the `# Homepage Polish Implementation Plan` H1), insert:

```markdown
> **SUPERSEDED (2026-06-12):** Implemented, then reverted. See `docs/superpowers/plans/2026-06-12-home-menu-sections-restore.md`. Kept for history.
```

- [ ] **Step 5: Commit**

```bash
git -C "C:/Users/jscha/source/repos/control-menu" add -A
git -C "C:/Users/jscha/source/repos/control-menu" commit -m "docs(home): update guide + CHANGELOG for menu-sections home; supersede polish spec/plan"
```

---

## Out of plan (session wrap-up, not repo changes)

- **Close TODO #22 as superseded** — archive it from `todo_control_menu.md` to `archive/todo_control_menu_shipped.md` with a "superseded by the home menu-sections restore (2026-06-12)" note. (Memory file, not in the repo PR.)
- **Open the PR** with a `release:beta` (or `release:none`) label at `gh pr create` time (per the auto-release label rule), squash-merge after CI green.
- **Update the breadcrumb** for the next session.

## Self-review notes (spec coverage)

- Spec "restored layout (data-driven)" → Task 1 Steps 3-4. ✓
- Deviation 1 (IsVisible filter) → Home.razor `entries` filter + `Module_PartiallyHiddenEntries`/`Module_AllEntriesHiddenByPredicate` tests. ✓
- Deviation 2 (hide empty cards) → `if (entries.Count > 0)` + `Module_WithNoVisibleEntries_IsHidden` test. ✓
- Deviation 3 (cameras logo) → `ModuleImageMap`. ✓
- Deviation 4 (Settings card order/labels) → Settings card markup + `SettingsCard_LinksToCanonicalSections` test. ✓
- Deviation 5 (theme tokens) → Step 4 note + Step 9 manual check. ✓
- Files delete/keep → Steps 5-7. ✓
- Tests rework + count drop → Steps 1, 8. ✓
- Docs + #22 + CHANGELOG → Task 2 + Out-of-plan. ✓
