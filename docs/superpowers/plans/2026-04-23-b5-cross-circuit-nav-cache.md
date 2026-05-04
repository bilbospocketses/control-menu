# B-5 Cross-Circuit Nav Cache Desync — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix cross-circuit cache invalidation so sidebar device-type counts and presence-watcher redirects update in all open browser tabs, not just the tab that mutated devices.

**Architecture:** Introduce a singleton `IDeviceChangeNotifier` pub/sub service. DeviceService calls `NotifyChanged()` on every mutation. DeviceTypeCache and DeviceTypePresenceWatcher subscribe to the notifier instead of the scoped `IDeviceService.DevicesChanged` event, which gets removed entirely.

**Tech Stack:** .NET 9, Blazor Server, xUnit

**Spec:** `docs/superpowers/specs/2026-04-23-b5-cross-circuit-nav-cache-design.md`

**Baseline:** 245 tests passing, zero warnings.

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `src/ControlMenu/Services/IDeviceChangeNotifier.cs` | **Create** | Singleton pub/sub interface |
| `src/ControlMenu/Services/DeviceChangeNotifier.cs` | **Create** | Implementation |
| `tests/ControlMenu.Tests/Services/Fakes/FakeDeviceChangeNotifier.cs` | **Create** | Test fake with `TriggerChanged()` |
| `tests/ControlMenu.Tests/Services/DeviceChangeNotifierTests.cs` | **Create** | Pub/sub unit tests |
| `src/ControlMenu/Services/IDeviceService.cs` | Modify | Remove `event Action DevicesChanged` |
| `src/ControlMenu/Services/DeviceService.cs` | Modify | Inject notifier, replace event invocations, remove event field |
| `tests/ControlMenu.Tests/Services/Fakes/FakeDeviceService.cs` | Modify | Remove `DevicesChanged` event + `RaiseChanged()` |
| `src/ControlMenu/Services/DeviceTypeCache.cs` | Modify | Subscribe to notifier instead of scoped event |
| `tests/ControlMenu.Tests/Services/DeviceTypeCacheTests.cs` | Modify | Use `FakeDeviceChangeNotifier` |
| `src/ControlMenu/Modules/AndroidDevices/Services/DeviceTypePresenceWatcher.cs` | Modify | Subscribe to notifier instead of scoped event |
| `tests/ControlMenu.Tests/Modules/AndroidDevices/DeviceTypePresenceWatcherTests.cs` | Modify | Use `FakeDeviceChangeNotifier` |
| `src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor` | Modify | Inject + pass notifier to watcher ctor |
| `src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor` | Modify | Inject + pass notifier to watcher ctor |
| `src/ControlMenu/Modules/AndroidDevices/Pages/TabletDashboard.razor` | Modify | Inject + pass notifier to watcher ctor |
| `src/ControlMenu/Modules/AndroidDevices/Pages/WatchDashboard.razor` | Modify | Inject + pass notifier to watcher ctor |
| `src/ControlMenu/Program.cs` | Modify | Add singleton registration |

---

### Task 1: Create `IDeviceChangeNotifier` and `DeviceChangeNotifier`

**Files:**
- Create: `src/ControlMenu/Services/IDeviceChangeNotifier.cs`
- Create: `src/ControlMenu/Services/DeviceChangeNotifier.cs`
- Create: `tests/ControlMenu.Tests/Services/DeviceChangeNotifierTests.cs`

- [ ] **Step 1: Write failing tests for the notifier**

Create `tests/ControlMenu.Tests/Services/DeviceChangeNotifierTests.cs`:

