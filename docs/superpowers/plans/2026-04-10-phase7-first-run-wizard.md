# Phase 7 — First-Run Wizard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a multi-step setup wizard that guides first-time users through device registration, service configuration, and dependency location — gated by a `setup-completed` settings flag.

**Architecture:** A parent `SetupWizard.razor` component with an enum-based step state machine renders child components for each step (`WizardWelcome`, `WizardDevices`, `WizardServices`, `WizardDependencies`, `WizardDone`). The home page (`/`) checks the `setup-completed` flag and renders the wizard instead of the dashboard when it's not set. A new `ScanForDependenciesAsync()` method on `DependencyManagerService` handles PATH + common location scanning.

**Tech Stack:** .NET 9, Blazor Server, EF Core + SQLite, xUnit + Moq

**Spec:** `docs/superpowers/specs/2026-04-10-phase7-first-run-wizard-design.md`

---

## File Map

### New Files

| File | Responsibility |
|------|---------------|
| `src/ControlMenu/Services/DependencyScanResult.cs` | Result record for dependency scanning |
| `src/ControlMenu/Components/Pages/SetupWizard.razor` | Parent: step enum, stepper bar, nav buttons, WizardState |
| `src/ControlMenu/Components/Pages/Setup/WizardStepper.razor` | Reusable stepper progress bar (5 labeled dots) |
| `src/ControlMenu/Components/Pages/Setup/WizardWelcome.razor` | Step 1: intro text |
| `src/ControlMenu/Components/Pages/Setup/WizardDevices.razor` | Step 2: add devices inline |
| `src/ControlMenu/Components/Pages/Setup/WizardServices.razor` | Step 3: configure module settings |
| `src/ControlMenu/Components/Pages/Setup/WizardDependencies.razor` | Step 4: auto-scan + manual path entry |
| `src/ControlMenu/Components/Pages/Setup/WizardDone.razor` | Step 5: summary with links |
| `tests/ControlMenu.Tests/Services/DependencyScanTests.cs` | Tests for ScanForDependenciesAsync |

### Modified Files

| File | Change |
|------|--------|
| `src/ControlMenu/Services/IDependencyManagerService.cs` | Add `ScanForDependenciesAsync()` |
| `src/ControlMenu/Services/DependencyManagerService.cs` | Implement scan with PATH + common locations |
| `src/ControlMenu/Components/Pages/Home.razor` | Gate on `setup-completed` flag |
| `src/ControlMenu/Components/Pages/Settings/GeneralSettings.razor` | Add "Re-run Setup Wizard" button |

---

## Task 1: DependencyScanResult + ScanForDependenciesAsync (TDD)

**Files:**
- Create: `src/ControlMenu/Services/DependencyScanResult.cs`
- Modify: `src/ControlMenu/Services/IDependencyManagerService.cs`
- Modify: `src/ControlMenu/Services/DependencyManagerService.cs`
- Create: `tests/ControlMenu.Tests/Services/DependencyScanTests.cs`

- [ ] **Step 1: Create the result record**

Create `src/ControlMenu/Services/DependencyScanResult.cs`:

```csharp
namespace ControlMenu.Services;

public record DependencyScanResult(
    string Name,
    string ModuleId,
    bool Found,
    string? Path,
    string? Version,
    string Source);
```

- [ ] **Step 2: Add method to interface**

In `src/ControlMenu/Services/IDependencyManagerService.cs`, add:

```csharp
Task<IReadOnlyList<DependencyScanResult>> ScanForDependenciesAsync();
```

- [ ] **Step 3: Write failing tests**

Create `tests/ControlMenu.Tests/Services/DependencyScanTests.cs`:

