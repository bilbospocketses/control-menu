# Phase 3: Android Devices Module — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Android Devices module — ADB service, Google TV dashboard, Pixel dashboard, and device selector — replacing the PowerShell Google TV and Pixel 9 sub-menus with a web-based UI.

**Architecture:** An `AndroidDevicesModule` implements `IToolModule` and is auto-discovered at startup. `AdbService` wraps all ADB commands through the existing `ICommandExecutor` for cross-platform support. Two dashboard pages (Google TV, Pixel) display action cards that call `AdbService` methods. A device selector lets the user pick which registered device to manage.

**Tech Stack:** .NET 9, Blazor Server, EF Core + SQLite, xUnit + Moq, Bootstrap Icons (already loaded)

---

## File Structure

### New Services

```
src/ControlMenu/Modules/AndroidDevices/
├── AndroidDevicesModule.cs
├── Services/
│   ├── IAdbService.cs
│   └── AdbService.cs
├── Pages/
│   ├── DeviceSelector.razor
│   ├── GoogleTvDashboard.razor
│   ├── GoogleTvDashboard.razor.css
│   ├── PixelDashboard.razor
│   └── PixelDashboard.razor.css
```

### New Tests

```
tests/ControlMenu.Tests/Modules/AndroidDevices/
├── AdbServiceTests.cs
├── AndroidDevicesModuleTests.cs
```

### Modified Files

```
src/ControlMenu/Program.cs              (register IAdbService)
src/ControlMenu/wwwroot/css/app.css     (dashboard card styles)
```

---

## Task 1: IAdbService Interface & AdbService

