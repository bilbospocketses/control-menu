using ControlMenu.Services;

namespace ControlMenu.Tests.Services;

public class CommandExecutorTests
{
    private readonly CommandExecutor _executor = new();

    [Fact]
    public async Task ExecuteAsync_SimpleCommand_ReturnsOutput()
    {
        var result = await _executor.ExecuteAsync("echo", "hello");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.StandardOutput);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task ExecuteAsync_BadCommand_ReturnsNonZeroExitCode()
    {
        var result = await _executor.ExecuteAsync(
            OperatingSystem.IsWindows() ? "cmd" : "bash",
            OperatingSystem.IsWindows() ? "/c exit 1" : "-c \"exit 1\"");
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task ExecuteAsync_CapturesStderr()
    {
        var result = await _executor.ExecuteAsync(
            OperatingSystem.IsWindows() ? "cmd" : "bash",
            OperatingSystem.IsWindows()
                ? "/c echo error message>&2"
                : "-c \"echo error message >&2\"");
        Assert.Contains("error message", result.StandardError);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_RespectsToken()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _executor.ExecuteAsync("echo", "hello", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ExecuteDefinitionAsync_SelectsPlatformCommand()
    {
        var definition = new CommandDefinition
        {
            WindowsCommand = "cmd",
            WindowsArguments = "/c echo windows-hello",
            LinuxCommand = "echo",
            LinuxArguments = "linux-hello"
        };
        var result = await _executor.ExecuteAsync(definition);
        Assert.Equal(0, result.ExitCode);
        if (OperatingSystem.IsWindows())
            Assert.Contains("windows-hello", result.StandardOutput);
        else
            Assert.Contains("linux-hello", result.StandardOutput);
    }

    [Fact]
    public async Task ExecuteDefinitionAsync_Timeout_SetsTimedOutFlag()
    {
        var definition = new CommandDefinition
        {
            WindowsCommand = "cmd",
            WindowsArguments = "/c ping -n 10 127.0.0.1",
            LinuxCommand = "sleep",
            LinuxArguments = "10",
            Timeout = TimeSpan.FromMilliseconds(200)
        };
        var result = await _executor.ExecuteAsync(definition);
        Assert.True(result.TimedOut);
    }
}