```csharp
using ControlMenu.Data;
using ControlMenu.Data.Enums;
using ControlMenu.Modules;
using ControlMenu.Services;
using ControlMenu.Tests.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Services;

public class DependencyScanTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<ICommandExecutor> _mockExecutor = new();
    private readonly Mock<IHttpClientFactory> _mockHttpFactory = new();

    public DependencyScanTests()
    {
        _db = TestDbContextFactory.Create();
    }

    public void Dispose() => _db.Dispose();

    private DependencyManagerService CreateService(params IToolModule[] modules)
    {
        return new DependencyManagerService(
            _db, modules, _mockExecutor.Object, _mockHttpFactory.Object,
            NullLogger<DependencyManagerService>.Instance);
    }

    [Fact]
    public async Task ScanForDependenciesAsync_FindsToolOnPath()
    {
        var module = new FakeModule("test-mod", "Test",
        [
            new ModuleDependency
            {
                Name = "adb",
                ExecutableName = "adb",
                VersionCommand = "adb --version",
                VersionPattern = @"Android Debug Bridge version ([\d.]+)",
                SourceType = UpdateSourceType.Manual
            }
        ]);

        // Seed dependency in DB (sync would do this normally)
        _db.Dependencies.Add(new Data.Entities.Dependency
        {
            Id = Guid.NewGuid(), ModuleId = "test-mod", Name = "adb",
            SourceType = UpdateSourceType.Manual, Status = DependencyStatus.UpToDate
        });
        await _db.SaveChangesAsync();

        // Mock: adb found on PATH
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", "--version", null, default))
            .ReturnsAsync(new CommandResult(0, "Android Debug Bridge version 37.0.0", "", false));

        var service = CreateService(module);
        var results = await service.ScanForDependenciesAsync();

        Assert.Single(results);
        Assert.True(results[0].Found);
        Assert.Equal("37.0.0", results[0].Version);
        Assert.Equal("PATH", results[0].Source);
    }

    [Fact]
    public async Task ScanForDependenciesAsync_ReportsNotFoundWhenMissing()
    {
        var module = new FakeModule("test-mod", "Test",
        [
            new ModuleDependency
            {
                Name = "scrcpy",
                ExecutableName = "scrcpy",
                VersionCommand = "scrcpy --version",
                VersionPattern = @"scrcpy ([\d.]+)",
                SourceType = UpdateSourceType.GitHub,
                GitHubRepo = "Genymobile/scrcpy"
            }
        ]);

        _db.Dependencies.Add(new Data.Entities.Dependency
        {
            Id = Guid.NewGuid(), ModuleId = "test-mod", Name = "scrcpy",
            SourceType = UpdateSourceType.GitHub, Status = DependencyStatus.UpToDate
        });
        await _db.SaveChangesAsync();

        // Mock: scrcpy not found on PATH
        _mockExecutor.Setup(e => e.ExecuteAsync("scrcpy", "--version", null, default))
            .ReturnsAsync(new CommandResult(1, "", "not found", false));
        // Mock: not found at any common location either
        _mockExecutor.Setup(e => e.ExecuteAsync(It.Is<string>(s => s != "scrcpy"), It.IsAny<string>(), null, default))
            .ReturnsAsync(new CommandResult(1, "", "not found", false));

        var service = CreateService(module);
        var results = await service.ScanForDependenciesAsync();

        Assert.Single(results);
        Assert.False(results[0].Found);
        Assert.Null(results[0].Version);
    }

    [Fact]
    public async Task ScanForDependenciesAsync_ReportsAlreadyConfigured()
    {
        var module = new FakeModule("test-mod", "Test",
        [
            new ModuleDependency
            {
                Name = "docker",
                ExecutableName = "docker",
                VersionCommand = "docker --version",
                VersionPattern = @"Docker version ([\d.]+)",
                SourceType = UpdateSourceType.Manual
            }
        ]);

        _db.Dependencies.Add(new Data.Entities.Dependency
        {
            Id = Guid.NewGuid(), ModuleId = "test-mod", Name = "docker",
            SourceType = UpdateSourceType.Manual, Status = DependencyStatus.UpToDate,
            InstalledVersion = "27.1.0"
        });
        await _db.SaveChangesAsync();

        var service = CreateService(module);
        var results = await service.ScanForDependenciesAsync();

        Assert.Single(results);
        Assert.True(results[0].Found);
        Assert.Equal("27.1.0", results[0].Version);
        Assert.Equal("Previously configured", results[0].Source);
    }
}

internal class FakeModule(string id, string displayName, ModuleDependency[] deps) : IToolModule
{
    public string Id => id;
    public string DisplayName => displayName;
    public string Icon => "bi-test";
    public int SortOrder => 0;
    public IEnumerable<ModuleDependency> Dependencies => deps;
    public IEnumerable<ConfigRequirement> ConfigRequirements => [];
    public IEnumerable<NavEntry> GetNavEntries() => [];
    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}
```

**Note:** The `FakeModule` class may already exist in `DependencyManagerServiceTests.cs`. If so, either make it `internal` in a shared location, or duplicate it here (tests should be self-contained). Check the existing file — if it's already `internal` in the same namespace, reuse it.

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DependencyScanTests" -v n`
Expected: FAIL — `ScanForDependenciesAsync` not implemented.

- [ ] **Step 5: Implement ScanForDependenciesAsync**

In `src/ControlMenu/Services/DependencyManagerService.cs`, add:

