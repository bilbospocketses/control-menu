namespace ControlMenu.Tests.TestHelpers;

/// <summary>
/// Stub IDataPathResolver for unit tests. All paths root at a caller-supplied
/// temp directory so tests are isolated and deterministic.
/// </summary>
internal sealed class TestPathResolver : ControlMenu.Common.Paths.IDataPathResolver
{
    private readonly string _root;

    public TestPathResolver(string root) { _root = root; }

    public string GetInstallRoot() => _root;
    public string GetCurrentDir() => _root;
    public string GetDataRoot() => _root;
    public string GetConfigDir() => Path.Combine(_root, "config");
    public string GetDbPath() => Path.Combine(_root, "config", "controlmenu.db");
    public string GetAppConfigPath() => Path.Combine(_root, "config", "app-config.json");
    public string GetDependenciesDir() => Path.Combine(_root, "dependencies");
    public string GetLogsDir() => Path.Combine(_root, "logs");
    public string GetKeysDir() => Path.Combine(_root, "keys");
    public string GetJellyfinBackupsDir() => Path.Combine(_root, "jellyfin-backups");
}