**Files:**
- Create: `src/ControlMenu/Modules/AndroidDevices/Services/IAdbService.cs`
- Create: `src/ControlMenu/Modules/AndroidDevices/Services/AdbService.cs`
- Create: `tests/ControlMenu.Tests/Modules/AndroidDevices/AdbServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/ControlMenu.Tests/Modules/AndroidDevices/AdbServiceTests.cs`:
```csharp
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Modules.AndroidDevices;

public class AdbServiceTests
{
    private readonly Mock<ICommandExecutor> _mockExecutor = new();

    private AdbService CreateService() => new(_mockExecutor.Object);

    [Fact]
    public async Task ConnectAsync_ReturnsTrue_WhenAdbConnectsSuccessfully()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", "connect 192.168.1.100:5555", null, default))
            .ReturnsAsync(new CommandResult(0, "connected to 192.168.1.100:5555", "", false));

        var service = CreateService();
        var result = await service.ConnectAsync("192.168.1.100", 5555);

        Assert.True(result);
    }

    [Fact]
    public async Task ConnectAsync_ReturnsFalse_WhenAdbFails()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", "connect 192.168.1.100:5555", null, default))
            .ReturnsAsync(new CommandResult(1, "", "cannot connect", false));

        var service = CreateService();
        var result = await service.ConnectAsync("192.168.1.100", 5555);

        Assert.False(result);
    }

    [Fact]
    public async Task DisconnectAsync_CallsAdbDisconnect()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", "disconnect 192.168.1.100:5555", null, default))
            .ReturnsAsync(new CommandResult(0, "disconnected", "", false));

        var service = CreateService();
        await service.DisconnectAsync("192.168.1.100", 5555);

        _mockExecutor.Verify(e => e.ExecuteAsync("adb", "disconnect 192.168.1.100:5555", null, default), Times.Once);
    }

    [Fact]
    public async Task GetPowerStateAsync_ReturnsAwake_WhenDeviceIsOn()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", "-s 192.168.1.100:5555 shell dumpsys power", null, default))
            .ReturnsAsync(new CommandResult(0, "mWakefulness=Awake\nmwakefulness=awake", "", false));

        var service = CreateService();
        var result = await service.GetPowerStateAsync("192.168.1.100", 5555);

        Assert.Equal(PowerState.Awake, result);
    }

    [Fact]
    public async Task GetPowerStateAsync_ReturnsAsleep_WhenDeviceIsOff()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", "-s 192.168.1.100:5555 shell dumpsys power", null, default))
            .ReturnsAsync(new CommandResult(0, "mWakefulness=Asleep", "", false));

        var service = CreateService();
        var result = await service.GetPowerStateAsync("192.168.1.100", 5555);

        Assert.Equal(PowerState.Asleep, result);
    }

    [Fact]
    public async Task RebootAsync_CallsAdbReboot()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", "-s 192.168.1.100:5555 shell reboot", null, default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        await service.RebootAsync("192.168.1.100", 5555);

        _mockExecutor.Verify(e => e.ExecuteAsync("adb", "-s 192.168.1.100:5555 shell reboot", null, default), Times.Once);
    }

    [Fact]
    public async Task TogglePowerAsync_SendsKeyEvent()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", "-s 192.168.1.100:5555 shell input keyevent KEYCODE_POWER", null, default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        await service.TogglePowerAsync("192.168.1.100", 5555);

        _mockExecutor.Verify(e => e.ExecuteAsync("adb", "-s 192.168.1.100:5555 shell input keyevent KEYCODE_POWER", null, default), Times.Once);
    }

    [Fact]
    public async Task GetScreensaverAsync_ReturnsSkyFolio_WhenSet()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", "-s 192.168.1.100:5555 shell settings get secure screensaver_components", null, default))
            .ReturnsAsync(new CommandResult(0, "com.snapwood.skyfolio/com.snapwood.skyfolio.DreamService", "", false));

        var service = CreateService();
        var result = await service.GetScreensaverAsync("192.168.1.100", 5555);

        Assert.Equal("SkyFolio", result);
    }

    [Fact]
    public async Task GetScreensaverAsync_ReturnsGoogle_WhenBackdropSet()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", "-s 192.168.1.100:5555 shell settings get secure screensaver_components", null, default))
            .ReturnsAsync(new CommandResult(0, "com.google.android.apps.tv.dreamx/.service.Backdrop", "", false));

        var service = CreateService();
        var result = await service.GetScreensaverAsync("192.168.1.100", 5555);

        Assert.Equal("Google", result);
    }

    [Fact]
    public async Task SetScreensaverAsync_SetsSkyFolioComponent()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb",
            "-s 192.168.1.100:5555 shell settings put secure screensaver_components com.snapwood.skyfolio/com.snapwood.skyfolio.DreamService",
            null, default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        await service.SetScreensaverAsync("192.168.1.100", 5555, "SkyFolio");

        _mockExecutor.Verify(e => e.ExecuteAsync("adb",
            "-s 192.168.1.100:5555 shell settings put secure screensaver_components com.snapwood.skyfolio/com.snapwood.skyfolio.DreamService",
            null, default), Times.Once);
    }

    [Fact]
    public async Task GetScreenTimeoutAsync_ReturnsMilliseconds()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", "-s 192.168.1.100:5555 shell settings get system screen_off_timeout", null, default))
            .ReturnsAsync(new CommandResult(0, "300000", "", false));

        var service = CreateService();
        var result = await service.GetScreenTimeoutAsync("192.168.1.100", 5555);

        Assert.Equal(300000, result);
    }

    [Fact]
    public async Task IsLauncherDisabledAsync_ReturnsTrue_WhenPackageDisabled()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", "-s 192.168.1.100:5555 shell pm list packages -d", null, default))
            .ReturnsAsync(new CommandResult(0, "package:com.google.android.apps.tv.launcherx\npackage:com.other.app", "", false));

        var service = CreateService();
        var result = await service.IsLauncherDisabledAsync("192.168.1.100", 5555);

        Assert.True(result);
    }

    [Fact]
    public async Task StartShizukuAsync_RunsStartScript()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb",
            "-s 192.168.1.100:5555 shell sh /storage/emulated/0/Android/data/moe.shizuku.privileged.api/start.sh",
            null, default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        await service.StartShizukuAsync("192.168.1.100", 5555);

        _mockExecutor.Verify(e => e.ExecuteAsync("adb",
            "-s 192.168.1.100:5555 shell sh /storage/emulated/0/Android/data/moe.shizuku.privileged.api/start.sh",
            null, default), Times.Once);
    }

    [Fact]
    public async Task ListProjectivyBackupsAsync_ReturnsFileList()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb",
            "-s 192.168.1.100:5555 shell ls /storage/emulated/0/Projectivy-Backups",
            null, default))
            .ReturnsAsync(new CommandResult(0, "backup_2026-01-01.json\nbackup_2026-02-01.json", "", false));

        var service = CreateService();
        var result = await service.ListProjectivyBackupsAsync("192.168.1.100", 5555);

        Assert.Equal(2, result.Count);
        Assert.Contains("backup_2026-01-01.json", result);
        Assert.Contains("backup_2026-02-01.json", result);
    }

    [Fact]
    public async Task RestoreProjectivyBackupAsync_LaunchesRestoreIntent()
    {
        var filename = "backup_2026-01-01.json";
        _mockExecutor.Setup(e => e.ExecuteAsync("adb",
            $"-s 192.168.1.100:5555 shell am start -a android.intent.action.VIEW -d \"file:///storage/emulated/0/Projectivy-Backups/{filename}\" -n com.spocky.projengmenu/.ui.launcherActivities.ImportSettingsActivity",
            null, default))
            .ReturnsAsync(new CommandResult(0, "Starting: Intent", "", false));

        var service = CreateService();
        await service.RestoreProjectivyBackupAsync("192.168.1.100", 5555, filename);

        _mockExecutor.Verify(e => e.ExecuteAsync("adb",
            $"-s 192.168.1.100:5555 shell am start -a android.intent.action.VIEW -d \"file:///storage/emulated/0/Projectivy-Backups/{filename}\" -n com.spocky.projengmenu/.ui.launcherActivities.ImportSettingsActivity",
            null, default), Times.Once);
    }

    [Fact]
    public async Task ResetTcpPortAsync_RunsTcpipCommand()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", "tcpip 5555", null, default))
            .ReturnsAsync(new CommandResult(0, "restarting in TCP mode port: 5555", "", false));

        var service = CreateService();
        await service.ResetTcpPortAsync(5555);

        _mockExecutor.Verify(e => e.ExecuteAsync("adb", "tcpip 5555", null, default), Times.Once);
    }

    [Fact]
    public async Task GetConnectedDevicesAsync_ParsesAdbDevices()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", "devices", null, default))
            .ReturnsAsync(new CommandResult(0, "List of devices attached\n192.168.1.100:5555\tdevice\n192.168.1.101:5555\tdevice\n\n", "", false));

        var service = CreateService();
        var result = await service.GetConnectedDevicesAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains("192.168.1.100:5555", result);
        Assert.Contains("192.168.1.101:5555", result);
    }

    [Fact]
    public async Task DisconnectAllAsync_DisconnectsEachDevice()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", "devices", null, default))
            .ReturnsAsync(new CommandResult(0, "List of devices attached\n192.168.1.100:5555\tdevice\n", "", false));
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", "disconnect 192.168.1.100:5555", null, default))
            .ReturnsAsync(new CommandResult(0, "disconnected", "", false));

        var service = CreateService();
        await service.DisconnectAllAsync();

        _mockExecutor.Verify(e => e.ExecuteAsync("adb", "disconnect 192.168.1.100:5555", null, default), Times.Once);
    }

    [Fact]
    public async Task LaunchScrcpyAsync_BuildsCorrectCommand()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("scrcpy",
            "--video-encoder=OMX.google.h264.encoder --no-audio -s 192.168.1.100:5555",
            null, default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        await service.LaunchScrcpyAsync("192.168.1.100", 5555);

        _mockExecutor.Verify(e => e.ExecuteAsync("scrcpy",
            "--video-encoder=OMX.google.h264.encoder --no-audio -s 192.168.1.100:5555",
            null, default), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~AdbServiceTests" --no-build`
Expected: Build failure — `AdbService`, `IAdbService`, `PowerState` do not exist yet.

- [ ] **Step 3: Create the IAdbService interface and PowerState enum**

