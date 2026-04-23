# Android device-type-aware navigation and dashboards — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Android Devices sidebar nav reflect the user's actual device inventory (types appear as devices are registered, disappear when the last of a type is deleted), ship Tablet + Watch dashboards for the new device types, replace the wizard's hand-rolled device form with the existing scanner panel, and swap all emoji nav icons for custom SVGs.

**Architecture:** `NavEntry` gains an optional `IsVisible` predicate. `IDeviceService` raises a `DevicesChanged` event after every mutation. A new scoped `IDeviceTypeCache` subscribes to that event and exposes a synchronous `HasDevicesOfType` query. The Sidebar primes the cache, subscribes to its `CacheUpdated`, and filters nav entries on each change. Each device dashboard (Phone / Google TV / new Tablet / new Watch) composes a `DeviceTypePresenceWatcher` that redirects to `/android/devices` when the last device of its type is deleted. The wizard's Devices step embeds the existing `DiscoveredPanel` via the existing `IScanLifecycleHandler`.

**Tech Stack:** .NET 9, Blazor Server, EF Core (IDbContextFactory), xUnit, existing IScanLifecycleHandler + DiscoveredPanel infrastructure.

**Spec reference:** `docs/superpowers/specs/2026-04-23-android-device-type-nav-design.md` (commit `286f6aa`).

---

## File structure

**New files:**

| Path | Responsibility |
|---|---|
| `src/ControlMenu/Services/IDeviceTypeCache.cs` | Interface: per-circuit cache of which `DeviceType`s are currently registered. |
| `src/ControlMenu/Services/DeviceTypeCache.cs` | Implementation. Subscribes to `IDeviceService.DevicesChanged`, refreshes on event, exposes sync query + `CacheUpdated` event. |
| `src/ControlMenu/Modules/AndroidDevices/Services/DeviceTypePresenceWatcher.cs` | Disposable composed by each dashboard. Redirects to `/android/devices` when the last device of its type is deleted. |
| `src/ControlMenu/Modules/AndroidDevices/Pages/TabletDashboard.razor` | `/android/tablet` — near-clone of `PixelDashboard.razor`. |
| `src/ControlMenu/Modules/AndroidDevices/Pages/WatchDashboard.razor` | `/android/watch` — near-clone of `PixelDashboard.razor`. |
| `src/ControlMenu/wwwroot/images/devices/device-list.svg` | Sidebar icon: Device List. |
| `src/ControlMenu/wwwroot/images/devices/smart-tv.svg` | Sidebar icon: Google TV. |
| `src/ControlMenu/wwwroot/images/devices/smart-phone.svg` | Sidebar icon: Android Phone. |
| `src/ControlMenu/wwwroot/images/devices/tablet.svg` | Sidebar icon: Android Tablet. |
| `src/ControlMenu/wwwroot/images/devices/smart-watch.svg` | Sidebar icon: Android Watch. |
| `tests/ControlMenu.Tests/Services/DeviceTypeCacheTests.cs` | Unit tests for `DeviceTypeCache`. |
| `tests/ControlMenu.Tests/Modules/AndroidDevices/DeviceTypePresenceWatcherTests.cs` | Unit tests for the watcher. |
| `tests/ControlMenu.Tests/Services/Fakes/FakeNavigationManager.cs` | Test double for `NavigationManager` that records `NavigateTo` calls. |

**Modified files:**

| Path | Change |
|---|---|
| `src/ControlMenu/Modules/NavEntry.cs` | Add optional `Func<IServiceProvider, bool>? IsVisible` parameter. |
| `src/ControlMenu/Services/IDeviceService.cs` | Add `event Action DevicesChanged`. |
| `src/ControlMenu/Services/DeviceService.cs` | Raise event on `AddDeviceAsync`, `UpdateDeviceAsync`, `DeleteDeviceAsync`. |
| `src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs` | Replace hardcoded nav entries with 5 entries using SVG paths + `IsVisible` predicates. |
| `src/ControlMenu/Program.cs` | Register `IDeviceTypeCache` as scoped. |
| `src/ControlMenu/Components/Layout/Sidebar.razor` | Inject cache, prime on init, subscribe to updates, filter entries, add SVG rendering branch. |
| `src/ControlMenu/Components/Layout/Sidebar.razor.css` | Add `.sidebar-nav-icon` class. |
| `src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor` | Compose `DeviceTypePresenceWatcher` in `OnInitializedAsync` + `Dispose`. |
| `src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor` | Compose `DeviceTypePresenceWatcher`. |
| `src/ControlMenu/Components/Pages/Setup/WizardDevices.razor` | Full rewrite — embed `DiscoveredPanel` + scanner orchestration. |
| `tests/ControlMenu.Tests/Services/DeviceServiceTests.cs` | Add tests for `DevicesChanged` event. |
| `tests/ControlMenu.Tests/Modules/AndroidDevices/AndroidDevicesModuleTests.cs` | Update nav-entry test for 5 entries + predicate semantics. |
| `docs/manual-test-checklist.md` | New section 5e covering dynamic nav + new dashboards + wizard scanner. |
| `docs/TECHNICAL_GUIDE.md` | Note: Watch dashboard is untested on real hardware. |

---

## Task 1: Extend `NavEntry` with optional `IsVisible` predicate

**Files:**
- Modify: `src/ControlMenu/Modules/NavEntry.cs`

- [ ] **Step 1: Read the current record**

`src/ControlMenu/Modules/NavEntry.cs` (7 lines):
```csharp
namespace ControlMenu.Modules;

public record NavEntry(
    string Title,
    string Href,
    string? Icon = null,
    int SortOrder = 0);
```

- [ ] **Step 2: Add the predicate field**

Replace the record definition with:

```csharp
namespace ControlMenu.Modules;

public record NavEntry(
    string Title,
    string Href,
    string? Icon = null,
    int SortOrder = 0,
    Func<IServiceProvider, bool>? IsVisible = null);
```

- [ ] **Step 3: Build to verify no regression**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj
```

Expected: build succeeds. `Func<IServiceProvider, bool>` is in `System` + `Microsoft.Extensions.DependencyInjection` namespaces — both already used elsewhere, `System` namespace is implicit. No new `using` needed in callers that don't touch the field.

- [ ] **Step 4: Run existing test suite**

```bash
dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj
```

Expected: all existing tests pass (backward-compatible extension).

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Modules/NavEntry.cs
git commit -m "feat(nav): add optional IsVisible predicate to NavEntry"
```

---

## Task 2: Add `DevicesChanged` event to `IDeviceService`

**Files:**
- Modify: `src/ControlMenu/Services/IDeviceService.cs`
- Modify: `src/ControlMenu/Services/DeviceService.cs`
- Modify: `tests/ControlMenu.Tests/Services/DeviceServiceTests.cs`

- [ ] **Step 1: Write failing tests for the event**

Add to end of `tests/ControlMenu.Tests/Services/DeviceServiceTests.cs` (before the closing `}` of the class):

```csharp
    [Fact]
    public async Task AddDeviceAsync_RaisesDevicesChanged()
    {
        var raised = 0;
        _service.DevicesChanged += () => raised++;

        await _service.AddDeviceAsync(MakeDevice());

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task UpdateDeviceAsync_RaisesDevicesChanged()
    {
        var device = MakeDevice();
        await _service.AddDeviceAsync(device);
        var raised = 0;
        _service.DevicesChanged += () => raised++;

        device.Name = "Renamed";
        await _service.UpdateDeviceAsync(device);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task DeleteDeviceAsync_RaisesDevicesChanged()
    {
        var device = MakeDevice();
        await _service.AddDeviceAsync(device);
        var raised = 0;
        _service.DevicesChanged += () => raised++;

        await _service.DeleteDeviceAsync(device.Id);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task UpdateLastSeenAsync_DoesNotRaiseDevicesChanged()
    {
        var device = MakeDevice();
        await _service.AddDeviceAsync(device);
        var raised = 0;
        _service.DevicesChanged += () => raised++;

        await _service.UpdateLastSeenAsync(device.Id, "192.168.1.100");

        Assert.Equal(0, raised);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter "FullyQualifiedName~DeviceServiceTests"
```

Expected: FAIL — compilation error, `'IDeviceService' does not contain a definition for 'DevicesChanged'`.

- [ ] **Step 3: Add the event to the interface**

Open `src/ControlMenu/Services/IDeviceService.cs`. Replace entire contents with:

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

    event Action DevicesChanged;
}
```

- [ ] **Step 4: Implement the event in `DeviceService`**

In `src/ControlMenu/Services/DeviceService.cs`, add the event field and raise it after successful mutations. Replace entire file contents with:

```csharp
using ControlMenu.Data;
using ControlMenu.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Services;