```csharp
using ControlMenu.Services;

namespace ControlMenu.Tests.Services;

public class DeviceChangeNotifierTests
{
    [Fact]
    public void NotifyChanged_InvokesSubscriber()
    {
        var notifier = new DeviceChangeNotifier();
        var called = false;
        notifier.Changed += () => called = true;

        notifier.NotifyChanged();

        Assert.True(called);
    }

    [Fact]
    public void NotifyChanged_InvokesMultipleSubscribers()
    {
        var notifier = new DeviceChangeNotifier();
        var count = 0;
        notifier.Changed += () => count++;
        notifier.Changed += () => count++;

        notifier.NotifyChanged();

        Assert.Equal(2, count);
    }

    [Fact]
    public void NotifyChanged_NoSubscribers_DoesNotThrow()
    {
        var notifier = new DeviceChangeNotifier();
        notifier.NotifyChanged();
    }

    [Fact]
    public void Unsubscribe_StopsReceivingNotifications()
    {
        var notifier = new DeviceChangeNotifier();
        var count = 0;
        void Handler() => count++;
        notifier.Changed += Handler;

        notifier.NotifyChanged();
        Assert.Equal(1, count);

        notifier.Changed -= Handler;
        notifier.NotifyChanged();
        Assert.Equal(1, count);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure (types don't exist)**

Run: `dotnet test tests/ControlMenu.Tests --filter "DeviceChangeNotifierTests" -v q`
Expected: build error — `DeviceChangeNotifier` and `IDeviceChangeNotifier` not found.

- [ ] **Step 3: Create the interface**

Create `src/ControlMenu/Services/IDeviceChangeNotifier.cs`:

```csharp
namespace ControlMenu.Services;

public interface IDeviceChangeNotifier
{
    event Action? Changed;
    void NotifyChanged();
}
```

- [ ] **Step 4: Create the implementation**

Create `src/ControlMenu/Services/DeviceChangeNotifier.cs`:

```csharp
namespace ControlMenu.Services;

public sealed class DeviceChangeNotifier : IDeviceChangeNotifier
{
    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();
}
```

- [ ] **Step 5: Run tests — all 4 pass**

Run: `dotnet test tests/ControlMenu.Tests --filter "DeviceChangeNotifierTests" -v q`
Expected: 4 passed.

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Services/IDeviceChangeNotifier.cs src/ControlMenu/Services/DeviceChangeNotifier.cs tests/ControlMenu.Tests/Services/DeviceChangeNotifierTests.cs
git commit -m "feat(b5): add IDeviceChangeNotifier singleton pub/sub"
```

---

### Task 2: Create `FakeDeviceChangeNotifier` for tests

**Files:**
- Create: `tests/ControlMenu.Tests/Services/Fakes/FakeDeviceChangeNotifier.cs`

- [ ] **Step 1: Create the fake**

Create `tests/ControlMenu.Tests/Services/Fakes/FakeDeviceChangeNotifier.cs`:

```csharp
using ControlMenu.Services;

namespace ControlMenu.Tests.Services.Fakes;

public sealed class FakeDeviceChangeNotifier : IDeviceChangeNotifier
{
    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();
    public void TriggerChanged() => Changed?.Invoke();
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build tests/ControlMenu.Tests -v q`
Expected: build succeeded, zero warnings.

- [ ] **Step 3: Commit**

```bash
git add tests/ControlMenu.Tests/Services/Fakes/FakeDeviceChangeNotifier.cs
git commit -m "feat(b5): add FakeDeviceChangeNotifier test helper"
```

---

### Task 3: Migrate `DeviceTypeCache` to use notifier

**Files:**
- Modify: `src/ControlMenu/Services/DeviceTypeCache.cs`
- Modify: `tests/ControlMenu.Tests/Services/DeviceTypeCacheTests.cs`

- [ ] **Step 1: Update `DeviceTypeCacheTests` to use `FakeDeviceChangeNotifier`**

Replace the full file `tests/ControlMenu.Tests/Services/DeviceTypeCacheTests.cs`:

