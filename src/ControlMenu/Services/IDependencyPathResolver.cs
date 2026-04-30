namespace ControlMenu.Services;

public interface IDependencyPathResolver
{
    /// <summary>
    /// Returns the absolute path to a bundled binary, applying any user-configured override.
    /// Throws <see cref="DependencyNotInstalledException"/> if the binary is not present locally.
    /// Per the project's "Local Dependencies Only" rule, this is the ONLY supported way to obtain
    /// a path to a bundled binary — never use system PATH, env vars, or "wherever the user installed it".
    /// </summary>
    Task<string> ResolveAsync(string moduleId, string name, CancellationToken cancellationToken = default);
}

public sealed class DependencyNotInstalledException : Exception
{
    public string ModuleId { get; }
    public string Name { get; }
    public string ExpectedPath { get; }

    public DependencyNotInstalledException(string moduleId, string name, string expectedPath)
        : base($"Dependency '{name}' (module '{moduleId}') is not installed locally. Expected at: {expectedPath}")
    {
        ModuleId = moduleId;
        Name = name;
        ExpectedPath = expectedPath;
    }
}
