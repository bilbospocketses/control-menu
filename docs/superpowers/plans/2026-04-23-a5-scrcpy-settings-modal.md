# A5: Scrcpy Settings Modal — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add per-device scrcpy stream settings (video codec, encoder, bitrate, fps, resolution, audio) to device dashboards with probe-driven defaults, DB persistence, and a settings modal.

**Architecture:** Server-side WebSocket probe to ws-scrcpy-web for device capabilities, settings stored as individual keys in the existing Settings table via `IConfigurationService`, passed to ws-scrcpy-web as URL params on the embed iframe. First-visit probes eagerly with a 3-second grace period before connecting. Modal follows ws-scrcpy-web's 2-column grid layout.

**Tech Stack:** .NET 9, Blazor Server, xUnit, `System.Net.WebSockets.ClientWebSocket`, SQLite via EF Core

**Spec:** `docs/superpowers/specs/2026-04-23-a5-scrcpy-settings-modal-design.md`

**Test command:** `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~<TestClass>"`

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| New | `src/ControlMenu/Services/ScrcpyProbeResult.cs` | Record type: probe response (width, height, density, encoders) |
| New | `src/ControlMenu/Services/ScrcpySettings.cs` | Record type: 8 user settings + static helpers for DB load/save/defaults |
| New | `src/ControlMenu/Services/IScrcpyProbeService.cs` | Interface: `ProbeAsync(udid)` |
| New | `src/ControlMenu/Services/ScrcpyProbeService.cs` | Singleton: WebSocket probe to ws-scrcpy-web |
| New | `tests/ControlMenu.Tests/Services/Fakes/FakeScrcpyProbeService.cs` | Test double with configurable result |
| New | `tests/ControlMenu.Tests/Services/ScrcpySettingsTests.cs` | Tests for defaults derivation + DB round-trip helpers |
| New | `src/ControlMenu/Components/Shared/ScrcpySettingsModal.razor` | Blazor modal: 2-column grid form, Save/Connect/Clear buttons |
| New | `src/ControlMenu/Components/Shared/ScrcpySettingsModal.razor.css` | Scoped styles: grid layout, slider styling, notification chip |
| Modify | `src/ControlMenu/Components/Shared/ScrcpyMirror.razor` | Add `ScrcpySettings` param, URL param building, `ReloadStream()`, loading placeholder |
| Modify | `src/ControlMenu/Program.cs` | Register `IScrcpyProbeService` singleton |
| Modify | `src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor` | Stream Settings action, first-visit probe, modal wiring |
| Modify | `src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor` | Same pattern |
| Modify | `src/ControlMenu/Modules/AndroidDevices/Pages/TabletDashboard.razor` | Same pattern |
| Modify | `src/ControlMenu/Modules/AndroidDevices/Pages/WatchDashboard.razor` | Same pattern |

---

### Task 1: Create ScrcpyProbeResult and ScrcpySettings records

**Files:**
- Create: `src/ControlMenu/Services/ScrcpyProbeResult.cs`
- Create: `src/ControlMenu/Services/ScrcpySettings.cs`

- [ ] **Step 1: Create ScrcpyProbeResult record**

Create `src/ControlMenu/Services/ScrcpyProbeResult.cs`:

```csharp
using System.Text.Json.Serialization;

namespace ControlMenu.Services;

public record ScrcpyProbeResult(
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("density")] int Density,
    [property: JsonPropertyName("videoEncoders")] string[] VideoEncoders,
    [property: JsonPropertyName("audioEncoders")] string[] AudioEncoders);
```

- [ ] **Step 2: Create ScrcpySettings record with DB helpers and defaults derivation**

Create `src/ControlMenu/Services/ScrcpySettings.cs`:

