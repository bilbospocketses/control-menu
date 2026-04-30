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
}
