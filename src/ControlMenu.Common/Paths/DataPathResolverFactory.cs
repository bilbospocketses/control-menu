namespace ControlMenu.Common.Paths;

/// <summary>
/// Probe for an <c>Update.exe</c> sibling-to-install-root to detect Velopack mode.
/// In Velopack PerMachine layout the launcher exe lives at
/// <c>&lt;installRoot&gt;\current\ControlMenuLauncher.exe</c> and Update.exe
/// lives at <c>&lt;installRoot&gt;\Update.exe</c>. Found that pattern? emit
/// <see cref="VelopackDataPathResolver"/>; otherwise fall back to
/// <see cref="DevDataPathResolver"/> rooted at the exe's directory.
/// </summary>
public static class DataPathResolverFactory
{
    public static IDataPathResolver Create(string exePath, string? programData = null)
    {
        var exeDir = Path.GetDirectoryName(exePath) ?? throw new InvalidOperationException("exe has no parent dir");
        var maybeInstallRoot = Path.GetDirectoryName(exeDir);
        if (maybeInstallRoot is not null
            && File.Exists(Path.Combine(maybeInstallRoot, "Update.exe")))
        {
            return new VelopackDataPathResolver(maybeInstallRoot, programData);
        }

        return new DevDataPathResolver(exeDir);
    }

    public static IDataPathResolver CreateFromCurrentProcess(string? programData = null)
    {
        var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("could not determine current process exe path");
        return Create(exe, programData);
    }
}