```csharp
namespace ControlMenu.Services;

public record ScrcpySettings(
    string? Codec,
    string? Encoder,
    int? Bitrate,
    int? MaxFps,
    int? MaxSize,
    bool? Audio,
    string? AudioSource,
    string? AudioCodec)
{
    private const string Module = "android-devices";

    public static ScrcpySettings DeriveDefaults(ScrcpyProbeResult probe)
    {
        var hasH265 = probe.VideoEncoders.Any(e => e.Contains("265") || e.Contains("hevc", StringComparison.OrdinalIgnoreCase));
        var hasAudio = probe.AudioEncoders.Length > 0;
        var hasOpus = probe.AudioEncoders.Any(e => e.Contains("opus", StringComparison.OrdinalIgnoreCase));
        var nativeSize = Math.Max(probe.Width, probe.Height);

        return new ScrcpySettings(
            Codec: hasH265 ? "h265" : "h264",
            Encoder: null,
            Bitrate: probe.Width * probe.Height * 4,
            MaxFps: 60,
            MaxSize: nativeSize,
            Audio: hasAudio,
            AudioSource: "playback",
            AudioCodec: hasOpus ? "opus" : probe.AudioEncoders.FirstOrDefault());
    }

    public static async Task<ScrcpySettings?> LoadAsync(IConfigurationService config, Guid deviceId)
    {
        var codec = await config.GetSettingAsync($"scrcpy-codec-{deviceId}", Module);
        if (codec is null) return null;

        return new ScrcpySettings(
            Codec: codec,
            Encoder: await config.GetSettingAsync($"scrcpy-encoder-{deviceId}", Module),
            Bitrate: int.TryParse(await config.GetSettingAsync($"scrcpy-bitrate-{deviceId}", Module), out var br) ? br : null,
            MaxFps: int.TryParse(await config.GetSettingAsync($"scrcpy-maxfps-{deviceId}", Module), out var fps) ? fps : null,
            MaxSize: int.TryParse(await config.GetSettingAsync($"scrcpy-maxsize-{deviceId}", Module), out var sz) ? sz : null,
            Audio: bool.TryParse(await config.GetSettingAsync($"scrcpy-audio-{deviceId}", Module), out var aud) ? aud : null,
            AudioSource: await config.GetSettingAsync($"scrcpy-audiosource-{deviceId}", Module),
            AudioCodec: await config.GetSettingAsync($"scrcpy-audiocodec-{deviceId}", Module));
    }

    public async Task SaveAsync(IConfigurationService config, Guid deviceId)
    {
        if (Codec is not null) await config.SetSettingAsync($"scrcpy-codec-{deviceId}", Codec, Module);
        if (Encoder is not null) await config.SetSettingAsync($"scrcpy-encoder-{deviceId}", Encoder, Module);
        if (Bitrate.HasValue) await config.SetSettingAsync($"scrcpy-bitrate-{deviceId}", Bitrate.Value.ToString(), Module);
        if (MaxFps.HasValue) await config.SetSettingAsync($"scrcpy-maxfps-{deviceId}", MaxFps.Value.ToString(), Module);
        if (MaxSize.HasValue) await config.SetSettingAsync($"scrcpy-maxsize-{deviceId}", MaxSize.Value.ToString(), Module);
        if (Audio.HasValue) await config.SetSettingAsync($"scrcpy-audio-{deviceId}", Audio.Value.ToString(), Module);
        if (AudioSource is not null) await config.SetSettingAsync($"scrcpy-audiosource-{deviceId}", AudioSource, Module);
        if (AudioCodec is not null) await config.SetSettingAsync($"scrcpy-audiocodec-{deviceId}", AudioCodec, Module);
    }

    public static async Task SaveProbeCacheAsync(IConfigurationService config, Guid deviceId, ScrcpyProbeResult probe)
    {
        await config.SetSettingAsync($"scrcpy-videoencoders-{deviceId}", string.Join(",", probe.VideoEncoders), Module);
        await config.SetSettingAsync($"scrcpy-audioencoders-{deviceId}", string.Join(",", probe.AudioEncoders), Module);
        await config.SetSettingAsync($"scrcpy-screendims-{deviceId}", $"{probe.Width}x{probe.Height}x{probe.Density}", Module);
    }

    public static async Task ClearAllAsync(IConfigurationService config, Guid deviceId)
    {
        string[] keys = [
            "scrcpy-codec", "scrcpy-encoder", "scrcpy-bitrate", "scrcpy-maxfps",
            "scrcpy-maxsize", "scrcpy-audio", "scrcpy-audiosource", "scrcpy-audiocodec",
            "scrcpy-videoencoders", "scrcpy-audioencoders", "scrcpy-screendims"
        ];
        foreach (var key in keys)
            await config.DeleteSettingAsync($"{key}-{deviceId}", Module);
    }
}
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build src/ControlMenu/`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Services/ScrcpyProbeResult.cs src/ControlMenu/Services/ScrcpySettings.cs
git commit -m "feat(a5): add ScrcpyProbeResult and ScrcpySettings records"
```

---

### Task 2: Create IScrcpyProbeService, FakeScrcpyProbeService, and ScrcpySettingsTests (TDD)

**Files:**
- Create: `src/ControlMenu/Services/IScrcpyProbeService.cs`
- Create: `tests/ControlMenu.Tests/Services/Fakes/FakeScrcpyProbeService.cs`
- Create: `tests/ControlMenu.Tests/Services/ScrcpySettingsTests.cs`

- [ ] **Step 1: Create IScrcpyProbeService interface**

Create `src/ControlMenu/Services/IScrcpyProbeService.cs`:

```csharp
namespace ControlMenu.Services;

public interface IScrcpyProbeService
{
    Task<ScrcpyProbeResult?> ProbeAsync(string udid, CancellationToken ct = default);
}
```

- [ ] **Step 2: Create FakeScrcpyProbeService**

Create `tests/ControlMenu.Tests/Services/Fakes/FakeScrcpyProbeService.cs`:

```csharp
using ControlMenu.Services;

namespace ControlMenu.Tests.Services.Fakes;

public sealed class FakeScrcpyProbeService : IScrcpyProbeService
{
    public ScrcpyProbeResult? Result { get; set; }

    public Task<ScrcpyProbeResult?> ProbeAsync(string udid, CancellationToken ct = default)
        => Task.FromResult(Result);
}
```

- [ ] **Step 3: Write ScrcpySettingsTests**

Create `tests/ControlMenu.Tests/Services/ScrcpySettingsTests.cs`:

```csharp
using ControlMenu.Services;

namespace ControlMenu.Tests.Services;

public class ScrcpySettingsTests
{
    private static ScrcpyProbeResult MakeProbe(
        int w = 1920, int h = 1080, int density = 480,
        string[]? videoEncoders = null, string[]? audioEncoders = null)
        => new(w, h, density,
            videoEncoders ?? ["OMX.qcom.video.encoder.avc", "c2.android.hevc.encoder"],
            audioEncoders ?? ["c2.android.opus.encoder", "c2.android.aac.encoder"]);

    [Fact]
    public void DeriveDefaults_WithH265Encoder_SelectsH265()
    {
        var probe = MakeProbe();
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal("h265", settings.Codec);
    }

    [Fact]
    public void DeriveDefaults_WithoutH265Encoder_SelectsH264()
    {
        var probe = MakeProbe(videoEncoders: ["OMX.qcom.video.encoder.avc"]);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal("h264", settings.Codec);
    }

