# Scanner Extraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Carve scanner state + finalize orchestration out of `DeviceManagement.razor` into a scoped `ScanLifecycleHandler` service and carve the Discovered panel markup into `DiscoveredPanel.razor`. Close reviewer item I-1 (the `_discovered`-across-awaits race) with an apply-time re-filter.

**Architecture:** `ScanLifecycleHandler` (scoped per Blazor circuit) owns `Discovered`, `DismissedAddresses`, `StashedNamesByMac`, `Phase`, `LastProgress`. Subscribes to `INetworkScanService` at construction; dispatches events and runs `FinalizeScanAsync` on `ScanCompleteEvent`. Exposes read-only state + `OnStateChanged` event + public methods (`StartFullScanAsync`, `CancelScanAsync`, `Dismiss`, `ReplaceDiscovered`, `ConsumeLastError`). Page injects the handler, subscribes to `OnStateChanged`, marshals via `InvokeAsync(StateHasChanged)`. `DiscoveredPanel.razor` is a pure presentational component.

**Tech Stack:** Blazor Server (.NET 9), xUnit, Moq. Existing patterns: `INetworkScanService` singleton with snapshot replay on subscribe, `ScanMergeHelper.AddressKey` for canonical dismiss keys, scoped services for per-circuit state, `NullLogger<T>.Instance` in tests.

**Spec:** `docs/superpowers/specs/2026-04-21-scanner-extraction-design.md` (commit `7648e75`).

**Branch:** `master`. Each task commits directly. If the engineer prefers a feature branch, create `feat/scanner-extraction` first via `git switch -c feat/scanner-extraction`.

**Working directory:** `C:/Users/jscha/source/repos/tools-menu/`

**Verification baseline:** 211 tests passing as of commit `d07ff7f`.

---

## File Structure

### Files to create

| File | Purpose |
|---|---|
| `src/ControlMenu/Services/Network/DiscoveredDevice.cs` | Public record promoted from its nested spot in `DeviceManagement.razor:189`. Shared by handler + panel. |
| `src/ControlMenu/Services/Network/IScanLifecycleHandler.cs` | Handler interface: read-only state, `OnStateChanged` event, public methods. |
| `src/ControlMenu/Services/Network/ScanLifecycleHandler.cs` | Handler implementation. Owns scanner state, subscribes to `INetworkScanService`, runs `FinalizeScanAsync`. |
| `src/ControlMenu/Components/Shared/Scanner/DiscoveredPanel.razor` | Presentational Razor component. Renders the Discovered table; owns `NameFor`. |
| `tests/ControlMenu.Tests/Services/FakeNetworkScanService.cs` | Test double: `Subscribe` returns a disposable; `Emit(ScanEvent)` pushes to subscribers; `Phase` is settable from tests. |
| `tests/ControlMenu.Tests/Services/ScanLifecycleHandlerTests.cs` | 12 handler tests. |

### Files to modify

| File | What changes |
|---|---|
| `src/ControlMenu/Program.cs` | Register `AddScoped<IScanLifecycleHandler, ScanLifecycleHandler>()` alongside existing scoped services. |
| `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor` | Remove the nested `DiscoveredDevice` record (T1). Switch from injecting `INetworkScanService` to injecting `IScanLifecycleHandler`; delete scan-related fields + methods; delete the inline Discovered block (T10). Replace the inline Discovered block with `<DiscoveredPanel ... />` and delete the now-unused `NameFor` (T11). |
| `CHANGELOG.md` | Add a `### Changed` bullet under `[Unreleased]` describing the extraction. |

### No changes

`INetworkScanService`, `NetworkScanService`, `ScanNetworkModal`, `ScanProgressChip`, `ScanMergeHelper`, `ParsedSubnet`, `SubnetParser`, `HitDedupe`, `ScanEvent`, `ScanHit`.

---

## Task Ordering Rationale

Tasks are ordered so every commit leaves the app working:

1. **T1** — Move `DiscoveredDevice` to its own file. Page still compiles (adds a `using`); no behavioral change.
2. **T2** — Add `FakeNetworkScanService` test double. Pure test-support; no prod code touched.
3. **T3-T9** — Build the handler incrementally, test-first. The handler is not wired into the page yet, so the page continues to work with its current inline implementation. Each task adds one behavior + one test, commits when green.
4. **T10** — Switch the page to use the handler in a single commit. This is the "moment of truth" — duplicate state goes away, page shrinks. Manual smoke test here.
5. **T11** — Carve `DiscoveredPanel.razor`. Mechanical markup move + `NameFor`; app works before and after.
6. **T12** — CHANGELOG.
7. **T13** — Manual QA (user-driven; 6 existing items from the UX-redesign spec + 2 new for handler-specific flows).

Each intermediate commit passes `dotnet test` and the app runs. No "broken commits in the middle" — critical because this work may be bisected.

---

## Task 1: Promote `DiscoveredDevice` to a standalone type

**Files:**
- Create: `src/ControlMenu/Services/Network/DiscoveredDevice.cs`
- Modify: `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor`

Currently defined as a nested `private record` inside the page's `@code` block (line 189). The handler and the panel both need it — can't stay nested.

- [ ] **Step 1: Create the standalone record**

Write `src/ControlMenu/Services/Network/DiscoveredDevice.cs`:

```csharp
namespace ControlMenu.Services.Network;

/// <summary>
/// A device that appeared in the "Discovered on Network" panel but isn't yet
/// registered with Control Menu. Populated from three sources: live mDNS hits
/// during a Full Scan, adb-merge rows appended on scan completion, and mDNS
/// results from Quick Refresh.
/// </summary>
public sealed record DiscoveredDevice(
    string ServiceName,
    string Ip,
    int Port,
    string? Mac,
    string? Source = null);
```

- [ ] **Step 2: Delete the nested record from `DeviceManagement.razor`**

Remove lines 186-189 from `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor`:

```csharp
    // Entries shown in the "Discovered on Network" panel. Devices already in the
    // registered-devices table are filtered out and their IP/port are updated in
    // the DB directly by ScanNetwork.
    private record DiscoveredDevice(string ServiceName, string Ip, int Port, string? Mac, string? Source = null);
```

The existing `@using ControlMenu.Services.Network` at the top of the file (line 6) already imports the namespace, so `DiscoveredDevice` resolves to the new type automatically.

- [ ] **Step 3: Build**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj --nologo -c Release
```

Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 4: Run tests**

```bash
dotnet test --nologo --verbosity quiet
```

Expected: 211/211 passing.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Services/Network/DiscoveredDevice.cs src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor
git commit -m "refactor(scanner): promote DiscoveredDevice to Services.Network

Nested record inside DeviceManagement.razor moves to its own public
file so the upcoming ScanLifecycleHandler and DiscoveredPanel can both
reference it. No behavioral change — Razor file picks up the new type
via the existing @using ControlMenu.Services.Network import."
```

---

## Task 2: `FakeNetworkScanService` test double

**Files:**
- Create: `tests/ControlMenu.Tests/Services/FakeNetworkScanService.cs`

The handler subscribes to `INetworkScanService` on construction. Handler tests need to dispatch events on demand. Rather than stand up the real `NetworkScanService` (which needs `WsScrcpyService` + config mocks), use a minimal fake with an `Emit(ScanEvent)` test helper.

- [ ] **Step 1: Create the fake**

Write `tests/ControlMenu.Tests/Services/FakeNetworkScanService.cs`:

```csharp
using ControlMenu.Services.Network;

namespace ControlMenu.Tests.Services;

/// <summary>
/// Minimal in-memory <see cref="INetworkScanService"/> for handler tests.
/// Subscribers are fired synchronously on <see cref="Emit"/>. Tests mutate
/// <see cref="Phase"/> and <see cref="Hits"/> directly.
/// </summary>
internal sealed class FakeNetworkScanService : INetworkScanService
{
    private readonly List<Action<ScanEvent>> _subscribers = new();

    public ScanPhase Phase { get; set; } = ScanPhase.Idle;
    public IReadOnlyList<ScanHit> Hits { get; set; } = Array.Empty<ScanHit>();

    public Func<IReadOnlyList<ParsedSubnet>, Task>? StartScanHook { get; set; }
    public Func<Task>? CancelHook { get; set; }

    public IDisposable Subscribe(Action<ScanEvent> onEvent)
    {
        _subscribers.Add(onEvent);
        return new Subscription(() => _subscribers.Remove(onEvent));
    }

    public Task StartScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default)
        => StartScanHook?.Invoke(subnets) ?? Task.CompletedTask;

    public Task CancelAsync(CancellationToken ct = default)
        => CancelHook?.Invoke() ?? Task.CompletedTask;

    /// <summary>Push an event to every current subscriber.</summary>
    public void Emit(ScanEvent evt)
    {
        foreach (var s in _subscribers.ToList())
            s(evt);
    }

    public int SubscriberCount => _subscribers.Count;

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        private Action? _onDispose = onDispose;
        public void Dispose()
        {
            _onDispose?.Invoke();
            _onDispose = null;
        }
    }
}
```

