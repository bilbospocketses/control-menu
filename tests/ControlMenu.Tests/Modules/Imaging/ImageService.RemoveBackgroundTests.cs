using ControlMenu.Modules.Imaging.Services;
using ControlMenu.Modules.Imaging.Services.Options;
using SkiaSharp;

namespace ControlMenu.Tests.Modules.Imaging;

/// <summary>
/// Integration tests for <see cref="ImageService.RemoveBackgroundAsync"/>. These drive the
/// REAL bundled magick.exe (via <see cref="ImageServiceFixture"/>) under our hardened
/// policy.xml, which DENIES the MVG coder — so the implementation must use the
/// <c>-floodfill</c>/<c>-transparent</c> OPERATORS (not <c>-draw "... floodfill"</c>, which
/// compiles to MVG and is policy-blocked). We build a known image in-process with SkiaSharp
/// (opaque white background, solid red center square), run removal, then decode the output
/// PNG with <see cref="SKBitmap.Decode"/> and assert real per-pixel alpha:
///   * the seeded background pixel becomes fully transparent (alpha == 0)
///   * the differently-coloured center stays opaque (alpha != 0)
///
/// Test geometry (64x64): background = white everywhere except a red square spanning
/// x,y in [24, 40). Corner (2,2) is background; center (32,32) is red.
/// </summary>
[Collection(nameof(ImageServiceCollection))]
public class ImageServiceRemoveBackgroundTests
{
    private readonly ImageServiceFixture _fx;

    public ImageServiceRemoveBackgroundTests(ImageServiceFixture fx) => _fx = fx;

    private const int Size = 64;
    private const int SquareLo = 24;
    private const int SquareHi = 40; // exclusive

    /// <summary>
    /// Opaque white canvas with a solid red square in the middle. Encoded as PNG bytes.
    /// The white is contiguous (the red square is an island), so a corner-seeded contiguous
    /// flood reaches the whole border without touching the red center.
    /// </summary>
    private static byte[] CreateWhiteBgRedCenter()
    {
        var info = new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Red, IsAntialias = false, Style = SKPaintStyle.Fill };
            canvas.DrawRect(new SKRect(SquareLo, SquareLo, SquareHi, SquareHi), paint);
            canvas.Flush();
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Two disjoint white regions split by a full-width red band, so the corner-seeded
    /// contiguous flood can reach only the top region. Top band y in [0, 20); red band
    /// y in [20, 44); bottom band y in [44, 64). Used by the non-contiguous test.
    /// </summary>
    private static byte[] CreateDisjointWhiteBands()
    {
        var info = new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Red, IsAntialias = false, Style = SKPaintStyle.Fill };
            canvas.DrawRect(new SKRect(0, 20, Size, 44), paint); // full-width red band
            canvas.Flush();
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [SkippableFact]
    public async Task RemoveBackgroundAsync_Contiguous_MakesSeededBackgroundTransparent()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var src = CreateWhiteBgRedCenter();

        byte[] output = await _fx.Service.RemoveBackgroundAsync(src, new BackgroundRemoveOptions
        {
            SeedX = 2, // a white background pixel near the corner
            SeedY = 2,
            Tolerance = 10,
            Contiguous = true,
        });

        using var bmp = SKBitmap.Decode(output);
        Assert.NotNull(bmp);

        // Seeded background corner is now transparent.
        Assert.Equal(0, bmp.GetPixel(2, 2).Alpha);
        // Another background pixel on the opposite side is also reached by the flood.
        Assert.Equal(0, bmp.GetPixel(Size - 3, Size - 3).Alpha);
        // The red center square is a different colour and is kept opaque.
        var center = bmp.GetPixel(32, 32);
        Assert.NotEqual(0, center.Alpha);
        Assert.Equal(SKColors.Red.Red, center.Red);
    }

    [SkippableFact]
    public async Task RemoveBackgroundAsync_NonContiguous_ClearsAllMatchingRegions()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var src = CreateDisjointWhiteBands();

        byte[] output = await _fx.Service.RemoveBackgroundAsync(src, new BackgroundRemoveOptions
        {
            SeedX = 2, // top white band
            SeedY = 2,
            Tolerance = 10,
            Contiguous = false,
        });

        using var bmp = SKBitmap.Decode(output);
        Assert.NotNull(bmp);

        // Both disjoint white bands are cleared (global colour match, not just the seed region).
        Assert.Equal(0, bmp.GetPixel(2, 2).Alpha);        // top band
        Assert.Equal(0, bmp.GetPixel(32, 60).Alpha);      // bottom band (disjoint from seed)
        // The red band in the middle is a different colour and is kept opaque.
        Assert.NotEqual(0, bmp.GetPixel(32, 32).Alpha);
    }

    [SkippableFact]
    public async Task RemoveBackgroundAsync_OutOfBoundsSeed_Throws()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var src = CreateWhiteBgRedCenter();

        await Assert.ThrowsAsync<ArgumentException>(
            () => _fx.Service.RemoveBackgroundAsync(src, new BackgroundRemoveOptions
            {
                SeedX = Size + 100, // outside the image bounds
                SeedY = 2,
            }));
    }
}