public class DeviceService : IDeviceService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public DeviceService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public event Action? DevicesChanged;

    event Action IDeviceService.DevicesChanged
    {
        add => DevicesChanged += value;
        remove => DevicesChanged -= value;
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
        DevicesChanged?.Invoke();
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
        DevicesChanged?.Invoke();
    }

    public async Task DeleteDeviceAsync(Guid id)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var device = await db.Devices.FindAsync(id);
        if (device is not null)
        {
            db.Devices.Remove(device);
            await db.SaveChangesAsync();
            DevicesChanged?.Invoke();
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

Note: the explicit-interface `event` implementation (`event Action IDeviceService.DevicesChanged`) forwards to the nullable-typed field `DevicesChanged`. This pattern avoids the NRT mismatch between `Action` (interface) and `Action?` (field) while keeping the raising site clean (`DevicesChanged?.Invoke()`).

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter "FullyQualifiedName~DeviceServiceTests"
```

Expected: all DeviceService tests pass (existing + 4 new).

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Services/IDeviceService.cs src/ControlMenu/Services/DeviceService.cs tests/ControlMenu.Tests/Services/DeviceServiceTests.cs
git commit -m "feat(devices): add DevicesChanged event to IDeviceService"
```

---

## Task 3: Create `IDeviceTypeCache` + tests

**Files:**
- Create: `src/ControlMenu/Services/IDeviceTypeCache.cs`
- Create: `src/ControlMenu/Services/DeviceTypeCache.cs`
- Create: `tests/ControlMenu.Tests/Services/DeviceTypeCacheTests.cs`
- Create: `tests/ControlMenu.Tests/Services/Fakes/FakeDeviceService.cs`

- [ ] **Step 1: Create fake `IDeviceService` for tests**

Create `tests/ControlMenu.Tests/Services/Fakes/FakeDeviceService.cs`:

```csharp
using ControlMenu.Data.Entities;
using ControlMenu.Services;

namespace ControlMenu.Tests.Services.Fakes;

public sealed class FakeDeviceService : IDeviceService
{
    public List<Device> Devices { get; } = new();
    public event Action? DevicesChanged;

    public Task<IReadOnlyList<Device>> GetAllDevicesAsync()
        => Task.FromResult<IReadOnlyList<Device>>(Devices.ToList());

    public Task<Device?> GetDeviceAsync(Guid id)
        => Task.FromResult(Devices.FirstOrDefault(d => d.Id == id));

    public Task<Device> AddDeviceAsync(Device device)
    {
        Devices.Add(device);
        DevicesChanged?.Invoke();
        return Task.FromResult(device);
    }

    public Task UpdateDeviceAsync(Device device)
    {
        DevicesChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task DeleteDeviceAsync(Guid id)
    {
        Devices.RemoveAll(d => d.Id == id);
        DevicesChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task UpdateLastSeenAsync(Guid id, string ipAddress)
        => Task.CompletedTask;

    public void RaiseChanged() => DevicesChanged?.Invoke();
}
```

- [ ] **Step 2: Write failing tests for `DeviceTypeCache`**

Create `tests/ControlMenu.Tests/Services/DeviceTypeCacheTests.cs`:

```csharp
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Services;
using ControlMenu.Tests.Services.Fakes;

namespace ControlMenu.Tests.Services;

public class DeviceTypeCacheTests
{
    private readonly FakeDeviceService _deviceService = new();
    private readonly DeviceTypeCache _cache;

    public DeviceTypeCacheTests()
    {
        _cache = new DeviceTypeCache(_deviceService);
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
    public async Task DevicesChanged_TriggersReadAndCacheUpdated()
    {
        var updated = 0;
        _cache.CacheUpdated += () => updated++;

        _deviceService.Devices.Add(Make(DeviceType.AndroidPhone));
        _deviceService.RaiseChanged();
        await Task.Delay(50);   // let async void handler settle

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

        _deviceService.Devices.Clear();
        _deviceService.RaiseChanged();
        await Task.Delay(50);

        Assert.False(_cache.HasDevicesOfType(DeviceType.AndroidPhone));
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromDevicesChanged()
    {
        var updated = 0;
        _cache.CacheUpdated += () => updated++;

        _cache.Dispose();
        _deviceService.Devices.Add(Make(DeviceType.AndroidPhone));
        _deviceService.RaiseChanged();
        await Task.Delay(50);

        Assert.Equal(0, updated);
        Assert.False(_cache.HasDevicesOfType(DeviceType.AndroidPhone));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter "FullyQualifiedName~DeviceTypeCacheTests"
```

Expected: FAIL — `DeviceTypeCache` doesn't exist.

- [ ] **Step 4: Create the interface**

Create `src/ControlMenu/Services/IDeviceTypeCache.cs`:

```csharp
using ControlMenu.Data.Enums;

namespace ControlMenu.Services;

public interface IDeviceTypeCache
{
    bool HasDevicesOfType(DeviceType type);
    event Action? CacheUpdated;
    Task RefreshAsync();
}
```

- [ ] **Step 5: Implement the cache**

Create `src/ControlMenu/Services/DeviceTypeCache.cs`:

```csharp
using ControlMenu.Data.Enums;

namespace ControlMenu.Services;

public sealed class DeviceTypeCache : IDeviceTypeCache, IDisposable
{
    private readonly IDeviceService _deviceService;
    private readonly ReaderWriterLockSlim _lock = new();
    private HashSet<DeviceType> _typesPresent = new();

    public event Action? CacheUpdated;

    public DeviceTypeCache(IDeviceService deviceService)
    {
        _deviceService = deviceService;
        _deviceService.DevicesChanged += OnDevicesChanged;
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
        _deviceService.DevicesChanged -= OnDevicesChanged;
        _lock.Dispose();
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter "FullyQualifiedName~DeviceTypeCacheTests"
```

Expected: 5 tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/ControlMenu/Services/IDeviceTypeCache.cs src/ControlMenu/Services/DeviceTypeCache.cs tests/ControlMenu.Tests/Services/DeviceTypeCacheTests.cs tests/ControlMenu.Tests/Services/Fakes/FakeDeviceService.cs
git commit -m "feat(devices): add IDeviceTypeCache scoped service"
```

---

## Task 4: Register `IDeviceTypeCache` in DI

**Files:**
- Modify: `src/ControlMenu/Program.cs:51` (after `AddScoped<IDeviceService, DeviceService>`)

- [ ] **Step 1: Verify the current registration line**

```bash
grep -n "AddScoped<IDeviceService" src/ControlMenu/Program.cs
```

Expected output: `51:builder.Services.AddScoped<IDeviceService, DeviceService>();`

- [ ] **Step 2: Register the cache**

In `src/ControlMenu/Program.cs`, find the line:
```csharp
builder.Services.AddScoped<IDeviceService, DeviceService>();
```

Add immediately after it:
```csharp
builder.Services.AddScoped<IDeviceTypeCache, DeviceTypeCache>();
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Program.cs
git commit -m "feat(devices): register IDeviceTypeCache as scoped service"
```

---

## Task 5: Copy SVG icon assets into wwwroot

**Files:**
- Create: `src/ControlMenu/wwwroot/images/devices/device-list.svg`
- Create: `src/ControlMenu/wwwroot/images/devices/smart-tv.svg`
- Create: `src/ControlMenu/wwwroot/images/devices/smart-phone.svg`
- Create: `src/ControlMenu/wwwroot/images/devices/tablet.svg`
- Create: `src/ControlMenu/wwwroot/images/devices/smart-watch.svg`

- [ ] **Step 1: Create the target directory**

```bash
mkdir -p src/ControlMenu/wwwroot/images/devices
```

- [ ] **Step 2: Copy the five SVG files**

```bash
cp C:/Temp/tablets/device-list.svg  src/ControlMenu/wwwroot/images/devices/device-list.svg
cp C:/Temp/tablets/smart-tv.svg     src/ControlMenu/wwwroot/images/devices/smart-tv.svg
cp C:/Temp/tablets/smart-phone.svg  src/ControlMenu/wwwroot/images/devices/smart-phone.svg
cp C:/Temp/tablets/tablet.svg       src/ControlMenu/wwwroot/images/devices/tablet.svg
cp C:/Temp/tablets/smart-watch.svg  src/ControlMenu/wwwroot/images/devices/smart-watch.svg
```

- [ ] **Step 3: Verify files are in place**

```bash
ls -la src/ControlMenu/wwwroot/images/devices/
```

Expected: 5 `.svg` files listed.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/wwwroot/images/devices/
git commit -m "feat(sidebar): add custom SVG icons for Android Devices nav"
```

---

## Task 6: Update `AndroidDevicesModule.GetNavEntries` + tests

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs`
- Modify: `tests/ControlMenu.Tests/Modules/AndroidDevices/AndroidDevicesModuleTests.cs`

- [ ] **Step 1: Write failing test for 5 nav entries + IsVisible semantics**

Replace the existing `NavEntries_IncludesDeviceSelectorAndDashboards` test and add new tests. Open `tests/ControlMenu.Tests/Modules/AndroidDevices/AndroidDevicesModuleTests.cs` and replace its contents:

```csharp
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Modules;
using ControlMenu.Modules.AndroidDevices;
using ControlMenu.Services;
using ControlMenu.Tests.Services.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace ControlMenu.Tests.Modules.AndroidDevices;

public class AndroidDevicesModuleTests
{
    private readonly AndroidDevicesModule _module = new();

    [Fact]
    public void Id_IsAndroidDevices() => Assert.Equal("android-devices", _module.Id);

    [Fact]
    public void DisplayName_IsAndroidDevices() => Assert.Equal("Android Devices", _module.DisplayName);

    [Fact]
    public void Icon_IsPhoneIcon() => Assert.Equal("bi-phone", _module.Icon);

    [Fact]
    public void Dependencies_IncludesAdbAndScrcpy()
    {
        var deps = _module.Dependencies.ToList();
        Assert.Contains(deps, d => d.Name == "adb");
        Assert.Contains(deps, d => d.Name == "scrcpy");
    }

    [Fact]
    public void NavEntries_Includes5EntriesForDeviceListAndFourDeviceTypes()
    {
        var entries = _module.GetNavEntries().ToList();
        Assert.Equal(5, entries.Count);
        Assert.Contains(entries, e => e.Href == "/android/devices");
        Assert.Contains(entries, e => e.Href == "/android/googletv");
        Assert.Contains(entries, e => e.Href == "/android/phone");
        Assert.Contains(entries, e => e.Href == "/android/tablet");
        Assert.Contains(entries, e => e.Href == "/android/watch");
    }

    [Fact]
    public void NavEntries_UseSvgIconPaths()
    {
        var entries = _module.GetNavEntries().ToList();
        Assert.Equal("/images/devices/device-list.svg", entries.First(e => e.Href == "/android/devices").Icon);
        Assert.Equal("/images/devices/smart-tv.svg",    entries.First(e => e.Href == "/android/googletv").Icon);
        Assert.Equal("/images/devices/smart-phone.svg", entries.First(e => e.Href == "/android/phone").Icon);
        Assert.Equal("/images/devices/tablet.svg",      entries.First(e => e.Href == "/android/tablet").Icon);
        Assert.Equal("/images/devices/smart-watch.svg", entries.First(e => e.Href == "/android/watch").Icon);
    }

    [Fact]
    public void NavEntries_DeviceListIsAlwaysVisible()
    {
        var deviceList = _module.GetNavEntries().First(e => e.Href == "/android/devices");
        Assert.Null(deviceList.IsVisible);
    }

    [Fact]
    public void NavEntries_DeviceTypeEntriesHaveIsVisiblePredicate()
    {
        var entries = _module.GetNavEntries().ToList();
        foreach (var href in new[] { "/android/googletv", "/android/phone", "/android/tablet", "/android/watch" })
        {
            var entry = entries.First(e => e.Href == href);
            Assert.NotNull(entry.IsVisible);
        }
    }

    [Fact]
    public void NavEntries_PhoneEntry_IsVisible_TrueWhenCacheSaysTrue()
    {
        var phone = _module.GetNavEntries().First(e => e.Href == "/android/phone");
        var sp = BuildServiceProviderWithTypes(DeviceType.AndroidPhone);

        Assert.True(phone.IsVisible!(sp));
    }

    [Fact]
    public void NavEntries_PhoneEntry_IsVisible_FalseWhenCacheSaysFalse()
    {
        var phone = _module.GetNavEntries().First(e => e.Href == "/android/phone");
        var sp = BuildServiceProviderWithTypes(DeviceType.AndroidTablet);  // has tablet, not phone

        Assert.False(phone.IsVisible!(sp));
    }

    [Fact]
    public void GetBackgroundJobs_ReturnsEmpty() => Assert.Empty(_module.GetBackgroundJobs());

    [Fact]
    public void AdbDependency_HasCorrectVersionCommand()
    {
        var adb = _module.Dependencies.First(d => d.Name == "adb");
        Assert.Equal("adb --version", adb.VersionCommand);
        Assert.Equal("adb", adb.ExecutableName);
    }

    [Fact]
    public void ScrcpyDependency_HasGitHubSource()
    {
        var scrcpy = _module.Dependencies.First(d => d.Name == "scrcpy");
        Assert.Equal(UpdateSourceType.GitHub, scrcpy.SourceType);
        Assert.Equal("Genymobile/scrcpy", scrcpy.GitHubRepo);
    }

    private static IServiceProvider BuildServiceProviderWithTypes(params DeviceType[] typesPresent)
    {
        var services = new ServiceCollection();
        var fakeDeviceService = new FakeDeviceService();
        foreach (var t in typesPresent)
            fakeDeviceService.Devices.Add(new Device { Id = Guid.NewGuid(), Name = "D", Type = t, MacAddress = "aa", ModuleId = "android-devices" });

        services.AddSingleton<IDeviceService>(fakeDeviceService);
        services.AddSingleton<IDeviceTypeCache>(sp =>
        {
            var cache = new DeviceTypeCache(sp.GetRequiredService<IDeviceService>());
            cache.RefreshAsync().GetAwaiter().GetResult();
            return cache;
        });
        return services.BuildServiceProvider();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter "FullyQualifiedName~AndroidDevicesModuleTests"
```

Expected: FAIL on the new tests — current implementation has only 3 entries, uses emoji icons, no predicates.

- [ ] **Step 3: Update `AndroidDevicesModule.GetNavEntries`**

Open `src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs`. Replace the existing `GetNavEntries()` method (lines 93-98) with:

```csharp
    public IEnumerable<NavEntry> GetNavEntries() =>
    [
        new NavEntry("Device List",    "/android/devices",  "/images/devices/device-list.svg", 0),
        new NavEntry("Google TV",      "/android/googletv", "/images/devices/smart-tv.svg",    1, HasDevicesOfType(DeviceType.GoogleTV)),
        new NavEntry("Android Phone",  "/android/phone",    "/images/devices/smart-phone.svg", 2, HasDevicesOfType(DeviceType.AndroidPhone)),
        new NavEntry("Android Tablet", "/android/tablet",   "/images/devices/tablet.svg",      3, HasDevicesOfType(DeviceType.AndroidTablet)),
        new NavEntry("Android Watch",  "/android/watch",    "/images/devices/smart-watch.svg", 4, HasDevicesOfType(DeviceType.AndroidWatch)),
    ];

    private static Func<IServiceProvider, bool> HasDevicesOfType(DeviceType type) =>
        sp => sp.GetRequiredService<ControlMenu.Services.IDeviceTypeCache>().HasDevicesOfType(type);
```

Also add the using at the top of the file (after existing usings):

```csharp
using Microsoft.Extensions.DependencyInjection;
```

(Using the full `ControlMenu.Services.IDeviceTypeCache` qualifier avoids ambiguity with the nested `ControlMenu.Modules.AndroidDevices.Services` namespace.)

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter "FullyQualifiedName~AndroidDevicesModuleTests"
```

Expected: all module tests pass (including the 5 new IsVisible / icon-path / entry-count tests).

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs tests/ControlMenu.Tests/Modules/AndroidDevices/AndroidDevicesModuleTests.cs
git commit -m "feat(sidebar): dynamic Android nav entries with IsVisible + SVG icons"
```

---

## Task 7: Sidebar — prime cache, subscribe, filter, SVG rendering branch, CSS

**Files:**
- Modify: `src/ControlMenu/Components/Layout/Sidebar.razor`
- Modify: `src/ControlMenu/Components/Layout/Sidebar.razor.css`

- [ ] **Step 1: Add the SVG-rendering branch and cache integration in Sidebar.razor**

Open `src/ControlMenu/Components/Layout/Sidebar.razor`. Replace the top of the file (up through the `<NavLink>` block in the foreach):

Current lines 1-82 become:

```razor
@inject ModuleDiscoveryService ModuleDiscovery
@inject IJSRuntime JS
@inject ControlMenu.Services.IDeviceTypeCache DeviceTypeCache
@inject IServiceProvider ServiceProvider
@implements IDisposable

<nav class="sidebar @(Collapsed ? "collapsed" : "")">
    <div class="sidebar-header">
        @if (!Collapsed)
        {
            <a href="/" class="sidebar-title-link">
                <img src="/icon-192.png" alt="" class="sidebar-app-icon" />Control Menu
            </a>
        }
        <button class="sidebar-toggle" @onclick="ToggleCollapsed" title="@(Collapsed ? "Expand sidebar" : "Collapse sidebar")">
            <i class="bi @(Collapsed ? "bi-chevron-right" : "bi-chevron-left")"></i>
        </button>
    </div>

    <div class="sidebar-nav">
        @if (ModuleDiscovery.Modules.Count == 0)
        {
            @if (!Collapsed)
            {
                <div class="sidebar-empty">
                    <i class="bi bi-inbox"></i>
                    <span>No modules loaded</span>
                </div>
            }
        }
        else
        {
            @if (!Collapsed)
            {
                <div class="sidebar-expand-toggle">
                    <button @onclick="ToggleAll" title="@(_allExpanded ? "Collapse all" : "Expand all")">
                        <i class="bi @(_allExpanded ? "bi-chevron-bar-up" : "bi-chevron-bar-down")"></i>
                        <span>@(_allExpanded ? "Collapse all" : "Expand all")</span>
                    </button>
                </div>
            }

            @foreach (var module in ModuleDiscovery.Modules)
            {
                <div class="sidebar-group">
                    <div class="sidebar-group-header" @onclick="() => ToggleGroup(module.Id)">
                        @if (ModuleImageMap.TryGetValue(module.Id, out var imgPath))
                        {
                            <img src="@imgPath" alt="" class="sidebar-module-icon" />
                        }
                        else
                        {
                            <i class="bi @module.Icon"></i>
                        }
                        @if (!Collapsed)
                        {
                            <span>@module.DisplayName</span>
                            <i class="bi bi-chevron-@(IsGroupExpanded(module.Id) ? "up" : "down") sidebar-chevron"></i>
                        }
                    </div>

                    @if (IsGroupExpanded(module.Id) && !Collapsed)
                    {
                        <div class="sidebar-group-items">
                            @foreach (var entry in VisibleEntries(module))
                            {
                                <NavLink class="sidebar-link" href="@entry.Href" Match="NavLinkMatch.All">
                                    @if (entry.Icon is not null)
                                    {
                                        @if (entry.Icon.StartsWith("bi-"))
                                        {
                                            <i class="bi @entry.Icon"></i>
                                        }
                                        else if (entry.Icon.StartsWith("/") || entry.Icon.EndsWith(".svg"))
                                        {
                                            <img src="@entry.Icon" alt="" class="sidebar-nav-icon" />
                                        }
                                        else
                                        {
                                            <span class="nav-emoji">@entry.Icon</span>
                                        }
                                    }
                                    <span>@entry.Title</span>
                                </NavLink>
                            }
                        </div>
                    }
                </div>
            }
        }
    </div>

    <div class="sidebar-footer">
        <NavLink class="sidebar-link" href="/settings" Match="NavLinkMatch.Prefix">
            <span class="nav-emoji">⚙️</span>
            @if (!Collapsed)
            {
                <span>Settings</span>
            }
        </NavLink>
    </div>
</nav>
```

Note the two changes inside the existing markup:
1. `@foreach (var entry in VisibleEntries(module))` — was `@foreach (var entry in module.GetNavEntries().OrderBy(e => e.SortOrder))`
2. Added `else if (entry.Icon.StartsWith("/") || entry.Icon.EndsWith(".svg"))` branch for SVG `<img>` rendering.

- [ ] **Step 2: Replace the @code block**

In the same file, replace the entire `@code { ... }` block at the end with:

```razor
@code {
    private bool Collapsed { get; set; }
    private readonly HashSet<string> _expandedGroups = new();
    private bool _allExpanded;
    private readonly Dictionary<Modules.NavEntry, bool> _visibilityCache = new();

    private static readonly Dictionary<string, string> ModuleImageMap = new()
    {
        ["android-devices"] = "/images/android-logo.svg",
        ["jellyfin"] = "/images/jellyfin-logo.svg"
    };

    protected override async Task OnInitializedAsync()
    {
        await DeviceTypeCache.RefreshAsync();
        DeviceTypeCache.CacheUpdated += OnCacheUpdated;
    }

    private async void OnCacheUpdated()
    {
        await InvokeAsync(() =>
        {
            _visibilityCache.Clear();
            StateHasChanged();
        });
    }

    private IEnumerable<Modules.NavEntry> VisibleEntries(Modules.IToolModule module) =>
        module.GetNavEntries()
              .Where(entry => entry.IsVisible is null
                              || _visibilityCache.GetValueOrDefault(entry, EvaluateVisibility(entry)))
              .OrderBy(e => e.SortOrder);

    private bool EvaluateVisibility(Modules.NavEntry entry)
    {
        var visible = entry.IsVisible!.Invoke(ServiceProvider);
        _visibilityCache[entry] = visible;
        return visible;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var hasVisited = await JS.InvokeAsync<string?>("localStorage.getItem", "sidebar-initialized");
            var saved = await JS.InvokeAsync<string?>("localStorage.getItem", "sidebar-expanded-groups");
            if (hasVisited is not null)
            {
                _expandedGroups.Clear();
                if (!string.IsNullOrEmpty(saved))
                {
                    foreach (var id in saved.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        _expandedGroups.Add(id);
                }
            }
            else
            {
                foreach (var m in ModuleDiscovery.Modules)
                    _expandedGroups.Add(m.Id);
                await JS.InvokeVoidAsync("localStorage.setItem", "sidebar-initialized", "1");
                await PersistState();
            }
            UpdateAllExpandedFlag();
            StateHasChanged();
        }
    }

    private void ToggleCollapsed() => Collapsed = !Collapsed;

    private async Task ToggleGroup(string moduleId)
    {
        if (!_expandedGroups.Remove(moduleId))
            _expandedGroups.Add(moduleId);
        UpdateAllExpandedFlag();
        await PersistState();
    }

    private async Task ToggleAll()
    {
        if (_allExpanded)
        {
            _expandedGroups.Clear();
        }
        else
        {
            foreach (var m in ModuleDiscovery.Modules)
                _expandedGroups.Add(m.Id);
        }
        _allExpanded = !_allExpanded;
        await PersistState();
    }

    private void UpdateAllExpandedFlag()
    {
        _allExpanded = ModuleDiscovery.Modules.All(m => _expandedGroups.Contains(m.Id));
    }

    private async Task PersistState()
    {
        await JS.InvokeVoidAsync("localStorage.setItem", "sidebar-expanded-groups",
            string.Join(",", _expandedGroups));
    }

    private bool IsGroupExpanded(string moduleId) => _expandedGroups.Contains(moduleId);

    public void Dispose() => DeviceTypeCache.CacheUpdated -= OnCacheUpdated;
}
```

- [ ] **Step 3: Add `.sidebar-nav-icon` to Sidebar.razor.css**

Open `src/ControlMenu/Components/Layout/Sidebar.razor.css` and append:

```css
.sidebar-nav-icon {
    width: 1.25em;
    height: 1.25em;
    object-fit: contain;
    flex-shrink: 0;
}
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj
```

Expected: build succeeds.

- [ ] **Step 5: Run full test suite to catch regressions**

```bash
dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj
```

Expected: all existing tests still pass.

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Components/Layout/Sidebar.razor src/ControlMenu/Components/Layout/Sidebar.razor.css
git commit -m "feat(sidebar): reactive filter + SVG icon rendering branch"
```

---

## Task 8: Create `FakeNavigationManager` test double

**Files:**
- Create: `tests/ControlMenu.Tests/Services/Fakes/FakeNavigationManager.cs`

- [ ] **Step 1: Create the fake**

Create `tests/ControlMenu.Tests/Services/Fakes/FakeNavigationManager.cs`:

```csharp
using Microsoft.AspNetCore.Components;

namespace ControlMenu.Tests.Services.Fakes;

public sealed class FakeNavigationManager : NavigationManager
{
    public List<(string Uri, bool Replace)> Navigations { get; } = new();

    public FakeNavigationManager()
    {
        Initialize("http://localhost/", "http://localhost/");
    }

    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        Navigations.Add((uri, options.ReplaceHistoryEntry));
    }

    protected override void NavigateToCore(string uri, bool forceLoad)
    {
        Navigations.Add((uri, false));
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build tests/ControlMenu.Tests/ControlMenu.Tests.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add tests/ControlMenu.Tests/Services/Fakes/FakeNavigationManager.cs
git commit -m "test(fakes): add FakeNavigationManager for testing redirects"
```

---

## Task 9: `DeviceTypePresenceWatcher` — tests + implementation

**Files:**
- Create: `tests/ControlMenu.Tests/Modules/AndroidDevices/DeviceTypePresenceWatcherTests.cs`
- Create: `src/ControlMenu/Modules/AndroidDevices/Services/DeviceTypePresenceWatcher.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/ControlMenu.Tests/Modules/AndroidDevices/DeviceTypePresenceWatcherTests.cs`:

```csharp
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Tests.Services.Fakes;

namespace ControlMenu.Tests.Modules.AndroidDevices;

public class DeviceTypePresenceWatcherTests
{
    private readonly FakeDeviceService _deviceService = new();
    private readonly FakeNavigationManager _nav = new();

    private static Device MakePhone()
        => new() { Id = Guid.NewGuid(), Name = "P", Type = DeviceType.AndroidPhone, MacAddress = "aa", ModuleId = "android-devices" };

    [Fact]
    public async Task EnsurePresentOrRedirectAsync_NoDevicesOfType_Redirects()
    {
        using var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _nav, null);

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
        using var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _nav, null);

        var redirected = await watcher.EnsurePresentOrRedirectAsync();

        Assert.False(redirected);
        Assert.Empty(_nav.Navigations);
    }

    [Fact]
    public async Task DevicesChanged_LastDeviceDeleted_Redirects()
    {
        var phone = MakePhone();
        _deviceService.Devices.Add(phone);
        using var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _nav, null);
        await watcher.EnsurePresentOrRedirectAsync();

        _deviceService.Devices.Clear();
        _deviceService.RaiseChanged();
        await Task.Delay(50);  // let async-void handler settle

        Assert.Single(_nav.Navigations);
        Assert.Equal("/android/devices", _nav.Navigations[0].Uri);
    }

    [Fact]
    public async Task DevicesChanged_OtherDevicesPresent_InvokesInvalidateCallback_DoesNotRedirect()
    {
        _deviceService.Devices.Add(MakePhone());
        var invalidateCount = 0;
        using var watcher = new DeviceTypePresenceWatcher(
            DeviceType.AndroidPhone,
            _deviceService,
            _nav,
            () => { invalidateCount++; return Task.CompletedTask; });
        await watcher.EnsurePresentOrRedirectAsync();

        _deviceService.Devices.Add(MakePhone());
        _deviceService.RaiseChanged();
        await Task.Delay(50);

        Assert.Empty(_nav.Navigations);
        Assert.Equal(1, invalidateCount);
    }

    [Fact]
    public async Task DevicesChanged_AfterAlreadyRedirected_DoesNotRedirectAgain()
    {
        using var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _nav, null);
        await watcher.EnsurePresentOrRedirectAsync();   // first redirect

        _deviceService.RaiseChanged();
        await Task.Delay(50);

        Assert.Single(_nav.Navigations);  // still only the initial redirect
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromDevicesChanged()
    {
        _deviceService.Devices.Add(MakePhone());
        var watcher = new DeviceTypePresenceWatcher(DeviceType.AndroidPhone, _deviceService, _nav, null);
        await watcher.EnsurePresentOrRedirectAsync();

        watcher.Dispose();
        _deviceService.Devices.Clear();
        _deviceService.RaiseChanged();
        await Task.Delay(50);

        Assert.Empty(_nav.Navigations);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter "FullyQualifiedName~DeviceTypePresenceWatcherTests"
```

Expected: FAIL — `DeviceTypePresenceWatcher` doesn't exist.

- [ ] **Step 3: Implement the watcher**

Create `src/ControlMenu/Modules/AndroidDevices/Services/DeviceTypePresenceWatcher.cs`:

```csharp
using ControlMenu.Data.Enums;
using ControlMenu.Services;
using Microsoft.AspNetCore.Components;

namespace ControlMenu.Modules.AndroidDevices.Services;

public sealed class DeviceTypePresenceWatcher : IDisposable
{
    private readonly DeviceType _type;
    private readonly IDeviceService _deviceService;
    private readonly NavigationManager _nav;
    private readonly Func<Task>? _onInvalidateAsync;
    private bool _redirected;

    public DeviceTypePresenceWatcher(
        DeviceType type,
        IDeviceService deviceService,
        NavigationManager nav,
        Func<Task>? onInvalidateAsync)
    {
        _type = type;
        _deviceService = deviceService;
        _nav = nav;
        _onInvalidateAsync = onInvalidateAsync;
        _deviceService.DevicesChanged += OnDevicesChanged;
    }

    /// <summary>
    /// Initial-load check. Returns true if the caller should abort its init (a redirect happened).
    /// </summary>
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

    public void Dispose() => _deviceService.DevicesChanged -= OnDevicesChanged;
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter "FullyQualifiedName~DeviceTypePresenceWatcherTests"
```

Expected: 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Services/DeviceTypePresenceWatcher.cs tests/ControlMenu.Tests/Modules/AndroidDevices/DeviceTypePresenceWatcherTests.cs
git commit -m "feat(devices): add DeviceTypePresenceWatcher for dashboard redirect"
```

---

## Task 10: Compose the watcher into `PixelDashboard`

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor`

- [ ] **Step 1: Add `NavigationManager` injection and watcher field**

Open the file. In the `@code` block (after the existing `[Inject]` attributes, around line 84), add:

```csharp
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private DeviceTypePresenceWatcher? _presenceWatcher;
```

- [ ] **Step 2: Modify `OnInitializedAsync` to compose the watcher**

Find the method (starts at line 96). Replace the existing method with:

```csharp
    protected override async Task OnInitializedAsync()
    {
        _presenceWatcher = new DeviceTypePresenceWatcher(
            DeviceType.AndroidPhone,
            DeviceService,
            Nav,
            () => InvokeAsync(StateHasChanged));

        if (await _presenceWatcher.EnsurePresentOrRedirectAsync())
            return;

        if (DeviceId.HasValue)
        {
            _device = await DeviceService.GetDeviceAsync(DeviceId.Value);
        }
        else
        {
            var devices = await DeviceService.GetAllDevicesAsync();
            _device = devices.FirstOrDefault(d => d.Type == DeviceType.AndroidPhone && d.ModuleId == "android-devices");
        }

        if (_device is not null)
        {
            _pin = await Config.GetSecretAsync($"device-pin-{_device.Id}");
            _hasPin = !string.IsNullOrEmpty(_pin);
        }

        if (_device?.LastKnownIp is not null)
        {
            _connected = await AdbService.ConnectAsync(_device.LastKnownIp, _device.AdbPort);

            // Query actual screen dimensions for mirror sizing
            if (_connected)
            {
                var size = await AdbService.GetScreenSizeAsync(_device.LastKnownIp, _device.AdbPort);
                if (size is not null)
                {
                    _screenW = size.Value.Width;
                    _screenH = size.Value.Height;
                }
            }
        }
    }
```

- [ ] **Step 3: Modify `Dispose` to dispose the watcher**

Find the existing `Dispose` method (around line 211):

```csharp
    public void Dispose()
    {
        _statusCts?.Cancel();
        _statusCts?.Dispose();
    }
```

Replace with:

```csharp
    public void Dispose()
    {
        _presenceWatcher?.Dispose();
        _statusCts?.Cancel();
        _statusCts?.Dispose();
    }
```

- [ ] **Step 4: Add missing using (if needed)**

At the top of the file, verify `@using ControlMenu.Modules.AndroidDevices.Services` is present (it is in the existing file at line 9). No change needed.

- [ ] **Step 5: Build to verify**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj
```

Expected: build succeeds.

- [ ] **Step 6: Run test suite**

```bash
dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor
git commit -m "feat(phone): redirect to device list when last phone is deleted"
```

---

## Task 11: Compose the watcher into `GoogleTvDashboard`

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor`

- [ ] **Step 1: Read the existing `OnInitializedAsync` + `Dispose`**

```bash
grep -n "OnInitializedAsync\|public void Dispose" src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor
```

Verify the method locations (should be in the `@code` block).

- [ ] **Step 2: Apply the same pattern as `PixelDashboard` with `DeviceType.GoogleTV`**

In `src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor`:

a) Add after the other `[Inject]` attributes in `@code`:
```csharp
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private DeviceTypePresenceWatcher? _presenceWatcher;
```

b) At the top of `OnInitializedAsync`, add:
```csharp
        _presenceWatcher = new DeviceTypePresenceWatcher(
            DeviceType.GoogleTV,
            DeviceService,
            Nav,
            () => InvokeAsync(StateHasChanged));

        if (await _presenceWatcher.EnsurePresentOrRedirectAsync())
            return;
```

c) At the top of the existing `Dispose` method, add:
```csharp
        _presenceWatcher?.Dispose();
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj
```