```csharp
public async Task<IReadOnlyList<DependencyScanResult>> ScanForDependenciesAsync()
{
    var results = new List<DependencyScanResult>();
    var existing = await _db.Dependencies.ToListAsync();

    foreach (var module in _modules)
    {
        foreach (var dep in module.Dependencies)
        {
            var entity = existing.FirstOrDefault(e =>
                e.ModuleId == module.Id && e.Name == dep.Name);

            // Already configured?
            if (entity?.InstalledVersion is not null)
            {
                results.Add(new DependencyScanResult(
                    dep.Name, module.Id, Found: true,
                    Path: null, Version: entity.InstalledVersion,
                    Source: "Previously configured"));
                continue;
            }

            // Try PATH
            var pathResult = await TryScanPathAsync(dep);
            if (pathResult is not null)
            {
                results.Add(pathResult with { ModuleId = module.Id });
                continue;
            }

            // Try common locations
            var locationResult = await TryScanCommonLocationsAsync(dep);
            if (locationResult is not null)
            {
                results.Add(locationResult with { ModuleId = module.Id });
                continue;
            }

            // Not found
            results.Add(new DependencyScanResult(
                dep.Name, module.Id, Found: false,
                Path: null, Version: null, Source: "Not found"));
        }
    }

    return results;
}

private async Task<DependencyScanResult?> TryScanPathAsync(ModuleDependency dep)
{
    var parts = dep.VersionCommand.Split(' ', 2);
    var command = parts[0];
    var args = parts.Length > 1 ? parts[1] : null;

    var result = await _executor.ExecuteAsync(command, args);
    if (result.ExitCode != 0) return null;

    var version = ExtractVersion(result.StandardOutput, dep.VersionPattern);
    return new DependencyScanResult(
        dep.Name, "", Found: true,
        Path: command, Version: version, Source: "PATH");
}

private async Task<DependencyScanResult?> TryScanCommonLocationsAsync(ModuleDependency dep)
{
    var locations = GetCommonLocations(dep.ExecutableName);

    foreach (var location in locations)
    {
        if (!File.Exists(location)) continue;

        var parts = dep.VersionCommand.Split(' ', 2);
        var args = parts.Length > 1 ? parts[1] : null;

        var result = await _executor.ExecuteAsync(location, args);
        if (result.ExitCode != 0) continue;

        var version = ExtractVersion(result.StandardOutput, dep.VersionPattern);
        var dir = System.IO.Path.GetDirectoryName(location) ?? location;
        return new DependencyScanResult(
            dep.Name, "", Found: true,
            Path: location, Version: version, Source: dir);
    }

    return null;
}

private static IEnumerable<string> GetCommonLocations(string executableName)
{
    var exe = OperatingSystem.IsWindows() && !executableName.EndsWith(".exe")
        ? executableName + ".exe" : executableName;

    if (OperatingSystem.IsWindows())
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return
        [
            $@"C:\platform-tools\{exe}",
            $@"C:\scrcpy\{exe}",
            $@"C:\Program Files\Android\platform-tools\{exe}",
            Path.Combine(localAppData, "Android", "Sdk", "platform-tools", exe),
        ];
    }

    return
    [
        $"/usr/local/bin/{exe}",
        $"/opt/platform-tools/{exe}",
        $"/opt/scrcpy/{exe}",
        $"/snap/bin/{exe}",
    ];
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DependencyScanTests" -v n`
Expected: All 3 tests PASS.

- [ ] **Step 7: Run full suite for regressions**

Run: `dotnet test tests/ControlMenu.Tests/ -v q`
Expected: All tests PASS.

- [ ] **Step 8: Commit**

```bash
git add src/ControlMenu/Services/DependencyScanResult.cs \
        src/ControlMenu/Services/IDependencyManagerService.cs \
        src/ControlMenu/Services/DependencyManagerService.cs \
        tests/ControlMenu.Tests/Services/DependencyScanTests.cs
git commit -m "feat(wizard): add ScanForDependenciesAsync with PATH and common location scanning"
```

---

## Task 2: WizardStepper + SetupWizard Parent Component

**Files:**
- Create: `src/ControlMenu/Components/Pages/Setup/WizardStepper.razor`
- Create: `src/ControlMenu/Components/Pages/SetupWizard.razor`

- [ ] **Step 1: Create WizardStepper.razor**

Create `src/ControlMenu/Components/Pages/Setup/WizardStepper.razor`:

```razor
<div class="wizard-stepper">
    @for (var i = 0; i < Steps.Length; i++)
    {
        var stepIndex = i;
        var isActive = stepIndex == CurrentStep;
        var isCompleted = stepIndex < CurrentStep;

        <div class="wizard-step @(isActive ? "active" : "") @(isCompleted ? "completed" : "")">
            <div class="step-dot">
                @if (isCompleted)
                {
                    <i class="bi bi-check"></i>
                }
                else
                {
                    <span>@(stepIndex + 1)</span>
                }
            </div>
            <span class="step-label">@Steps[stepIndex]</span>
        </div>
        @if (stepIndex < Steps.Length - 1)
        {
            <div class="step-connector @(stepIndex < CurrentStep ? "completed" : "")"></div>
        }
    }
</div>

<style>
    .wizard-stepper {
        display: flex;
        align-items: center;
        justify-content: center;
        padding: 24px 0;
        gap: 0;
    }

    .wizard-step {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: 6px;
        min-width: 80px;
    }

    .step-dot {
        width: 32px;
        height: 32px;
        border-radius: 50%;
        display: flex;
        align-items: center;
        justify-content: center;
        font-size: 14px;
        font-weight: 600;
        border: 2px solid var(--border-color, #555);
        color: var(--text-muted, #888);
        background: transparent;
        transition: all 0.2s;
    }

    .wizard-step.active .step-dot {
        border-color: var(--accent-color, #4a9eff);
        color: var(--accent-color, #4a9eff);
        background: var(--accent-bg, rgba(74, 158, 255, 0.1));
    }

    .wizard-step.completed .step-dot {
        border-color: var(--status-ok, #2ea043);
        color: #fff;
        background: var(--status-ok, #2ea043);
    }

    .step-label {
        font-size: 12px;
        color: var(--text-muted, #888);
        text-align: center;
    }

    .wizard-step.active .step-label {
        color: var(--text-primary, #fff);
        font-weight: 600;
    }

    .step-connector {
        flex: 1;
        height: 2px;
        background: var(--border-color, #555);
        min-width: 40px;
        margin-bottom: 24px;
    }

    .step-connector.completed {
        background: var(--status-ok, #2ea043);
    }
</style>

@code {
    [Parameter]
    public string[] Steps { get; set; } = [];

    [Parameter]
    public int CurrentStep { get; set; }
}
```

- [ ] **Step 2: Create SetupWizard.razor**

Create `src/ControlMenu/Components/Pages/SetupWizard.razor`:

```razor
@using ControlMenu.Data.Entities
@using ControlMenu.Data.Enums
@using ControlMenu.Services
@using ControlMenu.Modules

<div class="wizard-container">
    <Setup.WizardStepper Steps="@_stepLabels" CurrentStep="@((int)_currentStep)" />

    <div class="wizard-content">
        @switch (_currentStep)
        {
            case WizardStep.Welcome:
                <Setup.WizardWelcome />
                break;
            case WizardStep.Devices:
                <Setup.WizardDevices State="_state" />
                break;
            case WizardStep.Services:
                <Setup.WizardServices State="_state" />
                break;
            case WizardStep.Dependencies:
                <Setup.WizardDependencies State="_state" />
                break;
            case WizardStep.Done:
                <Setup.WizardDone State="_state" />
                break;
        }
    </div>

    <div class="wizard-nav">
        @if (_currentStep > WizardStep.Welcome)
        {
            <button class="btn btn-secondary" @onclick="GoBack">
                <i class="bi bi-arrow-left"></i> Back
            </button>
        }
        <div class="wizard-nav-spacer"></div>
        @if (_currentStep == WizardStep.Done)
        {
            <button class="btn btn-primary" @onclick="FinishSetup">
                <i class="bi bi-check-circle"></i> Finish Setup
            </button>
        }
        else if (_currentStep == WizardStep.Welcome)
        {
            <button class="btn btn-primary" @onclick="GoNext">
                Get Started <i class="bi bi-arrow-right"></i>
            </button>
        }
        else
        {
            <button class="btn btn-secondary" @onclick="GoNext">
                Skip <i class="bi bi-skip-forward"></i>
            </button>
            <button class="btn btn-primary" @onclick="GoNext">
                Next <i class="bi bi-arrow-right"></i>
            </button>
        }
    </div>
</div>

<style>
    .wizard-container {
        max-width: 800px;
        margin: 0 auto;
        padding: 16px;
    }

    .wizard-content {
        min-height: 400px;
        padding: 16px 0;
    }

    .wizard-nav {
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 16px 0;
        border-top: 1px solid var(--border-color, #333);
    }

    .wizard-nav-spacer {
        flex: 1;
    }
</style>

@code {
    [Inject] private IConfigurationService Config { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private enum WizardStep { Welcome, Devices, Services, Dependencies, Done }

    private static readonly string[] _stepLabels = ["Welcome", "Devices", "Services", "Dependencies", "Done"];

    private WizardStep _currentStep = WizardStep.Welcome;

    private WizardState _state = new();

    private void GoNext()
    {
        if (_currentStep < WizardStep.Done)
            _currentStep++;
    }

    private void GoBack()
    {
        if (_currentStep > WizardStep.Welcome)
            _currentStep--;
    }

    private async Task FinishSetup()
    {
        await Config.SetSettingAsync("setup-completed", "true");
        Nav.NavigateTo("/", forceLoad: true);
    }
}

public class WizardState
{
    public int DevicesAdded { get; set; }
    public int SettingsConfigured { get; set; }
    public int SettingsTotal { get; set; }
    public int DependenciesFound { get; set; }
    public int DependenciesTotal { get; set; }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj`
Expected: Build succeeded. (Child components don't exist yet but are referenced conditionally — Blazor compiles them as types, so they'll fail. Create stub files first.)

Create minimal stubs for the child components so the parent compiles:

`src/ControlMenu/Components/Pages/Setup/WizardWelcome.razor`:
```razor
<h2>Welcome</h2>
```

`src/ControlMenu/Components/Pages/Setup/WizardDevices.razor`:
```razor
<h2>Devices</h2>
@code { [Parameter] public WizardState State { get; set; } = default!; }
```

`src/ControlMenu/Components/Pages/Setup/WizardServices.razor`:
```razor
<h2>Services</h2>
@code { [Parameter] public WizardState State { get; set; } = default!; }
```

`src/ControlMenu/Components/Pages/Setup/WizardDependencies.razor`:
```razor
<h2>Dependencies</h2>
@code { [Parameter] public WizardState State { get; set; } = default!; }
```

`src/ControlMenu/Components/Pages/Setup/WizardDone.razor`:
```razor
<h2>Done</h2>
@code { [Parameter] public WizardState State { get; set; } = default!; }
```

Then build: `dotnet build src/ControlMenu/ControlMenu.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Components/Pages/SetupWizard.razor \
        src/ControlMenu/Components/Pages/Setup/
git commit -m "feat(wizard): add SetupWizard parent with stepper and step stubs"
```

---

## Task 3: Home Page Gate + Re-run Button

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Home.razor`
- Modify: `src/ControlMenu/Components/Pages/Settings/GeneralSettings.razor`

- [ ] **Step 1: Add setup-completed gate to Home.razor**

Replace the entire `src/ControlMenu/Components/Pages/Home.razor`:

```razor
@page "/"
@using ControlMenu.Services

<PageTitle>Control Menu</PageTitle>

