# A5: Scrcpy Settings Modal

**Date:** 2026-04-23
**Status:** Approved design, pending implementation plan
**Priority:** #1 on active backlog (after B-5 shipped)

## Problem

Scrcpy mirroring defaults work ~90% of the time. When they don't (h265-only encoder on an old TV, 20MHz WiFi so bitrate needs trimming, audio interfering with a phone stream), there's no recovery path short of editing embed URL params manually. The gear is "rarely needed, great-to-have when you do need it."

## Solution

Add a "Stream Settings" action to each device dashboard's quick-actions panel. Clicking it opens a Blazor modal with per-device scrcpy settings (video codec, encoder, bitrate, max fps, max resolution, audio toggle, audio source, audio codec). Settings persist in CM's SQLite database and are passed to ws-scrcpy-web's embed.html as URL params.

**No changes to ws-scrcpy-web.** All existing endpoints and URL params are reused as-is.

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Architecture | Hybrid — CM owns modal + persistence, ws-scrcpy-web provides probe + accepts URL params | No new endpoints needed; DB persistence survives cross-browser |
| Probe mechanism | Server-side WebSocket from CM backend | No ws-scrcpy-web changes; works in both managed and external deploy modes |
| Persistence | Individual keys per setting per device in Settings table | Matches existing `device-pin-{deviceId}` pattern; granular read/write |
| Gear placement | "Stream Settings" row in quick-actions panel | Keeps all device controls in one column; no new UI chrome |
| Modal field order | Matches ws-scrcpy-web's ConfigureScrcpy modal layout | Familiar to users who've seen both UIs |
| First-visit behavior | Eager probe on dashboard load with 3-second grace period | Single clean connection; no connect-then-reconnect jarring |
| Modal buttons | Save (persist only), Connect (reload stream + close), Clear/Refresh (re-probe) | Clean separation of concerns; only Connect closes modal |

## Settings Exposed

All 8 settings that ws-scrcpy-web's embed-entry.ts accepts:

| Setting | Key Pattern | Type | URL Param | Form Control |
|---------|-------------|------|-----------|--------------|
| Video codec | `scrcpy-codec-{deviceId}` | enum | `codec` | Dropdown: h264 / h265 / av1 (filtered by probe) |
| Encoder | `scrcpy-encoder-{deviceId}` | string | `encoder` | Dropdown: from probe `videoEncoders[]`, filtered by selected codec. Empty = auto |
| Max FPS | `scrcpy-maxfps-{deviceId}` | int | `maxFps` | Range slider (1–60), value shown in label |
| Bitrate | `scrcpy-bitrate-{deviceId}` | int (bps) | `bitrate` | Range slider (512KB–8MB), value shown in label as Mbps |
| Max resolution | `scrcpy-maxsize-{deviceId}` | int (px) | `maxSize` | Range slider, value shown in label. Presets: 720 / 1080 / 1440 / native |
| Audio enabled | `scrcpy-audio-{deviceId}` | bool | `audio` | Checkbox. When off, audio codec + source grayed out |
| Audio source | `scrcpy-audiosource-{deviceId}` | enum | `audioSource` | Dropdown: playback / output / mic |
| Audio codec | `scrcpy-audiocodec-{deviceId}` | enum | `audioCodec` | Dropdown: opus / aac / flac / raw (filtered by probe) |

All keys scoped to `moduleId = "android-devices"`.

## Probe Cache Keys

3 additional keys store raw probe data so the modal can populate dropdowns without re-probing:

| Key Pattern | Content |
|-------------|---------|
| `scrcpy-videoencoders-{deviceId}` | Comma-separated encoder names |
| `scrcpy-audioencoders-{deviceId}` | Comma-separated encoder names |
| `scrcpy-screendims-{deviceId}` | `{width}x{height}x{density}` |

Total: 11 keys per device (8 user settings + 3 probe cache).

## Smart Defaults (from probe)

When no saved settings exist and a probe succeeds:

| Setting | Default derivation |
|---------|--------------------|
| codec | `h265` if any h265 encoder in `videoEncoders[]`, else `h264` |
| encoder | Empty (auto — let scrcpy pick) |
| bitrate | `width × height × 4` bps (e.g., 1920×1080 → ~8.3Mbps) |
| maxFps | `60` |
| maxSize | Device's native width (larger dimension) — no downscaling |
| audio | `true` if `audioEncoders[]` is non-empty, else `false` |
| audioSource | `playback` |
| audioCodec | `opus` if in `audioEncoders[]`, else first available |

## First-Visit Flow

1. Dashboard `OnInitializedAsync` checks DB for `scrcpy-codec-{deviceId}`
2. Key missing → first visit. ScrcpyMirror renders loading placeholder (spinner)
3. Dashboard calls `IScrcpyProbeService.ProbeAsync(udid)` with 3-second grace period
4. **Probe succeeds within 3s:** derive all 8 defaults, save all 11 keys to DB, pass settings to ScrcpyMirror, stream starts once with full params. Single clean connection.
5. **Probe exceeds 3s:** start stream with bare defaults (`device` + `deviceKind` only). If probe finishes later, save results to DB silently — available for next visit or modal open.
6. **Probe fails:** start stream with bare defaults. No keys saved. User can open modal and hit Clear/Refresh to retry.

## Subsequent Visit Flow

1. Dashboard reads 8 setting keys from DB → passes `ScrcpySettings` to ScrcpyMirror → stream starts immediately with full params. No probe.

## Modal UI

### Header

Device name in upper-left corner (e.g., "Jamie's Tablet"), close button (X) in upper-right. Matches ws-scrcpy-web's ConfigureScrcpy modal header pattern.

