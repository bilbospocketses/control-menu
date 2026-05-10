using System.Diagnostics;

namespace ControlMenu.Services;

/// <summary>
/// Executes external commands. By project policy ("Local Dependencies Only" — see CLAUDE.md), the raw
/// (string command, …) overload is reserved for OS-builtin / genuinely external commands ONLY:
/// <list type="bullet">
///   <item><c>docker</c> — external daemon, not vendorable</item>
///   <item><c>powershell</c> — OS-shipped on Windows</item>
///   <item><c>arp</c> — OS-shipped network utility</item>
///   <item><c>ping</c> — OS-shipped network utility</item>
/// </list>
/// All bundled binaries (adb, scrcpy, node, sqlite3, go2rtc, ws-scrcpy-web) MUST go through
/// <see cref="ResolvedExecutorExtensions.ExecuteResolvedAsync"/>.
/// </summary>
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

        // Anchor working directory at the binary's own location by default (Gotcha 9).
        // Velopack's swap step renames current\ mid-flight; any child process holding a
        // working directory under current\ races the rename and can cause the swap to fail.
        //
        // When the caller passes an explicit workingDirectory, honour it.
        // When workingDirectory is null and FileName is an absolute path (all bundled
        // binaries routed through IDependencyPathResolver), default to the binary's own
        // directory — which under CM's deps layout is always outside current\.
        // When FileName is a bare name (PATH-resolved OS builtins: cmd, powershell, arp,
        // ping, docker), Path.GetDirectoryName returns null and we fall back to
        // Environment.CurrentDirectory. That is the only remaining cwd-inheritance path,
        // and it is intentionally limited to OS tools that do not depend on cwd.
        var resolvedWorkingDirectory = workingDirectory
            ?? Path.GetDirectoryName(command)
            ?? string.Empty;

        process.StartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = resolvedWorkingDirectory,
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
