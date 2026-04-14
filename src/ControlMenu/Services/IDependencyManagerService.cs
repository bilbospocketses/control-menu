namespace ControlMenu.Services;

public interface IDependencyManagerService
{
    Task SyncDependenciesAsync();
    Task<DependencyCheckResult> CheckDependencyAsync(Guid dependencyId);
    Task<IReadOnlyList<DependencyCheckResult>> CheckAllAsync();
    Task<AssetMatch?> ResolveDownloadAssetAsync(Guid dependencyId);
    Task<UpdateResult> DownloadAndInstallAsync(Guid dependencyId, AssetMatch asset);
    Task<int> GetUpdateAvailableCountAsync();
    Task<IReadOnlyList<Data.Entities.Dependency>> GetAllDependenciesAsync();
    Task<IReadOnlyList<DependencyScanResult>> ScanForDependenciesAsync();
    Task<DependencyScanResult?> ValidateManualPathAsync(string name, string moduleId, string executablePath);
    bool CanAutoInstall(string name, string moduleId);
    bool IsConfigurable(string name, string moduleId);
    string? GetInstallPath(string name, string moduleId);
    Task<string?> GetInstallPathAsync(string name, string moduleId);
    Task SetInstallPathAsync(string name, string path);
}
