# Homepage Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current Home module-card grid with a discovery-first dashboard: scan-action header, live Discovered sections (Android + Cameras) embedding the existing shared panels, and a compact 6-tile module-nav row.

**Architecture:** `Home.razor` becomes a thin composition of four new child components. Existing scan infrastructure (`IAdbService.ScanMdnsAsync`, `ICameraScanService.StartOnvifOnlyScanAsync`), existing notifiers (`IDeviceChangeNotifier`, `ICameraChangeNotifier`), and existing shared panels (`DiscoveredPanel`, `DiscoveredCamerasPanel`) are reused as-is — this plan adds wrappers and composition, no service-layer changes.

**Tech Stack:** Blazor Server, .NET 9, bUnit + Moq for tests, scoped CSS per component (`.razor` + `.razor.css`).

**Branch:** `feature/homepage-polish` (already created; spec at `docs/superpowers/specs/2026-05-05-homepage-polish-design.md` already committed).

**Spec source of truth:** `docs/superpowers/specs/2026-05-05-homepage-polish-design.md`. If a step here disagrees with the spec, the spec wins — flag the discrepancy before proceeding.

---

## File Structure

**New files:**
- `src/ControlMenu/Components/Pages/Home/HomeScanBand.razor` + `.razor.css`
- `src/ControlMenu/Components/Pages/Home/HomeDiscoveredAndroid.razor` + `.razor.css`
- `src/ControlMenu/Components/Pages/Home/HomeDiscoveredCameras.razor` + `.razor.css`
- `src/ControlMenu/Components/Pages/Home/HomeModuleTiles.razor` + `.razor.css`
- `tests/ControlMenu.Tests/Components/Home/HomeScanBandTests.cs`
- `tests/ControlMenu.Tests/Components/Home/HomeDiscoveredAndroidTests.cs`
- `tests/ControlMenu.Tests/Components/Home/HomeDiscoveredCamerasTests.cs`
- `tests/ControlMenu.Tests/Components/Home/HomeModuleTilesTests.cs`
- `tests/ControlMenu.Tests/Components/Home/HomeIntegrationTests.cs`

**Modified files:**
- `src/ControlMenu/Components/Pages/Home.razor` — full rewrite (the existing setup-wizard guard stays; everything below it is replaced)
- `CHANGELOG.md` — `[Unreleased]` → Changed entry

The new components live under `Components/Pages/Home/` (a new directory) so Home's pieces stay co-located rather than scattered into `Components/Shared/`. Each component has a single responsibility per spec.

---

## Task 1: HomeScanBand — failing test for idle state

**Files:**
- Create: `tests/ControlMenu.Tests/Components/Home/HomeScanBandTests.cs`

- [ ] **Step 1: Create the test class skeleton with one failing test**

```csharp
using Bunit;
using ControlMenu.Components.Pages.Home;
using Microsoft.AspNetCore.Components;

namespace ControlMenu.Tests.Components.Home;

public class HomeScanBandTests : TestContext
{
    [Fact]
    public void Idle_RendersThreeButtons_FixedWidth_WithIdleLabels()
    {
        var cut = RenderComponent<HomeScanBand>(p => p
            .Add(c => c.AndroidRunning, false)
            .Add(c => c.CamerasRunning, false)
            .Add(c => c.AllRunning, false)
            .Add(c => c.OnScanAndroid, EventCallback.Empty)
            .Add(c => c.OnScanCameras, EventCallback.Empty)
            .Add(c => c.OnScanAll, EventCallback.Empty));

        var buttons = cut.FindAll("button.scan-button");
        Assert.Equal(3, buttons.Count);
        Assert.Contains("Scan Android", buttons[0].TextContent);
        Assert.Contains("Scan Cameras", buttons[1].TextContent);
        Assert.Contains("Scan All", buttons[2].TextContent);
        // Each button must have the fixed-width class regardless of state
        Assert.All(buttons, b => Assert.Contains("scan-button", b.GetAttribute("class")!));
    }
}
```

- [ ] **Step 2: Run the test and verify it fails**

```
cd C:/Users/jscha/source/repos/control-menu
dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~HomeScanBandTests"
```

Expected: build error — `HomeScanBand` type does not exist.

- [ ] **Step 3: Commit the failing test**

```
git add tests/ControlMenu.Tests/Components/Home/HomeScanBandTests.cs
git commit -m "test(home): failing test for HomeScanBand idle state"
```

---

## Task 2: HomeScanBand — minimal component to pass idle test

**Files:**
- Create: `src/ControlMenu/Components/Pages/Home/HomeScanBand.razor`
- Create: `src/ControlMenu/Components/Pages/Home/HomeScanBand.razor.css`

- [ ] **Step 1: Create the component**

```razor
@* HomeScanBand.razor *@
<div class="scan-band">
    <button class="scan-button scan-button-android" type="button"
            disabled="@AndroidRunning" @onclick="HandleScanAndroid">
        @AndroidLabel
    </button>
    <button class="scan-button scan-button-cameras" type="button"
            disabled="@CamerasRunning" @onclick="HandleScanCameras">
        @CamerasLabel
    </button>
    <button class="scan-button scan-button-all" type="button"
            disabled="@AllRunning" @onclick="HandleScanAll">
        @AllLabel
    </button>
</div>

@code {
    [Parameter] public bool AndroidRunning { get; set; }
    [Parameter] public bool CamerasRunning { get; set; }
    [Parameter] public bool AllRunning { get; set; }
    [Parameter] public EventCallback OnScanAndroid { get; set; }
    [Parameter] public EventCallback OnScanCameras { get; set; }
    [Parameter] public EventCallback OnScanAll { get; set; }

    private string AndroidLabel => AndroidRunning ? "⏳ Scanning Android…" : "⚡ Scan Android";
    private string CamerasLabel => CamerasRunning ? "⏳ Scanning Cameras…" : "⚡ Scan Cameras";
    private string AllLabel => AllRunning ? "⏳ Scanning All…" : "⚡ Scan All";

    private Task HandleScanAndroid() => OnScanAndroid.InvokeAsync();
    private Task HandleScanCameras() => OnScanCameras.InvokeAsync();
    private Task HandleScanAll() => OnScanAll.InvokeAsync();
}
```

