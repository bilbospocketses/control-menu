namespace ControlMenu.Services;

public static class ResolvedExecutorExtensions
{
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
        CancellationToken cancellationToken = default)
    {
        var path = await resolver.ResolveAsync(moduleId, name, cancellationToken);
        return await executor.ExecuteAsync(path, arguments, workingDirectory, cancellationToken);
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
        CancellationToken cancellationToken = default)
    {
        var path = await resolver.ResolveAsync(moduleId, name, cancellationToken);
        return await executor.ExecuteAsync(path, argumentList, workingDirectory, cancellationToken);
    }
}
