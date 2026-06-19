using ControlMenu.Modules.Cameras.Migrations;
using ControlMenu.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Modules.Cameras;

public class PurgeLegacyCameraSettingsMigrationTests
{
    private readonly Mock<IConfigurationService> _config = new();

    [Fact]
    public async Task RunAsync_DeletesAllLegacyKeysInOneSetBasedCall_AndSetsMarker()
    {
        _config.Setup(c => c.GetSettingAsync("cameras-legacy-purge-completed", "cameras"))
            .ReturnsAsync((string?)null);

        IReadOnlyCollection<string>? deletedKeys = null;
        _config.Setup(c => c.DeleteSettingsAsync(It.IsAny<IReadOnlyCollection<string>>(), "cameras"))
            .Callback<IReadOnlyCollection<string>, string?>((keys, _) => deletedKeys = keys)
            .Returns(Task.CompletedTask);

        var sut = new PurgeLegacyCameraSettingsMigration(_config.Object, NullLogger<PurgeLegacyCameraSettingsMigration>.Instance);
        await sut.RunAsync();

        // One set-based delete, never the per-key path (the whole point of #27).
        _config.Verify(c => c.DeleteSettingsAsync(It.IsAny<IReadOnlyCollection<string>>(), "cameras"), Times.Once);
        _config.Verify(c => c.DeleteSettingAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);

        Assert.NotNull(deletedKeys);
        for (int i = 1; i <= 16; i++)
        {
            Assert.Contains($"camera-{i}-name", deletedKeys!);
            Assert.Contains($"camera-{i}-ip", deletedKeys!);
            Assert.Contains($"camera-{i}-port", deletedKeys!);
            Assert.Contains($"camera-{i}-username", deletedKeys!);
            Assert.Contains($"camera-{i}-password", deletedKeys!);
        }
        Assert.Contains("camera-count", deletedKeys!);
        Assert.Equal(81, deletedKeys!.Count); // 16 indices * 5 fields + camera-count

        _config.Verify(c => c.SetSettingAsync("cameras-legacy-purge-completed", "true", "cameras"), Times.Once);
    }

    [Fact]
    public async Task RunAsync_SkipsIfMarkerSet()
    {
        _config.Setup(c => c.GetSettingAsync("cameras-legacy-purge-completed", "cameras"))
            .ReturnsAsync("true");

        var sut = new PurgeLegacyCameraSettingsMigration(_config.Object, NullLogger<PurgeLegacyCameraSettingsMigration>.Instance);
        await sut.RunAsync();

        _config.Verify(c => c.DeleteSettingsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string?>()), Times.Never);
        _config.Verify(c => c.DeleteSettingAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        _config.Verify(c => c.SetSettingAsync("cameras-legacy-purge-completed", It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }
}