- [ ] **Step 2: Build tests**

```bash
dotnet build tests/ControlMenu.Tests/ControlMenu.Tests.csproj --nologo -c Release
```

Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 3: Run tests**

```bash
dotnet test --nologo --verbosity quiet
```

Expected: 211/211 passing (no new tests yet; the fake is unused).

- [ ] **Step 4: Commit**

```bash
git add tests/ControlMenu.Tests/Services/FakeNetworkScanService.cs
git commit -m "test(scanner): FakeNetworkScanService test double for handler tests

Minimal INetworkScanService implementation that lets tests Emit(ScanEvent)
directly to subscribers and mutate Phase/Hits without standing up the
real NetworkScanService WebSocket pipeline."
```

---

## Task 3: Handler scaffold + constructor subscription test

**Files:**
- Create: `src/ControlMenu/Services/Network/IScanLifecycleHandler.cs`
- Create: `src/ControlMenu/Services/Network/ScanLifecycleHandler.cs`
- Create: `tests/ControlMenu.Tests/Services/ScanLifecycleHandlerTests.cs`
- Modify: `src/ControlMenu/Program.cs`

Stand up the handler with a minimal surface: constructor that subscribes + stores deps, `Dispose` that unsubscribes, empty state properties, `OnStateChanged` event, and method stubs that throw `NotImplementedException`. Register in DI. Write the first test (constructor subscription + phase seeding) first.

- [ ] **Step 1: Write the failing test**

Create `tests/ControlMenu.Tests/Services/ScanLifecycleHandlerTests.cs`:

```csharp
using ControlMenu.Data.Entities;
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Services;
using ControlMenu.Services.Network;
using Moq;

namespace ControlMenu.Tests.Services;

public class ScanLifecycleHandlerTests
{
    private readonly FakeNetworkScanService _scan = new();
    private readonly Mock<IAdbService> _adb = new();
    private readonly Mock<INetworkDiscoveryService> _net = new();
    private readonly Mock<IConfigurationService> _config = new();
    private readonly Mock<IDeviceService> _devices = new();

    private ScanLifecycleHandler CreateHandler() =>
        new(_scan, _adb.Object, _net.Object, _config.Object, _devices.Object);

    [Fact]
    public void Constructor_SubscribesToScanService_AndSeedsPhase()
    {
        _scan.Phase = ScanPhase.Scanning;

        using var handler = CreateHandler();

        Assert.Equal(1, _scan.SubscriberCount);
        Assert.Equal(ScanPhase.Scanning, handler.Phase);
    }
}
```

- [ ] **Step 2: Run the test — confirm it fails to compile (type missing)**

```bash
dotnet test --nologo --verbosity quiet --filter "FullyQualifiedName~ScanLifecycleHandlerTests"
```

Expected: compile error — `ScanLifecycleHandler` does not exist.

- [ ] **Step 3: Create the interface**

Write `src/ControlMenu/Services/Network/IScanLifecycleHandler.cs`:

```csharp
namespace ControlMenu.Services.Network;

/// <summary>
/// Per-circuit handler that owns the state behind the Device Management page's
/// Discovered panel. Subscribes to <see cref="INetworkScanService"/>; dispatches
/// scan events into internal state; exposes read-only snapshots + an
/// <see cref="OnStateChanged"/> notification the page uses to trigger
/// <c>StateHasChanged</c>.
/// </summary>
public interface IScanLifecycleHandler : IDisposable
{
    IReadOnlyList<DiscoveredDevice> Discovered { get; }
    IReadOnlyDictionary<string, string> StashedNamesByMac { get; }
    ScanPhase Phase { get; }
    ScanProgressEvent? LastProgress { get; }

    /// <summary>
    /// Raised after every state mutation. Handler is Blazor-agnostic — the
    /// page is responsible for marshaling onto the UI thread via
    /// <c>InvokeAsync(StateHasChanged)</c>.
    /// </summary>
    event Action? OnStateChanged;

    /// <summary>
    /// Returns the last scan-error reason (if any) and clears it. One-shot so
    /// repeated <see cref="OnStateChanged"/> firings don't re-display the same toast.
    /// </summary>
    string? ConsumeLastError();

    /// <summary>
    /// Clears Discovered, DismissedAddresses, and StashedNamesByMac, then starts
    /// a new scan. Events flow back through the internal subscription.
    /// </summary>
    Task StartFullScanAsync(IReadOnlyList<ParsedSubnet> subnets);

    /// <summary>Cancel an in-flight scan. Phase changes arrive via the event stream.</summary>
    Task CancelScanAsync();

    /// <summary>
    /// Remove <paramref name="d"/> from Discovered and record its address as dismissed.
    /// Subsequent <see cref="ScanHitEvent"/>s and adb-merge rows for the same address
    /// are skipped for the remainder of the scan session.
    /// </summary>
    void Dismiss(DiscoveredDevice d);

    /// <summary>
    /// Replace the Discovered list wholesale. Used by Quick Refresh, which builds
    /// its own mDNS-derived list. Does NOT touch DismissedAddresses or
    /// StashedNamesByMac (Quick Refresh is a separate flow from Full Scan).
    /// </summary>
    void ReplaceDiscovered(IEnumerable<DiscoveredDevice> devices);
}
```

- [ ] **Step 4: Create the handler skeleton**

Write `src/ControlMenu/Services/Network/ScanLifecycleHandler.cs`:

```csharp
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Services;

namespace ControlMenu.Services.Network;

public sealed class ScanLifecycleHandler : IScanLifecycleHandler
{
    private readonly INetworkScanService _scan;
    private readonly IAdbService _adb;
    private readonly INetworkDiscoveryService _net;
    private readonly IConfigurationService _config;
    private readonly IDeviceService _devices;
    private readonly IDisposable _subscription;

    private readonly List<DiscoveredDevice> _discovered = new();
    private readonly HashSet<string> _dismissedAddresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _stashedNamesByMac = new(StringComparer.OrdinalIgnoreCase);
    private ScanPhase _phase;
    private ScanProgressEvent? _lastProgress;
    private string? _lastError;

    public ScanLifecycleHandler(
        INetworkScanService scan,
        IAdbService adb,
        INetworkDiscoveryService net,
        IConfigurationService config,
        IDeviceService devices)
    {
        _scan = scan;
        _adb = adb;
        _net = net;
        _config = config;
        _devices = devices;
        _subscription = _scan.Subscribe(OnScanEvent);
        _phase = _scan.Phase;
    }

    public IReadOnlyList<DiscoveredDevice> Discovered => _discovered;
    public IReadOnlyDictionary<string, string> StashedNamesByMac => _stashedNamesByMac;
    public ScanPhase Phase => _phase;
    public ScanProgressEvent? LastProgress => _lastProgress;

    public event Action? OnStateChanged;

    public string? ConsumeLastError()
    {
        var err = _lastError;
        _lastError = null;
        return err;
    }

    public Task StartFullScanAsync(IReadOnlyList<ParsedSubnet> subnets) =>
        throw new NotImplementedException("Task 6");

    public Task CancelScanAsync() =>
        throw new NotImplementedException("Task 6");

    public void Dismiss(DiscoveredDevice d) =>
        throw new NotImplementedException("Task 4");

    public void ReplaceDiscovered(IEnumerable<DiscoveredDevice> devices) =>
        throw new NotImplementedException("Task 7");

    public void Dispose() => _subscription.Dispose();

    private void OnScanEvent(ScanEvent evt)
    {
        // Populated in Tasks 4, 5, 6, 8.
    }

    private void RaiseStateChanged() => OnStateChanged?.Invoke();
}
```

- [ ] **Step 5: Register in DI**

