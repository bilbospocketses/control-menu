# Wizard Camera Scanner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the WizardCameras stub with a full first-run camera-discovery flow that mirrors the WizardDevices shape: one-click `Scan Network` → auto-detected subnet → ONVIF-only WS-Discovery → DiscoveredCamerasPanel inline-Add → registered table.

**Architecture:** Single Blazor component (`WizardCameras.razor`) modeled on `WizardDevices.razor`, reusing the existing `DiscoveredCamerasPanel`, `ScanProgressChip`, `SubnetDetectionClient`, and `CameraService`. Backend gains one new ONVIF-only entry point on `ICameraScanService` by factoring the existing scan body into a private helper that takes an `includeRtspSweep` flag. WizardState gains a `CamerasAdded` field; WizardDone surfaces it; WizardDevices gets an intro-copy update calling out the mDNS-only limitation.

**Tech Stack:** .NET 9 / Blazor Server / EF Core 9 / xUnit + Moq. Spec at `docs/superpowers/specs/2026-05-05-wizard-camera-scanner-design.md`.

---

## File Structure

**Created:**
- _none_ (the plan extends existing files only)

**Modified:**
- `src/ControlMenu/Modules/Cameras/Network/ICameraScanService.cs` — add `StartOnvifOnlyScanAsync` method to the interface
- `src/ControlMenu/Modules/Cameras/Network/CameraScanService.cs` — refactor `StartScanAsync` body into private `RunScanAsync(subnets, includeRtspSweep, ct)`; add `StartOnvifOnlyScanAsync` public wrapper
- `src/ControlMenu/Components/Pages/SetupWizard.razor` — add `int CamerasAdded { get; set; }` to `WizardState`
- `src/ControlMenu/Components/Pages/Setup/WizardCameras.razor` — replace 12-line stub with full implementation
- `src/ControlMenu/Components/Pages/Setup/WizardDevices.razor` — update intro text (mDNS-only callout)
- `src/ControlMenu/Components/Pages/Setup/WizardDone.razor` — add Cameras summary row
- `tests/ControlMenu.Tests/Modules/Cameras/Network/CameraScanServiceTests.cs` — add ONVIF-only path test
- `CHANGELOG.md` — `[Unreleased]` Added entry

**No automated Razor component tests** — the project has no bUnit setup. Razor components are validated via successful build + manual smoke per the spec's testing section.

---

### Task 1: Add `StartOnvifOnlyScanAsync` to the scan service interface

**Files:**
- Modify: `src/ControlMenu/Modules/Cameras/Network/ICameraScanService.cs`

- [ ] **Step 1.1: Add the new method to the interface**

Open `src/ControlMenu/Modules/Cameras/Network/ICameraScanService.cs`. The current interface body looks like this:

```csharp
public interface ICameraScanService
{
    ScanPhase Phase { get; }
    IReadOnlyList<CameraScanHit> Hits { get; }
    IDisposable Subscribe(Action<CameraScanEvent> onEvent);
    Task StartScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default);
    Task CancelAsync(CancellationToken ct = default);
}
```

Add the new method directly below `StartScanAsync`:

```csharp
public interface ICameraScanService
{
    ScanPhase Phase { get; }
    IReadOnlyList<CameraScanHit> Hits { get; }
    IDisposable Subscribe(Action<CameraScanEvent> onEvent);
    Task StartScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default);
    /// <summary>
    /// Runs ONVIF WS-Discovery only against the supplied subnets, skipping the
    /// TCP-554 RTSP sweep. Same Phase transitions, event bus, and Hits accumulation
    /// as <see cref="StartScanAsync"/>. Used by the Setup Wizard's Cameras step
    /// where the parallel to WizardDevices is mDNS-only quick discovery.
    /// </summary>
    Task StartOnvifOnlyScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default);
    Task CancelAsync(CancellationToken ct = default);
}
```

- [ ] **Step 1.2: Build to verify the interface change compiles (will fail until Task 2 lands the implementation)**

Run: `cd C:/Users/jscha/source/repos/control-menu && dotnet build -c Release --nologo 2>&1 | tail -10`

Expected: ONE build error: `'CameraScanService' does not implement interface member 'ICameraScanService.StartOnvifOnlyScanAsync(...)'`. This confirms the interface contract is in place; Task 2 satisfies it.

- [ ] **Step 1.3: Commit**

