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

}