In `src/ControlMenu/Program.cs`, find line 63:

```csharp
builder.Services.AddSingleton<INetworkScanService, NetworkScanService>();
```

Add immediately below it:

```csharp
builder.Services.AddScoped<IScanLifecycleHandler, ScanLifecycleHandler>();
```

- [ ] **Step 6: Run the test — confirm it passes**

```bash
dotnet test --nologo --verbosity quiet --filter "FullyQualifiedName~Constructor_SubscribesToScanService"
```

Expected: 1 passing.

- [ ] **Step 7: Run all tests**

```bash
dotnet test --nologo --verbosity quiet
```

Expected: 212/212 passing (prior 211 + 1 new).

- [ ] **Step 8: Build the app**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj --nologo -c Release
```

Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 9: Commit**

```bash
git add src/ControlMenu/Services/Network/IScanLifecycleHandler.cs src/ControlMenu/Services/Network/ScanLifecycleHandler.cs src/ControlMenu/Program.cs tests/ControlMenu.Tests/Services/ScanLifecycleHandlerTests.cs
git commit -m "feat(scanner): ScanLifecycleHandler scaffold + DI registration

Introduces IScanLifecycleHandler and a scaffold implementation that
subscribes to INetworkScanService on construction, seeds Phase, and
exposes read-only state + OnStateChanged + ConsumeLastError. Public
methods throw NotImplementedException; they're filled in across T4-T8
with a failing-test-first cadence.

Registered as scoped so each Blazor circuit gets its own handler but
shares the singleton INetworkScanService broadcast stream."
```

---

## Task 4: `ScanHitEvent` append + `Dismiss` method

**Files:**
- Modify: `src/ControlMenu/Services/Network/ScanLifecycleHandler.cs`
- Modify: `tests/ControlMenu.Tests/Services/ScanLifecycleHandlerTests.cs`

Three tests, one commit: hit appends when not dismissed, hit skipped when dismissed, dismiss removes + records + raises state-change.

- [ ] **Step 1: Add three failing tests**

Append to `ScanLifecycleHandlerTests.cs`:

```csharp
    [Fact]
    public void ScanHitEvent_AppendsToDiscovered_WhenAddressNotDismissed()
    {
        using var handler = CreateHandler();
        var stateChanges = 0;
        handler.OnStateChanged += () => stateChanges++;

        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "192.168.1.10:5555", "serial", "my-device", "", "aa:bb:cc:dd:ee:ff")));

        Assert.Single(handler.Discovered);
        Assert.Equal("192.168.1.10", handler.Discovered[0].Ip);
        Assert.Equal(5555, handler.Discovered[0].Port);
        Assert.Equal("my-device", handler.Discovered[0].ServiceName);
        Assert.Equal("aa:bb:cc:dd:ee:ff", handler.Discovered[0].Mac);
        Assert.Equal(1, stateChanges);
    }

    [Fact]
    public void ScanHitEvent_Skipped_WhenAddressIsDismissed()
    {
        using var handler = CreateHandler();
        // Seed one discovered row, then dismiss it.
        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "192.168.1.10:5555", "serial", "first", "", null)));
        handler.Dismiss(handler.Discovered[0]);

        // Re-emit the same address.
        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "192.168.1.10:5555", "serial2", "second", "", null)));

        Assert.Empty(handler.Discovered);
    }

    [Fact]
    public void Dismiss_RemovesFromDiscovered_AndRecordsAddress_AndRaisesStateChanged()
    {
        using var handler = CreateHandler();
        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "10.0.0.5:5555", "serial", "foo", "", null)));
        var stateChanges = 0;
        handler.OnStateChanged += () => stateChanges++;

        handler.Dismiss(handler.Discovered[0]);

        Assert.Empty(handler.Discovered);
        Assert.Equal(1, stateChanges);

        // Second hit at same address is skipped — proves DismissedAddresses is populated.
        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "10.0.0.5:5555", "serial2", "foo2", "", null)));
        Assert.Empty(handler.Discovered);
    }
```

- [ ] **Step 2: Run tests — confirm all three fail**

```bash
dotnet test --nologo --verbosity quiet --filter "FullyQualifiedName~ScanLifecycleHandlerTests"
```

Expected: 3 failing (NotImplementedException from `Dismiss`; the append test fails because `OnScanEvent` doesn't actually append).

- [ ] **Step 3: Implement `ScanHitEvent` append in `OnScanEvent`**

Replace the placeholder body of `OnScanEvent` in `ScanLifecycleHandler.cs`:

```csharp
    private void OnScanEvent(ScanEvent evt)
    {
        // Populated in Tasks 4, 5, 6, 8.
    }
```

with:

```csharp
    private void OnScanEvent(ScanEvent evt)
    {
        switch (evt)
        {
            case ScanHitEvent h:
                AppendHitIfNotDismissed(h.Hit);
                break;
        }
        _phase = _scan.Phase;
        RaiseStateChanged();
    }

    private void AppendHitIfNotDismissed(ScanHit hit)
    {
        var parts = hit.Address.Split(':');
        var ip = parts[0];
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 5555;
        if (_dismissedAddresses.Contains(ScanMergeHelper.AddressKey(ip, port)))
            return;
        _discovered.Add(new DiscoveredDevice(hit.Name, ip, port, hit.Mac));
    }
```

- [ ] **Step 4: Implement `Dismiss`**

Replace the `Dismiss` stub:

```csharp
    public void Dismiss(DiscoveredDevice d) =>
        throw new NotImplementedException("Task 4");
```

with:

```csharp
    public void Dismiss(DiscoveredDevice d)
    {
        _discovered.Remove(d);
        _dismissedAddresses.Add(ScanMergeHelper.AddressKey(d.Ip, d.Port));
        RaiseStateChanged();
    }
```

- [ ] **Step 5: Run tests — confirm all pass**

```bash
dotnet test --nologo --verbosity quiet
```

Expected: 215/215 passing (prior 212 + 3 new).

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Services/Network/ScanLifecycleHandler.cs tests/ControlMenu.Tests/Services/ScanLifecycleHandlerTests.cs
git commit -m "feat(scanner): handler ScanHit append + Dismiss

OnScanEvent now dispatches ScanHitEvent into _discovered (respecting
_dismissedAddresses). Dismiss(d) removes the row and records its
address key so future live hits and adb-merge rows targeting the same
ip:port are skipped for the scan session.

_phase is updated from the underlying service on every event and
OnStateChanged fires after every mutation."
```

---

## Task 5: Progress tracking + error surfacing

**Files:**
- Modify: `src/ControlMenu/Services/Network/ScanLifecycleHandler.cs`
- Modify: `tests/ControlMenu.Tests/Services/ScanLifecycleHandlerTests.cs`

Two tests: `ScanProgressEvent` updates `LastProgress`; `ScanErrorEvent` populates the one-shot error (consumed via `ConsumeLastError`).

- [ ] **Step 1: Add two failing tests**

Append to `ScanLifecycleHandlerTests.cs`:

```csharp
    [Fact]
    public void ScanProgressEvent_UpdatesLastProgress()
    {
        using var handler = CreateHandler();

        _scan.Emit(new ScanProgressEvent(42, 256, 3));

        Assert.NotNull(handler.LastProgress);
        Assert.Equal(42, handler.LastProgress!.Checked);
        Assert.Equal(256, handler.LastProgress.Total);
        Assert.Equal(3, handler.LastProgress.FoundSoFar);
    }

    [Fact]
    public void ScanErrorEvent_PopulatesConsumableError_OneShot()
    {
        using var handler = CreateHandler();

        _scan.Emit(new ScanErrorEvent("ws-scan connection refused"));

        var first = handler.ConsumeLastError();
        Assert.Equal("ws-scan connection refused", first);

        var second = handler.ConsumeLastError();
        Assert.Null(second);
    }
```

- [ ] **Step 2: Run tests — confirm both fail**

```bash
dotnet test --nologo --verbosity quiet --filter "FullyQualifiedName~ScanLifecycleHandlerTests"
```

Expected: 2 new failing.

- [ ] **Step 3: Extend `OnScanEvent`**

In `ScanLifecycleHandler.cs`, replace the `switch (evt)` body:

```csharp
        switch (evt)
        {
            case ScanHitEvent h:
                AppendHitIfNotDismissed(h.Hit);
                break;
        }
```

with:

```csharp
        switch (evt)
        {
            case ScanStartedEvent:
                _lastProgress = null;
                break;
            case ScanProgressEvent p:
                _lastProgress = p;
                break;
            case ScanHitEvent h:
                AppendHitIfNotDismissed(h.Hit);
                break;
            case ScanDrainingEvent:
            case ScanCompleteEvent:
            case ScanCancelledEvent:
                // Completion handling lands in Task 8.
                break;
            case ScanErrorEvent err:
                _lastError = err.Reason;
                break;
        }
```

(`ConsumeLastError` already exists on the handler from T3.)

- [ ] **Step 4: Run tests — confirm pass**

```bash
dotnet test --nologo --verbosity quiet
```

Expected: 217/217 passing.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Services/Network/ScanLifecycleHandler.cs tests/ControlMenu.Tests/Services/ScanLifecycleHandlerTests.cs
git commit -m "feat(scanner): handler progress + error event handling

ScanStartedEvent clears LastProgress; ScanProgressEvent updates it;
ScanErrorEvent populates a one-shot error string returned by
ConsumeLastError (clears on read so toasts don't re-fire across
unrelated state-change ticks).

Draining/Complete/Cancelled remain no-op placeholders — completion
orchestration lands in Task 8."
```

---

## Task 6: `StartFullScanAsync` + `CancelScanAsync`

**Files:**
- Modify: `src/ControlMenu/Services/Network/ScanLifecycleHandler.cs`
- Modify: `tests/ControlMenu.Tests/Services/ScanLifecycleHandlerTests.cs`

One test: `StartFullScanAsync` clears all three state sets and delegates to the underlying service. Cancel is a one-line delegate; no dedicated test (covered by the integration flow + manual QA).

- [ ] **Step 1: Add the test**

Append to `ScanLifecycleHandlerTests.cs`:

```csharp
    [Fact]
    public async Task StartFullScanAsync_ClearsState_AndDelegatesToScanService()
    {
        using var handler = CreateHandler();
        // Seed all three state sets.
        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "1.1.1.1:5555", "s", "n", "", null)));
        handler.Dismiss(handler.Discovered[0]);
        // Direct seed of stashed names via a second emit + finalize would require
        // Task 8 to be done; sufficient here to verify Discovered+Dismissed reset.
        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "2.2.2.2:5555", "s", "n", "", null)));
        Assert.Single(handler.Discovered);

        IReadOnlyList<ParsedSubnet>? capturedSubnets = null;
        _scan.StartScanHook = subnets => { capturedSubnets = subnets; return Task.CompletedTask; };

        var input = new List<ParsedSubnet> { new("192.168.1.0/24", "192.168.1.1", "192.168.1.254", 254) };
        await handler.StartFullScanAsync(input);

        Assert.Empty(handler.Discovered);
        Assert.Same(input, capturedSubnets);

        // Dismissed addresses cleared — the original 1.1.1.1:5555 would no longer be skipped.
        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "1.1.1.1:5555", "s", "n", "", null)));
        Assert.Single(handler.Discovered);
    }
```

Note: this test assumes `ParsedSubnet`'s constructor signature. Verify the signature by reading `src/ControlMenu/Services/Network/ParsedSubnet.cs` before finalizing — adjust the constructor call if the real type differs.

- [ ] **Step 2: Run test — confirm it fails**

```bash
dotnet test --nologo --verbosity quiet --filter "FullyQualifiedName~StartFullScanAsync"
```

Expected: 1 failing (NotImplementedException).

- [ ] **Step 3: Implement `StartFullScanAsync` + `CancelScanAsync`**

Replace the stubs:

```csharp
    public Task StartFullScanAsync(IReadOnlyList<ParsedSubnet> subnets) =>
        throw new NotImplementedException("Task 6");

    public Task CancelScanAsync() =>
        throw new NotImplementedException("Task 6");
```

with:

```csharp
    public async Task StartFullScanAsync(IReadOnlyList<ParsedSubnet> subnets)
    {
        _discovered.Clear();
        _dismissedAddresses.Clear();
        _stashedNamesByMac.Clear();
        RaiseStateChanged();
        await _scan.StartScanAsync(subnets);
    }

    public Task CancelScanAsync() => _scan.CancelAsync();
```

- [ ] **Step 4: Run tests — confirm pass**

```bash
dotnet test --nologo --verbosity quiet
```

Expected: 218/218 passing.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Services/Network/ScanLifecycleHandler.cs tests/ControlMenu.Tests/Services/ScanLifecycleHandlerTests.cs
git commit -m "feat(scanner): handler StartFullScanAsync + CancelScanAsync

StartFullScanAsync clears Discovered, DismissedAddresses, and
StashedNamesByMac, raises OnStateChanged, then delegates to the
underlying INetworkScanService. CancelScanAsync is a thin delegate —
phase changes arrive via the event stream."
```

---

## Task 7: `ReplaceDiscovered`

**Files:**
- Modify: `src/ControlMenu/Services/Network/ScanLifecycleHandler.cs`
- Modify: `tests/ControlMenu.Tests/Services/ScanLifecycleHandlerTests.cs`

- [ ] **Step 1: Add the test**

Append to `ScanLifecycleHandlerTests.cs`:

```csharp
    [Fact]
    public void ReplaceDiscovered_ReplacesList_LeavesOtherStateIntact()
    {
        using var handler = CreateHandler();
        // Seed Discovered + Dismissed via live flow.
        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "10.0.0.1:5555", "s", "pre", "", null)));
        handler.Dismiss(handler.Discovered[0]);

        var replacement = new[]
        {
            new DiscoveredDevice("fresh-1", "10.0.0.2", 5555, "aa:bb:cc:dd:ee:01"),
            new DiscoveredDevice("fresh-2", "10.0.0.3", 5555, "aa:bb:cc:dd:ee:02"),
        };

        var stateChanges = 0;
        handler.OnStateChanged += () => stateChanges++;

        handler.ReplaceDiscovered(replacement);

        Assert.Equal(2, handler.Discovered.Count);
        Assert.Equal("fresh-1", handler.Discovered[0].ServiceName);
        Assert.Equal(1, stateChanges);

        // Dismissed set preserved — re-emitting the previously dismissed address is still skipped.
        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "10.0.0.1:5555", "s", "after", "", null)));
        Assert.Equal(2, handler.Discovered.Count);
    }
```

- [ ] **Step 2: Run test — confirm it fails**

```bash
dotnet test --nologo --verbosity quiet --filter "FullyQualifiedName~ReplaceDiscovered"
```

Expected: 1 failing.

- [ ] **Step 3: Implement `ReplaceDiscovered`**

Replace the stub:

```csharp
    public void ReplaceDiscovered(IEnumerable<DiscoveredDevice> devices) =>
        throw new NotImplementedException("Task 7");
```

with:

```csharp
    public void ReplaceDiscovered(IEnumerable<DiscoveredDevice> devices)
    {
        _discovered.Clear();
        _discovered.AddRange(devices);
        RaiseStateChanged();
    }
```

- [ ] **Step 4: Run tests — confirm pass**

```bash
dotnet test --nologo --verbosity quiet
```

Expected: 219/219 passing.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Services/Network/ScanLifecycleHandler.cs tests/ControlMenu.Tests/Services/ScanLifecycleHandlerTests.cs
git commit -m "feat(scanner): handler ReplaceDiscovered for Quick Refresh