```bash
cd C:/Users/jscha/source/repos/control-menu
git add src/ControlMenu/Modules/Cameras/Network/ICameraScanService.cs
git commit -m "feat(cameras): ICameraScanService.StartOnvifOnlyScanAsync signature"
```

---

### Task 2: Add the failing ONVIF-only test, then implement

**Files:**
- Modify: `tests/ControlMenu.Tests/Modules/Cameras/Network/CameraScanServiceTests.cs` (extend existing file)
- Modify: `src/ControlMenu/Modules/Cameras/Network/CameraScanService.cs`

- [ ] **Step 2.1: Inspect the existing test file to confirm the mock-construction pattern**

Run: `cd C:/Users/jscha/source/repos/control-menu && grep -n "new CameraScanService\|Mock<I" tests/ControlMenu.Tests/Modules/Cameras/Network/CameraScanServiceTests.cs | head -20`

Note the constructor arg order and which collaborators are mocked. Reuse the same pattern in Step 2.2 — do NOT invent a new test fixture style. If the existing tests use a `BuildSut()` helper, use it.

- [ ] **Step 2.2: Add the failing test**

Append to `tests/ControlMenu.Tests/Modules/Cameras/Network/CameraScanServiceTests.cs` (inside the existing `CameraScanServiceTests` class):

```csharp
[Fact]
public async Task StartOnvifOnlyScanAsync_DoesNotInvokeRtspProbe()
{
    // Arrange: ONVIF probe returns no hits (we don't care about hit handling here,
    // just that the TCP-554 branch never fires).
    _onvif.Setup(o => o.ProbeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<OnvifProbeResponse>());
    _networkDiscovery.Setup(n => n.GetArpTableAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<ArpEntry>());

    var sut = BuildSut();
    var subnet = SubnetParser.Parse("192.168.86.0/24").Value!;

    // Act
    await sut.StartOnvifOnlyScanAsync(new[] { subnet });

    // Assert
    _onvif.Verify(o => o.ProbeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    _rtsp.Verify(
        r => r.ProbeTcpAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
        Times.Never);
    Assert.Equal(ScanPhase.Complete, sut.Phase);
}
```

If the existing test file uses different mock-field names (e.g., `_onvifMock` instead of `_onvif`) or a different SUT-construction helper, adapt this snippet to match Step 2.1's findings — keep the assertions identical.

- [ ] **Step 2.3: Run the test to verify it fails to compile**

Run: `cd C:/Users/jscha/source/repos/control-menu && dotnet test -c Release --nologo --filter "FullyQualifiedName~StartOnvifOnlyScanAsync_DoesNotInvokeRtspProbe" 2>&1 | tail -15`

Expected: build error referencing `StartOnvifOnlyScanAsync` not implemented on `CameraScanService` (carry-over from Task 1 plus the new test reference).

- [ ] **Step 2.4: Refactor `StartScanAsync` body into a private helper with an `includeRtspSweep` flag, and add the public `StartOnvifOnlyScanAsync` wrapper**

Open `src/ControlMenu/Modules/Cameras/Network/CameraScanService.cs`. Find the `StartScanAsync` method (currently lines 56–96). Replace it with this two-method shape — the existing public `StartScanAsync` becomes a thin wrapper that delegates to a new private `RunScanAsync` with `includeRtspSweep: true`; add a sibling `StartOnvifOnlyScanAsync` that calls the same helper with `includeRtspSweep: false`:

