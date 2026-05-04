# Camera Network Scanner — Design Spec

**Date:** 2026-05-04
**Status:** Approved (brainstormed); ready for implementation plan
**Resolves:** TODO item 10 — "Camera network scanner + settings redesign"
**Branch:** `feature/camera-network-scanner`

---

## 1. Problem statement

The current camera surface is an 8-slot indexed configuration: `Settings` rows keyed `camera-{1..8}-name|ip|port` plus matching secrets, served by `CameraService` returning a fixed-count list of `CameraConfig` records. Adding a camera is a manual form fill (name, IP, port, username, password). There is no way to discover cameras on the LAN, no way to identify a camera by manufacturer/model without manual labeling, and no way to add more than 8 cameras without bumping a hardcoded count.

Goal: replace the indexed-slot model with a DB-backed `Camera` entity, plus a network scanner that discovers cameras via ONVIF WS-Discovery (primary) and TCP-554 sweep (fallback for non-ONVIF cameras), modeled on the existing Android-device scanner pattern. Discovered cameras land in a panel where the user supplies credentials inline (ONVIF) or via a fuller modal (TCP-554) and adds them to the database with one validation round-trip.

---

## 2. Locked decisions

| # | Decision |
|---|---|
| 1 | **Hybrid discovery** — vendored .NET NuGet for the WS-Discovery UDP probe; hand-rolled SOAP for `GetDeviceInformation` / `GetProfiles` / `GetStreamUri` (3 verbs). go2rtc's undocumented `/api/onvif` endpoint is NOT used (depends on go2rtc release cycle; ONVIF protocol is stable, so handrolling is more reliable long-term). |
| 2 | **Schema:** `Camera` entity with explicit columns for identity (Id/Name/IpAddress/Port/LastSeen) and discovery-derived display fields (Manufacturer/Model/RtspStreamUrl/OnvifServiceUrl/IsOnvif/Enabled), plus a `Metadata` JSON column for non-load-bearing diagnostics (firmware, serial, profile count). Full schema in §4.1. |
| 3 | **Credentials** stored via `IConfigurationService` secrets, keyed by `Camera.Id`. No new encryption infrastructure. |
| 4 | **No migration of existing 8 cameras** — stale data is wiped, user re-adds via the new scanner. Old `camera-{1..8}-*` Settings keys + secrets purged at first launch (idempotent one-shot, marker-gated). |
| 5 | **Scan triggers:** on-demand (user-initiated from Cameras settings page) + periodic background re-scan every 15 min, configurable via `cameras-scan-interval-minutes` setting (`0` = off). |
| 6 | **Discovery scope (v1):** ONVIF + TCP-554 sweep, both shipped together. |
| 7 | **Credential validation flow:** ONVIF rows expose inline `username` / `password` fields in the Discovered panel; Add validates by attempting `GetStreamUri` (auth fail → inline error, row stays). TCP-554 rows open a fuller `AddTcpCameraModal` with display name + manufacturer + creds + stream path; Add validates by RTSP `DESCRIBE`. |
| 8 | **Subnet selection** mirrors the Android scanner: auto-detect the host's subnet via `SubnetDetectionClient` (queries ws-scrcpy-web's `/api/devices/scan/subnet`), user adds more via the existing `AddSubnetModal`, large-subnet warning via existing `LargeSubnetWarningModal`. |
| 9 | **UI entry point (v1):** `Settings → Cameras` page, replacing the current 8-slot configuration panel. Wizard step (`WizardCameras.razor`) and homepage Cameras module-card "Manage" pill are deferred to follow-up TODOs. |

**Code organization:** Approach (A) — self-contained `Modules/Cameras/Network/` with a new `ICameraScanService` whose contract shape parallels `INetworkScanService` but does not share an interface. The Android scanner is a sidecar-delegated WebSocket scanner; the camera scanner is in-process UDP+SOAP — the implementations differ structurally enough that generalization is YAGNI.

---

