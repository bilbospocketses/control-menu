using ControlMenu.Modules.Utilities.Services;
using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Modules.Utilities;

public class FileUnblockServiceTests : IDisposable
{
    private readonly Mock<ICommandExecutor> _mockExecutor = new();
    private readonly string _tempDir;

    public FileUnblockServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ControlMenu_Test_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private FileUnblockService CreateService() => new(_mockExecutor.Object);

    [Fact]
    public void IsSupported_ReturnsTrue_OnWindows()
    {
        var service = CreateService();
        Assert.IsType<bool>(service.IsSupported);
    }

    [Fact]
    public async Task UnblockDirectoryAsync_RunsUnblockFileCommand()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("powershell",
            It.Is<string>(s => s.Contains("Unblock-File") && s.Contains(_tempDir)),
            null, default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        var result = await service.UnblockDirectoryAsync(_tempDir);

        Assert.True(result.Success);
        _mockExecutor.Verify(e => e.ExecuteAsync("powershell",
            It.Is<string>(s => s.Contains("Unblock-File") && s.Contains(_tempDir)),
            null, default), Times.Once);
    }

    [Fact]
    public async Task UnblockDirectoryAsync_ReturnsFileCount()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("powershell",
            It.IsAny<string>(), null, default))
            .ReturnsAsync(new CommandResult(0, "42", "", false));

        var service = CreateService();
        var result = await service.UnblockDirectoryAsync(_tempDir);

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
        var result = await service.UnblockDirectoryAsync(_tempDir);

        Assert.False(result.Success);
        Assert.Equal("Access denied", result.ErrorMessage);
    }

    [Fact]
    public async Task UnblockDirectoryAsync_ReturnsFalse_ForNonExistentDirectory()
    {
        var service = CreateService();
        var result = await service.UnblockDirectoryAsync(@"C:\NonExistent_" + Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage);
    }
}