    [Fact]
    public void DeriveDefaults_EncoderIsNull()
    {
        var probe = MakeProbe();
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Null(settings.Encoder);
    }

    [Fact]
    public void DeriveDefaults_BitrateScalesToResolution()
    {
        var probe = MakeProbe(w: 1920, h: 1080);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal(1920 * 1080 * 4, settings.Bitrate);
    }

    [Fact]
    public void DeriveDefaults_MaxFpsIs60()
    {
        var probe = MakeProbe();
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal(60, settings.MaxFps);
    }

    [Fact]
    public void DeriveDefaults_MaxSizeIsLargerDimension()
    {
        var probe = MakeProbe(w: 1080, h: 1920);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal(1920, settings.MaxSize);
    }

    [Fact]
    public void DeriveDefaults_AudioTrueWhenEncodersPresent()
    {
        var probe = MakeProbe(audioEncoders: ["c2.android.opus.encoder"]);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.True(settings.Audio);
    }

    [Fact]
    public void DeriveDefaults_AudioFalseWhenNoEncoders()
    {
        var probe = MakeProbe(audioEncoders: []);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.False(settings.Audio);
    }

    [Fact]
    public void DeriveDefaults_AudioCodecPrefersOpus()
    {
        var probe = MakeProbe(audioEncoders: ["c2.android.aac.encoder", "c2.android.opus.encoder"]);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal("opus", settings.AudioCodec);
    }

    [Fact]
    public void DeriveDefaults_AudioCodecFallsBackToFirst()
    {
        var probe = MakeProbe(audioEncoders: ["c2.android.aac.encoder"]);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal("c2.android.aac.encoder", settings.AudioCodec);
    }

    [Fact]
    public void DeriveDefaults_AudioSourceIsPlayback()
    {
        var probe = MakeProbe();
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal("playback", settings.AudioSource);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~ScrcpySettingsTests"`
Expected: 11 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Services/IScrcpyProbeService.cs tests/ControlMenu.Tests/Services/Fakes/FakeScrcpyProbeService.cs tests/ControlMenu.Tests/Services/ScrcpySettingsTests.cs
git commit -m "feat(a5): add IScrcpyProbeService interface, fake, and ScrcpySettings defaults tests"
```

---

### Task 3: Implement ScrcpyProbeService (TDD)

**Files:**
- Create: `src/ControlMenu/Services/ScrcpyProbeService.cs`
- Modify: `src/ControlMenu/Program.cs`

The probe service opens a `ClientWebSocket` to ws-scrcpy-web. Integration testing against a real WebSocket server is impractical in unit tests, so we test the deserialization and timeout behavior, and register the service.

- [ ] **Step 1: Create ScrcpyProbeService**

Create `src/ControlMenu/Services/ScrcpyProbeService.cs`:

```csharp
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ControlMenu.Services;

public sealed class ScrcpyProbeService : IScrcpyProbeService
{
    private readonly WsScrcpyService _wsScrcpy;
    private readonly ILogger<ScrcpyProbeService> _logger;

    public ScrcpyProbeService(WsScrcpyService wsScrcpy, ILogger<ScrcpyProbeService> logger)
    {
        _wsScrcpy = wsScrcpy;
        _logger = logger;
    }

    public async Task<ScrcpyProbeResult?> ProbeAsync(string udid, CancellationToken ct = default)
    {
        if (!_wsScrcpy.IsRunning) return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            using var ws = new ClientWebSocket();
            var baseUri = new Uri(_wsScrcpy.BaseUrl);
            var wsScheme = baseUri.Scheme == "https" ? "wss" : "ws";
            var probeUri = new Uri($"{wsScheme}://{baseUri.Host}:{baseUri.Port}/?action=PROBE_DEVICE&udid={Uri.EscapeDataString(udid)}");

            await ws.ConnectAsync(probeUri, cts.Token);

            var buffer = new byte[8192];
            var result = await ws.ReceiveAsync(buffer, cts.Token);

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                return JsonSerializer.Deserialize<ScrcpyProbeResult>(json);
            }

            return null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or JsonException or UriFormatException)
        {
            _logger.LogWarning(ex, "Probe failed for {Udid}", udid);
            return null;
        }
    }
}
```

- [ ] **Step 2: Register in Program.cs**

In `src/ControlMenu/Program.cs`, find:

```csharp
// Android Devices module services
builder.Services.AddSingleton<IAdbService, AdbService>();
```

Insert **before** that line:

```csharp
builder.Services.AddSingleton<IScrcpyProbeService, ScrcpyProbeService>();
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build src/ControlMenu/`
Expected: Build succeeded.

- [ ] **Step 4: Run full test suite**

Run: `dotnet test tests/ControlMenu.Tests/`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Services/ScrcpyProbeService.cs src/ControlMenu/Program.cs
git commit -m "feat(a5): add ScrcpyProbeService singleton + register in DI"
```

---

### Task 4: Enhance ScrcpyMirror with settings URL params, ReloadStream, and loading state

**Files:**
- Modify: `src/ControlMenu/Components/Shared/ScrcpyMirror.razor`

- [ ] **Step 1: Replace full contents of ScrcpyMirror.razor**