`src/ControlMenu/Modules/AndroidDevices/Services/IAdbService.cs`:
```csharp
namespace ControlMenu.Modules.AndroidDevices.Services;

public enum PowerState
{
    Awake,
    Asleep,
    Unknown
}

public interface IAdbService
{
    Task<bool> ConnectAsync(string ip, int port, CancellationToken ct = default);
    Task DisconnectAsync(string ip, int port, CancellationToken ct = default);
    Task<PowerState> GetPowerStateAsync(string ip, int port, CancellationToken ct = default);
    Task RebootAsync(string ip, int port, CancellationToken ct = default);
    Task TogglePowerAsync(string ip, int port, CancellationToken ct = default);
    Task<string> GetScreensaverAsync(string ip, int port, CancellationToken ct = default);
    Task SetScreensaverAsync(string ip, int port, string screensaver, CancellationToken ct = default);
    Task<int> GetScreenTimeoutAsync(string ip, int port, CancellationToken ct = default);
    Task SetScreenTimeoutAsync(string ip, int port, int milliseconds, CancellationToken ct = default);
    Task<bool> IsLauncherDisabledAsync(string ip, int port, CancellationToken ct = default);
    Task SetLauncherEnabledAsync(string ip, int port, bool enabled, CancellationToken ct = default);
    Task StartShizukuAsync(string ip, int port, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListProjectivyBackupsAsync(string ip, int port, CancellationToken ct = default);
    Task RestoreProjectivyBackupAsync(string ip, int port, string filename, CancellationToken ct = default);
    Task ResetTcpPortAsync(int port, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetConnectedDevicesAsync(CancellationToken ct = default);
    Task DisconnectAllAsync(CancellationToken ct = default);
    Task LaunchScrcpyAsync(string ip, int port, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement AdbService**

`src/ControlMenu/Modules/AndroidDevices/Services/AdbService.cs`:
```csharp
using ControlMenu.Services;

namespace ControlMenu.Modules.AndroidDevices.Services;

public class AdbService : IAdbService
{
    private readonly ICommandExecutor _executor;

    public AdbService(ICommandExecutor executor)
    {
        _executor = executor;
    }

    private string DeviceArg(string ip, int port) => $"-s {ip}:{port}";

    public async Task<bool> ConnectAsync(string ip, int port, CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("adb", $"connect {ip}:{port}", null, ct);
        return result.ExitCode == 0 && result.StandardOutput.Contains("connected");
    }

    public async Task DisconnectAsync(string ip, int port, CancellationToken ct = default)
    {
        await _executor.ExecuteAsync("adb", $"disconnect {ip}:{port}", null, ct);
    }

    public async Task<PowerState> GetPowerStateAsync(string ip, int port, CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell dumpsys power", null, ct);
        if (result.ExitCode != 0) return PowerState.Unknown;
        if (result.StandardOutput.Contains("mwakefulness=awake", StringComparison.OrdinalIgnoreCase)
            || result.StandardOutput.Contains("mWakefulness=Awake"))
            return PowerState.Awake;
        return PowerState.Asleep;
    }

    public async Task RebootAsync(string ip, int port, CancellationToken ct = default)
    {
        await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell reboot", null, ct);
    }

    public async Task TogglePowerAsync(string ip, int port, CancellationToken ct = default)
    {
        await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell input keyevent KEYCODE_POWER", null, ct);
    }

    public async Task<string> GetScreensaverAsync(string ip, int port, CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell settings get secure screensaver_components", null, ct);
        var output = result.StandardOutput.Trim();
        if (output.Contains("skyfolio", StringComparison.OrdinalIgnoreCase))
            return "SkyFolio";
        return "Google";
    }

    public async Task SetScreensaverAsync(string ip, int port, string screensaver, CancellationToken ct = default)
    {
        var component = screensaver switch
        {
            "SkyFolio" => "com.snapwood.skyfolio/com.snapwood.skyfolio.DreamService",
            _ => "com.google.android.apps.tv.dreamx/.service.Backdrop"
        };
        await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell settings put secure screensaver_components {component}", null, ct);
    }

    public async Task<int> GetScreenTimeoutAsync(string ip, int port, CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell settings get system screen_off_timeout", null, ct);
        return int.TryParse(result.StandardOutput.Trim(), out var ms) ? ms : 0;
    }

    public async Task SetScreenTimeoutAsync(string ip, int port, int milliseconds, CancellationToken ct = default)
    {
        await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell settings put system screen_off_timeout {milliseconds}", null, ct);
    }

    public async Task<bool> IsLauncherDisabledAsync(string ip, int port, CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell pm list packages -d", null, ct);
        return result.StandardOutput.Contains("com.google.android.apps.tv.launcherx");
    }

    public async Task SetLauncherEnabledAsync(string ip, int port, bool enabled, CancellationToken ct = default)
    {
        if (enabled)
        {
            await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell pm enable com.google.android.apps.tv.launcherx", null, ct);
            await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell pm enable com.google.android.tungsten.setupwraith", null, ct);
        }
        else
        {
            await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell pm disable-user --user 0 com.google.android.apps.tv.launcherx", null, ct);
            await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell pm disable-user --user 0 com.google.android.tungsten.setupwraith", null, ct);
        }
    }

    public async Task StartShizukuAsync(string ip, int port, CancellationToken ct = default)
    {
        await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell sh /storage/emulated/0/Android/data/moe.shizuku.privileged.api/start.sh", null, ct);
    }