```csharp
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Services;
using ControlMenu.Tests.Services.Fakes;

namespace ControlMenu.Tests.Services;

public class DeviceTypeCacheTests
{
    private readonly FakeDeviceService _deviceService = new();
    private readonly FakeDeviceChangeNotifier _notifier = new();
    private readonly DeviceTypeCache _cache;

    public DeviceTypeCacheTests()
    {
        _cache = new DeviceTypeCache(_deviceService, _notifier);
    }

    private static Device Make(DeviceType type)
        => new() { Id = Guid.NewGuid(), Name = "D", Type = type, MacAddress = "aa:bb", ModuleId = "android-devices" };

    [Fact]
    public void HasDevicesOfType_BeforeRefresh_ReturnsFalse()
    {
        Assert.False(_cache.HasDevicesOfType(DeviceType.AndroidPhone));
    }

    [Fact]
    public async Task HasDevicesOfType_AfterRefreshWithPhones_ReturnsTrueForPhone_FalseForOthers()
    {
        _deviceService.Devices.Add(Make(DeviceType.AndroidPhone));
        _deviceService.Devices.Add(Make(DeviceType.AndroidPhone));
        _deviceService.Devices.Add(Make(DeviceType.AndroidTablet));

        await _cache.RefreshAsync();

        Assert.True(_cache.HasDevicesOfType(DeviceType.AndroidPhone));
        Assert.True(_cache.HasDevicesOfType(DeviceType.AndroidTablet));
        Assert.False(_cache.HasDevicesOfType(DeviceType.GoogleTV));
        Assert.False(_cache.HasDevicesOfType(DeviceType.AndroidWatch));
    }

    [Fact]
    public async Task NotifierChanged_TriggersRefreshAndCacheUpdated()
    {
        var updated = 0;
        var tcs = new TaskCompletionSource();
        _cache.CacheUpdated += () =>
        {
            updated++;
            tcs.TrySetResult();
        };

        _deviceService.Devices.Add(Make(DeviceType.AndroidPhone));
        _notifier.TriggerChanged();
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(_cache.HasDevicesOfType(DeviceType.AndroidPhone));
        Assert.Equal(1, updated);
    }

    [Fact]
    public async Task LastDeviceDeleted_MakesHasDevicesOfTypeReturnFalse()
    {
        var phone = Make(DeviceType.AndroidPhone);
        _deviceService.Devices.Add(phone);
        await _cache.RefreshAsync();
        Assert.True(_cache.HasDevicesOfType(DeviceType.AndroidPhone));

        var tcs = new TaskCompletionSource();
        _cache.CacheUpdated += () => tcs.TrySetResult();

        _deviceService.Devices.Clear();
        _notifier.TriggerChanged();
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(_cache.HasDevicesOfType(DeviceType.AndroidPhone));
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromNotifier()
    {
        var updated = 0;
        _cache.CacheUpdated += () => updated++;

        _cache.Dispose();
        _deviceService.Devices.Add(Make(DeviceType.AndroidPhone));
        _notifier.TriggerChanged();

        await Task.Delay(100);

        Assert.Equal(0, updated);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure (constructor signature changed)**

Run: `dotnet test tests/ControlMenu.Tests --filter "DeviceTypeCacheTests" -v q`
Expected: build error — `DeviceTypeCache` doesn't accept 2 parameters yet.

- [ ] **Step 3: Update `DeviceTypeCache` to subscribe to notifier**

Replace the full file `src/ControlMenu/Services/DeviceTypeCache.cs`:

```csharp
using ControlMenu.Data.Enums;

namespace ControlMenu.Services;

public sealed class DeviceTypeCache : IDeviceTypeCache, IDisposable
{
    private readonly IDeviceService _deviceService;
    private readonly IDeviceChangeNotifier _notifier;
    private readonly ReaderWriterLockSlim _lock = new();
    private HashSet<DeviceType> _typesPresent = new();

    public event Action? CacheUpdated;

    public DeviceTypeCache(IDeviceService deviceService, IDeviceChangeNotifier notifier)
    {
        _deviceService = deviceService;
        _notifier = notifier;
        _notifier.Changed += OnDevicesChanged;
    }

    public bool HasDevicesOfType(DeviceType type)
    {
        _lock.EnterReadLock();
        try { return _typesPresent.Contains(type); }
        finally { _lock.ExitReadLock(); }
    }

    public async Task RefreshAsync()
    {
        var devices = await _deviceService.GetAllDevicesAsync();
        var newSet = devices.Select(d => d.Type).ToHashSet();
        _lock.EnterWriteLock();
        try { _typesPresent = newSet; }
        finally { _lock.ExitWriteLock(); }
        CacheUpdated?.Invoke();
    }

    private async void OnDevicesChanged()
    {
        try { await RefreshAsync(); }
        catch
        {
            // Async-void event handler: exceptions must be swallowed to avoid
            // terminating the process. Host logging pipeline covers mutation failures.
        }
    }

