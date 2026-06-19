using Bunit;
using ControlMenu.Modules.Imaging.Pages;
using ControlMenu.Modules.Imaging.Services;
using ControlMenu.Modules.Imaging.Services.Options;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SkiaSharp;

// FilePickerResult lives in ControlMenu.Modules.Imaging.Pages (imported above).

namespace ControlMenu.Tests.Modules.Imaging;

/// <summary>
/// bUnit render smoke tests for the Magic Wand page. They assert the page renders its shell, that
/// Apply is gated until a seed is set, and — after a source image is loaded via the (mocked) file
/// picker — that the tolerance slider and contiguous checkbox appear. The image service is mocked
/// (GetInfoAsync returns a fixed ImageInfo); these are UI tests, not magick integration tests.
/// </summary>
public class MagicWandPageTests : BunitContext, IDisposable
{
    private readonly Mock<IImageService> _svc = new();
    private readonly string _webRoot;

    public MagicWandPageTests()
    {
        // The page writes a temp web-copy of the source under WebRootPath when a source loads, so
        // we point WebRootPath at a real throwaway directory.
        _webRoot = Path.Combine(Path.GetTempPath(), "cm-wand-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_webRoot);

        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.WebRootPath).Returns(_webRoot);

        Services.AddSingleton(_svc.Object);
        Services.AddSingleton(env.Object);

        // OnAfterRenderAsync probes for the File System Access API. Loose mode lets unconfigured
        // calls (e.g. getElementRect) no-op rather than throw.
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<bool>("hasFileSystemAccess", _ => true).SetResult(true);
    }

    /// <summary>A tiny real PNG so the page's decode/info path has valid bytes to work with.</summary>
    private static (string Base64, byte[] Bytes) TinyPng(int w = 16, int h = 16)
    {
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bmp = new SKBitmap(info);
        bmp.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = data.ToArray();
        return (Convert.ToBase64String(bytes), bytes);
    }

    [Fact]
    public void Renders_Heading_And_ApplyButton_NoSource()
    {
        var cut = Render<MagicWand>();

        Assert.Contains("Magic Wand", cut.Find("h1").TextContent);
        // With no source loaded, the Apply button is not rendered yet.
        Assert.Empty(cut.FindAll("button.btn-primary"));
        // The select-image control is present (File System Access mode).
        Assert.Contains("Select Image", cut.Markup);
    }

    [Fact]
    public void AfterSourceLoaded_ShowsToleranceSlider_AndContiguousCheckbox()
    {
        var (b64, _) = TinyPng();
        JSInterop.Setup<FilePickerResult?>("filePickerOpen", _ => true)
            .SetResult(new FilePickerResult("logo.png", b64));

        _svc.Setup(s => s.GetInfoAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageInfo(16, 16, "PNG", false, 256));

        var cut = Render<MagicWand>();

        // Drive the file picker → loads source → controls appear.
        cut.Find("button.btn-secondary").Click();
        cut.WaitForState(() => cut.FindAll("input.tolerance-slider").Count > 0, TimeSpan.FromSeconds(2));

        Assert.Single(cut.FindAll("input.tolerance-slider"));
        Assert.Single(cut.FindAll("input[type=checkbox]")); // the Contiguous checkbox
    }

    [Fact]
    public void Apply_IsDisabled_UntilSeedSet()
    {
        var (b64, _) = TinyPng();
        JSInterop.Setup<FilePickerResult?>("filePickerOpen", _ => true)
            .SetResult(new FilePickerResult("logo.png", b64));

        _svc.Setup(s => s.GetInfoAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageInfo(16, 16, "PNG", false, 256));

        var cut = Render<MagicWand>();
        cut.Find("button.btn-secondary").Click();
        cut.WaitForState(() => cut.FindAll("button.btn-primary").Count > 0, TimeSpan.FromSeconds(2));

        // No seed yet → Apply disabled.
        var apply = cut.Find("button.btn-primary");
        Assert.True(apply.HasAttribute("disabled"));
    }