```csharp
public Task StartScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default) =>
    RunScanAsync(subnets, includeRtspSweep: true, ct);

public Task StartOnvifOnlyScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default) =>
    RunScanAsync(subnets, includeRtspSweep: false, ct);

private async Task RunScanAsync(
    IReadOnlyList<ParsedSubnet> subnets,
    bool includeRtspSweep,
    CancellationToken ct)
{
    if (Phase == ScanPhase.Scanning) return;
    Phase = ScanPhase.Scanning;
    lock (_hitsLock) _hits.Clear();
    _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    var sw = Stopwatch.StartNew();

    Emit(new CameraScanStartedEvent(subnets.Select(s => s.Normalized).ToList()));

    var arpEntries = await _networkDiscovery.GetArpTableAsync(_cts.Token);
    _arpByIp = arpEntries
        .GroupBy(e => e.IpAddress)
        .ToDictionary(g => g.Key, g => g.First().MacAddress, StringComparer.OrdinalIgnoreCase);

    using var scope = _scopeFactory.CreateScope();
    var cameraService = scope.ServiceProvider.GetRequiredService<ICameraService>();
    var existingByIp = (await cameraService.GetAllAsync())
        .ToDictionary(c => c.IpAddress, c => c.Id);
    var seenIps = new ConcurrentDictionary<string, byte>();

    var onvifTask = RunOnvifBranchAsync(subnets, existingByIp, seenIps, cameraService, _cts.Token);
    var tcpTask = includeRtspSweep
        ? RunTcpSweepAsync(subnets, existingByIp, seenIps, cameraService, _cts.Token)
        : Task.CompletedTask;

    await Task.WhenAll(onvifTask, tcpTask);

    sw.Stop();
    if (_cts.Token.IsCancellationRequested)
    {
        Phase = ScanPhase.Idle;
        Emit(new CameraScanCancelledEvent());
        return;
    }

    Phase = ScanPhase.Complete;
    var onvifCount = Hits.Count(h => h.IsOnvif);
    var rtspCount = Hits.Count(h => !h.IsOnvif);
    _logger.LogInformation(
        "Camera scan ({Mode}): {Subnets} subnet(s), {OnvifHits} ONVIF, {RtspHits} RTSP-only, {Duration}",
        includeRtspSweep ? "full" : "onvif-only",
        subnets.Count, onvifCount, rtspCount, sw.Elapsed);
    Emit(new CameraScanCompletedEvent(onvifCount, rtspCount, sw.Elapsed));
}
```

Leave the `RunOnvifBranchAsync`, `RunTcpSweepAsync`, `EmitHitOrBumpAsync`, `Emit`, `IsInAnySubnet`, `SubnetContains`, `EnumerateAddresses`, and `Subscription` members exactly as they are. Only the public surface and the new private helper change.

- [ ] **Step 2.5: Run the new test to verify it passes**

Run: `cd C:/Users/jscha/source/repos/control-menu && dotnet test -c Release --nologo --filter "FullyQualifiedName~StartOnvifOnlyScanAsync_DoesNotInvokeRtspProbe" 2>&1 | tail -5`

Expected: `Passed!  - Failed: 0, Passed: 1`.

- [ ] **Step 2.6: Run the full test suite to confirm no regression**

Run: `cd C:/Users/jscha/source/repos/control-menu && dotnet test -c Release --nologo 2>&1 | tail -3`

Expected: `Passed!  - Failed: 0, Passed: 339, Skipped: 0, Total: 339` (one more than the prior 338 baseline). Build must be clean.

- [ ] **Step 2.7: Commit**

```bash
cd C:/Users/jscha/source/repos/control-menu
git add src/ControlMenu/Modules/Cameras/Network/CameraScanService.cs tests/ControlMenu.Tests/Modules/Cameras/Network/CameraScanServiceTests.cs
git commit -m "feat(cameras): StartOnvifOnlyScanAsync — ONVIF-only scan path

Factors the existing StartScanAsync body into a private RunScanAsync helper
with an includeRtspSweep flag. Public StartScanAsync delegates with the flag
true (preserves all existing behavior); new StartOnvifOnlyScanAsync delegates
with the flag false, skipping the TCP-554 sweep entirely. Added test verifies
IRtspProbeClient.ProbeTcpAsync is never invoked on the ONVIF-only path."
```

---

### Task 3: Add `CamerasAdded` to `WizardState`

**Files:**
- Modify: `src/ControlMenu/Components/Pages/SetupWizard.razor` (lines 73–82, the `WizardState` class block)

- [ ] **Step 3.1: Add the new property**

Open `src/ControlMenu/Components/Pages/SetupWizard.razor`. Find the `WizardState` class:

```csharp
public class WizardState
{
    public int DevicesAdded { get; set; }
    public bool JellyfinConfigured { get; set; }
    public bool SmtpConfigured { get; set; }
    public int DependenciesFound { get; set; }
    public int DependenciesTotal { get; set; }
}
```

Replace with:

```csharp
public class WizardState
{
    public int DevicesAdded { get; set; }
    public int CamerasAdded { get; set; }
    public bool JellyfinConfigured { get; set; }
    public bool SmtpConfigured { get; set; }
    public int DependenciesFound { get; set; }
    public int DependenciesTotal { get; set; }
}
```

