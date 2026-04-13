# UI Overhaul Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace navy dark mode with OAO grey palette, simplify theme toggle to 2-state, restyle sidebar nav as pill buttons, add "Update All" button to dependencies.

**Architecture:** CSS custom property swap for dark mode, JS simplification for toggle, scoped CSS changes for sidebar, Razor component update for Update All. All changes are independent — no shared state or ordering dependencies.

**Tech Stack:** CSS custom properties, vanilla JS (localStorage), Blazor Server components, Bootstrap Icons

---

### Task 1: Dark Mode Palette

**Files:**
- Modify: `src/ControlMenu/wwwroot/css/theme.css:24-43`

- [ ] **Step 1: Replace dark theme custom properties**

Replace lines 24-43 in `theme.css` with the OAO grey palette:

```css
/* Dark theme — OAO grey palette */
[data-theme="dark"] {
    --bg-primary: #2a2a2a;
    --content-bg: #333333;
    --sidebar-bg: #252525;
    --topbar-bg: #252525;
    --border-color: #444444;
    --text-primary: #e0e0e0;
    --text-secondary: #aaaaaa;
    --text-muted: #888888;
    --hover-bg: rgba(255, 255, 255, 0.05);
    --active-bg: rgba(16, 185, 129, 0.1);
    --accent-color: #10b981;
    --card-bg: #333333;
    --card-shadow: 0 1px 3px rgba(0, 0, 0, 0.3);
    --input-bg: #2e2e2e;
    --input-border: #444444;
    --danger-color: #f06c75;
    --success-color: #50c878;
    --warning-color: #ffd866;
}
```

- [ ] **Step 2: Verify in browser**

Run: Open `http://localhost:5159` in dark mode. Confirm no blue tints remain — all backgrounds should be neutral greys, accent color should be emerald green.

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/wwwroot/css/theme.css
git commit -m "feat: replace navy dark mode with OAO grey palette"
```

---

### Task 2: Simplify Theme Toggle to 2-State

**Files:**
- Modify: `src/ControlMenu/wwwroot/js/theme.js`
- Modify: `src/ControlMenu/Components/Layout/TopBar.razor:20-21, 30-44, 68-78`

- [ ] **Step 1: Simplify theme.js**

Replace the entire contents of `theme.js`:

```javascript
window.themeManager = {
    _storageKey: 'controlmenu-theme',

    get: function () {
        return localStorage.getItem(this._storageKey) || 'dark';
    },

    set: function (theme) {
        localStorage.setItem(this._storageKey, theme);
        document.documentElement.setAttribute('data-theme', theme);
    },

    toggle: function () {
        var current = this.get();
        var next = current === 'dark' ? 'light' : 'dark';
        this.set(next);
        return next;
    },

    init: function () {
        document.documentElement.setAttribute('data-theme', this.get());
    }
};

window.themeManager.init();
```

- [ ] **Step 2: Simplify TopBar.razor toggle**

Replace the `@code` block (lines 26-79) with:

```csharp
@code {
    [Parameter]
    public string PageTitle { get; set; } = "Home";

    private string CurrentTheme { get; set; } = "dark";

    private string ThemeIcon => CurrentTheme == "dark" ? "bi-sun-fill" : "bi-moon-fill";
    private string ThemeLabel => CurrentTheme == "dark" ? "Switch to light" : "Switch to dark";

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private IDependencyManagerService DepManager { get; set; } = default!;

    private int _updateCount;

    protected override async Task OnInitializedAsync()
    {
        _updateCount = await DepManager.GetUpdateAvailableCountAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            CurrentTheme = await JS.InvokeAsync<string>("themeManager.get");
            StateHasChanged();
        }
    }

    private async Task ToggleTheme()
    {
        CurrentTheme = await JS.InvokeAsync<string>("themeManager.toggle");
    }
}
```

- [ ] **Step 3: Update the button onclick**

In the template section of `TopBar.razor`, change line 20 from:
```html
<button class="theme-toggle" @onclick="CycleTheme" title="@ThemeLabel">
```
to:
```html
<button class="theme-toggle" @onclick="ToggleTheme" title="@ThemeLabel">
```

- [ ] **Step 4: Verify in browser**

Click the theme toggle button. Should alternate between dark ↔ light in one click. Refresh page — theme should persist. Default on fresh localStorage should be dark.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/wwwroot/js/theme.js src/ControlMenu/Components/Layout/TopBar.razor
git commit -m "feat: simplify theme toggle to 2-state dark/light"
```

---

### Task 3: Sidebar Pill Button Navigation

**Files:**
- Modify: `src/ControlMenu/Components/Layout/Sidebar.razor.css:92-107`

- [ ] **Step 1: Update sidebar link styles**

Replace lines 92-107 in `Sidebar.razor.css` with:

