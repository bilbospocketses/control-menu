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

    /// <summary>
    /// On Windows, appends a ".exe" suffix to a bare executable name when it lacks one
    /// (case-insensitive); on other platforms returns the name unchanged. Single source
    /// of truth for the suffix logic previously duplicated across DependencyPathResolver
    /// and DependencyManagerService.
    /// </summary>
    public static string WithExecutableSuffix(string executableName)
    {
        if (OperatingSystem.IsWindows()
            && !executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return executableName + ".exe";
        return executableName;
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