Replace the full contents of `src/ControlMenu/Components/Shared/ScrcpyMirror.razor` with:

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
    else if (Loading)
    {
        <div class="scrcpy-loading">
            <div class="spinner-border text-primary" role="status">
                <span class="visually-hidden">Connecting...</span>
            </div>
        </div>
    }
    else if (Inline)
    {
        <iframe @ref="_iframeRef" src="@StreamUrl" class="scrcpy-iframe" tabindex="0"
                allow="autoplay; fullscreen" @onload="FocusIframe" @key="_renderKey"></iframe>
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

    [Parameter]
    public string? DeviceKind { get; set; }

    [Parameter]
    public ScrcpySettings? Settings { get; set; }

    [Parameter]
    public bool Loading { get; set; }

    private int _renderKey;
    private ElementReference _iframeRef;

    private string StreamUrl
    {
        get
        {
            var url = $"{WsScrcpy.BaseUrl}/embed.html?device={Uri.EscapeDataString(Udid)}";
            if (!string.IsNullOrEmpty(DeviceKind))
                url += $"&deviceKind={Uri.EscapeDataString(DeviceKind)}";

            if (Settings is not null)
            {
                if (Settings.Codec is not null) url += $"&codec={Uri.EscapeDataString(Settings.Codec)}";
                if (Settings.Encoder is not null) url += $"&encoder={Uri.EscapeDataString(Settings.Encoder)}";
                if (Settings.Bitrate.HasValue) url += $"&bitrate={Settings.Bitrate.Value}";
                if (Settings.MaxFps.HasValue) url += $"&maxFps={Settings.MaxFps.Value}";
                if (Settings.MaxSize.HasValue) url += $"&maxSize={Settings.MaxSize.Value}";
                if (Settings.Audio.HasValue) url += $"&audio={Settings.Audio.Value.ToString().ToLowerInvariant()}";
                if (Settings.AudioSource is not null) url += $"&audioSource={Uri.EscapeDataString(Settings.AudioSource)}";
                if (Settings.AudioCodec is not null) url += $"&audioCodec={Uri.EscapeDataString(Settings.AudioCodec)}";
            }

            return url;
        }
    }

    public void ReloadStream()
    {
        _renderKey++;
        StateHasChanged();
    }

    private async Task FocusIframe()
    {
        await JS.InvokeVoidAsync("HTMLElement.prototype.focus.call", _iframeRef);
    }

    private async Task OpenMirrorWindow()
    {
        await JS.InvokeVoidAsync("open", StreamUrl, "_blank", "width=1080,height=600,menubar=no,toolbar=no,location=no,status=no");
    }
}
```

Key changes from original:
- Added `ScrcpySettings? Settings` parameter
- Added `bool Loading` parameter (shows spinner when true)
- `StreamUrl` appends all non-null settings as URL params
- Added `ReloadStream()` public method that increments `_renderKey` to force iframe re-render
- Added `@key="_renderKey"` on the iframe element

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/ControlMenu/`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/Components/Shared/ScrcpyMirror.razor
git commit -m "feat(a5): enhance ScrcpyMirror with settings URL params, ReloadStream, loading state"
```

---

### Task 5: Create ScrcpySettingsModal component

**Files:**
- Create: `src/ControlMenu/Components/Shared/ScrcpySettingsModal.razor`
- Create: `src/ControlMenu/Components/Shared/ScrcpySettingsModal.razor.css`

- [ ] **Step 1: Create the modal component**

Create `src/ControlMenu/Components/Shared/ScrcpySettingsModal.razor`:

```razor
@using ControlMenu.Services

<dialog @ref="_dialogRef" class="scrcpy-settings-dialog">
    <div class="modal-header">
        <span class="modal-title">@DeviceName</span>
        <button class="modal-close" @onclick="Close" title="Close">&times;</button>
    </div>

    <div class="modal-controls">
        @if (_probing)
        {
            <span class="label">status:</span>
            <span class="status-probing">probing...</span>
        }
        else
        {
            <span class="label">video codec:</span>
            <select class="input" @bind="_codec">
                @foreach (var c in _availableCodecs)
                {
                    <option value="@c">@c</option>
                }
            </select>

            <span class="label">encoder:</span>
            <select class="input" @bind="_encoder">
                <option value="">(auto)</option>
                @foreach (var e in FilteredEncoders)
                {
                    <option value="@e">@e</option>
                }
            </select>

            <span class="label">max fps (@_maxFps):</span>
            <input type="range" class="input" min="1" max="60" step="1" @bind="_maxFps" @bind:event="oninput" />

            <span class="label">audio codec:</span>
            <select class="input" disabled="@(!_audio)" @bind="_audioCodec">
                @foreach (var c in _availableAudioCodecs)
                {
                    <option value="@c">@c</option>
                }
            </select>

            <span class="label">audio source:</span>
            <select class="input" disabled="@(!_audio)" @bind="_audioSource">
                <option value="playback">playback — keeps device audio</option>
                <option value="output">output — silences device</option>
                <option value="mic">mic — captures microphone</option>
            </select>

            <span class="label">enable audio:</span>
            <input type="checkbox" class="input checkbox" @bind="_audio" />

            <span class="label">bitrate (@FormatBitrate(_bitrate)):</span>
            <input type="range" class="input" min="524288" max="8388608" step="524288" @bind="_bitrate" @bind:event="oninput" />
        }
    </div>

    <div class="modal-footer">
        @if (!string.IsNullOrEmpty(_notification))
        {
            <span class="notification-chip">@_notification</span>
        }
        <div class="modal-buttons">
            <button class="btn-modal" @onclick="OnSave" title="Save current settings">save</button>
            <button class="btn-modal" @onclick="OnClearRefresh" title="Clear saved settings and re-probe device capabilities">clear / refresh</button>
            <button class="btn-modal btn-connect" @onclick="OnConnect" title="Reconnect stream with current settings">connect</button>
        </div>
    </div>
