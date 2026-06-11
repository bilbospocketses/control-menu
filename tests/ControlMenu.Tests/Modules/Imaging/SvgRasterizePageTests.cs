using Bunit;
using ControlMenu.Modules.Imaging.Pages;
using ControlMenu.Modules.Imaging.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ControlMenu.Tests.Modules.Imaging;

/// <summary>
/// bUnit render smoke tests for the SVG Rasterize page. They assert the page renders its
/// shell, the PNG/ICO output-format radios exist, the size checkboxes exist, and the
/// background color input toggles with the Transparent checkbox. The image service is
/// mocked — these are UI tests, not Svg.Skia integration tests.
/// </summary>
public class SvgRasterizePageTests : BunitContext
{
    public SvgRasterizePageTests()
    {
        Services.AddSingleton(new Mock<IImageService>().Object);
        Services.AddSingleton<IWebHostEnvironment>(new Mock<IWebHostEnvironment>().Object);

        // OnAfterRenderAsync probes for the File System Access API.
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<bool>("hasFileSystemAccess", _ => true).SetResult(false);
    }

    [Fact]
    public void Renders_Heading_And_RasterizeButton()
    {
        var cut = Render<SvgRasterize>();

        Assert.Contains("SVG Rasterize", cut.Find("h1").TextContent);
        Assert.Contains("Rasterize", cut.Find("button.btn-primary").TextContent);
    }

    [Fact]
    public void OutputFormat_OffersPngAndIco()
    {
        var cut = Render<SvgRasterize>();

        var radioValues = cut.FindAll("input[type=radio]")
            .Select(r => r.GetAttribute("value"))
            .ToList();

        Assert.Contains("png", radioValues);
        Assert.Contains("ico", radioValues);
        Assert.Equal(2, radioValues.Count);
    }

    [Fact]
    public void SizeCheckboxes_CoverAllSupportedSizes()
    {
        var cut = Render<SvgRasterize>();

        // The Background "Transparent" checkbox shares the type=checkbox selector, so the
        // size checkboxes are the markup labelled "px". Seven sizes: 16/32/48/64/128/256/512.
        foreach (var size in new[] { "16px", "32px", "48px", "64px", "128px", "256px", "512px" })
        {
            Assert.Contains(size, cut.Markup);
        }
    }

    [Fact]
    public void BackgroundColorInput_TogglesWithTransparentCheckbox()
    {
        var cut = Render<SvgRasterize>();

        // Default is transparent → no color input rendered.
        Assert.Empty(cut.FindAll("input[type=color]"));

        // The Transparent checkbox is the last checkbox (after the 7 size checkboxes).
        var checkboxes = cut.FindAll("input[type=checkbox]");
        var transparent = checkboxes[checkboxes.Count - 1];
        transparent.Change(false);

        // Unchecking Transparent reveals the color picker.
        Assert.Single(cut.FindAll("input[type=color]"));
    }
}
