namespace ControlMenu.Common.Paths;

/// <summary>
/// Composition-root abstraction for all CM writable-state paths. Two
/// implementations: VelopackDataPathResolver (production, rooted at
/// <c>C:\ProgramData\ControlMenu</c>) and DevDataPathResolver (dotnet run,
/// rooted at <c>AppContext.BaseDirectory</c>). Selected at startup by
/// DataPathResolverFactory.
///
/// Spec reference: docs/superpowers/specs/2026-05-09-velopack-packaging-design.md
/// § "Path resolution".
/// </summary>
public interface IDataPathResolver
{
    string GetInstallRoot();
    string GetCurrentDir();
    string GetDataRoot();
    string GetConfigDir();
    string GetDbPath();
    string GetAppConfigPath();
    string GetDependenciesDir();
    string GetLogsDir();
    string GetKeysDir();
    string GetJellyfinBackupsDir();
}
