# Scanner UX Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move scan chip + live hits + dismiss buttons out of `ScanNetworkModal` onto the `DeviceManagement` page, following the ws-scrcpy-web UX; shrink the modal to a subnet-picker + Start trigger.

**Architecture:** `NetworkScanService` (singleton, unchanged) dispatches scan events to subscribers. Today the subscriber is the modal; after this plan the subscriber is the page. Modal shrinks to a thin wrapper over subnet management that emits `OnStart(subnets)` and closes. Per-row dismiss added to Discovered.

**Tech Stack:** Blazor Server (.NET 9), xUnit, Moq for tests. Existing codebase patterns: `.dialog-overlay` modals, `IConfigurationService` for settings, `AdbService` for `adb devices`, `NetworkScanService` + `ScanMergeHelper` for scanner logic.

**Spec:** `docs/superpowers/specs/2026-04-21-scanner-ux-redesign.md` (commit `b7a4aa9`).

**Branch:** `feat/scanner-port` (same worktree used throughout T14–T21 + post-QA fixes). HEAD at plan-time is `b7a4aa9`.

**Working directory:** `C:/Users/jscha/source/repos/tools-menu/.worktrees/scanner-port`

**Verification baseline:** 209 tests passing.

---

## File Structure

### Files to modify

| File | What changes |
|---|---|
| `src/ControlMenu/Services/Network/ScanMergeHelper.cs` | Add `FilterDismissed` pure function |
| `tests/ControlMenu.Tests/Services/ScanMergeHelperTests.cs` | Add 3 tests for `FilterDismissed` |
| `src/ControlMenu/Components/Shared/Scanner/ScanNetworkModal.razor` | Task 2 adds `OnStart` + `ScanInProgress` parameters. Task 4 strips chip, hit table, Cancel button, subscription, old callbacks. |
| `src/ControlMenu/Components/Shared/Scanner/ScanNetworkModal.razor.css` | Task 4 removes rules for hit table / source-tag (moved to page context) |
| `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor` | Task 3 gains `INetworkScanService` subscription, phase state, dismissed-address set, chip row, live hit append, dismiss button, in-handler adb-merge. Task 4 cleans up now-dead `AddFromScan` / `OnFullScanComplete` wrappers. |
| `src/ControlMenu/Components/Shared/Scanner/ScanProgressChip.razor.css` | Task 5 migrates palette from hardcoded hex to Control Menu CSS tokens |
| `CHANGELOG.md` | Task 6 adds a `### Changed` bullet under `[Unreleased]` |

### No new files

`FilterDismissed` is additive in an existing helper class. Chip component already exists.

---

## Task Ordering Rationale

Ordered so every commit leaves the app in a working state:

1. **T1 (helper)** — foundation; later tasks depend on `FilterDismissed`.
2. **T2 (modal adds OnStart + ScanInProgress)** — modal now emits the signal the page needs, but still has its own chip/hits in parallel. Nothing breaks — the existing `OnScanComplete` callback still fires too.
3. **T3 (page migration)** — page subscribes, renders chip, streams hits, dismisses, runs adb-merge. Modal still does the same in parallel (duplicate UI on the screen, but both work). Wires modal `OnStart` to a new page handler.
4. **T4 (modal cleanup)** — now that the page owns everything, strip the modal's subscription, chip, hit table, Cancel, and unused parameters. Also removes the now-dead `AddFromScan` / `OnFullScanComplete` from the page.
5. **T5 (chip palette)** — final chip location known, swap in theme tokens.
6. **T6 (CHANGELOG)** — doc.
7. **T7 (manual QA)** — user-driven; 6 new checklist items.

---

## Task 1: `ScanMergeHelper.FilterDismissed` + tests

**Files:**
- Modify: `src/ControlMenu/Services/Network/ScanMergeHelper.cs`
- Modify: `tests/ControlMenu.Tests/Services/ScanMergeHelperTests.cs`

A pure function that filters a sequence of `ScanHit` down to hits whose `Address` is not in a supplied dismissed set. Used in Task 3 to skip live hits and adb-merge rows the user has dismissed.

- [ ] **Step 1: Write the failing tests**

Append to `tests/ControlMenu.Tests/Services/ScanMergeHelperTests.cs`:

