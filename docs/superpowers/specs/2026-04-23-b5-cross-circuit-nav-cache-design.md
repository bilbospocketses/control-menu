# B-5 Cross-circuit nav cache desync — design spec

**Date:** 2026-04-23
**Author:** brainstormed interactively across two sessions (context-overflow handoff)
**Predecessor:** `todo_control_menu.md` §B-5
**Scope:** fix the cross-circuit cache invalidation bug where sidebar device-type counts and presence-watcher redirects only update in the circuit that mutated devices, not in other open tabs/circuits.

---

## 1. Motivation

`DeviceTypeCache` and `DeviceTypePresenceWatcher` both subscribe to `IDeviceService.DevicesChanged`, a scoped event. Because `IDeviceService` is registered as scoped (one per Blazor circuit), the event only fires within the circuit that performed the mutation. Other open circuits never hear about the change:

- **Sidebar counts go stale** — tab A adds a TV, tab B's sidebar still shows 0 TVs until the user navigates.
- **Presence watcher doesn't redirect** — tab B is on the TV page, tab A deletes the last TV, tab B stays on a now-empty page.

Both problems share a single root cause: cross-circuit notification requires a singleton pub/sub channel, not a scoped event.

---

## 2. Decisions locked in during brainstorming

| # | Topic | Decision | Rationale |
|---|---|---|---|
| 1 | Notification pattern | **Option (a): new singleton `IDeviceChangeNotifier`.** Dedicated pub/sub service. DeviceService calls `NotifyChanged()` on every mutation; consumers subscribe. | Purely additive — no changes to DeviceService's DI lifetime. Option (b) (promote DeviceService to singleton + `IDbContextFactory`) has larger blast radius. Option (c) (SignalR) is overkill for in-process Blazor Server. |
| 2 | Subscriber migration | **Both subscribers migrate to notifier; scoped event removed.** DeviceTypeCache and DeviceTypePresenceWatcher both get the same cross-circuit fix. | Both have the identical bug. Partial fix (cache-only) leaves presence watcher broken. Scoped event has no remaining subscribers after migration. |
| 3 | Event shape | **Keep `Action` (no payload).** | YAGNI. Both subscribers invalidate + re-query; neither needs to know what changed. |
| 4 | Disposal ordering | **Standard `IDisposable` unsubscription.** Both subscribers `-=` in Dispose. Blazor DI calls Dispose on circuit teardown for scoped services. | Singleton notifier outlives circuits. Event unsubscription is idempotent. No special ordering needed. |
| 5 | DeviceService safety | **Confirmed safe.** DeviceService has no `ICircuitAccessor` or circuit-scoped state. The notifier call is one-directional (service → singleton). | Grep audit found zero circuit-scoped references in DeviceService.cs. |
| 6 | Test strategy | **`FakeDeviceChangeNotifier : IDeviceChangeNotifier`** with `TriggerChanged()` method. Mirrors existing `FakeNavigationManager` pattern. | Unit tests can trigger cache invalidation without wiring real DeviceService. |

---

## 3. Component responsibilities

### New: `IDeviceChangeNotifier` / `DeviceChangeNotifier`

**Files:** `src/ControlMenu/Services/IDeviceChangeNotifier.cs`, `src/ControlMenu/Services/DeviceChangeNotifier.cs`

**DI registration:** `builder.Services.AddSingleton<IDeviceChangeNotifier, DeviceChangeNotifier>()` in `Program.cs`.

**Interface:**

```csharp
public interface IDeviceChangeNotifier
{
    event Action? Changed;
    void NotifyChanged();
}
```

**Implementation:**

```csharp
public sealed class DeviceChangeNotifier : IDeviceChangeNotifier
{
    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();
}
```

---

### Modified: `DeviceService`

**File:** `src/ControlMenu/Services/DeviceService.cs`

**Changes:**
- Inject `IDeviceChangeNotifier` via constructor.
- Replace every `DevicesChanged?.Invoke()` call (3 sites: add, update, delete) with `_notifier.NotifyChanged()`.
- Remove the `public event Action? DevicesChanged` field and the explicit interface `IDeviceService.DevicesChanged` implementation.

---

### Modified: `IDeviceService`

**File:** `src/ControlMenu/Services/IDeviceService.cs`

**Changes:**
- Remove `event Action DevicesChanged;` from the interface.

---

### Modified: `DeviceTypeCache`

**File:** `src/ControlMenu/Services/DeviceTypeCache.cs`

**Changes:**
- Replace `IDeviceService` dependency with `IDeviceChangeNotifier` (for subscription only; cache still uses `IDeviceService` for queries).
- Constructor: `_notifier.Changed += OnDevicesChanged;`
- Dispose: `_notifier.Changed -= OnDevicesChanged;`
- Handler logic unchanged (invalidate + re-query).

---

### Modified: `DeviceTypePresenceWatcher`

**File:** `src/ControlMenu/Modules/AndroidDevices/Services/DeviceTypePresenceWatcher.cs`

**Changes:**
- Add `IDeviceChangeNotifier` parameter to constructor (alongside existing `IDeviceService` for queries).
- Subscribe/unsubscribe to `_notifier.Changed` instead of `_deviceService.DevicesChanged`.
- Handler logic unchanged (check presence, redirect if empty, invoke callback if still present).

---

### New: `FakeDeviceChangeNotifier`

**File:** `tests/ControlMenu.Tests/Fakes/FakeDeviceChangeNotifier.cs` (or wherever existing fakes live — grep for `FakeNavigationManager`).

```csharp
public sealed class FakeDeviceChangeNotifier : IDeviceChangeNotifier
{
    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();
    public void TriggerChanged() => Changed?.Invoke();
}
```

---

## 4. Files touched (complete list)

| File | Action |
|---|---|
| `src/ControlMenu/Services/IDeviceChangeNotifier.cs` | **Create** |
| `src/ControlMenu/Services/DeviceChangeNotifier.cs` | **Create** |
| `src/ControlMenu/Services/IDeviceService.cs` | Modify — remove `DevicesChanged` event |
| `src/ControlMenu/Services/DeviceService.cs` | Modify — inject notifier, replace event invocations, remove event field |
| `src/ControlMenu/Services/DeviceTypeCache.cs` | Modify — subscribe to notifier instead of scoped event |
| `src/ControlMenu/Modules/AndroidDevices/Services/DeviceTypePresenceWatcher.cs` | Modify — subscribe to notifier instead of scoped event |
| `src/ControlMenu/Program.cs` | Modify — add singleton registration |
| `tests/ControlMenu.Tests/Fakes/FakeDeviceChangeNotifier.cs` | **Create** |
| `tests/ControlMenu.Tests/*DeviceTypeCacheTests*` | Modify — use FakeDeviceChangeNotifier |
| `tests/ControlMenu.Tests/*DeviceChangeNotifierTests*` | **Create** — basic pub/sub + multi-subscriber test |

---

## 5. What this does NOT change

- **No UI changes.** Sidebar re-render is automatic once cache invalidation fires cross-circuit.
- **No DeviceService lifetime change.** Stays scoped. Only the notifier is singleton.
- **No SignalR / JS interop.** Pure in-process C# pub/sub.
- **No new NuGet dependencies.**

---

## 6. Verification criteria

1. `dotnet build` — zero warnings
2. `dotnet test` — all existing + new tests pass
3. **Manual test:** open two browser tabs, add a device in tab A → tab B sidebar updates within seconds
4. **Manual test:** open TV page in tab B with one TV, delete it in tab A → tab B redirects to `/android/devices`