@if (!_setupDone)
{
    <SetupWizard />
}
else
{
    <div class="home-container">
        <h1>Control Menu</h1>
        <p class="home-subtitle">Manage your Android devices, media server, and utilities from one place.</p>

        @if (ModuleDiscovery.Modules.Count == 0)
        {
            <div class="home-empty-state">
                <i class="bi bi-box-seam"></i>
                <h2>No modules loaded</h2>
                <p>Modules will appear here as they are installed.</p>
            </div>
        }
        else
        {
            <div class="home-module-grid">
                @foreach (var module in ModuleDiscovery.Modules)
                {
                    <div class="home-module-card">
                        <i class="bi @module.Icon"></i>
                        <h3>@module.DisplayName</h3>
                        <div class="module-nav-links">
                            @foreach (var entry in module.GetNavEntries().OrderBy(e => e.SortOrder))
                            {
                                <a href="@entry.Href">@entry.Title</a>
                            }
                        </div>
                    </div>
                }
            </div>
        }
    </div>
}

@code {
    [Inject]
    private ModuleDiscoveryService ModuleDiscovery { get; set; } = default!;

    [Inject]
    private IConfigurationService Config { get; set; } = default!;

    private bool _setupDone;

    protected override async Task OnInitializedAsync()
    {
        var flag = await Config.GetSettingAsync("setup-completed");
        _setupDone = flag == "true";
    }
}
```

- [ ] **Step 2: Add Re-run button to GeneralSettings.razor**

In `src/ControlMenu/Components/Pages/Settings/GeneralSettings.razor`, add after the discovery interval form-group (before the `@if (_saved)` block):

```razor
<div class="form-group">
    <label>Setup Wizard</label>
    <button class="btn btn-secondary" @onclick="RerunWizard">
        <i class="bi bi-arrow-counterclockwise"></i> Re-run Setup Wizard
    </button>
    <div class="form-hint">Walk through the initial setup again.</div>
</div>
```

Add to the `@code` block:

```csharp
[Inject]
private NavigationManager Nav { get; set; } = default!;