## 3. Architecture overview

```
┌─ UI layer (Blazor) ──────────────────────────────────────┐
│  Settings/CamerasSettings.razor (replaces old 8-slot)    │
│   ├─ Camera list (CRUD against ICameraService)           │
│   ├─ ScanNetworkModal (reused)                           │
│   ├─ DiscoveredCamerasPanel (new)                        │
│   │   ├─ ONVIF row: inline username/password/Add         │
│   │   └─ TCP-554 row: opens AddTcpCameraModal (new)      │
│   └─ AddSubnetModal / LargeSubnetWarningModal (reused)   │
└──────────────────────────────────────────────────────────┘
           │ (CRUD)                  │ (scan)
           ▼                         ▼
┌─ Service layer ──────────────────────────────────────────┐
│  ICameraService           ICameraScanService             │
│   - GetAll/Get/Add/Update  - Phase / Hits / Subscribe    │
│     /Delete (DB-backed)    - StartScanAsync(subnets)     │
│   - GetCredentialsAsync    - CancelAsync                 │
│     (delegates to          (in-process; periodic scan    │
│     IConfigurationService   driven by                    │
│     secrets)                CameraScanHostedService)     │
│        │                          │                      │
│        │            ┌─────────────┼─────────────┐        │
│        │            ▼             ▼             ▼        │
│        │      IOnvifDiscovery  IOnvifClient  IRtspProbe  │
│        │      Client (NuGet    (handrolled    (handrolled│
│        │      WS-Discovery     SOAP, 3 verbs) RTSP DESC) │
│        │      adapter)                                   │
│        ▼                                                 │
│  Camera entity / AppDbContext / IDbContextFactory        │
│        │                                                 │
│        ▼                                                 │
│  Go2RtcService.RegenerateConfigAsync (existing,          │
│   fed by ICameraService.GetEnabledAsync — only change:   │
│   reads from new table instead of indexed Settings)      │
└──────────────────────────────────────────────────────────┘
```

**New folders:**
- `Modules/Cameras/Network/` — scanner internals (WS-Discovery client adapter, SOAP client, RTSP DESCRIBE probe, hosted background service).
- `Modules/Cameras/Migrations/` — purge step for legacy `camera-{1..8}-*` Settings keys and secrets (one-shot, idempotent).
- `Modules/Cameras/Entities/` — `Camera` entity (kept module-local, not under shared `Data/Entities/`).

**Reused (no changes):**
- `Services/Network/ParsedSubnet`, `SubnetParser`, `SubnetDetectionClient`.
- `Components/Shared/Scanner/ScanNetworkModal`, `AddSubnetModal`, `LargeSubnetWarningModal`.
- `IConfigurationService` (secrets), `IDbContextFactory<AppDbContext>`.
- `Go2RtcService` (regen plumbing untouched; just feeds from new entity table).

**Replaced:**
- `Modules/Cameras/CameraConfig.cs` (record) — deleted; `Camera` entity takes its place.
- `Modules/Cameras/Services/CameraService.cs` — rewritten as DB-backed CRUD service.
- `Modules/Cameras/Services/ICameraService.cs` — new contract (no more `GetCameraCountAsync` / index-based methods).

---

## 4. Data model

### 4.1 `Modules/Cameras/Entities/Camera.cs`

```csharp
namespace ControlMenu.Modules.Cameras.Entities;

public class Camera
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string IpAddress { get; set; }
    public int Port { get; set; } = 554;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? RtspStreamUrl { get; set; }    // resolved via GetStreamUri (ONVIF) or user-entered (TCP-554)
    public string? OnvifServiceUrl { get; set; }  // null for non-ONVIF cameras
    public bool IsOnvif { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime? LastSeen { get; set; }
    public string? Metadata { get; set; }         // JSON: firmware, serial, profile count, etc.
}
```

Lives under `Modules/Cameras/Entities/`, NOT `Data/Entities/`. Cameras are a module concern; `Data/Entities/` holds cross-module shared entities (`Device`, `Job`, `Dependency`, `Setting`).

