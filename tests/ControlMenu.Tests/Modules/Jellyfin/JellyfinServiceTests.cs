using ControlMenu.Modules.Jellyfin.Services;
using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Modules.Jellyfin;

public class JellyfinServiceTests
{
    private readonly Mock<ICommandExecutor> _mockExecutor = new();
    private readonly Mock<IConfigurationService> _mockConfig = new();
    private readonly Mock<IHttpClientFactory> _mockHttpFactory = new();

    private JellyfinService CreateService() => new(_mockExecutor.Object, _mockConfig.Object, _mockHttpFactory.Object);

    [Fact]
    public async Task GetContainerIdAsync_ParsesDockerPsOutput()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("jellyfin-container-name", null))
            .ReturnsAsync("jellyfin");
        _mockExecutor.Setup(e => e.ExecuteAsync("docker", "ps -a --filter name=^/jellyfin$ --format {{.ID}}", null, default))
            .ReturnsAsync(new CommandResult(0, "a1b2c3d4e5f6\n", "", false));

        var service = CreateService();
        var id = await service.GetContainerIdAsync();

        Assert.Equal("a1b2c3d4e5f6", id);
    }

    [Fact]
    public async Task GetContainerIdAsync_ReturnsNull_WhenNoContainer()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("jellyfin-container-name", null))
            .ReturnsAsync("jellyfin");
        _mockExecutor.Setup(e => e.ExecuteAsync("docker", "ps -a --filter name=^/jellyfin$ --format {{.ID}}", null, default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        var id = await service.GetContainerIdAsync();

        Assert.Null(id);
    }

    [Fact]
    public async Task StopContainerAsync_StopsWithGracePeriod()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("docker", "stop -t=15 abc123", null, default))
            .ReturnsAsync(new CommandResult(0, "abc123", "", false));

        var service = CreateService();
        var result = await service.StopContainerAsync("abc123");

        Assert.True(result);
    }

    [Fact]
    public async Task StartContainerAsync_StartsContainer()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("docker", "start abc123", null, default))
            .ReturnsAsync(new CommandResult(0, "abc123", "", false));

        var service = CreateService();
        var result = await service.StartContainerAsync("abc123");

        Assert.True(result);
    }

    [Fact]
    public async Task BackupDatabaseAsync_CopiesFile()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("jellyfin-db-path", null))
            .ReturnsAsync("D:/DockerData/jellyfin/config/data/jellyfin.db");
        _mockConfig.Setup(c => c.GetSettingAsync("jellyfin-backup-dir", null))
            .ReturnsAsync("C:/scripts/tools-menu/jellyfin-db-bkup-and-logs");

        // The command differs by OS, but we test the Windows path here
        _mockExecutor.Setup(e => e.ExecuteAsync(
            It.IsAny<CommandDefinition>(), default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        var backupPath = await service.BackupDatabaseAsync();

        Assert.NotNull(backupPath);
        Assert.Contains("jellyfin_", backupPath);
    }

    [Fact]
    public async Task UpdateDateCreatedAsync_RunsSqlUpdate()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("jellyfin-db-path", null))
            .ReturnsAsync("D:/DockerData/jellyfin/config/data/jellyfin.db");

        _mockExecutor.Setup(e => e.ExecuteAsync("sqlite3",
            It.Is<string>(s => s.Contains("UPDATE BaseItems SET DateCreated=PremiereDate")),
            null, default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        var result = await service.UpdateDateCreatedAsync();

        Assert.True(result);
    }

    [Fact]
    public async Task CleanupOldBackupsAsync_RemovesOldFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ControlMenu_Test_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            // Create an old backup file (10 days old)
            var oldFile = Path.Combine(tempDir, "jellyfin_old.db");
            File.WriteAllText(oldFile, "old");
            File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-10));

            // Create a recent backup file
            var newFile = Path.Combine(tempDir, "jellyfin_new.db");
            File.WriteAllText(newFile, "new");

            _mockConfig.Setup(c => c.GetSettingAsync("jellyfin-backup-dir", null))
                .ReturnsAsync(tempDir);
            _mockConfig.Setup(c => c.GetSettingAsync("jellyfin-backup-retention-days", null))
                .ReturnsAsync("5");

            var service = CreateService();
            await service.CleanupOldBackupsAsync();

            Assert.False(File.Exists(oldFile));
            Assert.True(File.Exists(newFile));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