    public async Task<IReadOnlyList<string>> ListProjectivyBackupsAsync(string ip, int port, CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell ls /storage/emulated/0/Projectivy-Backups", null, ct);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            return [];
        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    public async Task RestoreProjectivyBackupAsync(string ip, int port, string filename, CancellationToken ct = default)
    {
        await _executor.ExecuteAsync("adb",
            $"{DeviceArg(ip, port)} shell am start -a android.intent.action.VIEW -d \"file:///storage/emulated/0/Projectivy-Backups/{filename}\" -n com.spocky.projengmenu/.ui.launcherActivities.ImportSettingsActivity",
            null, ct);
    }

    public async Task ResetTcpPortAsync(int port, CancellationToken ct = default)
    {
        await _executor.ExecuteAsync("adb", $"tcpip {port}", null, ct);
    }

    public async Task<IReadOnlyList<string>> GetConnectedDevicesAsync(CancellationToken ct = default)
    {
        var result = await _executor.ExecuteAsync("adb", "devices", null, ct);
        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(1) // skip "List of devices attached" header
            .Where(line => line.Contains('\t'))
            .Select(line => line.Split('\t')[0])
            .ToList();
    }

    public async Task DisconnectAllAsync(CancellationToken ct = default)
    {
        var devices = await GetConnectedDevicesAsync(ct);
        foreach (var device in devices)
        {
            await _executor.ExecuteAsync("adb", $"disconnect {device}", null, ct);
        }
    }

    public async Task LaunchScrcpyAsync(string ip, int port, CancellationToken ct = default)
    {
        await _executor.ExecuteAsync("scrcpy", $"--video-encoder=OMX.google.h264.encoder --no-audio -s {ip}:{port}", null, ct);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~AdbServiceTests" -v n`
Expected: All 17 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Services/ tests/ControlMenu.Tests/Modules/AndroidDevices/AdbServiceTests.cs
git commit -m "feat(android): add AdbService with full ADB command coverage"
```

---

## Task 2: AndroidDevicesModule (IToolModule Implementation)

**Files:**
- Create: `src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs`
- Create: `tests/ControlMenu.Tests/Modules/AndroidDevices/AndroidDevicesModuleTests.cs`
- Modify: `src/ControlMenu/Program.cs`

- [ ] **Step 1: Write the failing tests**

`tests/ControlMenu.Tests/Modules/AndroidDevices/AndroidDevicesModuleTests.cs`:
```csharp
using ControlMenu.Data.Enums;
using ControlMenu.Modules;
using ControlMenu.Modules.AndroidDevices;

namespace ControlMenu.Tests.Modules.AndroidDevices;

public class AndroidDevicesModuleTests
{
    private readonly AndroidDevicesModule _module = new();

    [Fact]
    public void Id_IsAndroidDevices()
    {
        Assert.Equal("android-devices", _module.Id);
    }

    [Fact]
    public void DisplayName_IsAndroidDevices()
    {
        Assert.Equal("Android Devices", _module.DisplayName);
    }

    [Fact]
    public void Icon_IsPhoneIcon()
    {
        Assert.Equal("bi-phone", _module.Icon);
    }

    [Fact]
    public void Dependencies_IncludesAdbAndScrcpy()
    {
        var deps = _module.Dependencies.ToList();
        Assert.Contains(deps, d => d.Name == "adb");
        Assert.Contains(deps, d => d.Name == "scrcpy");
    }

    [Fact]
    public void NavEntries_IncludesDeviceSelectorAndDashboards()
    {
        var entries = _module.GetNavEntries().ToList();
        Assert.Contains(entries, e => e.Href == "/android/devices");
        Assert.Contains(entries, e => e.Href == "/android/googletv");
        Assert.Contains(entries, e => e.Href == "/android/pixel");
    }

    [Fact]
    public void GetBackgroundJobs_ReturnsEmpty()
    {
        Assert.Empty(_module.GetBackgroundJobs());
    }

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
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~AndroidDevicesModuleTests" --no-build`
Expected: Build failure — `AndroidDevicesModule` does not exist yet.

- [ ] **Step 3: Implement AndroidDevicesModule**

`src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs`:
```csharp
using ControlMenu.Data.Enums;

namespace ControlMenu.Modules.AndroidDevices;

public class AndroidDevicesModule : IToolModule
{
    public string Id => "android-devices";
    public string DisplayName => "Android Devices";
    public string Icon => "bi-phone";
    public int SortOrder => 1;

    public IEnumerable<ModuleDependency> Dependencies =>
    [
        new ModuleDependency
        {
            Name = "adb",
            ExecutableName = "adb",
            VersionCommand = "adb --version",
            VersionPattern = @"Android Debug Bridge version ([\d.]+)",
            SourceType = UpdateSourceType.Manual,
            ProjectHomeUrl = "https://developer.android.com/tools/releases/platform-tools"
        },
        new ModuleDependency
        {
            Name = "scrcpy",
            ExecutableName = "scrcpy",
            VersionCommand = "scrcpy --version",
            VersionPattern = @"scrcpy ([\d.]+)",
            SourceType = UpdateSourceType.GitHub,
            GitHubRepo = "Genymobile/scrcpy",
            AssetPattern = @"scrcpy-win64-v[\d.]+\.zip"
        }
    ];

    public IEnumerable<ConfigRequirement> ConfigRequirements => [];

    public IEnumerable<NavEntry> GetNavEntries() =>
    [
        new NavEntry("Device List", "/android/devices", "bi-list-ul", 0),
        new NavEntry("Google TV", "/android/googletv", "bi-tv", 1),
        new NavEntry("Pixel", "/android/pixel", "bi-phone", 2)
    ];

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}
```

- [ ] **Step 4: Register IAdbService in Program.cs**

Add to `src/ControlMenu/Program.cs` after the `INetworkDiscoveryService` registration:
```csharp
using ControlMenu.Modules.AndroidDevices.Services;
// ...
builder.Services.AddSingleton<IAdbService, AdbService>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~AndroidDevicesModuleTests" -v n`
Expected: All 8 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs tests/ControlMenu.Tests/Modules/AndroidDevices/AndroidDevicesModuleTests.cs src/ControlMenu/Program.cs
git commit -m "feat(android): add AndroidDevicesModule with nav entries and dependencies"
```

---

## Task 3: Device Selector Page

**Files:**
- Create: `src/ControlMenu/Modules/AndroidDevices/Pages/DeviceSelector.razor`

- [ ] **Step 1: Create DeviceSelector page**

`src/ControlMenu/Modules/AndroidDevices/Pages/DeviceSelector.razor`:
```razor
@page "/android/devices"
@using ControlMenu.Data.Entities
@using ControlMenu.Data.Enums
@using ControlMenu.Services
@using ControlMenu.Modules.AndroidDevices.Services

<PageTitle>Android Devices</PageTitle>

<h1>Android Devices</h1>
<p class="page-subtitle">Select a device to manage.</p>

@if (_loading)
{
    <div class="loading-spinner">
        <i class="bi bi-arrow-repeat spin"></i> Loading devices...
    </div>
}
else if (_devices.Count == 0)
{
    <div class="empty-state">
        <i class="bi bi-phone"></i>
        <h2>No devices registered</h2>
        <p>Add Android devices in <a href="/settings">Settings > Device Management</a>.</p>
    </div>
}
else
{
    <div class="device-card-grid">
        @foreach (var device in _devices)
        {
            <div class="device-card @(device.LastSeen.HasValue && device.LastSeen > DateTime.UtcNow.AddMinutes(-5) ? "online" : "offline")">
                <div class="device-card-header">
                    <i class="bi @(device.Type == DeviceType.GoogleTV ? "bi-tv" : "bi-phone")"></i>
                    <h3>@device.Name</h3>
                </div>
                <div class="device-card-body">
                    <div class="device-info-row">
                        <span class="label">IP:</span>
                        <span>@(device.LastKnownIp ?? "Unknown")</span>
                    </div>
                    <div class="device-info-row">
                        <span class="label">MAC:</span>
                        <span>@device.MacAddress</span>
                    </div>
                    <div class="device-info-row">
                        <span class="label">Last Seen:</span>
                        <span>@(device.LastSeen?.ToLocalTime().ToString("g") ?? "Never")</span>
                    </div>
                </div>
                <div class="device-card-actions">
                    @if (device.Type == DeviceType.GoogleTV)
                    {
                        <a href="/android/googletv/@device.Id" class="btn btn-primary">
                            <i class="bi bi-gear"></i> Manage
                        </a>
                    }
                    else
                    {
                        <a href="/android/pixel/@device.Id" class="btn btn-primary">
                            <i class="bi bi-gear"></i> Manage
                        </a>
                    }
                </div>
            </div>
        }
    </div>
}

@code {
    [Inject] private IDeviceService DeviceService { get; set; } = default!;

    private List<Device> _devices = [];
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        var allDevices = await DeviceService.GetAllDevicesAsync();
        _devices = allDevices.Where(d => d.ModuleId == "android-devices").ToList();
        _loading = false;
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build src/ControlMenu`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Pages/DeviceSelector.razor
git commit -m "feat(android): add device selector page with card grid"
```

---

## Task 4: Google TV Dashboard

**Files:**
- Create: `src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor`
- Create: `src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor.css`

- [ ] **Step 1: Create the Google TV Dashboard page**

`src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor`:
```razor
@page "/android/googletv"
@page "/android/googletv/{DeviceId:guid}"
@using ControlMenu.Data.Entities
@using ControlMenu.Data.Enums
@using ControlMenu.Services
@using ControlMenu.Modules.AndroidDevices.Services

<PageTitle>Google TV - @(_device?.Name ?? "Select Device")</PageTitle>

<h1><i class="bi bi-tv"></i> Google TV</h1>

@if (_device is null)
{
    <p>No device selected. <a href="/android/devices">Select a device</a>.</p>
}
else
{
    <div class="device-header">
        <h2>@_device.Name</h2>
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

    <div class="action-card-grid">
        <!-- Power Status -->
        <div class="action-card">
            <div class="action-card-icon"><i class="bi bi-lightning-charge"></i></div>
            <h3>Power Status</h3>
            <p class="action-value">@_powerState</p>
            <button class="btn btn-secondary" @onclick="CheckPowerStatus" disabled="@_busy">
                <i class="bi bi-arrow-clockwise"></i> Check
            </button>
        </div>

        <!-- Power Toggle -->
        <div class="action-card">
            <div class="action-card-icon"><i class="bi bi-power"></i></div>
            <h3>Power On/Off</h3>
            <p>Toggle power state</p>
            <button class="btn btn-warning" @onclick="TogglePower" disabled="@_busy">
                <i class="bi bi-power"></i> Toggle
            </button>
        </div>

        <!-- Reboot -->
        <div class="action-card">
            <div class="action-card-icon"><i class="bi bi-arrow-repeat"></i></div>
            <h3>Reboot</h3>
            <p>Restart the device</p>
            <button class="btn btn-danger" @onclick="RebootDevice" disabled="@_busy">
                <i class="bi bi-arrow-repeat"></i> Reboot
            </button>
        </div>

        <!-- Screen Mirror -->
        <div class="action-card">
            <div class="action-card-icon"><i class="bi bi-display"></i></div>
            <h3>Screen Mirror</h3>
            <p>Launch scrcpy</p>
            <button class="btn btn-primary" @onclick="LaunchScreenMirror" disabled="@_busy">
                <i class="bi bi-cast"></i> Mirror
            </button>
        </div>

        <!-- Shizuku -->
        <div class="action-card">
            <div class="action-card-icon"><i class="bi bi-shield-check"></i></div>
            <h3>Shizuku</h3>
            <p>Start Shizuku service</p>
            <button class="btn btn-primary" @onclick="StartShizuku" disabled="@_busy">
                <i class="bi bi-play-circle"></i> Start
            </button>
        </div>

        <!-- Screensaver -->
        <div class="action-card">
            <div class="action-card-icon"><i class="bi bi-image"></i></div>
            <h3>Screensaver</h3>
            <p class="action-value">Current: @_screensaver</p>
            <div class="action-button-group">
                <button class="btn btn-secondary" @onclick="CheckScreensaver" disabled="@_busy">
                    <i class="bi bi-arrow-clockwise"></i> Refresh
                </button>
                <button class="btn btn-primary" @onclick="ToggleScreensaver" disabled="@_busy">
                    <i class="bi bi-arrow-left-right"></i> Switch
                </button>
            </div>
        </div>

        <!-- Screen Timeout -->
        <div class="action-card">
            <div class="action-card-icon"><i class="bi bi-clock"></i></div>
            <h3>Screen Timeout</h3>
            <p class="action-value">@FormatTimeout(_screenTimeout)</p>
            <div class="timeout-input-group">
                <input type="number" @bind="_newTimeout" min="300000" step="60000" placeholder="ms" class="form-input" />
                <button class="btn btn-primary" @onclick="SetTimeout" disabled="@_busy">Set</button>
            </div>
        </div>

        <!-- Android TV Launcher -->
        <div class="action-card">
            <div class="action-card-icon"><i class="bi bi-grid-3x3-gap"></i></div>
            <h3>TV Launcher</h3>
            <p class="action-value">@(_launcherDisabled == null ? "Unknown" : _launcherDisabled.Value ? "Disabled" : "Enabled")</p>
            <button class="btn btn-warning" @onclick="ToggleLauncher" disabled="@_busy">
                <i class="bi bi-toggle-on"></i> Toggle
            </button>
        </div>

        <!-- Projectivy Restore -->
        <div class="action-card wide">
            <div class="action-card-icon"><i class="bi bi-cloud-download"></i></div>
            <h3>Restore Projectivy</h3>
            @if (_projectivyBackups.Count == 0)
            {
                <p>No backups found.</p>
                <button class="btn btn-secondary" @onclick="LoadProjectivyBackups" disabled="@_busy">
                    <i class="bi bi-arrow-clockwise"></i> Scan
                </button>
            }
            else
            {
                <select @bind="_selectedBackup" class="form-select">
                    <option value="">Select a backup...</option>
                    @foreach (var backup in _projectivyBackups)
                    {
                        <option value="@backup">@backup</option>
                    }
                </select>
                <button class="btn btn-primary" @onclick="RestoreProjectivy" disabled="@(_busy || string.IsNullOrEmpty(_selectedBackup))">
                    <i class="bi bi-cloud-download"></i> Restore
                </button>
            }
        </div>
    </div>
}

@code {
    [Parameter] public Guid? DeviceId { get; set; }
    [Inject] private IDeviceService DeviceService { get; set; } = default!;
    [Inject] private IAdbService AdbService { get; set; } = default!;

    private Device? _device;
    private bool _connected;
    private bool _busy;
    private string _statusMessage = "";
    private string _statusClass = "";
    private string _statusIcon = "";
    private string _powerState = "Unknown";
    private string _screensaver = "Unknown";
    private int _screenTimeout;
    private int _newTimeout = 300000;
    private bool? _launcherDisabled;
    private List<string> _projectivyBackups = [];
    private string? _selectedBackup;

    protected override async Task OnInitializedAsync()
    {
        if (DeviceId.HasValue)
        {
            _device = await DeviceService.GetDeviceAsync(DeviceId.Value);
        }
        else
        {
            // Default to first Google TV device
            var devices = await DeviceService.GetAllDevicesAsync();
            _device = devices.FirstOrDefault(d => d.Type == DeviceType.GoogleTV && d.ModuleId == "android-devices");
        }

        if (_device?.LastKnownIp is not null)
        {
            await ConnectToDevice();
        }
    }

    private async Task ConnectToDevice()
    {
        if (_device?.LastKnownIp is null) return;
        _connected = await AdbService.ConnectAsync(_device.LastKnownIp, _device.AdbPort);
        if (_connected)
        {
            await CheckPowerStatus();
            await CheckScreensaver();
            _screenTimeout = await AdbService.GetScreenTimeoutAsync(_device.LastKnownIp, _device.AdbPort);
            _launcherDisabled = await AdbService.IsLauncherDisabledAsync(_device.LastKnownIp, _device.AdbPort);
        }
    }

    private string Ip => _device!.LastKnownIp!;
    private int Port => _device!.AdbPort;

    private async Task CheckPowerStatus()
    {
        _busy = true;
        var state = await AdbService.GetPowerStateAsync(Ip, Port);
        _powerState = state.ToString();
        _busy = false;
    }

    private async Task TogglePower()
    {
        _busy = true;
        await AdbService.TogglePowerAsync(Ip, Port);
        SetStatus("Power toggled", "status-success", "bi-check-circle");
        await Task.Delay(1000);
        await CheckPowerStatus();
        _busy = false;
    }

    private async Task RebootDevice()
    {
        _busy = true;
        SetStatus("Rebooting device...", "status-warning", "bi-hourglass-split");
        await AdbService.RebootAsync(Ip, Port);
        SetStatus("Reboot sent. Device will reconnect when ready.", "status-success", "bi-check-circle");
        _busy = false;
    }

    private async Task LaunchScreenMirror()
    {
        _busy = true;
        SetStatus("Launching screen mirror...", "status-info", "bi-cast");
        await AdbService.LaunchScrcpyAsync(Ip, Port);
        SetStatus("Screen mirror closed.", "status-info", "bi-info-circle");
        _busy = false;
    }

    private async Task StartShizuku()
    {
        _busy = true;
        await AdbService.StartShizukuAsync(Ip, Port);
        SetStatus("Shizuku started.", "status-success", "bi-check-circle");
        _busy = false;
    }

    private async Task CheckScreensaver()
    {
        _screensaver = await AdbService.GetScreensaverAsync(Ip, Port);
    }

    private async Task ToggleScreensaver()
    {
        _busy = true;
        var newSaver = _screensaver == "SkyFolio" ? "Google" : "SkyFolio";
        await AdbService.SetScreensaverAsync(Ip, Port, newSaver);
        _screensaver = newSaver;
        SetStatus($"Screensaver set to {newSaver}.", "status-success", "bi-check-circle");
        _busy = false;
    }

    private async Task SetTimeout()
    {
        if (_newTimeout < 300000) return;
        _busy = true;
        await AdbService.SetScreenTimeoutAsync(Ip, Port, _newTimeout);
        _screenTimeout = _newTimeout;
        SetStatus($"Screen timeout set to {FormatTimeout(_newTimeout)}.", "status-success", "bi-check-circle");
        _busy = false;
    }

    private async Task ToggleLauncher()
    {
        _busy = true;
        var enable = _launcherDisabled == true;
        await AdbService.SetLauncherEnabledAsync(Ip, Port, enable);
        _launcherDisabled = !enable;
        SetStatus($"TV Launcher {(enable ? "enabled" : "disabled")}.", "status-success", "bi-check-circle");
        _busy = false;
    }

    private async Task LoadProjectivyBackups()
    {
        _busy = true;
        _projectivyBackups = (await AdbService.ListProjectivyBackupsAsync(Ip, Port)).ToList();
        if (_projectivyBackups.Count == 0)
            SetStatus("No Projectivy backups found on device.", "status-warning", "bi-exclamation-triangle");
        _busy = false;
    }

    private async Task RestoreProjectivy()
    {
        if (string.IsNullOrEmpty(_selectedBackup)) return;
        _busy = true;
        await AdbService.RestoreProjectivyBackupAsync(Ip, Port, _selectedBackup);
        SetStatus($"Restore initiated for {_selectedBackup}. Confirm on device.", "status-success", "bi-check-circle");
        _busy = false;
    }

    private void SetStatus(string message, string cssClass, string icon)
    {
        _statusMessage = message;
        _statusClass = cssClass;
        _statusIcon = icon;
    }

    private static string FormatTimeout(int ms) =>
        ms switch
        {
            0 => "Unknown",
            _ => $"{ms / 60000} min ({ms:N0} ms)"
        };
}
```

- [ ] **Step 2: Create scoped CSS for the dashboard**

`src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor.css`:
```css
.device-header {
    display: flex;
    align-items: center;
    gap: 1rem;
    margin-bottom: 1.5rem;
}

.status-badge {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    padding: 0.25rem 0.75rem;
    border-radius: 1rem;
    font-size: 0.85rem;
    font-weight: 500;
}
.status-badge.connected { background: var(--success-bg, #d4edda); color: var(--success-text, #155724); }
.status-badge.disconnected { background: var(--danger-bg, #f8d7da); color: var(--danger-text, #721c24); }

.status-bar {
    padding: 0.75rem 1rem;
    border-radius: 0.5rem;
    margin-bottom: 1rem;
    display: flex;
    align-items: center;
    gap: 0.5rem;
}
.status-success { background: var(--success-bg, #d4edda); color: var(--success-text, #155724); }
.status-warning { background: var(--warning-bg, #fff3cd); color: var(--warning-text, #856404); }
.status-info { background: var(--info-bg, #d1ecf1); color: var(--info-text, #0c5460); }

.action-card-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
    gap: 1.25rem;
}

.action-card {
    background: var(--card-bg, #fff);
    border: 1px solid var(--border-color, #dee2e6);
    border-radius: 0.75rem;
    padding: 1.25rem;
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
}
.action-card.wide { grid-column: span 2; }

.action-card-icon {
    font-size: 1.5rem;
    color: var(--accent-color, #0d6efd);
}

.action-card h3 {
    margin: 0;
    font-size: 1rem;
    font-weight: 600;
}

.action-value {
    font-weight: 500;
    color: var(--text-muted, #6c757d);
}

.action-button-group {
    display: flex;
    gap: 0.5rem;
}

.timeout-input-group {
    display: flex;
    gap: 0.5rem;
    align-items: center;
}

.timeout-input-group input {
    max-width: 140px;
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build src/ControlMenu`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor src/ControlMenu/Modules/AndroidDevices/Pages/GoogleTvDashboard.razor.css
git commit -m "feat(android): add Google TV dashboard with all action cards"
```

---

## Task 5: Pixel Dashboard

**Files:**
- Create: `src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor`
- Create: `src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor.css`

- [ ] **Step 1: Create the Pixel Dashboard page**

`src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor`:
```razor
@page "/android/pixel"
@page "/android/pixel/{DeviceId:guid}"
@using ControlMenu.Data.Entities
@using ControlMenu.Data.Enums
@using ControlMenu.Services
@using ControlMenu.Modules.AndroidDevices.Services

<PageTitle>Pixel - @(_device?.Name ?? "Select Device")</PageTitle>

<h1><i class="bi bi-phone"></i> Pixel</h1>

@if (_device is null)
{
    <p>No device selected. <a href="/android/devices">Select a device</a>.</p>
}
else
{
    <div class="device-header">
        <h2>@_device.Name</h2>
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

    <div class="action-card-grid">
        <!-- Reset ADB Port -->
        <div class="action-card">
            <div class="action-card-icon"><i class="bi bi-usb-plug"></i></div>
            <h3>Reset ADB Port</h3>
            <p>Reset TCP/IP port to 5555 after reboot. Requires USB connection.</p>
            <button class="btn btn-warning" @onclick="ResetAdbPort" disabled="@_busy">
                <i class="bi bi-arrow-counterclockwise"></i> Reset Port
            </button>
        </div>

        <!-- Connect -->
        <div class="action-card">
            <div class="action-card-icon"><i class="bi bi-plug"></i></div>
            <h3>ADB Connect</h3>
            <p>Connect to device over WiFi</p>
            <button class="btn btn-primary" @onclick="ConnectDevice" disabled="@_busy">
                <i class="bi bi-wifi"></i> Connect
            </button>
        </div>

        <!-- Screen Mirror -->
        <div class="action-card">
            <div class="action-card-icon"><i class="bi bi-display"></i></div>
            <h3>Screen Mirror</h3>
            <p>Launch scrcpy for screen mirroring</p>
            <button class="btn btn-primary" @onclick="LaunchScreenMirror" disabled="@_busy">
                <i class="bi bi-cast"></i> Mirror
            </button>
        </div>
    </div>
}

@code {
    [Parameter] public Guid? DeviceId { get; set; }
    [Inject] private IDeviceService DeviceService { get; set; } = default!;
    [Inject] private IAdbService AdbService { get; set; } = default!;

    private Device? _device;
    private bool _connected;
    private bool _busy;
    private string _statusMessage = "";
    private string _statusClass = "";
    private string _statusIcon = "";

    protected override async Task OnInitializedAsync()
    {
        if (DeviceId.HasValue)
        {
            _device = await DeviceService.GetDeviceAsync(DeviceId.Value);
        }
        else
        {
            var devices = await DeviceService.GetAllDevicesAsync();
            _device = devices.FirstOrDefault(d => d.Type == DeviceType.AndroidPhone && d.ModuleId == "android-devices");
        }

        if (_device?.LastKnownIp is not null)
        {
            _connected = await AdbService.ConnectAsync(_device.LastKnownIp, _device.AdbPort);
        }
    }

    private string Ip => _device!.LastKnownIp!;
    private int Port => _device!.AdbPort;

    private async Task ResetAdbPort()
    {
        _busy = true;
        SetStatus("Connect device via USB, then this will reset TCP port to 5555...", "status-warning", "bi-usb-plug");
        await AdbService.ResetTcpPortAsync(5555);
        SetStatus("ADB port reset to 5555. You can disconnect USB now.", "status-success", "bi-check-circle");
        _busy = false;
    }

    private async Task ConnectDevice()
    {
        if (_device?.LastKnownIp is null)
        {
            SetStatus("No IP address known for this device. Run a network scan in Settings.", "status-warning", "bi-exclamation-triangle");
            return;
        }
        _busy = true;
        _connected = await AdbService.ConnectAsync(Ip, Port);
        SetStatus(_connected ? "Connected successfully." : "Connection failed.", _connected ? "status-success" : "status-warning", _connected ? "bi-check-circle" : "bi-x-circle");
        _busy = false;
    }

    private async Task LaunchScreenMirror()
    {
        if (!_connected)
        {
            await ConnectDevice();
            if (!_connected) return;
        }
        _busy = true;
        SetStatus("Launching screen mirror...", "status-info", "bi-cast");
        await AdbService.LaunchScrcpyAsync(Ip, Port);
        SetStatus("Screen mirror closed.", "status-info", "bi-info-circle");
        _busy = false;
    }

    private void SetStatus(string message, string cssClass, string icon)
    {
        _statusMessage = message;
        _statusClass = cssClass;
        _statusIcon = icon;
    }
}
```

- [ ] **Step 2: Create scoped CSS**

`src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor.css`:
```css
.device-header {
    display: flex;
    align-items: center;
    gap: 1rem;
    margin-bottom: 1.5rem;
}

.status-badge {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    padding: 0.25rem 0.75rem;
    border-radius: 1rem;
    font-size: 0.85rem;
    font-weight: 500;
}
.status-badge.connected { background: var(--success-bg, #d4edda); color: var(--success-text, #155724); }
.status-badge.disconnected { background: var(--danger-bg, #f8d7da); color: var(--danger-text, #721c24); }

.status-bar {
    padding: 0.75rem 1rem;
    border-radius: 0.5rem;
    margin-bottom: 1rem;
    display: flex;
    align-items: center;
    gap: 0.5rem;
}
.status-success { background: var(--success-bg, #d4edda); color: var(--success-text, #155724); }
.status-warning { background: var(--warning-bg, #fff3cd); color: var(--warning-text, #856404); }
.status-info { background: var(--info-bg, #d1ecf1); color: var(--info-text, #0c5460); }

.action-card-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
    gap: 1.25rem;
}

.action-card {
    background: var(--card-bg, #fff);
    border: 1px solid var(--border-color, #dee2e6);
    border-radius: 0.75rem;
    padding: 1.25rem;
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
}

.action-card-icon {
    font-size: 1.5rem;
    color: var(--accent-color, #0d6efd);
}

.action-card h3 {
    margin: 0;
    font-size: 1rem;
    font-weight: 600;
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build src/ControlMenu`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor src/ControlMenu/Modules/AndroidDevices/Pages/PixelDashboard.razor.css
git commit -m "feat(android): add Pixel dashboard with connect, reset port, and screen mirror"
```

---

## Task 6: Dashboard CSS in app.css & Full Test Suite Run

**Files:**
- Modify: `src/ControlMenu/wwwroot/css/app.css`

- [ ] **Step 1: Add shared dashboard styles to app.css**

Append to `src/ControlMenu/wwwroot/css/app.css`:
```css
/* ===== Dashboard shared styles ===== */
.page-subtitle {
    color: var(--text-muted, #6c757d);
    margin-bottom: 1.5rem;
}

.device-card-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
    gap: 1.25rem;
}

.device-card {
    background: var(--card-bg, #fff);
    border: 1px solid var(--border-color, #dee2e6);
    border-radius: 0.75rem;
    padding: 1.25rem;
    transition: box-shadow 0.2s;
}
.device-card:hover { box-shadow: 0 2px 8px rgba(0,0,0,0.1); }

.device-card.online { border-left: 4px solid var(--success-color, #28a745); }
.device-card.offline { border-left: 4px solid var(--danger-color, #dc3545); }

.device-card-header {
    display: flex;
    align-items: center;
    gap: 0.75rem;
    margin-bottom: 0.75rem;
}
.device-card-header i { font-size: 1.5rem; color: var(--accent-color, #0d6efd); }
.device-card-header h3 { margin: 0; font-size: 1.1rem; }

.device-card-body { margin-bottom: 0.75rem; }

.device-info-row {
    display: flex;
    justify-content: space-between;
    padding: 0.25rem 0;
    font-size: 0.9rem;
}
.device-info-row .label { color: var(--text-muted, #6c757d); }

.device-card-actions { display: flex; gap: 0.5rem; }

.empty-state {
    text-align: center;
    padding: 3rem 1rem;
    color: var(--text-muted, #6c757d);
}
.empty-state i { font-size: 3rem; display: block; margin-bottom: 1rem; }

.loading-spinner {
    text-align: center;
    padding: 2rem;
    color: var(--text-muted, #6c757d);
}
.spin { animation: spin 1s linear infinite; }
@keyframes spin { 100% { transform: rotate(360deg); } }

.form-select {
    width: 100%;
    padding: 0.375rem 0.75rem;
    border: 1px solid var(--border-color, #dee2e6);
    border-radius: 0.375rem;
    background: var(--input-bg, #fff);
    color: var(--text-color, #212529);
    margin-bottom: 0.5rem;
}
```

- [ ] **Step 2: Run full test suite**

Run: `dotnet test tests/ControlMenu.Tests -v n`
Expected: All tests pass (previous 44 + 25 new = 69 total).

- [ ] **Step 3: Build and verify the whole project compiles**

Run: `dotnet build src/ControlMenu`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/wwwroot/css/app.css
git commit -m "feat(android): add shared dashboard CSS styles"
```