    public void Dispose()
    {
        _notifier.Changed -= OnDevicesChanged;
        _lock.Dispose();
    }
}
```

- [ ] **Step 4: Run DeviceTypeCacheTests — all 5 pass**

Run: `dotnet test tests/ControlMenu.Tests --filter "DeviceTypeCacheTests" -v q`
Expected: 5 passed.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Services/DeviceTypeCache.cs tests/ControlMenu.Tests/Services/DeviceTypeCacheTests.cs
git commit -m "feat(b5): migrate DeviceTypeCache to IDeviceChangeNotifier"
```

---

### Task 4: Migrate `DeviceTypePresenceWatcher` to use notifier

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/Services/DeviceTypePresenceWatcher.cs`
- Modify: `tests/ControlMenu.Tests/Modules/AndroidDevices/DeviceTypePresenceWatcherTests.cs`

- [ ] **Step 1: Update `DeviceTypePresenceWatcherTests` to use `FakeDeviceChangeNotifier`**

Replace the full file `tests/ControlMenu.Tests/Modules/AndroidDevices/DeviceTypePresenceWatcherTests.cs`:

```csharp
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Tests.Services.Fakes;

namespace ControlMenu.Tests.Modules.AndroidDevices;

public class DeviceTypePresenceWatcherTests
{
    private readonly FakeDeviceService _deviceService = new();
    private readonly FakeDeviceChangeNotifier _notifier = new();
    private readonly FakeNavigationManager _nav = new();

    private static Device MakePhone()
        => new() { Id = Guid.NewGuid(), Name = "P", Type = DeviceType.AndroidPhone, MacAddress = "aa", ModuleId = "android-devices" };

    [Fact]
    public async Task EnsurePresentOrRedirectAsync_NoDevicesOfType_Redirects()
    {
        using var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _notifier, _nav, null);

        var redirected = await watcher.EnsurePresentOrRedirectAsync();

        Assert.True(redirected);
        Assert.Single(_nav.Navigations);
        Assert.Equal("/android/devices", _nav.Navigations[0].Uri);
        Assert.True(_nav.Navigations[0].Replace);
    }

    [Fact]
    public async Task EnsurePresentOrRedirectAsync_DevicesPresent_DoesNotRedirect()
    {
        _deviceService.Devices.Add(MakePhone());
        using var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _notifier, _nav, null);

        var redirected = await watcher.EnsurePresentOrRedirectAsync();

        Assert.False(redirected);
        Assert.Empty(_nav.Navigations);
    }

    [Fact]
    public async Task NotifierChanged_LastDeviceDeleted_Redirects()
    {
        var phone = MakePhone();
        _deviceService.Devices.Add(phone);
        using var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _notifier, _nav, null);
        await watcher.EnsurePresentOrRedirectAsync();

        _deviceService.Devices.Clear();
        _notifier.TriggerChanged();
        await Task.Delay(50);

        Assert.Single(_nav.Navigations);
        Assert.Equal("/android/devices", _nav.Navigations[0].Uri);
    }

    [Fact]
    public async Task NotifierChanged_OtherDevicesPresent_InvokesInvalidateCallback_DoesNotRedirect()
    {
        _deviceService.Devices.Add(MakePhone());
        var invalidateCount = 0;
        using var watcher = new DeviceTypePresenceWatcher(
            DeviceType.AndroidPhone,
            _deviceService,
            _notifier,
            _nav,
            () => { invalidateCount++; return Task.CompletedTask; });
        await watcher.EnsurePresentOrRedirectAsync();

        _deviceService.Devices.Add(MakePhone());
        _notifier.TriggerChanged();
        await Task.Delay(50);

        Assert.Empty(_nav.Navigations);
        Assert.Equal(1, invalidateCount);
    }

    [Fact]
    public async Task NotifierChanged_AfterAlreadyRedirected_DoesNotRedirectAgain()
    {
        using var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _notifier, _nav, null);
        await watcher.EnsurePresentOrRedirectAsync();

        _notifier.TriggerChanged();
        await Task.Delay(50);

        Assert.Single(_nav.Navigations);
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromNotifier()
    {
        _deviceService.Devices.Add(MakePhone());
        var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _notifier, _nav, null);
        await watcher.EnsurePresentOrRedirectAsync();

        watcher.Dispose();
        _deviceService.Devices.Clear();
        _notifier.TriggerChanged();
        await Task.Delay(50);

        Assert.Empty(_nav.Navigations);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure (constructor signature changed)**

