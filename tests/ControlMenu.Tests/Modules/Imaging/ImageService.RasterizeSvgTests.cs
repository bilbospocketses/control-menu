using System.Buffers.Binary;
using ControlMenu.Modules.Imaging.Services;
using ControlMenu.Modules.Imaging.Services.Options;
using SkiaSharp;

namespace ControlMenu.Tests.Modules.Imaging;

/// <summary>
/// Tests for <see cref="ImageService.RasterizeSvgAsync"/>. The SVG is rasterized
/// IN-PROCESS via Svg.Skia (managed, no magick) — the security stance keeps SVG out
/// of magick's parser entirely. magick is only reached for the "ico" output path,
/// where the already-rasterized PNG is bundled via <see cref="ImageService.ConvertToIcoAsync"/>.
/// So only the ico test is gated on <see cref="ImageServiceFixture.MagickAvailable"/>;
/// the pure-render tests run unconditionally.
///
/// ICO header layout asserted by the ico test (see also ImageService.IconTests.cs):
///   offset 0 (uint16 LE): reserved, must be 0
///   offset 2 (uint16 LE): image type, 1 = icon
///   offset 4 (uint16 LE): number of directory entries
/// </summary>
[Collection(nameof(ImageServiceCollection))]
public class ImageServiceRasterizeSvgTests
{
    private readonly ImageServiceFixture _fx;

    public ImageServiceRasterizeSvgTests(ImageServiceFixture fx) => _fx = fx;

    // A valid SVG that fully covers its 100x100 viewport (red square + blue circle).
    private const string FullCoverSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\">" +
        "<rect width=\"100\" height=\"100\" fill=\"red\"/>" +
        "<circle cx=\"50\" cy=\"50\" r=\"30\" fill=\"blue\"/></svg>";

    // A valid SVG with only a centered circle, so the viewport corners are NOT painted
    // by the SVG content — they reveal whatever background the rasterizer cleared to.
    private const string CenteredCircleSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\">" +
        "<circle cx=\"50\" cy=\"50\" r=\"30\" fill=\"blue\"/></svg>";

    private static byte[] Utf8(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    private static ushort Reserved(byte[] ico) => BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(0, 2));
    private static ushort IconType(byte[] ico) => BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2, 2));
    private static ushort EntryCount(byte[] ico) => BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4, 2));

    [Fact]
    public async Task RasterizeSvgAsync_Png_ProducesPngOfLargestSize()
    {
        var options = new RasterizeOptions { Sizes = [128, 256], OutputFormat = "png" };

        byte[] png = await _fx.Service.RasterizeSvgAsync(Utf8(FullCoverSvg), options);

        // Decode in-process (no magick needed) and assert format + dimensions.
        using var decoded = SKBitmap.Decode(png);
        Assert.NotNull(decoded);
        Assert.Equal(256, decoded.Width);   // largest of [128,256]
        Assert.Equal(256, decoded.Height);

        // Also confirm it's a real PNG via codec detection.
        using var codec = SKCodec.Create(new MemoryStream(png));
        Assert.Equal(SKEncodedImageFormat.Png, codec.EncodedFormat);
    }

    [SkippableFact]
    public async Task RasterizeSvgAsync_Ico_ProducesValidIco()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var options = new RasterizeOptions { Sizes = [32, 64, 128], OutputFormat = "ico" };

        byte[] ico = await _fx.Service.RasterizeSvgAsync(Utf8(FullCoverSvg), options);

        Assert.True(ico.Length > 22, $"ICO too small: {ico.Length} bytes");
        Assert.Equal(0, Reserved(ico));    // reserved
        Assert.Equal(1, IconType(ico));    // type = icon
        Assert.Equal(3, EntryCount(ico));  // three sizes -> three directory entries
    }

    [Fact]
    public async Task RasterizeSvgAsync_OpaqueBackground_FillsBackground()
    {
        var options = new RasterizeOptions
        {
            Sizes = [256],
            OutputFormat = "png",
            Background = "#ffffff",
        };

        // Centered-circle SVG: the corners are untouched by SVG paint, so they must show
        // the opaque white background we cleared to.
        byte[] png = await _fx.Service.RasterizeSvgAsync(Utf8(CenteredCircleSvg), options);

        using var bmp = SKBitmap.Decode(png);
        Assert.NotNull(bmp);
        var corner = bmp.GetPixel(2, 2);   // a few px in from the top-left corner
        Assert.Equal(255, corner.Alpha);   // opaque
        Assert.Equal(255, corner.Red);
        Assert.Equal(255, corner.Green);
        Assert.Equal(255, corner.Blue);
    }

    [Fact]
    public async Task RasterizeSvgAsync_InvalidSvg_Throws()
    {
        var garbage = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0x10, 0x20 };
        var options = new RasterizeOptions { Sizes = [64], OutputFormat = "png" };

        await Assert.ThrowsAsync<ImagingException>(
            () => _fx.Service.RasterizeSvgAsync(garbage, options));
    }

    [Fact]
    public async Task RasterizeSvgAsync_EmptySizes_Throws()
    {
        var options = new RasterizeOptions { Sizes = [], OutputFormat = "png" };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _fx.Service.RasterizeSvgAsync(Utf8(FullCoverSvg), options));
    }
}
