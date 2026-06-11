using ControlMenu.Modules.Imaging.Services;
using ControlMenu.Modules.Imaging.Services.Options;

namespace ControlMenu.Tests.Modules.Imaging;

/// <summary>
/// Integration tests for <see cref="ImageService.ConvertFormatAsync"/>. These drive the
/// REAL bundled magick.exe (via <see cref="ImageServiceFixture"/>): a PNG is encoded into
/// the target container and we re-identify the bytes with <see cref="ImageService.GetInfoAsync"/>
/// to assert the output really is the requested format (magick reports the format code via %m).
///
/// The bundled portable is a Q8 build whose compiled delegates determine which encoders are
/// actually available; policy.xml additionally restricts the allowlist. We assert the
/// universally-present raster encoders (JPG/WEBP/TIFF) directly, and treat the
/// libheif-backed AVIF as best-effort (works or throws cleanly — documented, never silently
/// asserted as working).
/// </summary>
[Collection(nameof(ImageServiceCollection))]
public class ImageServiceConvertFormatTests
{
    private readonly ImageServiceFixture _fx;

    public ImageServiceConvertFormatTests(ImageServiceFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task ConvertFormatAsync_PngToJpg_ProducesJpeg()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var png = TestImages.CreatePng(256, 128);

        byte[] outBytes = await _fx.Service.ConvertFormatAsync(png, "jpg");

        Assert.NotEmpty(outBytes);
        ImageInfo info = await _fx.Service.GetInfoAsync(outBytes);
        // magick's %m reports "JPEG" for the JPEG container regardless of .jpg/.jpeg extension.
        Assert.Equal("JPEG", info.Format);
        Assert.Equal(256, info.Width);
        Assert.Equal(128, info.Height);
    }

    [SkippableFact]
    public async Task ConvertFormatAsync_PngToWebp_ProducesWebp()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var png = TestImages.CreatePng(200, 100);

        byte[] outBytes = await _fx.Service.ConvertFormatAsync(png, "webp");

        Assert.NotEmpty(outBytes);
        ImageInfo info = await _fx.Service.GetInfoAsync(outBytes);
        Assert.Equal("WEBP", info.Format);
        Assert.Equal(200, info.Width);
        Assert.Equal(100, info.Height);
    }

    [SkippableFact]
    public async Task ConvertFormatAsync_PngToTiff_ProducesTiff()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var png = TestImages.CreatePng(64, 64);

        byte[] outBytes = await _fx.Service.ConvertFormatAsync(png, "tiff");

        Assert.NotEmpty(outBytes);
        ImageInfo info = await _fx.Service.GetInfoAsync(outBytes);
        Assert.Equal("TIFF", info.Format);
    }

    [SkippableFact]
    public async Task ConvertFormatAsync_NormalizesTargetFormat_LeadingDotAndCase()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var png = TestImages.CreatePng(48, 48);

        // ".JPG" with a leading dot, upper-case, surrounding whitespace must normalize to jpg.
        byte[] outBytes = await _fx.Service.ConvertFormatAsync(png, "  .JPG  ");

        ImageInfo info = await _fx.Service.GetInfoAsync(outBytes);
        Assert.Equal("JPEG", info.Format);
    }

    [SkippableFact]
    public async Task ConvertFormatAsync_RespectsQualityOption_SmallerAtLowQuality()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        // A larger image so the quantizer has room to make a size difference.
        var png = TestImages.CreatePng(512, 512);

        byte[] high = await _fx.Service.ConvertFormatAsync(png, "jpg", new ConvertFormatOptions { Quality = 95 });
        byte[] low = await _fx.Service.ConvertFormatAsync(png, "jpg", new ConvertFormatOptions { Quality = 10 });

        // Both must be real JPEGs...
        Assert.Equal("JPEG", (await _fx.Service.GetInfoAsync(high)).Format);
        Assert.Equal("JPEG", (await _fx.Service.GetInfoAsync(low)).Format);
        // ...and -quality must actually be wired through: q10 < q95.
        Assert.True(low.Length < high.Length,
            $"Expected low-quality JPEG ({low.Length} B) to be smaller than high-quality ({high.Length} B)");
    }

    /// <summary>
    /// AVIF is served by the libheif delegate. Whether THIS portable Q8 build can ENCODE AVIF
    /// depends on libheif having an AV1 encoder compiled in. We do not assert it works — we
    /// assert it either round-trips as AVIF or fails with our typed <see cref="ImagingException"/>
    /// (never an unhandled crash). The test summary documents which path this build takes.
    /// </summary>
    [SkippableFact]
    public async Task ConvertFormatAsync_Avif_WorksOrThrowsImagingException()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var png = TestImages.CreatePng(96, 96);

        try
        {
            byte[] outBytes = await _fx.Service.ConvertFormatAsync(png, "avif");
            ImageInfo info = await _fx.Service.GetInfoAsync(outBytes);
            // If we got here the encoder exists — make sure it's genuinely AVIF (HEIF family).
            Assert.True(
                info.Format is "AVIF" or "HEIC" or "HEIF",
                $"AVIF conversion returned unexpected format '{info.Format}'");
        }
        catch (ImagingException)
        {
            // Acceptable: this Q8 portable has no AVIF encoder delegate. Documented, not a failure.
        }
    }
}
