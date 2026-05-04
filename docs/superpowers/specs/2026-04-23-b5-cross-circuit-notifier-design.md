# B-5: Cross-Circuit Device Change Notifier

**Date:** 2026-04-23
**Status:** Approved design, pending implementation plan
**Priority:** Top of backlog (surfaced during A1+A4 smoke test)

## Problem

`IDeviceTypeCache` and `IDeviceService` are both registered as **scoped** (one instance per Blazor Server circuit). When Tab A mutates devices, only Tab A's scoped `DeviceService` raises `DevicesChanged`. Tab B's `DeviceTypeCache` — bound to a different scoped `DeviceService` instance — never receives the event. Tab B's sidebar nav stays stale until the user triggers a mutation in that tab.

The `DeviceTypePresenceWatcher` redirect still works in Tab B (it re-queries the DB on navigation), but the nav entries linger with stale visibility.

## Solution

Introduce a **singleton `IDeviceChangeNotifier`** service that relays "devices changed" events across all circuits. Scoped services subscribe to the singleton; mutations on any circuit notify all subscribers.

This is the standard Blazor Server pattern for scoped-consumer-of-singleton-producer.

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Architecture | Singleton notifier (additive) | Minimum blast radius; no lifetime changes to existing services |
| Event shape | Bare `Action` (no payload) | Both subscribers already do full DB refresh; typed args is YAGNI |
| Invoke safety | Snapshot delegate list + per-handler try/catch | One dying circuit must not silence notifications to others |
| Scoped `DevicesChanged` event | Remove entirely | Single notification path; avoids two parallel event systems |
| Notifier API style | `event Action Changed` + `NotifyChanged()` | Matches existing codebase `+=`/`-=` patterns |

## New Components

### IDeviceChangeNotifier (interface)

```csharp
public interface IDeviceChangeNotifier
{
    event Action Changed;
    void NotifyChanged();
}
```

### DeviceChangeNotifier (singleton implementation)

- Registered as `AddSingleton<IDeviceChangeNotifier, DeviceChangeNotifier>()`
- `NotifyChanged()` snapshots the delegate invocation list via `Changed?.GetInvocationList()`, iterates each handler in a try/catch so one faulting subscriber does not prevent delivery to the rest
- Thread-safe: C# event accessor locking handles concurrent `+=`/`-=`

### FakeDeviceChangeNotifier (test double)

- Implements `IDeviceChangeNotifier`
- `RaiseChanged()` method for manual event triggering from tests
- `NotifyChangedCallCount` property for asserting that `DeviceService` called `NotifyChanged()` after mutations
- Lives in `tests/ControlMenu.Tests/Services/Fakes/`

## Modified Components

### IDeviceService / DeviceService

- **Remove** `event Action DevicesChanged` from the interface and implementation
- **Add** `IDeviceChangeNotifier` constructor dependency
- After `AddDeviceAsync`, `UpdateDeviceAsync`, `DeleteDeviceAsync`: call `_notifier.NotifyChanged()`
- `UpdateLastSeenAsync` continues to NOT notify (unchanged behavior)
- Registration stays `AddScoped`

### DeviceTypeCache

- **Add** `IDeviceChangeNotifier` constructor dependency (keeps `IDeviceService` for query access)
- Subscribe to `_notifier.Changed += OnDevicesChanged` in constructor (replaces `_deviceService.DevicesChanged`)
- `Dispose()`: unsubscribe from `_notifier.Changed`
- `OnDevicesChanged` handler unchanged — calls `RefreshAsync()` which queries DB, then raises `CacheUpdated`

### DeviceTypePresenceWatcher

- **Add** `IDeviceChangeNotifier` constructor dependency (keeps `IDeviceService` for query access)
- Subscribe to `_notifier.Changed += OnDevicesChanged` in constructor (replaces `_deviceService.DevicesChanged`)
- `Dispose()`: unsubscribe from `_notifier.Changed`
- Handler unchanged — re-queries DB fresh, redirects if empty

### Program.cs

```csharp
// New singleton
builder.Services.AddSingleton<IDeviceChangeNotifier, DeviceChangeNotifier>();

// Existing — unchanged
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IDeviceTypeCache, DeviceTypeCache>();
```

### Sidebar

No changes. Subscribes to `DeviceTypeCache.CacheUpdated`, not `DevicesChanged`.

## Test Changes

### FakeDeviceService cleanup

- Remove `event Action DevicesChanged` and `RaiseChanged()` method
- Slim to CRUD stubs only

### DeviceTypeCacheTests

- Construct with `FakeDeviceChangeNotifier` + `FakeDeviceService`
- Event-trigger tests call `fakeNotifier.RaiseChanged()` instead of `fakeDeviceService.RaiseChanged()`
- Dispose test asserts unsubscribe from notifier
- Existing assertions (HasDevicesOfType, CacheUpdated firing) unchanged

### DeviceServiceTests

- Inject `FakeDeviceChangeNotifier` into `DeviceService`
- Mutation tests assert `fakeNotifier.NotifyChangedCallCount` incremented
- `UpdateLastSeen` test asserts count unchanged

### DeviceTypePresenceWatcherTests

- Inject `FakeDeviceChangeNotifier`, trigger via `fakeNotifier.RaiseChanged()`

## Files Summary

| Action | File |
|--------|------|
| New | `src/ControlMenu/Services/IDeviceChangeNotifier.cs` |
| New | `src/ControlMenu/Services/DeviceChangeNotifier.cs` |
| New | `tests/ControlMenu.Tests/Services/Fakes/FakeDeviceChangeNotifier.cs` |
| Modify | `src/ControlMenu/Services/IDeviceService.cs` |
| Modify | `src/ControlMenu/Services/DeviceService.cs` |
| Modify | `src/ControlMenu/Services/DeviceTypeCache.cs` |
| Modify | `src/ControlMenu/Modules/AndroidDevices/Services/DeviceTypePresenceWatcher.cs` |
| Modify | `src/ControlMenu/Program.cs` |
| Modify | `tests/ControlMenu.Tests/Services/Fakes/FakeDeviceService.cs` |
| Modify | `tests/ControlMenu.Tests/Services/DeviceTypeCacheTests.cs` |
| Modify | `tests/ControlMenu.Tests/Services/DeviceServiceTests.cs` |
| Modify | `tests/ControlMenu.Tests/Services/DeviceTypePresenceWatcherTests.cs` |

## Verification

Two-tab smoke test:
1. Open Tab A on `/settings/devices` and Tab B on `/android/phone` (or any device-type dashboard)
2. Delete a device in Tab A
3. Confirm Tab B's sidebar nav updates within ~1 second without manual refresh
4. Confirm `DeviceTypePresenceWatcher` redirect still fires in Tab B if navigating to an empty device type

## Not In Scope

- Promoting `DeviceService` to singleton (rejected — larger blast radius, future constraint on scoped dependencies)
- SignalR hub broadcast (rejected — overkill for in-process Blazor Server)
- Typed event args with add/remove/update discriminator (YAGNI — both subscribers do full refresh)
- Any changes to dashboards, pages, or database schema
