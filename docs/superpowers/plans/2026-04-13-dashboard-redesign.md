# Dashboard Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the crowded action-card grid on Google TV and Pixel dashboards with a two-column layout: compact controls panel (left) + inline ws-scrcpy mirror (right).

**Architecture:** ScrcpyMirror component gains an inline iframe mode. Both dashboards switch from a card grid to a CSS Grid two-column layout. Quick actions become compact rows with icon, label, status, and button(s). The ws-scrcpy stream is embedded as an iframe filling the right column.

**Tech Stack:** Blazor Server (.NET 9), scoped CSS, ws-scrcpy-web iframe

---

### Task 1: Update ScrcpyMirror component — add inline iframe mode

**Files:**
- Modify: `src/ControlMenu/Components/Shared/ScrcpyMirror.razor`

The current component only opens a popup window. Add an `Inline` parameter that renders an iframe instead.

- [x] **Step 1: Replace ScrcpyMirror.razor with dual-mode component**

```razor
@* src/ControlMenu/Components/Shared/ScrcpyMirror.razor *@
@inject WsScrcpyService WsScrcpy
@inject IJSRuntime JS

<div class="scrcpy-mirror">
    @if (!WsScrcpy.IsRunning)
    {
        <div class="alert alert-warning">
            <i class="bi bi-exclamation-triangle"></i>
            Screen mirroring unavailable — configure ws-scrcpy-web path in Settings
        </div>
    }
    else if (Inline)
    {
        <iframe src="@StreamUrl" class="scrcpy-iframe" allow="autoplay; fullscreen"></iframe>
    }
    else
    {
        <button class="btn btn-primary" @onclick="OpenMirrorWindow">
            <i class="bi bi-cast"></i> Screen Mirror
        </button>
    }
</div>

@code {
    [Parameter, EditorRequired]
    public string Udid { get; set; } = string.Empty;

    [Parameter]
    public bool Inline { get; set; }

    private string StreamUrl =>
        $"{WsScrcpy.BaseUrl}/#!action=stream&udid={Uri.EscapeDataString(Udid)}&player=webcodecs";

    private async Task OpenMirrorWindow()
    {
        await JS.InvokeVoidAsync("open", StreamUrl, "_blank", "width=1080,height=600,menubar=no,toolbar=no,location=no,status=no");
    }
}
```

- [x] **Step 2: Verify build**

Run: `dotnet build src/ControlMenu`
Expected: 0 errors

- [x] **Step 3: Commit**

```bash
git add src/ControlMenu/Components/Shared/ScrcpyMirror.razor
git commit -m "feat: add inline iframe mode to ScrcpyMirror component"
```

---