- [ ] **Step 3.2: Build to verify (no consumers reference `CamerasAdded` yet, so no compile errors expected)**

Run: `cd C:/Users/jscha/source/repos/control-menu && dotnet build -c Release --nologo 2>&1 | tail -5`

Expected: `Build succeeded`, 0 errors.

- [ ] **Step 3.3: Commit**

```bash
cd C:/Users/jscha/source/repos/control-menu
git add src/ControlMenu/Components/Pages/SetupWizard.razor
git commit -m "feat(wizard): WizardState.CamerasAdded for Done-step summary"
```

---

### Task 4: Update WizardDone to surface the camera count

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Setup/WizardDone.razor`

- [ ] **Step 4.1: Add the Cameras summary row directly after the Devices row**

Open `src/ControlMenu/Components/Pages/Setup/WizardDone.razor`. Find the `<!-- Devices -->` block (lines 12–24). After the closing `</div>` of that summary-item, insert a new Cameras block:

```razor
        <!-- Cameras -->
        <div class="summary-item">
            @if (State.CamerasAdded > 0)
            {
                <i class="bi bi-check-circle-fill text-ok summary-icon"></i>
                <span>@State.CamerasAdded camera(s) registered</span>
            }
            else
            {
                <i class="bi bi-dash-circle summary-icon text-neutral"></i>
                <span>No cameras registered — <a href="/settings/cameras">add in Settings</a></span>
            }
        </div>
```

Use `bi-dash-circle` + `text-neutral` (not `bi-exclamation-triangle-fill text-warning`) for the empty state — cameras are optional in a way devices are not, and the neutral icon matches the Jellyfin/Email empty-state pattern in the same file.

- [ ] **Step 4.2: Build to verify**

Run: `cd C:/Users/jscha/source/repos/control-menu && dotnet build -c Release --nologo 2>&1 | tail -5`

Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4.3: Commit**

```bash
cd C:/Users/jscha/source/repos/control-menu
git add src/ControlMenu/Components/Pages/Setup/WizardDone.razor
git commit -m "feat(wizard): WizardDone shows camera count in summary"
```

---

### Task 5: Update WizardDevices intro to call out mDNS-only limitation

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Setup/WizardDevices.razor` (line 17)

- [ ] **Step 5.1: Replace the intro paragraph**

Open `src/ControlMenu/Components/Pages/Setup/WizardDevices.razor`. Find line 17:

```razor
    <p>Scan your network to discover Android devices, then add each one with a click. You can also add devices later from <a href="/settings/devices">Settings › Devices</a>.</p>
```

Replace with:

```razor
    <p>Scan your network to discover Android devices, then add each one with a click. <strong>Only modern Android devices that advertise over mDNS will appear in this scan;</strong> older devices can be added later from <a href="/settings/devices">Settings › Android Devices</a>.</p>
```

- [ ] **Step 5.2: Build to verify**

Run: `cd C:/Users/jscha/source/repos/control-menu && dotnet build -c Release --nologo 2>&1 | tail -5`

Expected: `Build succeeded`, 0 errors.

- [ ] **Step 5.3: Commit**

```bash
cd C:/Users/jscha/source/repos/control-menu
git add src/ControlMenu/Components/Pages/Setup/WizardDevices.razor
git commit -m "docs(wizard): WizardDevices intro calls out mDNS-only limitation"
```

---

### Task 6: Replace WizardCameras stub with full implementation

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Setup/WizardCameras.razor` (full rewrite)

- [ ] **Step 6.1: Replace the entire file contents**

Open `src/ControlMenu/Components/Pages/Setup/WizardCameras.razor`. The current file is a 12-line stub. Replace the whole file with:

```razor
@using ControlMenu.Components.Shared.Cameras
@using ControlMenu.Modules.Cameras.Entities
@using ControlMenu.Modules.Cameras.Network
@using ControlMenu.Modules.Cameras.Services
@using ControlMenu.Services.Network
@inject ICameraService CameraService
@inject ICameraScanService ScanService
@inject ICameraChangeNotifier ChangeNotifier
@inject SubnetDetectionClient SubnetDetector
@implements IDisposable