</dialog>

@code {
    [Parameter, EditorRequired] public Guid DeviceId { get; set; }
    [Parameter, EditorRequired] public string DeviceName { get; set; } = "";
    [Parameter, EditorRequired] public string Udid { get; set; } = "";
    [Parameter] public EventCallback<ScrcpySettings> OnConnectStream { get; set; }

    [Inject] private IConfigurationService Config { get; set; } = default!;
    [Inject] private IScrcpyProbeService ProbeService { get; set; } = default!;

    private ElementReference _dialogRef;
    private bool _probing;
    private string? _notification;

    private string _codec = "h264";
    private string _encoder = "";
    private int _maxFps = 60;
    private int _bitrate = 8_000_000;
    private int _maxSize = 1920;
    private bool _audio = true;
    private string _audioSource = "playback";
    private string _audioCodec = "opus";

    private string[] _videoEncoders = [];
    private string[] _audioEncoders = [];
    private string[] _availableCodecs = ["h264", "h265", "av1"];
    private string[] _availableAudioCodecs = ["opus", "aac", "flac", "raw"];

    private IEnumerable<string> FilteredEncoders => _videoEncoders.Where(e =>
        _codec == "h265" ? (e.Contains("265") || e.Contains("hevc", StringComparison.OrdinalIgnoreCase)) :
        _codec == "h264" ? (e.Contains("264") || e.Contains("avc", StringComparison.OrdinalIgnoreCase)) :
        _codec == "av1" ? e.Contains("av1", StringComparison.OrdinalIgnoreCase) :
        true);

    public async Task OpenAsync()
    {
        _notification = null;
        await LoadFromDb();
        StateHasChanged();
        await Task.Yield();
        // dialog.showModal() via JS interop
        await InvokeJsShowModal();
    }

    private async Task LoadFromDb()
    {
        var settings = await ScrcpySettings.LoadAsync(Config, DeviceId);
        if (settings is not null)
        {
            _codec = settings.Codec ?? "h264";
            _encoder = settings.Encoder ?? "";
            _bitrate = settings.Bitrate ?? 8_000_000;
            _maxFps = settings.MaxFps ?? 60;
            _maxSize = settings.MaxSize ?? 1920;
            _audio = settings.Audio ?? true;
            _audioSource = settings.AudioSource ?? "playback";
            _audioCodec = settings.AudioCodec ?? "opus";
        }

        var encodersRaw = await Config.GetSettingAsync($"scrcpy-videoencoders-{DeviceId}", "android-devices");
        _videoEncoders = string.IsNullOrEmpty(encodersRaw) ? [] : encodersRaw.Split(',');

        var audioEncodersRaw = await Config.GetSettingAsync($"scrcpy-audioencoders-{DeviceId}", "android-devices");
        _audioEncoders = string.IsNullOrEmpty(audioEncodersRaw) ? [] : audioEncodersRaw.Split(',');

        if (_videoEncoders.Length == 0 && _audioEncoders.Length == 0)
            await RunProbe();
    }

    private async Task RunProbe()
    {
        _probing = true;
        StateHasChanged();

        var probe = await ProbeService.ProbeAsync(Udid);
        if (probe is not null)
        {
            _videoEncoders = probe.VideoEncoders;
            _audioEncoders = probe.AudioEncoders;

            var defaults = ScrcpySettings.DeriveDefaults(probe);
            _codec = defaults.Codec ?? "h264";
            _encoder = defaults.Encoder ?? "";
            _bitrate = defaults.Bitrate ?? 8_000_000;
            _maxFps = defaults.MaxFps ?? 60;
            _maxSize = defaults.MaxSize ?? 1920;
            _audio = defaults.Audio ?? true;
            _audioSource = defaults.AudioSource ?? "playback";
            _audioCodec = defaults.AudioCodec ?? "opus";

            await ScrcpySettings.SaveProbeCacheAsync(Config, DeviceId, probe);
        }

        _probing = false;
        StateHasChanged();
    }

    private ScrcpySettings CurrentSettings => new(
        Codec: _codec,
        Encoder: string.IsNullOrEmpty(_encoder) ? null : _encoder,
        Bitrate: _bitrate,
        MaxFps: _maxFps,
        MaxSize: _maxSize,
        Audio: _audio,
        AudioSource: _audioSource,
        AudioCodec: _audioCodec);

    private async Task OnSave()
    {
        await CurrentSettings.SaveAsync(Config, DeviceId);
        _notification = "settings saved";
        StateHasChanged();
    }

    private async Task OnClearRefresh()
    {
        await ScrcpySettings.ClearAllAsync(Config, DeviceId);
        await RunProbe();
        _notification = "defaults restored";
        StateHasChanged();
    }

    private async Task OnConnect()
    {
        await OnConnectStream.InvokeAsync(CurrentSettings);
        Close();
    }

    private void Close()
    {
        // dialog.close() via JS interop — will be wired in step 2
    }

    private static string FormatBitrate(int bps)
    {
        if (bps >= 1_000_000) return $"{bps / 1_000_000.0:F1} Mbps";
        return $"{bps / 1_000.0:F0} Kbps";
    }

    private Task InvokeJsShowModal() => Task.CompletedTask; // Placeholder — JS interop wired in step 2
}
```

- [ ] **Step 2: Wire up dialog JS interop**

The modal uses the native `<dialog>` element. Add JS interop for `showModal()` and `close()`. Update the two placeholder methods in the `@code` block:

Replace `InvokeJsShowModal()`:
```csharp
[Inject] private IJSRuntime JS { get; set; } = default!;

