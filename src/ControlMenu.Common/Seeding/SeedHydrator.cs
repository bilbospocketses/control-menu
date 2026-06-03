namespace ControlMenu.Common.Seeding;

/// <summary>
/// Copies pre-seeded runtime dependencies bundled inside the install's
/// <c>current\seed\dependencies\</c> tree into the writable
/// <c>&lt;dataRoot&gt;\dependencies\</c> on first launch (and every launch
/// thereafter, idempotently — already-present leaves are preserved so that
/// any user-updated dependency survives a Velopack swap of the seed bundle).
///
/// Mirrors ws-scrcpy-web's <c>seed/node/</c> bootstrap pattern: the seed
/// inside <c>current\</c> is immutable and ships with the MSI; the working
/// copy in <c>dataRoot\</c> is what the resolver actually reads, and is
/// hot-swappable via the Dependencies UI.
/// </summary>
public static class SeedHydrator
{
    public record Result(int Copied, int Skipped, int Pruned);

    /// <summary>
    /// Dependencies that were previously seeded/installed but have since been
    /// removed from the app. The dependency manager never cleans a removed dep's
    /// directory, so <see cref="Hydrate"/> prunes them from
    /// <c>&lt;dataRoot&gt;\dependencies\</c> on launch to reclaim disk space.
    /// Best-effort — a delete failure (locked file, permissions) is swallowed so
    /// it can never block launch; the next launch retries.
    /// </summary>
    private static readonly string[] RetiredLeaves = ["scrcpy"];

    public static Result Hydrate(string currentDir, string targetDependenciesDir)
    {
        // Prune retired deps first so existing installs reclaim the space even in
        // dev mode (no seed/), where the hydration loop below early-returns.
        var pruned = PruneRetiredLeaves(targetDependenciesDir);

        var seedRoot = Path.Combine(currentDir, "seed", "dependencies");
        if (!Directory.Exists(seedRoot))
            return new Result(0, 0, pruned);

        Directory.CreateDirectory(targetDependenciesDir);

        var copied = 0;
        var skipped = 0;

        foreach (var leafSeed in Directory.EnumerateDirectories(seedRoot))
        {
            var leafName = Path.GetFileName(leafSeed);
            var targetLeaf = Path.Combine(targetDependenciesDir, leafName);

            if (Directory.Exists(targetLeaf))
            {
                skipped++;
                continue;
            }

            CopyTree(leafSeed, targetLeaf);
            copied++;
        }

        return new Result(copied, skipped, pruned);
    }

    private static int PruneRetiredLeaves(string targetDependenciesDir)
    {
        if (!Directory.Exists(targetDependenciesDir))
            return 0;

        var pruned = 0;
        foreach (var leaf in RetiredLeaves)
        {
            var dir = Path.Combine(targetDependenciesDir, leaf);
            if (!Directory.Exists(dir))
                continue;
            try
            {
                Directory.Delete(dir, recursive: true);
                pruned++;
            }
            catch
            {
                // Best-effort: a locked file or permission issue must never block
                // launch. The next launch retries.
            }
        }
        return pruned;
    }

    private static void CopyTree(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(dest, rel));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            File.Copy(file, Path.Combine(dest, rel), overwrite: false);
        }
    }
}
