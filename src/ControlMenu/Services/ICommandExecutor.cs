namespace ControlMenu.Services;

public interface ICommandExecutor
{
    /// <summary>
    /// Executes a command by raw path or PATH-resolvable name. RESERVED for the OS-builtin allowlist
    /// only: <c>docker</c>, <c>powershell</c>, <c>arp</c>, <c>ping</c>. For bundled binaries
    /// (adb, scrcpy, node, sqlite3, go2rtc, ws-scrcpy-web) use
    /// <see cref="ResolvedExecutorExtensions.ExecuteResolvedAsync"/> instead.
    /// </summary>
    Task<CommandResult> ExecuteAsync(
        string command,
        string? arguments = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    Task<CommandResult> ExecuteAsync(
        CommandDefinition definition,
        CancellationToken cancellationToken = default);
}