### 4.2 `AppDbContext` additions

```csharp
public DbSet<Camera> Cameras => Set<Camera>();

modelBuilder.Entity<Camera>(e =>
{
    e.HasKey(c => c.Id);
    e.HasIndex(c => c.IpAddress);  // common lookup pattern
});
```

EF Core migration generated via `dotnet ef migrations add AddCamerasTable`. Single table, one index, no foreign keys. Auto-applied on app startup via existing migration runner.

### 4.3 Settings keys (under `ModuleId="cameras"`)

- `cameras-scan-interval-minutes` (int, default `15`, `0` = off)
- `cameras-scan-subnets` (string — JSON array of normalized CIDRs the user has added beyond the auto-detected one; mirrors the Android scanner's `scan-subnets` key, namespaced with the `cameras-` prefix to keep module surfaces separate)
- `camera-{guid}-username` (secret, via `SetSecretAsync`)
- `camera-{guid}-password` (secret, via `SetSecretAsync`)
- `cameras-legacy-purge-completed` (string `"true"` once one-shot purge has run)

Seed/fixtures: none. The 8 existing cameras are wiped per decision #4; user re-adds via scanner.

---

## 5. Services

### 5.1 `ICameraService` — DB-backed CRUD

```csharp
public interface ICameraService
{
    Task<IReadOnlyList<Camera>> GetAllAsync();
    Task<IReadOnlyList<Camera>> GetEnabledAsync();   // for go2rtc.yaml regen
    Task<Camera?> GetAsync(Guid id);
    Task<Camera> AddAsync(Camera camera, string username, string password);
    Task UpdateAsync(Camera camera);
    Task SetCredentialsAsync(Guid id, string username, string password);
    Task<(string Username, string Password)?> GetCredentialsAsync(Guid id);
    Task DeleteAsync(Guid id);   // also wipes secrets
    Task UpdateLastSeenAsync(Guid id);
}
```

`AddAsync` takes creds inline because every Add path (ONVIF inline-row, TCP-554 modal) submits creds at the same moment as the row data. `DeleteAsync` calls `IConfigurationService.RemoveSecretAsync` for both keys to prevent orphaned secrets.

**Change notifier:** new `ICameraChangeNotifier` mirroring `IDeviceChangeNotifier`. Subscribes drive (i) live UI refresh in `CamerasSettings.razor` and (ii) `Go2RtcService.RegenerateConfigAsync()` so go2rtc picks up Add/Update/Delete/Enable-toggle without a manual restart.

### 5.2 `ICameraScanService` — orchestrator

Mirrors `INetworkScanService` shape:

```csharp
public interface ICameraScanService
{
    ScanPhase Phase { get; }
    IReadOnlyList<CameraScanHit> Hits { get; }
    IDisposable Subscribe(Action<CameraScanEvent> onEvent);
    Task StartScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default);
    Task CancelAsync(CancellationToken ct = default);
}

public sealed record CameraScanHit(
    string IpAddress,
    int Port,
    bool IsOnvif,
    string? Manufacturer,   // from WS-Discovery probe XML, null for TCP-554
    string? Model,          //   "
    string? OnvifServiceUrl // null for TCP-554
);
```

`StartScanAsync` orchestrates two sub-scans in parallel (see §6 for choreography). `Hits` already filters out cameras saved in the DB — those just get `LastSeen` bumped.

### 5.3 `IOnvifDiscoveryClient` — WS-Discovery probe (vendored NuGet adapter)

```csharp
public interface IOnvifDiscoveryClient
{
    Task<IReadOnlyList<OnvifProbeResponse>> ProbeAsync(TimeSpan timeout, CancellationToken ct);
}

public sealed record OnvifProbeResponse(
    string IpAddress,
    string? Manufacturer,
    string? Model,
    string OnvifServiceUrl);
```

Thin adapter over the chosen NuGet (candidate list to be evaluated during the implementation plan: `OnvifDiscovery` (Sven Bardos), or alternatives. Pick selection criteria: permissive license — preferably MIT or BSD — and active maintenance. Adapter pattern means we can swap libraries without rippling.

### 5.4 `IOnvifClient` — handrolled SOAP, 3 verbs

```csharp
public interface IOnvifClient
{
    Task<OnvifDeviceInfo> GetDeviceInformationAsync(string serviceUrl, string username, string password, CancellationToken ct);
    Task<IReadOnlyList<OnvifProfile>> GetProfilesAsync(string serviceUrl, string username, string password, CancellationToken ct);
    Task<string> GetStreamUriAsync(string serviceUrl, string profileToken, string username, string password, CancellationToken ct);
}
```

Implementation: `HttpClient` POSTs SOAP envelopes (templated strings, not the heavy `System.ServiceModel` stack). WS-Security UsernameToken with PasswordDigest (`Base64(SHA1(nonce + created + password))`) — standard ONVIF auth.

Throws typed exceptions:
- `OnvifAuthenticationException` (HTTP 401 / `ter:NotAuthorized` SOAP fault) → discovered-row inline error
- `OnvifSoapFaultException` → generic error display
- `HttpRequestException` / `TaskCanceledException` → "camera unreachable"

**Hikvision quirk handling:** some Hikvision firmware reject the password digest and require plain-text. On `wsse:FailedAuthentication`, retry once with PasswordText. Document the dual-path in code comments.

### 5.5 `IRtspProbeClient` — TCP-554 + RTSP DESCRIBE

```csharp
public interface IRtspProbeClient
{
    Task<bool> ProbeTcpAsync(string ip, int port, TimeSpan timeout, CancellationToken ct);
    Task<RtspDescribeResult> DescribeAsync(string rtspUrl, TimeSpan timeout, CancellationToken ct);
}
```

`ProbeTcpAsync` is a `TcpClient.ConnectAsync` with timeout — used during the sweep phase. `DescribeAsync` performs the RTSP `DESCRIBE` handshake (with `Authorization: Basic` or `Digest` per server challenge) — used at TCP-554-modal Add-time to validate creds + stream path.

### 5.6 `CameraScanHostedService` — periodic background scan

`IHostedService` mirroring the Jellyfin worker pattern. Uses `CancellationToken.None` for the inner scan call per the archived Blazor-background-jobs feedback memory (workers must not use circuit-scoped tokens).

```
StartAsync:
  Wait 30s (initial-run delay; lets go2rtc + network stack settle)
  Loop while !stopping:
    if (ICameraScanService.Phase == Idle):
      interval = read cameras-scan-interval-minutes (default 15)
      if interval > 0:
        subnets = SubnetDetectionClient.DetectAsync (auto)
                + user-added from cameras-scan-subnets setting (JSON CIDR list)
        ICameraScanService.StartScanAsync(subnets, CancellationToken.None)
    Sleep min(interval, 1) minutes  ← re-read setting on next tick so changes apply live
StopAsync: signal stopping; current scan completes naturally
```

**Re-entrancy guard:** if a manual scan is in progress when the timer fires, the hosted service skips that tick rather than queueing. `Phase != Idle` short-circuits.

The hosted service does NOT auto-add discovered cameras — it just keeps `Hits` fresh and bumps `LastSeen` for cameras already in the DB whose IPs respond. The user always controls Add via the Discovered panel.

**DI registration:** all six services + the hosted service registered in `Program.cs`. Existing `Go2RtcService` registration unchanged.

---

## 6. Discovery flow

```
StartScanAsync(subnets, ct)
   ▼
   Phase = Scanning
   Emit ScanStartedEvent
   ▼
   ┌─ Run in parallel ────────────────────────────────────────┐
   │                                                          │
   │  Branch A: ONVIF WS-Discovery                            │
   │   - Send single multicast probe (UDP 239.255.255.250:3702)│
   │   - Listen for responses for ~3s                          │
   │   - Filter responses to those whose source IP falls       │
   │     within any of the requested subnets                   │
   │   - For each: emit CameraScanHit(IsOnvif=true, ...)       │
   │                                                          │
   │  Branch B: TCP-554 sweep                                 │
   │   - For each subnet, parallelize across host IPs          │
   │     (semaphore cap, e.g. 64 concurrent)                   │
   │   - Probe = TcpClient.ConnectAsync, 1s timeout            │
   │   - Skip IPs already reported by Branch A                 │
   │     (live coordination via concurrent set)                │
   │   - For each surviving hit: emit CameraScanHit(           │
   │     IsOnvif=false, Manufacturer=null, Model=null, ...)    │
   │                                                          │
   └──────────────────────────────────────────────────────────┘
   ▼
   On each emitted hit: filter against ICameraService.GetAllAsync
   (matched by IpAddress) — known cameras get UpdateLastSeenAsync,
   are NOT added to Hits collection
   ▼
   Phase = Completed
   Emit ScanCompletedEvent
```

**Dedupe rules (layered):**
1. **Within-scan dedupe** — Branch B skips any IP Branch A already hit. Concurrent set; Branch A finishes first in practice (multicast is fast).
2. **Already-saved filter** — at emit time, hit IP looked up against existing `Camera.IpAddress`. Match → bump `LastSeen`, suppress from `Hits`. No match → goes into `Hits`.
3. **Cross-subnet dedupe** — same IP showing up because user added overlapping subnets is collapsed by `(IpAddress, Port)` key in the `Hits` list.

**Branch A subnet filtering caveat:** WS-Discovery is a multicast SHOUT — every ONVIF camera on every routable network the host can reach will respond, even if those networks weren't in `subnets`. The subnet filter is applied to the *response* source IPs (drop hits outside the requested subnets). This is intentional and matches the user's "scan these subnets" mental model; a camera on a subnet the user didn't add will not show up.

**Cancellation:** `ct` propagates to both branches. UDP listener exits cleanly; TCP sweep semaphore drains. Phase resets to `Idle`, `ScanCancelledEvent` emitted.

**Error policy:** a failed probe on one IP doesn't fail the scan. Logged at `Debug`, scan continues. A *catastrophic* failure (e.g., UDP socket bind fails because port 3702 already in use by another ONVIF client on the host) emits `ScanErrorEvent` and aborts that branch only — the other branch still completes.

**Performance budget:** `/24` subnet = 254 hosts × 1s timeout / 64 concurrent ≈ **~4s** for TCP sweep. WS-Discovery wait window 3s. Parallel total ≈ **5s**. `/22` (1022 hosts) ≈ ~16s. The existing `LargeSubnetWarningModal` covers user-visible "this'll take a while" UX.

**Logging granularity** per `feedback_logging_granularity.md`: log scan-shape + aggregates (subnet count, total IPs, ONVIF hits, TCP hits, duration), NOT per-IP probe attempts. Per-IP only at `Debug`.

---

## 7. UI

### 7.1 `Settings/CamerasSettings.razor` (replaces current 8-slot panel)

Single page, three regions stacked:

```
┌─────────────────────────────────────────────────────────────────┐
│  Cameras                                                        │
│                                                                 │
│  [Scan Network]  [Add Camera Manually]                          │
│                                                                 │
│  ┌─ Configured cameras ────────────────────────────────────┐    │
│  │ Name          │ IP            │ Mfr/Model │ ONVIF │ ⚙  │    │
│  │ ────────────────────────────────────────────────────────│    │
│  │ Front door    │ 192.168.1.50  │ Hikvision │  ✓    │ ☑ ✎ │    │
│  │               │               │ DS-2CD... │       │ 🗑  │    │
│  │ Back yard     │ 192.168.1.51  │ Hikvision │  ✓    │ ☑ ✎ │    │
│  │ Garage        │ 192.168.1.52  │ —         │  ✗    │ ☑ ✎ │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                 │
│  ┌─ Discovered (3) ────────────────────────────────────────┐    │
│  │  ONVIF: Hikvision DS-2CD2143G0-I  192.168.1.53          │    │
│  │     [user____] [pass____] [Add]                         │    │
│  │     ⚠ Authentication failed (last attempt 14:32:01)     │    │
│  │  ──────────────────────────────────────────────────     │    │
│  │  ONVIF: LTS LTCMR8222N            192.168.1.54          │    │
│  │     [user____] [pass____] [Add]                         │    │
│  │  ──────────────────────────────────────────────────     │    │
│  │  RTSP only: 192.168.1.99 :554                           │    │
│  │     [Add manually...]                                   │    │
│  └─────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

Conventions per `feedback_ui_consistency.md`:
- `white-space: nowrap` on table cells
- Notifications at bottom (5s success / 10s error)
- Pill buttons for Scan/Add
- Light-mode-readable

### 7.2 Scan trigger flow

`[Scan Network]` button → opens `ScanNetworkModal` (reused from Android scanner).

The modal already exposes a subnet list with auto-detect + Add-subnet + large-subnet warning. We reuse it as-is — the only difference is the modal needs to know which scan service to drive. Cleanest path: modal takes a delegate or strategy parameter (or a typed "scan source" enum). **Implementation note for the plan:** read the current `ScanNetworkModal` first; if it's hardcoded to `INetworkScanService`, factor a parameter through. If that refactor turns out to be more than ~50 LoC, fall back to a parallel `ScanCamerasModal.razor` that copies the shell — duplication preferable to a risky shared-component refactor.

### 7.3 `DiscoveredCamerasPanel.razor` (new)

Renders `ICameraScanService.Hits`. Two row variants:

**ONVIF row** — inline Add:
- Username + password text inputs
- Add button → calls `ICameraService.AddAsync` after `IOnvifClient.GetStreamUriAsync` returns
- On `OnvifAuthenticationException` → render error inline ("⚠ Authentication failed at HH:MM:SS"), row stays in panel
- On any other failure → render error, row stays
- On success → row removed from panel; new Camera appears in Configured list above

**TCP-554 row** — opens modal:
- "Add manually..." button → opens `AddTcpCameraModal` with IP/port pre-filled

### 7.4 `AddTcpCameraModal.razor` (new)

Form fields:
- IP (read-only, pre-filled)
- Port (read-only, default 554)
- Display name (required)
- Manufacturer (free text, optional)
- Username + password
- Stream path (with hint dropdown of common defaults: `Hikvision: /Streaming/Channels/101`, `Dahua: /cam/realmonitor?channel=1&subtype=0`, `Reolink: /h264Preview_01_main`, `Custom...`)

Submit → `IRtspProbeClient.DescribeAsync(rtsp://user:pass@ip:port/path)`. On 200/SDP → save Camera + creds, close modal, remove discovered row. On 401/404/timeout → inline error banner, modal stays open for retry.

### 7.5 Edit / Delete on Configured rows

- ✎ → `EditCameraModal.razor` (new) — same fields as Add but editable; saving re-runs validation.
- 🗑 → confirmation dialog → `ICameraService.DeleteAsync` → row removed, `Go2RtcService.RegenerateConfigAsync` triggered via `ICameraChangeNotifier`.
- ☑ (Enabled checkbox) → toggles `Camera.Enabled` → triggers regen so go2rtc immediately stops streaming the disabled one.

### 7.6 `CameraView.razor` (live-stream page) — unchanged

The user-facing camera-view page that renders streams via go2rtc — no design changes needed. It iterates streams from the same regen output; once `Go2RtcService` consumes the new entity table, this page works without modification (consumer code that enumerates `1..N` indices does need updating to enumerate from `ICameraService.GetEnabledAsync()` — verify during plan-writing).

---

## 8. go2rtc integration + cleanup

### 8.1 `Go2RtcService.GenerateConfigAsync` — minimal change

Current code reads `cameraService.GetConfiguredCamerasAsync()` (indexed) + per-index `GetCredentialsAsync(index)`. New code:

```csharp
var cameras = await cameraService.GetEnabledAsync();
foreach (var cam in cameras)
{
    var creds = await cameraService.GetCredentialsAsync(cam.Id);
    if (creds is null) continue;
    var (username, password) = creds.Value;
    sb.AppendLine($"  camera-{cam.Id:N}: rtsp://{username}:{password}@{cam.IpAddress}:{cam.Port}");
}
```

Stream key changes from `camera-{index}` (e.g., `camera-1`) to `camera-{guid:N}` (e.g., `camera-a1b2c3d4...`). Consumers of those stream names — primarily `CameraView.razor` — need updating to enumerate from `ICameraService.GetEnabledAsync()` rather than `1..N` indices.

For ONVIF cameras with a resolved `RtspStreamUrl` (full URL including profile path from `GetStreamUri`), use the resolved URL preferentially. For TCP-554 cameras, `RtspStreamUrl` was user-entered and already encodes the path. Fall back to bare `ip:port` only as a last resort for ONVIF cameras whose discovery couldn't fully probe — in practice this should be rare given the inline-validation-at-Add design.

**Trigger points for regen:**
1. Camera Add / Update / Delete / Enabled toggle (via `ICameraChangeNotifier` subscription).
2. Manual "Restart go2rtc" button (already exists).
3. App startup (already exists in `StartAsync`).

The notifier-driven regen replaces today's "save settings → manually click restart" flow with auto-regen-on-change.

### 8.2 Legacy purge — `PurgeLegacyCameraSettingsMigration`

`Modules/Cameras/Migrations/PurgeLegacyCameraSettingsMigration.cs` — runs once at startup if marker setting `cameras-legacy-purge-completed` is unset:

```
For i in 1..16 (cover any user who bumped camera-count):
  RemoveSettingAsync($"camera-{i}-name", "cameras")
  RemoveSettingAsync($"camera-{i}-ip", "cameras")
  RemoveSettingAsync($"camera-{i}-port", "cameras")
  RemoveSecretAsync($"camera-{i}-username", "cameras")
  RemoveSecretAsync($"camera-{i}-password", "cameras")
RemoveSettingAsync("camera-count", "cameras")
SetSettingAsync("cameras-legacy-purge-completed", "true", "cameras")
Log: "Purged N legacy camera settings"
```

Hardcoded ceiling of 16 covers any imaginable historical `camera-count` (default was 8). Idempotent guard means safe re-run if marker write fails. Logged once at info level.

**Run order:** runs **before** `Go2RtcService.StartAsync` so the first regen sees an empty configured-cameras table and writes a streams-less yaml (clean state). User then adds cameras via the new scanner; regen kicks in via notifier.

---

## 9. Testing approach

**Unit tests** (`tests/ControlMenu.Tests/Modules/Cameras/`):

- `CameraServiceTests` — CRUD round-trips against in-memory SQLite, secret-store delete-on-DeleteAsync, change-notifier fires on each mutation.
- `OnvifClientTests` — SOAP envelope generation (verify Username/PasswordDigest/Created/Nonce), fault-XML parsing → `OnvifAuthenticationException`, valid response → parsed result. Mock `HttpClient` via `HttpMessageHandler` interception. Cover both PasswordDigest and PasswordText paths (Hikvision quirk).
- `RtspProbeTests` — TCP probe timeout/success, RTSP DESCRIBE request format, 200/401/404 response parsing. Use a tiny mock TCP server in-process.
- `CameraScanServiceTests` — orchestration: dedupe between branches, already-saved filter, cancellation, error in one branch doesn't kill the other. Mock `IOnvifDiscoveryClient` + `IRtspProbeClient`.
- `PurgeLegacyCameraSettingsMigrationTests` — idempotency, marker-prevents-rerun, non-existent keys handled cleanly.

**Integration test:**
- `CameraScanHostedServiceTests` — interval re-read takes effect on next tick; `0` interval skips work; re-entrancy guard prevents overlap with manual scan.

**Manual smoke (added to `docs/manual-test-checklist.md`):**

New section "Camera scanner":
- Scan finds Hikvision/LTS units; inline Add with valid creds saves + streams; bad creds shows error in row.
- TCP-554-only camera goes through the modal flow; bad creds keeps modal open with error.
- Deleting a camera removes it from `go2rtc.yaml`; disabling toggles the same.
- Background scan: set interval to 1 minute; verify periodic logs after 30s warmup; set to 0; verify scan stops within 1 min.

**Regression-test asymmetry note:** per item 13 in `todo_control_menu.md`, every new local-binary consumer should get a `…_ResolvesViaDependencyPathResolver_NotBareName` regression test. The new camera code does NOT invoke any local binaries (go2rtc invocation is unchanged), so this rule does not apply here. Verify during implementation that no new spawn/exec lands.

**Playwright E2E** — out of scope for this work; once item 15 (Playwright E2E smoke tests) is greenlit, the Cameras settings flow gets included there.

---

## 10. Out of scope (explicit non-goals for v1)

- Setup wizard step (`WizardCameras.razor`) — bundled with the General Settings redesign cluster (todo item 2).
- Homepage Cameras module-card "Manage" pill — trivial follow-up.
- ONVIF PTZ control, motion-event subscriptions, snapshot URLs, audio streams.
- IPv6 scanning (we scan IPv4 subnets only; ONVIF probes mostly come back IPv4 anyway).
- Multi-profile selection (we save the first/main profile from `GetProfiles`; sub-streams left for future).
- Camera grouping / tagging / location metadata.
- Alerting on `LastSeen` going stale.
- Auto-creds-fill from a stored "default ONVIF creds" setting (explicitly rejected during brainstorm — every Add is per-camera-creds).

---

## 11. Risks + mitigations

| Risk | Mitigation |
|---|---|
| ONVIF NuGet pick is unmaintained / abandoned | Adapter pattern (`IOnvifDiscoveryClient`); swap library without touching consumers. License-check before pick. |
| WS-Discovery UDP 3702 conflicts with another local listener | Catch bind failure → `ScanErrorEvent` with friendly message; TCP-554 branch still runs. |
| Multicast doesn't traverse VLANs / managed switches with IGMP issues | User adds explicit subnets; TCP-554 sweep still finds those cameras. |
| Hikvision firmware quirks (some reject password digest, want plain text) | Try Digest first; on `wsse:FailedAuthentication` retry once with PasswordText. Document the dual-path. |
| go2rtc stream-name change (`camera-{index}` → `camera-{guid}`) breaks downstream consumers | Only known consumer is `CameraView.razor` (verified during plan). External consumers (Frigate? Home Assistant?) — flag in CHANGELOG as a breaking change for v1.0.0. |
| User has hand-edited `go2rtc.yaml` they'll lose on regen | Existing behavior — regen has always fully overwritten. No regression. |
| Concurrent scans (manual + scheduled) double-bumping `LastSeen` | Re-entrancy guard in hosted service skips ticks while `Phase != Idle`. |

---

## 12. Future follow-ups (post-v1)

1. **Setup wizard step** (`WizardCameras.razor`) — tied to general settings redesign cluster (todo item 2).
2. **Homepage Cameras module-card "Manage" pill** — one-line Razor change.
3. **Multi-profile selection** — let user pick sub-stream vs main-stream per camera.
4. **PTZ control** — for cameras that support it (`onvif://.../ptz` service).
5. **Snapshot polling** — `GetSnapshotUri` ONVIF verb for thumbnail generation.
6. **Camera tags / locations** — schema column + UI.

These are tracked under their own future TODOs once v1 ships.