Expected: build succeeds.

- [ ] **Step 4: Run test suite**

```bash
dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor
git commit -m "feat(googletv): redirect to device list when last TV is deleted"
```

---

## Task 12: Create `TabletDashboard.razor`

**Files:**
- Create: `src/ControlMenu/Modules/AndroidDevices/Pages/TabletDashboard.razor`

- [ ] **Step 1: Create the file**

Create `src/ControlMenu/Modules/AndroidDevices/Pages/TabletDashboard.razor` with the following complete content (cloned from `PixelDashboard.razor` with the six differences):

```razor
@page "/android/tablet"
@page "/android/tablet/{DeviceId:guid}"
@using ControlMenu.Data.Entities
@using ControlMenu.Data.Enums
@using ControlMenu.Services
@implements IDisposable
@using ControlMenu.Modules.AndroidDevices.Services

<PageTitle>Android Tablet - @(_device?.Name ?? "Select Device")</PageTitle>

@if (_device is null)
{
    <h1><i class="bi bi-tablet-landscape"></i> Android Tablet</h1>
    <p>No device selected. <a href="/android/devices">Select a device</a>.</p>
}
else
{
    <div class="device-header">
        <h1><i class="bi bi-tablet-landscape"></i> Android Tablet</h1>
        <span class="status-badge @(_connected ? "connected" : "disconnected")">
            <i class="bi @(_connected ? "bi-wifi" : "bi-wifi-off")"></i>
            @(_connected ? "Connected" : "Disconnected")
        </span>
    </div>

    @if (!string.IsNullOrEmpty(_statusMessage))
    {
        <div class="status-bar @_statusClass">
            <i class="bi @_statusIcon"></i> @_statusMessage
        </div>
    }

    <div class="dashboard-layout">
        <div class="controls-panel">
            <h3 class="panel-heading">Quick Actions</h3>

            <!-- ADB Connect -->
            <div class="action-row">
                <div class="action-label">
                    <i class="bi bi-plug"></i> ADB Connect
                </div>
                <span class="action-status">WiFi connection</span>
                <div class="action-buttons">
                    <button class="btn btn-sm btn-primary" @onclick="ConnectDevice" disabled="@_busy">Connect</button>
                </div>
            </div>

            <!-- Unlock Tablet -->
            <div class="action-row">
                <div class="action-label">
                    <i class="bi bi-unlock"></i> Unlock Tablet
                </div>
                @if (_hasPin)
                {
                    <span class="action-status">Send PIN to unlock screen</span>
                    <div class="action-buttons">
                        <button class="btn btn-sm btn-success" @onclick="UnlockPhone" disabled="@(_busy || !_connected)">Unlock</button>
                    </div>
                }
                else
                {
                    <span class="action-status">Set PIN in <a href="/settings/devices">Device Settings</a></span>
                }
            </div>
        </div>

        <div class="mirror-column">
            <span class="device-name">@_device.Name</span>
            <div class="mirror-panel" style="aspect-ratio: @MirrorAspectRatio;">
                <ScrcpyMirror Udid="@($"{Ip}:{Port}")" Inline="true" DeviceKind="tablet" />
            </div>
        </div>
    </div>
}

@code {
    [Parameter] public Guid? DeviceId { get; set; }
    [Inject] private IDeviceService DeviceService { get; set; } = default!;
    [Inject] private IAdbService AdbService { get; set; } = default!;
    [Inject] private INetworkDiscoveryService NetworkDiscovery { get; set; } = default!;
    [Inject] private IConfigurationService Config { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private DeviceTypePresenceWatcher? _presenceWatcher;
    private Device? _device;
    private bool _connected;
    private bool _busy;
    private bool _hasPin;
    private string? _pin;
    private int _screenW = 9;
    private int _screenH = 20;
    private string _statusMessage = "";
    private string _statusClass = "";
    private string _statusIcon = "";

    protected override async Task OnInitializedAsync()
    {
        _presenceWatcher = new DeviceTypePresenceWatcher(
            DeviceType.AndroidTablet,
            DeviceService,
            Nav,
            () => InvokeAsync(StateHasChanged));

        if (await _presenceWatcher.EnsurePresentOrRedirectAsync())
            return;

        if (DeviceId.HasValue)
        {
            _device = await DeviceService.GetDeviceAsync(DeviceId.Value);
        }
        else
        {
            var devices = await DeviceService.GetAllDevicesAsync();
            _device = devices.FirstOrDefault(d => d.Type == DeviceType.AndroidTablet && d.ModuleId == "android-devices");
        }

        if (_device is not null)
        {
            _pin = await Config.GetSecretAsync($"device-pin-{_device.Id}");
            _hasPin = !string.IsNullOrEmpty(_pin);
        }

        if (_device?.LastKnownIp is not null)
        {
            _connected = await AdbService.ConnectAsync(_device.LastKnownIp, _device.AdbPort);

            if (_connected)
            {
                var size = await AdbService.GetScreenSizeAsync(_device.LastKnownIp, _device.AdbPort);
                if (size is not null)
                {
                    _screenW = size.Value.Width;
                    _screenH = size.Value.Height;
                }
            }
        }
    }

    private string Ip => _device?.LastKnownIp ?? "";
    private int Port => _device?.AdbPort ?? 5555;

    private string MirrorAspectRatio
    {
        get
        {
            const double toolbarPx = 52.0;
            const double refHeight = 700.0;
            var videoWidth = refHeight * _screenW / _screenH;
            var totalWidth = videoWidth + toolbarPx;
            return $"{totalWidth:F0} / {refHeight:F0}";
        }
    }

    private async Task UnlockPhone()
    {
        if (_device is null || string.IsNullOrEmpty(Ip) || string.IsNullOrEmpty(_pin)) return;
        _busy = true;
        await AdbService.UnlockWithPinAsync(Ip, Port, _pin);
        SetStatus("Tablet unlocked.", "status-success", "bi-unlock");
        _busy = false;
    }

    private async Task ConnectDevice()
    {
        if (_device is null) return;

        if (_device.LastKnownIp is null && !string.IsNullOrEmpty(_device.MacAddress))
        {
            var ip = await NetworkDiscovery.ResolveIpFromMacAsync(_device.MacAddress);
            if (ip is not null)
            {
                _device.LastKnownIp = ip;
                await DeviceService.UpdateLastSeenAsync(_device.Id, ip);
            }
        }

        if (_device.LastKnownIp is null)
        {
            SetStatus("No IP address known for this device. Run a network scan in Settings.", "status-warning", "bi-exclamation-triangle");
            return;
        }
        _busy = true;
        _connected = await AdbService.ConnectAsync(Ip, Port);
        if (_connected)
        {
            await DeviceService.UpdateLastSeenAsync(_device.Id, _device.LastKnownIp);
        }
        SetStatus(_connected ? "Connected successfully." : "Connection failed.", _connected ? "status-success" : "status-warning", _connected ? "bi-check-circle" : "bi-x-circle");
        _busy = false;
    }

    private CancellationTokenSource? _statusCts;

    private void SetStatus(string message, string cssClass, string icon, int dismissMs = 5000)
    {
        _statusMessage = message;
        _statusClass = cssClass;
        _statusIcon = icon;

        _statusCts?.Cancel();
        _statusCts = new CancellationTokenSource();
        var token = _statusCts.Token;

        _ = Task.Delay(dismissMs, token).ContinueWith(_ =>
        {
            InvokeAsync(() =>
            {
                _statusMessage = "";
                StateHasChanged();
            });
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    public void Dispose()
    {
        _presenceWatcher?.Dispose();
        _statusCts?.Cancel();
        _statusCts?.Dispose();
    }
}
```