<div class="settings-section">
    <h2>Cameras</h2>
    <p>Scan your network to discover ONVIF-enabled IP cameras and add each one with a click. <strong>Non-ONVIF cameras (older or basic RTSP-only models) can be added later from <a href="/settings/cameras">Settings › Cameras</a>.</strong></p>

    <div class="toolbar" style="margin-bottom:1rem;">
        <button class="btn btn-primary" @onclick="StartScan" disabled="@_scanInProgress">
            <i class="bi bi-broadcast"></i> @(_scanInProgress ? "Scanning..." : "Scan Network")
        </button>
    </div>

    @if (ScanService.Phase is not ScanPhase.Idle)
    {
        <div class="scan-row" style="margin-bottom:1rem;">
            <span style="color: var(--text-muted);">@PhaseLabel</span>
        </div>
    }

    <DiscoveredCamerasPanel OnCameraAdded="@RefreshCamerasAsync" />

    @if (_cameras.Count > 0)
    {
        <h3 style="margin-top: 1.5rem;">Registered cameras</h3>
        <table class="data-table">
            <thead>
                <tr>
                    <th>CAM #</th>
                    <th>NAME</th>
                    <th>MFR / MODEL</th>
                    <th>ADDRESS</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var cam in _cameras)
                {
                    <tr>
                        <td>@(cam.CameraNumber?.ToString() ?? "—")</td>
                        <td>@cam.Name</td>
                        <td>@(cam.Manufacturer ?? "—") @(cam.Model ?? "")</td>
                        <td>@cam.IpAddress:@cam.Port</td>
                    </tr>
                }
            </tbody>
        </table>
    }

    @if (!string.IsNullOrEmpty(_message))
    {
        <div class="alert @(_messageIsError ? "alert-danger" : "alert-success")" style="margin-top:1rem;">
            @_message
        </div>
    }
</div>

@code {
    [Parameter] public SetupWizard.WizardState State { get; set; } = default!;

    private List<Camera> _cameras = new();
    private bool _scanInProgress;
    private string? _message;
    private bool _messageIsError;
    private IDisposable? _scanSubscription;

    private string PhaseLabel => ScanService.Phase switch
    {
        ScanPhase.Scanning => "Scanning network for ONVIF cameras…",
        ScanPhase.Draining => "Finishing up…",
        ScanPhase.Complete => "Scan complete.",
        _ => ""
    };

    protected override async Task OnInitializedAsync()
    {
        ChangeNotifier.CamerasChanged += OnCamerasChanged;
        _scanSubscription = ScanService.Subscribe(OnScanEvent);
        await RefreshCamerasAsync();
    }

    public Task SaveAsync() => Task.CompletedTask;

    private void OnScanEvent(CameraScanEvent ev)
    {
        _ = InvokeAsync(() =>
        {
            switch (ev)
            {
                case CameraScanStartedEvent:
                    _scanInProgress = true;
                    break;
                case CameraScanCompletedEvent or CameraScanCancelledEvent:
                    _scanInProgress = false;
                    break;
            }
            StateHasChanged();
        });
    }

    private async Task RefreshCamerasAsync()
    {
        var fresh = await CameraService.GetAllAsync();
        _cameras = fresh
            .OrderBy(c => c.CameraNumber ?? int.MaxValue)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        State.CamerasAdded = _cameras.Count;
        await InvokeAsync(StateHasChanged);
    }

    private void OnCamerasChanged() => _ = RefreshCamerasAsync();

    private async Task StartScan()
    {
        _message = null;
        StateHasChanged();

        var detected = await SubnetDetector.DetectAsync();
        if (detected is null)
        {
            _message = "Could not auto-detect a network subnet. You can add cameras manually from Settings → Cameras after setup completes.";
            _messageIsError = true;
            StateHasChanged();
            return;
        }

        var parseResult = SubnetParser.Parse(detected.Cidr);
        if (!parseResult.IsSuccess || parseResult.Value is null)
        {
            _message = $"Auto-detected subnet '{detected.Cidr}' could not be parsed. Use Settings → Cameras to add a custom subnet.";
            _messageIsError = true;
            StateHasChanged();
            return;
        }

        await ScanService.StartOnvifOnlyScanAsync(new[] { parseResult.Value });
    }

    public void Dispose()
    {
        ChangeNotifier.CamerasChanged -= OnCamerasChanged;
        _scanSubscription?.Dispose();
    }
}
```

Notes for the implementer:
- `SubnetDetectionClient` is a Scoped service registered via `builder.Services.AddScoped<SubnetDetectionClient>();` in `Program.cs:71`. Inject it directly — no wrapper needed.
- `SubnetParser.Parse` returns `Result<ParsedSubnet>` (with `IsSuccess` and `Value` properties); the existing pattern is in `CameraScanHostedService.ResolveSubnetsAsync` (now removed but visible in git history) and in `ScanNetworkModal.razor`.
- The simple "Scanning network for ONVIF cameras…" text replaces the heavier `ScanProgressChip` used on the production Settings page; the wizard scan is faster (~3-5s WS-Discovery only) so a simple label is sufficient. If the implementer wants to use `ScanProgressChip` instead, it requires `Checked` / `Total` / `FoundSoFar` parameters that the camera scan path doesn't currently emit — stick with the label.
- `Phase`/event handling reads from `ScanService.Phase` after each event; this is the same pattern Settings → Cameras uses through the panel itself.

- [ ] **Step 6.2: Build to verify**

Run: `cd C:/Users/jscha/source/repos/control-menu && dotnet build -c Release --nologo 2>&1 | tail -10`

Expected: `Build succeeded`, 0 errors. If you see `'SubnetParser' does not contain a definition for 'Parse'`, the import is correct (`ControlMenu.Services.Network` is already in the using directives) — re-verify the file matches Step 6.1 verbatim.

- [ ] **Step 6.3: Run the full test suite to confirm no regression**

Run: `cd C:/Users/jscha/source/repos/control-menu && dotnet test -c Release --nologo 2>&1 | tail -3`

Expected: `Passed!  - Failed: 0, Passed: 339`.

- [ ] **Step 6.4: Commit**

```bash
cd C:/Users/jscha/source/repos/control-menu
git add src/ControlMenu/Components/Pages/Setup/WizardCameras.razor
git commit -m "feat(wizard): WizardCameras step — auto-detect subnet + ONVIF-only scan

