using ControlMenu.Modules;
using ControlMenu.Tests.Modules.Fakes;

namespace ControlMenu.Tests.Modules;

public class ModuleDiscoveryServiceTests
{
    [Fact]
    public void DiscoverModules_FindsFakeModules()
    {
        var service = new ModuleDiscoveryService([typeof(FakeToolModule).Assembly]);
        Assert.True(service.Modules.Count >= 2);
        Assert.Contains(service.Modules, m => m.Id == "fake-module");
        Assert.Contains(service.Modules, m => m.Id == "second-fake");
    }

    [Fact]
    public void DiscoverModules_SortedBySortOrder()
    {
        var service = new ModuleDiscoveryService([typeof(FakeToolModule).Assembly]);
        var fakeIndex = service.Modules.ToList().FindIndex(m => m.Id == "fake-module");
        var secondIndex = service.Modules.ToList().FindIndex(m => m.Id == "second-fake");
        // SecondFakeToolModule has SortOrder 5, FakeToolModule has 10
        Assert.True(secondIndex < fakeIndex, "Modules should be sorted by SortOrder ascending");
    }

    [Fact]
    public void GetNavEntries_AggregatesAllModuleEntries()
    {
        var service = new ModuleDiscoveryService([typeof(FakeToolModule).Assembly]);
        var allNav = service.Modules.SelectMany(m => m.GetNavEntries()).ToList();
        Assert.Contains(allNav, n => n.Href == "/fake");
        Assert.Contains(allNav, n => n.Href == "/second");
    }

    [Fact]
    public void DiscoverModules_EmptyAssemblyList_ReturnsEmpty()
    {
        var service = new ModuleDiscoveryService([]);
        Assert.Empty(service.Modules);
    }
}