- [ ] **Step 2: Create the scoped CSS**

```css
/* HomeScanBand.razor.css */
.scan-band {
    display: flex;
    gap: 0.5rem;
    align-items: center;
    justify-content: flex-end;
}

.scan-button {
    width: 140px;
    padding: 0.5rem 0.75rem;
    border: 0;
    border-radius: 6px;
    color: white;
    font-size: 0.8125rem;
    font-weight: 500;
    cursor: pointer;
    white-space: nowrap;
    transition: opacity 0.15s ease;
}

.scan-button:disabled {
    opacity: 0.6;
    cursor: not-allowed;
}

.scan-button-android  { background: #2d6cdf; }
.scan-button-cameras  { background: #e74c3c; }
.scan-button-all      { background: #6b7280; }
```

- [ ] **Step 3: Run the test and verify it passes**

```
dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~HomeScanBandTests"
```

Expected: 1 test passed.

- [ ] **Step 4: Commit**

```
git add src/ControlMenu/Components/Pages/Home/
git commit -m "feat(home): HomeScanBand idle-state component + scoped CSS"
```

---

## Task 3: HomeScanBand — running-state and click-callback tests

**Files:**
- Modify: `tests/ControlMenu.Tests/Components/Home/HomeScanBandTests.cs`

- [ ] **Step 1: Append failing tests for running labels and disabled state**

Append these methods inside `HomeScanBandTests`:

```csharp
[Fact]
public void Running_AndroidOnly_ShowsScanningLabel_AndroidDisabled_OthersIdle()
{
    var cut = RenderComponent<HomeScanBand>(p => p
        .Add(c => c.AndroidRunning, true)
        .Add(c => c.CamerasRunning, false)
        .Add(c => c.AllRunning, false));

    var buttons = cut.FindAll("button.scan-button");
    Assert.Contains("⏳ Scanning Android…", buttons[0].TextContent);
    Assert.True(buttons[0].HasAttribute("disabled"));
    Assert.Contains("⚡ Scan Cameras", buttons[1].TextContent);
    Assert.False(buttons[1].HasAttribute("disabled"));
    Assert.Contains("⚡ Scan All", buttons[2].TextContent);
    Assert.False(buttons[2].HasAttribute("disabled"));
}

[Fact]
public void Running_AllRunning_AllButtonsDisabled_AllShowRunningLabel()
{
    var cut = RenderComponent<HomeScanBand>(p => p
        .Add(c => c.AndroidRunning, true)
        .Add(c => c.CamerasRunning, true)
        .Add(c => c.AllRunning, true));

    var buttons = cut.FindAll("button.scan-button");
    Assert.All(buttons, b => Assert.True(b.HasAttribute("disabled")));
    Assert.Contains("⏳ Scanning Android…", buttons[0].TextContent);
    Assert.Contains("⏳ Scanning Cameras…", buttons[1].TextContent);
    Assert.Contains("⏳ Scanning All…", buttons[2].TextContent);
}

[Fact]
public async Task Click_AndroidButton_FiresOnScanAndroidCallback()
{
    var fired = false;
    var cut = RenderComponent<HomeScanBand>(p => p
        .Add(c => c.OnScanAndroid, EventCallback.Factory.Create(this, () => { fired = true; })));

    await cut.Find("button.scan-button-android").ClickAsync(new());
    Assert.True(fired);
}
```

- [ ] **Step 2: Run tests — all 4 pass without component changes**

```
dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~HomeScanBandTests"
```

Expected: 4/4 pass. The minimal component already covers running labels via the ternaries.

- [ ] **Step 3: Commit**

```
git add tests/ControlMenu.Tests/Components/Home/HomeScanBandTests.cs
git commit -m "test(home): HomeScanBand running-state and click-callback coverage"
```

---

## Task 4: HomeDiscoveredAndroid — failing test for empty/cold state

**Files:**
- Create: `tests/ControlMenu.Tests/Components/Home/HomeDiscoveredAndroidTests.cs`

- [ ] **Step 1: Create the test class with empty-cold-state test**

```csharp
using Bunit;
using ControlMenu.Components.Pages.Home;
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Services;
using ControlMenu.Services.Network;
using ControlMenu.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ControlMenu.Tests.Components.Home;

public class HomeDiscoveredAndroidTests : TestContext
{
    private readonly Mock<IScanLifecycleHandler> _handler = new();
    private readonly Mock<IDeviceChangeNotifier> _notifier = new();
    private readonly Mock<IDeviceService> _deviceService = new();

    public HomeDiscoveredAndroidTests()
    {
        _handler.Setup(h => h.Discovered).Returns(new List<DiscoveredDevice>());
        _handler.Setup(h => h.Phase).Returns(ScanPhase.Idle);
        _deviceService.Setup(s => s.GetAllDevicesAsync()).ReturnsAsync(new List<Device>());

        Services.AddSingleton(_handler.Object);
        Services.AddSingleton(_notifier.Object);
        Services.AddSingleton(_deviceService.Object);
    }

    [Fact]
    public void EmptyCold_NoScanRun_RendersNothing()
    {
        var cut = RenderComponent<HomeDiscoveredAndroid>(p => p
            .Add(c => c.HasScanned, false));

        // Section is fully hidden when no scan has run and discovered list is empty
        Assert.Empty(cut.FindAll(".home-disc-section"));
    }
}
```

