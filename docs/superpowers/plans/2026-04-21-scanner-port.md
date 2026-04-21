# Network Scanner Port from ws-scrcpy-web — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a full-featured network scan modal to Control Menu's Settings › Devices page by delegating scan/probe logic to ws-scrcpy-web's `/ws-scan` WebSocket and porting the UX (subnet parser, progress chip, live hit stream, cheat sheet) to Blazor Server. Introduce a Managed vs External deployment-mode toggle so ws-scrcpy-web can run natively or in Docker.

**Architecture:** New C# singleton `NetworkScanService` holds a server-side `ClientWebSocket` to ws-scrcpy-web, fans `scan.progress` / `scan.hit` / `scan.complete` events to every Blazor circuit spectating the scan. Subnet list persists in `Settings` table (`scan-subnets`). Deploy mode (`wsscrcpy-mode` + `wsscrcpy-url`) determines whether Control Menu spawns the Node process or just pings a configured URL. Quick Refresh button keeps today's inline `adb mdns services` + ARP path untouched; new `📡 Scan Network…` button opens the modal.

**Tech Stack:** .NET 9, Blazor Server (SignalR circuits), xUnit + Moq for tests, `System.Net.WebSockets.ClientWebSocket`, existing `ConfigurationService` / `DeviceService` / `WsScrcpyService`.

**Reference spec:** `docs/superpowers/specs/2026-04-21-scanner-port-design.md`

---

## File Structure

Files created or modified by this plan. One responsibility per file.

### Created

```
src/ControlMenu/Services/Network/
  ScanPhase.cs                 # enum Idle/Scanning/Draining/Complete/Cancelled
  ParsedSubnet.cs              # record (raw, normalized, hostCount)
  ScanHit.cs                   # record (source, address, serial, name, label)
  ScanEvent.cs                 # discriminated union via abstract record hierarchy
  SubnetParser.cs              # static — CIDR / IP / range parser, matches ws-scrcpy-web
  INetworkScanService.cs       # public contract
  NetworkScanService.cs        # singleton orchestrator, holds ClientWebSocket
  SubnetDetectionClient.cs     # HTTP call to GET /api/devices/scan/subnet

src/ControlMenu/Components/Shared/Scanner/
  ScanNetworkModal.razor (+ .css)       # main modal
  AddSubnetModal.razor (+ .css)         # sub-modal (CIDR/IP/range input)
  LargeSubnetWarningModal.razor         # interstitial for >2048 hosts
  ScanProgressChip.razor (+ .css)       # four-state chip

src/ControlMenu/wwwroot/help/
  subnets.html                 # cheat sheet, copied verbatim from ws-scrcpy-web

tests/ControlMenu.Tests/Services/
  SubnetParserTests.cs
  NetworkScanServiceTests.cs
  WsScrcpyServiceTests.cs      # new — deploy mode split
  FakeWsScanServer.cs          # test helper (in-process WS server)
```

### Modified

```
src/ControlMenu/Services/WsScrcpyService.cs
src/ControlMenu/Services/DependencyManagerService.cs
src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor
src/ControlMenu/Components/Pages/Settings/GeneralSettings.razor
src/ControlMenu/Program.cs
src/ControlMenu/CHANGELOG.md    # Unreleased entry
tests/ControlMenu.Tests/Services/ConfigurationServiceTests.cs  # + scan-subnets round-trip
```

---

## Task 1: Data contracts (types only)

**Files:**
- Create: `src/ControlMenu/Services/Network/ScanPhase.cs`
- Create: `src/ControlMenu/Services/Network/ParsedSubnet.cs`
- Create: `src/ControlMenu/Services/Network/ScanHit.cs`
- Create: `src/ControlMenu/Services/Network/ScanEvent.cs`

No tests — plain records/enums. Every later task references these.

- [ ] **Step 1: Create `ScanPhase.cs`**

```csharp
namespace ControlMenu.Services.Network;

public enum ScanPhase
{
    Idle,
    Scanning,
    Draining,
    Complete,
    Cancelled,
}
```

- [ ] **Step 2: Create `ParsedSubnet.cs`**

```csharp
namespace ControlMenu.Services.Network;

/// <summary>
/// Normalized form of a user-entered subnet. <see cref="Raw"/> is what the user typed;
/// <see cref="Normalized"/> is the canonical form (e.g. <c>192.168.1.0/24</c> or
/// <c>192.168.1.10-192.168.1.50</c>). <see cref="HostCount"/> is the effective number
/// of scannable hosts (network/broadcast excluded for CIDR).
/// </summary>
public sealed record ParsedSubnet(string Raw, string Normalized, int HostCount);
```

- [ ] **Step 3: Create `ScanHit.cs`**

```csharp
namespace ControlMenu.Services.Network;

public enum DiscoverySource { Mdns, Tcp }

/// <summary>
/// A device observed during a scan. <see cref="Address"/> is <c>"IP:port"</c>
/// exactly as emitted by ws-scan's scan.hit message. <see cref="Mac"/> is null
/// until ARP resolves the IP post-TCP-touch.
/// </summary>
public sealed record ScanHit(
    DiscoverySource Source,
    string Address,
    string Serial,
    string Name,
    string Label,
    string? Mac);
```

- [ ] **Step 4: Create `ScanEvent.cs`**

```csharp
namespace ControlMenu.Services.Network;

public abstract record ScanEvent;

public sealed record ScanStartedEvent(int TotalHosts, int TotalSubnets, long StartedAt) : ScanEvent;
public sealed record ScanProgressEvent(int Checked, int Total, int FoundSoFar) : ScanEvent;
public sealed record ScanHitEvent(ScanHit Hit) : ScanEvent;
public sealed record ScanDrainingEvent : ScanEvent;
public sealed record ScanCompleteEvent(int Found) : ScanEvent;
public sealed record ScanCancelledEvent(int Found) : ScanEvent;
public sealed record ScanErrorEvent(string Reason) : ScanEvent;
```

- [ ] **Step 5: Build and commit**

```bash
dotnet build C:/Users/jscha/source/repos/tools-menu/src/ControlMenu/ControlMenu.csproj -c Debug
git add src/ControlMenu/Services/Network/
git commit -m "feat(scanner): data contracts for network scan service"
```

Expected: Build passes. No tests yet.

---

## Task 2: SubnetParser (TDD)

**Files:**
- Create: `tests/ControlMenu.Tests/Services/SubnetParserTests.cs`
- Create: `src/ControlMenu/Services/Network/SubnetParser.cs`

Port `src/common/SubnetParser.ts` from ws-scrcpy-web. Preserve error verbiage for test parity.

- [ ] **Step 1: Write failing tests first**

```csharp
using ControlMenu.Services.Network;

namespace ControlMenu.Tests.Services;

public class SubnetParserTests
{
    [Theory]
    [InlineData("192.168.1.0/24", "192.168.1.0/24", 254)]
    [InlineData("192.168.1.5/24", "192.168.1.0/24", 254)]  // non-network IP → normalized
    [InlineData("10.0.0.0/16", "10.0.0.0/16", 65534)]
    [InlineData("192.168.1.5/32", "192.168.1.5/32", 1)]
    [InlineData("192.168.1.0/31", "192.168.1.0/31", 2)]
    public void ParseCidr_Valid(string input, string normalized, int hostCount)
    {
        var result = SubnetParser.Parse(input);
        Assert.True(result.IsSuccess);
        Assert.Equal(normalized, result.Value!.Normalized);
        Assert.Equal(hostCount, result.Value.HostCount);
    }

    [Theory]
    [InlineData("192.168.1.5")]
    public void ParseBareIp_NormalizesToSlash32(string input)
    {
        var result = SubnetParser.Parse(input);
        Assert.True(result.IsSuccess);
        Assert.Equal($"{input}/32", result.Value!.Normalized);
        Assert.Equal(1, result.Value.HostCount);
    }

    [Theory]
    [InlineData("192.168.1.10-192.168.1.50", "192.168.1.10-192.168.1.50", 41)]
    [InlineData("192.168.1.10-20", "192.168.1.10-192.168.1.20", 11)]              // shorthand
    [InlineData("192.168.1.0-192.168.1.255", "192.168.1.0-192.168.1.255", 254)]   // skip .0 + .255
    public void ParseRange_Valid(string input, string normalized, int hostCount)
    {
        var result = SubnetParser.Parse(input);
        Assert.True(result.IsSuccess);
        Assert.Equal(normalized, result.Value!.Normalized);
        Assert.Equal(hostCount, result.Value.HostCount);
    }

    [Theory]
    [InlineData("", "Unrecognized format")]
    [InlineData("not-an-ip", "Unrecognized format")]
    [InlineData("192.168.1.0/8", "Prefix must be between /16 and /32")]
    [InlineData("192.168.1.0/33", "Prefix must be between /16 and /32")]
    [InlineData("999.168.1.0/24", "Invalid IP address")]
    [InlineData("192.168.1.50-192.168.1.10", "Range start must be ≤ end")]
    public void Parse_Invalid_ReturnsErrorWithExpectedSubstring(string input, string expectedSubstring)
    {
        var result = SubnetParser.Parse(input);
        Assert.False(result.IsSuccess);
        Assert.Contains(expectedSubstring, result.Error);
    }

    [Fact]
    public void ParseRange_TooLarge_Rejected()
    {
        // >65536 addresses — 0.0.0.0 to 1.0.0.0 is 16M+
        var result = SubnetParser.Parse("0.0.0.0-1.0.0.0");
        Assert.False(result.IsSuccess);
        Assert.Contains("Range too large", result.Error);
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

```
dotnet test tests/ControlMenu.Tests/ --filter FullyQualifiedName~SubnetParserTests
```

Expected: all fail — `SubnetParser` does not exist.

- [ ] **Step 3: Implement `SubnetParser.cs`**

Port of `src/common/SubnetParser.ts`. Key invariants:
- CIDR prefix 16-32 allowed, anything else is a hard reject (not "warning").
- `/32` and `/31` are special — `/32` = 1 host, `/31` = 2 hosts (both scannable), `/24` and below use network+1..broadcast-1.
- Range accepts `a-b` (full-IP shorthand) or `a-N` (last-octet shorthand).
- Range ≤ 65536 addresses. Range aligned to subnet boundary skips .0 and .255.
- Error strings include the cheat-sheet note: `"See the subnet cheat sheet at /help/subnets.html for help."`

```csharp
using System.Text.RegularExpressions;