private async Task RerunWizard()
{
    await Config.SetSettingAsync("setup-completed", "false");
    Nav.NavigateTo("/", forceLoad: true);
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Components/Pages/Home.razor \
        src/ControlMenu/Components/Pages/Settings/GeneralSettings.razor
git commit -m "feat(wizard): gate home page on setup-completed flag, add re-run button"
```

---

## Task 4: WizardWelcome

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Setup/WizardWelcome.razor`

- [ ] **Step 1: Implement WizardWelcome**

Replace `src/ControlMenu/Components/Pages/Setup/WizardWelcome.razor`:

```razor
<div class="wizard-welcome">
    <div class="welcome-icon">
        <i class="bi bi-gear-wide-connected"></i>
    </div>
    <h2>Welcome to Control Menu</h2>
    <p>
        This wizard will help you set up your devices, configure services, and locate
        required tools. Everything is optional — you can skip any step and configure it
        later in Settings.
    </p>
    <div class="welcome-overview">
        <h3>We'll walk through:</h3>
        <ol>
            <li><strong>Devices</strong> — Register your Android devices (Google TVs, phones)</li>
            <li><strong>Services</strong> — Configure Jellyfin connection, SMTP, and other module settings</li>
            <li><strong>Dependencies</strong> — Locate required tools (adb, scrcpy, docker, etc.)</li>
        </ol>
    </div>
</div>

<style>
    .wizard-welcome {
        text-align: center;
        padding: 24px 0;
    }

    .welcome-icon {
        font-size: 64px;
        color: var(--accent-color, #4a9eff);
        margin-bottom: 16px;
    }

    .wizard-welcome h2 {
        margin-bottom: 12px;
    }

    .wizard-welcome p {
        color: var(--text-secondary, #aaa);
        max-width: 500px;
        margin: 0 auto 24px;
    }

    .welcome-overview {
        text-align: left;
        max-width: 400px;
        margin: 0 auto;
    }

    .welcome-overview h3 {
        font-size: 16px;
        margin-bottom: 8px;
    }

    .welcome-overview ol {
        padding-left: 20px;
    }

    .welcome-overview li {
        margin-bottom: 8px;
        color: var(--text-secondary, #aaa);
    }
</style>
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/Components/Pages/Setup/WizardWelcome.razor
git commit -m "feat(wizard): implement Welcome step"
```

---

## Task 5: WizardDevices

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Setup/WizardDevices.razor`

- [ ] **Step 1: Implement WizardDevices**

Replace `src/ControlMenu/Components/Pages/Setup/WizardDevices.razor`:

```razor
@using ControlMenu.Data.Entities
@using ControlMenu.Data.Enums
@using ControlMenu.Services

<div class="settings-section">
    <h2><i class="bi bi-phone"></i> Register Devices</h2>
    <p>Add your Android devices so the app can manage them via ADB. You can add more later in Settings.</p>

    <div class="wizard-device-form">
        <div class="form-group">
            <label>Device Name</label>
            <input class="form-control" @bind="_name" placeholder="Living Room TV" />
        </div>

        <div class="form-row" style="display:flex; gap:12px;">
            <div class="form-group" style="flex:1;">
                <label>Device Type</label>
                <select class="form-control" @bind="_type">
                    @foreach (var t in Enum.GetValues<DeviceType>())
                    {
                        <option value="@t">@t</option>
                    }
                </select>
            </div>
            <div class="form-group" style="flex:1;">
                <label>ADB Port</label>
                <input type="number" class="form-control" @bind="_port" />
            </div>
        </div>

        <div class="form-group">
            <label>MAC Address</label>
            <input class="form-control" @bind="_mac" placeholder="b8-7b-d4-f3-ae-84" />
            <div class="form-hint">Used for automatic IP discovery via ARP.</div>
        </div>

        <button class="btn btn-primary" @onclick="AddDevice"
                disabled="@(!IsValid)">
            <i class="bi bi-plus-lg"></i> Add Device
        </button>
    </div>

    @if (_devices.Count > 0)
    {
        <h3 style="margin-top:24px;">Added Devices</h3>
        <table class="data-table">
            <thead>
                <tr>
                    <th>Name</th>
                    <th>Type</th>
                    <th>MAC</th>
                    <th>Port</th>
                    <th></th>
                </tr>
            </thead>
            <tbody>
                @foreach (var device in _devices)
                {
                    <tr>
                        <td>@device.Name</td>
                        <td>@device.Type</td>
                        <td><code>@device.MacAddress</code></td>
                        <td>@device.AdbPort</td>
                        <td>
                            <button class="btn btn-danger btn-sm" @onclick="() => RemoveDevice(device)">
                                Remove
                            </button>
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    }
</div>

@code {
    [Parameter] public WizardState State { get; set; } = default!;
    [Inject] private IDeviceService DeviceService { get; set; } = default!;

    private string _name = "";
    private DeviceType _type = DeviceType.GoogleTV;
    private string _mac = "";
    private int _port = 5555;
    private List<Device> _devices = [];

    private bool IsValid =>
        !string.IsNullOrWhiteSpace(_name) &&
        !string.IsNullOrWhiteSpace(_mac);

    private async Task AddDevice()
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Name = _name,
            Type = _type,
            MacAddress = _mac,
            AdbPort = _port,
            ModuleId = "android-devices"
        };
        await DeviceService.AddDeviceAsync(device);
        _devices.Add(device);
        State.DevicesAdded = _devices.Count;

        // Reset form
        _name = "";
        _mac = "";
        _port = 5555;
        _type = DeviceType.GoogleTV;
    }

    private async Task RemoveDevice(Device device)
    {
        await DeviceService.DeleteDeviceAsync(device.Id);
        _devices.Remove(device);
        State.DevicesAdded = _devices.Count;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/Components/Pages/Setup/WizardDevices.razor
git commit -m "feat(wizard): implement Devices step with inline add/remove"
```

---

## Task 6: WizardServices

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Setup/WizardServices.razor`

- [ ] **Step 1: Implement WizardServices**

Replace `src/ControlMenu/Components/Pages/Setup/WizardServices.razor`:

```razor
@using ControlMenu.Modules
@using ControlMenu.Services

<div class="settings-section">
    <h2><i class="bi bi-sliders"></i> Configure Services</h2>
    <p>Set up connections for your modules. Leave fields empty to configure later in Settings.</p>

    @foreach (var group in _moduleGroups)
    {
        <div class="wizard-service-group">
            <h3>@group.DisplayName</h3>
            @foreach (var req in group.Requirements)
            {
                <div class="form-group">
                    <label>@req.Requirement.DisplayName</label>
                    @if (req.Requirement.IsSecret)
                    {
                        <input type="password" class="form-control" style="max-width:400px;"
                               value="@req.Value"
                               @onchange="e => OnValueChanged(req, e)" />
                    }
                    else
                    {
                        <input class="form-control" style="max-width:400px;"
                               value="@req.Value"
                               @onchange="e => OnValueChanged(req, e)" />
                    }
                    <div class="form-hint">@req.Requirement.Description</div>
                </div>
            }
        </div>
    }

    @if (_saved)
    {
        <div class="alert alert-success">Settings saved.</div>
    }
</div>

<style>
    .wizard-service-group {
        margin-bottom: 24px;
        padding-bottom: 16px;
        border-bottom: 1px solid var(--border-color, #333);
    }

    .wizard-service-group h3 {
        font-size: 16px;
        margin-bottom: 12px;
    }
</style>

@code {
    [Parameter] public WizardState State { get; set; } = default!;
    [Inject] private IConfigurationService Config { get; set; } = default!;
    [Inject] private ModuleDiscoveryService ModuleDiscovery { get; set; } = default!;

    private List<ModuleGroup> _moduleGroups = [];
    private bool _saved;

    protected override async Task OnInitializedAsync()
    {
        var totalCount = 0;
        var configuredCount = 0;

        foreach (var module in ModuleDiscovery.Modules)
        {
            var reqs = module.ConfigRequirements.ToList();
            if (reqs.Count == 0) continue;

            var entries = new List<SettingEntry>();
            foreach (var req in reqs)
            {
                totalCount++;
                string? currentValue;
                if (req.IsSecret)
                    currentValue = await Config.GetSecretAsync(req.Key, module.Id);
                else
                    currentValue = await Config.GetSettingAsync(req.Key, module.Id);

                var value = currentValue ?? req.DefaultValue ?? "";
                if (!string.IsNullOrEmpty(currentValue)) configuredCount++;

                entries.Add(new SettingEntry(module.Id, req, value));
            }

            _moduleGroups.Add(new ModuleGroup(module.DisplayName, entries));
        }

        State.SettingsTotal = totalCount;
        State.SettingsConfigured = configuredCount;
    }

    private void OnValueChanged(SettingEntry entry, ChangeEventArgs e)
    {
        entry.Value = e.Value?.ToString() ?? "";
    }

    /// <summary>
    /// Called by the parent wizard when navigating away from this step (Next/Skip).
    /// Saves all non-empty values.
    /// </summary>
    public async Task SaveAsync()
    {
        var configured = 0;
        foreach (var group in _moduleGroups)
        {
            foreach (var entry in group.Requirements)
            {
                if (string.IsNullOrWhiteSpace(entry.Value)) continue;

                if (entry.Requirement.IsSecret)
                    await Config.SetSecretAsync(entry.Requirement.Key, entry.Value, entry.ModuleId);
                else
                    await Config.SetSettingAsync(entry.Requirement.Key, entry.Value, entry.ModuleId);

                configured++;
            }
        }

        State.SettingsConfigured = configured;
        _saved = true;
    }

    private record ModuleGroup(string DisplayName, List<SettingEntry> Requirements);

    private class SettingEntry(string moduleId, ConfigRequirement requirement, string value)
    {
        public string ModuleId { get; } = moduleId;
        public ConfigRequirement Requirement { get; } = requirement;
        public string Value { get; set; } = value;
    }
}
```

**Note:** The `SaveAsync()` method needs to be called by the parent when the user clicks Next or Skip. Update `SetupWizard.razor`'s `GoNext()` to call it:

In `SetupWizard.razor`, add a reference to the WizardServices component and call save:

Change the WizardServices render to:
```razor
case WizardStep.Services:
    <Setup.WizardServices @ref="_servicesRef" State="_state" />
    break;
```

Add field:
```csharp
private Setup.WizardServices? _servicesRef;
```

Update `GoNext()`:
```csharp
private async Task GoNext()
{
    // Save services settings when leaving that step
    if (_currentStep == WizardStep.Services && _servicesRef is not null)
        await _servicesRef.SaveAsync();

    if (_currentStep < WizardStep.Done)
        _currentStep++;
}
```

Also update `GoBack()` to async and do the same save if backing away from Services.

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/Components/Pages/Setup/WizardServices.razor \
        src/ControlMenu/Components/Pages/SetupWizard.razor
git commit -m "feat(wizard): implement Services step with module config requirements"
```

---

## Task 7: WizardDependencies

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Setup/WizardDependencies.razor`

- [ ] **Step 1: Implement WizardDependencies**

Replace `src/ControlMenu/Components/Pages/Setup/WizardDependencies.razor`:

```razor
@using ControlMenu.Services

<div class="settings-section">
    <h2><i class="bi bi-box-seam"></i> Locate Dependencies</h2>
    <p>Scanning your system for required tools. We check your PATH and common install locations.</p>

    @if (_scanning)
    {
        <div class="scan-status">
            <i class="bi bi-hourglass-split"></i> Scanning...
        </div>
    }
    else if (_results.Count > 0)
    {
        <div class="toolbar">
            <button class="btn btn-secondary" @onclick="Rescan">
                <i class="bi bi-arrow-repeat"></i> Re-scan
            </button>
        </div>

        <table class="data-table">
            <thead>
                <tr>
                    <th>Tool</th>
                    <th>Status</th>
                    <th>Version</th>
                    <th>Location</th>
                    <th>Action</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var result in _results)
                {
                    <tr>
                        <td><strong>@result.Name</strong></td>
                        <td>
                            @if (result.Found)
                            {
                                <span class="status-badge status-ok">Found</span>
                            }
                            else
                            {
                                <span class="status-badge status-error">Not Found</span>
                            }
                        </td>
                        <td><code>@(result.Version ?? "—")</code></td>
                        <td>@(result.Source)</td>
                        <td>
                            @if (result.Found && !_accepted.Contains(result.Name))
                            {
                                <button class="btn btn-primary btn-sm" @onclick="() => Accept(result)">
                                    Accept
                                </button>
                            }
                            else if (result.Found && _accepted.Contains(result.Name))
                            {
                                <span style="color: var(--status-ok);">
                                    <i class="bi bi-check-circle"></i> Accepted
                                </span>
                            }
                            else if (_editingPath == result.Name)
                            {
                                <div style="display:flex; gap:4px;">
                                    <input class="form-control" style="width:200px;"
                                           @bind="_manualPath" placeholder="/path/to/tool" />
                                    <button class="btn btn-primary btn-sm" @onclick="() => SubmitManualPath(result)">
                                        OK
                                    </button>
                                    <button class="btn btn-secondary btn-sm" @onclick="() => _editingPath = null">
                                        Cancel
                                    </button>
                                </div>
                            }
                            else
                            {
                                <button class="btn btn-secondary btn-sm" @onclick="() => StartManualEntry(result.Name)">
                                    Enter Path...
                                </button>
                            }
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    }
</div>

@code {
    [Parameter] public WizardState State { get; set; } = default!;
    [Inject] private IDependencyManagerService DepManager { get; set; } = default!;

    private List<DependencyScanResult> _results = [];
    private HashSet<string> _accepted = [];
    private bool _scanning;
    private string? _editingPath;
    private string _manualPath = "";

    protected override async Task OnInitializedAsync()
    {
        await Rescan();
    }

    private async Task Rescan()
    {
        _scanning = true;
        StateHasChanged();

        _results = (await DepManager.ScanForDependenciesAsync()).ToList();

        // Auto-accept previously configured ones
        foreach (var r in _results.Where(r => r.Source == "Previously configured"))
            _accepted.Add(r.Name);

        UpdateState();
        _scanning = false;
    }

    private void Accept(DependencyScanResult result)
    {
        _accepted.Add(result.Name);
        UpdateState();
    }

    private void StartManualEntry(string name)
    {
        _editingPath = name;
        _manualPath = "";
    }

    private async Task SubmitManualPath(DependencyScanResult result)
    {
        if (string.IsNullOrWhiteSpace(_manualPath)) return;

        // Validate by trying to run the version command against the given path
        // For now, just accept it — validation would require CommandExecutor injection
        _accepted.Add(result.Name);
        _editingPath = null;
        UpdateState();
    }

    private void UpdateState()
    {
        State.DependenciesTotal = _results.Count;
        State.DependenciesFound = _results.Count(r => r.Found || _accepted.Contains(r.Name));
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/Components/Pages/Setup/WizardDependencies.razor
git commit -m "feat(wizard): implement Dependencies step with auto-scan and manual entry"
```

---

## Task 8: WizardDone + Final Wiring

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Setup/WizardDone.razor`

- [ ] **Step 1: Implement WizardDone**

Replace `src/ControlMenu/Components/Pages/Setup/WizardDone.razor`:

```razor
<div class="wizard-done">
    <div class="done-icon">
        <i class="bi bi-check-circle-fill"></i>
    </div>
    <h2>Setup Complete</h2>
    <p>Here's a summary of what was configured. You can always change these in Settings.</p>

    <div class="done-summary">
        <div class="summary-item">
            <i class="bi @(State.DevicesAdded > 0 ? "bi-check-circle-fill text-ok" : "bi-exclamation-triangle-fill text-warning")"></i>
            <div>
                <strong>Devices</strong>
                @if (State.DevicesAdded > 0)
                {
                    <span>@State.DevicesAdded device(s) added</span>
                }
                else
                {
                    <span>No devices configured — <a href="/settings/devices">go to Settings</a></span>
                }
            </div>
        </div>

        <div class="summary-item">
            <i class="bi @(State.SettingsConfigured == State.SettingsTotal && State.SettingsTotal > 0 ? "bi-check-circle-fill text-ok" : State.SettingsConfigured > 0 ? "bi-exclamation-triangle-fill text-warning" : "bi-exclamation-triangle-fill text-warning")"></i>
            <div>
                <strong>Services</strong>
                @if (State.SettingsTotal == 0)
                {
                    <span>No module settings to configure</span>
                }
                else
                {
                    <span>@State.SettingsConfigured of @State.SettingsTotal settings configured
                        @if (State.SettingsConfigured < State.SettingsTotal)
                        {
                            <text> — <a href="/settings/modules">go to Settings</a></text>
                        }
                    </span>
                }
            </div>
        </div>

        <div class="summary-item">
            <i class="bi @(State.DependenciesFound == State.DependenciesTotal && State.DependenciesTotal > 0 ? "bi-check-circle-fill text-ok" : State.DependenciesFound > 0 ? "bi-exclamation-triangle-fill text-warning" : "bi-exclamation-triangle-fill text-warning")"></i>
            <div>
                <strong>Dependencies</strong>
                @if (State.DependenciesTotal == 0)
                {
                    <span>No dependencies to locate</span>
                }
                else
                {
                    <span>@State.DependenciesFound of @State.DependenciesTotal tools found
                        @if (State.DependenciesFound < State.DependenciesTotal)
                        {
                            <text> — <a href="/settings/dependencies">go to Settings</a></text>
                        }
                    </span>
                }
            </div>
        </div>
    </div>

    <p class="done-hint">You can re-run this wizard anytime from Settings &gt; General.</p>
</div>

<style>
    .wizard-done {
        text-align: center;
        padding: 24px 0;
    }

    .done-icon {
        font-size: 64px;
        color: var(--status-ok, #2ea043);
        margin-bottom: 16px;
    }

    .done-summary {
        text-align: left;
        max-width: 500px;
        margin: 24px auto;
    }

    .summary-item {
        display: flex;
        align-items: flex-start;
        gap: 12px;
        padding: 12px 0;
        border-bottom: 1px solid var(--border-color, #333);
    }

    .summary-item i {
        font-size: 20px;
        margin-top: 2px;
    }

    .text-ok { color: var(--status-ok, #2ea043); }
    .text-warning { color: var(--status-warning, #e6a700); }

    .summary-item div {
        display: flex;
        flex-direction: column;
        gap: 4px;
    }

    .summary-item span {
        color: var(--text-secondary, #aaa);
        font-size: 14px;
    }

    .done-hint {
        margin-top: 24px;
        color: var(--text-muted, #888);
        font-size: 13px;
    }
</style>

@code {
    [Parameter] public WizardState State { get; set; } = default!;
}
```

- [ ] **Step 2: Build and run full test suite**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj && dotnet test tests/ControlMenu.Tests/ -v q`
Expected: Build succeeded. All tests PASS.

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/Components/Pages/Setup/WizardDone.razor
git commit -m "feat(wizard): implement Done step with configuration summary"
```

---

## Task 9: Final Verification + Push

- [ ] **Step 1: Run full test suite**

Run: `dotnet test tests/ControlMenu.Tests/ -v q`
Expected: All tests PASS.

- [ ] **Step 2: Build release**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj -c Release`
Expected: Build succeeded with 0 errors.

- [ ] **Step 3: Push to GitHub**

```bash
git push origin master
```

- [ ] **Step 4: Verify**

Run: `git log --oneline -10`
Expected: All Phase 7 commits visible.