- [ ] **Step 2: Run, verify failure (`HomeDiscoveredAndroid` not found)**

```
dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~HomeDiscoveredAndroidTests"
```

Expected: build error.

- [ ] **Step 3: Commit failing test**

```
git add tests/ControlMenu.Tests/Components/Home/HomeDiscoveredAndroidTests.cs
git commit -m "test(home): failing test for HomeDiscoveredAndroid empty-cold state"
```

---

## Task 5: HomeDiscoveredAndroid — minimal wrapper component

**Files:**
- Create: `src/ControlMenu/Components/Pages/Home/HomeDiscoveredAndroid.razor`
- Create: `src/ControlMenu/Components/Pages/Home/HomeDiscoveredAndroid.razor.css`

- [ ] **Step 1: Create the wrapper component**

```razor
@* HomeDiscoveredAndroid.razor *@
@using ControlMenu.Components.Shared.Scanner
@using ControlMenu.Data.Entities
@using ControlMenu.Modules.AndroidDevices.Services
@using ControlMenu.Services
@using ControlMenu.Services.Network
@inject IScanLifecycleHandler Handler
@inject IDeviceChangeNotifier Notifier
@inject IDeviceService DeviceService
@implements IDisposable

@if (HasScanned || Handler.Discovered.Count > 0)
{
    <section class="home-disc-section">
        <div class="home-disc-header">
            <span class="home-disc-label">DISCOVERED — ANDROID</span>
            <span class="home-disc-count home-disc-count-android">@Handler.Discovered.Count</span>
        </div>
        @if (Handler.Discovered.Count == 0)
        {
            <p class="home-disc-empty">No Android devices found on the last scan.</p>
        }
        else
        {
            <DiscoveredPanel Discovered="Handler.Discovered"
                             Registered="_devices"
                             OnAdd="HandleInlineAdd"
                             OnDismiss="Handler.Dismiss" />
        }
    </section>
}

@code {
    [Parameter] public bool HasScanned { get; set; }
    [Parameter] public EventCallback OnDeviceAdded { get; set; }

    private List<Device> _devices = [];

    protected override async Task OnInitializedAsync()
    {
        _devices = (await DeviceService.GetAllDevicesAsync()).ToList();
        Notifier.Changed += HandleNotifierChanged;
    }

    private async void HandleNotifierChanged()
    {
        _devices = (await DeviceService.GetAllDevicesAsync()).ToList();
        await InvokeAsync(StateHasChanged);
    }

    // Mirrors DeviceManagement.HandleInlineAdd — registers the device, then surfaces
    // the change so the parent (Home.razor) can refresh status counts.
    private async Task HandleInlineAdd(InlineAddPayload payload)
    {
        await DeviceService.RegisterFromDiscoveredAsync(payload);
        Handler.RemoveByMac(payload.MacAddress);
        _devices = (await DeviceService.GetAllDevicesAsync()).ToList();
        await OnDeviceAdded.InvokeAsync();
    }

    public void Dispose()
    {
        Notifier.Changed -= HandleNotifierChanged;
    }
}
```

**Note:** `IDeviceService.RegisterFromDiscoveredAsync` and `IScanLifecycleHandler.RemoveByMac` mirror what `DeviceManagement.HandleInlineAdd` does today (`DeviceManagement.razor:468`). Before implementing, verify these method names exist on the actual interfaces — if the real method is named differently, use that name. Do NOT invent new service methods; reuse what `DeviceManagement.razor` already calls.

```
grep -n "HandleInlineAdd\|RegisterFromDiscoveredAsync\|RemoveByMac" src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor src/ControlMenu/Modules/AndroidDevices/Services/IDeviceService.cs src/ControlMenu/Services/Network/IScanLifecycleHandler.cs
```

If the actual method shape differs, mirror the real `HandleInlineAdd` body verbatim from `DeviceManagement.razor`.

- [ ] **Step 2: Create the scoped CSS**

```css
/* HomeDiscoveredAndroid.razor.css */
.home-disc-section {
    padding: 1rem 1.5rem;
    background: var(--bg-elevated, #1a1a1a);
    border-bottom: 1px solid var(--border-subtle, #2a2a2a);
}

.home-disc-header {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin-bottom: 0.625rem;
}

.home-disc-label {
    font-size: 0.6875rem;
    color: var(--text-muted, #888);
    letter-spacing: 0.5px;
}

.home-disc-count {
    color: #000;
    font-size: 0.625rem;
    padding: 0.0625rem 0.375rem;
    border-radius: 0.5rem;
    font-weight: 600;
}

.home-disc-count-android { background: #3DDC84; }

.home-disc-empty {
    color: var(--text-muted, #888);
    font-size: 0.8125rem;
    margin: 0;
}
```

- [ ] **Step 3: Run the test, verify pass**

```
dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~HomeDiscoveredAndroidTests"
```

Expected: 1/1 pass.

- [ ] **Step 4: Commit**

```
git add src/ControlMenu/Components/Pages/Home/HomeDiscoveredAndroid.razor src/ControlMenu/Components/Pages/Home/HomeDiscoveredAndroid.razor.css
git commit -m "feat(home): HomeDiscoveredAndroid wrapper around shared DiscoveredPanel"
```

