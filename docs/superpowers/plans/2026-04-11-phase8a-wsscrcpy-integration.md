# Phase 8a: ws-scrcpy-web Integration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Embed ws-scrcpy-web's browser-based screen mirroring into Control Menu's Google TV and Pixel dashboards via iframe, with ws-scrcpy-web auto-launched as a managed background process.

**Architecture:** A new `WsScrcpyService` hosted service reads the ws-scrcpy-web installation path from the settings DB, spawns `node dist/index.js` on startup, monitors process health, and exposes `BaseUrl`/`IsRunning` to Blazor components. A shared `ScrcpyMirror.razor` component renders a togglable iframe on both dashboards. The existing `LaunchScrcpyAsync` (desktop scrcpy) is removed. ws-scrcpy-web is added as a module dependency for path discovery.

**Tech Stack:** C# / .NET 9, Blazor Server, Process management, iframe

**Spec:** `docs/superpowers/specs/2026-04-11-phase8a-wsscrcpy-integration-design.md`

**Working directory:** `C:/Users/jscha/source/repos/tools-menu`

---

## File Map

### New Files
| File | Purpose |
|------|---------|
| `src/ControlMenu/Services/WsScrcpyService.cs` | IHostedService: spawns/monitors ws-scrcpy-web Node.js process |
| `src/ControlMenu/Components/Shared/ScrcpyMirror.razor` | Reusable iframe component with toggle |

### Modified Files
| File | Change |
|------|--------|
| `src/ControlMenu/Program.cs` | Register WsScrcpyService as singleton + hosted service |
| `src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs` | Add ws-scrcpy-web + node as module dependencies |
| `src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor` | Replace mirror button with ScrcpyMirror component |
| `src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor` | Replace mirror button with ScrcpyMirror component |
| `src/ControlMenu/Modules/AndroidDevices/Services/IAdbService.cs` | Remove LaunchScrcpyAsync |
| `src/ControlMenu/Modules/AndroidDevices/Services/AdbService.cs` | Remove LaunchScrcpyAsync |

---

## Task 1: Create WsScrcpyService

**Files:**
- Create: `src/ControlMenu/Services/WsScrcpyService.cs`

The service manages ws-scrcpy-web as a child process. It's both an `IHostedService` (starts/stops with app) and injectable by Blazor components (exposes state).

- [ ] **Step 1: Create WsScrcpyService**

