# B-5: Cross-Circuit Device Change Notifier — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce a singleton `IDeviceChangeNotifier` so device mutations in one Blazor Server circuit notify all circuits' sidebar caches and presence watchers.

**Architecture:** A new singleton pub/sub service (`DeviceChangeNotifier`) sits between `DeviceService` (scoped, raises notifications) and `DeviceTypeCache` + `DeviceTypePresenceWatcher` (scoped, subscribe). The scoped `IDeviceService.DevicesChanged` event is removed entirely — the singleton is the single notification path. Snapshot + per-handler try/catch invoke protects against faulting subscribers.

**Tech Stack:** .NET 9, Blazor Server, xUnit, C# events

**Spec:** `docs/superpowers/specs/2026-04-23-b5-cross-circuit-notifier-design.md`

**Test command:** `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~<TestClass>"`

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| New | `src/ControlMenu/Services/IDeviceChangeNotifier.cs` | Interface: `event Action Changed` + `void NotifyChanged()` |
| New | `src/ControlMenu/Services/DeviceChangeNotifier.cs` | Singleton impl: snapshot invoke + per-handler try/catch |
| New | `tests/ControlMenu.Tests/Services/Fakes/FakeDeviceChangeNotifier.cs` | Test double: `RaiseChanged()` + `NotifyChangedCallCount` |
| Modify | `src/ControlMenu/Services/IDeviceService.cs` | Remove `event Action DevicesChanged` |
| Modify | `src/ControlMenu/Services/DeviceService.cs` | Remove event, add `IDeviceChangeNotifier` dep, call `NotifyChanged()` |
| Modify | `src/ControlMenu/Services/DeviceTypeCache.cs` | Subscribe to notifier instead of DeviceService event |
| Modify | `src/ControlMenu/Modules/AndroidDevices/Services/DeviceTypePresenceWatcher.cs` | Subscribe to notifier instead of DeviceService event |
| Modify | `src/ControlMenu/Program.cs` | Register `IDeviceChangeNotifier` as singleton |
| Modify | `tests/ControlMenu.Tests/Services/Fakes/FakeDeviceService.cs` | Remove `DevicesChanged` event + `RaiseChanged()` |
| Modify | `tests/ControlMenu.Tests/Services/DeviceTypeCacheTests.cs` | Use `FakeDeviceChangeNotifier` |
| Modify | `tests/ControlMenu.Tests/Services/DeviceServiceTests.cs` | Assert `NotifyChangedCallCount` on fake notifier |
| Modify | `tests/ControlMenu.Tests/Modules/AndroidDevices/DeviceTypePresenceWatcherTests.cs` | Use `FakeDeviceChangeNotifier` |

---

### Task 1: Create IDeviceChangeNotifier interface

**Files:**
- Create: `src/ControlMenu/Services/IDeviceChangeNotifier.cs`

- [ ] **Step 1: Create the interface file**

```csharp
namespace ControlMenu.Services;

public interface IDeviceChangeNotifier
{
    event Action Changed;
    void NotifyChanged();
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/ControlMenu/`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/Services/IDeviceChangeNotifier.cs
git commit -m "feat(b5): add IDeviceChangeNotifier interface"
```

---

### Task 2: Create FakeDeviceChangeNotifier test double

**Files:**
- Create: `tests/ControlMenu.Tests/Services/Fakes/FakeDeviceChangeNotifier.cs`

- [ ] **Step 1: Create the fake**

```csharp
using ControlMenu.Services;

namespace ControlMenu.Tests.Services.Fakes;

public sealed class FakeDeviceChangeNotifier : IDeviceChangeNotifier
{
    public event Action? Changed;

    public int NotifyChangedCallCount { get; private set; }

    public void NotifyChanged()
    {
        NotifyChangedCallCount++;
        Changed?.Invoke();
    }

    public void RaiseChanged() => Changed?.Invoke();
}
```

Note: `NotifyChanged()` increments the counter AND fires the event (so it works both for DeviceService tests that assert the count, and for cache/watcher tests where the fake acts as a real notifier). `RaiseChanged()` fires the event only (for tests that need to trigger subscribers without going through the "DeviceService called NotifyChanged" path).

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build tests/ControlMenu.Tests/`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add tests/ControlMenu.Tests/Services/Fakes/FakeDeviceChangeNotifier.cs
git commit -m "test(b5): add FakeDeviceChangeNotifier test double"
```

---

### Task 3: Create DeviceChangeNotifier singleton implementation (TDD)

**Files:**
- Create: `src/ControlMenu/Services/DeviceChangeNotifier.cs`
- Create: `tests/ControlMenu.Tests/Services/DeviceChangeNotifierTests.cs`

- [ ] **Step 1: Write test — NotifyChanged fires event to subscriber**

Create `tests/ControlMenu.Tests/Services/DeviceChangeNotifierTests.cs`:

```csharp
using ControlMenu.Services;

