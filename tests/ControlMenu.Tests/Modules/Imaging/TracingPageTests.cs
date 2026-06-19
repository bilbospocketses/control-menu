using Bunit;
using ControlMenu.Modules.Imaging.Pages;
using ControlMenu.Modules.Imaging.Services;
using ControlMenu.Modules.Imaging.Services.Options;
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
public class TracingPageTests : BunitContext, IDisposable
{
    private readonly Mock<ITracingService> _svc = new();
    private readonly string _webRoot;

    public TracingPageTests()
    {
        // A real WebRootPath so the (pre-fix) /temp web-copy path has somewhere to write; the
        // #11 fix removes that write, but the test must let the old behaviour run to go RED.
        _webRoot = Path.Combine(Path.GetTempPath(), "cm-tracing-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_webRoot);
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.WebRootPath).Returns(_webRoot);

        Services.AddSingleton(_svc.Object);
        Services.AddSingleton(env.Object);

        // OnAfterRenderAsync probes for the File System Access API.
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<bool>("hasFileSystemAccess", _ => true).SetResult(false);
    }

    public new void Dispose()
    {
        try { if (Directory.Exists(_webRoot)) Directory.Delete(_webRoot, recursive: true); } catch { }
        base.Dispose();
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

    [Fact]
    public async Task Trace_PreviewAndDownload_UseInertSvgDataUrl_NoNavigableTempSvg()
    {
        // Path-input mode (FSA mock reports unavailable) with a real source file on disk.
        var srcDir = Path.Combine(_webRoot, "src");
        Directory.CreateDirectory(srcDir);
        var srcPath = Path.Combine(srcDir, "pic.png");
        await File.WriteAllBytesAsync(srcPath, new byte[] { 1, 2, 3 }); // opaque to the mocked tracer

        var svgBytes = System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1\" height=\"1\"/></svg>");
        _svc.Setup(s => s.TraceColorAsync(It.IsAny<byte[]>(), It.IsAny<TraceColorOptions>()))
            .ReturnsAsync(svgBytes);

        var cut = Render<Tracing>();
        cut.Find("input.form-control[type=text]").Change(srcPath);
        cut.Find("button.btn-primary").Click();
        cut.WaitForState(() => cut.FindAll("img.trace-image").Count > 0, TimeSpan.FromSeconds(5));

        // Preview must be an inert SVG data: URL — never a navigable same-origin /temp .svg.
        var previewSrc = cut.Find("img.trace-image").GetAttribute("src")!;
        Assert.StartsWith("data:image/svg+xml", previewSrc);
        Assert.DoesNotContain("/temp/", previewSrc);

        // The Download Copy link is likewise an inert data: URL (forces save, opaque origin).
        var downloadHref = cut.Find("a.btn-pill-success").GetAttribute("href")!;
        Assert.StartsWith("data:image/svg+xml", downloadHref);

        // The stored-XSS primitive is gone: no navigable .svg was written under wwwroot/temp.
        var tempDir = Path.Combine(_webRoot, "temp");
        var strayCount = Directory.Exists(tempDir) ? Directory.GetFiles(tempDir, "*.svg").Length : 0;
        Assert.Equal(0, strayCount);
    }
}
