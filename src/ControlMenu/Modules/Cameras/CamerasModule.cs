namespace ControlMenu.Modules.Cameras;

public class CamerasModule : IToolModule
{
    public string Id => "cameras";
    public string DisplayName => "Cameras";
    public string Icon => "bi-camera-video";
    public int SortOrder => 4;

    public IEnumerable<ModuleDependency> Dependencies => [];
    public IEnumerable<ConfigRequirement> ConfigRequirements => [];

    public IEnumerable<NavEntry> GetNavEntries()
    {
        for (var i = 1; i <= 8; i++)
            yield return new NavEntry($"Camera {i}", $"/cameras/{i}", "📷", i);
    }

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}