### Task 2: Rewrite GoogleTvDashboard layout

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor`

Replace the action-card grid with a two-column layout. Left panel has compact action rows. Right panel has the inline ScrcpyMirror iframe. Restore Projectivy section at bottom of left panel. Rename "TV Launcher" to "Google TV Launcher" and show Projectivy note when disabled.

- [x] **Step 1: Replace GoogleTvDashboard.razor markup**

Replace everything from `<div class="action-card-grid">` through its closing `</div>` (lines 33–143) with the new two-column layout:

```razor
    <div class="dashboard-layout">
        <div class="controls-panel">
            <h3 class="panel-heading">Quick Actions</h3>

            <!-- Power Status -->
            <div class="action-row">
                <div class="action-label">
                    <i class="bi bi-lightning-charge"></i> Power
                </div>
                <span class="action-status">@_powerState</span>
                <div class="action-buttons">
                    <button class="btn btn-sm btn-secondary" @onclick="CheckPowerStatus" disabled="@_busy">Check</button>
                    <button class="btn btn-sm btn-warning" @onclick="TogglePower" disabled="@_busy">Toggle</button>
                </div>
            </div>

            <!-- Reboot -->
            <div class="action-row">
                <div class="action-label">
                    <i class="bi bi-arrow-repeat"></i> Reboot
                </div>
                <div class="action-buttons">
                    <button class="btn btn-sm btn-danger" @onclick="RebootDevice" disabled="@_busy">Reboot</button>
                </div>
            </div>

            <!-- Shizuku -->
            <div class="action-row">
                <div class="action-label">
                    <i class="bi bi-shield-check"></i> Shizuku
                </div>
                <div class="action-buttons">
                    <button class="btn btn-sm btn-primary" @onclick="StartShizuku" disabled="@_busy">Start</button>
                </div>
            </div>

            <!-- Screensaver -->
            <div class="action-row">
                <div class="action-label">
                    <i class="bi bi-image"></i> Screensaver
                </div>
                <span class="action-status">@_screensaver</span>
                <div class="action-buttons">
                    <button class="btn btn-sm btn-secondary" @onclick="CheckScreensaver" disabled="@_busy">Refresh</button>
                    <button class="btn btn-sm btn-primary" @onclick="ToggleScreensaver" disabled="@_busy">Switch</button>
                </div>
            </div>

            <!-- Screen Timeout -->
            <div class="action-row">
                <div class="action-label">
                    <i class="bi bi-clock"></i> Timeout
                </div>
                <span class="action-status">@FormatTimeout(_screenTimeout)</span>
                <div class="action-buttons timeout-group">
                    <input type="number" @bind="_newTimeout" min="300000" step="60000" placeholder="ms" class="form-input form-input-sm" />
                    <button class="btn btn-sm btn-primary" @onclick="SetTimeout" disabled="@_busy">Set</button>
                </div>
            </div>

            <!-- Google TV Launcher -->
            <div class="action-row">
                <div class="action-label">
                    <i class="bi bi-grid-3x3-gap"></i> Google TV Launcher
                </div>
                <span class="action-status">
                    @(_launcherDisabled == null ? "Unknown" : _launcherDisabled.Value ? "Disabled" : "Enabled")
                </span>
                <div class="action-buttons">
                    <button class="btn btn-sm btn-warning" @onclick="ToggleLauncher" disabled="@_busy">Toggle</button>
                </div>
            </div>
            @if (_launcherDisabled == true)
            {
                <div class="action-note">
                    <i class="bi bi-info-circle"></i> Projectivy Launcher is currently the default
                </div>
            }

            <!-- Restore Projectivy -->
            <div class="panel-section">
                <h3 class="panel-heading">Restore Projectivy</h3>
                @if (_projectivyBackups.Count == 0)
                {
                    <button class="btn btn-sm btn-secondary" @onclick="LoadProjectivyBackups" disabled="@_busy">
                        <i class="bi bi-arrow-clockwise"></i> Scan for Backups
                    </button>
                }
                else
                {
                    <select @bind="_selectedBackup" class="form-select form-select-sm projectivy-select">
                        <option value="">Select a backup...</option>
                        @foreach (var backup in _projectivyBackups)
                        {
                            <option value="@backup">@backup</option>
                        }
                    </select>
                    <div class="action-buttons" style="margin-top: 0.5rem;">
                        <button class="btn btn-sm btn-secondary" @onclick="LoadProjectivyBackups" disabled="@_busy">Scan</button>
                        <button class="btn btn-sm btn-primary" @onclick="RestoreProjectivy" disabled="@(_busy || string.IsNullOrEmpty(_selectedBackup))">Restore</button>
                    </div>
                }
            </div>
        </div>

        <div class="mirror-panel">
            <ScrcpyMirror Udid="@($"{Ip}:{Port}")" Inline="true" />
        </div>
    </div>
```

The `@code` block, `@page` directives, `@using` statements, device header, and status bar all remain unchanged. Only the body below the status bar changes.

- [x] **Step 2: Verify build**

Run: `dotnet build src/ControlMenu`
Expected: 0 errors

- [x] **Step 3: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor
git commit -m "feat: redesign Google TV dashboard — compact controls + inline mirror"
```

---

### Task 3: Rewrite GoogleTvDashboard CSS

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor.css`

Replace the card grid styles with two-column layout styles.

- [x] **Step 1: Replace GoogleTvDashboard.razor.css**

```css
/* Device header */
.device-header {
    display: flex;
    align-items: center;
    gap: 1rem;
    margin-bottom: 1rem;
}

