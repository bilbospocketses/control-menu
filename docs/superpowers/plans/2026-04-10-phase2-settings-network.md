# Phase 2: Settings & Network — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add network device discovery, device CRUD, and a settings UI with general configuration, device management, and module-specific settings — unlocking Phases 3-6.

**Architecture:** NetworkDiscoveryService parses ARP tables via CommandExecutor for MAC-to-IP resolution. DeviceService provides CRUD over the Devices table. A unified `/settings` page uses sections for General, Device Management, and Module Settings. All forms use custom CSS consistent with the existing theme system.

**Tech Stack:** .NET 9, Blazor Server, EF Core + SQLite, xUnit + Moq, Bootstrap Icons (already loaded)

---

## File Structure

### New Services

```
src/ControlMenu/Services/
├── INetworkDiscoveryService.cs
├── NetworkDiscoveryService.cs
├── ArpEntry.cs
├── IDeviceService.cs
└── DeviceService.cs
```

### New UI Pages

```
src/ControlMenu/Components/Pages/
├── Settings/
│   ├── SettingsPage.razor
│   ├── SettingsPage.razor.css
│   ├── GeneralSettings.razor
│   ├── DeviceManagement.razor
│   ├── DeviceManagement.razor.css
│   ├── DeviceForm.razor
│   └── ModuleSettings.razor
```

### New/Modified CSS

```
src/ControlMenu/wwwroot/css/
├── app.css              (add settings + form styles)
```

### New Tests

```
tests/ControlMenu.Tests/Services/
├── NetworkDiscoveryServiceTests.cs
├── DeviceServiceTests.cs
```

---

## Task 1: NetworkDiscoveryService

**Files:**
- Create: `src/ControlMenu/Services/ArpEntry.cs`
- Create: `src/ControlMenu/Services/INetworkDiscoveryService.cs`
- Create: `src/ControlMenu/Services/NetworkDiscoveryService.cs`
- Create: `tests/ControlMenu.Tests/Services/NetworkDiscoveryServiceTests.cs`

The NetworkDiscoveryService runs `arp -a` via CommandExecutor, parses the output into ArpEntry records, and resolves MAC addresses to IPs. It also pings devices. MAC addresses are normalized to lowercase with `-` separators for consistent comparison.

- [ ] **Step 1: Write the failing tests**

`tests/ControlMenu.Tests/Services/NetworkDiscoveryServiceTests.cs`:
```csharp
using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Services;

public class NetworkDiscoveryServiceTests
{
    private readonly Mock<ICommandExecutor> _mockExecutor = new();

    private NetworkDiscoveryService CreateService() => new(_mockExecutor.Object);

    [Fact]
    public async Task GetArpTableAsync_ParsesWindowsOutput()
    {
        var windowsOutput = """
            Interface: 192.168.1.100 --- 0x4
              Internet Address      Physical Address      Type
              192.168.1.1           a0-b1-c2-d3-e4-f5     dynamic
              192.168.1.50          b8-7b-d4-f3-ae-84     dynamic
              192.168.1.255         ff-ff-ff-ff-ff-ff     static
            """;

        _mockExecutor.Setup(e => e.ExecuteAsync("arp", "-a", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, windowsOutput, "", false));

        var service = CreateService();
        var entries = await service.GetArpTableAsync();

        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.IpAddress == "192.168.1.1" && e.MacAddress == "a0-b1-c2-d3-e4-f5");
        Assert.Contains(entries, e => e.IpAddress == "192.168.1.50" && e.MacAddress == "b8-7b-d4-f3-ae-84");
    }

    [Fact]
    public async Task GetArpTableAsync_ParsesLinuxArpOutput()
    {
        var linuxOutput = """
            ? (192.168.1.1) at a0:b1:c2:d3:e4:f5 [ether] on eth0
            ? (192.168.1.50) at b8:7b:d4:f3:ae:84 [ether] on eth0
            """;

        _mockExecutor.Setup(e => e.ExecuteAsync("arp", "-a", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, linuxOutput, "", false));

        var service = CreateService();
        var entries = await service.GetArpTableAsync();

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.IpAddress == "192.168.1.1" && e.MacAddress == "a0-b1-c2-d3-e4-f5");
        Assert.Contains(entries, e => e.IpAddress == "192.168.1.50" && e.MacAddress == "b8-7b-d4-f3-ae-84");
    }

    [Fact]
    public async Task GetArpTableAsync_EmptyOutput_ReturnsEmptyList()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("arp", "-a", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        var entries = await service.GetArpTableAsync();

        Assert.Empty(entries);
    }

    [Fact]
    public async Task ResolveIpFromMacAsync_FindsMatchingEntry()
    {
        var output = """
            Interface: 192.168.1.100 --- 0x4
              Internet Address      Physical Address      Type
              192.168.1.50          b8-7b-d4-f3-ae-84     dynamic
            """;

        _mockExecutor.Setup(e => e.ExecuteAsync("arp", "-a", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, output, "", false));

        var service = CreateService();
        var ip = await service.ResolveIpFromMacAsync("B8-7B-D4-F3-AE-84");

        Assert.Equal("192.168.1.50", ip);
    }

    [Fact]
    public async Task ResolveIpFromMacAsync_NormalizesColonFormat()
    {
        var output = """
            Interface: 192.168.1.100 --- 0x4
              Internet Address      Physical Address      Type
              192.168.1.50          b8-7b-d4-f3-ae-84     dynamic
            """;

        _mockExecutor.Setup(e => e.ExecuteAsync("arp", "-a", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, output, "", false));

        var service = CreateService();
        // Pass MAC with colons — should still match
        var ip = await service.ResolveIpFromMacAsync("b8:7b:d4:f3:ae:84");

        Assert.Equal("192.168.1.50", ip);
    }

    [Fact]
    public async Task ResolveIpFromMacAsync_NotFound_ReturnsNull()
    {
        var output = """
            Interface: 192.168.1.100 --- 0x4
              Internet Address      Physical Address      Type
              192.168.1.50          b8-7b-d4-f3-ae-84     dynamic
            """;

        _mockExecutor.Setup(e => e.ExecuteAsync("arp", "-a", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, output, "", false));

        var service = CreateService();
        var ip = await service.ResolveIpFromMacAsync("00-00-00-00-00-00");

        Assert.Null(ip);
    }

    [Fact]
    public async Task PingAsync_SuccessfulPing_ReturnsTrue()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync(
                It.Is<string>(s => s == "ping"),
                It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, "Reply from 192.168.1.50", "", false));

        var service = CreateService();
        var result = await service.PingAsync("192.168.1.50");

        Assert.True(result);
    }

    [Fact]
    public async Task PingAsync_FailedPing_ReturnsFalse()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync(
                It.Is<string>(s => s == "ping"),
                It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(1, "Request timed out", "", false));

        var service = CreateService();
        var result = await service.PingAsync("192.168.1.50");

        Assert.False(result);
    }

    [Fact]
    public void NormalizeMac_ConvertsFormats()
    {
        Assert.Equal("b8-7b-d4-f3-ae-84", NetworkDiscoveryService.NormalizeMac("B8-7B-D4-F3-AE-84"));
        Assert.Equal("b8-7b-d4-f3-ae-84", NetworkDiscoveryService.NormalizeMac("b8:7b:d4:f3:ae:84"));
        Assert.Equal("b8-7b-d4-f3-ae-84", NetworkDiscoveryService.NormalizeMac("B8:7B:D4:F3:AE:84"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd C:\Scripts\tools-menu
dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~NetworkDiscoveryServiceTests" -v minimal
```

