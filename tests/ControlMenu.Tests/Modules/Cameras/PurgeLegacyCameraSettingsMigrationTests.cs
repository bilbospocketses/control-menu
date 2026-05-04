using ControlMenu.Modules.Cameras.Migrations;
using ControlMenu.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Modules.Cameras;

public class PurgeLegacyCameraSettingsMigrationTests
{
    private readonly Mock<IConfigurationService> _config = new();

    [Fact]
    public async Task RunAsync_DeletesAllLegacyKeys_AndSetsMarker()
    {
        _config.Setup(c => c.GetSettingAsync("cameras-legacy-purge-completed", "cameras"))
            .ReturnsAsync((string?)null);

        var sut = new PurgeLegacyCameraSettingsMigration(_config.Object, NullLogger<PurgeLegacyCameraSettingsMigration>.Instance);
        await sut.RunAsync();

        for (int i = 1; i <= 16; i++)
        {
            _config.Verify(c => c.DeleteSettingAsync($"camera-{i}-name", "cameras"), Times.Once);
            _config.Verify(c => c.DeleteSettingAsync($"camera-{i}-ip", "cameras"), Times.Once);
            _config.Verify(c => c.DeleteSettingAsync($"camera-{i}-port", "cameras"), Times.Once);
            _config.Verify(c => c.DeleteSettingAsync($"camera-{i}-username", "cameras"), Times.Once);
            _config.Verify(c => c.DeleteSettingAsync($"camera-{i}-password", "cameras"), Times.Once);
        }
        _config.Verify(c => c.DeleteSettingAsync("camera-count", "cameras"), Times.Once);
        _config.Verify(c => c.SetSettingAsync("cameras-legacy-purge-completed", "true", "cameras"), Times.Once);
    }

    [Fact]
    public async Task RunAsync_SkipsIfMarkerSet()
    {
        _config.Setup(c => c.GetSettingAsync("cameras-legacy-purge-completed", "cameras"))
            .ReturnsAsync("true");

        var sut = new PurgeLegacyCameraSettingsMigration(_config.Object, NullLogger<PurgeLegacyCameraSettingsMigration>.Instance);
        await sut.RunAsync();

        _config.Verify(c => c.DeleteSettingAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _config.Verify(c => c.SetSettingAsync("cameras-legacy-purge-completed", It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