```csharp
// src/ControlMenu/Services/WsScrcpyService.cs
using System.Diagnostics;
using ControlMenu.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Services;

public class WsScrcpyService : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WsScrcpyService> _logger;
    private Process? _process;
    private int _crashCount;
    private DateTime _lastCrash = DateTime.MinValue;

    public string BaseUrl { get; private set; } = "http://localhost:8000";
    public bool IsRunning => _process is { HasExited: false };

    public WsScrcpyService(IServiceScopeFactory scopeFactory, ILogger<WsScrcpyService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var path = await GetWsScrcpyPathAsync();
        if (string.IsNullOrEmpty(path))
        {
            _logger.LogWarning("ws-scrcpy-web path not configured — screen mirroring unavailable");
            return;
        }

        var indexJs = Path.Combine(path, "dist", "index.js");
        if (!File.Exists(indexJs))
        {
            _logger.LogWarning("ws-scrcpy-web not found at {Path} — screen mirroring unavailable", indexJs);
            return;
        }

        SpawnProcess(indexJs);

        // Wait for HTTP 200
        using var http = new HttpClient();
        for (var i = 0; i < 30; i++)
        {
            if (cancellationToken.IsCancellationRequested) return;
            try
            {
                var response = await http.GetAsync(BaseUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("ws-scrcpy-web ready at {Url}", BaseUrl);
                    return;
                }
            }
            catch { /* not ready yet */ }
            await Task.Delay(500, cancellationToken);
        }

        _logger.LogWarning("ws-scrcpy-web did not become ready within 15 seconds");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        KillProcess();
        return Task.CompletedTask;
    }

    private void SpawnProcess(string indexJs)
    {
        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = indexJs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Environment = { ["PORT"] = "8000" }
            },
            EnableRaisingEvents = true
        };

        _process.Exited += OnProcessExited;
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        _logger.LogInformation("ws-scrcpy-web started (PID {Pid})", _process.Id);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        _logger.LogWarning("ws-scrcpy-web process exited");

        var now = DateTime.UtcNow;
        if ((now - _lastCrash).TotalSeconds < 30)
        {
            _crashCount++;
        }
        else
        {
            _crashCount = 1;
        }
        _lastCrash = now;

        if (_crashCount <= 1)
        {
            _logger.LogInformation("Restarting ws-scrcpy-web in 2 seconds...");
            Task.Delay(2000).ContinueWith(_ =>
            {
                var indexJs = _process?.StartInfo.Arguments;
                if (!string.IsNullOrEmpty(indexJs))
                {
                    SpawnProcess(indexJs);
                }
            });
        }
        else
        {
            _logger.LogError("ws-scrcpy-web crashed twice within 30s — giving up");
        }
    }

    public void Restart()
    {
        _crashCount = 0;
        var indexJs = _process?.StartInfo.Arguments;
        KillProcess();
        if (!string.IsNullOrEmpty(indexJs))
        {
            SpawnProcess(indexJs);
        }
    }

    private void KillProcess()
    {
        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                _logger.LogInformation("ws-scrcpy-web stopped");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill ws-scrcpy-web process");
            }
        }
        _process?.Dispose();
        _process = null;
    }

    private async Task<string?> GetWsScrcpyPathAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var setting = await db.Settings.FirstOrDefaultAsync(s =>
            s.Key == "ws_scrcpy_web_path" && s.ModuleId == "android-devices");
        return setting?.Value;
    }

    public void Dispose()
    {
        KillProcess();
        GC.SuppressFinalize(this);
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/ControlMenu/`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/Services/WsScrcpyService.cs
git commit -m "feat(phase8a): add WsScrcpyService hosted service for ws-scrcpy-web process management"
```

---

## Task 2: Register WsScrcpyService in Program.cs

**Files:**
- Modify: `src/ControlMenu/Program.cs`

- [ ] **Step 1: Add service registration**

After the `IAdbService` registration (around line 38), add:

```csharp
// ws-scrcpy-web process management
builder.Services.AddSingleton<WsScrcpyService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WsScrcpyService>());
```

The dual registration lets Blazor components inject `WsScrcpyService` directly while also starting it as a hosted service.

- [ ] **Step 2: Verify build**

Run: `dotnet build src/ControlMenu/`

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/Program.cs
git commit -m "feat(phase8a): register WsScrcpyService in DI container"
```

---

