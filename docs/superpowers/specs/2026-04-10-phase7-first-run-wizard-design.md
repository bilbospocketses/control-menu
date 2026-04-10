# Phase 7 — First-Run Wizard

Design spec for Phase 7 of Control Menu. A multi-step setup wizard that guides new users through initial configuration on first launch.

## Scope

1. **SetupWizard parent component** — step state machine, stepper progress bar, navigation buttons
2. **WizardWelcome** — brief introduction
3. **WizardDevices** — register Android devices
4. **WizardServices** — configure module settings (Jellyfin, SMTP, etc.)
5. **WizardDependencies** — auto-scan PATH + common locations for required tools
6. **WizardDone** — summary with links to Settings for anything skipped
7. **Dependency scan logic** — new `ScanForDependenciesAsync()` method on `IDependencyManagerService`
8. **Home page gate** — render wizard instead of dashboard when `setup-completed` flag is not set
9. **Re-run button** — "Re-run Setup Wizard" on General Settings page

## Trigger Mechanism

A `setup-completed` global setting in the Settings table (via `ConfigurationService`). The wizard shows until the user either completes it or explicitly skips to the end.

- **First launch:** Setting doesn't exist → wizard shows
- **Completed:** Setting = `"true"` → normal dashboard
- **Re-run:** General Settings page has a "Re-run Setup Wizard" button that sets the flag to `"false"` and navigates to `/`

The EF Core auto-migration creates the DB before the UI loads, so "DB doesn't exist" is never true by the time Blazor renders. A settings flag is the reliable gate.

## UI Approach

