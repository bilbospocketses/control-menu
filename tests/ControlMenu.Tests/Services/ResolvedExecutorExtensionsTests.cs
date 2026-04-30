using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Services;

public class ResolvedExecutorExtensionsTests
{
    [Fact]
    public async Task ExecuteResolvedAsync_PassesResolvedPathToExecutor()
    {
        var executor = new Mock<ICommandExecutor>();
        var resolver = new Mock<IDependencyPathResolver>();
        resolver.Setup(r => r.ResolveAsync("android-devices", "adb", It.IsAny<CancellationToken>()))
                .ReturnsAsync("C:/cm/dependencies/platform-tools/adb.exe");
        executor.Setup(e => e.ExecuteAsync("C:/cm/dependencies/platform-tools/adb.exe",
                                            "devices", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CommandResult(0, "List of devices attached", "", false));

        var result = await executor.Object.ExecuteResolvedAsync(
            resolver.Object, "android-devices", "adb", "devices");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("List of devices", result.StandardOutput);
    }

    [Fact]
    public async Task ExecuteResolvedAsync_PropagatesNotInstalledException()
    {
        var executor = new Mock<ICommandExecutor>();
        var resolver = new Mock<IDependencyPathResolver>();
        resolver.Setup(r => r.ResolveAsync("android-devices", "adb", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DependencyNotInstalledException("android-devices", "adb", "/missing"));

        await Assert.ThrowsAsync<DependencyNotInstalledException>(() =>
            executor.Object.ExecuteResolvedAsync(resolver.Object, "android-devices", "adb", "devices"));
    }
}
