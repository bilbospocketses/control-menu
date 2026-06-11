using ControlMenu.Modules;
using ControlMenu.Modules.Utilities;

namespace ControlMenu.Tests.Modules.Utilities;

public class UtilitiesModuleTests
{
    private readonly UtilitiesModule _module = new();

    [Fact]
    public void Id_IsUtilities()
    {
        Assert.Equal("utilities", _module.Id);
    }

    [Fact]
    public void DisplayName_IsUtilities()
    {
        Assert.Equal("Utilities", _module.DisplayName);
    }

    [Fact]
    public void Icon_IsToolsIcon()
    {
        Assert.Equal("bi-tools", _module.Icon);
    }

    [Fact]
    public void Dependencies_IsEmpty()
    {
        Assert.Empty(_module.Dependencies);
    }

    [Fact]
    public void ConfigRequirements_IsEmpty()
    {
        Assert.Empty(_module.ConfigRequirements);
    }

    [Fact]
    public void NavEntries_IncludesFileUnblocker()
    {
        var entries = _module.GetNavEntries().ToList();
        Assert.Contains(entries, e => e.Href == "/utilities/file-unblocker");
    }

    [Fact]
    public void NavEntries_DoesNotIncludeIconConverter()
    {
        // Icon Converter migrated to the Imaging Tools module
        // (/imaging/icon-converter); the old /utilities/icon-converter route is
        // now a redirect stub, not a sidebar nav entry.
        var entries = _module.GetNavEntries().ToList();
        Assert.DoesNotContain(entries, e => e.Href == "/utilities/icon-converter");
    }

    [Fact]
    public void GetBackgroundJobs_ReturnsEmpty()
    {
        Assert.Empty(_module.GetBackgroundJobs());
    }
}