namespace ControlMenu.Services.Network;

public readonly record struct ParseResult<T>(bool IsSuccess, T? Value, string Error)
{
    public static ParseResult<T> Ok(T value) => new(true, value, "");
    public static ParseResult<T> Fail(string reason) => new(false, default, reason);
}

public static class SubnetParser
{
    private const string CHEAT = "See the subnet cheat sheet at /help/subnets.html for help.";

    public static ParseResult<ParsedSubnet> Parse(string input)
    {
        var raw = (input ?? "").Trim();
        if (raw.Length == 0) return Unrecognized();

        if (raw.Contains('/')) return ParseCidr(raw);
        if (raw.Contains('-')) return ParseRange(raw);
        if (IsValidIp(raw))
        {
            return ParseResult<ParsedSubnet>.Ok(new ParsedSubnet(raw, $"{raw}/32", 1));
        }
        return Unrecognized();
    }

    private static ParseResult<ParsedSubnet> ParseCidr(string input)
    {
        var parts = input.Split('/');
        if (parts.Length != 2) return Unrecognized();
        var (ipPart, prefixPart) = (parts[0], parts[1]);
        if (string.IsNullOrEmpty(ipPart) || string.IsNullOrEmpty(prefixPart)) return Unrecognized();
        if (!IsValidIp(ipPart)) return Fail($"Invalid IP address \"{ipPart}\". {CHEAT}");

        if (!int.TryParse(prefixPart, out var prefix) || prefix < 0 || prefix > 32)
            return Fail($"Prefix must be between /16 and /32. {CHEAT}");
        if (prefix < 16)
            return Fail("Subnet too large — maximum prefix is /16 (65,534 hosts). " +
                        "If you need to cover more than that, add multiple /16 entries " +
                        $"(one per subnet) using the 'add subnet' button. {CHEAT}");

        uint ipInt = IpToInt(ipPart);
        int maskBits = 32 - prefix;
        uint netmask = maskBits == 32 ? 0u : (0xFFFFFFFFu << maskBits);
        uint networkInt = ipInt & netmask;
        string normalizedIp = IntToIp(networkInt);
        string normalized = $"{normalizedIp}/{prefix}";

        int hostCount = prefix switch
        {
            32 => 1,
            31 => 2,
            _ => (int)(Math.Pow(2, maskBits) - 2),
        };
        return ParseResult<ParsedSubnet>.Ok(new ParsedSubnet(input, normalized, hostCount));
    }

    private static ParseResult<ParsedSubnet> ParseRange(string input)
    {
        int dashIdx = input.IndexOf('-');
        var startStr = input[..dashIdx].Trim();
        var endStr = input[(dashIdx + 1)..].Trim();

        if (!IsValidIp(startStr)) return Fail($"Invalid start IP \"{startStr}\". {CHEAT}");

        string endIp;
        if (IsValidIp(endStr)) endIp = endStr;
        else if (Regex.IsMatch(endStr, @"^\d{1,3}$"))
        {
            var sp = startStr.Split('.');
            endIp = $"{sp[0]}.{sp[1]}.{sp[2]}.{endStr}";
            if (!IsValidIp(endIp)) return Fail($"Invalid end octet \"{endStr}\". {CHEAT}");
        }
        else return Fail($"Invalid end of range \"{endStr}\". {CHEAT}");

        uint startInt = IpToInt(startStr);
        uint endInt = IpToInt(endIp);
        if (startInt > endInt)
            return Fail($"Range start must be ≤ end (got {startStr} > {endIp}). {CHEAT}");

        long literalCount = (long)endInt - startInt + 1;
        if (literalCount > 65536)
            return Fail($"Range too large — maximum is 65,536 addresses (the size of a /16 CIDR block). " +
                        $"Got {literalCount:N0}. For larger scans, split into multiple entries " +
                        $"or use CIDR notation like 10.0.0.0/16. {CHEAT}");

        bool skipFirst = (startInt & 0xFF) == 0;
        bool skipLast = (endInt & 0xFF) == 0xFF;
        uint scanStart = skipFirst ? startInt + 1 : startInt;
        uint scanEnd = skipLast ? endInt - 1 : endInt;
        int hostCount = scanEnd >= scanStart ? (int)(scanEnd - scanStart + 1) : 0;

        return ParseResult<ParsedSubnet>.Ok(
            new ParsedSubnet(input, $"{IntToIp(startInt)}-{IntToIp(endInt)}", hostCount));
    }

    private static ParseResult<ParsedSubnet> Unrecognized() =>
        Fail($"Unrecognized format. Try CIDR (192.168.1.0/24), a single IP (192.168.1.5), " +
             $"or a range (192.168.1.10-50). {CHEAT}");

    private static ParseResult<ParsedSubnet> Fail(string reason) =>
        ParseResult<ParsedSubnet>.Fail(reason);

    private static bool IsValidIp(string s)
    {
        var parts = s.Split('.');
        if (parts.Length != 4) return false;
        foreach (var p in parts)
        {
            if (!Regex.IsMatch(p, @"^\d{1,3}$")) return false;
            if (!int.TryParse(p, out var n) || n < 0 || n > 255) return false;
        }
        return true;
    }

    private static uint IpToInt(string ip)
    {
        var parts = ip.Split('.').Select(int.Parse).ToArray();
        return (uint)((parts[0] << 24) | (parts[1] << 16) | (parts[2] << 8) | parts[3]);
    }

    private static string IntToIp(uint n) =>
        $"{(n >> 24) & 0xFF}.{(n >> 16) & 0xFF}.{(n >> 8) & 0xFF}.{n & 0xFF}";
}
```

- [ ] **Step 4: Run tests — verify they pass**

```
dotnet test tests/ControlMenu.Tests/ --filter FullyQualifiedName~SubnetParserTests
```

Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Services/Network/SubnetParser.cs tests/ControlMenu.Tests/Services/SubnetParserTests.cs
git commit -m "feat(scanner): SubnetParser for CIDR/IP/range input"
```

---

## Task 3: Settings keys — subnets + deploy mode

**Files:**
- Modify: `tests/ControlMenu.Tests/Services/ConfigurationServiceTests.cs`

Add round-trip tests for the three new keys so future refactors don't silently break them. Keys themselves are just string<->string in Settings; no new code needed in `ConfigurationService`.

- [ ] **Step 1: Add tests**

Append to the existing test class:

```csharp
[Fact]
public async Task ScanSubnets_RoundTripsJsonArray()
{
    using var scope = CreateServiceScope();
    var svc = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
    var subnets = new[] { "192.168.1.0/24", "10.0.0.0/16" };
    await svc.SetSettingAsync("scan-subnets", System.Text.Json.JsonSerializer.Serialize(subnets));
    var raw = await svc.GetSettingAsync("scan-subnets");
    var back = System.Text.Json.JsonSerializer.Deserialize<string[]>(raw!);
    Assert.Equal(subnets, back);
}

[Theory]
[InlineData("managed")]
[InlineData("external")]
public async Task WsscrcpyMode_RoundTrips(string mode)
{
    using var scope = CreateServiceScope();
    var svc = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
    await svc.SetSettingAsync("wsscrcpy-mode", mode);
    Assert.Equal(mode, await svc.GetSettingAsync("wsscrcpy-mode"));
}

[Fact]
public async Task WsscrcpyUrl_RoundTrips()
{
    using var scope = CreateServiceScope();
    var svc = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
    await svc.SetSettingAsync("wsscrcpy-url", "http://ws-scrcpy:8000");
    Assert.Equal("http://ws-scrcpy:8000", await svc.GetSettingAsync("wsscrcpy-url"));
}
```

- [ ] **Step 2: Run tests — verify pass**

```
dotnet test tests/ControlMenu.Tests/ --filter FullyQualifiedName~ConfigurationServiceTests
```

Expected: all pass (these are just existing-code round-trip checks).

- [ ] **Step 3: Commit**

```bash
git add tests/ControlMenu.Tests/Services/ConfigurationServiceTests.cs
git commit -m "test(config): round-trip tests for scan-subnets + wsscrcpy-mode keys"
```

---

## Task 4: WsScrcpyService — deploy mode split

**Files:**
- Modify: `src/ControlMenu/Services/WsScrcpyService.cs`
- Create: `tests/ControlMenu.Tests/Services/WsScrcpyServiceTests.cs`

Add a `DeployMode` property (`Managed` | `External`) read from `wsscrcpy-mode`. In Managed mode: today's child-process behaviour. In External mode: no process spawn, health check becomes a URL ping to `{wsscrcpy-url}/`.

- [ ] **Step 1: Read current `WsScrcpyService.cs` to understand the surface**

```
cat src/ControlMenu/Services/WsScrcpyService.cs
```

Identify: `IsRunning`, `BaseUrl`, the auto-launch hook (hosted service or `StartAsync`).

- [ ] **Step 2: Write failing tests**

