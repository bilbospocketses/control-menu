using ControlMenu.Services;

namespace ControlMenu.Tests.Services;

public class CommandExecutorTests
{
    private readonly CommandExecutor _executor = new();

    [Fact]
    public async Task ExecuteAsync_SimpleCommand_ReturnsOutput()
    {
        var result = await _executor.ExecuteAsync(
            OperatingSystem.IsWindows() ? "cmd" : "bash",
            OperatingSystem.IsWindows() ? "/c echo hello" : "-c \"echo hello\"");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.StandardOutput);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_BareNameRespectsToken()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        // The cancellation must throw before Process.Start is reached, so a bare-name
        // command (which can't be resolved on Windows) is fine here.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _executor.ExecuteAsync(
                OperatingSystem.IsWindows() ? "cmd" : "bash",
                OperatingSystem.IsWindows() ? "/c echo hello" : "-c \"echo hello\"",
                cancellationToken: cts.Token));
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

    [Fact]
    public async Task ExecuteAsync_ArgumentList_SimpleCommand_ReturnsOutput()
    {
        var (cmd, args) = OperatingSystem.IsWindows()
            ? ("cmd", (IReadOnlyList<string>)["/c", "echo", "hello"])
            : ("echo", (IReadOnlyList<string>)["hello"]);
        var result = await _executor.ExecuteAsync(cmd, args);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.StandardOutput);
    }

    [Fact]
    public async Task ExecuteAsync_ArgumentList_PassesSpaceContainingArgAsOneToken()
    {
        // Boundary proof: a path containing a space is passed as ONE argument.
        // With the old string overload, "type C:\...\with space.txt" would split
        // into two tokens and the file would not be found (non-zero exit, no body).
        var dir = Path.Combine(Path.GetTempPath(), "cm-argv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, "with space.txt");
        await File.WriteAllTextAsync(filePath, "CONTENT_OK");
        try
        {
            var (cmd, args) = OperatingSystem.IsWindows()
                ? ("cmd", (IReadOnlyList<string>)["/c", "type", filePath])
                : ("cat", (IReadOnlyList<string>)[filePath]);
            var result = await _executor.ExecuteAsync(cmd, args);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("CONTENT_OK", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
