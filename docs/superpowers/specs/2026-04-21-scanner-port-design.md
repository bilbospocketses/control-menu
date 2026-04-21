# Network Scanner Port from ws-scrcpy-web — Design

## Overview

Port the full network-scan experience from ws-scrcpy-web's `feat/network-scan-port-5555` branch (shipped to ws-scrcpy-web main 2026-04-20, 230 tests, merge `611d49c`) into Control Menu's Settings › Devices page. Scanner logic stays in ws-scrcpy-web; Control Menu consumes it through the existing `ws-scan` WebSocket endpoint and surfaces the UX in Blazor Server.

This is Spec 1 of a two-spec effort. Spec 2 covers the per-device connection-options modal and auto-connect defaults (explicitly out of scope here).

## Problem

Control Menu's current Settings › Devices page has a single "Scan Network" button that runs `adb mdns services` + ARP once, updates registered-device IP/port in the DB, and lists any unregistered hits in a "Discovered on Network" panel. It has no way to:

- Scan for devices on non-default subnets.
- Probe a range or CIDR block for ADB-advertising devices that don't use mDNS.
- Recover from the double-connect `adb connect` bug that silently drops probes on some embedded adbd stacks (reproduced against the user's SM-T550 tablet).
- Show progress or cancel a scan in flight.
- Share scan state across browser tabs.

Meanwhile, ws-scrcpy-web has solved every one of those problems in its own scanner rewrite: subnet parser, gateway auto-detection, streaming WebSocket scan with progress/hit/cancel events, single-socket CNXN handshake probe, MAC resolver with post-TCP ARP refresh, large-subnet warning, and a four-state progress chip. The work is shipped, tested (230 tests), and exposed via the `ws-scan` endpoint that ships with every ws-scrcpy-web deploy.

## Solution: delegate scanner, port UX

Control Menu delegates all scan logic to ws-scrcpy-web's `ws-scan` endpoint. A new C# `NetworkScanService` singleton holds a server-side `ClientWebSocket` to ws-scrcpy-web, forwards user input from Blazor pages, fans out `scan.progress` / `scan.hit` / `scan.done` events to every connected circuit, and merges confirmed Adds into Control Menu's existing `Device` table.

The Blazor UX ports the ws-scrcpy-web modal stack (scan dialog, add-subnet dialog, large-subnet warning, progress chip, cheat sheet) while reusing Control Menu's existing `DeviceForm` for the Add step.

## Architecture

```
┌────────────────────────────────────────────────────────────────────┐
│  BROWSER (Blazor Server circuit — SignalR)                         │
│                                                                    │
│  DeviceManagement.razor                                            │
│    ├─ [+ Add Device]   [⟳ Quick Refresh]   [📡 Scan Network…]      │
│    ├─ Devices table                                                │
│    └─ Discovered on Network section  ◂────── mirrors last scan     │
│                                                                    │
│  ScanNetworkModal.razor  ◂── opened by "Scan Network…" button      │
│    ├─ Subnet list with [+ Add Subnet]                              │
│    ├─ ScanProgressChip  (idle/scanning/draining/complete/cancel)   │
│    ├─ Live hit stream (read-only rows + Add buttons)               │
│    └─ AddSubnetModal / LargeSubnetWarningModal / cheat-sheet link  │
└────────────────────┬───────────────────────────────────────────────┘
                     │ SignalR circuit
┌────────────────────▼───────────────────────────────────────────────┐
│  CONTROL MENU SERVER (.NET 9 Blazor Server)                        │
│                                                                    │
│  NetworkScanService (singleton)        DeviceService (existing)    │
│    ├─ Phase state machine                ├─ Add/Update/Delete      │
│    ├─ Spectator list                     └─ UpdateLastSeen         │
│    ├─ Hit buffer (for mid-scan attach)                             │
│    ├─ ClientWebSocket → ws-scan          ConfigurationService      │
│    └─ Emits scan events to spectators       └─ scan-subnets (JSON) │
│                                                                    │
│  WsScrcpyService                                                   │
│    └─ BaseUrl + deploy mode (Managed | External)                   │
└────────────────────┬───────────────────────────────────────────────┘
                     │ ws://<BaseUrl>/ws-scan
┌────────────────────▼───────────────────────────────────────────────┐
│  ws-scrcpy-web  (Node; native or Docker --network host)            │
│                                                                    │
│  ScanMw ──► NetworkScanner ──► mDNS + TCP sweep                    │
│                                AdbHandshakeProbe (CNXN)            │
│                                MacResolver (arp -a)                │
│                                SubnetDetector (gateway)            │
└────────────────────────────────────────────────────────────────────┘
```

### Key properties

- **Single integration surface.** Everything needed (embed.html iframe, ws-scan WebSocket) lives on one port. Deploy mode (Managed vs External) is a Control Menu setting, not a ws-scrcpy-web change.
- **ws-scrcpy-web stays standalone.** No upstream code changes. Matches `feedback_wsscrcpy_standalone` memory — compose, don't merge.
- **Blazor reactivity only.** The C# `NetworkScanService` holds the server-side WebSocket and drives page updates through the normal SignalR circuit. No browser-side WebSocket to ws-scrcpy-web, so CORS and browser compatibility are non-issues.
- **Shared scan state.** Two browser tabs attach to the same singleton and see the same scan. Second tab joining mid-scan gets a snapshot replay of hits-so-far.

## State model

Three distinct state scopes with three different lifetimes:

| Scope | Lives in | Survives |
|-------|----------|----------|
| **Subnet list** (persistent config) | `Settings` table, key `scan-subnets` (JSON string array) | App restart, container restart, machine reboot |
| **Scan state** (session-wide runtime) | `NetworkScanService` singleton — phase, hit buffer, cancel token, `ClientWebSocket` | Modal close, second-tab open, page reload |
| **Modal UI state** (transient) | `ScanNetworkModal.razor` component fields | Nothing — closing the modal disposes the component |

## Components

### New — Blazor UI (all under `Components/Shared/Scanner/`)

| Component | Role |
|-----------|------|
| `ScanNetworkModal.razor` | Main scan dialog. Subnet list + add button, progress chip, live hit stream with Add buttons. |
| `AddSubnetModal.razor` | Sub-dialog for adding a subnet. CIDR / IP / IP-range input, live validation via `SubnetParser`. |
| `LargeSubnetWarningModal.razor` | Interstitial before starting a scan with >2048 hosts (same threshold as ws-scrcpy-web). Strictly `>` not `>=`. |
| `ScanProgressChip.razor` | Four-state status pill rendered inside the modal header: `idle / scanning / draining / complete / cancelled`. Complete auto-hides after 5s, cancelled after 10s. |

### New — C# services (under `Services/Network/`)

| Type | Role |
|------|------|
| `INetworkScanService` / `NetworkScanService` | Singleton orchestrator. State machine, spectator list, hit buffer, owns the `ClientWebSocket` to `ws-scan`. Emits `ScanEvent`s via subscriber callbacks. Registered with `AddSingleton` in `Program.cs`. |
| `SubnetParser` | Static parser for `192.168.0.0/24` / `10.0.0.5` / `192.168.1.1-192.168.1.50`. Port of ws-scrcpy-web's TS version with identical error messages. |
| `ParsedSubnet` record | Normalized representation: CIDR string, host count, original raw input. |
| `ScanPhase` enum | `Idle / Scanning / Draining / Complete / Cancelled` |
| `ScanHit` record | Source (`mdns\|tcp`), IP, port, MAC, serial, name, label. Mirrors `scan.hit` from ws-scan. |
| `ScanEvent` hierarchy | `Progress / Hit / Done / Error / Cancelled` payloads fanned out to spectators. |

### Modified — existing files

| File | Change |
|------|--------|
| `Components/Pages/Settings/DeviceManagement.razor` | Split today's `Scan Network` button into `⟳ Quick Refresh` (inline `adb mdns services` + ARP, unchanged logic) and `📡 Scan Network…` (opens modal). Discovered section: semantics shift to "mirrors last scan" — Full Scan replaces, Quick Refresh merges. Existing `AddFromDiscovery` flow unchanged. |
| `Services/WsScrcpyService.cs` | Add deployment-mode toggle. Two modes: `Managed` (today — spawn Node child process on port 8000) and `External` (just ping `BaseUrl`, no process spawn). Health-check paths diverge; call sites don't notice. |
| `Services/DependencyManagerService.cs` | ws-scrcpy-web dependency entry respects mode: install/update controls hide in External mode; show read-only "URL" display instead. |
| `Components/Pages/Settings/GeneralSettings.razor` | Add "ws-scrcpy-web deployment" radio group (Managed / External) + URL input. Writes to `wsscrcpy-mode` + `wsscrcpy-url` settings. |
| `Program.cs` | Register `INetworkScanService` as `AddSingleton`. |

### Untouched (referenced only)

- `DeviceForm.razor` — used as-is for close-modal-then-open-form flow.
- `AdbService.ScanMdnsAsync / DetectDeviceKindAsync / GetPropAsync` — still drive Quick Refresh + AddFromDiscovery probes.
- `NetworkDiscoveryService` — ARP map, ping, `NormalizeMac` still used by Quick Refresh.
- `DeviceService` — Add/Update/Delete entry points unchanged.
- ws-scrcpy-web — zero code changes. `ws-scan` already ships on main.

### Static assets + new settings keys

- `wwwroot/help/subnets.html` — copy of ws-scrcpy-web's cheat sheet, linked from `AddSubnetModal`.
- `Settings` rows: `scan-subnets` (JSON string array), `wsscrcpy-mode` (`managed\|external`), `wsscrcpy-url` (string, read when mode = `external`).

## Data flow

### Full Scan from modal (happy path)

```
User clicks [📡 Scan Network…]
  │
  ▼
ScanNetworkModal.OnOpen
  → NetworkScanService.Subscribe(myCircuit)         ◂── becomes spectator
  → reads Settings["scan-subnets"] via ConfigurationService
  → renders subnet list + SubnetDetector.Detect() suggestion
  │
User clicks [Start]  (total hosts ≤ 2048 — else LargeSubnetWarning fires)
  │
  ▼
NetworkScanService.StartScan(subnets)
  precondition: phase ∈ {Idle, Complete, Cancelled}        ◂── Start button enabled only in these
  → hitBuffer.Clear()                                      ◂── prior scan's hits dropped
  phase: → Scanning
  → ClientWebSocket.ConnectAsync(BaseUrl + "/ws-scan")
  → send { type:"scan.start", subnets:[...] }
  │
ws-scan / NetworkScanner:  mDNS + TCP sweep in parallel, 64 concurrent probes
  │
  ▼
For each reply from ws-scan:
  { type:"scan.progress", scanned, total }  →  chip update
  { type:"scan.hit", source, ip, port, mac, serial, name, label }
       → service.hitBuffer.Add()                    ◂── replay cache
       → foreach spectator: subscriber.OnHit(hit)
       → Blazor circuit StateHasChanged → modal row appears
  { type:"scan.done", totalHits }  →  phase: Scanning → Complete
  │
  ▼
User clicks [Add] on hit row
  → modal.Close()    (but scan-state survives — phase stays Complete)
  → DeviceForm opens with pre-filled Name / MAC / IP / AdbPort
  → concurrent AdbService.DetectDeviceKindAsync + GetPropAsync refine Type + Name
  → user clicks Save → DeviceService.AddDeviceAsync
  → Discovered section drops the row; Devices table gains it
  │
  ▼
User reopens Scan Network… later
  → NetworkScanService.Subscribe() → snapshot of last scan's hits replayed
  → modal shows phase:Complete, hits:[...], counters frozen at scan.done values
  → Start re-enabled (ready for a new scan)
```

### Second browser tab opens modal mid-scan

Tab A has scan running, phase `Scanning`. Tab B opens `/settings/devices` and clicks Scan Network…:

- Tab B subscribes to `NetworkScanService`, receives a snapshot: `{phase:Scanning, progress:47/256, hits:[3]}`.
- Tab B's modal renders current state immediately. Start is disabled (phase ∉ {Idle, Complete, Cancelled}); Cancel is enabled.
- Both tabs receive the same hit/progress events from here on.
- Cancel in either tab: phase `Scanning → Draining → Cancelled`. Both chips flip through the same transitions.

### Quick Refresh (no modal, no ws-scan)

Unchanged from today's `DeviceManagement.ScanNetwork` logic:
- `AdbService.ScanMdnsAsync()` + ARP table build + ping-missing + re-ARP.
- Registered devices: update `LastKnownIp` / `LastSeen` / `AdbPort` in-place.
- Unregistered devices: MERGE (not replace) into Discovered section.
- Toast: `"{N} of {M} registered device(s) found; {K} new on network"`.

### Dedupe rule

- **MAC primary, IP fallback.** If a hit's MAC matches a registered device, it refreshes that device's `LastKnownIp` / `AdbPort` / `LastSeen` and is NOT surfaced in Discovered. If MAC is null (TCP probe landed before ARP resolved), dedupe falls back to IP; a placeholder MAC format `serial:<serial>` is used so subsequent hits on the same device merge correctly.
- Mirrors ws-scrcpy-web's `DeviceLabelStore` dual-key (serial + MAC) pattern.

## Error handling

| Failure | UX |
|---------|-----|
| ws-scrcpy-web unreachable on Start | Modal shows red banner: `ws-scrcpy-web unreachable at {BaseUrl} — check Settings › Dependencies.` Start button stays enabled for retry. No scan state is created. |
| Invalid subnet input | AddSubnetModal shows inline validation message from `SubnetParser` (same verbiage as ws-scrcpy-web). Save disabled until valid. |
| Subnet > 2048 hosts | LargeSubnetWarningModal intercepts Start. Lists offending subnet + host count. User confirms or cancels. Threshold is strict `>`. |
| Second Start while scanning | Service rejects in-process. UI keeps Start disabled while phase ∈ {Scanning, Draining}. Not surfaced as an error. |
| WS disconnects mid-scan (upstream crash, container restart) | Phase forced `Cancelled` with reason "upstream disconnect". Chip shows the reason. Partial hits preserved in buffer + Discovered section. No auto-reconnect in v1. |
| Cancel during Scanning | Phase `Scanning → Draining → Cancelled`. Drain window ≤ ws-scan's `scanTcpTimeoutMs` (default 300ms). Chip flickers through states; normal behaviour. Late hits arriving after Cancelled are silently dropped. |
| Managed mode spawn fails (port 8000 taken) | Existing `WsScrcpyService` behaviour — not a new failure surface. Dependencies page flags "not running". |
| External mode URL typo | Dependency health check fails (HTTP ping to `{BaseUrl}/` returns non-200). Dependencies page shows red. Scanner fails identically to "ws-scrcpy-web unreachable on Start". |
| ADB probe timeout during Add | DeviceForm opens pre-filled with defaults. Probes race with user input. Probes complete → Type/Name update via `StateHasChanged`; probes time out → user saves with defaults. No error toast. |

## Testing strategy

### Unit

- **`SubnetParserTests`** — CIDR, IP, range, fuzz pass of malformed inputs. Mirror ws-scrcpy-web's 30+ cases including the range-cap behaviour called out in `project_wsscrcpy_qa_findings`.
- **`SubnetDetectorTests`** — Windows `route print` fixture including `On-link` row filter, Linux `ip route` fixture, multi-interface metric sort, RFC1918 interface fallback, null-gateway (no detectable subnet) return path.
- **`NetworkScanServiceTests`** — phase transitions `Idle→Scanning→Draining→Cancelled/Complete`; spectator subscribe/unsubscribe during each phase; snapshot replay on mid-scan subscribe; dedupe (MAC primary, IP fallback, serial placeholder merge); concurrent Start rejection; late-hit drop after Cancelled.
- **`ConfigurationServiceTests`** additions — `scan-subnets` JSON round-trip, `wsscrcpy-mode` enum parse, `wsscrcpy-url` normalization.

### Integration

- **`FakeWsScanServer`** — minimal in-process WS server that accepts `scan.start`, replays scripted `progress/hit/done/cancelled/error` sequences, and verifies `scan.cancel` dispatch. Runs on a dynamic port in tests.
- **`NetworkScanService ↔ FakeWsScanServer`** — full happy path, cancel, WS-drop-mid-scan, invalid-subnets error response.
- **bUnit component tests** for `ScanNetworkModal`, `AddSubnetModal`, `LargeSubnetWarningModal`, `ScanProgressChip` — phase→chip binding, Add button wiring, subnet list render.
- **`DeviceManagementTests`** — Discovered section replace-on-Full vs merge-on-Quick-Refresh semantics, backed by a fake scan service.

### Manual QA checklist

1. Modal opens, gateway subnet auto-suggested, host count shown.
2. Add-subnet: CIDR / IP / range accepted; garbage rejected inline.
3. Subnets persist across reload (from `Settings` table).
4. Large-subnet warning fires on `> 2048` hosts.
5. Progress chip update cadence matches scan activity.
6. Cancel lifecycle: `Scanning → Draining → Cancelled`.
7. Complete auto-hides chip after 5s; Cancelled after 10s.
8. Cheat sheet link opens `/help/subnets.html`, back-link returns to modal.
9. mDNS + TCP dedupe — single card per device, no doubles.
10. Already-connected devices (in `adb devices`) appear once and are refreshed in place, not re-offered.
11. Managed mode: Control Menu spawns ws-scrcpy-web, scan works.
12. External mode: Control Menu skips spawn, URL pings for health, scan works against container's published port.
13. Two-tab spectator: second tab opening mid-scan gets full snapshot, shared cancel.
14. Add from scan: modal closes, DeviceForm pre-filled, save adds to table and drops row from Discovered.
15. Discovered section: Full Scan replaces, Quick Refresh merges.

## Deployment notes

### Managed vs External mode

Control Menu today auto-launches ws-scrcpy-web as a Node child process on port 8000 (see `feedback_control_menu_starts_scrcpy`). Introducing a deployment-mode toggle lets users put ws-scrcpy-web in Docker without tripping over that behaviour:

- **Managed** — today's default. Control Menu spawns Node, watches it, restarts on crash. Dependency page shows install/update controls.
- **External** — Control Menu makes no process-level assumptions. Health check is an HTTP ping to `{wsscrcpy-url}/`. Dependency page shows a read-only URL. All iframe + WebSocket traffic routes to the configured URL.

### Docker: mDNS requires host networking

`adb mdns services` uses multicast DNS (UDP 5353) over the host's LAN interface. Inside a default Docker bridge network, multicast doesn't cross the bridge, so the container sees zero mDNS hits. For the scanner to work in External mode against a Docker-hosted ws-scrcpy-web:

- Container must run with `--network host` (Linux host) or use a host-mode equivalent.
- Bridge networking is not supported for mDNS discovery. TCP sweep still works in bridge mode, but mDNS contribution is lost.

This is a ws-scrcpy-web deploy property, not a Control Menu change. Document it in the Deployment section of Control Menu's README and flag it in the External-mode settings UI hint text.

## Out of scope

Explicitly deferred — either to Spec 2 or to nowhere:

| Excluded | Why / where it lives instead |
|----------|------------------------------|
| Connection-options modal (codec, bitrate, audio source, keyboard, deviceKind, …) | Spec 2. This spec is scanner only. |
| Per-device "use as defaults" auto-connect logic | Spec 2. |
| "Adjust connection options" button on device dashboards | Spec 2. |
| Reconnect-with-new-options button mid-stream | Spec 2. |
| Auto-reconnect of the ws-scan WebSocket after upstream drop | v1 forces phase Cancelled. Defer until we see real-world drop frequency. |
| Historical scan log / audit trail | Discovered section mirrors *last* scan only. |
| Per-device "probe this one" button in the Devices table | Quick Refresh already covers that case. |
| IPv6 subnets | ws-scan is IPv4-only. Matching that. |
| Subnet list import/export | Nice-to-have; user retypes if the DB is blown away. |
| UI exposure of ws-scan tuning knobs (`scanConcurrency`, `scanTcpTimeoutMs`, …) | Defaults work. Overrides live in ws-scrcpy-web's own config. |
| Scan entry points outside `/settings/devices` | Not requested. One discovery surface. |
| Floating scan chip on the home page | Chip stays inside the modal header. |
| Direct C# CNXN handshake probe | We delegate. If ws-scrcpy-web ever needs to be optional, that's a separate effort. |
| Docker Compose / container recipes for ws-scrcpy-web | Deploy recipes belong in the separate installer / Docker sub-project (see `project_wsscrcpy_todo`). |
| Authentication / per-user session isolation | Control Menu is local-first, single-tenant. Two tabs on one scan is a feature. |

### Anti-goals (things to actively push back on during implementation)

- **Don't** reimplement `NetworkScanner` or `AdbHandshakeProbe` in C#. If the delegation path has a hole, fix it upstream — don't fork.
- **Don't** add a "skip mDNS" toggle. ws-scan's mDNS + TCP parallel is the tested shape.
- **Don't** persist in-flight scan hits to the DB mid-scan. Only user-confirmed Adds hit the `Device` table. Discovered is UI-only.
- **Don't** silently switch between Managed and External modes based on port heuristics. Deploy mode is an explicit setting.
- **Don't** add localStorage for subnet suggestions "just in case." `Settings` table is the single source of truth.

## References

- **ws-scrcpy-web spec + plan:** `docs/superpowers/specs/2026-04-19-network-scan-port-5555-design.md` and `docs/superpowers/plans/2026-04-19-network-scan-port-5555.md` (in the ws-scrcpy-web repo).
- **ws-scrcpy-web merge:** main `611d49c`, docs refresh `ae93cff` (both 2026-04-20).
- **Upstream source files (for porting reference, not modification):**
  - `src/server/network/NetworkScanner.ts` (orchestrator, 261 lines)
  - `src/server/network/AdbHandshakeProbe.ts` (single-socket CNXN probe, 169 lines)
  - `src/server/network/MacResolver.ts` (ARP resolver, 77 lines)
  - `src/server/network/SubnetDetector.ts` (gateway + RFC1918 detector, 183 lines)
  - `src/server/mw/ScanMw.ts` (WS message handler)
  - `src/app/client/ScanNetworkModal.ts` (UX reference, 275 lines)
  - `src/app/client/AddSubnetModal.ts`, `LargeSubnetWarningModal.ts`, `ScanProgressChip.ts`
  - `public/help/subnets.html` (cheat sheet — copy verbatim)
- **Related Control Menu memories:** `project_control_menu`, `feedback_wsscrcpy_standalone`, `feedback_control_menu_starts_scrcpy`, `project_wsscrcpy_todo` (section 6 — port to Control Menu).