    [Fact]
    public void LivePreview_ServedViaTempUrl_NotBase64DataUrlOverCircuit()
    {
        var (b64, _) = TinyPng();
        JSInterop.Setup<FilePickerResult?>("filePickerOpen", _ => true)
            .SetResult(new FilePickerResult("logo.png", b64));
        _svc.Setup(s => s.GetInfoAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageInfo(16, 16, "PNG", false, 256));
        // Click-to-seed reads the element rect; a 1:1 natural/rendered mapping keeps the math simple.
        JSInterop.Setup<ElementRect?>("getElementRect", _ => true)
            .SetResult(new ElementRect(16, 16, 16, 16));

        var cut = Render<MagicWand>();
        cut.Find("button.btn-secondary").Click();
        cut.WaitForState(() => cut.FindAll("img.wand-image").Count > 0, TimeSpan.FromSeconds(2));

        var sourceSrc = cut.Find("img.wand-image").GetAttribute("src");
        // Click a pixel → seeds → fires the live flood-fill preview.
        cut.Find("img.wand-image").Click(new MouseEventArgs { OffsetX = 5, OffsetY = 5 });
        cut.WaitForState(() => cut.Find("img.wand-image").GetAttribute("src") != sourceSrc, TimeSpan.FromSeconds(5));

        var previewSrc = cut.Find("img.wand-image").GetAttribute("src")!;
        // The preview must be served off the circuit via a /temp web-copy, not a full base64 data: URL.
        Assert.StartsWith("/temp/", previewSrc);
        Assert.DoesNotContain("data:", previewSrc);
    }

    [Fact]
    public void Apply_PreviewAndDownload_ServedViaTempUrl_NotBase64DataUrlOverCircuit()
    {
        var (b64, _) = TinyPng();
        JSInterop.Setup<FilePickerResult?>("filePickerOpen", _ => true)
            .SetResult(new FilePickerResult("logo.png", b64));
        _svc.Setup(s => s.GetInfoAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageInfo(16, 16, "PNG", false, 256));
        JSInterop.Setup<ElementRect?>("getElementRect", _ => true)
            .SetResult(new ElementRect(16, 16, 16, 16));
        // Apply: magick returns a PNG; the native save returns a path so the result panel renders.
        var (_, outBytes) = TinyPng(8, 8);
        _svc.Setup(s => s.RemoveBackgroundAsync(It.IsAny<byte[]>(), It.IsAny<BackgroundRemoveOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outBytes);
        JSInterop.Setup<string?>("filePickerSaveAs", _ => true).SetResult("C:\\out\\logo-nobg.png");

        var cut = Render<MagicWand>();
        cut.Find("button.btn-secondary").Click();
        cut.WaitForState(() => cut.FindAll("img.wand-image").Count > 0, TimeSpan.FromSeconds(2));
        cut.Find("img.wand-image").Click(new MouseEventArgs { OffsetX = 5, OffsetY = 5 });
        cut.WaitForState(() => !cut.Find("button.btn-primary").HasAttribute("disabled"), TimeSpan.FromSeconds(5));

        cut.Find("button.btn-primary").Click(); // Apply & Save
        cut.WaitForState(() => cut.FindAll("a.btn-pill-success").Count > 0, TimeSpan.FromSeconds(5));

        var previewSrc = cut.Find("img.wand-image").GetAttribute("src")!;
        var downloadHref = cut.Find("a.btn-pill-success").GetAttribute("href")!;
        // Both the authoritative result preview and the download link come off the circuit via /temp.
        Assert.StartsWith("/temp/", previewSrc);
        Assert.DoesNotContain("data:", previewSrc);
        Assert.StartsWith("/temp/", downloadHref);
    }

    public new void Dispose()
    {
        try { if (Directory.Exists(_webRoot)) Directory.Delete(_webRoot, recursive: true); } catch { }
        base.Dispose();
    }
}