```csharp
using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Services;

public class WsScrcpyServiceTests
{
    private readonly Mock<IConfigurationService> _mockConfig = new();
    private readonly Mock<ICommandExecutor> _mockExecutor = new();

    [Fact]
    public async Task DeployMode_External_DoesNotSpawnProcess()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-mode", default)).ReturnsAsync("external");
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-url", default)).ReturnsAsync("http://ws-scrcpy:8000");
        var svc = new WsScrcpyService(_mockConfig.Object, _mockExecutor.Object /* + any existing deps */);
        await svc.StartAsync(CancellationToken.None);
        _mockExecutor.Verify(e => e.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("http://ws-scrcpy:8000", svc.BaseUrl);
    }

    [Fact]
    public async Task DeployMode_Managed_SpawnsNode()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-mode", default)).ReturnsAsync("managed");
        // existing managed-mode expectations: spawn node on default port 8000
        var svc = new WsScrcpyService(_mockConfig.Object, _mockExecutor.Object);
        await svc.StartAsync(CancellationToken.None);
        // Managed mode uses the configured install path; verify the spawn occurred.
        // (Specific assertion depends on the existing implementation — adapt.)
        _mockExecutor.Verify(e => e.ExecuteAsync(It.IsRegex("node"), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        Assert.Equal("http://localhost:8000", svc.BaseUrl);
    }

    [Fact]
    public async Task DeployMode_DefaultsToManaged_WhenSettingAbsent()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-mode", default)).ReturnsAsync((string?)null);
        var svc = new WsScrcpyService(_mockConfig.Object, _mockExecutor.Object);
        Assert.Equal(WsScrcpyDeployMode.Managed, await svc.GetDeployModeAsync());
    }
}
```

- [ ] **Step 3: Run tests — they should fail**

```
dotnet test tests/ControlMenu.Tests/ --filter FullyQualifiedName~WsScrcpyServiceTests
```

Expected: fails — `WsScrcpyDeployMode`, `GetDeployModeAsync`, external-branch logic don't exist yet.

- [ ] **Step 4: Implement changes in `WsScrcpyService.cs`**

Add at the top of the file:

```csharp
public enum WsScrcpyDeployMode { Managed, External }
```

Add to the service:

```csharp
public async Task<WsScrcpyDeployMode> GetDeployModeAsync(CancellationToken ct = default)
{
    var raw = (await _config.GetSettingAsync("wsscrcpy-mode", ct)) ?? "managed";
    return string.Equals(raw, "external", StringComparison.OrdinalIgnoreCase)
        ? WsScrcpyDeployMode.External
        : WsScrcpyDeployMode.Managed;
}

// BaseUrl getter resolves at call time based on mode.
public string BaseUrl
{
    get
    {
        // Preserve sync access pattern by caching resolved BaseUrl in StartAsync.
        return _cachedBaseUrl ?? "http://localhost:8000";
    }
}
private string? _cachedBaseUrl;
```

In `StartAsync`, short-circuit External mode:

```csharp
public async Task StartAsync(CancellationToken ct)
{
    var mode = await GetDeployModeAsync(ct);
    if (mode == WsScrcpyDeployMode.External)
    {
        _cachedBaseUrl = (await _config.GetSettingAsync("wsscrcpy-url", ct)) ?? "http://localhost:8000";
        // No process spawn, no watchdog. Health is a URL ping (handled by DependencyManagerService).
        return;
    }
    _cachedBaseUrl = "http://localhost:8000";
    // ... existing spawn logic unchanged ...
}
```

Keep `IsRunning` behaviour: in Managed mode today's process-alive check; in External mode, `IsRunning` returns `true` (Control Menu can't observe container lifecycle) — actual health surfaces via the dependency health check added in Task 5.

- [ ] **Step 5: Run tests — verify pass**

```
dotnet test tests/ControlMenu.Tests/ --filter FullyQualifiedName~WsScrcpyServiceTests
```

Expected: pass.

- [ ] **Step 6: Run full suite — verify no regression**

```
dotnet test tests/ControlMenu.Tests/
```

Expected: all existing tests still pass.

- [ ] **Step 7: Commit**

```bash
git add src/ControlMenu/Services/WsScrcpyService.cs tests/ControlMenu.Tests/Services/WsScrcpyServiceTests.cs
git commit -m "feat(wsscrcpy): Managed vs External deploy mode"
```

---

## Task 5: Dependency health check — URL ping in External mode

**Files:**
- Modify: `src/ControlMenu/Services/DependencyManagerService.cs`
- Modify: `tests/ControlMenu.Tests/Services/DependencyManagerServiceTests.cs`

When mode is External, the ws-scrcpy-web dependency entry (1) hides install/update controls at render time (Task 8 UI) and (2) replaces the process-alive health check with an HTTP GET to `{wsscrcpy-url}/`. 2xx = healthy.

- [ ] **Step 1: Add test for URL-ping health check**

```csharp
[Fact]
public async Task WsScrcpyHealth_External_ReturnsHealthyOn200()
{
    _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-mode", default)).ReturnsAsync("external");
    _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-url", default)).ReturnsAsync("http://fake:8000");
    var fakeHandler = new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK);
    var service = new DependencyManagerService(_mockConfig.Object, _mockExecutor.Object,
        new HttpClient(fakeHandler) /* + existing deps */);
    var result = await service.CheckHealthAsync("ws-scrcpy-web");
    Assert.True(result.IsHealthy);
}
```

`FakeHttpMessageHandler` is a small inline helper returning a canned status code — add it to the test file if not already present.

- [ ] **Step 2: Run — verify fails**

```
dotnet test tests/ControlMenu.Tests/ --filter FullyQualifiedName~DependencyManagerServiceTests.WsScrcpyHealth
```

Expected: fail.

- [ ] **Step 3: Implement URL-ping branch in `DependencyManagerService`**

In `CheckHealthAsync`, before the existing process/path check:

```csharp
if (name == "ws-scrcpy-web")
{
    var mode = (await _config.GetSettingAsync("wsscrcpy-mode")) ?? "managed";
    if (string.Equals(mode, "external", StringComparison.OrdinalIgnoreCase))
    {
        var url = (await _config.GetSettingAsync("wsscrcpy-url")) ?? "http://localhost:8000";
        try
        {
            var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            return new DependencyCheckResult(resp.IsSuccessStatusCode, /* version: */ null, /* path: */ url);
        }
        catch
        {
            return new DependencyCheckResult(false, null, url);
        }
    }
}
// ... existing Managed path unchanged ...
```

Inject `HttpClient` via DI if not already present.

- [ ] **Step 4: Run — verify pass**

```
dotnet test tests/ControlMenu.Tests/ --filter FullyQualifiedName~DependencyManagerServiceTests
```

Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Services/DependencyManagerService.cs tests/ControlMenu.Tests/Services/DependencyManagerServiceTests.cs
git commit -m "feat(deps): URL-ping health check for ws-scrcpy-web in External mode"
```

---

## Task 6: FakeWsScanServer test helper

**Files:**
- Create: `tests/ControlMenu.Tests/Services/FakeWsScanServer.cs`

In-process `HttpListener`-based WebSocket server for driving `NetworkScanService` in tests. Accepts one client at a time, emits scripted JSON messages matching ws-scrcpy-web's `ScanMessage.ts`.

- [ ] **Step 1: Create the helper (no test for the helper itself — it's a tool)**

```csharp
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ControlMenu.Tests.Services;

