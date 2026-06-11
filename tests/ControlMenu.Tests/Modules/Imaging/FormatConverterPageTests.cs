using Bunit;
using ControlMenu.Modules.Imaging.Pages;
using ControlMenu.Modules.Imaging.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ControlMenu.Tests.Modules.Imaging;

/// <summary>
/// bUnit render smoke tests for the Format Converter page. They assert the page renders its
/// shell, the target-format dropdown carries exactly the verified-encoder options (and NOT
/// HEIC), and the quality slider only appears for lossy targets. The image service is mocked
/// — these are UI tests, not magick integration tests.
/// </summary>
public class FormatConverterPageTests : BunitContext
{
    public FormatConverterPageTests()
    {
        Services.AddSingleton(new Mock<IImageService>().Object);
        Services.AddSingleton<IWebHostEnvironment>(new Mock<IWebHostEnvironment>().Object);

        // OnAfterRenderAsync probes for the File System Access API.
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<bool>("hasFileSystemAccess", _ => true).SetResult(false);
    }

    [Fact]
    public void Renders_Heading_And_ConvertButton()
    {
        var cut = Render<FormatConverter>();

        Assert.Contains("Format Converter", cut.Find("h1").TextContent);
        Assert.Contains("Convert", cut.Find("button.btn-primary").TextContent);
    }

    [Fact]
    public void TargetDropdown_ListsVerifiedEncoders_AndExcludesHeic()
    {
        var cut = Render<FormatConverter>();

        var options = cut.FindAll("select.form-control option")
            .Select(o => o.TextContent.Trim())
            .ToList();

        foreach (var fmt in new[] { "PNG", "JPG", "WEBP", "AVIF", "TIFF", "BMP", "GIF" })
        {
            Assert.Contains(fmt, options);
        }

        Assert.DoesNotContain("HEIC", options);
        Assert.Equal(7, options.Count);
    }

    [Fact]
    public void QualitySlider_HiddenForPng_ShownForLossy()
    {
        var cut = Render<FormatConverter>();

        // Default target is PNG (lossless) — no slider.
        Assert.Empty(cut.FindAll("input.quality-slider"));

        // Switch to JPG (lossy) — slider appears.
        cut.Find("select.form-control").Change("JPG");
        Assert.Single(cut.FindAll("input.quality-slider"));
    }
}
