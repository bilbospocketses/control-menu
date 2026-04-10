using System.Diagnostics;

namespace ControlMenu.Services;

public class CommandExecutor : ICommandExecutor
{
    public async Task<CommandResult> ExecuteAsync(
        string command,
        string? arguments = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = workingDirectory ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new CommandResult(process.ExitCode, stdout, stderr, TimedOut: false);
    }

    public async Task<CommandResult> ExecuteAsync(
        CommandDefinition definition,
        CancellationToken cancellationToken = default)
    {
        var isWindows = OperatingSystem.IsWindows();
        var command = isWindows ? definition.WindowsCommand : definition.LinuxCommand;
        var arguments = isWindows ? definition.WindowsArguments : definition.LinuxArguments;

        if (definition.Timeout is { } timeout)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            try
            {
                return await ExecuteAsync(command, arguments, definition.WorkingDirectory, cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new CommandResult(ExitCode: -1, StandardOutput: "", StandardError: "Process timed out", TimedOut: true);
            }
        }

        return await ExecuteAsync(command, arguments, definition.WorkingDirectory, cancellationToken);
    }
}
