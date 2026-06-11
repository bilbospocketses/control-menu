using Bunit;
using ControlMenu.Modules.Imaging.Pages;
using ControlMenu.Modules.Imaging.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ControlMenu.Tests.Modules.Imaging;

/// <summary>
/// bUnit render smoke tests for the Tracing page. They assert the page renders its shell, the
/// Color/B&amp;W engine radios exist, switching engine swaps the option controls (vtracer's
/// Color/Curve mode selects vs potrace's Threshold slider + Invert checkbox), Trace is disabled
/// until a file is loaded, and the disabled "Open in svgedit" placeholder is absent until a
/// trace produces a preview. The tracing service is mocked — these are UI tests, not
/// vtracer/potrace integration tests.
/// </summary>
public class TracingPageTests : BunitContext
{
    public TracingPageTests()
    {
        Services.AddSingleton(new Mock<ITracingService>().Object);
        Services.AddSingleton<IWebHostEnvironment>(new Mock<IWebHostEnvironment>().Object);

        // OnAfterRenderAsync probes for the File System Access API.
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<bool>("hasFileSystemAccess", _ => true).SetResult(false);
    }

    [Fact]
    public void Renders_Heading_And_TraceButton()
    {
        var cut = Render<Tracing>();

        Assert.Contains("Tracing", cut.Find("h1").TextContent);
        Assert.Contains("Trace", cut.Find("button.btn-primary").TextContent);
    }

    [Fact]
    public void EngineSelector_OffersColorAndBlackWhite()
    {
        var cut = Render<Tracing>();

        var radioValues = cut.FindAll("input[type=radio]")
            .Select(r => r.GetAttribute("value"))
            .ToList();

        Assert.Contains("color", radioValues);
        Assert.Contains("bw", radioValues);
        Assert.Equal(2, radioValues.Count);
    }

    [Fact]
    public void SwitchingEngine_SwapsOptionControls()
    {
        var cut = Render<Tracing>();

        // Default engine is Color (vtracer): three <select> controls (Color Mode, Hierarchical,
        // Curve Mode) and NO threshold slider.
        Assert.Equal(3, cut.FindAll("select.form-control").Count);
        Assert.Empty(cut.FindAll("input.threshold-slider"));

        // Switch to Black & White (potrace): the selects give way to the threshold slider and
        // the Invert / Optimize checkboxes.
        var bwRadio = cut.FindAll("input[type=radio]").First(r => r.GetAttribute("value") == "bw");
        bwRadio.Change("bw");

        Assert.Empty(cut.FindAll("select.form-control"));
        Assert.Single(cut.FindAll("input.threshold-slider"));
        // potrace block has the two boolean knobs (Optimize curves + Invert).
        Assert.Equal(2, cut.FindAll("input[type=checkbox]").Count);
    }

    [Fact]
    public void TraceButton_DisabledUntilFileLoaded()
    {
        var cut = Render<Tracing>();

        // No file selected yet (FSA mock reports unavailable, so we're in path-input mode with an
        // empty path) — Trace must be disabled.
        var traceBtn = cut.Find("button.btn-primary");
        Assert.True(traceBtn.HasAttribute("disabled"));

        // Typing a source path enables Trace (path mode reads bytes lazily at trace time).
        cut.Find("input.form-control[type=text]").Change("C:\\path\\to\\image.png");
        Assert.False(cut.Find("button.btn-primary").HasAttribute("disabled"));
    }

    [Fact]
    public void SvgeditStub_PresentAndDisabled()
    {
        var cut = Render<Tracing>();

        // The "Open in svgedit" placeholder is always rendered but inert until the future
        // svgedit-integration task wires it up. Assert it exists and is disabled.
        var svgeditBtn = cut.FindAll("button")
            .Single(b => b.TextContent.Contains("Open in svgedit"));

        Assert.True(svgeditBtn.HasAttribute("disabled"));
    }
}