Run: `dotnet test tests/ControlMenu.Tests --filter "DeviceTypePresenceWatcherTests" -v q`
Expected: build error — `DeviceTypePresenceWatcher` doesn't accept notifier parameter yet.

- [ ] **Step 3: Update `DeviceTypePresenceWatcher` to subscribe to notifier**

Replace the full file `src/ControlMenu/Modules/AndroidDevices/Services/DeviceTypePresenceWatcher.cs`:

```csharp
using ControlMenu.Data.Enums;
using ControlMenu.Services;
using Microsoft.AspNetCore.Components;

namespace ControlMenu.Modules.AndroidDevices.Services;

public sealed class DeviceTypePresenceWatcher : IDisposable
{
    private readonly DeviceType _type;
    private readonly IDeviceService _deviceService;
    private readonly IDeviceChangeNotifier _notifier;
    private readonly NavigationManager _nav;
    private readonly Func<Task>? _onInvalidateAsync;
    private bool _redirected;

    public DeviceTypePresenceWatcher(
        DeviceType type,
        IDeviceService deviceService,
        IDeviceChangeNotifier notifier,
        NavigationManager nav,
        Func<Task>? onInvalidateAsync)
    {
        _type = type;
        _deviceService = deviceService;
        _notifier = notifier;
        _nav = nav;
        _onInvalidateAsync = onInvalidateAsync;
        _notifier.Changed += OnDevicesChanged;
    }

    public async Task<bool> EnsurePresentOrRedirectAsync()
    {
        var devices = await _deviceService.GetAllDevicesAsync();
        if (!devices.Any(d => d.Type == _type))
        {
            _redirected = true;
            _nav.NavigateTo("/android/devices", replace: true);
            return true;
        }
        return false;
    }

    private async void OnDevicesChanged()
    {
        if (_redirected) return;
        try
        {
            var devices = await _deviceService.GetAllDevicesAsync();
            if (!devices.Any(d => d.Type == _type))
            {
                _redirected = true;
                _nav.NavigateTo("/android/devices", replace: true);
            }
            else if (_onInvalidateAsync is not null)
            {
                await _onInvalidateAsync();
            }
        }
        catch
        {
            // Async-void event handler: swallow to avoid process termination.
        }
    }

    public void Dispose() => _notifier.Changed -= OnDevicesChanged;
}
```

- [ ] **Step 4: Run DeviceTypePresenceWatcherTests — all 6 pass**

Run: `dotnet test tests/ControlMenu.Tests --filter "DeviceTypePresenceWatcherTests" -v q`
Expected: 6 passed.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Services/DeviceTypePresenceWatcher.cs tests/ControlMenu.Tests/Modules/AndroidDevices/DeviceTypePresenceWatcherTests.cs
git commit -m "feat(b5): migrate DeviceTypePresenceWatcher to IDeviceChangeNotifier"
```

---

### Task 5: Remove scoped event from `IDeviceService` / `DeviceService` and wire notifier

**Files:**
- Modify: `src/ControlMenu/Services/IDeviceService.cs`
- Modify: `src/ControlMenu/Services/DeviceService.cs`
- Modify: `tests/ControlMenu.Tests/Services/Fakes/FakeDeviceService.cs`

- [ ] **Step 1: Update `IDeviceService` — remove the event**

Replace the full file `src/ControlMenu/Services/IDeviceService.cs`:

```csharp
using ControlMenu.Data.Entities;

namespace ControlMenu.Services;

