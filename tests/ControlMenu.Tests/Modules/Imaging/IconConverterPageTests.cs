using Bunit;
using ControlMenu.Modules.Imaging.Pages;
using ControlMenu.Modules.Imaging.Services;
using ControlMenu.Modules.Imaging.Services.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ControlMenu.Tests.Modules.Imaging;

/// <summary>
/// bUnit tests for the Icon Converter page. Render smokes assert the shell + the size checkboxes;
/// the temp-file accounting test drives the page in path mode (a real source file on disk, so it
/// never touches the JS file picker), points WebRootPath at a throwaway directory, and asserts the
/// page's /temp download copies don't accumulate across conversions and are cleared on Dispose.
/// </summary>
public class IconConverterPageTests : BunitContext, IDisposable
{
    private readonly Mock<IImageService> _svc = new();
    private readonly string _webRoot;

    public IconConverterPageTests()
    {
        // Convert publishes a /temp download copy under WebRootPath, so point it at a real
        // throwaway directory the test owns and cleans up.
        _webRoot = Path.Combine(Path.GetTempPath(), "cm-icon-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_webRoot);

        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.WebRootPath).Returns(_webRoot);

        Services.AddSingleton(_svc.Object);
        Services.AddSingleton(env.Object);

        // OnAfterRenderAsync probes for the File System Access API. False = path mode (the text-box
        // input), which keeps these tests off the JS file picker.
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<bool>("hasFileSystemAccess", _ => true).SetResult(false);
    }

    [Fact]
    public void Renders_Heading_And_ConvertButton()
    {
        var cut = Render<IconConverter>();

        Assert.Contains("Icon Converter", cut.Find("h1").TextContent);
        Assert.Contains("Convert to ICO", cut.Find("button.btn-primary").TextContent);
    }

    [Fact]
    public void SizeCheckboxes_CoverAllSupportedSizes()
    {
        var cut = Render<IconConverter>();

        // Six sizes: 16/32/48/64/128/256.
        foreach (var size in new[] { "16px", "32px", "48px", "64px", "128px", "256px" })
        {
            Assert.Contains(size, cut.Markup);
        }
    }

    [Fact]
    public void DownloadCopies_DoNotAccumulate_AndDisposeClearsTempFiles()
    {
        // Path mode: a real source file on disk, converted twice. ConvertToIcoAsync is mocked, so
        // the returned bytes are opaque — only the /temp download-copy accounting matters here.
        var srcDir = Path.Combine(_webRoot, "src");
        Directory.CreateDirectory(srcDir);
        var srcPath = Path.Combine(srcDir, "logo.png");
        File.WriteAllBytes(srcPath, [1, 2, 3, 4]);

        _svc.Setup(s => s.ConvertToIcoAsync(It.IsAny<byte[]>(), It.IsAny<int[]>(), It.IsAny<IcoOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([9, 9, 9, 9]);

        var cut = Render<IconConverter>();
        // Path mode renders the path text-box; typing the path sets the source.
        cut.Find("input.form-control").Change(srcPath);

        var tempDir = Path.Combine(_webRoot, "temp");

        // First convert → exactly one download web-copy.
        cut.Find("button.btn-primary").Click();
        cut.WaitForState(() => cut.FindAll("a.btn-pill-success").Count > 0, TimeSpan.FromSeconds(5));
        var href1 = cut.Find("a.btn-pill-success").GetAttribute("href");

        // Second convert → the prior copy must be replaced, not accumulated (would be 2 if it leaked).
        // Convert() clears the result panel at its start, so wait via FindAll (Find would throw during
        // that transient window) until the link reappears with a fresh href.
        cut.Find("button.btn-primary").Click();
        cut.WaitForState(() =>
        {
            var link = cut.FindAll("a.btn-pill-success").FirstOrDefault();
            return link is not null && link.GetAttribute("href") != href1;
        }, TimeSpan.FromSeconds(5));

        Assert.Single(Directory.GetFiles(tempDir));

        // Disposing the component (what the framework does on navigate-away) deletes its tracked
        // /temp copy rather than leaving it for the 5-minute fallback timer. Idempotent, so the
        // context's later teardown is a no-op.
        ((IDisposable)cut.Instance).Dispose();
        Assert.Empty(Directory.GetFiles(tempDir));
    }

    public new void Dispose()
    {
        try { if (Directory.Exists(_webRoot)) Directory.Delete(_webRoot, recursive: true); } catch { }
        base.Dispose();
    }
}