### Form Layout

Two-column grid matching ws-scrcpy-web: labels left (35%), controls right (65%). Field order top to bottom:

1. **video codec:** dropdown (h264 / h265 / av1, filtered by probe cache)
2. **encoder:** dropdown (filtered by selected codec, empty = auto)
3. **max fps:** range slider (1–60), current value in label
4. **audio codec:** dropdown (opus / aac / flac / raw, filtered by probe cache)
5. **audio source:** dropdown (playback / output / mic)
6. **enable audio:** checkbox. When unchecked, audio codec + source grayed out
7. **bitrate:** range slider (512KB–8MB), current value in label as Mbps

### Footer Buttons

Three buttons, center-justified:

| Button | Action | Closes modal? | Notification |
|--------|--------|---------------|--------------|
| **Save** | Persists current form values (8 keys) to DB | No | Chip: "Settings saved" |
| **Connect** | Reloads stream with current form values (does NOT save) | Yes | None (stream reloads) |
| **Clear / Refresh** | Clears all 11 DB keys, re-probes device, repopulates form with derived defaults | No | Chip: "Defaults restored" |

Tooltips:
- Save: "Save current settings"
- Connect: "Reconnect stream with current settings"
- Clear / Refresh: "Clear saved settings and re-probe device capabilities"

### Dropdown Population

When modal opens:
- If probe cache keys (9-11) exist in DB: populate dropdowns from cached data
- If probe cache keys missing: show "Probing..." state, run fresh probe, populate on completion

## ScrcpyMirror Changes

`ScrcpyMirror.razor` gains:

- **`ScrcpySettings?` parameter** — when non-null, appends all settings as URL params to embed URL
- **`ReloadStream()` public method** — forces iframe reload by toggling a render key. Called by dashboard after Connect.
- **Loading placeholder** — shown during first-visit 3-second probe wait (replaces iframe temporarily)

URL construction with settings:
```
{BaseUrl}/embed.html?device={udid}&deviceKind=tv&codec=h265&bitrate=8300000&maxFps=60&maxSize=1920&audio=true&audioSource=playback&audioCodec=opus&encoder=OMX.qcom.video.encoder.hevc
```

Without settings (bare defaults): `{BaseUrl}/embed.html?device={udid}&deviceKind=tv`

## Probe Service

### Interface

```csharp
public interface IScrcpyProbeService
{
    Task<ScrcpyProbeResult?> ProbeAsync(string udid, CancellationToken ct = default);
}
```

### ScrcpyProbeResult

```csharp
public record ScrcpyProbeResult(
    int Width,
    int Height,
    int Density,
    string[] VideoEncoders,
    string[] AudioEncoders);
```

### Implementation

- Singleton registration (stateless — fresh WebSocket per call)
- Opens `ClientWebSocket` to `{WsScrcpyService.BaseUrl}/?action=PROBE_DEVICE&udid={udid}`
- Reads first message, deserializes JSON to `ScrcpyProbeResult`
- 5-second timeout (probe typically 100-300ms)
- Returns `null` on any failure (ws-scrcpy-web not running, device offline, timeout, parse error)

## ScrcpySettings Record

```csharp
public record ScrcpySettings(
    string? Codec,
    string? Encoder,
    int? Bitrate,
    int? MaxFps,
    int? MaxSize,
    bool? Audio,
    string? AudioSource,
    string? AudioCodec);
```

Nullable fields — only non-null values are appended to the embed URL.

## Dashboard Integration

All 4 dashboards (GoogleTvDashboard, PixelDashboard, TabletDashboard, WatchDashboard):

1. Add "Stream Settings" row to quick-actions panel (below existing controls)
2. Inject `IScrcpyProbeService`, `IConfigurationService`, `IDeviceChangeNotifier`
3. First-visit probe logic in `OnInitializedAsync`
4. `ScrcpySettingsModal` component wired with device ID, device name, and settings-changed callback
5. Settings-changed callback calls `ScrcpyMirror.ReloadStream()`

## Files Summary

| Action | File |
|--------|------|
| New | `src/ControlMenu/Services/IScrcpyProbeService.cs` |
| New | `src/ControlMenu/Services/ScrcpyProbeService.cs` |
| New | `src/ControlMenu/Services/ScrcpyProbeResult.cs` |
| New | `src/ControlMenu/Services/ScrcpySettings.cs` |
| New | `src/ControlMenu/Components/Shared/ScrcpySettingsModal.razor` |
| New | `src/ControlMenu/Components/Shared/ScrcpySettingsModal.razor.css` |
| New | `tests/ControlMenu.Tests/Services/ScrcpyProbeServiceTests.cs` |
| New | `tests/ControlMenu.Tests/Services/Fakes/FakeScrcpyProbeService.cs` |
| Modify | `src/ControlMenu/Components/Shared/ScrcpyMirror.razor` |
| Modify | `src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor` |
| Modify | `src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor` |
| Modify | `src/ControlMenu/Modules/AndroidDevices/Pages/TabletDashboard.razor` |
| Modify | `src/ControlMenu/Modules/AndroidDevices/Pages/WatchDashboard.razor` |
| Modify | `src/ControlMenu/Program.cs` |

## Not In Scope

- Changes to ws-scrcpy-web (all existing endpoints/URL params reused)
- Database migrations (uses existing Settings key-value table)
- Display selection (CM dashboards are single-display)
- Advanced settings (i-frame interval, fit-to-screen, max width/height, codec options)
- Per-encoder bitrate/fps caps (scrcpy handles encoder limits gracefully)