---

## Task 6: HomeDiscoveredAndroid — populated and post-scan-empty tests

**Files:**
- Modify: `tests/ControlMenu.Tests/Components/Home/HomeDiscoveredAndroidTests.cs`

- [ ] **Step 1: Append two more tests**

```csharp
[Fact]
public void Populated_RendersSectionHeader_AndDelegatesToDiscoveredPanel()
{
    var hits = new List<DiscoveredDevice>
    {
        new() { Ip = "192.168.1.42", Port = 5555, MacAddress = "AA:BB:CC:DD:EE:01", Name = "Pixel 8" }
    };
    _handler.Setup(h => h.Discovered).Returns(hits);

    var cut = RenderComponent<HomeDiscoveredAndroid>(p => p
        .Add(c => c.HasScanned, true));

    Assert.Single(cut.FindAll(".home-disc-section"));
    Assert.Contains("DISCOVERED — ANDROID", cut.Markup);
    Assert.Contains("1", cut.Find(".home-disc-count-android").TextContent);
    // Delegated child renders at least one row
    Assert.NotEmpty(cut.FindAll("table"));
}

[Fact]
public void EmptyPostScan_RendersHeaderAndEmptyMessage()
{
    var cut = RenderComponent<HomeDiscoveredAndroid>(p => p
        .Add(c => c.HasScanned, true));

    Assert.Single(cut.FindAll(".home-disc-section"));
    Assert.Contains("No Android devices found", cut.Markup);
}
```

- [ ] **Step 2: Run, verify all 3 pass**

```
dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~HomeDiscoveredAndroidTests"
```

Expected: 3/3 pass.

- [ ] **Step 3: Commit**

```
git add tests/ControlMenu.Tests/Components/Home/HomeDiscoveredAndroidTests.cs
git commit -m "test(home): HomeDiscoveredAndroid populated and empty-post-scan coverage"
```

---

## Task 7: HomeDiscoveredCameras — failing test for empty/cold state

**Files:**
- Create: `tests/ControlMenu.Tests/Components/Home/HomeDiscoveredCamerasTests.cs`

- [ ] **Step 1: Create the test class with cold-state and post-scan tests**

```csharp
using Bunit;
using ControlMenu.Components.Pages.Home;
using ControlMenu.Modules.Cameras.Network;
using ControlMenu.Modules.Cameras.Services;
using ControlMenu.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ControlMenu.Tests.Components.Home;

public class HomeDiscoveredCamerasTests : TestContext
{
    public HomeDiscoveredCamerasTests()
    {
        // DiscoveredCamerasPanel needs these injects — set up loose mocks
        Services.AddSingleton(new Mock<ICameraScanService>().Object);
        Services.AddSingleton(new Mock<IOnvifClient>().Object);
        Services.AddSingleton(new Mock<ICameraService>().Object);
        Services.AddSingleton(new Mock<IHikvisionIsapiClient>().Object);
        Services.AddSingleton(new Mock<ICameraChangeNotifier>().Object);
    }

    [Fact]
    public void EmptyCold_NoScanRun_RendersNothing()
    {
        var cut = RenderComponent<HomeDiscoveredCameras>(p => p
            .Add(c => c.HasScanned, false));

        Assert.Empty(cut.FindAll(".home-disc-section"));
    }

    [Fact]
    public void PostScan_RendersHeaderEvenIfChildPanelIsEmpty()
    {
        var cut = RenderComponent<HomeDiscoveredCameras>(p => p
            .Add(c => c.HasScanned, true));

        Assert.Single(cut.FindAll(".home-disc-section"));
        Assert.Contains("DISCOVERED — CAMERAS", cut.Markup);
    }
}
```

- [ ] **Step 2: Run, verify failure**

```
dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~HomeDiscoveredCamerasTests"
```

Expected: build error — `HomeDiscoveredCameras` does not exist.

- [ ] **Step 3: Commit failing test**

```
git add tests/ControlMenu.Tests/Components/Home/HomeDiscoveredCamerasTests.cs
git commit -m "test(home): failing tests for HomeDiscoveredCameras"
```

---

## Task 8: HomeDiscoveredCameras — wrapper component

**Files:**
- Create: `src/ControlMenu/Components/Pages/Home/HomeDiscoveredCameras.razor`
- Create: `src/ControlMenu/Components/Pages/Home/HomeDiscoveredCameras.razor.css`

- [ ] **Step 1: Create the wrapper**

```razor
@* HomeDiscoveredCameras.razor *@
@using ControlMenu.Components.Shared.Cameras
@using ControlMenu.Modules.Cameras.Network
@using ControlMenu.Services
@inject ICameraChangeNotifier Notifier
@implements IDisposable

@if (HasScanned)
{
    <section class="home-disc-section">
        <div class="home-disc-header">
            <span class="home-disc-label">DISCOVERED — CAMERAS</span>
        </div>
        <DiscoveredCamerasPanel OnCameraAdded="HandleCameraAdded" />
    </section>
}

@code {
    [Parameter] public bool HasScanned { get; set; }
    [Parameter] public EventCallback OnCameraAdded { get; set; }

    protected override void OnInitialized()
    {
        // DiscoveredCamerasPanel handles its own subscription; wrapper just bubbles
        // the OnCameraAdded callback up to Home.
    }

    private Task HandleCameraAdded() => OnCameraAdded.InvokeAsync();

    public void Dispose() { /* notifier subscription owned by child */ }
}
```

