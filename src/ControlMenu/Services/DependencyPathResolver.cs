using ControlMenu.Modules;

namespace ControlMenu.Services;

public class DependencyPathResolver : IDependencyPathResolver
{
    private readonly IReadOnlyList<IToolModule> _modules;
    private readonly IConfigurationService _config;

    public DependencyPathResolver(IEnumerable<IToolModule> modules, IConfigurationService config)
    {
        _modules = modules.ToList();
        _config = config;
    }

    public async Task<string> ResolveAsync(string moduleId, string name, CancellationToken cancellationToken = default)
    {
        var module = _modules.FirstOrDefault(m => m.Id == moduleId)
            ?? throw new DependencyNotInstalledException(moduleId, name,
                $"<unknown module '{moduleId}'>");

        var dep = module.Dependencies.FirstOrDefault(d => d.Name == name)
            ?? throw new DependencyNotInstalledException(moduleId, name,
                $"<dependency '{name}' not declared in module '{moduleId}'>");

        if (dep.InstallPath is null)
            throw new DependencyNotInstalledException(moduleId, name,
                $"<dependency '{name}' has no InstallPath; cannot be a local binary>");

        var customPath = await _config.GetSettingAsync($"dep-path-{name}");
        var installDir = InstallPathResolver.Resolve(dep.InstallPath, customPath);

        var exeName = dep.ExecutableName;
        if (OperatingSystem.IsWindows() && !exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            exeName += ".exe";

        var exePath = Path.Combine(installDir, exeName);
        if (!File.Exists(exePath))
            throw new DependencyNotInstalledException(moduleId, name, exePath);

        return exePath;
    }
}