public sealed class FakeWsScanServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<WebSocket> _socketTcs = new();
    public string Url { get; }

    public FakeWsScanServer()
    {
        var port = GetFreePort();
        Url = $"http://localhost:{port}";
        _listener = new HttpListener();
        _listener.Prefixes.Add($"{Url}/");
        _listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    public Task<WebSocket> GetClientAsync(TimeSpan timeout) =>
        _socketTcs.Task.WaitAsync(timeout);

    public async Task SendAsync(WebSocket ws, object message)
    {
        var json = JsonSerializer.Serialize(message,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    public async Task<T?> ReceiveAsync<T>(WebSocket ws)
    {
        var buffer = new byte[4096];
        var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        return JsonSerializer.Deserialize<T>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private async Task AcceptLoop()
    {
        try
        {
            var ctx = await _listener.GetContextAsync();
            if (ctx.Request.IsWebSocketRequest && ctx.Request.Url?.AbsolutePath == "/ws-scan")
            {
                var wsCtx = await ctx.AcceptWebSocketAsync(subProtocol: null);
                _socketTcs.TrySetResult(wsCtx.WebSocket);
            }
            else
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
            }
        }
        catch { /* listener shut down */ }
    }

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        _listener.Close();
        await Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Verify compiles and listener starts (micro-smoke)**

Add a one-off test:

```csharp
[Fact]
public async Task FakeWsScanServer_AcceptsWebSocketConnection()
{
    await using var server = new FakeWsScanServer();
    using var client = new ClientWebSocket();
    var wsUrl = new Uri(server.Url.Replace("http://", "ws://") + "/ws-scan");
    await client.ConnectAsync(wsUrl, CancellationToken.None);
    var serverSocket = await server.GetClientAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(WebSocketState.Open, serverSocket.State);
}
```

- [ ] **Step 3: Run smoke test**

```
dotnet test tests/ControlMenu.Tests/ --filter FakeWsScanServer_AcceptsWebSocketConnection
```

Expected: pass.

- [ ] **Step 4: Commit**

```bash
git add tests/ControlMenu.Tests/Services/FakeWsScanServer.cs
git commit -m "test(scanner): FakeWsScanServer helper for scan-service integration tests"
```

---

## Task 7: NetworkScanService — subscribe/snapshot (TDD)

**Files:**
- Create: `src/ControlMenu/Services/Network/INetworkScanService.cs`
- Create: `src/ControlMenu/Services/Network/NetworkScanService.cs`
- Create: `tests/ControlMenu.Tests/Services/NetworkScanServiceTests.cs`

Start with subscribe semantics and snapshot replay. StartScan and cancel come in Tasks 8–9.

- [ ] **Step 1: Write failing tests**

```csharp
using ControlMenu.Services.Network;

namespace ControlMenu.Tests.Services;

public class NetworkScanServiceTests
{
    [Fact]
    public void Initial_Phase_IsIdle()
    {
        var svc = CreateService();
        Assert.Equal(ScanPhase.Idle, svc.Phase);
    }

    [Fact]
    public async Task Subscribe_WhenIdle_ReceivesNoSnapshotEvents()
    {
        var svc = CreateService();
        var received = new List<ScanEvent>();
        using var sub = svc.Subscribe(e => received.Add(e));
        // No events fired — no scan in progress.
        Assert.Empty(received);
    }

    [Fact]
    public async Task Subscribe_MidScan_ReceivesSnapshotReplay()
    {
        var svc = CreateService();
        // Simulate a running scan by directly feeding hits through the internal bus.
        // (Implementation uses a TestHook to emit events without a real WS — documented below.)
        svc.TestOnlyInject(new ScanStartedEvent(256, 1, 0));
        svc.TestOnlyInject(new ScanProgressEvent(10, 256, 0));
        svc.TestOnlyInject(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "192.168.86.43:5555", "ABC123", "adb-ABC123", "", null)));

        var received = new List<ScanEvent>();
        using var sub = svc.Subscribe(e => received.Add(e));
        // Snapshot replay emits: ScanStarted, current ScanProgress, all hits so far.
        Assert.Equal(3, received.Count);
        Assert.IsType<ScanStartedEvent>(received[0]);
        Assert.IsType<ScanProgressEvent>(received[1]);
        Assert.IsType<ScanHitEvent>(received[2]);
    }

    [Fact]
    public void Unsubscribe_StopsReceivingEvents()
    {
        var svc = CreateService();
        var received = new List<ScanEvent>();
        var sub = svc.Subscribe(e => received.Add(e));
        sub.Dispose();
        svc.TestOnlyInject(new ScanProgressEvent(1, 1, 0));
        Assert.Empty(received);
    }

    private static NetworkScanService CreateService()
    {
        var config = new Moq.Mock<IConfigurationService>();
        var wsscrcpy = new Moq.Mock<WsScrcpyService>(MockBehavior.Loose, /* match ctor */);
        return new NetworkScanService(config.Object, wsscrcpy.Object);
    }
}
```

- [ ] **Step 2: Run tests — fail**

```
dotnet test tests/ControlMenu.Tests/ --filter FullyQualifiedName~NetworkScanServiceTests
```

- [ ] **Step 3: Implement interface and skeleton**

```csharp
// INetworkScanService.cs
namespace ControlMenu.Services.Network;

public interface INetworkScanService
{
    ScanPhase Phase { get; }
    IReadOnlyList<ScanHit> Hits { get; }
    IDisposable Subscribe(Action<ScanEvent> onEvent);
    Task StartScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default);
    Task CancelAsync(CancellationToken ct = default);
}
```

```csharp
// NetworkScanService.cs — subscribe/snapshot portion only
using ControlMenu.Services;

namespace ControlMenu.Services.Network;

public sealed class NetworkScanService : INetworkScanService
{
    private readonly object _lock = new();
    private readonly List<Subscriber> _subscribers = new();
    private readonly List<ScanHit> _hits = new();
    private ScanStartedEvent? _lastStarted;
    private ScanProgressEvent? _lastProgress;

    private readonly IConfigurationService _config;
    private readonly WsScrcpyService _wsscrcpy;

    public NetworkScanService(IConfigurationService config, WsScrcpyService wsscrcpy)
    {
        _config = config;
        _wsscrcpy = wsscrcpy;
    }

    public ScanPhase Phase { get; private set; } = ScanPhase.Idle;
    public IReadOnlyList<ScanHit> Hits { get { lock (_lock) return _hits.ToList(); } }

    public IDisposable Subscribe(Action<ScanEvent> onEvent)
    {
        var sub = new Subscriber(onEvent, this);
        lock (_lock) _subscribers.Add(sub);

        // Snapshot replay.
        if (Phase is not ScanPhase.Idle)
        {
            if (_lastStarted is not null) onEvent(_lastStarted);
            if (_lastProgress is not null) onEvent(_lastProgress);
            foreach (var hit in _hits) onEvent(new ScanHitEvent(hit));
        }
        return sub;
    }

    // --- test hook (compiled in DEBUG + Tests only; gate with an internal InternalsVisibleTo) ---
    internal void TestOnlyInject(ScanEvent evt) => Dispatch(evt);

    private void Dispatch(ScanEvent evt)
    {
        Subscriber[] snapshot;
        lock (_lock)
        {
            switch (evt)
            {
                case ScanStartedEvent started:
                    Phase = ScanPhase.Scanning;
                    _lastStarted = started;
                    _lastProgress = null;
                    _hits.Clear();
                    break;
                case ScanProgressEvent p: _lastProgress = p; break;
                case ScanHitEvent h: _hits.Add(h.Hit); break;
                case ScanDrainingEvent: Phase = ScanPhase.Draining; break;
                case ScanCompleteEvent: Phase = ScanPhase.Complete; break;
                case ScanCancelledEvent: Phase = ScanPhase.Cancelled; break;
                case ScanErrorEvent: Phase = ScanPhase.Idle; break;
            }
            snapshot = _subscribers.ToArray();
        }
        foreach (var s in snapshot) s.Invoke(evt);
    }

    public Task StartScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default)
        => throw new NotImplementedException();  // Task 8

    public Task CancelAsync(CancellationToken ct = default)
        => throw new NotImplementedException();  // Task 9

    private sealed class Subscriber : IDisposable
    {
        private Action<ScanEvent>? _handler;
        private readonly NetworkScanService _parent;
        public Subscriber(Action<ScanEvent> h, NetworkScanService p) { _handler = h; _parent = p; }
        public void Invoke(ScanEvent e) => _handler?.Invoke(e);
        public void Dispose()
        {
            lock (_parent._lock) _parent._subscribers.Remove(this);
            _handler = null;
        }
    }
}
```

Add to `ControlMenu.csproj`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="ControlMenu.Tests" />
</ItemGroup>
```

- [ ] **Step 4: Run tests — verify pass**

```
dotnet test tests/ControlMenu.Tests/ --filter FullyQualifiedName~NetworkScanServiceTests
```

Expected: 4 pass.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Services/Network/ src/ControlMenu/ControlMenu.csproj tests/ControlMenu.Tests/Services/NetworkScanServiceTests.cs
git commit -m "feat(scanner): NetworkScanService subscribe + snapshot replay"
```

---

## Task 8: NetworkScanService — StartScan happy path (TDD against FakeWsScanServer)

**Files:**
- Modify: `src/ControlMenu/Services/Network/NetworkScanService.cs`
- Modify: `tests/ControlMenu.Tests/Services/NetworkScanServiceTests.cs`

StartScan opens `ClientWebSocket` to `{BaseUrl}/ws-scan`, sends `{type:"scan.start", subnets:[...]}`, reads server messages in a background loop, dispatches events.

- [ ] **Step 1: Write failing integration test**

```csharp
[Fact]
public async Task StartScan_HappyPath_StreamsEvents()
{
    await using var fakeServer = new FakeWsScanServer();
    var config = new Moq.Mock<IConfigurationService>();
    var wsscrcpy = new StubWsScrcpyService(fakeServer.Url);   // returns fakeServer.Url as BaseUrl
    var svc = new NetworkScanService(config.Object, wsscrcpy);

    var received = new List<ScanEvent>();
    using var sub = svc.Subscribe(e => received.Add(e));

    var subnets = new[] { new ParsedSubnet("10.0.0.0/29", "10.0.0.0/29", 6) };
    var startTask = svc.StartScanAsync(subnets);

    var serverSocket = await fakeServer.GetClientAsync(TimeSpan.FromSeconds(5));

    // Verify client sent scan.start
    var clientMsg = await fakeServer.ReceiveAsync<Dictionary<string, object>>(serverSocket);
    Assert.Equal("scan.start", clientMsg!["type"].ToString());

    // Script server-side replies.
    await fakeServer.SendAsync(serverSocket, new { type = "scan.started", totalHosts = 6, totalSubnets = 1, startedAt = 0 });
    await fakeServer.SendAsync(serverSocket, new { type = "scan.progress", checked_ = 3, total = 6, foundSoFar = 0 });
    await fakeServer.SendAsync(serverSocket, new { type = "scan.hit", source = "tcp", address = "10.0.0.5:5555", serial = "xyz", name = "adb-xyz", label = "" });
    await fakeServer.SendAsync(serverSocket, new { type = "scan.complete", found = 1 });

    await startTask;
    // Allow receive loop to drain.
    await Task.Delay(200);

    Assert.Contains(received, e => e is ScanStartedEvent);
    Assert.Contains(received, e => e is ScanHitEvent h && h.Hit.Address == "10.0.0.5:5555");
    Assert.Equal(ScanPhase.Complete, svc.Phase);
}
```

`StubWsScrcpyService` is a minimal subclass/stub that returns the fake URL from `BaseUrl`. Create alongside the test.

NOTE: `scan.progress`'s `checked` field conflicts with a C# keyword; deserialize as-is via JSON property — adjust to `[JsonPropertyName("checked")]` on the C# record used to read messages.

- [ ] **Step 2: Run — fail**

- [ ] **Step 3: Implement `StartScanAsync`**

```csharp
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

// ... in NetworkScanService:

private ClientWebSocket? _ws;
private CancellationTokenSource? _scanCts;

public async Task StartScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default)
{
    lock (_lock)
    {
        if (Phase is ScanPhase.Scanning or ScanPhase.Draining)
            throw new InvalidOperationException("scan already in progress");
        _hits.Clear();
        _lastStarted = null;
        _lastProgress = null;
    }

    var baseUrl = _wsscrcpy.BaseUrl;
    var wsUrl = new Uri(baseUrl.Replace("http://", "ws://").Replace("https://", "wss://") + "/ws-scan");

    _ws = new ClientWebSocket();
    _scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

    try
    {
        await _ws.ConnectAsync(wsUrl, _scanCts.Token);
    }
    catch (Exception ex)
    {
        Dispatch(new ScanErrorEvent($"ws-scrcpy-web unreachable at {baseUrl} — {ex.Message}"));
        _ws.Dispose();
        _ws = null;
        return;
    }

    var startMsg = JsonSerializer.Serialize(new
    {
        type = "scan.start",
        subnets = subnets.Select(s => s.Raw).ToArray(),
    });
    await _ws.SendAsync(Encoding.UTF8.GetBytes(startMsg), WebSocketMessageType.Text, true, _scanCts.Token);

    _ = Task.Run(() => ReceiveLoopAsync(_ws, _scanCts.Token));
}

private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
{
    var buffer = new byte[8192];
    try
    {
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) break;
            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var evt = ParseServerMessage(json);
            if (evt is not null) Dispatch(evt);
        }
    }
    catch when (ct.IsCancellationRequested) { /* expected on cancel */ }
    catch (Exception ex)
    {
        Dispatch(new ScanCancelledEvent(_hits.Count));
        Dispatch(new ScanErrorEvent($"upstream disconnect: {ex.Message}"));
    }
}

private static ScanEvent? ParseServerMessage(string json)
{
    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;
    if (!root.TryGetProperty("type", out var typeProp)) return null;
    var type = typeProp.GetString();
    return type switch
    {
        "scan.started" => new ScanStartedEvent(
            root.GetProperty("totalHosts").GetInt32(),
            root.GetProperty("totalSubnets").GetInt32(),
            root.GetProperty("startedAt").GetInt64()),
        "scan.progress" => new ScanProgressEvent(
            root.GetProperty("checked").GetInt32(),
            root.GetProperty("total").GetInt32(),
            root.GetProperty("foundSoFar").GetInt32()),
        "scan.hit" => new ScanHitEvent(new ScanHit(
            Enum.Parse<DiscoverySource>(root.GetProperty("source").GetString() ?? "Tcp", true),
            root.GetProperty("address").GetString() ?? "",
            root.GetProperty("serial").GetString() ?? "",
            root.GetProperty("name").GetString() ?? "",
            root.GetProperty("label").GetString() ?? "",
            null)),
        "scan.draining" => new ScanDrainingEvent(),
        "scan.complete" => new ScanCompleteEvent(root.GetProperty("found").GetInt32()),
        "scan.cancelled" => new ScanCancelledEvent(root.GetProperty("found").GetInt32()),
        "scan.error" => new ScanErrorEvent(root.GetProperty("reason").GetString() ?? "unknown"),
        _ => null,
    };
}
```

- [ ] **Step 4: Run tests — verify pass**

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Services/Network/NetworkScanService.cs tests/ControlMenu.Tests/Services/NetworkScanServiceTests.cs
git commit -m "feat(scanner): StartScanAsync streams scan events from ws-scan"
```

---

## Task 9: NetworkScanService — Cancel + WS drop handling

**Files:**
- Modify: same as Task 8

- [ ] **Step 1: Add cancel + drop tests**

```csharp
[Fact]
public async Task Cancel_DuringScanning_TransitionsThroughDraining()
{
    await using var fakeServer = new FakeWsScanServer();
    var svc = new NetworkScanService(Mock.Of<IConfigurationService>(), new StubWsScrcpyService(fakeServer.Url));
    var phases = new List<ScanPhase>();
    using var sub = svc.Subscribe(e => phases.Add(svc.Phase));

    var subnets = new[] { new ParsedSubnet("10.0.0.0/29", "10.0.0.0/29", 6) };
    _ = svc.StartScanAsync(subnets);
    var serverSocket = await fakeServer.GetClientAsync(TimeSpan.FromSeconds(5));
    await fakeServer.ReceiveAsync<object>(serverSocket);
    await fakeServer.SendAsync(serverSocket, new { type = "scan.started", totalHosts = 6, totalSubnets = 1, startedAt = 0 });
    await Task.Delay(50);

    await svc.CancelAsync();

    // Simulate ws-scan's cancel-response sequence.
    await fakeServer.SendAsync(serverSocket, new { type = "scan.draining" });
    await fakeServer.SendAsync(serverSocket, new { type = "scan.cancelled", found = 0 });
    await Task.Delay(100);

    Assert.Contains(ScanPhase.Scanning, phases);
    Assert.Contains(ScanPhase.Draining, phases);
    Assert.Contains(ScanPhase.Cancelled, phases);
}

[Fact]
public async Task WsDropsMidScan_ForcesCancelled()
{
    await using var fakeServer = new FakeWsScanServer();
    var svc = new NetworkScanService(Mock.Of<IConfigurationService>(), new StubWsScrcpyService(fakeServer.Url));
    using var sub = svc.Subscribe(_ => { });

    _ = svc.StartScanAsync(new[] { new ParsedSubnet("10.0.0.0/29", "10.0.0.0/29", 6) });
    var serverSocket = await fakeServer.GetClientAsync(TimeSpan.FromSeconds(5));
    await fakeServer.ReceiveAsync<object>(serverSocket);
    await fakeServer.SendAsync(serverSocket, new { type = "scan.started", totalHosts = 6, totalSubnets = 1, startedAt = 0 });
    await Task.Delay(50);

    // Server aborts without protocol close.
    await serverSocket.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "boom", CancellationToken.None);
    await Task.Delay(200);

    Assert.Equal(ScanPhase.Cancelled, svc.Phase);
}
```

- [ ] **Step 2: Run — fail (CancelAsync throws NotImplemented)**

- [ ] **Step 3: Implement Cancel**

```csharp
public async Task CancelAsync(CancellationToken ct = default)
{
    if (_ws is null || _ws.State != WebSocketState.Open) return;
    var msg = Encoding.UTF8.GetBytes("{\"type\":\"scan.cancel\"}");
    try { await _ws.SendAsync(msg, WebSocketMessageType.Text, true, ct); }
    catch { /* ignore — server will close anyway */ }
}
```

No separate code needed for the drop case — `ReceiveLoopAsync`'s catch block already dispatches `ScanCancelledEvent`.

- [ ] **Step 4: Run — pass**

- [ ] **Step 5: Commit**

```bash
git commit -am "feat(scanner): cancel + upstream-drop force Cancelled"
```

---

## Task 10: NetworkScanService — dedupe rule

**Files:**
- Modify: `src/ControlMenu/Services/Network/NetworkScanService.cs`
- Modify: `tests/ControlMenu.Tests/Services/NetworkScanServiceTests.cs`

MAC primary, IP fallback, serial placeholder merge. Applied by the **consumer** (DeviceManagement page) when merging hits with the `Device` table — NOT in the service itself. The service just buffers ordered hits. Consumer gets a helper.

- [ ] **Step 1: Add `HitDedupe` static helper + tests**

```csharp
// src/ControlMenu/Services/Network/HitDedupe.cs
namespace ControlMenu.Services.Network;

public static class HitDedupe
{
    /// <summary>
    /// Collapses a sequence of raw scan hits into unique devices.
    /// Dedupe key preference: MAC > IP (when MAC null) > serial placeholder.
    /// Last hit wins for each key (later hits usually have richer data, e.g. MAC
    /// arrives after TCP probe).
    /// </summary>
    public static IReadOnlyList<ScanHit> Collapse(IEnumerable<ScanHit> hits)
    {
        var byKey = new Dictionary<string, ScanHit>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in hits)
        {
            var key = h.Mac ?? (string.IsNullOrEmpty(h.Serial) ? h.Address : $"serial:{h.Serial}");
            byKey[key] = h;
        }
        return byKey.Values.ToList();
    }
}
```

Tests:

```csharp
[Fact]
public void Dedupe_MacPrimary()
{
    var a = new ScanHit(DiscoverySource.Mdns, "192.168.1.5:5555", "SER1", "", "", "aa:bb:cc:dd:ee:ff");
    var b = new ScanHit(DiscoverySource.Tcp, "192.168.1.5:5555", "SER1", "", "", "aa:bb:cc:dd:ee:ff");
    var collapsed = HitDedupe.Collapse(new[] { a, b });
    Assert.Single(collapsed);
}

[Fact]
public void Dedupe_NullMac_FallsBackToSerialPlaceholder()
{
    var a = new ScanHit(DiscoverySource.Mdns, "192.168.1.5:5555", "SER1", "", "", null);
    var b = new ScanHit(DiscoverySource.Tcp, "192.168.1.5:5555", "SER1", "", "", null);
    var collapsed = HitDedupe.Collapse(new[] { a, b });
    Assert.Single(collapsed);
}

[Fact]
public void Dedupe_LastWins_RicherMacReplacesNull()
{
    var a = new ScanHit(DiscoverySource.Tcp, "192.168.1.5:5555", "SER1", "", "", null);
    var b = new ScanHit(DiscoverySource.Mdns, "192.168.1.5:5555", "SER1", "", "", "aa:bb:cc:dd:ee:ff");
    var collapsed = HitDedupe.Collapse(new[] { a, b });
    // Two different keys here — no collapse happens (known limitation: user will see one
    // card, whichever arrives last overwriting as keys change). Assert documented behaviour.
    Assert.Equal(2, collapsed.Count);
}
```

Note: the third test documents a deliberate limitation — the real-world fix is that `DeviceManagement` merges by MAC after an ARP refresh, not at scan-time. Comment-clarify.

- [ ] **Step 2–4: Run, implement, re-run.**

- [ ] **Step 5: Commit**

```bash
git commit -am "feat(scanner): HitDedupe helper + rule documentation"
```

---

## Task 11: DI registration

**Files:**
- Modify: `src/ControlMenu/Program.cs`

- [ ] **Step 1: Register services**

In `Program.cs`, inside `builder.Services`:

```csharp
builder.Services.AddSingleton<INetworkScanService, NetworkScanService>();
builder.Services.AddHttpClient();  // for DependencyManagerService URL ping, if not already present
```

- [ ] **Step 2: Build + run the app; load `/settings/devices` — confirm no DI resolution error**

```bash
dotnet build src/ControlMenu/ControlMenu.csproj -c Debug
```

- [ ] **Step 3: Commit**

```bash
git commit -am "chore(di): register INetworkScanService + HttpClient"
```

---

## Task 12: SubnetDetectionClient (HTTP call)

**Files:**
- Create: `src/ControlMenu/Services/Network/SubnetDetectionClient.cs`
- Create: `tests/ControlMenu.Tests/Services/SubnetDetectionClientTests.cs`

Thin HTTP client that GETs `{BaseUrl}/api/devices/scan/subnet` and returns either `{cidr, hostCount, source}` or `null`.

- [ ] **Step 1: Write failing test with a `FakeHttpMessageHandler`**

```csharp
using ControlMenu.Services.Network;

namespace ControlMenu.Tests.Services;

public class SubnetDetectionClientTests
{
    [Fact]
    public async Task Detect_ReturnsSubnetOn200()
    {
        var handler = new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK,
            "{\"cidr\":\"192.168.1.0/24\",\"hostCount\":254,\"source\":\"gateway\"}");
        var client = new SubnetDetectionClient(new HttpClient(handler), new StubWsScrcpyService("http://fake:8000"));
        var detected = await client.DetectAsync();
        Assert.NotNull(detected);
        Assert.Equal("192.168.1.0/24", detected!.Cidr);
        Assert.Equal(254, detected.HostCount);
    }

    [Fact]
    public async Task Detect_ReturnsNullOnNonSuccess()
    {
        var handler = new FakeHttpMessageHandler(System.Net.HttpStatusCode.ServiceUnavailable, "");
        var client = new SubnetDetectionClient(new HttpClient(handler), new StubWsScrcpyService("http://fake:8000"));
        Assert.Null(await client.DetectAsync());
    }
}
```

- [ ] **Step 2: Implement**

```csharp
using System.Text.Json;