Expected: FAIL — types do not exist.

- [ ] **Step 3: Create ArpEntry record**

`src/ControlMenu/Services/ArpEntry.cs`:
```csharp
namespace ControlMenu.Services;

public record ArpEntry(string IpAddress, string MacAddress, string Type);
```

- [ ] **Step 4: Create INetworkDiscoveryService interface**

`src/ControlMenu/Services/INetworkDiscoveryService.cs`:
```csharp
namespace ControlMenu.Services;

public interface INetworkDiscoveryService
{
    Task<IReadOnlyList<ArpEntry>> GetArpTableAsync(CancellationToken ct = default);
    Task<string?> ResolveIpFromMacAsync(string macAddress, CancellationToken ct = default);
    Task<bool> PingAsync(string ipAddress, CancellationToken ct = default);
}
```

- [ ] **Step 5: Create NetworkDiscoveryService implementation**

`src/ControlMenu/Services/NetworkDiscoveryService.cs`:
```csharp
using System.Text.RegularExpressions;

namespace ControlMenu.Services;

public partial class NetworkDiscoveryService : INetworkDiscoveryService
{
    private readonly ICommandExecutor _executor;

    public NetworkDiscoveryService(ICommandExecutor executor)
    {
        _executor = executor;
    }

    public async Task<IReadOnlyList<ArpEntry>> GetArpTableAsync(CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("arp", "-a", cancellationToken: ct);
        if (result.ExitCode != 0)
            return [];

        return ParseArpOutput(result.StandardOutput);
    }

    public async Task<string?> ResolveIpFromMacAsync(string macAddress, CancellationToken ct = default)
    {
        var normalized = NormalizeMac(macAddress);
        var entries = await GetArpTableAsync(ct);
        return entries.FirstOrDefault(e => e.MacAddress == normalized)?.IpAddress;
    }

    public async Task<bool> PingAsync(string ipAddress, CancellationToken ct = default)
    {
        var args = OperatingSystem.IsWindows()
            ? $"-n 1 -w 2000 {ipAddress}"
            : $"-c 1 -W 2 {ipAddress}";

        var result = await _executor.ExecuteAsync("ping", args, cancellationToken: ct);
        return result.ExitCode == 0;
    }

    public static string NormalizeMac(string mac)
    {
        return mac.ToLowerInvariant().Replace(':', '-');
    }

    private static List<ArpEntry> ParseArpOutput(string output)
    {
        var entries = new List<ArpEntry>();

        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries))
        {
            // Windows format: "  192.168.1.1           a0-b1-c2-d3-e4-f5     dynamic"
            var windowsMatch = WindowsArpRegex().Match(line);
            if (windowsMatch.Success)
            {
                entries.Add(new ArpEntry(
                    windowsMatch.Groups["ip"].Value,
                    NormalizeMac(windowsMatch.Groups["mac"].Value),
                    windowsMatch.Groups["type"].Value));
                continue;
            }

            // Linux format: "? (192.168.1.1) at a0:b1:c2:d3:e4:f5 [ether] on eth0"
            var linuxMatch = LinuxArpRegex().Match(line);
            if (linuxMatch.Success)
            {
                entries.Add(new ArpEntry(
                    linuxMatch.Groups["ip"].Value,
                    NormalizeMac(linuxMatch.Groups["mac"].Value),
                    "dynamic"));
                continue;
            }
        }

        return entries;
    }

    [GeneratedRegex(@"(?<ip>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\s+(?<mac>[0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2})\s+(?<type>\w+)")]
    private static partial Regex WindowsArpRegex();

    [GeneratedRegex(@"\((?<ip>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\)\s+at\s+(?<mac>[0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2})")]
    private static partial Regex LinuxArpRegex();
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
cd C:\Scripts\tools-menu
dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~NetworkDiscoveryServiceTests" -v minimal
```