## Task 3: Add ws-scrcpy-web as module dependency

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs`

- [ ] **Step 1: Add ws-scrcpy-web dependency**

In the `Dependencies` property, after the existing `scrcpy` entry, add:

```csharp
        new ModuleDependency
        {
            Name = "node",
            ExecutableName = "node",
            VersionCommand = "node --version",
            VersionPattern = @"v([\d.]+)",
            SourceType = UpdateSourceType.DirectUrl,
            ProjectHomeUrl = "https://nodejs.org/",
            DownloadUrl = "https://nodejs.org/en/download/"
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
```

Note: ws-scrcpy-web isn't a single executable — it's a Node.js project. The `VersionCommand` is a placeholder check. The actual path is stored via ConfigurationService as `ws_scrcpy_web_path` setting. The dependency entry ensures it appears on the Dependencies page for visibility.

- [ ] **Step 2: Verify build**

Run: `dotnet build src/ControlMenu/`

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs
git commit -m "feat(phase8a): add node and ws-scrcpy-web as Android module dependencies"
```

---

## Task 4: Create ScrcpyMirror.razor component

**Files:**
- Create: `src/ControlMenu/Components/Shared/ScrcpyMirror.razor`

- [ ] **Step 1: Create the component**

```razor
@* src/ControlMenu/Components/Shared/ScrcpyMirror.razor *@
@inject WsScrcpyService WsScrcpy

<div class="scrcpy-mirror">
    @if (!WsScrcpy.IsRunning)
    {
        <div class="alert alert-warning">
            <i class="bi bi-exclamation-triangle"></i>
            Screen mirroring unavailable — configure ws-scrcpy-web path in Settings
        </div>
    }
    else
    {
        <button class="btn @(_visible ? "btn-secondary" : "btn-primary")" @onclick="Toggle">
            <i class="bi bi-cast"></i>
            @(_visible ? "Hide Mirror" : "Screen Mirror")
        </button>

        @if (_visible)
        {
            <div class="scrcpy-mirror-frame mt-2">
                <iframe src="@GetStreamUrl()"
                        width="960"
                        height="540"
                        style="border: 1px solid var(--border-color); border-radius: 8px;"
                        allow="autoplay"
                        sandbox="allow-scripts allow-same-origin">
                </iframe>
            </div>
        }
    }
</div>

@code {
    [Parameter, EditorRequired]
    public string Udid { get; set; } = string.Empty;

    private bool _visible;

    private void Toggle() => _visible = !_visible;

    private string GetStreamUrl()
    {
        var encoded = Uri.EscapeDataString(Udid);
        return $"{WsScrcpy.BaseUrl}/#!action=stream&udid={encoded}&player=webcodecs";
    }
}
```

- [ ] **Step 2: Add using statement**

Ensure `WsScrcpyService` is accessible in Razor components. Check if `src/ControlMenu/Components/_Imports.razor` exists and add:

```razor
@using ControlMenu.Services
```

(If this using is already present from other services, skip this step.)

- [ ] **Step 3: Verify build**

Run: `dotnet build src/ControlMenu/`

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Components/Shared/ScrcpyMirror.razor
git commit -m "feat(phase8a): add ScrcpyMirror Blazor component with iframe embedding"
```

---

## Task 5: Update GoogleTvDashboard to use ScrcpyMirror

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor`

- [ ] **Step 1: Replace the Screen Mirror action card**

Find the Screen Mirror card (the `<div class="action-card">` block containing "Screen Mirror" / "Launch scrcpy"):

```razor
<!-- Screen Mirror -->
<div class="action-card">
    <div class="action-card-icon"><i class="bi bi-display"></i></div>
    <h3>Screen Mirror</h3>
    <p>Launch scrcpy</p>
    <button class="btn btn-primary" @onclick="LaunchScreenMirror" disabled="@_busy">
        <i class="bi bi-cast"></i> Mirror
    </button>
</div>
```

Replace with:

```razor
<!-- Screen Mirror -->
<div class="action-card">
    <div class="action-card-icon"><i class="bi bi-display"></i></div>
    <h3>Screen Mirror</h3>
    <p>Browser-based screen mirroring</p>
    <ScrcpyMirror Udid="@($"{Ip}:{Port}")" />
</div>
```

- [ ] **Step 2: Remove the LaunchScreenMirror method**

Find and delete the `LaunchScreenMirror` method from the `@code` block:

```csharp
// DELETE:
private async Task LaunchScreenMirror()
{
    _busy = true;
    SetStatus("Launching screen mirror...", "status-info", "bi-cast");
    await AdbService.LaunchScrcpyAsync(Ip, Port);
    SetStatus("Screen mirror closed.", "status-info", "bi-info-circle");
    _busy = false;
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build src/ControlMenu/`

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor
git commit -m "feat(phase8a): replace desktop scrcpy with embedded ScrcpyMirror on Google TV dashboard"
```

---

## Task 6: Update PixelDashboard to use ScrcpyMirror

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor`

- [ ] **Step 1: Replace the Screen Mirror action card**

Find the Screen Mirror card and replace with the same pattern:

```razor
<!-- Screen Mirror -->
<div class="action-card">
    <div class="action-card-icon"><i class="bi bi-display"></i></div>
    <h3>Screen Mirror</h3>
    <p>Browser-based screen mirroring</p>
    <ScrcpyMirror Udid="@($"{Ip}:{Port}")" />
</div>
```

- [ ] **Step 2: Remove the LaunchScreenMirror method**

Delete the `LaunchScreenMirror` method from the `@code` block. Note: Pixel's version has an auto-connect guard — delete the entire method:

```csharp
// DELETE:
private async Task LaunchScreenMirror()
{
    if (!_connected)
    {
        await ConnectDevice();
        if (!_connected) return;
    }
    _busy = true;
    SetStatus("Launching screen mirror...", "status-info", "bi-cast");
    await AdbService.LaunchScrcpyAsync(Ip, Port);
    SetStatus("Screen mirror closed.", "status-info", "bi-info-circle");
    _busy = false;
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build src/ControlMenu/`

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor
git commit -m "feat(phase8a): replace desktop scrcpy with embedded ScrcpyMirror on Pixel dashboard"
```

---

## Task 7: Remove LaunchScrcpyAsync from AdbService

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/Services/IAdbService.cs`
- Modify: `src/ControlMenu/Modules/AndroidDevices/Services/AdbService.cs`

- [ ] **Step 1: Remove from interface**

In `IAdbService.cs`, delete line 29:

```csharp
// DELETE:
    Task LaunchScrcpyAsync(string ip, int port, CancellationToken ct = default);
```

- [ ] **Step 2: Remove from implementation**

In `AdbService.cs`, delete the `LaunchScrcpyAsync` method:

```csharp
// DELETE:
    public async Task LaunchScrcpyAsync(string ip, int port, CancellationToken ct = default)
    {
        await _executor.ExecuteAsync("scrcpy", $"--video-encoder=OMX.google.h264.encoder --no-audio -s {ip}:{port}", null, ct);
    }
```

- [ ] **Step 3: Search for remaining callers**

```bash
grep -rn "LaunchScrcpy" src/ControlMenu/ --include="*.cs" --include="*.razor"
```

Expected: No matches (dashboards were updated in Tasks 5-6).

- [ ] **Step 4: Verify build + tests**

```bash
dotnet build src/ControlMenu/
dotnet test tests/ControlMenu.Tests/
```

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Services/IAdbService.cs src/ControlMenu/Modules/AndroidDevices/Services/AdbService.cs
git commit -m "refactor(phase8a): remove LaunchScrcpyAsync — replaced by ws-scrcpy-web iframe"
```

---

## Task 8: Smoke test

- [ ] **Step 1: Ensure ws-scrcpy-web is built and ready**

```bash
cd C:/Users/jscha/source/repos/ws-scrcpy-web
npm run build
```

- [ ] **Step 2: Configure ws-scrcpy-web path in Control Menu**

Start Control Menu, go to Settings, and set `ws_scrcpy_web_path` to `C:\Users\jscha\source\repos\ws-scrcpy-web` in the Android Devices module settings.

Alternatively, insert directly into the DB:
```bash
cd C:/Users/jscha/source/repos/tools-menu/src/ControlMenu
sqlite3 controlmenu.db "INSERT OR REPLACE INTO Settings (Id, ModuleId, Key, Value, IsSecret) VALUES (lower(hex(randomblob(16))), 'android-devices', 'ws_scrcpy_web_path', 'C:\Users\jscha\source\repos\ws-scrcpy-web', 0);"
```

- [ ] **Step 3: Start Control Menu**

```bash
cd C:/Users/jscha/source/repos/tools-menu/src/ControlMenu
dotnet run
```

Check logs for: `ws-scrcpy-web ready at http://localhost:8000`

- [ ] **Step 4: Test Google TV Dashboard**

Open `http://localhost:5159/android/googletv`, select a device. The Screen Mirror card should show a "Screen Mirror" button. Click it — the iframe should appear with the live device stream.

- [ ] **Step 5: Test Pixel Dashboard**

Open `http://localhost:5159/android/pixel`, same test.

- [ ] **Step 6: Test error state**

Stop the Control Menu, delete the ws-scrcpy-web path setting, restart. The dashboards should show "Screen mirroring unavailable" warning instead of the button.

- [ ] **Step 7: Commit any fixes**
