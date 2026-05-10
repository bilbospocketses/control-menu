using ControlMenu.Common.Config;

namespace ControlMenu.Common.Paths;

/// <summary>
/// Velopack-mode resolver. Install root from Velopack PerMachine layout
/// (<c>C:\Program Files\ControlMenu</c>). Data root via PROGRAMDATA env
/// (<c>C:\ProgramData\ControlMenu</c>) — composed from
/// AppConfigPaths.DataRootForWindows.
/// </summary>
public sealed class VelopackDataPathResolver : IDataPathResolver
{
    private readonly string _installRoot;
    private readonly string _dataRoot;

    public VelopackDataPathResolver(string installRoot, string? programData = null)
    {
        _installRoot = installRoot;
        _dataRoot = AppConfigPaths.DataRootForWindows(programData ?? Environment.GetEnvironmentVariable("PROGRAMDATA"));
    }

    public string GetInstallRoot() => _installRoot;
    public string GetCurrentDir() => Path.Combine(_installRoot, "current");
    public string GetDataRoot() => _dataRoot;
    public string GetConfigDir() => Path.Combine(_dataRoot, "config");
    public string GetDbPath() => Path.Combine(_dataRoot, "config", "controlmenu.db");
    public string GetAppConfigPath() => Path.Combine(_dataRoot, "config", "app-config.json");
    public string GetDependenciesDir() => Path.Combine(_dataRoot, "dependencies");
    public string GetLogsDir() => Path.Combine(_dataRoot, "logs");
    public string GetKeysDir() => Path.Combine(_dataRoot, "keys");
    public string GetJellyfinBackupsDir() => Path.Combine(_dataRoot, "jellyfin-backups");
}