Expected: 9 passed, 0 failed.

- [ ] **Step 7: Commit**

```bash
cd C:\Scripts\tools-menu
git add src/ControlMenu/Services/ArpEntry.cs src/ControlMenu/Services/INetworkDiscoveryService.cs src/ControlMenu/Services/NetworkDiscoveryService.cs tests/ControlMenu.Tests/Services/NetworkDiscoveryServiceTests.cs
git commit -m "feat: add NetworkDiscoveryService with ARP parsing and ping"
```

---

## Task 2: DeviceService

**Files:**
- Create: `src/ControlMenu/Services/IDeviceService.cs`
- Create: `src/ControlMenu/Services/DeviceService.cs`
- Create: `tests/ControlMenu.Tests/Services/DeviceServiceTests.cs`

CRUD operations for the Devices table, plus a method to update LastKnownIp/LastSeen when a device is discovered on the network.

- [ ] **Step 1: Write the failing tests**

`tests/ControlMenu.Tests/Services/DeviceServiceTests.cs`:
```csharp
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Services;
using ControlMenu.Tests.Data;

namespace ControlMenu.Tests.Services;

public class DeviceServiceTests : IDisposable
{
    private readonly ControlMenu.Data.AppDbContext _db;
    private readonly DeviceService _service;

    public DeviceServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _service = new DeviceService(_db);
    }

    public void Dispose() => _db.Dispose();

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
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd C:\Scripts\tools-menu
dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DeviceServiceTests" -v minimal
```

- [ ] **Step 3: Create IDeviceService interface**

`src/ControlMenu/Services/IDeviceService.cs`:
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

- [ ] **Step 4: Create DeviceService implementation**

`src/ControlMenu/Services/DeviceService.cs`:
```csharp
using ControlMenu.Data;
using ControlMenu.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Services;

public class DeviceService : IDeviceService
{
    private readonly AppDbContext _db;

    public DeviceService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Device>> GetAllDevicesAsync()
    {
        return await _db.Devices.OrderBy(d => d.Name).ToListAsync();
    }

    public async Task<Device?> GetDeviceAsync(Guid id)
    {
        return await _db.Devices.FindAsync(id);
    }

    public async Task<Device> AddDeviceAsync(Device device)
    {
        if (device.Id == Guid.Empty)
            device.Id = Guid.NewGuid();

        _db.Devices.Add(device);
        await _db.SaveChangesAsync();
        return device;
    }

    public async Task UpdateDeviceAsync(Device device)
    {
        _db.Devices.Update(device);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteDeviceAsync(Guid id)
    {
        var device = await _db.Devices.FindAsync(id);
        if (device is not null)
        {
            _db.Devices.Remove(device);
            await _db.SaveChangesAsync();
        }
    }

    public async Task UpdateLastSeenAsync(Guid id, string ipAddress)
    {
        var device = await _db.Devices.FindAsync(id);
        if (device is not null)
        {
            device.LastKnownIp = ipAddress;
            device.LastSeen = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
cd C:\Scripts\tools-menu
dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~DeviceServiceTests" -v minimal
```

Expected: 7 passed, 0 failed.

- [ ] **Step 6: Run all tests**

```bash
cd C:\Scripts\tools-menu
dotnet test ControlMenu.sln -v minimal
```

Expected: 44 passed (28 existing + 9 NetworkDiscovery + 7 DeviceService).

- [ ] **Step 7: Commit**

```bash
cd C:\Scripts\tools-menu
git add src/ControlMenu/Services/IDeviceService.cs src/ControlMenu/Services/DeviceService.cs tests/ControlMenu.Tests/Services/DeviceServiceTests.cs
git commit -m "feat: add DeviceService with CRUD and last-seen tracking"
```

---

## Task 3: Register New Services in DI + Add CSS

**Files:**
- Modify: `src/ControlMenu/Program.cs`
- Modify: `src/ControlMenu/Components/App.razor`
- Modify: `src/ControlMenu/wwwroot/css/app.css`

Register the new services and add form/table/settings styles to the CSS.

- [ ] **Step 1: Update Program.cs — add service registrations**

Add these lines after the existing service registrations in `src/ControlMenu/Program.cs`, right after the `builder.Services.AddScoped<IConfigurationService, ConfigurationService>();` line:

```csharp
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddSingleton<INetworkDiscoveryService, NetworkDiscoveryService>();
```

Also add the using at the top if not present:
```csharp
using ControlMenu.Services;
```

- [ ] **Step 2: Add settings/form/table styles to app.css**

Append to the end of `src/ControlMenu/wwwroot/css/app.css`:

