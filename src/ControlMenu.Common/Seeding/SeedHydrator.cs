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
    public record Result(int Copied, int Skipped);

    public static Result Hydrate(string currentDir, string targetDependenciesDir)
    {
        var seedRoot = Path.Combine(currentDir, "seed", "dependencies");
        if (!Directory.Exists(seedRoot))
            return new Result(0, 0);

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

        return new Result(copied, skipped);
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