**Note:** Camera scan-result counts surface from `DiscoveredCamerasPanel` internals. The wrapper deliberately does NOT render its own count badge for cameras to avoid duplicating logic and risking drift with the child's filtering. If the user later wants a count badge on the section header, expose a `Count` parameter from `DiscoveredCamerasPanel` as a separate task.

- [ ] **Step 2: Create scoped CSS**

```css
/* HomeDiscoveredCameras.razor.css */
.home-disc-section {
    padding: 1rem 1.5rem;
    background: var(--bg-elevated, #1a1a1a);
    border-bottom: 1px solid var(--border-subtle, #2a2a2a);
}

.home-disc-header {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin-bottom: 0.625rem;
}

.home-disc-label {
    font-size: 0.6875rem;
    color: var(--text-muted, #888);
    letter-spacing: 0.5px;
}
```

- [ ] **Step 3: Run, verify all 2 pass**

```
dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~HomeDiscoveredCamerasTests"
```

Expected: 2/2 pass.

- [ ] **Step 4: Commit**

```
git add src/ControlMenu/Components/Pages/Home/HomeDiscoveredCameras.razor src/ControlMenu/Components/Pages/Home/HomeDiscoveredCameras.razor.css
git commit -m "feat(home): HomeDiscoveredCameras wrapper around shared panel"
```

---

## Task 9: HomeModuleTiles — failing tests for tile resolution

**Files:**
- Create: `tests/ControlMenu.Tests/Components/Home/HomeModuleTilesTests.cs`

- [ ] **Step 1: Create test class with three tile-resolution tests**

```csharp
using Bunit;
using ControlMenu.Components.Pages.Home;
using ControlMenu.Modules;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ControlMenu.Tests.Components.Home;

public class HomeModuleTilesTests : TestContext
{
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

    private void RegisterDiscovery(params IToolModule[] modules)
    {
        var discovery = new ModuleDiscoveryService(modules);
        Services.AddSingleton(discovery);
        Services.AddSingleton<IServiceProvider>(sp => sp);
    }

    [Fact]
    public void Tile_ResolvesToLowestSortOrderEntry()
    {
        RegisterDiscovery(MakeModule("android-devices", "Android Devices",
            new NavEntry("Device List", "/android/devices", "/i.svg", 0),
            new NavEntry("Google TV",   "/android/googletv", "/i.svg", 1)));

        var cut = RenderComponent<HomeModuleTiles>();
        var anchor = cut.Find("a.home-tile[data-module-id='android-devices']");
        Assert.Equal("/android/devices", anchor.GetAttribute("href"));
    }

    [Fact]
    public void Tile_HonorsVisibilityPredicate_PicksFirstVisible()
    {
        // First entry is hidden; should pick second
        RegisterDiscovery(MakeModule("test-mod", "Test",
            new NavEntry("Hidden", "/hidden", "/i.svg", 0, _ => false),
            new NavEntry("Visible", "/visible", "/i.svg", 1)));

        var cut = RenderComponent<HomeModuleTiles>();
        var anchor = cut.Find("a.home-tile[data-module-id='test-mod']");
        Assert.Equal("/visible", anchor.GetAttribute("href"));
    }

    [Fact]
    public void CamerasModule_ZeroEntries_FallsBackToSettingsCameras()
    {
        // Cameras module yields zero nav entries when no cameras are registered
        RegisterDiscovery(MakeModule("cameras", "Cameras"));

        var cut = RenderComponent<HomeModuleTiles>();
        var anchor = cut.Find("a.home-tile[data-module-id='cameras']");
        Assert.Equal("/settings/cameras", anchor.GetAttribute("href"));
    }

    [Fact]
    public void SettingsTile_AlwaysRoutesTo_SettingsGeneral()
    {
        RegisterDiscovery(); // no modules — Settings tile is hard-coded
        var cut = RenderComponent<HomeModuleTiles>();
        var anchor = cut.Find("a.home-tile[data-module-id='settings']");
        Assert.Equal("/settings/general", anchor.GetAttribute("href"));
    }
}
```

**Note:** `NavEntry` is a `record` in `src/ControlMenu/Modules/NavEntry.cs`. If the constructor signature differs from the test (especially the optional `Func<IServiceProvider, bool>?` predicate parameter), inspect the file and adjust the test calls to match — the goal is exercising the lowest-SortOrder + visibility-predicate logic, not the exact constructor positional ordering.

- [ ] **Step 2: Run, verify build error or test failures**

```
dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~HomeModuleTilesTests"
```

Expected: build error — `HomeModuleTiles` does not exist.

- [ ] **Step 3: Commit failing tests**

```
git add tests/ControlMenu.Tests/Components/Home/HomeModuleTilesTests.cs
git commit -m "test(home): failing tests for HomeModuleTiles tile resolver"
```

---

## Task 10: HomeModuleTiles — component with tile resolver

**Files:**
- Create: `src/ControlMenu/Components/Pages/Home/HomeModuleTiles.razor`
- Create: `src/ControlMenu/Components/Pages/Home/HomeModuleTiles.razor.css`

- [ ] **Step 1: Create the component**