```css
/* ── Settings Page ── */
.settings-layout {
    display: flex;
    gap: 24px;
    max-width: 1100px;
}

.settings-nav {
    display: flex;
    flex-direction: column;
    gap: 2px;
    min-width: 200px;
    padding-top: 4px;
}

.settings-nav-item {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 10px 14px;
    border-radius: 6px;
    cursor: pointer;
    color: var(--text-primary);
    font-size: 0.9rem;
    background: none;
    border: none;
    text-align: left;
    width: 100%;
}

.settings-nav-item:hover {
    background-color: var(--hover-bg);
}

.settings-nav-item.active {
    background-color: var(--active-bg);
    color: var(--accent-color);
    font-weight: 500;
}

.settings-content {
    flex: 1;
    min-width: 0;
}

.settings-section {
    margin-bottom: 32px;
}

.settings-section h2 {
    font-size: 1.2rem;
    font-weight: 600;
    margin: 0 0 16px 0;
}

.settings-section p {
    color: var(--text-secondary);
    margin: 0 0 16px 0;
    font-size: 0.9rem;
}

/* ── Forms ── */
.form-group {
    margin-bottom: 16px;
}

.form-group label {
    display: block;
    font-size: 0.85rem;
    font-weight: 500;
    color: var(--text-secondary);
    margin-bottom: 4px;
}

.form-control {
    width: 100%;
    padding: 8px 12px;
    border: 1px solid var(--input-border);
    border-radius: 6px;
    background-color: var(--input-bg);
    color: var(--text-primary);
    font-size: 0.9rem;
    transition: border-color 0.15s;
}

.form-control:focus {
    outline: none;
    border-color: var(--accent-color);
    box-shadow: 0 0 0 2px rgba(13, 110, 253, 0.15);
}

select.form-control {
    appearance: auto;
}

.form-hint {
    font-size: 0.8rem;
    color: var(--text-muted);
    margin-top: 4px;
}

.form-row {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 16px;
}

/* ── Buttons ── */
.btn {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 8px 16px;
    border: 1px solid transparent;
    border-radius: 6px;
    font-size: 0.9rem;
    font-weight: 500;
    cursor: pointer;
    transition: background-color 0.15s, border-color 0.15s;
}

.btn-primary {
    background-color: var(--accent-color);
    color: #fff;
}

.btn-primary:hover {
    opacity: 0.9;
}

.btn-secondary {
    background-color: transparent;
    color: var(--text-primary);
    border-color: var(--input-border);
}

.btn-secondary:hover {
    background-color: var(--hover-bg);
}

.btn-danger {
    background-color: var(--danger-color);
    color: #fff;
}

.btn-danger:hover {
    opacity: 0.9;
}

.btn-sm {
    padding: 4px 10px;
    font-size: 0.8rem;
}

/* ── Tables ── */
.data-table {
    width: 100%;
    border-collapse: collapse;
    font-size: 0.9rem;
}

.data-table th {
    text-align: left;
    padding: 10px 12px;
    border-bottom: 2px solid var(--border-color);
    color: var(--text-secondary);
    font-weight: 600;
    font-size: 0.8rem;
    text-transform: uppercase;
    letter-spacing: 0.04em;
}

.data-table td {
    padding: 10px 12px;
    border-bottom: 1px solid var(--border-color);
    vertical-align: middle;
}

.data-table tr:hover td {
    background-color: var(--hover-bg);
}

.data-table .actions {
    display: flex;
    gap: 6px;
    justify-content: flex-end;
}

/* ── Status indicators ── */
.status-dot {
    display: inline-block;
    width: 8px;
    height: 8px;
    border-radius: 50%;
    margin-right: 6px;
}

.status-dot.online {
    background-color: var(--success-color);
}

.status-dot.stale {
    background-color: var(--warning-color);
}

.status-dot.offline {
    background-color: var(--danger-color);
}

/* ── Dialog / Modal overlay ── */
.dialog-overlay {
    position: fixed;
    inset: 0;
    background-color: rgba(0, 0, 0, 0.5);
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 1000;
}

.dialog {
    background-color: var(--card-bg);
    border: 1px solid var(--border-color);
    border-radius: 10px;
    padding: 24px;
    width: 480px;
    max-width: 90vw;
    box-shadow: 0 8px 32px rgba(0, 0, 0, 0.2);
}

.dialog h3 {
    font-size: 1.1rem;
    font-weight: 600;
    margin: 0 0 16px 0;
}

.dialog-actions {
    display: flex;
    justify-content: flex-end;
    gap: 8px;
    margin-top: 20px;
}

/* ── Toolbar ── */
.toolbar {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-bottom: 16px;
}

.toolbar-spacer {
    flex: 1;
}

/* ── Alert/Toast ── */
.alert {
    padding: 10px 14px;
    border-radius: 6px;
    font-size: 0.9rem;
    margin-bottom: 16px;
}

.alert-success {
    background-color: rgba(25, 135, 84, 0.1);
    color: var(--success-color);
    border: 1px solid rgba(25, 135, 84, 0.2);
}

.alert-danger {
    background-color: rgba(220, 53, 69, 0.1);
    color: var(--danger-color);
    border: 1px solid rgba(220, 53, 69, 0.2);
}
```

- [ ] **Step 3: Verify build**

```bash
cd C:\Scripts\tools-menu
dotnet build ControlMenu.sln
```

- [ ] **Step 4: Commit**

