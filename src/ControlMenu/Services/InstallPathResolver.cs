namespace ControlMenu.Services;

internal static class InstallPathResolver
{
    public static string Resolve(string defaultInstallPath, string? storedOverride)
    {
        if (string.IsNullOrWhiteSpace(storedOverride)) return defaultInstallPath;
        if (Path.IsPathRooted(storedOverride)) return storedOverride;

        var depsRoot = Path.GetDirectoryName(defaultInstallPath);
        return string.IsNullOrEmpty(depsRoot)
            ? storedOverride
            : Path.Combine(depsRoot, storedOverride);
    }

    public static string Encode(string absolutePath, string defaultInstallPath)
    {
        var depsRoot = Path.GetDirectoryName(defaultInstallPath);
        if (string.IsNullOrEmpty(depsRoot)) return absolutePath;
        return IsUnder(absolutePath, depsRoot)
            ? Path.GetRelativePath(depsRoot, absolutePath)
            : absolutePath;
    }

    public static bool IsParentMissing(string resolvedPath)
    {
        var parent = Path.GetDirectoryName(resolvedPath);
        return string.IsNullOrEmpty(parent) || !Directory.Exists(parent);
    }

    private static bool IsUnder(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root);
        var rel = Path.GetRelativePath(fullRoot, fullPath);
        return !rel.StartsWith("..", StringComparison.Ordinal)
            && !Path.IsPathRooted(rel);
    }
}
