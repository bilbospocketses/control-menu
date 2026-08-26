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

    // ---------------------------------------------------------------------
    // Version patterns, pinned against real `--version` output
    // ---------------------------------------------------------------------

    [Theory]
    // vtracer changed its banner upstream: 0.6.4 printed "visioncortex VTracer 0.6.4",
    // 1.0.0-alpha.3 prints "vtracer 1.0.0-alpha.3" -- lowercase, no prefix, and a prerelease
    // suffix that a [\d.]+ capture cannot represent. Both shapes must parse.
    [InlineData("vtracer", "vtracer 1.0.0-alpha.3", "1.0.0-alpha.3")]
    [InlineData("vtracer", "visioncortex VTracer 0.6.4", "0.6.4")]
    // potrace ends its banner with a sentence period, which a greedy [\d.]+ swallowed,
    // recording the installed version as "1.16." instead of "1.16".
    [InlineData("potrace", "potrace 1.16. Copyright (C) 2001-2019 Peter Selinger.", "1.16")]
    // magick already parsed correctly; pinned so a future edit cannot regress it.
    [InlineData("magick", "Version: ImageMagick 7.1.2-30 Q8 x64 344e905:20260823 https://imagemagick.org", "7.1.2-30")]
    public void VersionPattern_ParsesRealVersionOutput(string dependency, string output, string expected)
    {
        var pattern = _module.Dependencies.Single(d => d.Name == dependency).VersionPattern;

        var match = System.Text.RegularExpressions.Regex.Match(output, pattern);

        Assert.True(match.Success, $"{dependency} pattern '{pattern}' did not match: {output}");
        Assert.Equal(expected, match.Groups[1].Value);
    }

    [Fact]
    public void VersionPattern_ParsedInstalledVersion_MatchesTheReleaseTagShape()
    {
        // The installed version is compared against the GitHub tag to decide "update available".
        // vtracer tags carry no "v" prefix and do include the prerelease suffix, so a pattern that
        // drops the suffix leaves the dependency permanently reporting an update.
        var pattern = _module.Dependencies.Single(d => d.Name == "vtracer").VersionPattern;

        var parsed = System.Text.RegularExpressions.Regex
            .Match("vtracer 1.0.0-alpha.3", pattern).Groups[1].Value;

        Assert.Equal("1.0.0-alpha.3", parsed);
    }
}