```bash
cd C:\Scripts\tools-menu
git add src/ControlMenu/Program.cs src/ControlMenu/wwwroot/css/app.css
git commit -m "feat: register new services and add settings/form CSS"
```

---

## Task 4: Settings Page Layout + General Settings

**Files:**
- Create: `src/ControlMenu/Components/Pages/Settings/SettingsPage.razor`
- Create: `src/ControlMenu/Components/Pages/Settings/SettingsPage.razor.css`
- Create: `src/ControlMenu/Components/Pages/Settings/GeneralSettings.razor`

The settings page uses a left nav + content area pattern. General Settings manages theme persistence and discovery interval.

- [ ] **Step 1: Create GeneralSettings.razor**

`src/ControlMenu/Components/Pages/Settings/GeneralSettings.razor`:
```razor
@inject IConfigurationService Config
@inject IJSRuntime JS

<div class="settings-section">
    <h2>General</h2>
    <p>Application-wide configuration settings.</p>

    <div class="form-group">
        <label>Theme</label>
        <select class="form-control" style="max-width: 200px;" value="@_theme" @onchange="OnThemeChanged">
            <option value="system">System (auto)</option>
            <option value="dark">Dark</option>
            <option value="light">Light</option>
        </select>
    </div>

    <div class="form-group">
        <label>Device Discovery Interval (seconds)</label>
        <input type="number" class="form-control" style="max-width: 200px;" min="30" max="3600"
               value="@_discoveryInterval" @onchange="OnDiscoveryIntervalChanged" />
        <div class="form-hint">How often to scan the network for device IP changes. Min 30s, max 3600s.</div>
    </div>

    @if (_saved)
    {
        <div class="alert alert-success">Settings saved.</div>
    }
</div>

@code {
    private string _theme = "system";
    private int _discoveryInterval = 300;
    private bool _saved;

    protected override async Task OnInitializedAsync()
    {
        _theme = await Config.GetSettingAsync("theme") ?? "system";
        var interval = await Config.GetSettingAsync("discovery-interval");
        if (int.TryParse(interval, out var parsed))
            _discoveryInterval = parsed;
    }

    private async Task OnThemeChanged(ChangeEventArgs e)
    {
        _theme = e.Value?.ToString() ?? "system";
        await Config.SetSettingAsync("theme", _theme);
        await JS.InvokeVoidAsync("themeManager.set", _theme);
        await ShowSaved();
    }

    private async Task OnDiscoveryIntervalChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var val) && val >= 30 && val <= 3600)
        {
            _discoveryInterval = val;
            await Config.SetSettingAsync("discovery-interval", val.ToString());
            await ShowSaved();
        }
    }

    private async Task ShowSaved()
    {
        _saved = true;
        StateHasChanged();
        await Task.Delay(2000);
        _saved = false;
        StateHasChanged();
    }
}
```

- [ ] **Step 2: Create SettingsPage.razor**

`src/ControlMenu/Components/Pages/Settings/SettingsPage.razor`:
```razor
@page "/settings"
@page "/settings/{Section}"

<PageTitle>Settings — Control Menu</PageTitle>

<div class="settings-layout">
    <nav class="settings-nav">
        <button class="settings-nav-item @(ActiveSection == "general" ? "active" : "")"
                @onclick='() => Navigate("general")'>
            <i class="bi bi-gear"></i> General
        </button>
        <button class="settings-nav-item @(ActiveSection == "devices" ? "active" : "")"
                @onclick='() => Navigate("devices")'>
            <i class="bi bi-phone"></i> Devices
        </button>
        @if (ModuleDiscovery.Modules.Count > 0)
        {
            <button class="settings-nav-item @(ActiveSection == "modules" ? "active" : "")"
                    @onclick='() => Navigate("modules")'>
                <i class="bi bi-puzzle"></i> Module Settings
            </button>
        }
    </nav>

    <div class="settings-content">
        @switch (ActiveSection)
        {
            case "general":
                <GeneralSettings />
                break;
            case "devices":
                <DeviceManagement />
                break;
            case "modules":
                <ModuleSettings />
                break;
        }
    </div>
</div>

@code {
    [Parameter]
    public string? Section { get; set; }

    [Inject]
    private NavigationManager Nav { get; set; } = default!;

    [Inject]
    private ModuleDiscoveryService ModuleDiscovery { get; set; } = default!;

    private string ActiveSection => Section?.ToLowerInvariant() ?? "general";

    private void Navigate(string section)
    {
        Nav.NavigateTo($"/settings/{section}");
    }
}
```

- [ ] **Step 3: Create SettingsPage.razor.css**

`src/ControlMenu/Components/Pages/Settings/SettingsPage.razor.css`:
```css
/* Scoped styles already in app.css via .settings-layout — this file intentionally minimal */
```

- [ ] **Step 4: Verify build**

```bash
cd C:\Scripts\tools-menu
dotnet build ControlMenu.sln
```

Build will fail because `DeviceManagement` and `ModuleSettings` components don't exist yet. Create empty stubs to unblock the build:

- [ ] **Step 5: Create stub DeviceManagement.razor**

`src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor`:
```razor
<div class="settings-section">
    <h2>Device Management</h2>
    <p>Manage registered devices.</p>
    <p><em>Loading...</em></p>
</div>

@code {
}
```

- [ ] **Step 6: Create stub ModuleSettings.razor**