namespace ControlMenu.Tests.Services;

public class DeviceChangeNotifierTests
{
    [Fact]
    public void NotifyChanged_FiresChangedEvent()
    {
        var notifier = new DeviceChangeNotifier();
        var fired = 0;
        notifier.Changed += () => fired++;

        notifier.NotifyChanged();

        Assert.Equal(1, fired);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DeviceChangeNotifierTests.NotifyChanged_FiresChangedEvent"`
Expected: FAIL — `DeviceChangeNotifier` class does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/ControlMenu/Services/DeviceChangeNotifier.cs`:

```csharp
namespace ControlMenu.Services;

public sealed class DeviceChangeNotifier : IDeviceChangeNotifier
{
    public event Action? Changed;

    public void NotifyChanged()
    {
        var snapshot = Changed?.GetInvocationList();
        if (snapshot is null) return;
        foreach (var handler in snapshot)
        {
            try { ((Action)handler)(); }
            catch { }
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DeviceChangeNotifierTests.NotifyChanged_FiresChangedEvent"`
Expected: PASS

- [ ] **Step 5: Write test — one faulting subscriber does not block others**

Add to `DeviceChangeNotifierTests`:

```csharp
[Fact]
public void NotifyChanged_FaultingSubscriberDoesNotBlockOthers()
{
    var notifier = new DeviceChangeNotifier();
    var secondFired = false;
    notifier.Changed += () => throw new InvalidOperationException("boom");
    notifier.Changed += () => secondFired = true;

    notifier.NotifyChanged();

    Assert.True(secondFired);
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DeviceChangeNotifierTests.NotifyChanged_FaultingSubscriberDoesNotBlockOthers"`
Expected: PASS (the snapshot + per-handler try/catch implementation already handles this).

- [ ] **Step 7: Write test — no subscribers does not throw**

Add to `DeviceChangeNotifierTests`:

```csharp
[Fact]
public void NotifyChanged_NoSubscribers_DoesNotThrow()
{
    var notifier = new DeviceChangeNotifier();
    var ex = Record.Exception(() => notifier.NotifyChanged());
    Assert.Null(ex);
}
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DeviceChangeNotifierTests.NotifyChanged_NoSubscribers_DoesNotThrow"`
Expected: PASS

- [ ] **Step 9: Run all DeviceChangeNotifier tests**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DeviceChangeNotifierTests"`
Expected: 3 passed, 0 failed.

- [ ] **Step 10: Commit**

```bash
git add src/ControlMenu/Services/DeviceChangeNotifier.cs tests/ControlMenu.Tests/Services/DeviceChangeNotifierTests.cs
git commit -m "feat(b5): add DeviceChangeNotifier with snapshot invoke + per-handler try/catch"
```

---

### Task 4: Migrate DeviceService to use IDeviceChangeNotifier

**Files:**
- Modify: `src/ControlMenu/Services/IDeviceService.cs`
- Modify: `src/ControlMenu/Services/DeviceService.cs`
- Modify: `tests/ControlMenu.Tests/Services/DeviceServiceTests.cs`

- [ ] **Step 1: Update DeviceServiceTests to use FakeDeviceChangeNotifier**

Replace the full contents of `tests/ControlMenu.Tests/Services/DeviceServiceTests.cs` with:

```csharp
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Services;
using ControlMenu.Tests.Data;
using ControlMenu.Tests.Services.Fakes;

namespace ControlMenu.Tests.Services;

public class DeviceServiceTests : IDisposable
{
    private readonly InMemoryDbContextFactory _factory;
    private readonly FakeDeviceChangeNotifier _notifier = new();
    private readonly DeviceService _service;

    public DeviceServiceTests()
    {
        _factory = TestDbContextFactory.CreateFactory();
        _service = new DeviceService(_factory, _notifier);
    }

    public void Dispose() => _factory.Dispose();

    private Device MakeDevice(string name = "Test TV", string mac = "aa-bb-cc-dd-ee-ff")
    {
        return new Device
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = DeviceType.GoogleTV,
            MacAddress = mac,
            ModuleId = "android-devices"
        };
    }

    [Fact]
    public async Task GetAllDevicesAsync_Empty_ReturnsEmptyList()
    {
        var devices = await _service.GetAllDevicesAsync();
        Assert.Empty(devices);
    }

    [Fact]
    public async Task AddDeviceAsync_AddsAndReturnsDevice()
    {
        var device = MakeDevice();
        var result = await _service.AddDeviceAsync(device);
        Assert.Equal(device.Name, result.Name);
        var all = await _service.GetAllDevicesAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task GetDeviceAsync_ReturnsById()
    {
        var device = MakeDevice();
        await _service.AddDeviceAsync(device);
        var loaded = await _service.GetDeviceAsync(device.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Test TV", loaded.Name);
    }

    [Fact]
    public async Task GetDeviceAsync_NotFound_ReturnsNull()
    {
        var loaded = await _service.GetDeviceAsync(Guid.NewGuid());
        Assert.Null(loaded);
    }

    [Fact]
    public async Task UpdateDeviceAsync_ModifiesFields()
    {
        var device = MakeDevice();
        await _service.AddDeviceAsync(device);
        device.Name = "Renamed TV";
        device.AdbPort = 5556;
        await _service.UpdateDeviceAsync(device);
        var loaded = await _service.GetDeviceAsync(device.Id);
        Assert.Equal("Renamed TV", loaded!.Name);
        Assert.Equal(5556, loaded.AdbPort);
    }

    [Fact]
    public async Task DeleteDeviceAsync_RemovesDevice()
    {
        var device = MakeDevice();
        await _service.AddDeviceAsync(device);
        await _service.DeleteDeviceAsync(device.Id);
        var all = await _service.GetAllDevicesAsync();
        Assert.Empty(all);
    }

    [Fact]
    public async Task UpdateLastSeenAsync_SetsIpAndTimestamp()
    {
        var device = MakeDevice();
        await _service.AddDeviceAsync(device);
        await _service.UpdateLastSeenAsync(device.Id, "192.168.1.50");
        var loaded = await _service.GetDeviceAsync(device.Id);
        Assert.Equal("192.168.1.50", loaded!.LastKnownIp);
        Assert.NotNull(loaded.LastSeen);
    }

    [Fact]
    public async Task AddDeviceAsync_NotifiesViaNotifier()
    {
        await _service.AddDeviceAsync(MakeDevice());
        Assert.Equal(1, _notifier.NotifyChangedCallCount);
    }

    [Fact]
    public async Task UpdateDeviceAsync_NotifiesViaNotifier()
    {
        var device = MakeDevice();
        await _service.AddDeviceAsync(device);
        _notifier.NotifyChangedCallCount = 0;

        device.Name = "Renamed";
        await _service.UpdateDeviceAsync(device);

        Assert.Equal(1, _notifier.NotifyChangedCallCount);
    }

    [Fact]
    public async Task DeleteDeviceAsync_NotifiesViaNotifier()
    {
        var device = MakeDevice();
        await _service.AddDeviceAsync(device);
        _notifier.NotifyChangedCallCount = 0;

        await _service.DeleteDeviceAsync(device.Id);

        Assert.Equal(1, _notifier.NotifyChangedCallCount);
    }

    [Fact]
    public async Task UpdateLastSeenAsync_DoesNotNotify()
    {
        var device = MakeDevice();
        await _service.AddDeviceAsync(device);
        _notifier.NotifyChangedCallCount = 0;

        await _service.UpdateLastSeenAsync(device.Id, "192.168.1.100");

        Assert.Equal(0, _notifier.NotifyChangedCallCount);
    }
}
```

Note: `NotifyChangedCallCount` needs a public setter for reset between operations. Update `FakeDeviceChangeNotifier` accordingly — change `{ get; private set; }` to `{ get; set; }`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DeviceServiceTests"`
Expected: FAIL — `DeviceService` constructor does not accept `IDeviceChangeNotifier`.

- [ ] **Step 3: Update IDeviceService — remove DevicesChanged event**

Replace the full contents of `src/ControlMenu/Services/IDeviceService.cs` with:

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

- [ ] **Step 4: Update DeviceService — remove event, add notifier dependency**

Replace the full contents of `src/ControlMenu/Services/DeviceService.cs` with:

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

- [ ] **Step 5: Update FakeDeviceChangeNotifier — make NotifyChangedCallCount settable**

In `tests/ControlMenu.Tests/Services/Fakes/FakeDeviceChangeNotifier.cs`, change:

```csharp
public int NotifyChangedCallCount { get; set; }
```

(was `{ get; private set; }`)

- [ ] **Step 6: Run DeviceService tests to verify they pass**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DeviceServiceTests"`
Expected: 11 passed, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add src/ControlMenu/Services/IDeviceService.cs src/ControlMenu/Services/DeviceService.cs tests/ControlMenu.Tests/Services/DeviceServiceTests.cs tests/ControlMenu.Tests/Services/Fakes/FakeDeviceChangeNotifier.cs
git commit -m "feat(b5): migrate DeviceService from scoped event to singleton notifier"
```

---

### Task 5: Migrate DeviceTypeCache to use IDeviceChangeNotifier

**Files:**
- Modify: `src/ControlMenu/Services/DeviceTypeCache.cs`
- Modify: `tests/ControlMenu.Tests/Services/DeviceTypeCacheTests.cs`

- [ ] **Step 1: Update DeviceTypeCacheTests to use FakeDeviceChangeNotifier**

Replace the full contents of `tests/ControlMenu.Tests/Services/DeviceTypeCacheTests.cs` with:

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
        _notifier.RaiseChanged();
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
        _notifier.RaiseChanged();
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
        _notifier.RaiseChanged();

        await Task.Delay(100);

        Assert.Equal(0, updated);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DeviceTypeCacheTests"`
Expected: FAIL — `DeviceTypeCache` constructor does not accept `IDeviceChangeNotifier`.

- [ ] **Step 3: Update DeviceTypeCache to subscribe to notifier**

Replace the full contents of `src/ControlMenu/Services/DeviceTypeCache.cs` with:

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

- [ ] **Step 4: Run DeviceTypeCacheTests to verify they pass**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DeviceTypeCacheTests"`
Expected: 5 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Services/DeviceTypeCache.cs tests/ControlMenu.Tests/Services/DeviceTypeCacheTests.cs
git commit -m "feat(b5): migrate DeviceTypeCache to subscribe to singleton notifier"
```

---

### Task 6: Migrate DeviceTypePresenceWatcher to use IDeviceChangeNotifier

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/Services/DeviceTypePresenceWatcher.cs`
- Modify: `tests/ControlMenu.Tests/Modules/AndroidDevices/DeviceTypePresenceWatcherTests.cs`

- [ ] **Step 1: Update DeviceTypePresenceWatcherTests to use FakeDeviceChangeNotifier**

Replace the full contents of `tests/ControlMenu.Tests/Modules/AndroidDevices/DeviceTypePresenceWatcherTests.cs` with:

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
        _notifier.RaiseChanged();
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
        _notifier.RaiseChanged();
        await Task.Delay(50);

        Assert.Empty(_nav.Navigations);
        Assert.Equal(1, invalidateCount);
    }

    [Fact]
    public async Task NotifierChanged_AfterAlreadyRedirected_DoesNotRedirectAgain()
    {
        using var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _notifier, _nav, null);
        await watcher.EnsurePresentOrRedirectAsync();

        _notifier.RaiseChanged();
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
        _notifier.RaiseChanged();
        await Task.Delay(50);

        Assert.Empty(_nav.Navigations);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DeviceTypePresenceWatcherTests"`
Expected: FAIL — `DeviceTypePresenceWatcher` constructor does not accept `IDeviceChangeNotifier`.

- [ ] **Step 3: Update DeviceTypePresenceWatcher to subscribe to notifier**

Replace the full contents of `src/ControlMenu/Modules/AndroidDevices/Services/DeviceTypePresenceWatcher.cs` with:

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

- [ ] **Step 4: Run DeviceTypePresenceWatcherTests to verify they pass**

Run: `dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DeviceTypePresenceWatcherTests"`
Expected: 6 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Services/DeviceTypePresenceWatcher.cs tests/ControlMenu.Tests/Modules/AndroidDevices/DeviceTypePresenceWatcherTests.cs
git commit -m "feat(b5): migrate DeviceTypePresenceWatcher to subscribe to singleton notifier"
```

---

### Task 7: Clean up FakeDeviceService and register singleton in Program.cs

**Files:**
- Modify: `tests/ControlMenu.Tests/Services/Fakes/FakeDeviceService.cs`
- Modify: `src/ControlMenu/Program.cs`

- [ ] **Step 1: Clean up FakeDeviceService — remove DevicesChanged event**

Replace the full contents of `tests/ControlMenu.Tests/Services/Fakes/FakeDeviceService.cs` with:

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

- [ ] **Step 2: Register IDeviceChangeNotifier singleton in Program.cs**

In `src/ControlMenu/Program.cs`, find:

```csharp
builder.Services.AddScoped<IDeviceService, DeviceService>();
```

Insert **before** that line:

```csharp
builder.Services.AddSingleton<IDeviceChangeNotifier, DeviceChangeNotifier>();
```

The notifier must be registered before the scoped services that depend on it (DI will resolve it either way, but this keeps the registration order readable: singleton dependencies first, then scoped consumers).

- [ ] **Step 3: Verify the full project builds**

Run: `dotnet build src/ControlMenu/`
Expected: Build succeeded. No warnings about unused events or missing references.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test tests/ControlMenu.Tests/`
Expected: All tests pass. Zero failures.

- [ ] **Step 5: Commit**

```bash
git add tests/ControlMenu.Tests/Services/Fakes/FakeDeviceService.cs src/ControlMenu/Program.cs
git commit -m "feat(b5): register singleton notifier, clean up FakeDeviceService"
```

---

### Task 8: Fix callers of DeviceTypePresenceWatcher constructor

The `DeviceTypePresenceWatcher` constructor now takes an `IDeviceChangeNotifier` parameter. Any Razor component that `new`s up a watcher needs to inject and pass the notifier.

**Files:**
- Modify: All `.razor` files that construct `DeviceTypePresenceWatcher`

- [ ] **Step 1: Find all callers**

Run: `grep -rn "new DeviceTypePresenceWatcher" src/ControlMenu/`

Each match is a Razor component that needs `@inject IDeviceChangeNotifier` and must pass the notifier as the third constructor argument.

- [ ] **Step 2: For each caller, add the injection and update the constructor call**

For each `.razor` file found in step 1:

1. Add at the top (alongside existing `@inject` directives):
```razor
@inject IDeviceChangeNotifier DeviceChangeNotifier
```

2. Add the using directive if not already present (check `_Imports.razor` first — if `ControlMenu.Services` is already imported there, skip this):
```razor
@using ControlMenu.Services
```

3. Update the `new DeviceTypePresenceWatcher(...)` call to pass `DeviceChangeNotifier` as the third argument (between `IDeviceService` and `NavigationManager`):

Before:
```csharp
new DeviceTypePresenceWatcher(DeviceType.XXX, DeviceService, Nav, ...)
```

After:
```csharp
new DeviceTypePresenceWatcher(DeviceType.XXX, DeviceService, DeviceChangeNotifier, Nav, ...)
```

- [ ] **Step 3: Verify the full project builds**

Run: `dotnet build src/ControlMenu/`
Expected: Build succeeded.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test tests/ControlMenu.Tests/`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/
git commit -m "feat(b5): inject IDeviceChangeNotifier into dashboard pages"
```

---

### Task 9: Two-tab smoke test

**Files:** None (manual verification)

- [ ] **Step 1: Start the app**

Run: `dotnet run --project src/ControlMenu/ControlMenu.csproj -c Release`
Expected: App starts on http://localhost:5159

- [ ] **Step 2: Open two browser tabs**

- Tab A: navigate to `/settings/devices` (or `/android/devices`)
- Tab B: navigate to `/android/phone` (or any device-type dashboard that has at least one device)

- [ ] **Step 3: Delete a device in Tab A**

Delete a device of the type shown in Tab B's sidebar.

- [ ] **Step 4: Verify Tab B updates**

Expected: Tab B's sidebar nav entry for the deleted device type disappears within ~1 second, without refreshing Tab B.

- [ ] **Step 5: Verify redirect still works**

Navigate Tab B to a device-type URL that has zero devices (e.g., `/android/watch` if no watches exist).
Expected: `DeviceTypePresenceWatcher` redirects to `/android/devices`.

- [ ] **Step 6: Verify adding a device cross-circuit**

In Tab A, add a new device of a type not currently present (e.g., AndroidWatch).
Expected: Tab B's sidebar gains the new nav entry within ~1 second.

- [ ] **Step 7: Commit CHANGELOG update**

Add an entry to `CHANGELOG.md` under `[Unreleased]` > `Fixed`:

```markdown
- Fixed cross-circuit sidebar nav desync: device mutations in one browser tab now update all open tabs' sidebar navigation in real-time (B-5)
```

```bash
git add CHANGELOG.md
git commit -m "docs(changelog): add B-5 cross-circuit nav fix"
```
