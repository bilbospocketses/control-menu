using ControlMenu.Modules.Imaging;

namespace ControlMenu.Tests.Modules.Imaging;

public class ImagingModuleTests
{
    private readonly ImagingModule _module = new();

    [Fact]
    public void Id_IsImaging()
    {
        Assert.Equal("imaging", _module.Id);
    }

    [Fact]
    public void DisplayName_IsImagingTools()
    {
        Assert.Equal("Imaging Tools", _module.DisplayName);
    }

    [Fact]
    public void NavEntries_IncludeIconConverter()
    {
        var entries = _module.GetNavEntries().ToList();
        Assert.Contains(entries, e => e.Href == "/imaging/icon-converter");
    }

    [Fact]
    public void NavEntries_IncludeFormatConverter()
    {
        var entries = _module.GetNavEntries().ToList();
        Assert.Contains(entries, e => e.Href == "/imaging/format-converter");
    }

    [Fact]
    public void NavEntries_IncludeImageResize()
    {
        var entries = _module.GetNavEntries().ToList();
        Assert.Contains(entries, e => e.Href == "/imaging/image-resize");
    }

    [Fact]
    public void NavEntries_IncludeSvgRasterize()
    {
        var entries = _module.GetNavEntries().ToList();
        Assert.Contains(entries, e => e.Href == "/imaging/svg-rasterize");
    }

    [Fact]
    public void NavEntries_IncludeMagicWand()
    {
        var entries = _module.GetNavEntries().ToList();
        Assert.Contains(entries, e => e.Href == "/imaging/magic-wand");
    }

    [Fact]
    public void NavEntries_IncludeTracing()
    {
        var entries = _module.GetNavEntries().ToList();
        Assert.Contains(entries, e => e.Href == "/imaging/tracing");
    }

    [Fact]
    public void NavEntries_AreOrderedBySortOrder()
    {
        var entries = _module.GetNavEntries().ToList();
        Assert.Equal("/imaging/icon-converter", entries[0].Href);
        Assert.Equal("/imaging/format-converter", entries[1].Href);
        Assert.Equal("/imaging/image-resize", entries[2].Href);
        Assert.Equal("/imaging/svg-rasterize", entries[3].Href);
        Assert.Equal("/imaging/magic-wand", entries[4].Href);
        Assert.Equal("/imaging/tracing", entries[5].Href);
    }
}