```razor
@* HomeModuleTiles.razor *@
@using ControlMenu.Modules
@inject ModuleDiscoveryService ModuleDiscovery
@inject IServiceProvider ServiceProvider

<div class="home-tiles-band">
    <span class="home-tiles-label">MODULES</span>
    <div class="home-tiles-grid">
        @foreach (var module in ModuleDiscovery.Modules)
        {
            var href = ResolveHref(module);
            <a class="home-tile" href="@href" data-module-id="@module.Id">
                @if (ModuleImageMap.TryGetValue(module.Id, out var img))
                {
                    <img src="@img" alt="" class="home-tile-img" />
                }
                else
                {
                    <i class="bi @module.Icon home-tile-bi"></i>
                }
                <span class="home-tile-name">@module.DisplayName</span>
            </a>
        }
        <a class="home-tile" href="/settings/general" data-module-id="settings">
            <i class="bi bi-gear home-tile-bi"></i>
            <span class="home-tile-name">Settings</span>
        </a>
    </div>
</div>

@code {
    private static readonly Dictionary<string, string> ModuleImageMap = new()
    {
        ["android-devices"] = "/images/android-logo.svg",
        ["jellyfin"] = "/images/jellyfin-logo.svg"
    };

    private string ResolveHref(IToolModule module)
    {
        var first = module.GetNavEntries()
            .Where(e => e.Visible is null || e.Visible(ServiceProvider))
            .OrderBy(e => e.SortOrder)
            .FirstOrDefault();

        if (first is not null)
            return first.Href;

        // Cameras with zero registered cameras yields no entries — fall back to Settings
        if (module.Id == "cameras")
            return "/settings/cameras";

        // Generic fallback for any other module that has no visible entries
        return "/";
    }
}
```

**Note:** The `Visible` member of `NavEntry` may have a different name in the actual record (e.g., `Predicate`, `IsVisible`). Inspect `src/ControlMenu/Modules/NavEntry.cs` and adjust. The principle is: filter out entries whose predicate returns false against the IServiceProvider, then pick lowest SortOrder.

- [ ] **Step 2: Create scoped CSS**

```css
/* HomeModuleTiles.razor.css */
.home-tiles-band {
    padding: 0.875rem 1.5rem;
    border-top: 1px solid var(--border-subtle, #2a2a2a);
}

.home-tiles-label {
    display: block;
    font-size: 0.6875rem;
    color: var(--text-muted, #888);
    letter-spacing: 0.5px;
    margin-bottom: 0.625rem;
}

.home-tiles-grid {
    display: grid;
    grid-template-columns: repeat(6, 1fr);
    gap: 0.5rem;
}

@media (max-width: 900px) {
    .home-tiles-grid { grid-template-columns: repeat(3, 1fr); }
}
@media (max-width: 480px) {
    .home-tiles-grid { grid-template-columns: repeat(2, 1fr); }
}

.home-tile {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 0.25rem;
    background: var(--bg-card, #2a2a2a);
    color: var(--text-primary, #e0e0e0);
    padding: 0.75rem 0.375rem;
    border-radius: 6px;
    text-decoration: none;
    text-align: center;
    transition: background 0.15s ease;
}
.home-tile:hover { background: var(--bg-card-hover, #333); }

.home-tile-img { width: 24px; height: 24px; }
.home-tile-bi  { font-size: 1.25rem; }
.home-tile-name { font-size: 0.6875rem; }
```

- [ ] **Step 3: Run, verify all 4 tests pass**

```
dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~HomeModuleTilesTests"
```

Expected: 4/4 pass. If a test fails because of `NavEntry` constructor or `Visible` property mismatch, fix the test/component to match the real type.

- [ ] **Step 4: Commit**

```
git add src/ControlMenu/Components/Pages/Home/HomeModuleTiles.razor src/ControlMenu/Components/Pages/Home/HomeModuleTiles.razor.css
git commit -m "feat(home): HomeModuleTiles with first-nav-entry resolver"
```

---

## Task 11: Home.razor rewrite — composition + scan orchestration

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Home.razor`
- Create:  `src/ControlMenu/Components/Pages/Home.razor.css`

- [ ] **Step 1: Rewrite Home.razor**

```razor
@page "/"
@using ControlMenu.Components.Pages.Home
@using ControlMenu.Modules.AndroidDevices.Services
@using ControlMenu.Modules.Cameras.Network
@using ControlMenu.Services
@using ControlMenu.Services.Network
@inject IConfigurationService Config
@inject IAdbService AdbService
@inject ICameraScanService CameraScanService
@inject IScanLifecycleHandler AndroidHandler
@inject IDeviceChangeNotifier DeviceNotifier
@inject ICameraChangeNotifier CameraNotifier

<PageTitle>Control Menu</PageTitle>

@if (!_setupDone)
{
    <SetupWizard />
}
else
{
    <div class="home-root">
        <header class="home-header">
            <div class="home-brand">
                <img src="/icon-512.png" alt="Control Menu" class="home-logo" />
                <div class="home-titles">
                    <div class="home-title">Control Menu</div>
                    <div class="home-status">@StatusLine</div>
                </div>
            </div>
            <HomeScanBand AndroidRunning="_androidRunning"
                          CamerasRunning="_camerasRunning"
                          AllRunning="_allRunning"
                          OnScanAndroid="ScanAndroidAsync"
                          OnScanCameras="ScanCamerasAsync"
                          OnScanAll="ScanAllAsync" />
        </header>

        <HomeDiscoveredAndroid HasScanned="_androidScanned" OnDeviceAdded="HandleStatusRefresh" />
        <HomeDiscoveredCameras HasScanned="_camerasScanned" OnCameraAdded="HandleStatusRefresh" />
        <HomeModuleTiles />
    </div>
}

