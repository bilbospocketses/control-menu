using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Modules.AndroidDevices;

public class AdbServiceTests
{
    private readonly Mock<ICommandExecutor> _mockExecutor = new();
    private readonly Mock<IDependencyPathResolver> _mockResolver = new();

    public AdbServiceTests()
    {
        _mockResolver.Setup(r => r.ResolveAsync("android-devices", "adb", It.IsAny<CancellationToken>()))
                     .ReturnsAsync("adb");
    }

    private AdbService CreateService() => new(_mockExecutor.Object, _mockResolver.Object);

    // Matches the structured ArgumentList overload against an exact token sequence.
    private static IReadOnlyList<string> Argv(params string[] tokens) =>
        It.Is<IReadOnlyList<string>>(a => a.SequenceEqual(tokens));

    [Fact]
    public async Task ConnectAsync_ReturnsTrue_WhenAdbConnectsSuccessfully()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", Argv("connect", "192.168.1.100:5555"), null, default))
            .ReturnsAsync(new CommandResult(0, "connected to 192.168.1.100:5555", "", false));

        var service = CreateService();
        var result = await service.ConnectAsync("192.168.1.100", 5555);

        Assert.True(result);
    }

    [Fact]
    public async Task ConnectAsync_ReturnsFalse_WhenAdbFails()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", Argv("connect", "192.168.1.100:5555"), null, default))
            .ReturnsAsync(new CommandResult(1, "", "cannot connect", false));

        var service = CreateService();
        var result = await service.ConnectAsync("192.168.1.100", 5555);

        Assert.False(result);
    }

    [Fact]
    public async Task DisconnectAsync_CallsAdbDisconnect()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", Argv("disconnect", "192.168.1.100:5555"), null, default))
            .ReturnsAsync(new CommandResult(0, "disconnected", "", false));

        var service = CreateService();
        await service.DisconnectAsync("192.168.1.100", 5555);

        _mockExecutor.Verify(e => e.ExecuteAsync("adb", Argv("disconnect", "192.168.1.100:5555"), null, default), Times.Once);
    }

    [Fact]
    public async Task GetPowerStateAsync_ReturnsAwake_WhenDeviceIsOn()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", Argv("-s", "192.168.1.100:5555", "shell", "dumpsys", "power"), null, default))
            .ReturnsAsync(new CommandResult(0, "mWakefulness=Awake\nmwakefulness=awake", "", false));

        var service = CreateService();
        var result = await service.GetPowerStateAsync("192.168.1.100", 5555);

        Assert.Equal(PowerState.Awake, result);
    }

    [Fact]
    public async Task GetPowerStateAsync_ReturnsAsleep_WhenDeviceIsOff()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", Argv("-s", "192.168.1.100:5555", "shell", "dumpsys", "power"), null, default))
            .ReturnsAsync(new CommandResult(0, "mWakefulness=Asleep", "", false));

        var service = CreateService();
        var result = await service.GetPowerStateAsync("192.168.1.100", 5555);

        Assert.Equal(PowerState.Asleep, result);
    }

    [Fact]
    public async Task RebootAsync_CallsAdbReboot()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", Argv("-s", "192.168.1.100:5555", "shell", "reboot"), null, default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        await service.RebootAsync("192.168.1.100", 5555);

        _mockExecutor.Verify(e => e.ExecuteAsync("adb", Argv("-s", "192.168.1.100:5555", "shell", "reboot"), null, default), Times.Once);
    }

    [Fact]
    public async Task TogglePowerAsync_SendsKeyEvent()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", Argv("-s", "192.168.1.100:5555", "shell", "input", "keyevent", "KEYCODE_POWER"), null, default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        await service.TogglePowerAsync("192.168.1.100", 5555);

        _mockExecutor.Verify(e => e.ExecuteAsync("adb", Argv("-s", "192.168.1.100:5555", "shell", "input", "keyevent", "KEYCODE_POWER"), null, default), Times.Once);
    }

    [Fact]
    public async Task GetScreensaverAsync_ReturnsSkyFolio_WhenSet()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", Argv("-s", "192.168.1.100:5555", "shell", "settings", "get", "secure", "screensaver_components"), null, default))
            .ReturnsAsync(new CommandResult(0, "com.snapwood.skyfolio/com.snapwood.skyfolio.DreamService", "", false));

        var service = CreateService();
        var result = await service.GetScreensaverAsync("192.168.1.100", 5555);

        Assert.Equal("SkyFolio", result);
    }

    [Fact]
    public async Task GetScreensaverAsync_ReturnsGoogle_WhenBackdropSet()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", Argv("-s", "192.168.1.100:5555", "shell", "settings", "get", "secure", "screensaver_components"), null, default))
            .ReturnsAsync(new CommandResult(0, "com.google.android.apps.tv.dreamx/.service.Backdrop", "", false));

        var service = CreateService();
        var result = await service.GetScreensaverAsync("192.168.1.100", 5555);

        Assert.Equal("Google", result);
    }

    [Fact]
    public async Task SetScreensaverAsync_SetsSkyFolioComponent()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb",
            Argv("-s", "192.168.1.100:5555", "shell", "settings", "put", "secure", "screensaver_components", "com.snapwood.skyfolio/com.snapwood.skyfolio.DreamService"),
            null, default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        await service.SetScreensaverAsync("192.168.1.100", 5555, "SkyFolio");

        _mockExecutor.Verify(e => e.ExecuteAsync("adb",
            Argv("-s", "192.168.1.100:5555", "shell", "settings", "put", "secure", "screensaver_components", "com.snapwood.skyfolio/com.snapwood.skyfolio.DreamService"),
            null, default), Times.Once);
    }

    [Fact]
    public async Task GetScreenTimeoutAsync_ReturnsMilliseconds()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", Argv("-s", "192.168.1.100:5555", "shell", "settings", "get", "system", "screen_off_timeout"), null, default))
            .ReturnsAsync(new CommandResult(0, "300000", "", false));

        var service = CreateService();
        var result = await service.GetScreenTimeoutAsync("192.168.1.100", 5555);

        Assert.Equal(300000, result);
    }

    [Fact]
    public async Task IsLauncherDisabledAsync_ReturnsTrue_WhenPackageDisabled()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", Argv("-s", "192.168.1.100:5555", "shell", "pm", "list", "packages", "-d"), null, default))
            .ReturnsAsync(new CommandResult(0, "package:com.google.android.apps.tv.launcherx\npackage:com.other.app", "", false));

        var service = CreateService();
        var result = await service.IsLauncherDisabledAsync("192.168.1.100", 5555);

        Assert.True(result);
    }

    [Fact]
    public async Task StartShizukuAsync_RunsStartScript()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb",
            Argv("-s", "192.168.1.100:5555", "shell", "sh", "/storage/emulated/0/Android/data/moe.shizuku.privileged.api/start.sh"),
            null, default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        await service.StartShizukuAsync("192.168.1.100", 5555);

        _mockExecutor.Verify(e => e.ExecuteAsync("adb",
            Argv("-s", "192.168.1.100:5555", "shell", "sh", "/storage/emulated/0/Android/data/moe.shizuku.privileged.api/start.sh"),
            null, default), Times.Once);
    }

    [Fact]
    public async Task ListProjectivyBackupsAsync_ReturnsFileList()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb",
            Argv("-s", "192.168.1.100:5555", "shell", "ls", "/storage/emulated/0/Projectivy-Backups"),
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
            Argv("-s", "192.168.1.100:5555", "shell", "am", "start", "-a", "android.intent.action.VIEW",
                 "-d", $"file:///storage/emulated/0/Projectivy-Backups/{filename}",
                 "-n", "com.spocky.projengmenu/.ui.launcherActivities.ImportSettingsActivity"),
            null, default))
            .ReturnsAsync(new CommandResult(0, "Starting: Intent", "", false));

        var service = CreateService();
        await service.RestoreProjectivyBackupAsync("192.168.1.100", 5555, filename);

        _mockExecutor.Verify(e => e.ExecuteAsync("adb",
            Argv("-s", "192.168.1.100:5555", "shell", "am", "start", "-a", "android.intent.action.VIEW",
                 "-d", $"file:///storage/emulated/0/Projectivy-Backups/{filename}",
                 "-n", "com.spocky.projengmenu/.ui.launcherActivities.ImportSettingsActivity"),
            null, default), Times.Once);
    }

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("a b.json")]
    [InlineData("x\".json")]
    [InlineData("x;reboot")]
    public async Task RestoreProjectivyBackupAsync_RejectsMaliciousFilename(string filename)
    {
        var service = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RestoreProjectivyBackupAsync("192.168.1.100", 5555, filename));
        // Nothing must be sent to adb for a rejected filename.
        _mockExecutor.Verify(e => e.ExecuteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnlockWithPinAsync_SendsPinAsSingleTextToken()
    {
        var captured = new List<IReadOnlyList<string>>();
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", It.IsAny<IReadOnlyList<string>>(), null, default))
            .Callback<string, IReadOnlyList<string>, string?, CancellationToken>((_, a, _, _) => captured.Add(a))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        await service.UnlockWithPinAsync("192.168.1.100", 5555, "1234");

        // The PIN reaches adb as one discrete token, never concatenated into the arg string.
        var textCall = captured.Single(a => a.Contains("text"));
        Assert.Equal(["-s", "192.168.1.100:5555", "shell", "input", "text", "1234"], textCall);
    }

    [Theory]
    [InlineData("12 34")]
    [InlineData("1234;reboot")]
    [InlineData("")]
    public async Task UnlockWithPinAsync_RejectsNonNumericPin(string pin)
    {
        var service = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UnlockWithPinAsync("192.168.1.100", 5555, pin));
    }

    [Fact]
    public async Task GetConnectedDevicesAsync_ParsesAdbDevices()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", Argv("devices"), null, default))
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
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", Argv("devices"), null, default))
            .ReturnsAsync(new CommandResult(0, "List of devices attached\n192.168.1.100:5555\tdevice\n", "", false));
        _mockExecutor.Setup(e => e.ExecuteAsync("adb", Argv("disconnect", "192.168.1.100:5555"), null, default))
            .ReturnsAsync(new CommandResult(0, "disconnected", "", false));

        var service = CreateService();
        await service.DisconnectAllAsync();

        _mockExecutor.Verify(e => e.ExecuteAsync("adb", Argv("disconnect", "192.168.1.100:5555"), null, default), Times.Once);
    }

    [Fact]
    public async Task AdbService_ResolvesViaDependencyPathResolver_NotBareName()
    {
        var localResolver = new Mock<IDependencyPathResolver>();
        localResolver.Setup(r => r.ResolveAsync("android-devices", "adb", It.IsAny<CancellationToken>()))
                     .ReturnsAsync("/cm/local/adb.exe");
        var localExecutor = new Mock<ICommandExecutor>();
        localExecutor.Setup(e => e.ExecuteAsync("/cm/local/adb.exe", It.IsAny<IReadOnlyList<string>>(), null, default))
                     .ReturnsAsync(new CommandResult(0, "List of devices attached", "", false));

        var service = new AdbService(localExecutor.Object, localResolver.Object);
        await service.GetConnectedDevicesAsync();

        localExecutor.Verify(
            e => e.ExecuteAsync("adb", It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "AdbService must NOT call the executor with bare 'adb' — local-deps rule.");
        localExecutor.Verify(
            e => e.ExecuteAsync("/cm/local/adb.exe", Argv("devices"), null, default),
            Times.Once);
    }
}