Replaces the Discovered list wholesale without touching DismissedAddresses
or StashedNamesByMac. Used by Quick Refresh, which builds its own fresh
mDNS-derived list and hands it to the handler."
```

---

## Task 8: `FinalizeScanAsync` + helpers + R2 race fix

**Files:**
- Modify: `src/ControlMenu/Services/Network/ScanLifecycleHandler.cs`
- Modify: `tests/ControlMenu.Tests/Services/ScanLifecycleHandlerTests.cs`

The big one. Port `FinalizeScanAsync` + its five helpers from the page, hook into `ScanCompleteEvent`, and include the R2 re-filter in `AppendAdbMergeRows`. Three tests: MAC enrichment, adb-merge append, and the R2 race (dismissal during ARP await is honored).

- [ ] **Step 1: Add three failing tests**

Append to `ScanLifecycleHandlerTests.cs`:

```csharp
    [Fact]
    public async Task ScanComplete_EnrichesNullMacFromArp()
    {
        using var handler = CreateHandler();
        // One live hit with null MAC.
        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "192.168.1.50:5555", "s", "target", "", null)));

        // ARP resolves its MAC.
        _net.Setup(n => n.GetArpTableAsync())
            .ReturnsAsync(new[] { new ArpEntry("192.168.1.50", "aa:bb:cc:dd:ee:50") });
        _adb.Setup(a => a.GetConnectedDevicesAsync()).ReturnsAsync(Array.Empty<string>());
        _devices.Setup(d => d.GetAllDevicesAsync()).ReturnsAsync(Array.Empty<Device>());

        _scan.Emit(new ScanCompleteEvent(1));
        // The ScanCompleteEvent handler starts FinalizeScanAsync; yield for its
        // awaits to complete. Handler dispatches event fire-and-forget, so we
        // need to let the task run.
        await WaitForHandlerIdle();

        Assert.Single(handler.Discovered);
        Assert.Equal("aa:bb:cc:dd:ee:50", handler.Discovered[0].Mac);
    }

    [Fact]
    public async Task ScanComplete_AppendsAdbMergeRows()
    {
        using var handler = CreateHandler();
        _adb.Setup(a => a.GetConnectedDevicesAsync())
            .ReturnsAsync(new[] { "192.168.1.100:5555", "192.168.1.101:5555" });
        _net.Setup(n => n.GetArpTableAsync())
            .ReturnsAsync(new[]
            {
                new ArpEntry("192.168.1.100", "aa:bb:cc:dd:ee:01"),
                new ArpEntry("192.168.1.101", "aa:bb:cc:dd:ee:02"),
            });
        _devices.Setup(d => d.GetAllDevicesAsync()).ReturnsAsync(Array.Empty<Device>());

        _scan.Emit(new ScanCompleteEvent(0));
        await WaitForHandlerIdle();

        Assert.Equal(2, handler.Discovered.Count);
        Assert.All(handler.Discovered, d => Assert.Equal("adb", d.Source));
    }

    [Fact]
    public async Task ScanComplete_AdbMergeRow_Dismissed_DuringArpWindow_IsSkipped()
    {
        using var handler = CreateHandler();
        _adb.Setup(a => a.GetConnectedDevicesAsync())
            .ReturnsAsync(new[] { "192.168.1.200:5555" });
        _devices.Setup(d => d.GetAllDevicesAsync()).ReturnsAsync(Array.Empty<Device>());

        // Gate the ARP call so we can dismiss in between.
        var arpTcs = new TaskCompletionSource<IReadOnlyList<ArpEntry>>();
        _net.Setup(n => n.GetArpTableAsync()).Returns(arpTcs.Task);

        _scan.Emit(new ScanCompleteEvent(0));
        // Handler is now awaiting ARP. User dismisses the adb-merge candidate.
        handler.Dismiss(new DiscoveredDevice("", "192.168.1.200", 5555, null));
        // Release ARP; adb-merge should re-filter against the freshly updated
        // _dismissedAddresses before appending.
        arpTcs.SetResult(new[] { new ArpEntry("192.168.1.200", "aa:bb:cc:dd:ee:99") });
        await WaitForHandlerIdle();

        Assert.Empty(handler.Discovered);
    }

    // Helper: yield control until all in-flight handler-triggered tasks have run.
    // Handler dispatches ScanComplete fire-and-forget; xUnit tests on the same
    // thread need to yield to let those continuations run.
    private static async Task WaitForHandlerIdle()
    {
        // Two yields cover ARP + PopulateStashedNames in the common path. If a
        // future test has more await depth, increase as needed.
        for (var i = 0; i < 5; i++) await Task.Yield();
    }