@code {
    private bool _setupDone = true;
    private bool _androidScanned;
    private bool _camerasScanned;
    private bool _androidRunning;
    private bool _camerasRunning;
    private bool _allRunning;
    private DateTime? _lastScanUtc;

    protected override async Task OnInitializedAsync()
    {
        var flag = await Config.GetSettingAsync("setup-completed");
        if (flag != "true") _setupDone = false;
    }

    private string StatusLine
    {
        get
        {
            if (!_androidScanned && !_camerasScanned)
                return "Find devices and cameras on your network, then manage them.";
            var ago = _lastScanUtc.HasValue
                ? $" · last scan {(DateTime.UtcNow - _lastScanUtc.Value).TotalSeconds:F0}s ago"
                : "";
            return $"{AndroidHandler.Discovered.Count} Android · cameras tracked separately{ago}";
        }
    }

    private async Task ScanAndroidAsync()
    {
        if (_androidRunning) return;
        _androidRunning = true;
        StateHasChanged();
        try { await AdbService.ScanMdnsAsync(); }
        finally
        {
            _androidRunning = false;
            _androidScanned = true;
            _lastScanUtc = DateTime.UtcNow;
            StateHasChanged();
        }
    }

    private async Task ScanCamerasAsync()
    {
        if (_camerasRunning) return;
        _camerasRunning = true;
        StateHasChanged();
        try { await CameraScanService.StartOnvifOnlyScanAsync(); }
        finally
        {
            _camerasRunning = false;
            _camerasScanned = true;
            _lastScanUtc = DateTime.UtcNow;
            StateHasChanged();
        }
    }

    private async Task ScanAllAsync()
    {
        if (_allRunning) return;
        _allRunning = true;
        var androidTask = _androidRunning ? Task.CompletedTask : ScanAndroidAsync();
        var camerasTask = _camerasRunning ? Task.CompletedTask : ScanCamerasAsync();
        try { await Task.WhenAll(androidTask, camerasTask); }
        finally
        {
            _allRunning = false;
            StateHasChanged();
        }
    }

    private Task HandleStatusRefresh()
    {
        StateHasChanged();
        return Task.CompletedTask;
    }
}
```

**Notes for the implementer:**
- `IAdbService.ScanMdnsAsync()` and `ICameraScanService.StartOnvifOnlyScanAsync()` are the existing methods used by `DeviceManagement` and `CameraSettings`. Verify the exact signatures — they may take cancellation tokens or return scan-result objects. Mirror the call sites in those existing pages.
- `StateHasChanged` is called inside `try/finally` to guarantee the running flag clears even if the scan throws. If the existing `DeviceManagement` does richer error handling (toast notifications), match that pattern here.
- The "Done flash" (~3s ✓ Scanned label between Running and Idle, per spec) is intentionally omitted from this minimal pass — the button reverts directly from Running to Idle. Add the flash in a follow-up step if behavior validation flags it as missing.

- [ ] **Step 2: Add the Home root layout CSS**

```css
/* Home.razor.css */
.home-root {
    display: flex;
    flex-direction: column;
}

