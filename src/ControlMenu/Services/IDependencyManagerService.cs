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
}