namespace ControlMenu.Services.Network;

public sealed record DetectedSubnet(string Cidr, int HostCount, string Source);

public sealed class SubnetDetectionClient
{
    private readonly HttpClient _http;
    private readonly WsScrcpyService _wsscrcpy;

    public SubnetDetectionClient(HttpClient http, WsScrcpyService wsscrcpy)
    {
        _http = http;
        _wsscrcpy = wsscrcpy;
    }

    public async Task<DetectedSubnet?> DetectAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync($"{_wsscrcpy.BaseUrl}/api/devices/scan/subnet", ct);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<DetectedSubnet>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }
}
```

- [ ] **Step 3: Register in `Program.cs`**

```csharp
builder.Services.AddScoped<SubnetDetectionClient>();
```

- [ ] **Step 4: Run tests + commit**

```bash
git commit -am "feat(scanner): SubnetDetectionClient wraps ws-scrcpy-web /api/devices/scan/subnet"
```

---

## Task 13: Copy subnet cheat sheet

**Files:**
- Create: `src/ControlMenu/wwwroot/help/subnets.html`

- [ ] **Step 1: Copy verbatim**

```bash
cp C:/Users/jscha/source/repos/ws-scrcpy-web/public/help/subnets.html C:/Users/jscha/source/repos/tools-menu/src/ControlMenu/wwwroot/help/subnets.html
```

- [ ] **Step 2: Adjust styling to match Control Menu theme**

Read the existing cheat sheet and replace inline style references that conflict with Control Menu's palette. Minimum: ensure `body { background }` uses `var(--bg)` or matches Control Menu's current settings-page style. If the cheat sheet already uses a simple light-on-dark design, leave it.

- [ ] **Step 3: Verify it loads**

Run the app, browse to `http://localhost:5159/help/subnets.html` — page should render.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/wwwroot/help/subnets.html
git commit -m "feat(scanner): subnet cheat sheet static asset"
```

---

## Task 14: ScanProgressChip.razor

**Files:**
- Create: `src/ControlMenu/Components/Shared/Scanner/ScanProgressChip.razor`
- Create: `src/ControlMenu/Components/Shared/Scanner/ScanProgressChip.razor.css`

Four-state pill rendered inside the modal header. Complete auto-hides after 5s, Cancelled after 10s.

- [ ] **Step 1: Write `ScanProgressChip.razor`**

```razor
@using ControlMenu.Services.Network
@implements IDisposable