```css
.sidebar-link {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 10px 12px 10px 32px;
    color: var(--text-secondary);
    text-decoration: none;
    font-size: 0.9rem;
    border-radius: 6px;
    margin: 2px 8px;
    transition: background-color 0.15s ease, color 0.15s ease;
}

.sidebar-link i {
    opacity: 0.6;
}

.sidebar-link:hover {
    background-color: var(--hover-bg);
    color: var(--text-primary);
    text-decoration: none;
}

.sidebar-link:hover i {
    opacity: 0.8;
}

.sidebar-link.active {
    background-color: var(--content-bg);
    color: var(--text-primary);
}

.sidebar-link.active i {
    opacity: 1;
}
```

- [ ] **Step 2: Update sidebar footer link padding**

Replace lines 114-116 with:

```css
.sidebar-footer .sidebar-link {
    padding-left: 12px;
    margin: 2px 8px;
}
```

- [ ] **Step 3: Verify in browser**

Open sidebar in both dark and light mode. Confirm:
- No blue underlined links
- Active page has pill-shaped grey background
- Hover shows subtle background highlight
- Icons are dimmer on inactive, full opacity on active
- Settings link at bottom has consistent styling

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Components/Layout/Sidebar.razor.css
git commit -m "feat: restyle sidebar nav as pill buttons"
```

---

### Task 4: Self-Contained Dependencies Folder + PATH Injection

**Files:**
- Modify: `src/ControlMenu/Program.cs:1-12`
- Modify: `src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs:12-57`
- Modify: `src/ControlMenu/Modules/Jellyfin/JellyfinModule.cs` (dependencies section)

Dependencies live in subfolders under the app root: `dependencies/<name>/`. At startup, each subfolder is prepended to `PATH` so `CommandExecutor` finds bundled executables automatically. `InstallPath` on each `ModuleDependency` points to its subfolder so the update pipeline knows where to swap files.

- [ ] **Step 1: Create dependencies directory structure**

```bash
mkdir -p src/ControlMenu/dependencies/platform-tools
mkdir -p src/ControlMenu/dependencies/scrcpy
mkdir -p src/ControlMenu/dependencies/node
```

Add a `.gitkeep` in each so the folders are tracked:

```bash
touch src/ControlMenu/dependencies/platform-tools/.gitkeep
touch src/ControlMenu/dependencies/scrcpy/.gitkeep
touch src/ControlMenu/dependencies/node/.gitkeep
```

- [ ] **Step 2: Add PATH injection to Program.cs**

Add this block immediately after line 12 (`var builder = WebApplication.CreateBuilder(args);`):

```csharp
// Prepend bundled dependency folders to PATH for self-contained operation
var depsRoot = Path.Combine(AppContext.BaseDirectory, "dependencies");
if (Directory.Exists(depsRoot))
{
    var depPaths = Directory.GetDirectories(depsRoot)
        .Where(d => !Path.GetFileName(d).StartsWith('.'));
    var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
    var newPath = string.Join(Path.PathSeparator, depPaths) + Path.PathSeparator + currentPath;
    Environment.SetEnvironmentVariable("PATH", newPath);
}
```

- [ ] **Step 3: Set InstallPath on Android module dependencies**

Replace the `Dependencies` property in `AndroidDevicesModule.cs` with:

```csharp
private static string DepsRoot => Path.Combine(AppContext.BaseDirectory, "dependencies");