`src/ControlMenu/Components/Pages/Settings/ModuleSettings.razor`:
```razor
<div class="settings-section">
    <h2>Module Settings</h2>
    <p>Configure module-specific settings.</p>
    <p><em>No modules configured.</em></p>
</div>

@code {
}
```

- [ ] **Step 7: Add usings for Services namespace**

Add this line to `src/ControlMenu/Components/_Imports.razor`:
```
@using ControlMenu.Services
```

- [ ] **Step 8: Verify build passes**

```bash
cd C:\Scripts\tools-menu
dotnet build ControlMenu.sln
```

Expected: Build succeeded.

- [ ] **Step 9: Commit**

```bash
cd C:\Scripts\tools-menu
git add src/ControlMenu/Components/
git commit -m "feat: add settings page with general settings section"
```

---

## Task 5: Device Management — Full CRUD + Network Scan

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor`
- Create: `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor.css`
- Create: `src/ControlMenu/Components/Pages/Settings/DeviceForm.razor`

The device management section displays a table of all devices, allows Add/Edit/Delete, and has a "Scan Network" button that refreshes IP addresses via ARP.

- [ ] **Step 1: Create DeviceForm.razor**

`src/ControlMenu/Components/Pages/Settings/DeviceForm.razor`:
```razor
@using ControlMenu.Data.Entities
@using ControlMenu.Data.Enums

<div class="dialog-overlay" @onclick="OnCancel">
    <div class="dialog" @onclick:stopPropagation="true">
        <h3>@(IsEdit ? "Edit Device" : "Add Device")</h3>

        <div class="form-group">
            <label>Device Name</label>
            <input class="form-control" @bind="Model.Name" placeholder="Living Room TV" />
        </div>

        <div class="form-row">
            <div class="form-group">
                <label>Device Type</label>
                <select class="form-control" @bind="Model.Type">
                    @foreach (var type in Enum.GetValues<DeviceType>())
                    {
                        <option value="@type">@type</option>
                    }
                </select>
            </div>

            <div class="form-group">
                <label>ADB Port</label>
                <input type="number" class="form-control" @bind="Model.AdbPort" />
            </div>
        </div>

        <div class="form-group">
            <label>MAC Address</label>
            <input class="form-control" @bind="Model.MacAddress" placeholder="b8-7b-d4-f3-ae-84" />
            <div class="form-hint">Used for automatic IP discovery via ARP table.</div>
        </div>

        <div class="form-group">
            <label>Serial Number (optional)</label>
            <input class="form-control" @bind="Model.SerialNumber" placeholder="47121FDAQ000WC" />
        </div>

        <div class="dialog-actions">
            <button class="btn btn-secondary" @onclick="OnCancel">Cancel</button>
            <button class="btn btn-primary" @onclick="OnSave" disabled="@(!IsValid)">
                @(IsEdit ? "Save Changes" : "Add Device")
            </button>
        </div>
    </div>
</div>

