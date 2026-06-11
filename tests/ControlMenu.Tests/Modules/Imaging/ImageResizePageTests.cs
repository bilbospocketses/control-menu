using Bunit;
using ControlMenu.Modules.Imaging.Pages;
using ControlMenu.Modules.Imaging.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ControlMenu.Tests.Modules.Imaging;

/// <summary>
/// bUnit render smoke tests for the Image Resize page. They assert the page renders its
/// shell and the three resize-mode inputs swap the visible controls (pixel dimensions /
/// percentage / max dimension). The image service is mocked — these are UI tests, not
/// magick integration tests.
/// </summary>
public class ImageResizePageTests : BunitContext
{
    public ImageResizePageTests()
    {
        Services.AddSingleton(new Mock<IImageService>().Object);
        Services.AddSingleton<IWebHostEnvironment>(new Mock<IWebHostEnvironment>().Object);

        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<bool>("hasFileSystemAccess", _ => true).SetResult(false);
    }

    [Fact]
    public void Renders_Heading_And_ResizeButton()
    {
        var cut = Render<ImageResize>();

        Assert.Contains("Image Resize", cut.Find("h1").TextContent);
        Assert.Contains("Resize", cut.Find("button.btn-primary").TextContent);
    }

    [Fact]
    public void DefaultMode_PixelDimensions_ShowsWidthAndHeight()
    {
        var cut = Render<ImageResize>();

        // Pixel mode is default: width + height number inputs are present.
        var numberInputs = cut.FindAll("input[type=number]");
        Assert.Equal(2, numberInputs.Count);
        Assert.Contains("Width", cut.Markup);
        Assert.Contains("Height", cut.Markup);
    }

    [Fact]
    public void PercentageMode_ShowsSinglePercentageInput()
    {
        var cut = Render<ImageResize>();

        // Second radio is Percentage.
        var radios = cut.FindAll("input[type=radio]");
        radios[1].Change(ControlMenu.Modules.Imaging.Services.Options.ResizeMode.Percentage.ToString());

        Assert.Single(cut.FindAll("input[type=number]"));
        Assert.Contains("Percentage", cut.Markup);
    }

    [Fact]
    public void MaxDimensionMode_ShowsSingleMaxInput()
    {
        var cut = Render<ImageResize>();

        var radios = cut.FindAll("input[type=radio]");
        radios[2].Change(ControlMenu.Modules.Imaging.Services.Options.ResizeMode.MaxDimensionFit.ToString());

        Assert.Single(cut.FindAll("input[type=number]"));
        Assert.Contains("Max dimension", cut.Markup);
    }
}
