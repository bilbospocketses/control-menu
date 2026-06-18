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

    [Fact]
    public async Task ExecuteResolvedAsync_ArgumentList_PassesResolvedPathAndArgsVerbatim()
    {
        var executor = new Mock<ICommandExecutor>();
        var resolver = new Mock<IDependencyPathResolver>();
        resolver.Setup(r => r.ResolveAsync("jellyfin", "sqlite3", It.IsAny<CancellationToken>()))
                .ReturnsAsync("C:/cm/dependencies/sqlite3/sqlite3.exe");
        IReadOnlyList<string>? captured = null;
        executor.Setup(e => e.ExecuteAsync("C:/cm/dependencies/sqlite3/sqlite3.exe",
                            It.IsAny<IReadOnlyList<string>>(), null, It.IsAny<CancellationToken>()))
                .Callback<string, IReadOnlyList<string>, string?, CancellationToken>((_, a, _, _) => captured = a)
                .ReturnsAsync(new CommandResult(0, "", "", false));

        // A value with a space and a quote must survive as a single element.
        var args = new[] { "/db path/x.db", "SELECT 1;" };
        await executor.Object.ExecuteResolvedAsync(resolver.Object, "jellyfin", "sqlite3", args);

        Assert.NotNull(captured);
        Assert.Equal(args, captured);
    }

    [Fact]
    public async Task ExecuteResolvedAsync_ReturnsTimedOut_WhenChildExceedsTimeout()
    {
        var executor = new Mock<ICommandExecutor>();
        var resolver = new Mock<IDependencyPathResolver>();
        resolver.Setup(r => r.ResolveAsync("android-devices", "adb", It.IsAny<CancellationToken>()))
                .ReturnsAsync("adb.exe");
        // Simulate a hung child: block until the (linked) token cancels — the kill-on-cancel path.
        executor.Setup(e => e.ExecuteAsync("adb.exe", "devices", null, It.IsAny<CancellationToken>()))
                .Returns(async (string _, string? _, string? _, CancellationToken ct) =>
                {
                    await Task.Delay(Timeout.Infinite, ct);
                    return new CommandResult(0, "", "", false);
                });

        var result = await executor.Object.ExecuteResolvedAsync(
            resolver.Object, "android-devices", "adb", "devices",
            timeout: TimeSpan.FromMilliseconds(100));

        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    public async Task ExecuteResolvedAsync_PropagatesCallerCancellation_NotAsTimeout()
    {
        var executor = new Mock<ICommandExecutor>();
        var resolver = new Mock<IDependencyPathResolver>();
        resolver.Setup(r => r.ResolveAsync("android-devices", "adb", It.IsAny<CancellationToken>()))
                .ReturnsAsync("adb.exe");
        executor.Setup(e => e.ExecuteAsync("adb.exe", "devices", null, It.IsAny<CancellationToken>()))
                .Returns(async (string _, string? _, string? _, CancellationToken ct) =>
                {
                    await Task.Delay(Timeout.Infinite, ct);
                    return new CommandResult(0, "", "", false);
                });

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        // A caller-driven cancellation must surface as OperationCanceledException, not a TimedOut result.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            executor.Object.ExecuteResolvedAsync(
                resolver.Object, "android-devices", "adb", "devices",
                cancellationToken: cts.Token, timeout: TimeSpan.FromSeconds(30)));
    }
}
