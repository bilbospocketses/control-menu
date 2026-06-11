namespace ControlMenu.Modules.Utilities;

public class UtilitiesModule : IToolModule
{
    public string Id => "utilities";
    public string DisplayName => "Utilities";
    public string Icon => "bi-tools";
    public int SortOrder => 4;

    public IEnumerable<ModuleDependency> Dependencies => [];
    public IEnumerable<ConfigRequirement> ConfigRequirements => [];

    public IEnumerable<NavEntry> GetNavEntries() =>
    [
        new NavEntry("File Unblocker", "/utilities/file-unblocker", "🔓", 1)
    ];

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}
