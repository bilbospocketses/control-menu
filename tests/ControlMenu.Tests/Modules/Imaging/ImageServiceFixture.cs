using ControlMenu.Modules.Imaging.Services;
using ControlMenu.Services;
using ControlMenu.Tests.TestHelpers;

namespace ControlMenu.Tests.Modules.Imaging;

/// <summary>
/// xUnit collection fixture for the imaging integration tests. It stands up a real
/// <see cref="ImageService"/> that drives the REAL bundled magick.exe end-to-end:
///
///   * <see cref="CommandExecutor"/> (real) — actually spawns magick.exe. This is the
///     integration point under test: does the bundled portable run and produce parseable
///     output on this machine.
///   * <see cref="StagedMagickResolver"/> (test double for <see cref="IDependencyPathResolver"/>)
///     — returns the absolute path to a copy of the staged magick under the fixture's temp
///     deps dir. We substitute the resolver INTERFACE rather than reuse the production
///     <see cref="DependencyPathResolver"/> because the production resolver reads the binary
///     location from the process-global <c>DepsRootHolder.Path</c> (set once at the app
///     composition root) and from <c>ImagingModule</c>'s static-readonly deps-root capture.
///     Mutating that static from a test would be order-fragile and would leak across the
///     whole test run. <see cref="ImageService"/> only ever depends on the interface, so the
///     substitution is faithful — the real binary still runs.
///   * <see cref="TestPathResolver"/> (real test helper) as <see cref="IDataPathResolver"/>,
///     rooted at the temp dir, so <c>ImageService.CreateWorkDir()</c> writes under it.
///
/// magick needs its config files (policy.xml, *.xml, *.icc) sitting next to magick.exe, so
/// the fixture copies the ENTIRE staged dependency directory
/// (publish/seed/dependencies/magick) into &lt;tempRoot&gt;/dependencies/magick.
///
/// If the staged magick can't be found (e.g. seed not present), <see cref="MagickAvailable"/>
/// is false and the tests Skip rather than fail.
/// </summary>
public sealed class ImageServiceFixture : IDisposable
{
    public ImageService Service { get; }
    public bool MagickAvailable { get; }

    /// <summary>Absolute path to the temp data root all fixture paths hang off of.</summary>
    public string TempRoot { get; }

    public ImageServiceFixture()
    {
        TempRoot = Path.Combine(Path.GetTempPath(), "cm-imaging-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempRoot);

        // <tempRoot>/dependencies/magick — matches the deps layout the production resolver
        // and InstallPath convention use (<depsRoot>/<dependency-name>/<exe>).
        var magickTargetDir = Path.Combine(TempRoot, "dependencies", "magick");
        var magickExePath = Path.Combine(magickTargetDir, "magick.exe");

        var stagedDir = FindStagedMagickDir();
        if (stagedDir is not null)
        {
            CopyDirectory(stagedDir, magickTargetDir);
        }

        MagickAvailable = File.Exists(magickExePath);

        var paths = new TestPathResolver(TempRoot);
        var executor = new CommandExecutor();
        var resolver = new StagedMagickResolver(magickExePath);

        Service = new ImageService(executor, resolver, paths);
    }

    /// <summary>
    /// Walks up from the test assembly location looking for
    /// publish/seed/dependencies/magick (the ready-staged copy of the bundled binary).
    /// Returns null if it can't be located, in which case tests skip.
    /// </summary>
    private static string? FindStagedMagickDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "publish", "seed", "dependencies", "magick");
            if (File.Exists(Path.Combine(candidate, "magick.exe")))
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
    /// Test <see cref="IDependencyPathResolver"/> that resolves ONLY the imaging/magick
    /// pair to the staged copy, and throws <see cref="DependencyNotInstalledException"/>
    /// (the same type the production resolver throws) for anything else — keeping the
    /// Local-Dependencies-Only contract honest under test.
    /// </summary>
    private sealed class StagedMagickResolver : IDependencyPathResolver
    {
        private readonly string _magickExePath;

        public StagedMagickResolver(string magickExePath) => _magickExePath = magickExePath;

        public Task<string> ResolveAsync(string moduleId, string name, CancellationToken cancellationToken = default)
        {
            if (moduleId == "imaging" && name == "magick")
            {
                if (!File.Exists(_magickExePath))
                    throw new DependencyNotInstalledException(moduleId, name, _magickExePath);
                return Task.FromResult(_magickExePath);
            }

            throw new DependencyNotInstalledException(moduleId, name,
                $"<test resolver only knows imaging/magick; got {moduleId}/{name}>");
        }
    }
}

[CollectionDefinition(nameof(ImageServiceCollection))]
public class ImageServiceCollection : ICollectionFixture<ImageServiceFixture>
{
}
