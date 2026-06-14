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

    /// <summary>
    /// Executes a command passing each argument as a discrete element via
    /// <c>ProcessStartInfo.ArgumentList</c> — the runtime quotes each verbatim, so a value
    /// containing spaces or quotes can never be split or injected into extra arguments. This is
    /// the injection-safe path: prefer it for any command whose arguments include caller- or
    /// data-derived values (file paths, device IDs, credentials, geometry). Same OS-builtin
    /// allowlist caveat as the string overload applies to the raw-path form; bundled binaries
    /// go through <see cref="ResolvedExecutorExtensions.ExecuteResolvedAsync(ICommandExecutor, IDependencyPathResolver, string, string, IReadOnlyList{string}, string?, CancellationToken)"/>.
    /// </summary>
    Task<CommandResult> ExecuteAsync(
        string command,
        IReadOnlyList<string> argumentList,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    Task<CommandResult> ExecuteAsync(
        CommandDefinition definition,
        CancellationToken cancellationToken = default);
}