@if (_visible)
{
    <div class="scan-chip @_stateClass">
        <span class="scan-dot"></span>
        <span class="scan-label">@_label</span>
        @if (Phase is ScanPhase.Scanning or ScanPhase.Draining)
        {
            <span class="scan-counter">@_progress</span>
        }
    </div>
}

@code {
    [Parameter] public ScanPhase Phase { get; set; }
    [Parameter] public int Checked { get; set; }
    [Parameter] public int Total { get; set; }
    [Parameter] public int FoundSoFar { get; set; }

    private bool _visible;
    private string _stateClass = "";
    private string _label = "";
    private string _progress = "";
    private CancellationTokenSource? _hideCts;

    protected override void OnParametersSet()
    {
        (_stateClass, _label) = Phase switch
        {
            ScanPhase.Idle => ("idle", "idle"),
            ScanPhase.Scanning => ("scanning", "scanning"),
            ScanPhase.Draining => ("draining", "draining"),
            ScanPhase.Complete => ("complete", $"complete — {FoundSoFar} found"),
            ScanPhase.Cancelled => ("cancelled", $"cancelled — {FoundSoFar} found"),
            _ => ("", ""),
        };
        _progress = Total > 0 ? $"{Checked} / {Total}" : "";
        _visible = Phase != ScanPhase.Idle;

        _hideCts?.Cancel();
        if (Phase is ScanPhase.Complete or ScanPhase.Cancelled)
        {
            var timeout = Phase == ScanPhase.Complete ? 5000 : 10000;
            _hideCts = new();
            var token = _hideCts.Token;
            _ = Task.Delay(timeout, token).ContinueWith(_ =>
            {
                InvokeAsync(() => { _visible = false; StateHasChanged(); });
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }
    }

    public void Dispose() => _hideCts?.Cancel();
}
```

- [ ] **Step 2: Write CSS**

```css
.scan-chip { display: inline-flex; gap: .5rem; align-items: center; padding: .25rem .75rem;
    border-radius: 999px; font-size: .85rem; font-weight: 500; }
.scan-chip.scanning { background: rgba(59, 130, 246, .15); color: #2563eb; }
.scan-chip.draining { background: rgba(245, 159, 0, .15); color: #d97706; }
.scan-chip.complete { background: rgba(34, 197, 94, .15); color: #16a34a; }
.scan-chip.cancelled { background: rgba(239, 68, 68, .15); color: #dc2626; }
.scan-dot { width: .5rem; height: .5rem; border-radius: 50%; background: currentColor; }
.scan-chip.scanning .scan-dot { animation: scan-pulse 1s ease-in-out infinite; }
.scan-counter { opacity: .7; font-variant-numeric: tabular-nums; }
@keyframes scan-pulse { 0%, 100% { opacity: 1 } 50% { opacity: .35 } }
```

- [ ] **Step 3: Build; no tests (pure UI)**

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Components/Shared/Scanner/
git commit -m "feat(scanner): ScanProgressChip component with four phase states"
```

---

## Task 15: AddSubnetModal + LargeSubnetWarningModal

**Files:**
- Create: `src/ControlMenu/Components/Shared/Scanner/AddSubnetModal.razor`
- Create: `src/ControlMenu/Components/Shared/Scanner/LargeSubnetWarningModal.razor`

Both are simple reactive modals following existing Control Menu modal patterns (check `DeviceForm.razor` style). Use `SubnetParser.Parse` for live validation.

- [ ] **Step 1: Write `AddSubnetModal.razor`**

```razor
@using ControlMenu.Services.Network

<div class="dialog-overlay" @onclick="Cancel">
    <div class="dialog" @onclick:stopPropagation="true">
        <h3>Add subnet</h3>
        <div class="form-group">
            <input class="form-control @(_error is null ? "" : "is-invalid")"
                   placeholder="192.168.1.0/24 or 192.168.1.10-50 or 192.168.1.5"
                   @bind="_input" @bind:event="oninput" />
            @if (_error is not null)
            {
                <div class="form-hint text-danger">@_error</div>
            }
            else if (_parsed is not null)
            {
                <div class="form-hint">
                    @_parsed.Normalized — @_parsed.HostCount host@(_parsed.HostCount == 1 ? "" : "s")
                </div>
            }
        </div>
        <p class="form-hint">
            <a href="/help/subnets.html" target="_blank" rel="noopener">Subnet syntax help ↗</a>
        </p>
        <div class="dialog-actions">
            <button class="btn btn-secondary" @onclick="Cancel">Cancel</button>
            <button class="btn btn-primary" disabled="@(_parsed is null)" @onclick="Save">Add</button>
        </div>
    </div>
</div>

@code {
    [Parameter] public EventCallback<ParsedSubnet> OnSave { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private string _inputValue = "";
    private string _input
    {
        get => _inputValue;
        set { _inputValue = value; Validate(); }
    }
    private ParsedSubnet? _parsed;
    private string? _error;

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(_inputValue))
        {
            _parsed = null; _error = null; return;
        }
        var r = SubnetParser.Parse(_inputValue);
        if (r.IsSuccess) { _parsed = r.Value; _error = null; }
        else { _parsed = null; _error = r.Error; }
    }

    private Task Save() => _parsed is null ? Task.CompletedTask : OnSave.InvokeAsync(_parsed);
    private Task Cancel() => OnCancel.InvokeAsync();
}
```

- [ ] **Step 2: Write `LargeSubnetWarningModal.razor`**

```razor
@using ControlMenu.Services.Network

<div class="dialog-overlay" @onclick="Cancel">
    <div class="dialog" @onclick:stopPropagation="true">
        <h3>Large subnet</h3>
        <p>
            This scan covers <strong>@TotalHosts.ToString("N0") hosts</strong> across @SubnetCount subnet@(SubnetCount == 1 ? "" : "s").
            Scans larger than 2,048 hosts take noticeably longer and produce far more traffic.
        </p>
        <p>Continue anyway?</p>
        <div class="dialog-actions">
            <button class="btn btn-secondary" @onclick="Cancel">Cancel</button>
            <button class="btn btn-warning" @onclick="Confirm">Scan anyway</button>
        </div>
    </div>
</div>

@code {
    [Parameter] public int TotalHosts { get; set; }
    [Parameter] public int SubnetCount { get; set; }
    [Parameter] public EventCallback OnConfirm { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private Task Confirm() => OnConfirm.InvokeAsync();
    private Task Cancel() => OnCancel.InvokeAsync();
}
```

- [ ] **Step 3: Build; commit**

```bash
git add src/ControlMenu/Components/Shared/Scanner/AddSubnetModal.razor src/ControlMenu/Components/Shared/Scanner/LargeSubnetWarningModal.razor
git commit -m "feat(scanner): AddSubnet + LargeSubnetWarning modals"
```

---

## Task 16: ScanNetworkModal.razor — main composite

**Files:**
- Create: `src/ControlMenu/Components/Shared/Scanner/ScanNetworkModal.razor`
- Create: `src/ControlMenu/Components/Shared/Scanner/ScanNetworkModal.razor.css`

Composes everything. Subscribes to `INetworkScanService`, renders subnet list + hits, Start / Cancel / Add buttons, triggers `LargeSubnetWarningModal` when threshold exceeded.

- [ ] **Step 1: Write the component**

```razor
@using ControlMenu.Services.Network
@using System.Text.Json
@inject INetworkScanService ScanService
@inject IConfigurationService Config
@inject SubnetDetectionClient DetectionClient
@implements IAsyncDisposable

<div class="dialog-overlay" @onclick="Close">
    <div class="dialog dialog-large" @onclick:stopPropagation="true">
        <div class="dialog-header">
            <h3>Scan network</h3>
            <ScanProgressChip Phase="_phase"
                              Checked="_lastProgress?.Checked ?? 0"
                              Total="_lastProgress?.Total ?? 0"
                              FoundSoFar="_hits.Count" />
            <button class="btn-close" @onclick="Close">×</button>
        </div>
        <div class="dialog-body">

            <div class="subnet-list">
                <label>Subnets to scan</label>
                @if (_detected is not null && _subnets.Count == 0)
                {
                    <p class="form-hint">Detected: <code>@_detected.Cidr</code> (@_detected.HostCount hosts)
                        — <button class="btn-link" @onclick="UseDetected">use this</button></p>
                }
                @foreach (var s in _subnets)
                {
                    <div class="subnet-row">
                        <code>@s.Normalized</code>
                        <span class="form-hint">@s.HostCount host@(s.HostCount == 1 ? "" : "s")</span>
                        <button class="btn-sm btn-danger" @onclick="() => RemoveSubnet(s)">Remove</button>
                    </div>
                }
                <button class="btn btn-secondary" @onclick="() => _showAddSubnet = true">+ Add subnet</button>
            </div>

            <div class="hits-area">
                @if (_hits.Count == 0)
                {
                    <p class="form-hint">
                        @(_phase == ScanPhase.Idle ? "Click Start to begin scanning." : "No hits yet.")
                    </p>
                }
                else
                {
                    <table class="data-table">
                        <thead><tr><th>Source</th><th>Name</th><th>Address</th><th>MAC</th><th></th></tr></thead>
                        <tbody>
                            @foreach (var h in _hits)
                            {
                                <tr>
                                    <td><span class="source-tag @h.Source.ToString().ToLowerInvariant()">@h.Source</span></td>
                                    <td>@(string.IsNullOrEmpty(h.Label) ? h.Name : h.Label)</td>
                                    <td><code>@h.Address</code></td>
                                    <td><code>@(h.Mac ?? "—")</code></td>
                                    <td><button class="btn btn-sm btn-primary" @onclick="() => AddHit(h)">Add</button></td>
                                </tr>
                            }
                        </tbody>
                    </table>
                }
            </div>

        </div>
        <div class="dialog-actions">
            <button class="btn btn-secondary" @onclick="Close">Close</button>
            @if (_phase is ScanPhase.Scanning or ScanPhase.Draining)
            {
                <button class="btn btn-warning" @onclick="Cancel">Cancel</button>
            }
            else
            {
                <button class="btn btn-primary" disabled="@(_subnets.Count == 0)" @onclick="StartClicked">Start</button>
            }
        </div>
    </div>
</div>

@if (_showAddSubnet)
{
    <AddSubnetModal OnSave="AddSubnet" OnCancel="() => _showAddSubnet = false" />
}
@if (_showLargeWarn)
{
    <LargeSubnetWarningModal TotalHosts="_subnets.Sum(s => s.HostCount)" SubnetCount="_subnets.Count"
        OnConfirm="StartConfirmed" OnCancel="() => _showLargeWarn = false" />
}

@code {
    [Parameter] public EventCallback<ScanHit> OnAddHit { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private ScanPhase _phase = ScanPhase.Idle;
    private ScanProgressEvent? _lastProgress;
    private readonly List<ScanHit> _hits = new();
    private readonly List<ParsedSubnet> _subnets = new();
    private DetectedSubnet? _detected;
    private bool _showAddSubnet;
    private bool _showLargeWarn;
    private IDisposable? _subscription;

    protected override async Task OnInitializedAsync()
    {
        _subscription = ScanService.Subscribe(OnScanEvent);
        _phase = ScanService.Phase;
        _hits.AddRange(ScanService.Hits);

        await LoadSubnetsAsync();
        _detected = await DetectionClient.DetectAsync();
    }

    private async Task LoadSubnetsAsync()
    {
        var raw = await Config.GetSettingAsync("scan-subnets");
        if (string.IsNullOrEmpty(raw)) return;
        try
        {
            var strings = JsonSerializer.Deserialize<string[]>(raw) ?? Array.Empty<string>();
            foreach (var s in strings)
            {
                var p = SubnetParser.Parse(s);
                if (p.IsSuccess) _subnets.Add(p.Value!);
            }
        }
        catch { /* ignore corrupt JSON — scan with empty list */ }
    }

    private async Task SaveSubnetsAsync()
    {
        await Config.SetSettingAsync("scan-subnets",
            JsonSerializer.Serialize(_subnets.Select(s => s.Raw).ToArray()));
    }

    private async Task AddSubnet(ParsedSubnet subnet)
    {
        _showAddSubnet = false;
        _subnets.Add(subnet);
        await SaveSubnetsAsync();
    }

    private async Task RemoveSubnet(ParsedSubnet subnet)
    {
        _subnets.Remove(subnet);
        await SaveSubnetsAsync();
    }

    private async Task UseDetected()
    {
        if (_detected is null) return;
        var p = SubnetParser.Parse(_detected.Cidr);
        if (!p.IsSuccess) return;
        _subnets.Add(p.Value!);
        await SaveSubnetsAsync();
    }

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

    private Task Cancel() => ScanService.CancelAsync();

    private async Task AddHit(ScanHit hit)
    {
        await OnAddHit.InvokeAsync(hit);
        await Close();
    }

    private void OnScanEvent(ScanEvent evt)
    {
        InvokeAsync(() =>
        {
            switch (evt)
            {
                case ScanStartedEvent: _hits.Clear(); _lastProgress = null; break;
                case ScanProgressEvent p: _lastProgress = p; break;
                case ScanHitEvent h: _hits.Add(h.Hit); break;
                case ScanErrorEvent: /* surfaced via notification from parent */ break;
            }
            _phase = ScanService.Phase;
            StateHasChanged();
        });
    }

    private Task Close() => OnClose.InvokeAsync();

    public ValueTask DisposeAsync()
    {
        _subscription?.Dispose();
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 2: Build; commit**

```bash
git commit -am "feat(scanner): ScanNetworkModal main composite component"
```

---

## Task 17: DeviceManagement.razor — button split + Discovered semantics

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Settings/DeviceManagement.razor`

Change the single `Scan Network` button into **two** buttons: `⟳ Quick Refresh` (today's `ScanNetwork` logic, renamed) and `📡 Scan Network…` (opens `ScanNetworkModal`). Update Discovered section: **Full Scan replaces** the list, **Quick Refresh merges** (existing behaviour).

- [ ] **Step 1: Rename `ScanNetwork()` → `QuickRefresh()` internally; preserve logic**

Find in `DeviceManagement.razor`:

```razor
<button class="btn btn-secondary" @onclick="ScanNetwork" disabled="@_scanning">
    <i class="bi bi-broadcast"></i> @(_scanning ? "Scanning..." : "Scan Network")
</button>
```

Replace with:

```razor
<button class="btn btn-secondary" @onclick="QuickRefresh" disabled="@_scanning">
    <i class="bi bi-arrow-clockwise"></i> @(_scanning ? "Refreshing..." : "Quick Refresh")
</button>
<button class="btn btn-secondary" @onclick="() => _showScanModal = true">
    <i class="bi bi-broadcast"></i> Scan Network…
</button>
```

Rename method `ScanNetwork` → `QuickRefresh` in `@code`.

- [ ] **Step 2: Add modal invocation + Add-from-scan handler**

At the end of the markup, before the closing tag of the component:

```razor
@if (_showScanModal)
{
    <ScanNetworkModal OnAddHit="AddFromScan" OnClose="() => _showScanModal = false" />
}
```

Add fields + handler in `@code`:

```csharp
private bool _showScanModal;

private async Task AddFromScan(ScanHit hit)
{
    _showScanModal = false;
    var parts = hit.Address.Split(':');
    var ip = parts[0];
    var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 5555;
    // Reuse the AddFromDiscovery path — it already handles ADB probe, model read,
    // and DeviceForm open. Convert ScanHit to the existing DiscoveredDevice shape.
    await AddFromDiscovery(new DiscoveredDevice(hit.Name, ip, port, hit.Mac));
}
```

- [ ] **Step 3: Update Discovered section semantics — Full Scan replaces**

In `AddFromScan`... actually the replace rule applies when the SCAN completes, not when user adds. Add a `ScanCompleteEvent` handler that replaces `_discovered` with the dedupe-collapsed scan hits minus any already-registered. Wire from `ScanNetworkModal`'s `OnClose` or via a new `OnScanComplete` callback.

Simplest: expose `OnScanComplete` callback from `ScanNetworkModal` that fires with the collapsed hit list:

In `ScanNetworkModal.razor`, add:

```csharp
[Parameter] public EventCallback<IReadOnlyList<ScanHit>> OnScanComplete { get; set; }
```

And in `OnScanEvent`:

```csharp
case ScanCompleteEvent:
    OnScanComplete.InvokeAsync(HitDedupe.Collapse(_hits).ToList());
    break;
```

In `DeviceManagement.razor`:

```csharp
private async Task OnFullScanComplete(IReadOnlyList<ScanHit> hits)
{
    // Replace: Full Scan's Discovered section shows only the hits from this scan.
    _discovered = hits.Where(h => !_devices.Any(d =>
            string.Equals(d.MacAddress, h.Mac, StringComparison.OrdinalIgnoreCase)))
        .Select(h =>
        {
            var parts = h.Address.Split(':');
            var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 5555;
            return new DiscoveredDevice(h.Name, parts[0], port, h.Mac);
        })
        .ToList();
    await InvokeAsync(StateHasChanged);
}
```

Wire: `<ScanNetworkModal OnScanComplete="OnFullScanComplete" OnAddHit="..." OnClose="..." />`

Quick Refresh merges — keep existing `ScanNetwork()` (now `QuickRefresh()`) logic that appends new hits without clearing.

- [ ] **Step 4: Build; manually smoke via the app**

```bash
dotnet run --project src/ControlMenu/ControlMenu.csproj -c Debug
```

Open `http://localhost:5159/settings/devices`. Verify:
- Two buttons visible.
- Quick Refresh does what old "Scan Network" did.
- Scan Network… opens the new modal.
- Add from modal closes modal and opens DeviceForm.

- [ ] **Step 5: Commit**

```bash
git commit -am "feat(settings): split scan button into Quick Refresh + Scan Network…"
```

---

## Task 18: GeneralSettings.razor — deploy-mode radio + URL input

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Settings/GeneralSettings.razor`

- [ ] **Step 1: Add form section**

```razor
<div class="settings-section">
    <h2>ws-scrcpy-web deployment</h2>
    <div class="form-group">
        <label>Deployment mode</label>
        <div class="radio-group">
            <label>
                <input type="radio" name="wsscrcpyMode" value="managed" checked="@(_wsscrcpyMode == "managed")"
                       @onchange="() => SetWsScrcpyMode(""managed"")" />
                Managed — Control Menu spawns and watches the node process on port 8000.
            </label>
            <label>
                <input type="radio" name="wsscrcpyMode" value="external" checked="@(_wsscrcpyMode == "external")"
                       @onchange="() => SetWsScrcpyMode(""external"")" />
                External — connect to an already-running ws-scrcpy-web at the URL below (Docker, remote host).
            </label>
        </div>
    </div>

    @if (_wsscrcpyMode == "external")
    {
        <div class="form-group">
            <label>ws-scrcpy-web URL</label>
            <input class="form-control" style="max-width:400px;" @bind="_wsscrcpyUrl" @bind:event="onchange" />
            <div class="form-hint">
                e.g. <code>http://localhost:8000</code> or <code>http://ws-scrcpy:8000</code>.
                If running in Docker and you want mDNS discovery to work, the container needs
                <code>--network host</code> (Linux) or the TCP sweep will work but mDNS will return zero hits.
            </div>
        </div>
    }
</div>

@code {
    private string _wsscrcpyMode = "managed";
    private string _wsscrcpyUrl = "http://localhost:8000";

    // OnInitialized addition:
    // _wsscrcpyMode = (await Config.GetSettingAsync("wsscrcpy-mode")) ?? "managed";
    // _wsscrcpyUrl = (await Config.GetSettingAsync("wsscrcpy-url")) ?? "http://localhost:8000";

    private async Task SetWsScrcpyMode(string mode)
    {
        _wsscrcpyMode = mode;
        await Config.SetSettingAsync("wsscrcpy-mode", mode);
        await ShowMessage("Deploy mode saved. Restart to apply.");
    }
}
```

Save URL field on blur. Use existing `ShowMessage` notification pattern.

- [ ] **Step 2: Build, load page, smoke check**

- [ ] **Step 3: Commit**

```bash
git commit -am "feat(settings): Managed vs External deploy mode for ws-scrcpy-web"
```

---

## Task 19: DependencyManagement.razor — read-only URL in External mode

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Settings/DependencyManagement.razor`

- [ ] **Step 1: Hide install/update controls for ws-scrcpy-web in External mode**

Find the dependency row rendering and add a gate:

```razor
@if (dep.Name == "ws-scrcpy-web" && _wsscrcpyMode == "external")
{
    <span class="form-hint">External: <code>@_wsscrcpyUrl</code></span>
}
else
{
    @* existing install / update / status buttons unchanged *@
}
```

Read mode + URL in `OnInitializedAsync`. Reload if `wsscrcpy-mode` changes (user toggled in GeneralSettings and then navigated here).

- [ ] **Step 2: Manual check — External mode hides Install, shows URL**

- [ ] **Step 3: Commit**

```bash
git commit -am "feat(settings): hide install controls for External-mode ws-scrcpy-web"
```

---

## Task 20: CHANGELOG update

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add under `## [Unreleased]`**

```markdown
### Added

- **Network scanner modal** — `Settings › Devices` gained a new *Scan Network…* button that opens a full scanner modal (CIDR/IP/range subnet input, gateway auto-detect, four-state progress chip, live hit stream, Add buttons per hit, 2,048-host large-subnet warning, subnet syntax cheat sheet at `/help/subnets.html`).
- **ws-scrcpy-web deployment mode** — General Settings gained a Managed / External toggle. Managed keeps today's auto-spawn behaviour; External lets Control Menu point at a Docker container or remote host via URL. Dependency management respects mode: Install/Update hidden in External mode.
- **`NetworkScanService` (singleton)** — holds a `ClientWebSocket` to ws-scrcpy-web's `/ws-scan` endpoint, fans scan events to every Blazor circuit spectating a scan. Two browser tabs attaching mid-scan share one scan session with snapshot replay.
- **`SubnetParser`** — CIDR / IP / range parser mirroring ws-scrcpy-web's behaviour byte-for-byte, error verbiage included.

### Changed

- **`Settings › Devices`** — `Scan Network` button split into `⟳ Quick Refresh` (today's silent mDNS+ARP, unchanged) and `📡 Scan Network…` (new modal). "Discovered on Network" section now mirrors the last scan: Full Scan replaces the list; Quick Refresh merges.
- **`WsScrcpyService`** — `StartAsync` short-circuits process spawn in External mode. `BaseUrl` resolves at start time based on mode.
- **`DependencyManagerService.CheckHealthAsync`** — ws-scrcpy-web entry pings `{url}/` in External mode instead of checking process state.
```

- [ ] **Step 2: Commit**

```bash
git commit -am "docs(changelog): scanner port from ws-scrcpy-web"
```

---

## Task 21: Manual QA pass

Run through the 15-item checklist from the design spec §Testing strategy. Resolve anything that fails.

- [ ] 1. Modal opens, gateway subnet auto-suggested, host count shown.
- [ ] 2. Add-subnet: CIDR / IP / range accepted; garbage rejected inline.
- [ ] 3. Subnets persist across reload (from `Settings` table).
- [ ] 4. Large-subnet warning fires on `> 2048` hosts (test with `192.168.0.0/20` — 4094 hosts).
- [ ] 5. Progress chip update cadence matches scan activity.
- [ ] 6. Cancel lifecycle: `Scanning → Draining → Cancelled`.
- [ ] 7. Complete chip auto-hides after 5s; Cancelled after 10s.
- [ ] 8. Cheat sheet link opens `/help/subnets.html`, back-link returns cleanly.
- [ ] 9. mDNS + TCP dedupe — one card per device.
- [ ] 10. Devices already in `adb devices` refresh in place, not re-offered.
- [ ] 11. Managed mode: Control Menu spawns ws-scrcpy-web, scan works.
- [ ] 12. External mode: Control Menu skips spawn, URL pings health, scan works against container.
- [ ] 13. Two-tab spectator: second tab opening mid-scan gets snapshot, shared cancel.
- [ ] 14. Add from scan: modal closes, DeviceForm pre-filled, save adds to table and drops from Discovered.
- [ ] 15. Discovered section: Full Scan replaces, Quick Refresh merges.

- [ ] **Final commit**

```bash
git commit -am "test(scanner): manual QA checklist — all items pass"
```

---

## Self-Review Checklist (ran during plan authoring)

- **Spec coverage:** Architecture (Task 11, 17), Components new (1, 2, 6, 7, 10, 12, 14, 15, 16), Components modified (4, 5, 17, 18, 19), Data flow happy path (8, 16, 17), mid-scan spectator (7, 16), Quick Refresh (17), dedupe (10, 17), error handling (5, 8, 9, 17, 18), testing strategy (2, 4, 5, 6, 7, 8, 9, 10, 12), deployment notes (4, 5, 18, 19), out-of-scope enforced by the absence of Spec 2 scope — ✓ all covered.
- **Placeholder scan:** no TBDs; every step either shows code or a concrete command/expectation. The single phrase "Adjust styling to match Control Menu theme" in Task 13 is deliberately loose because it depends on what ws-scrcpy-web's cheat sheet looks like at port time — low-risk.
- **Type consistency:** `ParsedSubnet` (Task 1) matches uses in Tasks 2, 12, 15, 16. `ScanHit` (Task 1) matches Tasks 7–10, 16, 17. `ScanPhase` enum consistent throughout. `DeployMode` → `WsScrcpyDeployMode` — used only in Task 4; Task 18 uses the lowercase setting string (`"managed"` / `"external"`) rather than the enum, matching the stored format.
- **Dependency ordering:** Tasks 1–3 are foundation. 4–6 wire deploy mode + test infra. 7–10 build the service. 11–13 register and stub UI scaffolding. 14–16 UI. 17–19 wire into the app. 20 docs. 21 QA.
