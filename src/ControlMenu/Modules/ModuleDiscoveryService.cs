using System.Reflection;

namespace ControlMenu.Modules;

public class ModuleDiscoveryService
{
    public IReadOnlyList<IToolModule> Modules { get; }

    public ModuleDiscoveryService(IEnumerable<Assembly> assemblies)
    {
        Modules = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IToolModule).IsAssignableFrom(t)
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (IToolModule)Activator.CreateInstance(t)!)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.DisplayName)
            .ToList();
    }
}
