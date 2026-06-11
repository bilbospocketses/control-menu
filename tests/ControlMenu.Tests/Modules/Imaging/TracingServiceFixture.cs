using ControlMenu.Modules.Imaging.Services;
using ControlMenu.Services;
using ControlMenu.Tests.TestHelpers;

namespace ControlMenu.Tests.Modules.Imaging;

/// <summary>
/// xUnit collection fixture for the tracing integration tests. It stands up a real
/// <see cref="TracingService"/> that drives the REAL bundled vtracer.exe, potrace.exe, and
/// magick.exe end-to-end (mirrors <see cref="ImageServiceFixture"/>, but stages all THREE
/// binaries instead of just magick, because tracing's pipelines need all of them):
///
///   * <see cref="CommandExecutor"/> (real) — actually spawns the binaries. This is the
///     integration point under test: do the bundled portables run and produce parseable SVG
///     on this machine.
///   * <see cref="StagedTracingResolver"/> (test double for <see cref="IDependencyPathResolver"/>)
///     — returns the absolute path to a copy of each staged binary under the fixture's temp
///     deps dir. We substitute the resolver INTERFACE rather than reuse the production
///     <see cref="DependencyPathResolver"/> for the same reason ImageServiceFixture does: the
///     production resolver reads from the process-global DepsRootHolder.Path, which is fragile
///     to mutate from tests. <see cref="TracingService"/> only depends on the interface, so the
///     substitution is faithful — the real binaries still run.
///   * <see cref="TestPathResolver"/> (real test helper) as <see cref="IDataPathResolver"/>,
///     rooted at the temp dir, so <c>TracingService</c>'s per-call workdir writes under it.
///
/// magick needs its config files (policy.xml, *.xml, *.icc) sitting next to magick.exe, so the
/// fixture copies the ENTIRE staged dependency directory for each binary into
/// &lt;tempRoot&gt;/dependencies/&lt;name&gt;. The hardened policy.xml travels with magick — which is
/// why the monochrome pipeline routes through BMP (policy denies PNM).
///
/// If any of the three staged binaries can't be found (e.g. seed not present),
/// <see cref="EnginesAvailable"/> is false and the tests Skip rather than fail.
/// </summary>
public sealed class TracingServiceFixture : IDisposable
{
    public TracingService Service { get; }

    /// <summary>True only when ALL THREE bundled binaries (magick, vtracer, potrace) are staged.</summary>
    public bool EnginesAvailable { get; }

    /// <summary>Absolute path to the temp data root all fixture paths hang off of.</summary>
    public string TempRoot { get; }

    private static readonly string[] DependencyNames = ["magick", "vtracer", "potrace"];

    public TracingServiceFixture()
    {
        TempRoot = Path.Combine(Path.GetTempPath(), "cm-tracing-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempRoot);

        // Copy each staged dependency dir into <tempRoot>/dependencies/<name>, matching the
        // deps layout the production resolver and InstallPath convention use
        // (<depsRoot>/<dependency-name>/<exe>).
        var exePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var allPresent = true;
        foreach (var name in DependencyNames)
        {
            var targetDir = Path.Combine(TempRoot, "dependencies", name);
            var exePath = Path.Combine(targetDir, $"{name}.exe");
            exePaths[name] = exePath;

            var stagedDir = FindStagedDependencyDir(name);
            if (stagedDir is not null)
                CopyDirectory(stagedDir, targetDir);

            if (!File.Exists(exePath))
                allPresent = false;
        }

        EnginesAvailable = allPresent;

        var paths = new TestPathResolver(TempRoot);
        var executor = new CommandExecutor();
        var resolver = new StagedTracingResolver(exePaths);

        Service = new TracingService(executor, resolver, paths);
    }

    /// <summary>
    /// Walks up from the test assembly location looking for
    /// publish/seed/dependencies/&lt;name&gt; (the ready-staged copy of the bundled binary).
    /// Returns null if it can't be located, in which case tests skip.
    /// </summary>
    private static string? FindStagedDependencyDir(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "publish", "seed", "dependencies", name);
            if (File.Exists(Path.Combine(candidate, $"{name}.exe")))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(dest, Path.GetFileName(file));
            File.Copy(file, target, overwrite: true);
        }
        foreach (var sub in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(sub, Path.Combine(dest, Path.GetFileName(sub)));
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(TempRoot))
                Directory.Delete(TempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; a held file handle shouldn't fail the suite.
        }
    }

    /// <summary>
    /// Test <see cref="IDependencyPathResolver"/> that resolves the imaging/{magick,vtracer,potrace}
    /// triple to their staged copies, and throws <see cref="DependencyNotInstalledException"/>
    /// (the same type the production resolver throws) for anything else — keeping the
    /// Local-Dependencies-Only contract honest under test.
    /// </summary>
    private sealed class StagedTracingResolver : IDependencyPathResolver
    {
        private readonly IReadOnlyDictionary<string, string> _exePaths;

        public StagedTracingResolver(IReadOnlyDictionary<string, string> exePaths) => _exePaths = exePaths;

        public Task<string> ResolveAsync(string moduleId, string name, CancellationToken cancellationToken = default)
        {
            if (moduleId == "imaging" && _exePaths.TryGetValue(name, out var exePath))
            {
                if (!File.Exists(exePath))
                    throw new DependencyNotInstalledException(moduleId, name, exePath);
                return Task.FromResult(exePath);
            }

            throw new DependencyNotInstalledException(moduleId, name,
                $"<test resolver only knows imaging/{{magick,vtracer,potrace}}; got {moduleId}/{name}>");
        }
    }
}

[CollectionDefinition(nameof(TracingServiceCollection))]
public class TracingServiceCollection : ICollectionFixture<TracingServiceFixture>
{
}