public IEnumerable<ModuleDependency> Dependencies =>
[
    new ModuleDependency
    {
        Name = "adb",
        ExecutableName = "adb",
        VersionCommand = "adb --version",
        VersionPattern = @"Android Debug Bridge version ([\d.]+)",
        SourceType = UpdateSourceType.DirectUrl,
        ProjectHomeUrl = "https://developer.android.com/tools/releases/platform-tools",
        DownloadUrl = OperatingSystem.IsWindows()
            ? "https://dl.google.com/android/repository/platform-tools-latest-windows.zip"
            : "https://dl.google.com/android/repository/platform-tools-latest-linux.zip",
        VersionCheckUrl = "https://dl.google.com/android/repository/repository2-3.xml",
        VersionCheckPattern = @"<major>(\d+)</major>\s*<minor>(\d+)</minor>\s*<micro>(\d+)</micro>",
        InstallPath = Path.Combine(DepsRoot, "platform-tools")
    },
    new ModuleDependency
    {
        Name = "scrcpy",
        ExecutableName = "scrcpy",
        VersionCommand = "scrcpy --version",
        VersionPattern = @"scrcpy ([\d.]+)",
        SourceType = UpdateSourceType.GitHub,
        GitHubRepo = "Genymobile/scrcpy",
        AssetPattern = @"scrcpy-win64-v[\d.]+\.zip",
        InstallPath = Path.Combine(DepsRoot, "scrcpy")
    },
    new ModuleDependency
    {
        Name = "node",
        ExecutableName = "node",
        VersionCommand = "node --version",
        VersionPattern = @"v([\d.]+)",
        SourceType = UpdateSourceType.DirectUrl,
        ProjectHomeUrl = "https://nodejs.org/",
        DownloadUrl = "https://nodejs.org/en/download/",
        InstallPath = Path.Combine(DepsRoot, "node")
    },
    new ModuleDependency
    {
        Name = "ws-scrcpy-web",
        ExecutableName = "node",
        VersionCommand = "node -e \"console.log('installed')\"",
        VersionPattern = @"(installed)",
        SourceType = UpdateSourceType.Manual,
        ProjectHomeUrl = "https://github.com/bilbospocketses/ws-scrcpy-web"
    }
];
```

- [ ] **Step 4: Verify PATH injection**

Build and run the app. In the app logs or via a debug breakpoint, confirm that `dependencies/platform-tools`, `dependencies/scrcpy`, and `dependencies/node` appear at the front of `PATH`. Run "Check All" in Settings → Dependencies — the "Update" button should appear for dependencies with available updates.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Program.cs src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs src/ControlMenu/dependencies/
git commit -m "feat: self-contained dependencies folder with PATH injection at startup"
```

---

### Task 5: Update All Dependencies Button

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Settings/DependencyManagement.razor:10-18, 99-110`

- [ ] **Step 1: Add Update All button to toolbar**

Replace the toolbar div (lines 10-18) with:

```html
<div class="toolbar">
    <button class="btn btn-secondary" @onclick="CheckAll" disabled="@_checkingAll">
        <i class="bi bi-arrow-repeat"></i> @(_checkingAll ? "Checking..." : "Check All")
    </button>
    @if (_updatableCount > 0)
    {
        <button class="btn btn-primary" @onclick="UpdateAll" disabled="@_updatingAll">
            <i class="bi bi-download"></i>
            @if (_updatingAll)
            {
                @:Updating @_updateProgress of @_updatableCount...
            }
            else
            {
                @:Update All (@_updatableCount)
            }
        </button>
    }
    <div class="toolbar-spacer"></div>
    @if (!string.IsNullOrEmpty(_message))
    {
        <span class="alert @(_messageIsError ? "alert-danger" : "alert-success")" style="margin:0; padding:6px 12px;">@_message</span>
    }
</div>
```

- [ ] **Step 2: Add state fields and UpdateAll method**

Add these fields after line 109 (`private Guid _updateTargetId;`):

```csharp
private int _updatableCount;
private bool _updatingAll;
private int _updateProgress;
```

Update `LoadDependencies()` to compute the updatable count:

```csharp
private async Task LoadDependencies()
{
    _dependencies = (await DepManager.GetAllDependenciesAsync()).ToList();
    _hasInstallPath = ModuleDiscovery.Modules
        .SelectMany(m => m.Dependencies)
        .Where(d => !string.IsNullOrEmpty(d.InstallPath))
        .Select(d => d.Name)
        .ToHashSet();
    _updatableCount = _dependencies.Count(d =>
        d.Status == DependencyStatus.UpdateAvailable && _hasInstallPath.Contains(d.Name));
}
```

Add the `UpdateAll` method after `CancelUpdate()`:

```csharp
private async Task UpdateAll()
{
    _updatingAll = true;
    _updateProgress = 0;
    _message = null;
    StateHasChanged();

    var toUpdate = _dependencies
        .Where(d => d.Status == DependencyStatus.UpdateAvailable && _hasInstallPath.Contains(d.Name))
        .ToList();

    int succeeded = 0;
    int failed = 0;

    foreach (var dep in toUpdate)
    {
        _updateProgress++;
        StateHasChanged();

        var asset = await DepManager.ResolveDownloadAssetAsync(dep.Id);
        if (asset is null)
        {
            failed++;
            continue;
        }

        var result = await DepManager.DownloadAndInstallAsync(dep.Id, asset);
        if (result.Success)
            succeeded++;
        else
            failed++;
    }

    _updatingAll = false;
    _message = failed == 0
        ? $"Updated {succeeded} dependenc{(succeeded == 1 ? "y" : "ies")} successfully"
        : $"Updated {succeeded}, failed {failed}";
    _messageIsError = failed > 0;

    await LoadDependencies();
}
```

- [ ] **Step 3: Verify in browser**

Go to Settings → Dependencies. Click "Check All". If any updates are available, the "Update All (N)" button should appear. Click it — should update sequentially with progress feedback.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Components/Pages/Settings/DependencyManagement.razor
git commit -m "feat: add Update All button for bulk dependency updates"
```