public interface IDeviceService
{
    Task<IReadOnlyList<Device>> GetAllDevicesAsync();
    Task<Device?> GetDeviceAsync(Guid id);
    Task<Device> AddDeviceAsync(Device device);
    Task UpdateDeviceAsync(Device device);
    Task DeleteDeviceAsync(Guid id);
    Task UpdateLastSeenAsync(Guid id, string ipAddress);
}
```

- [ ] **Step 2: Update `DeviceService` — inject notifier, replace event calls**

Replace the full file `src/ControlMenu/Services/DeviceService.cs`:

```csharp
using ControlMenu.Data;
using ControlMenu.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Services;

public class DeviceService : IDeviceService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDeviceChangeNotifier _notifier;

    public DeviceService(IDbContextFactory<AppDbContext> dbFactory, IDeviceChangeNotifier notifier)
    {
        _dbFactory = dbFactory;
        _notifier = notifier;
    }

    public async Task<IReadOnlyList<Device>> GetAllDevicesAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Devices.AsNoTracking().OrderBy(d => d.Name).ToListAsync();
    }

    public async Task<Device?> GetDeviceAsync(Guid id)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<Device> AddDeviceAsync(Device device)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        if (device.Id == Guid.Empty)
            device.Id = Guid.NewGuid();
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        _notifier.NotifyChanged();
        return device;
    }

    public async Task UpdateDeviceAsync(Device device)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.Devices.FindAsync(device.Id);
        if (existing is null)
            throw new InvalidOperationException($"Device {device.Id} not found in database.");

        db.Entry(existing).CurrentValues.SetValues(device);
        await db.SaveChangesAsync();
        _notifier.NotifyChanged();
    }

    public async Task DeleteDeviceAsync(Guid id)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var device = await db.Devices.FindAsync(id);
        if (device is not null)
        {
            db.Devices.Remove(device);
            await db.SaveChangesAsync();
            _notifier.NotifyChanged();
        }
    }

    public async Task UpdateLastSeenAsync(Guid id, string ipAddress)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var device = await db.Devices.FindAsync(id);
        if (device is not null)
        {
            device.LastKnownIp = ipAddress;
            device.LastSeen = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }
}
```

- [ ] **Step 3: Update `FakeDeviceService` — remove event and `RaiseChanged()`**

Replace the full file `tests/ControlMenu.Tests/Services/Fakes/FakeDeviceService.cs`:

```csharp
using ControlMenu.Data.Entities;
using ControlMenu.Services;

namespace ControlMenu.Tests.Services.Fakes;

public sealed class FakeDeviceService : IDeviceService
{
    public List<Device> Devices { get; } = new();

    public Task<IReadOnlyList<Device>> GetAllDevicesAsync()
        => Task.FromResult<IReadOnlyList<Device>>(Devices.ToList());

    public Task<Device?> GetDeviceAsync(Guid id)
        => Task.FromResult(Devices.FirstOrDefault(d => d.Id == id));

    public Task<Device> AddDeviceAsync(Device device)
    {
        Devices.Add(device);
        return Task.FromResult(device);
    }

    public Task UpdateDeviceAsync(Device device)
        => Task.CompletedTask;

    public Task DeleteDeviceAsync(Guid id)
    {
        Devices.RemoveAll(d => d.Id == id);
        return Task.CompletedTask;
    }

    public Task UpdateLastSeenAsync(Guid id, string ipAddress)
        => Task.CompletedTask;
}
```

- [ ] **Step 4: Check for any other references to the removed event**

Run: `grep -r "DevicesChanged" src/ tests/ --include="*.cs" --include="*.razor"`
Expected: zero hits. If any remain, fix them before proceeding.

- [ ] **Step 5: Build the full solution**

Run: `dotnet build -v q`
Expected: build succeeded, zero warnings. If `AndroidDevicesModuleTests.cs` or any other file references `RaiseChanged()` or `DevicesChanged`, fix those first.

- [ ] **Step 6: Run all tests**

Run: `dotnet test tests/ControlMenu.Tests -v q`
Expected: all pass (count may temporarily drop if `AndroidDevicesModuleTests` used `RaiseChanged()` — fix in next step if so).

- [ ] **Step 7: Commit**

```bash
git add src/ControlMenu/Services/IDeviceService.cs src/ControlMenu/Services/DeviceService.cs tests/ControlMenu.Tests/Services/Fakes/FakeDeviceService.cs
git commit -m "feat(b5): remove scoped DevicesChanged event, wire DeviceService to notifier"
```

---

### Task 6: Update dashboard pages to inject and pass `IDeviceChangeNotifier`

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor`
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor`
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/TabletDashboard.razor`
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/WatchDashboard.razor`

All four pages follow the same pattern. Each currently has:

```razor
@inject IDeviceService DeviceService
@inject NavigationManager Nav
```

And constructs the watcher as:

```csharp
_presenceWatcher = new DeviceTypePresenceWatcher(
    DeviceType.XXX,
    DeviceService,
    Nav,
    () => InvokeAsync(StateHasChanged));
