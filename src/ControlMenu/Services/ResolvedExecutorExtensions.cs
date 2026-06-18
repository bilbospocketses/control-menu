namespace ControlMenu.Services;

public static class ResolvedExecutorExtensions
{
    /// <summary>
    /// Backstop timeout applied to a bundled-binary invocation when the caller specifies none.
    /// The executor kills the child tree on cancellation, but its callers passed no deadline, so a
    /// hung child (stuck adb/magick/sqlite3) blocked the awaiting Blazor circuit indefinitely. This
    /// bounds every one-shot invocation; it is generous enough not to clip legitimate work and is
    /// overridable per call. Long-running processes (go2rtc, scrcpy) are supervised separately, not
    /// run through this path.
    /// </summary>
    public static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Executes a bundled local binary identified by (moduleId, name). The path is resolved through
    /// <see cref="IDependencyPathResolver"/> — the ONLY supported way to invoke a bundled binary in
    /// this codebase per the "Local Dependencies Only" rule. Throws
    /// <see cref="DependencyNotInstalledException"/> if the binary isn't installed.
    /// </summary>
    /// <remarks>
    /// Do NOT add OS-builtin allowlist entries here (docker, powershell, arp, ping). Those go through
    /// the raw <see cref="ICommandExecutor.ExecuteAsync(string, string?, string?, CancellationToken)"/>
    /// overload, which is reserved for the documented allowlist only.
    /// </remarks>
    public static async Task<CommandResult> ExecuteResolvedAsync(
        this ICommandExecutor executor,
        IDependencyPathResolver resolver,
        string moduleId,
        string name,
        string? arguments = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        var path = await resolver.ResolveAsync(moduleId, name, cancellationToken);
        return await RunWithTimeoutAsync(
            ct => executor.ExecuteAsync(path, arguments, workingDirectory, ct), timeout, cancellationToken);
    }

    /// <summary>
    /// Injection-safe variant of <see cref="ExecuteResolvedAsync(ICommandExecutor, IDependencyPathResolver, string, string, string?, string?, CancellationToken)"/>:
    /// passes each argument as a discrete <c>ArgumentList</c> element so caller- or data-derived
    /// values (paths, IDs, credentials) can never be split or injected. Prefer this overload for any
    /// bundled-binary invocation that interpolates a non-constant value.
    /// </summary>
    public static async Task<CommandResult> ExecuteResolvedAsync(
        this ICommandExecutor executor,
        IDependencyPathResolver resolver,
        string moduleId,
        string name,
        IReadOnlyList<string> argumentList,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        var path = await resolver.ResolveAsync(moduleId, name, cancellationToken);
        return await RunWithTimeoutAsync(
            ct => executor.ExecuteAsync(path, argumentList, workingDirectory, ct), timeout, cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="run"/> under a linked CTS bounded by <paramref name="timeout"/>
    /// (or <see cref="DefaultCommandTimeout"/>). On expiry the executor's kill-on-cancel tears down
    /// the child tree and we return a TimedOut result; a caller-driven cancellation propagates as
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    private static async Task<CommandResult> RunWithTimeoutAsync(
        Func<CancellationToken, Task<CommandResult>> run, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? DefaultCommandTimeout);
        try
        {
            return await run(cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CommandResult(ExitCode: -1, StandardOutput: "", StandardError: "Process timed out", TimedOut: true);
        }
    }
}