```csharp
    [Fact]
    public void FilterDismissed_RemovesHitsWithMatchingAddress()
    {
        var hits = new[]
        {
            new ScanHit(DiscoverySource.Mdns, "192.168.1.10:5555", "s1", "n1", "", null),
            new ScanHit(DiscoverySource.Tcp,  "192.168.1.20:5555", "s2", "n2", "", null),
            new ScanHit(DiscoverySource.Mdns, "192.168.1.30:5555", "s3", "n3", "", null),
        };
        var dismissed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "192.168.1.20:5555" };
        var result = ScanMergeHelper.FilterDismissed(hits, dismissed);
        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, h => h.Address == "192.168.1.20:5555");
    }

    [Fact]
    public void FilterDismissed_CaseInsensitiveAddressMatch()
    {
        // Addresses are ip:port strings — numeric — so mixed case doesn't occur
        // naturally, but the set uses OrdinalIgnoreCase defensively. Verify.
        var hits = new[] { new ScanHit(DiscoverySource.Mdns, "192.168.1.10:5555", "", "", "", null) };
        var dismissed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "192.168.1.10:5555" };
        var result = ScanMergeHelper.FilterDismissed(hits, dismissed);
        Assert.Empty(result);
    }

    [Fact]
    public void FilterDismissed_EmptyDismissed_ReturnsAllHits()
    {
        var hits = new[]
        {
            new ScanHit(DiscoverySource.Mdns, "192.168.1.10:5555", "", "", "", null),
            new ScanHit(DiscoverySource.Tcp,  "192.168.1.20:5555", "", "", "", null),
        };
        var result = ScanMergeHelper.FilterDismissed(hits, new HashSet<string>());
        Assert.Equal(2, result.Count);
    }
```

- [ ] **Step 2: Run tests, confirm they fail**

```
cd C:/Users/jscha/source/repos/tools-menu/.worktrees/scanner-port
dotnet test --nologo --verbosity quiet --filter "FullyQualifiedName~FilterDismissed"
```