.status-badge {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    padding: 0.25rem 0.75rem;
    border-radius: 1rem;
    font-size: 0.85rem;
    font-weight: 500;
}
.status-badge.connected { background: var(--success-bg, #d4edda); color: var(--success-text, #155724); }
.status-badge.disconnected { background: var(--danger-bg, #f8d7da); color: var(--danger-text, #721c24); }

/* Status bar */
.status-bar {
    padding: 0.75rem 1rem;
    border-radius: 0.5rem;
    margin-bottom: 1rem;
    display: flex;
    align-items: center;
    gap: 0.5rem;
}
.status-success { background: var(--success-bg, #d4edda); color: var(--success-text, #155724); }
.status-warning { background: var(--warning-bg, #fff3cd); color: var(--warning-text, #856404); }
.status-info { background: var(--info-bg, #d1ecf1); color: var(--info-text, #0c5460); }

/* Two-column dashboard layout */
.dashboard-layout {
    display: grid;
    grid-template-columns: 280px 1fr;
    gap: 1.25rem;
    min-height: 500px;
}

/* Left panel — controls */
.controls-panel {
    background: var(--card-bg, #fff);
    border: 1px solid var(--border-color, #dee2e6);
    border-radius: 0.75rem;
    padding: 1rem;
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
    overflow-y: auto;
    max-height: calc(100vh - 200px);
}

.panel-heading {
    margin: 0 0 0.5rem 0;
    font-size: 0.8rem;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.05em;
    color: var(--text-secondary, #6c757d);
}

.panel-section {
    margin-top: 1rem;
    padding-top: 1rem;
    border-top: 1px solid var(--border-color, #dee2e6);
}

/* Compact action rows */
.action-row {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.4rem;
    padding: 0.5rem 0;
    border-bottom: 1px solid var(--border-color, #dee2e6);
}
.action-row:last-of-type {
    border-bottom: none;
}

.action-label {
    font-size: 0.85rem;
    font-weight: 500;
    min-width: 100%;
    color: var(--text-primary, #212529);
}
.action-label i {
    color: var(--accent-color, #0d6efd);
    margin-right: 0.3rem;
}

.action-status {
    font-size: 0.8rem;
    font-weight: 500;
    color: var(--text-muted, #adb5bd);
    flex: 1;
}

.action-buttons {
    display: flex;
    gap: 0.35rem;
    flex-wrap: wrap;
}

.action-note {
    font-size: 0.75rem;
    color: var(--accent-color, #638cff);
    padding: 0.25rem 0 0.5rem 1.4rem;
}

/* Timeout input within action row */
.timeout-group {
    width: 100%;
}
.timeout-group input {
    width: 90px;
    font-size: 0.8rem;
    padding: 0.2rem 0.4rem;
}

/* Projectivy dropdown — white text for dark theme readability */
.projectivy-select {
    font-size: 0.8rem;
    color: var(--text-primary, #212529);
    background-color: var(--input-bg, #fff);
    border-color: var(--input-border, #ced4da);
}
.projectivy-select option {
    color: var(--text-primary, #212529);
    background-color: var(--input-bg, #fff);
}

/* Small button overrides */
.btn-sm {
    font-size: 0.75rem;
    padding: 0.2rem 0.5rem;
}

/* Right panel — mirror */
.mirror-panel {
    background: var(--card-bg, #fff);
    border: 1px solid var(--border-color, #dee2e6);
    border-radius: 0.75rem;
    padding: 0.5rem;
    display: flex;
    flex-direction: column;
    min-height: 500px;
}

.mirror-panel ::deep .scrcpy-mirror {
    flex: 1;
    display: flex;
    flex-direction: column;
}

.mirror-panel ::deep .scrcpy-iframe {
    flex: 1;
    width: 100%;
    min-height: 480px;
    border: none;
    border-radius: 0.5rem;
}

/* Responsive: stack on narrow screens */
@media (max-width: 768px) {
    .dashboard-layout {
        grid-template-columns: 1fr;
    }
    .controls-panel {
        max-height: none;
    }
}
```

- [x] **Step 2: Verify build**

Run: `dotnet build src/ControlMenu`
Expected: 0 errors

- [x] **Step 3: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor.css
git commit -m "style: Google TV dashboard — two-column layout with compact action rows"
```

---

### Task 4: Rewrite PixelDashboard layout

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor`

Replace the action-card grid with the same two-column layout. Left panel has Reset ADB Port and ADB Connect. Right panel has inline ScrcpyMirror.

- [x] **Step 1: Replace PixelDashboard.razor markup**

Replace everything from `<div class="action-card-grid">` through its closing `</div>` (lines 33–61) with:

```razor
    <div class="dashboard-layout">
        <div class="controls-panel">
            <h3 class="panel-heading">Quick Actions</h3>

            <!-- Reset ADB Port -->
            <div class="action-row">
                <div class="action-label">
                    <i class="bi bi-usb-plug"></i> Reset ADB Port
                </div>
                <span class="action-status">Requires USB</span>
                <div class="action-buttons">
                    <button class="btn btn-sm btn-warning" @onclick="ResetAdbPort" disabled="@_busy">Reset</button>
                </div>
            </div>

            <!-- ADB Connect -->
            <div class="action-row">
                <div class="action-label">
                    <i class="bi bi-plug"></i> ADB Connect
                </div>
                <span class="action-status">WiFi connection</span>
                <div class="action-buttons">
                    <button class="btn btn-sm btn-primary" @onclick="ConnectDevice" disabled="@_busy">Connect</button>
                </div>
            </div>
        </div>

        <div class="mirror-panel">
            <ScrcpyMirror Udid="@($"{Ip}:{Port}")" Inline="true" />
        </div>
    </div>
```

The `@code` block, `@page` directives, `@using` statements, device header, and status bar all remain unchanged.

- [x] **Step 2: Verify build**

Run: `dotnet build src/ControlMenu`
Expected: 0 errors

- [x] **Step 3: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor
git commit -m "feat: redesign Pixel dashboard — compact controls + inline mirror"
```

---

### Task 5: Rewrite PixelDashboard CSS

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor.css`

Apply the same two-column styles. Identical to GoogleTvDashboard CSS minus the Projectivy-specific styles.

- [x] **Step 1: Replace PixelDashboard.razor.css**

```css
/* Device header */
.device-header {
    display: flex;
    align-items: center;
    gap: 1rem;
    margin-bottom: 1rem;
}

.status-badge {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    padding: 0.25rem 0.75rem;
    border-radius: 1rem;
    font-size: 0.85rem;
    font-weight: 500;
}
.status-badge.connected { background: var(--success-bg, #d4edda); color: var(--success-text, #155724); }
.status-badge.disconnected { background: var(--danger-bg, #f8d7da); color: var(--danger-text, #721c24); }

/* Status bar */
.status-bar {
    padding: 0.75rem 1rem;
    border-radius: 0.5rem;
    margin-bottom: 1rem;
    display: flex;
    align-items: center;
    gap: 0.5rem;
}
.status-success { background: var(--success-bg, #d4edda); color: var(--success-text, #155724); }
.status-warning { background: var(--warning-bg, #fff3cd); color: var(--warning-text, #856404); }
.status-info { background: var(--info-bg, #d1ecf1); color: var(--info-text, #0c5460); }

/* Two-column dashboard layout */
.dashboard-layout {
    display: grid;
    grid-template-columns: 280px 1fr;
    gap: 1.25rem;
    min-height: 500px;
}

/* Left panel */
.controls-panel {
    background: var(--card-bg, #fff);
    border: 1px solid var(--border-color, #dee2e6);
    border-radius: 0.75rem;
    padding: 1rem;
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
}

.panel-heading {
    margin: 0 0 0.5rem 0;
    font-size: 0.8rem;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.05em;
    color: var(--text-secondary, #6c757d);
}

/* Compact action rows */
.action-row {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.4rem;
    padding: 0.5rem 0;
    border-bottom: 1px solid var(--border-color, #dee2e6);
}
.action-row:last-of-type {
    border-bottom: none;
}

.action-label {
    font-size: 0.85rem;
    font-weight: 500;
    min-width: 100%;
    color: var(--text-primary, #212529);
}
.action-label i {
    color: var(--accent-color, #0d6efd);
    margin-right: 0.3rem;
}

.action-status {
    font-size: 0.8rem;
    font-weight: 500;
    color: var(--text-muted, #adb5bd);
    flex: 1;
}

.action-buttons {
    display: flex;
    gap: 0.35rem;
}

.btn-sm {
    font-size: 0.75rem;
    padding: 0.2rem 0.5rem;
}

/* Right panel — mirror */
.mirror-panel {
    background: var(--card-bg, #fff);
    border: 1px solid var(--border-color, #dee2e6);
    border-radius: 0.75rem;
    padding: 0.5rem;
    display: flex;
    flex-direction: column;
    min-height: 500px;
}

.mirror-panel ::deep .scrcpy-mirror {
    flex: 1;
    display: flex;
    flex-direction: column;
}

.mirror-panel ::deep .scrcpy-iframe {
    flex: 1;
    width: 100%;
    min-height: 480px;
    border: none;
    border-radius: 0.5rem;
}

@media (max-width: 768px) {
    .dashboard-layout {
        grid-template-columns: 1fr;
    }
}
```

- [x] **Step 2: Verify build**

Run: `dotnet build src/ControlMenu`
Expected: 0 errors

- [x] **Step 3: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor.css
git commit -m "style: Pixel dashboard — two-column layout with compact action rows"
```

---

### Task 6: Run existing tests to verify no regressions

**Files:** None (verification only)

- [x] **Step 1: Run full test suite**

Run: `dotnet test tests/ControlMenu.Tests`
Expected: All 128 tests pass

- [x] **Step 2: Commit all remaining changes (if any unstaged)**

Verify `git status` is clean. If any files were missed, stage and commit.

---

### Task 7: Fix WsScrcpyService startup reliability

**Files:**
- Modify: `src/ControlMenu/Services/WsScrcpyService.cs`

WsScrcpyService had several issues preventing ws-scrcpy-web from starting reliably when launched from Control Menu:

1. **Missing WorkingDirectory** — Node process was spawned without a CWD, causing relative path resolution failures in ws-scrcpy-web
2. **No stdout/stderr capture** — crashes were silent; no way to diagnose startup failures
3. **IsRunning flicker** — `IsRunning` returned false between health check and process start
4. **Orphan processes** — when Control Menu crashed or was killed, the ws-scrcpy-web node process stayed alive on port 8000, causing EADDRINUSE crash loops on next startup

**Fixes applied:**
- Set `WorkingDirectory` to ws-scrcpy-web project root (parent of `dist/`)
- Added `OutputDataReceived`/`ErrorDataReceived` handlers logging to ILogger
- Added `_serviceReady` flag so `IsRunning` stays true after health check passes
- Added `KillOrphanOnPortAsync()` — on startup, checks if port 8000 is occupied, finds the owning PID via `netstat` (Windows) or `lsof` (Linux), and kills it before spawning a fresh instance

- [x] **Step 1: Implement all fixes in WsScrcpyService.cs**
- [x] **Step 2: Verify orphan cleanup works with live orphan on port 8000**
- [x] **Step 3: Run test suite — 127 tests passing**

---

### Task 8: Dashboard UI polish

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor`
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor.css`
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor`
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor.css`

**Changes:**
- Reboot and Shizuku rows made single-line (label left, button right)
- Power status shows colored dot (green=Awake, red=Asleep) under label
- Power row buttons vertically centered
- Timeout status text in white, positioned above text box (right-aligned)
- Action row backgrounds use `--sidebar-bg` for darker contrast
- All buttons right-aligned via `margin-left: auto`
- Projectivy note moved inside Google TV Launcher action-row
- Status messages auto-dismiss after 5 seconds on both dashboards
- Dashboard layout fills viewport height via `calc(100vh - 180px)`

- [x] All changes implemented and verified visually

---

### Task 9: ws-scrcpy-web embed mode

**Files (ws-scrcpy-web repo):**
- Modify: `src/app/index.ts` — parse `embed=true` from hash, add body class
- Modify: `src/app/client/BaseClient.ts` — preserve `embed` class across `setBodyClass()` calls
- Modify: `src/app/googDevice/client/StreamClientScrcpy.ts` — force `fitToScreen` in embed, auto-enable keyboard capture
- Modify: `src/style/app.css` — embed CSS hides toolbar/morebox, flexbox video layout

**Files (Control Menu repo):**
- Modify: `src/ControlMenu/Components/Shared/ScrcpyMirror.razor` — pass `embed=true` in iframe URL, auto-focus iframe on load

**How it works:**
- When `embed=true` is in the hash params, `body.embed` class is added
- CSS hides `.control-buttons-list` and `.more-box`, switches `.device-view` from float to flex
- `fitToScreen` forced true so video scales to iframe size instead of native 1080p
- Keyboard capture auto-enabled so D-pad keys work without toolbar toggle
- `setBodyClass()` patched to preserve `embed` class (was being wiped by `className = text`)

**Known issues:**
- Mouse clicks send touch events which freeze the scrcpy video stream (pre-existing scrcpy issue, not embed-specific)
- Video stream occasionally shows black on first load (scrcpy connection timing)
- Keyboard D-pad input needs testing — may require iframe focus

- [x] All changes implemented, video rendering confirmed in iframe