Replaces the placeholder stub with a full first-run camera-discovery flow
modeled on WizardDevices: single Scan Network button, auto-detected subnet
via SubnetDetectionClient, ONVIF-only WS-Discovery via the new
StartOnvifOnlyScanAsync entry point, results stream into the existing
DiscoveredCamerasPanel for inline-Add. Registered cameras render in a small
read-only table at the bottom (Cam # / Name / Mfr-Model / Address) only when
≥1 camera. Updates State.CamerasAdded so WizardDone summarizes correctly.
Non-ONVIF / RTSP-only cameras handled later via Settings → Cameras (called
out in the intro paragraph)."
```

---

### Task 7: Update CHANGELOG and run final smoke

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 7.1: Add the entry to the `[Unreleased]` Added section**

Open `CHANGELOG.md`. Find the `## [Unreleased]` heading and the `### Added` subheading directly under it. Insert as the FIRST bullet under `### Added`:

```markdown
- **Setup Wizard — Cameras step.** First-run users can now discover and register IP cameras during the Setup Wizard alongside Android devices. Single `Scan Network` button auto-detects the LAN subnet via `SubnetDetectionClient` (which calls ws-scrcpy-web's `/api/devices/scan/subnet` endpoint to find the adapter with a default gateway in the same subnet) and runs ONVIF-only WS-Discovery — no TCP-554 RTSP sweep, mirroring how WizardDevices uses mDNS-only quick discovery instead of full sweeps. Discovered ONVIF cameras stream into the existing `DiscoveredCamerasPanel` (shared with Settings → Cameras): inline Add per row, optional shared-creds entry above the grid for fleets with one admin password. Registered cameras render in a small Cam # / Name / Mfr-Model / Address summary table at the bottom of the step. WizardDone summary surfaces the camera count alongside the device count. Non-ONVIF / RTSP-only cameras are deferred to Settings → Cameras post-wizard via a clearly worded help note in the step intro.
- `ICameraScanService.StartOnvifOnlyScanAsync(IReadOnlyList<ParsedSubnet>, CancellationToken)` — new ONVIF-only scan entry point. Same `Phase` transitions, event bus, and `Hits` accumulation as `StartScanAsync`, but skips the parallel TCP-554 sweep branch. Implemented by factoring the existing `StartScanAsync` body into a private `RunScanAsync(subnets, includeRtspSweep, ct)` helper; `StartScanAsync` is now a thin wrapper with `includeRtspSweep: true`, preserving all existing behavior bit-for-bit.
- `WizardState.CamerasAdded` for the Done-step summary line.
```

Then find the `### Changed` subheading under `[Unreleased]` and append (or insert near the top):

```markdown
- **WizardDevices intro paragraph** now explicitly calls out the mDNS-only discovery limitation and directs older Android devices to Settings → Android Devices post-wizard. Substantive change in messaging, not behavior.
```

- [ ] **Step 7.2: Build + full test run + smoke checklist**

Run the build and tests one final time:

```bash
cd C:/Users/jscha/source/repos/control-menu
dotnet build -c Release --nologo 2>&1 | tail -5
dotnet test -c Release --nologo 2>&1 | tail -3
```

Expected: build clean, `Passed: 339`.

Then perform manual smoke (the implementer cannot run the Blazor app from a non-interactive environment, but the user will). Smoke list:

1. Reset wizard state: `await Config.SetSettingAsync("setup-completed", "false");` (or delete the row from `Settings`). Empty the `Cameras` table if any rows exist from prior testing.
2. Run: `dotnet run --project src/ControlMenu/ControlMenu.csproj -c Release` → http://localhost:5159 → wizard auto-launches.
3. Click through Welcome → Devices → Cameras step. Confirm:
   - Step heading "Cameras"
   - Intro paragraph mentions "ONVIF-enabled" + "Settings → Cameras" link for non-ONVIF
   - Single `Scan Network` button (no Add Manually, no Delete All)
4. Click `Scan Network`. Confirm within ~5s:
   - "Scanning network for ONVIF cameras…" label appears
   - Discovered panel populates with the user's 8 Hikvision/LTS cameras
   - Bulk-creds entry appears above the grid (since rows are ONVIF)
5. Enter shared admin password via bulk creds, click "Add all" — all 8 cameras register. Registered table appears below with Cam # / Name / Mfr-Model / Address.
6. Click Skip on the next step (Jellyfin) and walk to Done. Confirm Done summary shows "8 camera(s) registered" with green check.
7. Click Finish Setup → home page loads. Visit Settings → Cameras → confirm all 8 cameras present with correct Mfr/Model/MAC/CamNumber.
8. Re-run wizard via Settings → General → "Re-run Setup Wizard" button (verify path with the user — if it doesn't exist, smoke a fresh DB instead). Confirm Cameras step shows the registered table populated from DB on entry, no auto-scan, button still says "Scan Network".

If smoke is green:

- [ ] **Step 7.3: Commit CHANGELOG**

```bash
cd C:/Users/jscha/source/repos/control-menu
git add CHANGELOG.md
git commit -m "docs(changelog): wizard cameras step + ONVIF-only scan entry point"
```

- [ ] **Step 7.4: Hand off to user for merge decision**

The branch `feature/wizard-camera-scanner` should now have these commits on top of master:
1. `feat(liveness): Scan Now button on both liveness-interval surfaces` (already landed before this plan)
2. `docs(spec): wizard camera scanner design (2026-05-05)` (already landed)
3. `feat(cameras): ICameraScanService.StartOnvifOnlyScanAsync signature`
4. `feat(cameras): StartOnvifOnlyScanAsync — ONVIF-only scan path`
5. `feat(wizard): WizardState.CamerasAdded for Done-step summary`
6. `feat(wizard): WizardDone shows camera count in summary`
7. `docs(wizard): WizardDevices intro calls out mDNS-only limitation`
8. `feat(wizard): WizardCameras step — auto-detect subnet + ONVIF-only scan`
9. `docs(changelog): wizard cameras step + ONVIF-only scan entry point`

Surface the branch state to the user with `git log --oneline master..HEAD` and ask whether to merge to master + log the post-wizard "Settings → Cameras Quick Scan button" backlog item in `todo_control_menu.md`. Do NOT merge or push without explicit user authorization (per CLAUDE.md hard-to-reverse-action rule).

---

## Out of scope / post-merge follow-ups

These are NOT part of this plan; log to `todo_control_menu.md` after the wizard branch merges:

1. **Settings → Cameras `Quick Scan` button** (ONVIF-only, mirroring Android `Quick Refresh`). Reuses the `StartOnvifOnlyScanAsync` entry point this plan adds. Existing `Scan Network` button stays full (ONVIF + TCP-554). Parallel to the Android Settings page pattern.
2. **Vendor-adapter pattern for non-Hikvision ONVIF cameras** (Dahua / Reolink / Axis) — pre-existing follow-up from the camera-scanner branch shipment.
