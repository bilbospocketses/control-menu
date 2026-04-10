namespace ControlMenu.Modules;

public interface IToolModule
{
    string Id { get; }
    string DisplayName { get; }
    string Icon { get; }
    int SortOrder { get; }
    IEnumerable<ModuleDependency> Dependencies { get; }
    IEnumerable<ConfigRequirement> ConfigRequirements { get; }
    IEnumerable<NavEntry> GetNavEntries();
    IEnumerable<BackgroundJobDefinition> GetBackgroundJobs();
}