Note: the method name `UnlockPhone` stays as-is (it's the internal name used by the button's `@onclick`); only the **button label** "Unlock Tablet" and **status message** "Tablet unlocked." change. Renaming the method is scope creep — leave it.

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Run full test suite**

```bash
dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj
```

Expected: all tests pass (no tablet-dashboard unit tests added; behavior mirrored from Phone which is already tested implicitly through existing integration).

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Pages/TabletDashboard.razor
git commit -m "feat(tablet): add Android Tablet dashboard"
```

---

## Task 13: Create `WatchDashboard.razor`

**Files:**
- Create: `src/ControlMenu/Modules/AndroidDevices/Pages/WatchDashboard.razor`

- [ ] **Step 1: Create the file**

Create `src/ControlMenu/Modules/AndroidDevices/Pages/WatchDashboard.razor` with the following full content:

```razor
@page "/android/watch"
@page "/android/watch/{DeviceId:guid}"
@using ControlMenu.Data.Entities
@using ControlMenu.Data.Enums
@using ControlMenu.Services
@implements IDisposable
@using ControlMenu.Modules.AndroidDevices.Services

<PageTitle>Android Watch - @(_device?.Name ?? "Select Device")</PageTitle>

@if (_device is null)
{
    <h1><i class="bi bi-smartwatch"></i> Android Watch</h1>
    <p>No device selected. <a href="/android/devices">Select a device</a>.</p>
}
else
{
    <div class="device-header">
        <h1><i class="bi bi-smartwatch"></i> Android Watch</h1>
        <span class="status-badge @(_connected ? "connected" : "disconnected")">
            <i class="bi @(_connected ? "bi-wifi" : "bi-wifi-off")"></i>
            @(_connected ? "Connected" : "Disconnected")
        </span>
    </div>

    @if (!string.IsNullOrEmpty(_statusMessage))
    {
        <div class="status-bar @_statusClass">
            <i class="bi @_statusIcon"></i> @_statusMessage
        </div>
    }

    <div class="dashboard-layout">
        <div class="controls-panel">
            <h3 class="panel-heading">Quick Actions</h3>

            <!-- ADB Connect -->
            <div class="action-row">
                <div class="action-label">
                    <i class="bi bi-plug"></i> ADB Connect
                </div>
                <span class="action-status">WiFi connection</span>
                <div class="action-buttons">
                    <button class="btn btn-sm btn-primary" @onclick="ConnectDevice" disabled="@_busy">Connect</button>
                </div>
            </div>

            <!-- Unlock Watch -->
            <div class="action-row">
                <div class="action-label">
                    <i class="bi bi-unlock"></i> Unlock Watch
                </div>
                @if (_hasPin)
                {
                    <span class="action-status">Send PIN to unlock screen</span>
                    <div class="action-buttons">
                        <button class="btn btn-sm btn-success" @onclick="UnlockPhone" disabled="@(_busy || !_connected)">Unlock</button>
                    </div>
                }
                else
                {
                    <span class="action-status">Set PIN in <a href="/settings/devices">Device Settings</a></span>
                }
            </div>
        </div>

        <div class="mirror-column">
            <span class="device-name">@_device.Name</span>
            <div class="mirror-panel" style="aspect-ratio: @MirrorAspectRatio;">
                <ScrcpyMirror Udid="@($"{Ip}:{Port}")" Inline="true" DeviceKind="watch" />
            </div>
        </div>
    </div>
}