Full-page stepper that replaces the main content area. The sidebar remains visible — the user can click away to leave the wizard at any time (it resumes on next visit since the flag isn't set).

### SetupWizard.razor (Parent)

Manages current step via an enum:

```csharp
private enum WizardStep { Welcome, Devices, Services, Dependencies, Done }
```

Renders:
1. **Stepper progress bar** at top — 5 labeled dots showing current step
2. **Active step's child component** — switched based on current step
3. **Bottom navigation** — contextual buttons:
   - Welcome: [Get Started]
   - Devices/Services/Dependencies: [Back] [Skip] [Next]
   - Done: [Back] [Finish Setup]

**Finish Setup** writes `setup-completed = "true"` via `ConfigurationService.SetSettingAsync()` and calls `NavigationManager.NavigateTo("/", forceLoad: true)`.

### State Object

The parent holds a simple state object passed to child components as a `[Parameter]`:

```csharp
public class WizardState
{
    public int DevicesAdded { get; set; }
    public int SettingsConfigured { get; set; }
    public int SettingsTotal { get; set; }
    public int DependenciesFound { get; set; }
    public int DependenciesTotal { get; set; }
}
```

Each child component updates its counts when work is done. The Done step reads it to build the summary.

## Step 1: Welcome

**Component:** `WizardWelcome.razor`

Minimal — sets the tone:
- "Welcome to Control Menu" heading
- 2-3 sentences: "This wizard will help you set up your devices, configure services, and locate required tools. Everything is optional — you can skip any step and configure it later in Settings."
- Overview: "We'll walk through: Devices → Services → Dependencies"
- [Get Started] button (acts as Next)

No configuration, no inputs. Just context.

## Step 2: Add Devices

**Component:** `WizardDevices.razor`

Simplified device registration form:

- Brief explanation: "Register your Android devices so the app can manage them via ADB."
- Inline form: Name (required), Device Type (dropdown), MAC Address (required), ADB Port (default 5555)
- **[Add Device]** button — saves via `IDeviceService` and adds to a visible list below
- List shows devices added so far with a [Remove] option
- No "Scan Network" button — keep it simple for first-run. Available later in Settings > Devices.

On **[Skip]**: no devices added, move to next step. Summary will note "No devices configured" with a link.

Updates `WizardState.DevicesAdded` on each add/remove.

## Step 3: Configure Services

**Component:** `WizardServices.razor`

Collects `ConfigRequirements` from all discovered modules via `ModuleDiscoveryService` and renders input fields, grouped by module.

For each `ConfigRequirement`:
- Label + description
- Text input (or password input if `IsSecret`)
- Pre-filled with `DefaultValue` if one exists

Layout grouped by module:

```
Jellyfin
  API Key:          [••••••••]
  Database Path:    [D:/DockerData/jellyfin/config/data/jellyfin.db]  (pre-filled)
  Container Name:   [jellyfin]  (pre-filled)
  ...

Notifications
  SMTP Server:      [mail.smtp2go.com]  (pre-filled)
  SMTP Username:    [          ]
  ...
```

On **[Next]**: saves all non-empty values via `ConfigurationService.SetSettingAsync()` / `SetSecretAsync()`. Empty fields are skipped — user can fill them later in Settings > Module Settings.

Updates `WizardState.SettingsConfigured` (count of non-empty fields saved) and `WizardState.SettingsTotal` (total config requirements across all modules).

## Step 4: Locate Dependencies

**Component:** `WizardDependencies.razor`

Runs an auto-scan when the step loads, then shows results.

### Scan Logic

New method on `IDependencyManagerService`:

```csharp
Task<IReadOnlyList<DependencyScanResult>> ScanForDependenciesAsync();
```

New result record:

```csharp
public record DependencyScanResult(
    string Name,
    string ModuleId,
    bool Found,
    string? Path,
    string? Version,
    string Source);  // "PATH", or the specific directory where found
```

Scan algorithm for each module dependency:

1. If dependency already has `InstalledVersion` in DB (from a previous sync) — report as found, source = "Previously configured"
2. **PATH check:** Run `VersionCommand` via `CommandExecutor`. If exit code 0, extract version — found on PATH, source = "PATH"
3. **Common locations check** (only if PATH check failed):
   - Windows: `C:\platform-tools`, `C:\scrcpy`, `C:\Program Files\Android\*`, `%LOCALAPPDATA%\Android\Sdk\platform-tools`
   - Linux: `/usr/local/bin`, `/opt/platform-tools`, `/opt/scrcpy`, `/snap/bin`
   - For each location, check if the executable exists. If found, run version command against the full path.
   - Source = the specific directory path
4. If not found anywhere — `Found = false`

### UI

Table showing scan results:

| Tool | Status | Version | Location | Action |
|------|--------|---------|----------|--------|
| adb | Found | 37.0.0 | PATH | [Accept] |
| scrcpy | Not found | — | — | [Enter Path...] |
| docker | Found | 27.1.0 | PATH | [Accept] |
| sqlite3 | Found | 3.46.0 | PATH | [Accept] |

- **[Accept]** — saves the found path/version to the Dependencies table
- **[Enter Path...]** — expands an inline text input for the user to type a path. On submit, validates by running the version command against that path.
- If a tool is found in a common location but not on PATH: "Found at `C:\platform-tools\adb.exe` (not on PATH)" with [Accept]

A "Re-scan" button re-runs the scan.

Updates `WizardState.DependenciesFound` and `WizardState.DependenciesTotal`.

## Step 5: Done

**Component:** `WizardDone.razor`

Summary of what was configured:

- **Devices:** "2 devices added" (green check) or "No devices configured" (amber warning) — link to [Settings > Devices](/settings/devices)
- **Services:** "5 of 11 settings configured" (green/amber based on completeness) — link to [Settings > Modules](/settings/modules)
- **Dependencies:** "3 of 4 tools found" (green/amber) — link to [Settings > Dependencies](/settings/dependencies)

Each line has a green check icon if fully configured, amber warning icon if partial/empty.

A note: "You can re-run this wizard anytime from Settings > General."

**[Finish Setup]** button writes `setup-completed = "true"` and navigates to home.

## Home Page Integration

The existing home page component (or `MainLayout.razor`) checks the `setup-completed` flag:

```csharp
var setupDone = await Config.GetSettingAsync("setup-completed");
if (setupDone != "true")
{
    // Render SetupWizard instead of normal content
}
```

The cleanest integration point is the home page route (`/`). When `setup-completed` is not `"true"`, it renders `<SetupWizard />`. Otherwise, it renders the normal dashboard.

## General Settings Integration

Add a "Re-run Setup Wizard" button to `GeneralSettings.razor`:

```razor
<div class="form-group">
    <label>Setup Wizard</label>
    <button class="btn btn-secondary" @onclick="RerunWizard">
        <i class="bi bi-arrow-counterclockwise"></i> Re-run Setup Wizard
    </button>
    <div class="form-hint">Walk through the initial setup again.</div>
</div>
```

On click: sets `setup-completed` to `"false"` and navigates to `/`.

## New Files

| File | Purpose |
|------|---------|
| `Components/Pages/SetupWizard.razor` | Parent: step state machine, stepper bar, navigation |
| `Components/Pages/Setup/WizardWelcome.razor` | Step 1: introduction |
| `Components/Pages/Setup/WizardDevices.razor` | Step 2: add devices |
| `Components/Pages/Setup/WizardServices.razor` | Step 3: configure module settings |
| `Components/Pages/Setup/WizardDependencies.razor` | Step 4: auto-scan and locate tools |
| `Components/Pages/Setup/WizardDone.razor` | Step 5: summary |
| `Components/Pages/Setup/WizardStepper.razor` | Reusable stepper progress bar |
| `Services/DependencyScanResult.cs` | Result record for dependency scanning |

## Modified Files

| File | Change |
|------|--------|
| `Services/IDependencyManagerService.cs` | Add `ScanForDependenciesAsync()` |
| `Services/DependencyManagerService.cs` | Implement scan with PATH + common locations |
| `Components/Pages/Home.razor` | Gate on `setup-completed` flag, render `<SetupWizard />` when not set |
| `Components/Pages/Settings/GeneralSettings.razor` | Add "Re-run Setup Wizard" button |

## Testing

- **ScanForDependenciesAsync:** Unit tests mocking CommandExecutor — verify PATH check, common location check, already-configured skip
- **WizardState flow:** Verify counts update correctly across steps
- **setup-completed flag:** Verify wizard shows when flag absent/false, hides when true
- **Re-run button:** Verify it resets the flag and triggers wizard on next home visit