.home-header {
    display: flex;
    align-items: center;
    gap: 0.875rem;
    padding: 0.875rem 1.5rem;
    border-bottom: 1px solid var(--border-subtle, #2a2a2a);
}

.home-brand {
    display: flex;
    align-items: center;
    gap: 0.875rem;
    flex: 1;
}

.home-logo {
    width: 36px;
    height: 36px;
    border-radius: 7px;
}

.home-title {
    font-size: 1rem;
    font-weight: 600;
}

.home-status {
    font-size: 0.6875rem;
    color: var(--text-muted, #888);
}
```

- [ ] **Step 3: Build and verify all existing tests still pass**

```
dotnet build src/ControlMenu/ControlMenu.csproj
dotnet test tests/ControlMenu.Tests
```

Expected: build green; existing 354 + new tests all pass. If existing tests for the old Home pill-link layout fail, those tests were testing the now-removed UI — delete them or update to test the new structure (which the new component-level tests already cover).

- [ ] **Step 4: Commit**

```
git add src/ControlMenu/Components/Pages/Home.razor src/ControlMenu/Components/Pages/Home.razor.css
git commit -m "feat(home): Home.razor rewrite — discovery dashboard composition"
```

---

## Task 12: Home integration test — setup-wizard guard + composition

**Files:**
- Create: `tests/ControlMenu.Tests/Components/Home/HomeIntegrationTests.cs`

- [ ] **Step 1: Create the integration test**

```csharp
using Bunit;
using ControlMenu.Components.Pages;
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Modules.Cameras.Network;
using ControlMenu.Modules;
using ControlMenu.Services;
using ControlMenu.Services.Network;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ControlMenu.Tests.Components.Home;

public class HomeIntegrationTests : TestContext
{
    public HomeIntegrationTests()
    {
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetSettingAsync("setup-completed")).ReturnsAsync("true");
        Services.AddSingleton(config.Object);

        Services.AddSingleton(new Mock<IAdbService>().Object);
        Services.AddSingleton(new Mock<ICameraScanService>().Object);
        Services.AddSingleton(new Mock<IDeviceChangeNotifier>().Object);
        Services.AddSingleton(new Mock<ICameraChangeNotifier>().Object);
        Services.AddSingleton(new Mock<IDeviceService>().Object);

        var handler = new Mock<IScanLifecycleHandler>();
        handler.Setup(h => h.Discovered).Returns(new List<DiscoveredDevice>());
        Services.AddSingleton(handler.Object);

        Services.AddSingleton(new ModuleDiscoveryService(Array.Empty<IToolModule>()));
        Services.AddSingleton<IServiceProvider>(sp => sp);
    }

    [Fact]
    public void SetupComplete_RendersAllFourSections()
    {
        var cut = RenderComponent<ControlMenu.Components.Pages.Home>();
        Assert.Single(cut.FindAll(".home-header"));
        Assert.Single(cut.FindAll(".home-tiles-band"));
        // Discovered sections are conditionally hidden when no scan run; verify host elements
        Assert.NotNull(cut.FindComponent<HomeScanBand>());
        Assert.NotNull(cut.FindComponent<HomeModuleTiles>());
    }

    [Fact]
    public void SetupNotComplete_RendersSetupWizardOnly()
    {
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetSettingAsync("setup-completed")).ReturnsAsync((string?)null);
        var existing = Services.First(d => d.ServiceType == typeof(IConfigurationService));
        Services.Remove(existing);
        Services.AddSingleton(config.Object);

        var cut = RenderComponent<ControlMenu.Components.Pages.Home>();
        Assert.Empty(cut.FindAll(".home-header"));
    }
}
```

- [ ] **Step 2: Run integration tests**

```
dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~HomeIntegrationTests"
```

Expected: 2/2 pass.

- [ ] **Step 3: Run full suite — confirm no regressions**

```
dotnet test tests/ControlMenu.Tests
```

Expected: all 354+ tests green.

- [ ] **Step 4: Commit**

```
git add tests/ControlMenu.Tests/Components/Home/HomeIntegrationTests.cs
git commit -m "test(home): integration tests — setup-wizard guard and composition"
```

---

## Task 13: Manual smoke test

**Files:** none modified — interactive verification only.

- [ ] **Step 1: Build and run**

```
dotnet run --project src/ControlMenu/ControlMenu.csproj -c Release
```

Open http://localhost:5159/.

- [ ] **Step 2: Walk the smoke checklist and capture any defects**

For each item, expected behavior:

1. **Cold state:** Header shows tagline (`Find devices and cameras…`); three buttons in idle state; no Discovered sections; module-tile row at bottom.
2. **Click Scan Android alone:** Only Scan Android shows `⏳ Scanning Android…` and is disabled; other two buttons stay idle and remain clickable; on completion, Discovered — Android section appears (with rows or `No Android devices found` placeholder); status line updates to count + `last scan Xs ago`.
3. **Click Scan Cameras alone:** symmetric to (2) for cameras.
4. **Click Scan All cold:** all three buttons enter Running state simultaneously; Android and Cameras scans run in parallel; on completion, both Discovered sections render.
5. **Click Scan Android, then Scan Cameras while Android is still running:** both specific buttons disabled with running labels; Scan All stays idle.
6. **Click Scan Android, then Scan All while Android is still running:** Scan All immediately shows `⏳ Scanning All…` and disables; Cameras kicks off; the original Android scan continues unaffected.
7. **Inline + Add a Discovered Android device:** registration completes via the existing DiscoveredPanel flow; row disappears from Discovered; status line updates registered count.
8. **Inline + Add a Discovered Camera:** same, via DiscoveredCamerasPanel.
9. **Module tile clicks:** Android Devices → `/android/devices`; Power Tools → `/android-power-tools`; Jellyfin → `/jellyfin/db-update`; Cameras (with cameras) → `/cameras/{firstCameraId}`; Cameras (with no cameras) → `/settings/cameras`; Utilities → `/utilities/icon-converter`; Settings → `/settings/general`.
10. **Setup-wizard guard:** with `setup-completed` setting cleared, Home renders the SetupWizard exclusively.

- [ ] **Step 3: Capture any defects as inline edits**

If a smoke step fails, fix the underlying component (not the test) and re-run the affected test class. Recommit small fixes individually with `fix(home): <describe>` messages.

---

## Task 14: CHANGELOG entry + final commit

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add a Changed entry to `[Unreleased]`**

Open `CHANGELOG.md` and add under the existing `[Unreleased]` → `### Changed` section (create the section if it doesn't exist):

```markdown
- Home page redesigned as a discovery-first dashboard. Replaces the module-card grid with: a header band exposing Quick Scan buttons (Scan Android / Scan Cameras / Scan All) at fixed width, two stacked Discovered sections (Android / Cameras) embedding the existing shared panels from Settings, and a compact 6-tile module nav row that routes each tile to its module's first nav entry (with `/settings/cameras` fallback when zero cameras are registered).
```

- [ ] **Step 2: Run the full suite one more time**

```
dotnet test tests/ControlMenu.Tests
```

Expected: green.

- [ ] **Step 3: Commit**

```
git add CHANGELOG.md
git commit -m "docs: changelog entry for homepage-polish dashboard redesign"
```

- [ ] **Step 4: Push the branch**

```
git push -u origin feature/homepage-polish
```

---

## Self-review notes

- **Spec coverage:** All four spec components (HomeScanBand, HomeDiscoveredAndroid, HomeDiscoveredCameras, HomeModuleTiles) have dedicated tasks plus a Home.razor composition task plus integration test plus smoke. Setup-wizard guard preserved (Task 11 + Task 12). Done-flash (~3s ✓ Scanned) intentionally deferred per Task 11 note.
- **Reuse boundary:** Tasks 5/8 explicitly call out that the Discovered panels are the existing shared components consumed exactly as `DeviceManagement` and `CameraSettings` consume them today.
- **Type-name verification:** Task 5 and Task 11 both flag specific service-method names that must be verified against the real interfaces before committing (`RegisterFromDiscoveredAsync`, `RemoveByMac`, `ScanMdnsAsync`, `StartOnvifOnlyScanAsync`, `NavEntry.Visible`). Implementer must grep before assuming.
- **Out-of-scope confirmed:** TODO item 20 (Handler.Discovered staleness) and item 12 (ws-scrcpy-web Local-Deps) untouched per spec.
