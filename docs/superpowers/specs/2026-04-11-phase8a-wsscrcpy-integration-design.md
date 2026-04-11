# Phase 8a: ws-scrcpy-web Integration — Design Spec

## Overview

Embed ws-scrcpy-web's browser-based Android screen mirroring directly into Control Menu's dashboards via iframe. Control Menu manages ws-scrcpy-web as an auto-launched background process. No changes to the ws-scrcpy-web repository — all coupling is one-directional.

## Architecture

Control Menu gains a `WsScrcpyService` (singleton hosted service) that spawns ws-scrcpy-web as a child Node.js process on startup. Both `GoogleTvDashboard` and `PixelDashboard` embed a shared `ScrcpyMirror` Blazor component that renders an iframe pointing to ws-scrcpy-web's existing hash-based stream URL. The existing `LaunchScrcpyAsync` method (which opens a desktop scrcpy window) is removed.

### Key Principle

ws-scrcpy-web remains a fully standalone project. No Control Menu-specific code in the ws-scrcpy-web repo. Integration is via composition: process management + iframe embedding.

---

## Components

### WsScrcpyService (new, singleton IHostedService)

**Responsibility:** Manage ws-scrcpy-web as a child process.

**Lifecycle:**
- On `StartAsync()`: reads ws-scrcpy-web path from `ConfigurationService` settings DB, spawns `node {path}/dist/index.js` with `PORT=8000` env var, waits for HTTP 200 on `http://localhost:8000/`
- Exposes `string BaseUrl` property (`http://localhost:8000`) and `bool IsRunning` for Blazor components
- Monitors process exit event: auto-restarts once after 2-second delay. If crashes again within 30 seconds, gives up and sets `IsRunning = false`
- On `StopAsync()`: kills the child process

**Configuration:**
- ws-scrcpy-web path stored in settings DB (same pattern as adb, scrcpy paths)
- Managed as a known dependency in `DependencyManagerService`

### ScrcpyMirror.razor (new, shared component)

**Responsibility:** Reusable iframe embed for both dashboards.

**Parameters:**
- `string Udid` — device address (e.g., `192.168.86.43:5555`)

**Behavior:**
- Injects `WsScrcpyService` to get `BaseUrl` and `IsRunning`
- Renders a "Screen Mirror" toggle button
- When toggled on: shows iframe with `src="{BaseUrl}/#!action=stream&udid={Udid}&player=webcodecs"`
- When toggled off: hides iframe (not destroyed, to avoid reconnection)
- Default iframe size: 960x540 (16:9), stream auto-fits inside
- If `IsRunning` is false: shows "Screen mirroring unavailable — configure ws-scrcpy-web path in Settings" instead of the button

### DependencyManagerService update

- Add ws-scrcpy-web as a known dependency
- Auto-scan for `node` in PATH
- Auto-scan for ws-scrcpy-web `dist/index.js` in common locations (sibling directories, user repos)
- Settings page entry for manual path configuration

### AdbService cleanup

- Remove `LaunchScrcpyAsync(string ip, int port)` from `IAdbService` interface and `AdbService` implementation
- Desktop scrcpy is no longer needed — ws-scrcpy-web handles everything

---

## Data Flow

```
Control Menu startup
  -> WsScrcpyService.StartAsync()
    -> reads ws-scrcpy-web path from ConfigurationService
    -> spawns: node {path}/dist/index.js (PORT=8000)
    -> health check: HTTP GET http://localhost:8000/ until 200

User opens Google TV Dashboard
  -> sees device controls + "Screen Mirror" button
  -> clicks "Screen Mirror"
    -> ScrcpyMirror component toggles visible
    -> iframe src = "http://localhost:8000/#!action=stream&udid=192.168.86.43:5555&player=webcodecs"
    -> ws-scrcpy-web handles everything (device connection, scrcpy-server push, streaming)

User clicks "Screen Mirror" again (or navigates away)
  -> iframe hidden (avoids reconnection overhead)
```

Control Menu never talks to ADB for mirroring. ws-scrcpy-web owns the entire scrcpy pipeline.

---

## Error Handling

**ws-scrcpy-web not installed/configured:**
- `WsScrcpyService` checks path on startup. If missing, logs warning, sets `IsRunning = false`
- Dashboards show "Screen mirroring unavailable" message instead of mirror button
- No crash, no retry loop — graceful degradation

**ws-scrcpy-web process crashes:**
- Auto-restart once after 2-second delay
- If crashes again within 30 seconds, gives up, sets `IsRunning = false`
- Dashboard shows "Screen mirroring service stopped" with manual "Retry" button

**Device not reachable:**
- ws-scrcpy-web handles this internally (timeout, error in browser)
- Control Menu doesn't need to know — iframe shows ws-scrcpy-web's own error state

**Node.js not installed:**
- Same as "not configured" — process can't spawn, logs warning, degrades gracefully

---

## File Changes (Control Menu repo only)

### New Files
| File | Purpose |
|------|---------|
| `src/ControlMenu/Services/WsScrcpyService.cs` | Hosted service: spawns/monitors ws-scrcpy-web process |
| `src/ControlMenu/Components/Shared/ScrcpyMirror.razor` | Reusable iframe component with toggle |

### Modified Files
| File | Change |
|------|--------|
| `src/ControlMenu/Program.cs` | Register `WsScrcpyService` as hosted service |
| `src/ControlMenu/Modules/AndroidDevices/Services/AdbService.cs` | Remove `LaunchScrcpyAsync` |
| `src/ControlMenu/Modules/AndroidDevices/Services/IAdbService.cs` | Remove `LaunchScrcpyAsync` from interface |
| `src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor` | Replace mirror button with `ScrcpyMirror` component |
| `src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor` | Replace mirror button with `ScrcpyMirror` component |
| `src/ControlMenu/Services/DependencyManagerService.cs` | Add ws-scrcpy-web as managed dependency |

### No changes to ws-scrcpy-web repo
