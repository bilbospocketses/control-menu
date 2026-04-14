namespace ControlMenu.Modules.Utilities;

public class UtilitiesModule : IToolModule
{
    public string Id => "utilities";
    public string DisplayName => "Utilities";
    public string Icon => "bi-tools";
    public int SortOrder => 3;

    public IEnumerable<ModuleDependency> Dependencies => [];
    public IEnumerable<ConfigRequirement> ConfigRequirements => [];

    public IEnumerable<NavEntry> GetNavEntries() =>
    [
        new NavEntry("Icon Converter", "/utilities/icon-converter", "🖼️", 0),
        new NavEntry("File Unblocker", "/utilities/file-unblocker", "🔓", 1)
    ];

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}