private Task InvokeJsShowModal() =>
    JS.InvokeVoidAsync("HTMLDialogElement.prototype.showModal.call", _dialogRef).AsTask();
```

Replace `Close()`:
```csharp
private async void Close()
{
    await JS.InvokeVoidAsync("HTMLDialogElement.prototype.close.call", _dialogRef);
}
```

Remove the `Task.CompletedTask` placeholder line. Ensure `[Inject] private IJSRuntime JS` is added to the inject block at the top of `@code`.

- [ ] **Step 3: Create scoped CSS**

Create `src/ControlMenu/Components/Shared/ScrcpySettingsModal.razor.css`:

```css
.scrcpy-settings-dialog {
    background: var(--bs-body-bg, #1a1a2e);
    color: var(--bs-body-color, #e0e0e0);
    border: 1px solid var(--bs-border-color, #333);
    border-radius: 8px;
    padding: 0;
    min-width: 500px;
    max-width: 700px;
}

.scrcpy-settings-dialog::backdrop {
    background: rgba(0, 0, 0, 0.5);
}

.modal-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 12px 16px;
    border-bottom: 1px solid var(--bs-border-color, #333);
}

.modal-title {
    font-weight: bold;
    font-size: 1.1rem;
}

.modal-close {
    background: none;
    border: none;
    color: var(--bs-body-color, #e0e0e0);
    font-size: 1.4rem;
    cursor: pointer;
    padding: 0 4px;
    line-height: 1;
}

.modal-controls {
    display: grid;
    grid-template-columns: 35% 1fr;
    gap: 0.5rem 0.75rem;
    align-items: center;
    padding: 16px;
}

.label {
    font-size: 13px;
    color: #aaa;
    text-align: right;
    padding-right: 8px;
}

.input {
    background: var(--bs-tertiary-bg, #1a1a2e);
    color: var(--bs-body-color, #e0e0e0);
    border: 1px solid var(--bs-border-color, #444);
    border-radius: 6px;
    padding: 4px 8px;
    font-size: 13px;
    width: 100%;
}

.input:disabled {
    opacity: 0.4;
}

.checkbox {
    width: 16px;
    height: 16px;
    justify-self: start;
}

input[type="range"] {
    accent-color: #5b9aff;
    padding: 0;
    border: none;
    background: transparent;
}

.status-probing {
    color: #f06c75;
    font-size: 13px;
}

.modal-footer {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 8px;
    padding: 12px 16px;
    border-top: 1px solid var(--bs-border-color, #333);
}

.notification-chip {
    font-size: 12px;
    color: #4ade80;
    padding: 2px 10px;
    border: 1px solid #4ade80;
    border-radius: 12px;
}

.modal-buttons {
    display: flex;
    gap: 8px;
    justify-content: center;
}

.btn-modal {
    background: transparent;
    color: var(--bs-body-color, #e0e0e0);
    border: 1px solid var(--bs-border-color, #444);
    border-radius: 6px;
    padding: 6px 16px;
    font-size: 13px;
    cursor: pointer;
}

.btn-modal:hover {
    background: var(--bs-tertiary-bg, #2a2a3a);
}

.btn-connect {
    color: #5b9aff;
    border-color: #5b9aff;
}

.btn-connect:hover {
    background: rgba(91, 154, 255, 0.1);
}
```

- [ ] **Step 4: Verify it compiles**

Run: `dotnet build src/ControlMenu/`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Components/Shared/ScrcpySettingsModal.razor src/ControlMenu/Components/Shared/ScrcpySettingsModal.razor.css
git commit -m "feat(a5): add ScrcpySettingsModal component with 2-column grid layout"
```

---

### Task 6: Integrate into GoogleTvDashboard (first dashboard, template for others)

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor`

This is the template integration — Tasks 7-9 replicate this pattern for the other 3 dashboards.

- [ ] **Step 1: Add service injections**

In the `@code` block of `GoogleTvDashboard.razor`, find the existing inject block and add:

```csharp
[Inject] private IConfigurationService Config { get; set; } = default!;
[Inject] private IScrcpyProbeService ProbeService { get; set; } = default!;
```

Note: `IConfigurationService` may already be injected in GoogleTvDashboard for PIN retrieval. If so, only add `IScrcpyProbeService`. Check first.

- [ ] **Step 2: Add scrcpy settings fields**

Add these fields to the `@code` block, alongside the existing private fields:

```csharp
private ScrcpySettings? _scrcpySettings;
private bool _mirrorLoading = true;
private ScrcpySettingsModal? _settingsModal;
```

- [ ] **Step 3: Add first-visit probe logic to OnInitializedAsync**

Find the section in `OnInitializedAsync` where `_connected` is set (after `AdbService.ConnectAsync`). After the connection block, add the first-visit probe logic:

```csharp
// Scrcpy settings: load from DB or probe on first visit
_scrcpySettings = await ScrcpySettings.LoadAsync(Config, _device.Id);
if (_scrcpySettings is not null)
{
    _mirrorLoading = false;
}
else
{
    _ = ProbeAndStartAsync();
}
```

Then add the probe helper method to the `@code` block:

```csharp
private async Task ProbeAndStartAsync()
{
    var udid = $"{Ip}:{Port}";
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    var probe = await ProbeService.ProbeAsync(udid, cts.Token);

    if (probe is not null)
    {
        _scrcpySettings = ScrcpySettings.DeriveDefaults(probe);
        await _scrcpySettings.SaveAsync(Config, _device!.Id);
        await ScrcpySettings.SaveProbeCacheAsync(Config, _device.Id, probe);
    }

    _mirrorLoading = false;
    await InvokeAsync(StateHasChanged);
}
```

- [ ] **Step 4: Add Stream Settings row to quick-actions**

In the HTML markup, find the last action row in the controls-panel (before the closing `</div>` of controls-panel). Add the Stream Settings row:

```razor
<!-- Stream Settings -->
<div class="action-row">
    <div class="action-label">
        <i class="bi bi-gear"></i> Stream Settings
    </div>
    <span class="action-status">Video and audio configuration</span>
    <div class="action-buttons">
        <button class="btn btn-sm btn-secondary" @onclick="OpenStreamSettings">
            <i class="bi bi-sliders"></i> Configure
        </button>
    </div>
</div>
```

- [ ] **Step 5: Update ScrcpyMirror usage to pass Settings and Loading**

Find the existing `<ScrcpyMirror>` tag and update it:

```razor
<ScrcpyMirror @ref="_mirror" Udid="@($"{Ip}:{Port}")" Inline="true" DeviceKind="tv"
              Settings="@_scrcpySettings" Loading="@_mirrorLoading" />
```

Add the `_mirror` field to `@code`:

```csharp
private ScrcpyMirror? _mirror;
```

- [ ] **Step 6: Add modal component and handler methods**

Add the modal component just before the closing `}` of the else block (after the dashboard-layout div):

```razor
<ScrcpySettingsModal @ref="_settingsModal"
    DeviceId="@_device.Id"
    DeviceName="@_device.Name"
    Udid="@($"{Ip}:{Port}")"
    OnConnectStream="OnConnectStream" />
```

Add handler methods to `@code`:

```csharp
private async Task OpenStreamSettings()
{
    if (_settingsModal is not null)
        await _settingsModal.OpenAsync();
}

private async Task OnConnectStream(ScrcpySettings settings)
{
    _scrcpySettings = settings;
    _mirror?.ReloadStream();
    await InvokeAsync(StateHasChanged);
}
```

- [ ] **Step 7: Verify it compiles**

Run: `dotnet build src/ControlMenu/`
Expected: Build succeeded.

- [ ] **Step 8: Run full test suite**

Run: `dotnet test tests/ControlMenu.Tests/`
Expected: All tests pass.

- [ ] **Step 9: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor
git commit -m "feat(a5): integrate scrcpy settings modal into GoogleTvDashboard"
```

---

### Task 7: Integrate into PixelDashboard

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor`

Apply the same pattern as Task 6. PixelDashboard already has `IConfigurationService` injected (for PIN retrieval).

- [ ] **Step 1: Add IScrcpyProbeService injection**

```csharp
[Inject] private IScrcpyProbeService ProbeService { get; set; } = default!;
```

- [ ] **Step 2: Add scrcpy settings fields**

```csharp
private ScrcpySettings? _scrcpySettings;
private bool _mirrorLoading = true;
private ScrcpySettingsModal? _settingsModal;
private ScrcpyMirror? _mirror;
```

- [ ] **Step 3: Add first-visit probe logic to OnInitializedAsync**

After the connection block (after `_connected = await AdbService.ConnectAsync(...)` and the screen-size query), add:

```csharp
_scrcpySettings = await ScrcpySettings.LoadAsync(Config, _device.Id);
if (_scrcpySettings is not null)
{
    _mirrorLoading = false;
}
else
{
    _ = ProbeAndStartAsync();
}
```

Add the `ProbeAndStartAsync` method (same as Task 6):

```csharp
private async Task ProbeAndStartAsync()
{
    var udid = $"{Ip}:{Port}";
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    var probe = await ProbeService.ProbeAsync(udid, cts.Token);

    if (probe is not null)
    {
        _scrcpySettings = ScrcpySettings.DeriveDefaults(probe);
        await _scrcpySettings.SaveAsync(Config, _device!.Id);
        await ScrcpySettings.SaveProbeCacheAsync(Config, _device.Id, probe);
    }

    _mirrorLoading = false;
    await InvokeAsync(StateHasChanged);
}
```

- [ ] **Step 4: Add Stream Settings row to quick-actions**

After the last action row (Unlock Phone), add:

```razor
<!-- Stream Settings -->
<div class="action-row">
    <div class="action-label">
        <i class="bi bi-gear"></i> Stream Settings
    </div>
    <span class="action-status">Video and audio configuration</span>
    <div class="action-buttons">
        <button class="btn btn-sm btn-secondary" @onclick="OpenStreamSettings">
            <i class="bi bi-sliders"></i> Configure
        </button>
    </div>
</div>
```

- [ ] **Step 5: Update ScrcpyMirror tag**

```razor
<ScrcpyMirror @ref="_mirror" Udid="@($"{Ip}:{Port}")" Inline="true" DeviceKind="phone"
              Settings="@_scrcpySettings" Loading="@_mirrorLoading" />
```

- [ ] **Step 6: Add modal component and handlers**

After the dashboard-layout div:

```razor
<ScrcpySettingsModal @ref="_settingsModal"
    DeviceId="@_device.Id"
    DeviceName="@_device.Name"
    Udid="@($"{Ip}:{Port}")"
    OnConnectStream="OnConnectStream" />
```

Handler methods:

```csharp
private async Task OpenStreamSettings()
{
    if (_settingsModal is not null)
        await _settingsModal.OpenAsync();
}

private async Task OnConnectStream(ScrcpySettings settings)
{
    _scrcpySettings = settings;
    _mirror?.ReloadStream();
    await InvokeAsync(StateHasChanged);
}
```

- [ ] **Step 7: Verify it compiles and tests pass**

Run: `dotnet build src/ControlMenu/ && dotnet test tests/ControlMenu.Tests/`
Expected: Build succeeded. All tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor
git commit -m "feat(a5): integrate scrcpy settings modal into PixelDashboard"
```

---

### Task 8: Integrate into TabletDashboard

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/TabletDashboard.razor`

Same pattern as Task 7. TabletDashboard is a clone of PixelDashboard with `DeviceType.AndroidTablet` and `DeviceKind="tablet"`.

- [ ] **Step 1: Add IScrcpyProbeService and IConfigurationService injections** (add IConfigurationService if not already present)

- [ ] **Step 2: Add scrcpy settings fields** (same 4 fields as Task 7)

- [ ] **Step 3: Add first-visit probe logic to OnInitializedAsync** (same pattern, after connection block)

- [ ] **Step 4: Add Stream Settings row** (same HTML as Task 7)

- [ ] **Step 5: Update ScrcpyMirror tag** (`DeviceKind="tablet"`)

```razor
<ScrcpyMirror @ref="_mirror" Udid="@($"{Ip}:{Port}")" Inline="true" DeviceKind="tablet"
              Settings="@_scrcpySettings" Loading="@_mirrorLoading" />
```

- [ ] **Step 6: Add modal component and handlers** (same as Task 7)

- [ ] **Step 7: Verify it compiles and tests pass**

Run: `dotnet build src/ControlMenu/ && dotnet test tests/ControlMenu.Tests/`
Expected: Build succeeded. All tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Pages/TabletDashboard.razor
git commit -m "feat(a5): integrate scrcpy settings modal into TabletDashboard"
```

---

### Task 9: Integrate into WatchDashboard

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/WatchDashboard.razor`

Same pattern as Task 8. WatchDashboard uses `DeviceType.AndroidWatch` and `DeviceKind="watch"` (note: ws-scrcpy-web treats unknown deviceKind values as phone-like, which is correct for watch).

- [ ] **Step 1-6: Same pattern as Task 8** — injections, fields, probe logic, Stream Settings row, ScrcpyMirror update, modal + handlers.

DeviceKind for the mirror:

```razor
<ScrcpyMirror @ref="_mirror" Udid="@($"{Ip}:{Port}")" Inline="true" DeviceKind="phone"
              Settings="@_scrcpySettings" Loading="@_mirrorLoading" />
```

(Watch uses `DeviceKind="phone"` — ws-scrcpy-web defaults to touch mode, which is correct for Wear OS.)

- [ ] **Step 7: Verify it compiles and tests pass**

Run: `dotnet build src/ControlMenu/ && dotnet test tests/ControlMenu.Tests/`
Expected: Build succeeded. All tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Pages/WatchDashboard.razor
git commit -m "feat(a5): integrate scrcpy settings modal into WatchDashboard"
```

---

### Task 10: Manual smoke test + CHANGELOG

**Files:** None new (manual verification + CHANGELOG update)

- [ ] **Step 1: Start the app**

Run: `dotnet run --project src/ControlMenu/ControlMenu.csproj -c Release`
Expected: App starts on http://localhost:5159

- [ ] **Step 2: Test first-visit probe flow**

Navigate to a device dashboard (e.g., `/android/googletv`). Ensure ws-scrcpy-web is running.

Expected: Brief spinner (up to 3s), then stream connects with probed settings. No double-connect.

- [ ] **Step 3: Test modal — open and inspect defaults**

Click "Configure" in the Stream Settings quick-action row.

Expected: Modal opens with device name in header. Dropdowns populated from probe (video codecs, encoders, audio codecs). Slider values show derived defaults (60fps, ~8Mbps, native resolution).

- [ ] **Step 4: Test modal — Save**

Change bitrate to 4Mbps. Click Save.

Expected: "settings saved" chip appears. Modal stays open. Stream unchanged.

- [ ] **Step 5: Test modal — Connect**

Click Connect.

Expected: Modal closes. Stream reloads with updated settings (check embed URL in browser dev tools — should include `&bitrate=4000000`).

- [ ] **Step 6: Test subsequent visit**

Navigate away and back to the same dashboard.

Expected: Stream starts immediately with saved 4Mbps setting. No spinner (settings loaded from DB).

- [ ] **Step 7: Test Clear/Refresh**

Open modal. Click "clear / refresh".

Expected: "defaults restored" chip. Dropdowns repopulated from fresh probe. Settings reverted to defaults (8Mbps). Modal stays open.

- [ ] **Step 8: Test audio toggle**

Uncheck "enable audio". Verify audio codec and audio source dropdowns are grayed out. Click Save. Click Connect.

Expected: Stream restarts with `&audio=false` in URL. Audio disabled.

- [ ] **Step 9: Commit CHANGELOG update**

Add an entry to `CHANGELOG.md` under `[Unreleased]` > `Added`:

```markdown
- **`feat(a5)`** Per-device scrcpy stream settings modal on all device dashboards. Gear icon in quick-actions opens a settings modal (video codec, encoder, bitrate, max FPS, max resolution, audio toggle, audio source, audio codec) with probe-driven smart defaults. Settings persist in the database and are passed to ws-scrcpy-web as URL params. First visit probes the device automatically with a 3-second grace period before connecting. Three modal buttons: Save (persists without reconnecting), Connect (applies settings and reconnects), Clear/Refresh (re-probes device for updated capabilities).
```

```bash
git add CHANGELOG.md
git commit -m "docs(changelog): add A5 scrcpy settings modal"
```