@code {
    [Parameter] public Guid? DeviceId { get; set; }
    [Inject] private IDeviceService DeviceService { get; set; } = default!;
    [Inject] private IAdbService AdbService { get; set; } = default!;
    [Inject] private INetworkDiscoveryService NetworkDiscovery { get; set; } = default!;
    [Inject] private IConfigurationService Config { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private DeviceTypePresenceWatcher? _presenceWatcher;
    private Device? _device;
    private bool _connected;
    private bool _busy;
    private bool _hasPin;
    private string? _pin;
    private int _screenW = 9;
    private int _screenH = 20;
    private string _statusMessage = "";
    private string _statusClass = "";
    private string _statusIcon = "";

    protected override async Task OnInitializedAsync()
    {
        _presenceWatcher = new DeviceTypePresenceWatcher(
            DeviceType.AndroidWatch,
            DeviceService,
            Nav,
            () => InvokeAsync(StateHasChanged));

        if (await _presenceWatcher.EnsurePresentOrRedirectAsync())
            return;

        if (DeviceId.HasValue)
        {
            _device = await DeviceService.GetDeviceAsync(DeviceId.Value);
        }
        else
        {
            var devices = await DeviceService.GetAllDevicesAsync();
            _device = devices.FirstOrDefault(d => d.Type == DeviceType.AndroidWatch && d.ModuleId == "android-devices");
        }

        if (_device is not null)
        {
            _pin = await Config.GetSecretAsync($"device-pin-{_device.Id}");
            _hasPin = !string.IsNullOrEmpty(_pin);
        }

        if (_device?.LastKnownIp is not null)
        {
            _connected = await AdbService.ConnectAsync(_device.LastKnownIp, _device.AdbPort);

            if (_connected)
            {
                var size = await AdbService.GetScreenSizeAsync(_device.LastKnownIp, _device.AdbPort);
                if (size is not null)
                {
                    _screenW = size.Value.Width;
                    _screenH = size.Value.Height;
                }
            }
        }
    }

    private string Ip => _device?.LastKnownIp ?? "";
    private int Port => _device?.AdbPort ?? 5555;

    private string MirrorAspectRatio
    {
        get
        {
            const double toolbarPx = 52.0;
            const double refHeight = 700.0;
            var videoWidth = refHeight * _screenW / _screenH;
            var totalWidth = videoWidth + toolbarPx;
            return $"{totalWidth:F0} / {refHeight:F0}";
        }
    }

    private async Task UnlockPhone()
    {
        if (_device is null || string.IsNullOrEmpty(Ip) || string.IsNullOrEmpty(_pin)) return;
        _busy = true;
        await AdbService.UnlockWithPinAsync(Ip, Port, _pin);
        SetStatus("Watch unlocked.", "status-success", "bi-unlock");
        _busy = false;
    }

    private async Task ConnectDevice()
    {
        if (_device is null) return;

        if (_device.LastKnownIp is null && !string.IsNullOrEmpty(_device.MacAddress))
        {
            var ip = await NetworkDiscovery.ResolveIpFromMacAsync(_device.MacAddress);
            if (ip is not null)
            {
                _device.LastKnownIp = ip;
                await DeviceService.UpdateLastSeenAsync(_device.Id, ip);
            }
        }

        if (_device.LastKnownIp is null)
        {
            SetStatus("No IP address known for this device. Run a network scan in Settings.", "status-warning", "bi-exclamation-triangle");
            return;
        }
        _busy = true;
        _connected = await AdbService.ConnectAsync(Ip, Port);
        if (_connected)
        {
            await DeviceService.UpdateLastSeenAsync(_device.Id, _device.LastKnownIp);
        }
        SetStatus(_connected ? "Connected successfully." : "Connection failed.", _connected ? "status-success" : "status-warning", _connected ? "bi-check-circle" : "bi-x-circle");
        _busy = false;
    }

    private CancellationTokenSource? _statusCts;

    private void SetStatus(string message, string cssClass, string icon, int dismissMs = 5000)
    {
        _statusMessage = message;
        _statusClass = cssClass;
        _statusIcon = icon;

        _statusCts?.Cancel();
        _statusCts = new CancellationTokenSource();
        var token = _statusCts.Token;

        _ = Task.Delay(dismissMs, token).ContinueWith(_ =>
        {
            InvokeAsync(() =>
            {
                _statusMessage = "";
                StateHasChanged();
            });
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    public void Dispose()
    {
        _presenceWatcher?.Dispose();
        _statusCts?.Cancel();
        _statusCts?.Dispose();
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Run full test suite**

```bash
dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Pages/WatchDashboard.razor
git commit -m "feat(watch): add Android Watch dashboard"
```

---

## Task 14: Rewrite `WizardDevices.razor` to embed the scanner

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Setup/WizardDevices.razor`

- [ ] **Step 1: Verify `IScanLifecycleHandler` + `DiscoveredPanel` usage pattern**

Reference the existing `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor` for the canonical orchestration of `IScanLifecycleHandler` + `DiscoveredPanel` + `DiscoveredPanelRow`. The wizard replicates the Quick Refresh path (not the full Scan Modal — onboarding doesn't need CIDR/IP/range subnet configuration).

- [ ] **Step 2: Replace the file contents**

Replace the entire contents of `src/ControlMenu/Components/Pages/Setup/WizardDevices.razor` with:

```razor
@using ControlMenu.Components.Shared.Scanner
@using ControlMenu.Data.Entities
@using ControlMenu.Data.Enums
@using ControlMenu.Modules.AndroidDevices.Services
@using ControlMenu.Services
@using ControlMenu.Services.Network
@inject IDeviceService DeviceService
@inject INetworkDiscoveryService NetworkDiscovery
@inject IConfigurationService Config
@inject IScanLifecycleHandler Handler
@implements IDisposable

<div class="settings-section">
    <h2>Android Devices</h2>
    <p>Scan your network to discover Android devices, then add each one with a click. You can also add devices later from <a href="/settings/devices">Settings › Devices</a>.</p>

    <div class="toolbar" style="margin-bottom:1rem;">
        <button class="btn btn-secondary" @onclick="QuickRefresh" disabled="@_scanning">
            <i class="bi bi-arrow-clockwise"></i> @(_scanning ? "Scanning..." : "Scan Network")
        </button>
    </div>

    @if (Handler.Phase is not ScanPhase.Idle)
    {
        <div class="scan-row">
            <ScanProgressChip Phase="Handler.Phase"
                              Checked="Handler.LastProgress?.Checked ?? 0"
                              Total="Handler.LastProgress?.Total ?? 0"
                              FoundSoFar="Handler.Discovered.Count" />
        </div>
    }

    <DiscoveredPanel Discovered="Handler.Discovered"
                     Registered="_devices"
                     OnAdd="HandleInlineAdd"
                     OnDismiss="Handler.Dismiss" />

    @if (_devices.Count > 0)
    {
        <h3 style="margin-top: 1.5rem;">Registered devices</h3>
        <table class="data-table">
            <thead>
                <tr><th>Name</th><th>Type</th><th>MAC</th><th>Port</th></tr>
            </thead>
            <tbody>
                @foreach (var device in _devices)
                {
                    <tr>
                        <td>@device.Name</td>
                        <td>@device.Type</td>
                        <td><code>@device.MacAddress</code></td>
                        <td>@device.AdbPort</td>
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

    private List<Device> _devices = [];
    private bool _scanning;
    private string? _message;
    private bool _messageIsError;

    protected override async Task OnInitializedAsync()
    {
        await LoadDevices();
        DeviceService.DevicesChanged += OnDevicesChanged;
        Handler.OnStateChanged += HandleHandlerStateChanged;
    }

    private async void OnDevicesChanged() => await InvokeAsync(async () =>
    {
        await LoadDevices();
        StateHasChanged();
    });

    private void HandleHandlerStateChanged()
    {
        _ = InvokeAsync(() =>
        {
            var err = Handler.ConsumeLastError();
            if (err is not null)
            {
                _message = err;
                _messageIsError = true;
            }
            StateHasChanged();
        });
    }

    private async Task LoadDevices()
    {
        _devices = (await DeviceService.GetAllDevicesAsync()).ToList();
        State.DevicesAdded = _devices.Count;
    }

    private async Task QuickRefresh()
    {
        _scanning = true;
        _message = null;
        try
        {
            await Handler.QuickRefreshAsync();
        }
        finally
        {
            _scanning = false;
        }
    }

    private async Task HandleInlineAdd(InlineAddPayload payload)
    {
        try
        {
            var device = new Device
            {
                Id = Guid.NewGuid(),
                Name = payload.Name,
                Type = payload.Type,
                AdbPort = payload.AdbPort,
                MacAddress = NetworkDiscoveryService.NormalizeMac(payload.Mac),
                SerialNumber = payload.Serial,
                LastKnownIp = payload.Ip,
                ModuleId = "android-devices",
            };
            await DeviceService.AddDeviceAsync(device);

            if (!string.IsNullOrEmpty(payload.Pin))
                await Config.SetSecretAsync($"device-pin-{device.Id}", payload.Pin);

            Handler.ReplaceDiscovered(Handler.Discovered
                .Where(d => !string.Equals(d.Mac, device.MacAddress, StringComparison.OrdinalIgnoreCase)));

            _message = $"Added {device.Name}.";
            _messageIsError = false;
        }
        catch (Exception ex)
        {
            _message = $"Failed to add device: {ex.Message}";
            _messageIsError = true;
        }
    }

    public void Dispose()
    {
        DeviceService.DevicesChanged -= OnDevicesChanged;
        Handler.OnStateChanged -= HandleHandlerStateChanged;
    }
}
```

**Verification notes on scan handler API surface used above** (verify these against `IScanLifecycleHandler`'s actual interface before building):
- `Handler.Phase` (enum)
- `Handler.Discovered` (IReadOnlyList)
- `Handler.LastProgress` (nullable with `.Checked` and `.Total`)
- `Handler.QuickRefreshAsync()`
- `Handler.OnStateChanged` (event Action)
- `Handler.ConsumeLastError()` (returns string?)
- `Handler.Dismiss` (EventCallback<DiscoveredDevice>)
- `Handler.ReplaceDiscovered(IEnumerable<DiscoveredDevice>)`
- `InlineAddPayload` has: `Name`, `Type`, `AdbPort`, `Mac`, `Serial`, `Ip`, `Pin`

These match the calls made in `DeviceManagement.razor`. If any member name differs, reuse the pattern from `DeviceManagement.razor` verbatim.

- [ ] **Step 3: Build to verify**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj
```

Expected: build succeeds. **If it fails** due to mismatched `IScanLifecycleHandler` / `InlineAddPayload` member names, correct them by copying the exact call sites from `DeviceManagement.razor`'s code block.

- [ ] **Step 4: Run test suite**

```bash
dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj
```

Expected: all existing tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Components/Pages/Setup/WizardDevices.razor
git commit -m "feat(wizard): replace hand-rolled form with scanner panel"
```

---

## Task 15: Update manual test checklist

**Files:**
- Modify: `docs/manual-test-checklist.md`

- [ ] **Step 1: Append section 5e**

Open `docs/manual-test-checklist.md`. Find the end of section 5d (the scanner-related section). Insert a new section 5e after it:

```markdown
## 5e. A1+A4 dynamic nav and dashboards

**Empty-inventory start:**
- [ ] Fresh install or all devices deleted. Sidebar shows only "Device List" under Android Devices.
- [ ] Navigate to `/android/phone` directly in the URL bar → redirected to `/android/devices`.

**First-device emergence:**
- [ ] On Device List page, scan and add an Android Phone. Sidebar updates within one render cycle to show "Android Phone" entry.
- [ ] Click the Android Phone entry → `/android/phone` loads the newly added device.

**Multi-type sidebar:**
- [ ] Add an Android Tablet. Both "Android Phone" and "Android Tablet" entries present in sidebar.
- [ ] Add a Google TV device. "Google TV" entry present too.

**Last-device deletion while on its dashboard:**
- [ ] Navigate to `/android/tablet`. Delete the only tablet from another browser tab or via the scanner. Current tab auto-redirects to `/android/devices`.
- [ ] Repeat for phone (`/android/phone`) and Google TV (`/android/googletv`).

**Watch dashboard cold load (no hardware test possible):**
- [ ] Register a watch device (manually via DB or via scanner if the device advertises as a watch). Navigate to `/android/watch`. Page renders with "Unlock Watch" label. `ScrcpyMirror` iframe is wired.

**Wizard flow (from fresh install):**
- [ ] Walk the setup wizard. On Devices step, `DiscoveredPanel` renders, Scan Network button works, discovered list populates.
- [ ] Use inline add on a discovered row → device appears in the "Registered devices" table below.
- [ ] Complete wizard → `/` → sidebar shows the added device types.

**Collapsed sidebar:**
- [ ] Toggle sidebar collapsed. Device type entries still render as SVG icons (no text).
- [ ] Click the Android Phone SVG icon → navigates correctly to `/android/phone`.

**Icon visual check in both themes:**
- [ ] Verify custom SVG icons render correctly in light mode.
- [ ] Verify custom SVG icons render correctly in dark mode.
- [ ] Flag any contrast issues with the device-list icon (lighter content against dark sidebar).
```

- [ ] **Step 2: Commit**

```bash
git add docs/manual-test-checklist.md
git commit -m "docs(test-checklist): add section 5e for dynamic nav + dashboards"
```

---

## Task 16: Add Watch hardware caveat to `TECHNICAL_GUIDE.md`

**Files:**
- Modify: `docs/TECHNICAL_GUIDE.md`

- [ ] **Step 1: Find the Android Devices section**

```bash
grep -n "Android Devices\|## Modules" docs/TECHNICAL_GUIDE.md | head -20
```

- [ ] **Step 2: Add the caveat**

In `docs/TECHNICAL_GUIDE.md`, locate the Android Devices module section (or the Dashboards subsection within it). Append a paragraph:

```markdown
### Watch dashboard — unverified on real hardware

The Android Watch dashboard (`/android/watch`) ships as a near-clone of the Android Phone dashboard and has not been verified against a physical Wear OS device. Code parity with Phone means ADB-connect, PIN unlock, and scrcpy mirror all wire up identically; please report any watch-specific issues so we can iterate.
```

If the Android Devices module section doesn't have an obvious anchor, append the paragraph at the end of the file's "Known limitations" or equivalent section. If neither exists, append at the end of the file.

- [ ] **Step 3: Commit**

```bash
git add docs/TECHNICAL_GUIDE.md
git commit -m "docs(technical-guide): flag Watch dashboard as unverified on hardware"
```

---

## Task 17: Full regression pass

**Files:** none (verification only).

- [ ] **Step 1: Full build**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj -c Release
```

Expected: build succeeds with zero warnings beyond existing ones.

- [ ] **Step 2: Full test suite**

```bash
dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj
```

Expected: all tests pass. Count should include:
- Pre-existing tests (225+ per `project_control_menu_scanner_port.md`).
- 4 new `DeviceServiceTests` (event-raising tests).
- 5 new `DeviceTypeCacheTests`.
- 6 new `DeviceTypePresenceWatcherTests`.
- 5 new `AndroidDevicesModuleTests` (nav entry shape + predicate semantics).

- [ ] **Step 3: Run the app and sanity-check the sidebar**

```bash
dotnet run --project src/ControlMenu/ControlMenu.csproj -c Release
```

Browse to http://localhost:5159.

- [ ] Sidebar renders with custom SVG icons for existing device types (if any devices registered).
- [ ] With zero devices registered: sidebar shows only "Device List" under Android Devices.
- [ ] Add a phone via scanner → "Android Phone" entry appears in sidebar without page reload.
- [ ] Delete the phone → "Android Phone" entry disappears; if viewing `/android/phone`, page redirects to `/android/devices`.

If any of the above fails, STOP and report. Do not commit additional changes until root-caused.

- [ ] **Step 4: No commit** — this is a verification-only task.

---

## Done

All 16 implementation tasks complete + regression verified.
