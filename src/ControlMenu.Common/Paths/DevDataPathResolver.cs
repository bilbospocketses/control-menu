namespace ControlMenu.Common.Paths;

/// <summary>
/// Dev-mode resolver. All paths root at <see cref="AppContext.BaseDirectory"/>
/// (or any directory passed to the constructor). Preserves the dotnet-run
/// workflow — config, logs, db, deps, keys all colocated under the build
/// output dir.
/// </summary>
public sealed class DevDataPathResolver : IDataPathResolver
{
    private readonly string _baseDir;

    public DevDataPathResolver(string baseDir) { _baseDir = baseDir; }

    public string GetInstallRoot() => _baseDir;
    public string GetCurrentDir() => _baseDir;
    public string GetDataRoot() => _baseDir;
    /// <remarks>Dev mode collapses config into the data root by design — no subdirectory separation.</remarks>
    public string GetConfigDir() => _baseDir;
    public string GetDbPath() => Path.Combine(_baseDir, "controlmenu.db");
    public string GetAppConfigPath() => Path.Combine(_baseDir, "app-config.json");
    public string GetDependenciesDir() => Path.Combine(_baseDir, "dependencies");
    public string GetLogsDir() => Path.Combine(_baseDir, "logs");
    public string GetKeysDir() => Path.Combine(_baseDir, "keys");
    public string GetJellyfinBackupsDir() => Path.Combine(_baseDir, "jellyfin-backups");
}