@code {
    [Parameter, EditorRequired]
    public Device Model { get; set; } = default!;

    [Parameter]
    public bool IsEdit { get; set; }

    [Parameter]
    public EventCallback OnSave { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    private bool IsValid =>
        !string.IsNullOrWhiteSpace(Model.Name) &&
        !string.IsNullOrWhiteSpace(Model.MacAddress);
}
```

- [ ] **Step 2: Replace DeviceManagement.razor**

Replace `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor` with:

```razor
@inject IDeviceService DeviceService
@inject INetworkDiscoveryService NetworkDiscovery

<div class="settings-section">
    <h2>Device Management</h2>
    <p>Register and manage Android devices. Use "Scan Network" to auto-discover IP addresses via ARP.</p>

    <div class="toolbar">
        <button class="btn btn-primary" @onclick="ShowAddForm">
            <i class="bi bi-plus-lg"></i> Add Device
        </button>
        <button class="btn btn-secondary" @onclick="ScanNetwork" disabled="@_scanning">
            <i class="bi bi-broadcast"></i> @(_scanning ? "Scanning..." : "Scan Network")
        </button>
        <div class="toolbar-spacer"></div>
        @if (!string.IsNullOrEmpty(_message))
        {
            <span class="alert @(_messageIsError ? "alert-danger" : "alert-success")" style="margin:0; padding:6px 12px;">@_message</span>
        }
    </div>

    @if (_devices.Count == 0)
    {
        <p style="color: var(--text-muted);">No devices registered. Click "Add Device" to get started.</p>
    }
    else
    {
        <table class="data-table">
            <thead>
                <tr>
                    <th>Status</th>
                    <th>Name</th>
                    <th>Type</th>
                    <th>MAC Address</th>
                    <th>IP Address</th>
                    <th>ADB Port</th>
                    <th>Last Seen</th>
                    <th style="text-align:right;">Actions</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var device in _devices)
                {
                    <tr>
                        <td><span class="status-dot @GetStatusClass(device)"></span></td>
                        <td>@device.Name</td>
                        <td>@device.Type</td>
                        <td><code>@device.MacAddress</code></td>
                        <td>@(device.LastKnownIp ?? "—")</td>
                        <td>@device.AdbPort</td>
                        <td>@FormatLastSeen(device.LastSeen)</td>
                        <td class="actions">
                            <button class="btn btn-secondary btn-sm" @onclick="() => ShowEditForm(device)">Edit</button>
                            <button class="btn btn-danger btn-sm" @onclick="() => ConfirmDelete(device)">Delete</button>
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    }
</div>

@if (_showForm)
{
    <DeviceForm Model="_formDevice" IsEdit="_isEditing" OnSave="SaveDevice" OnCancel="CloseForm" />
}

@if (_showDeleteConfirm)
{
    <div class="dialog-overlay" @onclick="CloseDeleteConfirm">
        <div class="dialog" @onclick:stopPropagation="true">
            <h3>Delete Device</h3>
            <p>Are you sure you want to delete <strong>@_deleteTarget?.Name</strong>?</p>
            <div class="dialog-actions">
                <button class="btn btn-secondary" @onclick="CloseDeleteConfirm">Cancel</button>
                <button class="btn btn-danger" @onclick="DeleteDevice">Delete</button>
            </div>
        </div>
    </div>
}

@code {
    private List<ControlMenu.Data.Entities.Device> _devices = [];
    private bool _showForm;
    private bool _isEditing;
    private ControlMenu.Data.Entities.Device _formDevice = new()
    {
        Name = "", MacAddress = "", ModuleId = "android-devices"
    };
    private bool _showDeleteConfirm;
    private ControlMenu.Data.Entities.Device? _deleteTarget;
    private bool _scanning;
    private string? _message;
    private bool _messageIsError;

    protected override async Task OnInitializedAsync()
    {
        await LoadDevices();
    }

    private async Task LoadDevices()
    {
        _devices = (await DeviceService.GetAllDevicesAsync()).ToList();
    }

    private void ShowAddForm()
    {
        _formDevice = new ControlMenu.Data.Entities.Device
        {
            Name = "", MacAddress = "", ModuleId = "android-devices"
        };
        _isEditing = false;
        _showForm = true;
    }

    private void ShowEditForm(ControlMenu.Data.Entities.Device device)
    {
        _formDevice = device;
        _isEditing = true;
        _showForm = true;
    }

    private void CloseForm()
    {
        _showForm = false;
    }

    private async Task SaveDevice()
    {
        // Normalize MAC before saving
        _formDevice.MacAddress = NetworkDiscoveryService.NormalizeMac(_formDevice.MacAddress);

        if (_isEditing)
            await DeviceService.UpdateDeviceAsync(_formDevice);
        else
            await DeviceService.AddDeviceAsync(_formDevice);

        _showForm = false;
        await LoadDevices();
        await ShowMessage($"Device {(_isEditing ? "updated" : "added")} successfully.");
    }

    private void ConfirmDelete(ControlMenu.Data.Entities.Device device)
    {
        _deleteTarget = device;
        _showDeleteConfirm = true;
    }

    private void CloseDeleteConfirm()
    {
        _showDeleteConfirm = false;
        _deleteTarget = null;
    }

    private async Task DeleteDevice()
    {
        if (_deleteTarget is not null)
        {
            await DeviceService.DeleteDeviceAsync(_deleteTarget.Id);
            _showDeleteConfirm = false;
            _deleteTarget = null;
            await LoadDevices();
            await ShowMessage("Device deleted.");
        }
    }

    private async Task ScanNetwork()
    {
        _scanning = true;
        StateHasChanged();

        int updated = 0;
        foreach (var device in _devices)
        {
            var ip = await NetworkDiscovery.ResolveIpFromMacAsync(device.MacAddress);
            if (ip is not null)
            {
                await DeviceService.UpdateLastSeenAsync(device.Id, ip);
                updated++;
            }
        }

        await LoadDevices();
        _scanning = false;
        await ShowMessage($"Scan complete. {updated} of {_devices.Count} device(s) found on network.");
    }

    private static string GetStatusClass(ControlMenu.Data.Entities.Device device)
    {
        if (device.LastSeen is null) return "offline";
        var age = DateTime.UtcNow - device.LastSeen.Value;
        if (age.TotalMinutes < 10) return "online";
        if (age.TotalHours < 24) return "stale";
        return "offline";
    }

    private static string FormatLastSeen(DateTime? lastSeen)
    {
        if (lastSeen is null) return "Never";
        var age = DateTime.UtcNow - lastSeen.Value;
        if (age.TotalMinutes < 1) return "Just now";
        if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalHours < 24) return $"{(int)age.TotalHours}h ago";
        return lastSeen.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    private async Task ShowMessage(string message, bool isError = false)
    {
        _message = message;
        _messageIsError = isError;
        StateHasChanged();
        await Task.Delay(3000);
        _message = null;
        StateHasChanged();
    }
}
```

- [ ] **Step 3: Create DeviceManagement.razor.css**

`src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor.css`:
```css
code {
    font-size: 0.85rem;
    color: var(--text-secondary);
}
```

- [ ] **Step 4: Verify build**

```bash
cd C:\Scripts\tools-menu
dotnet build ControlMenu.sln
```

Expected: Build succeeded.

- [ ] **Step 5: Run all tests**

```bash
cd C:\Scripts\tools-menu
dotnet test ControlMenu.sln -v minimal
```

Expected: All 44 tests pass.

- [ ] **Step 6: Commit**

```bash
cd C:\Scripts\tools-menu
git add src/ControlMenu/Components/Pages/Settings/
git commit -m "feat: add device management with CRUD and network scan"
```

---

## Task 6: Module Settings

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Settings/ModuleSettings.razor`

Generates settings forms dynamically from each module's `ConfigRequirements`. Secret fields are masked and stored encrypted.

- [ ] **Step 1: Replace ModuleSettings.razor**

Replace `src/ControlMenu/Components/Pages/Settings/ModuleSettings.razor` with:

```razor
@inject ModuleDiscoveryService ModuleDiscovery
@inject IConfigurationService Config

<div class="settings-section">
    <h2>Module Settings</h2>
    <p>Configure settings for each installed module.</p>

    @if (ModuleDiscovery.Modules.Count == 0)
    {
        <p style="color: var(--text-muted);">No modules installed. Module settings will appear here when modules are added in later phases.</p>
    }
    else
    {
        @foreach (var module in ModuleDiscovery.Modules)
        {
            var requirements = module.ConfigRequirements.ToList();
            if (requirements.Count == 0)
                continue;

            <div class="module-config-card">
                <h3><i class="bi @module.Icon"></i> @module.DisplayName</h3>

                @foreach (var req in requirements)
                {
                    var key = $"{module.Id}:{req.Key}";
                    <div class="form-group">
                        <label>@req.DisplayName</label>
                        @if (req.IsSecret)
                        {
                            <input type="password" class="form-control" style="max-width: 400px;"
                                   value="@GetValue(key)"
                                   placeholder="@(req.DefaultValue ?? "Enter secret...")"
                                   @onchange="e => SetSecret(module.Id, req.Key, e)" />
                        }
                        else
                        {
                            <input class="form-control" style="max-width: 400px;"
                                   value="@GetValue(key)"
                                   placeholder="@(req.DefaultValue ?? "")"
                                   @onchange="e => SetSetting(module.Id, req.Key, e)" />
                        }
                        <div class="form-hint">@req.Description</div>
                    </div>
                }
            </div>
        }

        @if (_saved)
        {
            <div class="alert alert-success">Setting saved.</div>
        }
    }
</div>

@code {
    private readonly Dictionary<string, string> _values = new();
    private bool _saved;

    protected override async Task OnInitializedAsync()
    {
        foreach (var module in ModuleDiscovery.Modules)
        {
            foreach (var req in module.ConfigRequirements)
            {
                var key = $"{module.Id}:{req.Key}";
                string? value;
                if (req.IsSecret)
                    value = await Config.GetSecretAsync(req.Key, module.Id);
                else
                    value = await Config.GetSettingAsync(req.Key, module.Id);

                _values[key] = value ?? "";
            }
        }
    }

    private string GetValue(string key) => _values.GetValueOrDefault(key, "");

    private async Task SetSetting(string moduleId, string settingKey, ChangeEventArgs e)
    {
        var val = e.Value?.ToString() ?? "";
        await Config.SetSettingAsync(settingKey, val, moduleId);
        _values[$"{moduleId}:{settingKey}"] = val;
        await ShowSaved();
    }

    private async Task SetSecret(string moduleId, string settingKey, ChangeEventArgs e)
    {
        var val = e.Value?.ToString() ?? "";
        if (!string.IsNullOrEmpty(val))
        {
            await Config.SetSecretAsync(settingKey, val, moduleId);
            _values[$"{moduleId}:{settingKey}"] = val;
            await ShowSaved();
        }
    }

    private async Task ShowSaved()
    {
        _saved = true;
        StateHasChanged();
        await Task.Delay(2000);
        _saved = false;
        StateHasChanged();
    }
}
```

- [ ] **Step 2: Add module-config-card style to app.css**

Append to the end of `src/ControlMenu/wwwroot/css/app.css`:

```css
/* ── Module Settings Cards ── */
.module-config-card {
    background-color: var(--card-bg);
    border: 1px solid var(--border-color);
    border-radius: 8px;
    padding: 20px;
    margin-bottom: 16px;
}

.module-config-card h3 {
    font-size: 1rem;
    font-weight: 600;
    margin: 0 0 16px 0;
    display: flex;
    align-items: center;
    gap: 8px;
}
```

- [ ] **Step 3: Verify build and tests**

```bash
cd C:\Scripts\tools-menu
dotnet build ControlMenu.sln && dotnet test ControlMenu.sln -v minimal
```

Expected: Build succeeded, 44 tests pass.

- [ ] **Step 4: Commit**

```bash
cd C:\Scripts\tools-menu
git add src/ControlMenu/Components/Pages/Settings/ModuleSettings.razor src/ControlMenu/wwwroot/css/app.css
git commit -m "feat: add dynamic module settings with encrypted secret support"
```

---

## Summary

After completing all 6 tasks, Phase 2 delivers:

- **NetworkDiscoveryService** — ARP table parsing (Windows + Linux formats), MAC-to-IP resolution, ping
- **DeviceService** — CRUD operations + last-seen tracking for devices
- **Settings page** — `/settings` route with left nav and three sections
- **General Settings** — Theme persistence to DB, device discovery interval
- **Device Management** — Device table with status indicators, Add/Edit/Delete dialogs, "Scan Network" button
- **Module Settings** — Dynamic form from `ConfigRequirements`, automatic secret encryption
- **16+ new tests** covering NetworkDiscoveryService and DeviceService
- **Form/table/dialog CSS** consistent with the existing theme system

**Ready for Phase 3:** Android Devices Module — AdbService, Google TV dashboard, Pixel dashboard, all device action cards.