Expected: compile error or test-not-found (`FilterDismissed` doesn't exist yet).

- [ ] **Step 3: Implement `FilterDismissed`**

Add to `src/ControlMenu/Services/Network/ScanMergeHelper.cs`, inside the `ScanMergeHelper` static class:

```csharp
    /// <summary>
    /// Returns hits whose <c>Address</c> is not in <paramref name="dismissedAddresses"/>.
    /// Used by the Discovered panel to honor per-row dismissals for the remainder
    /// of a scan session (live stream + adb-merge).
    /// </summary>
    public static IReadOnlyList<ScanHit> FilterDismissed(
        IEnumerable<ScanHit> hits,
        ISet<string> dismissedAddresses)
    {
        var result = new List<ScanHit>();
        foreach (var hit in hits)
        {
            if (!dismissedAddresses.Contains(hit.Address))
                result.Add(hit);
        }
        return result;
    }
```

- [ ] **Step 4: Run all tests, confirm green**

```
dotnet test --nologo --verbosity quiet
```

Expected: all tests pass (should show 212/212 — prior 209 + 3 new).

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Services/Network/ScanMergeHelper.cs tests/ControlMenu.Tests/Services/ScanMergeHelperTests.cs
git commit -m "feat(scanner): ScanMergeHelper.FilterDismissed for per-row dismiss"
```

---

## Task 2: Modal emits `OnStart`; accepts `ScanInProgress`

**Files:**
- Modify: `src/ControlMenu/Components/Shared/Scanner/ScanNetworkModal.razor`

The modal adds two new contract pieces without removing anything existing. This is a pure additive commit so the app keeps working while Task 3 migrates responsibility.

- [ ] **Step 1: Add the two new parameters**

Find the existing `@code { ... [Parameter] public EventCallback<ScanHit> OnAddHit ... }` block. Add these parameters alongside the existing ones (keep `OnAddHit`, `OnScanComplete`, `OnClose` for now):

```csharp
    [Parameter] public EventCallback<IReadOnlyList<ParsedSubnet>> OnStart { get; set; }
    [Parameter] public bool ScanInProgress { get; set; }
```

- [ ] **Step 2: Rename modal's internal Start handlers to invoke `OnStart` then close**

Find the existing `StartClicked()` and `StartConfirmed()` methods in the `@code` block. Modify them so that after calling `ScanService.StartScanAsync`, they also invoke `OnStart(subnets)` and then close the modal. Replace:

```csharp
    private void StartClicked()
    {
        var total = _subnets.Sum(s => s.HostCount);
        if (total > 2048)
        {
            _showLargeWarn = true;
        }
        else
        {
            _ = ScanService.StartScanAsync(_subnets);
        }
    }

    private Task StartConfirmed()
    {
        _showLargeWarn = false;
        return ScanService.StartScanAsync(_subnets);
    }
```

with:

```csharp
    private async Task StartClicked()
    {
        var total = _subnets.Sum(s => s.HostCount);
        if (total > 2048)
        {
            _showLargeWarn = true;
            return;
        }
        await OnStart.InvokeAsync(_subnets);
        await Close();
    }

    private async Task StartConfirmed()
    {
        _showLargeWarn = false;
        await OnStart.InvokeAsync(_subnets);
        await Close();
    }
```

Note: the body no longer calls `ScanService.StartScanAsync` — the page does that now from its `OnFullScanStart` handler. The modal's job is just to fire the `OnStart` callback and close.

- [ ] **Step 3: Disable Start button when a scan is in progress**

Find the Start button markup:

```razor
                <button class="btn btn-primary" disabled="@(_subnets.Count == 0)" @onclick="StartClicked">Start</button>
```

Replace with:

```razor
                <button class="btn btn-primary"
                        disabled="@(_subnets.Count == 0 || ScanInProgress)"
                        @onclick="StartClicked">
                    @(ScanInProgress ? "Scan in Progress…" : "Start")
                </button>
```

- [ ] **Step 4: Build**

```
dotnet build src/ControlMenu/ControlMenu.csproj --nologo -c Release
```

Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 5: Run tests**

```
dotnet test --nologo --verbosity quiet
```

Expected: 212/212 passing.

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Components/Shared/Scanner/ScanNetworkModal.razor
git commit -m "feat(scanner): modal emits OnStart and accepts ScanInProgress

Modal now fires OnStart(subnets) on the Start button click and closes
itself, instead of directly calling StartScanAsync. Parent page takes
over responsibility for owning the scan lifecycle.

Start button disables with a 'Scan in Progress...' label while parent
reports ScanInProgress=true.

Existing OnAddHit / OnScanComplete / OnClose callbacks remain in place;
they will be removed in a later task once DeviceManagement no longer
depends on them."
```

---

## Task 3: `DeviceManagement` page takes over chip, live hits, dismiss, adb-merge

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor`

The biggest task. Page subscribes to `INetworkScanService`, renders chip between devices table and Discovered section, streams hits live into `_discovered`, handles dismiss, runs adb-merge on complete. Modal is still fully functional with its own chip/hits — we're intentionally running both for one commit to keep each change reviewable.

- [ ] **Step 1: Add `INetworkScanService` injection**

Find the existing `@inject` block at the top. Add:

```razor
@inject INetworkScanService ScanService
```

- [ ] **Step 2: Make the component implement `IDisposable` for subscription cleanup**

At the top of the file, directly below the existing `@inject` lines and before the markup, add:

```razor
@implements IDisposable
```

- [ ] **Step 3: Add new fields for phase, progress, dismissed, subscription**

Inside the `@code { }` block, add next to the existing field declarations:

```csharp
    private ScanPhase _phase = ScanPhase.Idle;
    private ScanProgressEvent? _lastProgress;
    private readonly HashSet<string> _dismissedAddresses = new(StringComparer.OrdinalIgnoreCase);
    private IDisposable? _scanSubscription;
```

- [ ] **Step 4: Subscribe in `OnInitializedAsync`**

Find the existing `OnInitializedAsync` method. Add at the END of the method, after the existing `await LoadDevices();` line:

```csharp
        _scanSubscription = ScanService.Subscribe(OnScanEvent);
        _phase = ScanService.Phase;
```

- [ ] **Step 5: Implement `Dispose()`**

Add to the `@code { }` block (anywhere convenient — put it near the bottom above the closing brace):

```csharp
    public void Dispose()
    {
        _scanSubscription?.Dispose();
    }
```

- [ ] **Step 6: Implement the `OnScanEvent` handler**

Add to the `@code { }` block:

```csharp
    private void OnScanEvent(ScanEvent evt)
    {
        InvokeAsync(async () =>
        {
            switch (evt)
            {
                case ScanStartedEvent:
                    _lastProgress = null;
                    break;
                case ScanProgressEvent p:
                    _lastProgress = p;
                    break;
                case ScanHitEvent h:
                    if (!_dismissedAddresses.Contains(h.Hit.Address))
                    {
                        var parts = h.Hit.Address.Split(':');
                        var ip = parts[0];
                        var port = parts.Length > 1 && int.TryParse(parts[1], out var pr) ? pr : 5555;
                        _discovered.Add(new DiscoveredDevice(h.Hit.Name, ip, port, h.Hit.Mac));
                    }
                    break;
                case ScanCompleteEvent:
                    await MergeAdbConnectedAsync();
                    break;
                case ScanCancelledEvent:
                    // Partial hits stay in place; no adb-merge on cancel.
                    break;
                case ScanErrorEvent err:
                    await ShowMessage($"Scan error: {err.Reason}", isError: true);
                    break;
            }
            _phase = ScanService.Phase;
            StateHasChanged();
        });
    }
```

Note: `_discovered` is a `List<DiscoveredDevice>` (already exists). The `DiscoveredDevice` record has an optional `Source` parameter (added in commit `cf6068e`), leave it null for scan-hit rows.

- [ ] **Step 7: Extract the adb-merge into its own method**

The current `OnFullScanComplete(IReadOnlyList<ScanHit> hits)` method contains adb-merge logic that we now need to call from `OnScanEvent`. Refactor: extract a new `MergeAdbConnectedAsync()` method that operates on the current `_discovered` state, respecting `_dismissedAddresses`.

Replace the existing `OnFullScanComplete` method entirely with:

```csharp
    // Called on ScanCompleteEvent. Runs adb devices locally, filters out:
    //  • IP:ports already registered as devices
    //  • IP:ports already present in _discovered (from live hits)
    //  • IP:ports the user has dismissed this scan session
    // Appends the survivors to _discovered with Source="adb".
    //
    // ws-scrcpy-web's NetworkScanner filters out adb-connected IP:ports from
    // its scan output, so any device already in adb devices (e.g. connected
    // via ws-scrcpy-web's own UI) is invisible to the scan. This merge
    // surfaces them for Add.
    private async Task MergeAdbConnectedAsync()
    {
        var registeredIpPorts = _devices
            .Where(d => !string.IsNullOrEmpty(d.LastKnownIp))
            .Select(d => $"{d.LastKnownIp}:{d.AdbPort}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var excludeIpPorts = _discovered
            .Select(d => $"{d.Ip}:{d.Port}")
            .Concat(registeredIpPorts)
            .Concat(_dismissedAddresses);

        var adbConnected = await AdbService.GetConnectedDevicesAsync();
        var fromAdb = ScanMergeHelper.FindUnregisteredAdbConnected(adbConnected, excludeIpPorts);

        // ARP-resolve MAC with ping fallback (mirrors Quick Refresh).
        var arpMap = await BuildArpMapAsync();
        var missing = fromAdb.Where(x => !arpMap.ContainsKey(x.Ip)).Select(x => x.Ip).ToList();
        if (missing.Count > 0)
        {
            await Task.WhenAll(missing.Select(ip => NetworkDiscovery.PingAsync(ip)));
            arpMap = await BuildArpMapAsync();
        }

        foreach (var x in fromAdb)
        {
            var mac = arpMap.TryGetValue(x.Ip, out var m) ? m : null;
            _discovered.Add(new DiscoveredDevice($"{x.Ip}:{x.Port}", x.Ip, x.Port, mac, Source: "adb"));
        }
    }
```

- [ ] **Step 8: Add the page-level scan start handler (invoked from modal)**

Add to `@code { }`:

```csharp
    // Modal fires this when the user clicks Start. Clears the Discovered panel
    // (Full Scan replaces semantics), clears dismissed addresses (new scan =
    // fresh intent), kicks off the scan. Events will flow back through
    // OnScanEvent from the service singleton.
    private async Task OnFullScanStart(IReadOnlyList<ParsedSubnet> subnets)
    {
        _discovered.Clear();
        _dismissedAddresses.Clear();
        await ScanService.StartScanAsync(subnets);
    }
```

- [ ] **Step 9: Wire modal's `OnStart` and `ScanInProgress`**

Find the existing modal invocation:

```razor
@if (_showScanModal)
{
    <ScanNetworkModal OnAddHit="AddFromScan"
                      OnScanComplete="OnFullScanComplete"
                      OnClose="CloseScanModal" />
}
```

Replace with:

```razor
@if (_showScanModal)
{
    <ScanNetworkModal OnStart="OnFullScanStart"
                      OnAddHit="AddFromScan"
                      OnScanComplete="OnFullScanComplete"
                      OnClose="CloseScanModal"
                      ScanInProgress="@(_phase is ScanPhase.Scanning or ScanPhase.Draining)" />
}
```

Keep the old callbacks connected for now — they'll be removed with the modal cleanup in Task 4.

Also delete the old `OnFullScanComplete` method declaration if the compiler now flags it as a duplicate or unused (Task 7's Step 7 already replaced its body — verify via `dotnet build` in the next step).

- [ ] **Step 10: Add the chip row markup**

Find the Device Management section closing tag. Just after the closing `</div>` of `<div class="settings-section">` (the one containing the devices table), and BEFORE the `@if (_discovered.Count > 0)` Discovered section, insert:

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

- [ ] **Step 11: Add the `CancelScan` handler**

Add to `@code { }`:

```csharp
    private Task CancelScan() => ScanService.CancelAsync();
```

- [ ] **Step 12: Add dismiss button to each Discovered row + handler**

Find the Discovered rows' actions cell:

```razor
                        <td class="actions">
                            <button class="btn btn-primary btn-sm" @onclick="() => AddFromDiscovery(d)" disabled="@(d.Mac is null)">Add</button>
                        </td>
```

Replace with:

```razor
                        <td class="actions">
                            <button class="btn btn-primary btn-sm" @onclick="() => AddFromDiscovery(d)" disabled="@(d.Mac is null)">Add</button>
                            <button class="btn btn-secondary btn-sm" @onclick="() => DismissDiscovered(d)" title="Dismiss — remove from this list">×</button>
                        </td>
```

Add the handler to `@code { }`:

```csharp
    private void DismissDiscovered(DiscoveredDevice d)
    {
        _discovered.Remove(d);
        _dismissedAddresses.Add($"{d.Ip}:{d.Port}");
    }
```

- [ ] **Step 13: Add CSS for `.scan-row`**

Find the `.source-badge` rule in `src/ControlMenu/wwwroot/css/app.css` (added in commit `cf6068e`). Add just after it:

```css
.scan-row { display: flex; align-items: center; gap: 12px; margin: 1rem 0; padding: 0.5rem 0; }
```

- [ ] **Step 14: Build**

```
dotnet build src/ControlMenu/ControlMenu.csproj --nologo -c Release
```

Expected: 0 Warning(s), 0 Error(s).

If the compiler complains about a duplicate `OnFullScanComplete` method declaration, delete the remaining leftover.

- [ ] **Step 15: Run tests**

```
dotnet test --nologo --verbosity quiet
```

Expected: 212/212 passing.

- [ ] **Step 16: Commit**

```bash
git add src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor src/ControlMenu/wwwroot/css/app.css
git commit -m "feat(scanner): page subscribes to scanner; chip + live hits + dismiss on page

DeviceManagement now subscribes to INetworkScanService and handles scan
events directly: appends live hits into _discovered (respecting a new
_dismissedAddresses set), updates the chip state, and runs the adb-merge
on ScanCompleteEvent via the new MergeAdbConnectedAsync helper.

The chip is rendered in a new .scan-row between the devices table and
the Discovered section, with a Cancel button beside it while scanning.

Each Discovered row gains a × Dismiss button that removes the row and
records its address so live or merged re-hits are skipped for the rest
of the session. Dismissed state clears on the next Full Scan start.

Modal still has its own chip/hits running in parallel — duplicate UI
during this transition commit by design. Task 4 will strip the modal.

OnFullScanStart is the new entry point the modal calls on Start click.
It clears the Discovered panel and the dismissed set (new-scan = fresh
intent), then invokes StartScanAsync. The previous OnFullScanComplete
modal-callback glue is still wired for compatibility — removed in T4."
```

---

## Task 4: Modal cleanup — strip chip, hits, Cancel, and old callbacks

**Files:**
- Modify: `src/ControlMenu/Components/Shared/Scanner/ScanNetworkModal.razor`
- Modify: `src/ControlMenu/Components/Shared/Scanner/ScanNetworkModal.razor.css`
- Modify: `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor`

Now that the page handles everything, strip the modal down to subnet management + Start. Remove the parallel chip, hit table, Cancel button, `INetworkScanService` subscription, and the now-dead `OnAddHit` / `OnScanComplete` callbacks.

Also remove the now-dead `AddFromScan` and `OnFullScanComplete` methods from DeviceManagement (they were only called from modal callbacks).

- [ ] **Step 1: Remove modal subscription and related fields**

In `src/ControlMenu/Components/Shared/Scanner/ScanNetworkModal.razor`, remove all of:

```csharp
    private ScanPhase _phase = ScanPhase.Idle;
    private ScanProgressEvent? _lastProgress;
    private readonly List<ScanHit> _hits = new();
    private IDisposable? _subscription;
```

Also remove:

```csharp
    [Parameter] public EventCallback<ScanHit> OnAddHit { get; set; }
    [Parameter] public EventCallback<IReadOnlyList<ScanHit>> OnScanComplete { get; set; }
```

Keep `OnStart`, `OnClose`, `ScanInProgress`.

- [ ] **Step 2: Remove subscription setup from `OnInitializedAsync`**

Find:

```csharp
    protected override async Task OnInitializedAsync()
    {
        _subscription = ScanService.Subscribe(OnScanEvent);
        _phase = ScanService.Phase;
        _hits.AddRange(ScanService.Hits);

        await LoadSubnetsAsync();
        _detected = await DetectionClient.DetectAsync();
    }
```

Replace with:

```csharp
    protected override async Task OnInitializedAsync()
    {
        await LoadSubnetsAsync();
        _detected = await DetectionClient.DetectAsync();
    }
```

- [ ] **Step 3: Remove `OnScanEvent` method and `DisposeAsync`**

Delete the entire `private void OnScanEvent(ScanEvent evt)` method body and the `public ValueTask DisposeAsync()` method. Also remove `@implements IAsyncDisposable` at the top of the file.

- [ ] **Step 4: Remove `AddHit`, `Cancel` methods**

Delete these methods entirely:

```csharp
    private Task Cancel() => ScanService.CancelAsync();

    private async Task AddHit(ScanHit hit)
    {
        await OnAddHit.InvokeAsync(hit);
        await Close();
    }
```

- [ ] **Step 5: Remove chip, hit table, and Cancel button from markup**

Remove these markup blocks from the modal:

- The `<ScanProgressChip ... />` element in the header
- The entire `<div class="hits-area"> ... </div>` including the hit table and "Click Start to begin scanning" empty state
- The `@if (_phase is ScanPhase.Scanning or ScanPhase.Draining) { <button @onclick="Cancel"> ... } else { ... }` branching in the dialog-actions — replace with just the Start button (now controlled by `ScanInProgress`)

The resulting `<div class="dialog-body">` should contain only the `<div class="subnet-list">` with subnets UI. The `<div class="dialog-actions">` should contain only `[Close]` and `[Start]` (no Cancel button, no ternary).

- [ ] **Step 6: Remove the no-longer-used `@inject INetworkScanService ScanService`**

Delete the `@inject INetworkScanService ScanService` line at the top of the modal file. (Start click no longer calls it.)

- [ ] **Step 7: Remove modal's unused CSS rules**

In `src/ControlMenu/Components/Shared/Scanner/ScanNetworkModal.razor.css`, remove:
- The `.dialog-header` (chip lived inside it — header now just has title + close)
- `.dialog-body` if only used by the now-removed hits-area
- `.hits-area` rules
- `.source-tag` and `.source-tag.mdns` / `.source-tag.tcp` rules
- `.dialog-large` and `.btn-close` rules if they only applied to the expanded modal — keep `.btn-close` if the modal still renders an `×` close button in the header
- `.btn-link` and `.subnet-row` rules — KEEP, the subnet picker still uses them
- `.subnet-list` — KEEP

Best-effort: delete rules you're confident belonged to the removed content. If unsure, leave them — dead CSS is harmless.

- [ ] **Step 8: In DeviceManagement, remove the now-dead modal callback wiring**

Find the modal invocation (updated in Task 3 Step 9):

```razor
@if (_showScanModal)
{
    <ScanNetworkModal OnStart="OnFullScanStart"
                      OnAddHit="AddFromScan"
                      OnScanComplete="OnFullScanComplete"
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
                      ScanInProgress="@(_phase is ScanPhase.Scanning or ScanPhase.Draining)" />
}
```

- [ ] **Step 9: In DeviceManagement, delete the now-dead handler methods**

Remove these methods from `DeviceManagement.razor`'s `@code { }` block:

```csharp
    private async Task AddFromScan(ScanHit hit)
    {
        _showScanModal = false;
        var parts = hit.Address.Split(':');
        var ip = parts[0];
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 5555;
        await AddFromDiscovery(new DiscoveredDevice(hit.Name, ip, port, hit.Mac));
    }
```

And (if still present from before Task 3 Step 7):

```csharp
    private async Task OnFullScanComplete(IReadOnlyList<ScanHit> hits) { ... }
```

- [ ] **Step 10: Build**

```
dotnet build src/ControlMenu/ControlMenu.csproj --nologo -c Release
```

Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 11: Run tests**

```
dotnet test --nologo --verbosity quiet
```

Expected: 212/212 passing.

- [ ] **Step 12: Commit**

```bash
git add src/ControlMenu/Components/Shared/Scanner/ScanNetworkModal.razor src/ControlMenu/Components/Shared/Scanner/ScanNetworkModal.razor.css src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor
git commit -m "refactor(scanner): strip modal of chip, hits, cancel — page owns them now

Modal is now a thin subnet picker: subnet list + add/remove + syntax
help + Start + Close. Removes its INetworkScanService subscription,
ScanProgressChip instance, live hit table, Cancel button, and the
OnAddHit / OnScanComplete event callbacks that nobody emits to.

DeviceManagement drops the now-dead AddFromScan and OnFullScanComplete
handlers that only existed to bridge modal callbacks to the page state."
```

---

## Task 5: Chip palette migrated to theme tokens

**Files:**
- Modify: `src/ControlMenu/Components/Shared/Scanner/ScanProgressChip.razor.css`

Chip now renders on page background, which differs from the modal background it was designed against. QA flagged blue-on-dark as hard to read. Swap hardcoded hex to Control Menu's existing theme tokens so both dark and light themes render well.

- [ ] **Step 1: Replace the four state blocks**

Open `src/ControlMenu/Components/Shared/Scanner/ScanProgressChip.razor.css`. Replace the four state rule blocks with:

```css
.scan-chip.scanning {
    background: color-mix(in srgb, var(--info-color, #2563eb) 15%, transparent);
    color: var(--info-color, #2563eb);
}

.scan-chip.draining {
    background: color-mix(in srgb, var(--warning-color, #d97706) 15%, transparent);
    color: var(--warning-color, #d97706);
}

.scan-chip.complete {
    background: color-mix(in srgb, var(--success-color, #16a34a) 15%, transparent);
    color: var(--success-color, #16a34a);
}

.scan-chip.cancelled {
    background: color-mix(in srgb, var(--danger-color, #dc2626) 15%, transparent);
    color: var(--danger-color, #dc2626);
}
```

The `color-mix` function is supported in all modern evergreen browsers (Chrome 111+, Edge 111+, Safari 16.2+, Firefox 113+). Fallback color via CSS var fallback covers older engines.

- [ ] **Step 2: Build**

```
dotnet build src/ControlMenu/ControlMenu.csproj --nologo -c Release
```

Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 3: Manual smoke — check chip legibility in both themes**

Restart the app (`dotnet run --project src/ControlMenu/ControlMenu.csproj -c Release --no-build`).

Navigate to Settings › Devices, kick off a Scan Network on a trivial subnet (e.g. `127.0.0.1/32`). While chip is visible, toggle theme via the sun/moon icon in the top bar. Both should render legibly. If not, go back and introduce explicit `--*-color-soft` vars in `app.css` with theme-specific values — but `color-mix` with 15% should be fine on both.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Components/Shared/Scanner/ScanProgressChip.razor.css
git commit -m "style(scanner): chip palette uses theme tokens for dark/light legibility

Hardcoded blue/amber/green/red hex values are replaced with color-mix
over Control Menu's --info-color, --warning-color, --success-color,
--danger-color vars so the chip renders correctly on both theme
backgrounds. Hex fallbacks preserve the original palette on engines
without color-mix support."
```

---

## Task 6: CHANGELOG update

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Append to the existing `### Changed` section under `[Unreleased]`**

Find the existing `## [Unreleased]` block and its `### Changed` subsection. Append these bullets:

```markdown
- **Scan UX redesign** — `Scan Network…` modal shrinks to a subnet picker + Start button. Chip, live hit stream, Cancel button, and per-row Add buttons now live on the `Settings › Devices` page between the registered-devices table and the Discovered section. Each Discovered row gains a `×` Dismiss button to clear unwanted entries without a fresh scan. Opening the modal during an active scan disables Start (label: "Scan in Progress…") but allows editing the subnet list for the next run.
- **Chip palette theme-aware** — `ScanProgressChip` swaps hardcoded hex for `color-mix` over `--info-color` / `--warning-color` / `--success-color` / `--danger-color` tokens so both dark and light themes render legibly now that the chip lives on the page background.
```

- [ ] **Step 2: Commit**

```bash
git add CHANGELOG.md
git commit -m "docs(changelog): scanner UX redesign + chip palette tokens"
```

---

## Task 7: Manual QA — 6 new checklist items

No code changes. User-driven. Add these items to the manual QA run covered by T21:

- [ ] **16. Modal Start → close → page chip + live hits**

Open Scan Network…, add a subnet, click Start. Modal closes immediately. Chip appears on the Device Management page in its own row between the devices table and Discovered section. Live hits stream in; counter increments.

- [ ] **17. Per-row Dismiss**

During or after a scan, click the `×` button on any Discovered row. Row disappears. If the same address is re-emitted (e.g., live-refresh, adb-merge at completion, or running scan mid-stream), the row does NOT reappear — the dismiss sticks for the remainder of the scan session.

- [ ] **18. Modal during active scan**

Start a scan. While it's running (chip visible), click Scan Network… again. Modal opens. Subnet list is fully editable (can add/remove). Start button is disabled and reads "Scan in Progress…". Close the modal. Chip still on page; scan continues.

- [ ] **19. Page Cancel → draining → cancelled**

Start a scan. Click Cancel next to chip. Chip transitions Scanning → Draining → Cancelled. No adb-merge runs on cancel — partial hits remain in Discovered without adb-sourced rows appended. Chip auto-hides after 10s.

- [ ] **20. Second-tab spectator**

Open Control Menu in a second browser tab. Navigate to Settings › Devices. Starting a scan in tab A shows chip + live hits in BOTH tabs. Cancel from tab B cancels the shared scan, tab A sees the cancellation.

Note: dismissed-address state is per-tab; if you dismiss a row in tab A it does NOT auto-dismiss in tab B (documented limitation).

- [ ] **21. Chip legibility — dark and light themes**

During a scan, toggle theme via the sun/moon icon in the top bar. The chip should remain legible in both — Scanning (info blue), Draining (warning amber), Complete (success green), Cancelled (danger red). No eye-strain on either theme.

- [ ] **Final commit (user)**

```bash
git commit -am "test(scanner): manual QA pass — UX redesign items 16-21"
```

---

## Self-Review Checklist

Completed during plan authoring:

- **Spec coverage:** Every numbered decision in spec §2 maps to a task. Quick Refresh preservation — no-op (Task 3 doesn't touch Quick Refresh). Live streaming — Task 3 Step 6. Chip placement — Task 3 Step 10. Modal behavior during scan — Task 2 Step 3. Per-row dismiss — Task 3 Steps 12 + Task 1. Subnet state — unchanged (Task 2/4 preserve modal-local). Dismissed reset — Task 3 Step 8. ✓
- **Placeholder scan:** No TBDs; every step shows code or exact command. Task 4 Step 7 says "delete rules you're confident belonged to removed content" — that's a judgment call not a placeholder; the alternative (exhaustively listing every CSS rule) isn't worth the weight and the impact of leaving dead CSS is zero.
- **Type consistency:** `DiscoveredDevice(ServiceName, Ip, Port, Mac, Source = null)` — matches commit `cf6068e`. `ScanHit.Address` / `.Mac` / `.Name` / `.Source` — matches ScanHit.cs. `_discovered`, `_dismissedAddresses`, `_phase`, `_lastProgress`, `_scanSubscription` — consistent across Task 3 and Task 4. `ScanMergeHelper.FilterDismissed(IEnumerable<ScanHit>, ISet<string>)` — signature used in Task 1 (definition) and implicitly available for future wiring; note Task 3's `OnScanEvent` handles dismissed filtering inline rather than via `FilterDismissed` because hits arrive one-at-a-time (no collection to filter). `FilterDismissed` is still worth having for tests + future use; flagged here so the engineer understands why the helper exists but isn't invoked from production code.
- **Task ordering:** T1 (helper) — T2 (modal additive) — T3 (page migration, duplicate UI acceptable for one commit) — T4 (modal strip + page dead-code cleanup) — T5 (palette) — T6 (docs) — T7 (QA). Each commit leaves the app in a working state.