```

Before writing this, verify two type names by reading the source:

```bash
# Confirm the shape of ArpEntry and IDeviceService + Device:
grep -n "record ArpEntry\|class ArpEntry" src/ControlMenu/Services/NetworkDiscoveryService.cs
grep -n "interface IDeviceService" src/ControlMenu/Modules/AndroidDevices/Services/*.cs
```

If `ArpEntry` uses different property names or lives in a different namespace, adjust the `using` block + constructor calls. If `IDeviceService.GetAllDevicesAsync` returns `IEnumerable<Device>` rather than `IReadOnlyList<Device>`, adjust the `.ReturnsAsync(...)` call accordingly.

- [ ] **Step 2: Run tests — confirm all three fail**

```bash
dotnet test --nologo --verbosity quiet --filter "FullyQualifiedName~ScanComplete"
```

Expected: 3 failing.

- [ ] **Step 3: Port `FinalizeScanAsync` + five helpers into the handler**

In `ScanLifecycleHandler.cs`, wire `ScanCompleteEvent` into `FinalizeScanAsync`:

Replace:

```csharp
            case ScanDrainingEvent:
            case ScanCompleteEvent:
            case ScanCancelledEvent:
                // Completion handling lands in Task 8.
                break;
```

with:

```csharp
            case ScanDrainingEvent:
                break;
            case ScanCompleteEvent:
                _ = FinalizeScanAsync();
                break;
            case ScanCancelledEvent:
                break;
```

Add the `FinalizeScanAsync` method + helpers at the bottom of the class (before the closing brace):

```csharp
    private async Task FinalizeScanAsync()
    {
        try
        {
            var fromAdb = await DetermineAdbMergeCandidatesAsync();

            var needMacIps = _discovered.Where(d => d.Mac is null).Select(d => d.Ip)
                .Concat(fromAdb.Select(x => x.Ip))
                .Distinct()
                .ToList();

            var arpMap = await BuildArpMapWithPingsAsync(needMacIps);

            EnrichDiscoveredMacs(arpMap);
            AppendAdbMergeRows(fromAdb, arpMap);
            await PopulateStashedNamesAsync();
        }
        catch (Exception ex)
        {
            _lastError = $"Scan finalize failed: {ex.Message}";
        }
        finally
        {
            _phase = _scan.Phase;
            RaiseStateChanged();
        }
    }

    private async Task<IReadOnlyList<(string Ip, int Port)>> DetermineAdbMergeCandidatesAsync()
    {
        var devices = await _devices.GetAllDevicesAsync();
        var registeredIpPorts = devices
            .Where(d => !string.IsNullOrEmpty(d.LastKnownIp))
            .Select(d => ScanMergeHelper.AddressKey(d.LastKnownIp!, d.AdbPort))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var excludeIpPorts = _discovered
            .Select(d => ScanMergeHelper.AddressKey(d.Ip, d.Port))
            .Concat(registeredIpPorts)
            .Concat(_dismissedAddresses);

        var adbConnected = await _adb.GetConnectedDevicesAsync();
        return ScanMergeHelper.FindUnregisteredAdbConnected(adbConnected, excludeIpPorts);
    }

    private async Task<Dictionary<string, string>> BuildArpMapWithPingsAsync(IReadOnlyList<string> ipsToCover)
    {
        var arpMap = await BuildArpMapAsync();
        if (ipsToCover.Count == 0) return arpMap;

        var missing = ipsToCover.Where(ip => !arpMap.ContainsKey(ip)).ToList();
        if (missing.Count > 0)
        {
            await Task.WhenAll(missing.Select(ip => _net.PingAsync(ip)));
            arpMap = await BuildArpMapAsync();
        }
        return arpMap;
    }

    private async Task<Dictionary<string, string>> BuildArpMapAsync()
    {
        var entries = await _net.GetArpTableAsync();
        return entries
            .GroupBy(e => e.IpAddress)
            .ToDictionary(g => g.Key, g => g.First().MacAddress, StringComparer.OrdinalIgnoreCase);
    }

    private void EnrichDiscoveredMacs(IReadOnlyDictionary<string, string> arpMap)
    {
        for (var i = 0; i < _discovered.Count; i++)
        {
            if (_discovered[i].Mac is not null) continue;
            if (arpMap.TryGetValue(_discovered[i].Ip, out var mac))
                _discovered[i] = _discovered[i] with { Mac = mac };
        }
    }

    private void AppendAdbMergeRows(
        IReadOnlyList<(string Ip, int Port)> fromAdb,
        IReadOnlyDictionary<string, string> arpMap)
    {
        foreach (var x in fromAdb)
        {
            // R2 race fix. Between DetermineAdbMergeCandidatesAsync (which
            // captured _dismissedAddresses at t=0) and this loop, several
            // seconds of ARP+ping awaits elapsed during which the user may
            // have dismissed one of these addresses.
            if (_dismissedAddresses.Contains(ScanMergeHelper.AddressKey(x.Ip, x.Port)))
                continue;
            var mac = arpMap.TryGetValue(x.Ip, out var m) ? m : null;
            _discovered.Add(new DiscoveredDevice(
                ScanMergeHelper.AddressKey(x.Ip, x.Port),
                x.Ip, x.Port, mac, Source: "adb"));
        }
    }

    private async Task PopulateStashedNamesAsync()
    {
        foreach (var d in _discovered)
        {
            if (string.IsNullOrEmpty(d.Mac)) continue;
            if (_stashedNamesByMac.ContainsKey(d.Mac)) continue;
            var stashed = await _config.GetSettingAsync($"device-name-{d.Mac}");
            if (!string.IsNullOrEmpty(stashed))
                _stashedNamesByMac[d.Mac] = stashed;
        }
    }
```

Add the required `using System.Linq;` if not already present. Add a `using ControlMenu.Services;` if the `INetworkDiscoveryService.GetArpTableAsync` / `ArpEntry` live there (verify via the grep above).

- [ ] **Step 4: Run tests — confirm pass**

```bash
dotnet test --nologo --verbosity quiet
```

Expected: 222/222 passing.

If the R2 race test flakes (passes sometimes, fails sometimes), bump `WaitForHandlerIdle`'s iteration count from 5 to 10. The test is deterministic via `TaskCompletionSource`, but the yield count needs to cover all continuations in the FinalizeScanAsync chain.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Services/Network/ScanLifecycleHandler.cs tests/ControlMenu.Tests/Services/ScanLifecycleHandlerTests.cs
git commit -m "feat(scanner): handler FinalizeScanAsync + R2 race fix

Ports FinalizeScanAsync and its five helpers (DetermineAdbMergeCandidates,
BuildArpMapWithPings, BuildArpMap, EnrichDiscoveredMacs, AppendAdbMergeRows,
PopulateStashedNames) from DeviceManagement.razor into the handler. Runs
on ScanCompleteEvent.

R2: AppendAdbMergeRows re-checks _dismissedAddresses at apply time —
covers the window where the user dismisses a row during the multi-second
ARP/ping await between DetermineAdbMergeCandidates and the append loop.

Exceptions during finalize route to _lastError (consumable via
ConsumeLastError); _phase + RaiseStateChanged always fire in the finally
so the chip reflects the final phase even if finalize throws."
```

---

## Task 9: Verify `Dispose` unsubscribes

**Files:**
- Modify: `tests/ControlMenu.Tests/Services/ScanLifecycleHandlerTests.cs`

One confirmatory test — `Dispose` on the handler removes its subscription from the scan service.

- [ ] **Step 1: Add the test**

Append to `ScanLifecycleHandlerTests.cs`:

```csharp
    [Fact]
    public void Dispose_Unsubscribes()
    {
        var handler = CreateHandler();
        Assert.Equal(1, _scan.SubscriberCount);

        handler.Dispose();

        Assert.Equal(0, _scan.SubscriberCount);
    }
```

- [ ] **Step 2: Run the test — should already pass**

```bash
dotnet test --nologo --verbosity quiet --filter "FullyQualifiedName~Dispose_Unsubscribes"
```

Expected: 1 passing (the scaffold from T3 already calls `_subscription.Dispose()`; `FakeNetworkScanService.Subscription.Dispose` already removes the subscriber).

- [ ] **Step 3: Run all tests**

```bash
dotnet test --nologo --verbosity quiet
```

Expected: 223/223 passing.

- [ ] **Step 4: Commit**

```bash
git add tests/ControlMenu.Tests/Services/ScanLifecycleHandlerTests.cs
git commit -m "test(scanner): assert handler Dispose unsubscribes from scan service"
```

---

## Task 10: Switch `DeviceManagement.razor` to use the handler

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor`

The moment of truth. Delete the duplicate scanner state + methods from the page; inject `IScanLifecycleHandler`; route all scanner UX through it. The inline Discovered block stays in place here (T11 extracts it). Quick Refresh changes from `_discovered = discovered;` to `Handler.ReplaceDiscovered(discovered);`.

- [ ] **Step 1: Swap `@inject` and `@implements`**

In `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor`, find:

```razor
@inject INetworkScanService ScanService
@implements IDisposable
```

Replace with:

```razor
@inject IScanLifecycleHandler Handler
```

(`IDisposable` goes away — the scoped DI container disposes the handler when the circuit ends.)

- [ ] **Step 2: Delete scanner fields and nested state**

Find the field block starting at roughly line 192 (after T1's deletion of the nested record):

```csharp
    private bool _messageIsError;
    private int _discoveryInterval = 300;
    private ScanPhase _phase = ScanPhase.Idle;
    private ScanProgressEvent? _lastProgress;
    private readonly HashSet<string> _dismissedAddresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _stashedNamesByMac = new(StringComparer.OrdinalIgnoreCase);
    private IDisposable? _scanSubscription;
```

Replace with:

```csharp
    private bool _messageIsError;
    private int _discoveryInterval = 300;
```

- [ ] **Step 3: Rewrite `OnInitializedAsync`**

Find:

```csharp
    protected override async Task OnInitializedAsync()
    {
        var interval = await Config.GetSettingAsync("discovery-interval");
        if (int.TryParse(interval, out var parsed))
            _discoveryInterval = parsed;
        await LoadDevices();
        _scanSubscription = ScanService.Subscribe(OnScanEvent);
        _phase = ScanService.Phase;
    }
```

Replace with:

```csharp
    protected override async Task OnInitializedAsync()
    {
        var interval = await Config.GetSettingAsync("discovery-interval");
        if (int.TryParse(interval, out var parsed))
            _discoveryInterval = parsed;
        await LoadDevices();
        Handler.OnStateChanged += HandleHandlerStateChanged;
    }

    private void HandleHandlerStateChanged()
    {
        _ = InvokeAsync(async () =>
        {
            var err = Handler.ConsumeLastError();
            if (err is not null)
                await ShowMessage(err, isError: true);
            StateHasChanged();
        });
    }
```

- [ ] **Step 4: Delete `Dispose`**

Remove:

```csharp
    public void Dispose()
    {
        _scanSubscription?.Dispose();
    }
```

The handler disposes itself via the scoped DI lifetime.

- [ ] **Step 5: Rebind chip row markup**

Find:

```razor
@if (_phase is not ScanPhase.Idle)
{
    <div class="scan-row">
        <ScanProgressChip Phase="_phase"
                          Checked="_lastProgress?.Checked ?? 0"
                          Total="_lastProgress?.Total ?? 0"
                          FoundSoFar="_discovered.Count" />
        @if (_phase is ScanPhase.Scanning or ScanPhase.Draining)
        {
            <button class="btn btn-warning btn-sm" @onclick="CancelScan">Cancel</button>
        }
    </div>
}
```

Replace with:

```razor
@if (Handler.Phase is not ScanPhase.Idle)
{
    <div class="scan-row">
        <ScanProgressChip Phase="Handler.Phase"
                          Checked="Handler.LastProgress?.Checked ?? 0"
                          Total="Handler.LastProgress?.Total ?? 0"
                          FoundSoFar="Handler.Discovered.Count" />
        @if (Handler.Phase is ScanPhase.Scanning or ScanPhase.Draining)
        {
            <button class="btn btn-warning btn-sm" @onclick="Handler.CancelScanAsync">Cancel</button>
        }
    </div>
}
```

- [ ] **Step 6: Rebind modal `ScanInProgress`**

Find:

```razor
@if (_showScanModal)
{
    <ScanNetworkModal OnStart="OnFullScanStart"
                      OnClose="CloseScanModal"
                      ScanInProgress="@(_phase is ScanPhase.Scanning or ScanPhase.Draining)" />
}
```

Replace with:

```razor
@if (_showScanModal)
{
    <ScanNetworkModal OnStart="OnFullScanStart"
                      OnClose="CloseScanModal"
                      ScanInProgress="@(Handler.Phase is ScanPhase.Scanning or ScanPhase.Draining)" />
}
```

- [ ] **Step 7: Rewrite the Discovered block to use handler state**

Find:

```razor
@if (_discovered.Count > 0)
{
    <div class="settings-section">
        <h2>Discovered on Network</h2>
        <p>ADB-advertising devices on the local network that aren't yet registered. ...</p>
        <table class="data-table">
            ...
            <tbody>
                @foreach (var d in _discovered)
                {
                    <tr>
                        ...
                        <td>@NameFor(d)</td>
                        ...
                        <td class="actions">
                            <button class="btn btn-primary btn-sm" @onclick="() => AddFromDiscovery(d)" disabled="@(d.Mac is null)">Add</button>
                            <button class="btn btn-secondary btn-sm" @onclick="() => DismissDiscovered(d)" title="Dismiss — remove from this list">×</button>
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    </div>
}
```

Replace `_discovered` with `Handler.Discovered` (two occurrences: the `@if` condition and the `@foreach`). Replace `() => DismissDiscovered(d)` with `() => Handler.Dismiss(d)`. `NameFor` and `AddFromDiscovery` stay for now — they move in T11.

The block ends up as:

```razor
@if (Handler.Discovered.Count > 0)
{
    <div class="settings-section">
        <h2>Discovered on Network</h2>
        <p>ADB-advertising devices on the local network that aren't yet registered. Click Add — IP, port, MAC, suggested name, and device type are pre-filled from the scan and an ADB probe.</p>
        <table class="data-table">
            <thead>
                <tr>
                    <th>Service</th>
                    <th>Name</th>
                    <th>IP</th>
                    <th>ADB Port</th>
                    <th>MAC</th>
                    <th style="text-align:right;">Actions</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var d in Handler.Discovered)
                {
                    <tr>
                        <td>
                            <code>@d.ServiceName</code>
                            @if (!string.IsNullOrEmpty(d.Source))
                            {
                                <span class="source-badge" title="Discovery source">@d.Source</span>
                            }
                        </td>
                        <td>@NameFor(d)</td>
                        <td>@d.Ip</td>
                        <td>@d.Port</td>
                        <td><code>@(d.Mac ?? "—")</code></td>
                        <td class="actions">
                            <button class="btn btn-primary btn-sm" @onclick="() => AddFromDiscovery(d)" disabled="@(d.Mac is null)">Add</button>
                            <button class="btn btn-secondary btn-sm" @onclick="() => Handler.Dismiss(d)" title="Dismiss — remove from this list">×</button>
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    </div>
}
```

- [ ] **Step 8: Rewrite `OnFullScanStart`**

Find:

```csharp
    private async Task OnFullScanStart(IReadOnlyList<ParsedSubnet> subnets)
    {
        _discovered.Clear();
        _dismissedAddresses.Clear();
        _stashedNamesByMac.Clear();
        await ScanService.StartScanAsync(subnets);
    }
```

Replace with:

```csharp
    private Task OnFullScanStart(IReadOnlyList<ParsedSubnet> subnets)
        => Handler.StartFullScanAsync(subnets);
```

- [ ] **Step 9: Delete scanner methods moved to the handler**

Delete the following entire methods from `DeviceManagement.razor`:

- `OnScanEvent(ScanEvent evt)`
- `FinalizeScanAsync()`
- `DetermineAdbMergeCandidatesAsync()`
- `BuildArpMapWithPingsAsync(IReadOnlyList<string>)`
- `EnrichDiscoveredMacs(IReadOnlyDictionary<string, string>)`
- `AppendAdbMergeRows((IReadOnlyList<(string, int)>, IReadOnlyDictionary<string, string>))`
- `PopulateStashedNamesAsync()`
- `CancelScan()`
- `DismissDiscovered(DiscoveredDevice)`

**Keep** `BuildArpMapAsync()` for now — `QuickRefresh` still uses it. (It's a thin wrapper; moving it is a cleanup for another day.)

- [ ] **Step 10: Rewire `QuickRefresh`**

Find the tail of `QuickRefresh`:

```csharp
        _discovered = discovered;
        await LoadDevices();
        _scanning = false;
```

Replace with:

```csharp
        Handler.ReplaceDiscovered(discovered);
        await LoadDevices();
        _scanning = false;
```

- [ ] **Step 11: Rewire `SaveDevice`'s discovered-list filter**

Find the block in `SaveDevice` (around line 286):

```csharp
            if (!isEdit && !string.IsNullOrEmpty(_formDevice.MacAddress))
            {
                _discovered = _discovered
                    .Where(disc => !string.Equals(disc.Mac, _formDevice.MacAddress, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
```

Replace with:

```csharp
            if (!isEdit && !string.IsNullOrEmpty(_formDevice.MacAddress))
            {
                Handler.ReplaceDiscovered(Handler.Discovered
                    .Where(disc => !string.Equals(disc.Mac, _formDevice.MacAddress, StringComparison.OrdinalIgnoreCase)));
            }
```

- [ ] **Step 12: Build**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj --nologo -c Release
```

Expected: 0 Warning(s), 0 Error(s). If the compiler flags unused fields or methods, re-check Step 2 and Step 9 deletions.

- [ ] **Step 13: Run tests**

```bash
dotnet test --nologo --verbosity quiet
```

Expected: 223/223 passing.

- [ ] **Step 14: Manual smoke — scan still works end-to-end**

Start the app:

```bash
dotnet run --project src/ControlMenu/ControlMenu.csproj -c Release --no-build
```

Navigate to Settings › Devices. Open the Scan Network modal, enter a trivial subnet (e.g. `127.0.0.1/32`), click Start. The modal should close; the chip should appear on the page; the scan should complete. Dismiss a row; it should stay gone. Run Quick Refresh; it should replace the Discovered list.

If the smoke fails, the handler isn't hooked up correctly — debug with the browser console + Control Menu's log output before committing.

- [ ] **Step 15: Commit**

```bash
git add src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor
git commit -m "refactor(scanner): DeviceManagement delegates scanner state to handler

Page injects IScanLifecycleHandler instead of INetworkScanService. The
120 lines of scan event dispatch + finalize orchestration, plus the
five helper methods, all move out. Chip + Discovered block now bind to
Handler.Phase / Handler.LastProgress / Handler.Discovered. Quick Refresh
ends with Handler.ReplaceDiscovered(newList) instead of mutating a page
field. DismissDiscovered button wires directly to Handler.Dismiss.

OnStateChanged is marshaled onto the UI thread via InvokeAsync inside
HandleHandlerStateChanged, which also drains Handler.ConsumeLastError()
into the existing toast channel.

The Discovered block remains inline in the page — T11 carves it into
DiscoveredPanel.razor."
```

---

## Task 11: Carve `DiscoveredPanel.razor`

**Files:**
- Create: `src/ControlMenu/Components/Shared/Scanner/DiscoveredPanel.razor`
- Modify: `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor`

Mechanical markup move. Panel takes the Discovered list + stashed names + registered devices as parameters; exposes `OnAdd` and `OnDismiss` callbacks. `NameFor` moves with it.

- [ ] **Step 1: Create the panel component**

Write `src/ControlMenu/Components/Shared/Scanner/DiscoveredPanel.razor`:

```razor
@using ControlMenu.Data.Entities
@using ControlMenu.Services.Network

@if (Discovered.Count > 0)
{
    <div class="settings-section">
        <h2>Discovered on Network</h2>
        <p>ADB-advertising devices on the local network that aren't yet registered. Click Add — IP, port, MAC, suggested name, and device type are pre-filled from the scan and an ADB probe.</p>
        <table class="data-table">
            <thead>
                <tr>
                    <th>Service</th>
                    <th>Name</th>
                    <th>IP</th>
                    <th>ADB Port</th>
                    <th>MAC</th>
                    <th style="text-align:right;">Actions</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var d in Discovered)
                {
                    <tr>
                        <td>
                            <code>@d.ServiceName</code>
                            @if (!string.IsNullOrEmpty(d.Source))
                            {
                                <span class="source-badge" title="Discovery source">@d.Source</span>
                            }
                        </td>
                        <td>@NameFor(d)</td>
                        <td>@d.Ip</td>
                        <td>@d.Port</td>
                        <td><code>@(d.Mac ?? "—")</code></td>
                        <td class="actions">
                            <button class="btn btn-primary btn-sm" @onclick="() => OnAdd.InvokeAsync(d)" disabled="@(d.Mac is null)">Add</button>
                            <button class="btn btn-secondary btn-sm" @onclick="() => OnDismiss.InvokeAsync(d)" title="Dismiss — remove from this list">×</button>
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    </div>
}

@code {
    [Parameter, EditorRequired] public IReadOnlyList<DiscoveredDevice> Discovered { get; set; } = default!;
    [Parameter, EditorRequired] public IReadOnlyDictionary<string, string> StashedNamesByMac { get; set; } = default!;
    [Parameter, EditorRequired] public IReadOnlyList<Device> Registered { get; set; } = default!;
    [Parameter] public EventCallback<DiscoveredDevice> OnAdd { get; set; }
    [Parameter] public EventCallback<DiscoveredDevice> OnDismiss { get; set; }

    // MAC-based lookup. Priority:
    //  1. Registered device — user has a live name.
    //  2. Stashed name — user deleted the device but kept the name for future rediscovery.
    //  3. "Unknown".
    private string NameFor(DiscoveredDevice d)
    {
        if (string.IsNullOrEmpty(d.Mac)) return "Unknown";
        var match = Registered.FirstOrDefault(dev =>
            string.Equals(dev.MacAddress, d.Mac, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match.Name;
        if (StashedNamesByMac.TryGetValue(d.Mac, out var stashed)) return stashed;
        return "Unknown";
    }
}
```

- [ ] **Step 2: Replace the inline block in `DeviceManagement.razor`**

In `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor`, find the entire block that starts with `@if (Handler.Discovered.Count > 0)` and spans through its closing `}` (~40 lines).

Replace with:

```razor
<DiscoveredPanel Discovered="Handler.Discovered"
                 StashedNamesByMac="Handler.StashedNamesByMac"
                 Registered="_devices"
                 OnAdd="AddFromDiscovery"
                 OnDismiss="Handler.Dismiss" />
```

- [ ] **Step 3: Delete `NameFor` from the page**

Remove:

```csharp
    // MAC-based lookup. Priority: ...
    private string NameFor(DiscoveredDevice d)
    {
        if (string.IsNullOrEmpty(d.Mac)) return "Unknown";
        var match = _devices.FirstOrDefault(dev =>
            string.Equals(dev.MacAddress, d.Mac, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match.Name;
        if (_stashedNamesByMac.TryGetValue(d.Mac, out var stashed)) return stashed;
        return "Unknown";
    }
```

- [ ] **Step 4: Build**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj --nologo -c Release
```

Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 5: Run tests**

```bash
dotnet test --nologo --verbosity quiet
```

Expected: 223/223 passing.

- [ ] **Step 6: Manual smoke — Discovered panel renders identically**

Run the app, trigger a scan, confirm the Discovered table renders with the same columns, names, Add/Dismiss buttons. Dismiss still works. Add still opens the prefilled Device form.

- [ ] **Step 7: Commit**

```bash
git add src/ControlMenu/Components/Shared/Scanner/DiscoveredPanel.razor src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor
git commit -m "refactor(scanner): carve DiscoveredPanel out of DeviceManagement

The ~40-line inline Discovered block becomes a reusable component under
Components/Shared/Scanner. Parameters: Discovered (handler-owned),
StashedNamesByMac (handler-owned), Registered (page-owned _devices),
plus OnAdd/OnDismiss callbacks. NameFor moves with the panel.

DeviceManagement.razor drops from ~420 to ~380 lines."
```

---

## Task 12: CHANGELOG update

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Append to the `[Unreleased] ### Changed` section**

Open `CHANGELOG.md`. Find the `## [Unreleased]` block and its `### Changed` subsection. Append:

```markdown
- **Scanner extraction** — The 120-line scan event dispatch + finalize orchestration that lived in `DeviceManagement.razor` moves into a new scoped service `ScanLifecycleHandler`, unit-testable without bUnit. The ~40-line Discovered panel markup moves into `DiscoveredPanel.razor`. Page responsibilities shrink to device CRUD, Quick Refresh, and modal wiring; handler owns Discovered / DismissedAddresses / StashedNamesByMac / Phase / LastProgress. Closes reviewer item I-1 (the `_discovered`-across-awaits race) — `AppendAdbMergeRows` now re-filters dismissed addresses at apply time, covering the window where a user dismisses a row during the ARP/ping finalize await.
```

- [ ] **Step 2: Commit**

```bash
git add CHANGELOG.md
git commit -m "docs(changelog): scanner extraction + I-1 race fix"
```

---

## Task 13: Manual QA

No code changes. User-driven. Verify the extraction did not regress the UX-redesign manual QA items, plus two new handler-specific checks.

- [ ] **QA-1: All 6 UX-redesign QA items (16-21 from `2026-04-21-scanner-ux-redesign.md`) still pass**

Run the manual test checklist. Flag any regressions immediately; extraction should be behavior-preserving.

- [ ] **QA-2 (new): Handler survives circuit refresh**

Open Control Menu, start a scan, watch the chip. Reload the browser tab mid-scan. After reconnect, the page should re-subscribe to the same running scan (singleton `INetworkScanService` broadcast) via a new scoped handler. Chip + hits reappear; final state settles correctly.

- [ ] **QA-3 (new): Quick Refresh + dismissed state**

Run a Full Scan. Dismiss one row. Run Quick Refresh. Dismissed addresses from the scan session are not cleared (`ReplaceDiscovered` doesn't touch `_dismissedAddresses`), so if Quick Refresh would surface the same IP:port it is not re-added. Verify by looking at the Discovered list after Quick Refresh — dismissed rows stay dismissed until the next Full Scan start.

- [ ] **Final: Mark the scanner-port memory done and close out the task list**

Update `project_control_menu_scanner_port.md` — move item 2 from **DEFERRED** to **SHIPPED** with the commit hash of T12's CHANGELOG commit. Update MEMORY.md's index line accordingly.

---

## Self-Review Checklist

Completed during plan authoring:

- **Spec coverage.**
  - Decision 1 (A1 ownership) — T3 (state fields), T4 (dismiss), T6 (start), T7 (replace). ✓
  - Decision 2 (R2 race) — T8 Step 3 `AppendAdbMergeRows` comment + T8 test 3. ✓
  - Decision 3 (D1 IDeviceService inject) — T3 constructor; T8 `DetermineAdbMergeCandidatesAsync` calls `_devices.GetAllDevicesAsync`. ✓
  - Decision 4 (scoped lifetime) — T3 Step 5 DI registration. ✓
  - Decision 5 (M1 surface) — T3 interface; T4/T6/T7/T8 methods. ✓
  - Decision 6 (handler Blazor-agnostic) — T3 raises event directly; T10 Step 3 `HandleHandlerStateChanged` marshals via `InvokeAsync`. ✓
  - Decision 7 (ConsumeLastError one-shot) — T3 interface + scaffold; T5 test; T10 Step 3 integration. ✓
  - Decision 8 (DiscoveredDevice location) — T1. ✓
  - All 12 tests from the spec test-list — T3 (1), T4 (2,3,5), T5 (4,11), T6 (6), T7 (7), T8 (8,9,10), T9 (12). ✓

- **Placeholder scan.** No "TBD" / "TODO" / "implement later". Each step shows concrete code or a concrete command. Two soft calls to re-verify external type shapes (T6 `ParsedSubnet` constructor, T8 `ArpEntry`) — these are guardrails, not placeholders; the engineer verifies and adjusts if needed.

- **Type consistency.** Handler members: `_discovered` / `_dismissedAddresses` / `_stashedNamesByMac` / `_phase` / `_lastProgress` / `_lastError` / `_subscription` used consistently across T3–T8. Public surface matches interface from T3. `DiscoveredDevice(ServiceName, Ip, Port, Mac, Source = null)` ctor shape consistent across T1, T4, T7, T8. `ScanMergeHelper.AddressKey(ip, port)` used everywhere a dismiss key is produced (T4, T8, T10). `OnStateChanged` / `ConsumeLastError()` / `Dismiss` / `ReplaceDiscovered` / `StartFullScanAsync` / `CancelScanAsync` names stable across interface, scaffold, tests, and page integration.

- **Task ordering.** T1 (types) → T2 (test double) → T3-T9 (handler built test-first, unused by page) → T10 (page switch in one commit) → T11 (panel carve) → T12 (docs) → T13 (QA). Each intermediate commit leaves the app working: T1-T9 don't touch the page's runtime behavior; T10 swaps state owner in a single atomic commit; T11 is a pure markup refactor.
