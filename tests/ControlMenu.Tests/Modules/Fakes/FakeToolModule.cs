using ControlMenu.Modules;

namespace ControlMenu.Tests.Modules.Fakes;

public class FakeToolModule : IToolModule
{
    public string Id => "fake-module";
    public string DisplayName => "Fake Module";
    public string Icon => "bi-gear";
    public int SortOrder => 10;
    public IEnumerable<ModuleDependency> Dependencies => [];
    public IEnumerable<ConfigRequirement> ConfigRequirements => [];
    public IEnumerable<NavEntry> GetNavEntries() =>
    [
        new NavEntry("Fake Page", "/fake", "bi-gear", 0)
    ];
    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}

public class SecondFakeToolModule : IToolModule
{
    public string Id => "second-fake";
    public string DisplayName => "Second Fake";
    public string Icon => "bi-star";
    public int SortOrder => 5;
    public IEnumerable<ModuleDependency> Dependencies => [];
    public IEnumerable<ConfigRequirement> ConfigRequirements => [];
    public IEnumerable<NavEntry> GetNavEntries() =>
    [
        new NavEntry("Second Page", "/second", "bi-star", 0)
    ];
    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}