```

- [ ] **Step 1: Add `@inject IDeviceChangeNotifier` to each page**

Add this line after the existing `@inject IDeviceService DeviceService` in each of the 4 files:

```razor
@inject IDeviceChangeNotifier DeviceChangeNotifier
```

Add the using directive if not already covered by `_Imports.razor`:

```razor
@using ControlMenu.Services
```

(Check `_Imports.razor` first — if `ControlMenu.Services` is already imported globally, skip the `@using`.)

- [ ] **Step 2: Update each watcher constructor call to pass the notifier**

In each of the 4 files, change the `new DeviceTypePresenceWatcher(...)` call from 4 args to 5 args by inserting `DeviceChangeNotifier` after `DeviceService`:

```csharp
_presenceWatcher = new DeviceTypePresenceWatcher(
    DeviceType.XXX,
    DeviceService,
    DeviceChangeNotifier,
    Nav,
    () => InvokeAsync(StateHasChanged));
```

The `DeviceType` enum value is specific to each page:
- `GoogleTvDashboard.razor` → `DeviceType.GoogleTV`
- `PixelDashboard.razor` → `DeviceType.AndroidPhone`
- `TabletDashboard.razor` → `DeviceType.AndroidTablet`
- `WatchDashboard.razor` → `DeviceType.AndroidWatch`

- [ ] **Step 3: Build**

Run: `dotnet build -v q`
Expected: build succeeded, zero warnings.

- [ ] **Step 4: Run full test suite**

Run: `dotnet test tests/ControlMenu.Tests -v q`
Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor src/ControlMenu/Modules/AndroidDevices/Pages/TabletDashboard.razor src/ControlMenu/Modules/AndroidDevices/Pages/WatchDashboard.razor
git commit -m "feat(b5): inject IDeviceChangeNotifier into dashboard pages"
```

---

### Task 7: Register singleton in `Program.cs` and run full validation

**Files:**
- Modify: `src/ControlMenu/Program.cs`

- [ ] **Step 1: Add singleton registration**

In `src/ControlMenu/Program.cs`, after line 48 (`AddSingleton<ICommandExecutor, CommandExecutor>`), add:

```csharp
builder.Services.AddSingleton<IDeviceChangeNotifier, DeviceChangeNotifier>();
```

- [ ] **Step 2: Build**

Run: `dotnet build -v q`
Expected: build succeeded, zero warnings.

- [ ] **Step 3: Run full test suite**

Run: `dotnet test tests/ControlMenu.Tests -v q`
Expected: all tests pass. Count should be at least 245 (baseline) + 4 (DeviceChangeNotifierTests) = 249.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Program.cs
git commit -m "feat(b5): register IDeviceChangeNotifier singleton in DI"
```

---

### Task 8: Final validation — build clean + run app

- [ ] **Step 1: Clean build**

Run: `dotnet build -c Release -v q`
Expected: build succeeded, zero warnings.

- [ ] **Step 2: Full test suite one more time**

Run: `dotnet test tests/ControlMenu.Tests -v q`
Expected: 249+ tests pass, zero failures.

- [ ] **Step 3: Start the app and verify manually**

Run: `dotnet run --project src/ControlMenu/ControlMenu.csproj -c Release`

Manual checks (per spec §6 verification criteria):
1. Open two browser tabs to `http://localhost:5159`
2. In tab A, add a device → tab B sidebar updates
3. In tab B, navigate to a device-type page → delete that device type's last device in tab A → tab B redirects to `/android/devices`

- [ ] **Step 4: Commit any fixes if needed, then stop the app**
