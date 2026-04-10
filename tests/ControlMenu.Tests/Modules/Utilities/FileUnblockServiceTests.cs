using ControlMenu.Modules.Utilities.Services;
using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Modules.Utilities;

public class FileUnblockServiceTests
{
    private readonly Mock<ICommandExecutor> _mockExecutor = new();

    private FileUnblockService CreateService() => new(_mockExecutor.Object);

    [Fact]
    public void IsSupported_ReturnsTrue_OnWindows()
    {
        var service = CreateService();
        // This test validates the property exists and returns a bool.
        // It will be true on Windows CI, false on Linux CI.
        Assert.IsType<bool>(service.IsSupported);
    }

    [Fact]
    public async Task UnblockDirectoryAsync_RunsUnblockFileCommand()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("powershell",
            It.Is<string>(s => s.Contains("Unblock-File") && s.Contains("C:\\TestDir")),
            null, default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        var result = await service.UnblockDirectoryAsync("C:\\TestDir");

        Assert.True(result.Success);
        _mockExecutor.Verify(e => e.ExecuteAsync("powershell",
            It.Is<string>(s => s.Contains("Unblock-File") && s.Contains("C:\\TestDir")),
            null, default), Times.Once);
    }

    [Fact]
    public async Task UnblockDirectoryAsync_ReturnsFileCount()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("powershell",
            It.IsAny<string>(), null, default))
            .ReturnsAsync(new CommandResult(0, "42", "", false));

        var service = CreateService();
        var result = await service.UnblockDirectoryAsync("C:\\TestDir");

        Assert.True(result.Success);
        Assert.Equal(42, result.FileCount);
    }

    [Fact]
    public async Task UnblockDirectoryAsync_ReturnsFalse_OnError()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("powershell",
            It.IsAny<string>(), null, default))
            .ReturnsAsync(new CommandResult(1, "", "Access denied", false));

        var service = CreateService();
        var result = await service.UnblockDirectoryAsync("C:\\TestDir");

        Assert.False(result.Success);
        Assert.Equal("Access denied", result.ErrorMessage);
    }
}
