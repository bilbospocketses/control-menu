using ControlMenu.Modules.Imaging.Services;
using SkiaSharp;

namespace ControlMenu.Tests.Modules.Imaging;

/// <summary>
/// Unit tests for <see cref="FloodFillPreviewEngine"/>, the in-process SkiaSharp flood-fill that
/// powers the Magic Wand LIVE PREVIEW (an approximation of the authoritative magick render). These
/// are pure, in-process tests — NO magick.exe, so no MagickAvailable gate.
///
/// Test geometry (64x64): an opaque white frame with a solid red square island in the middle
/// (x,y in [24,40)). A corner-seeded contiguous flood reaches the whole white frame but not the
/// red island; a global flood clears every white pixel anywhere. A second image with two disjoint
/// white bands separated by a red band exercises the contiguous-vs-global difference directly.
/// </summary>
public class FloodFillPreviewEngineTests
{
    private const int Size = 64;
    private const int SquareLo = 24;
    private const int SquareHi = 40; // exclusive

    /// <summary>Opaque white canvas with a solid red square island in the middle.</summary>
    private static SKBitmap WhiteFrameRedCenter()
    {
        var info = new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Red, IsAntialias = false, Style = SKPaintStyle.Fill };
        canvas.DrawRect(new SKRect(SquareLo, SquareLo, SquareHi, SquareHi), paint);
        canvas.Flush();
        return bitmap;
    }

    /// <summary>
    /// Two disjoint white bands split by a full-width red band: top white y in [0,20), red band
    /// y in [20,44), bottom white y in [44,64). A corner-seeded contiguous flood reaches only the
    /// top band; a global flood clears both white bands.
    /// </summary>
    private static SKBitmap DisjointWhiteBands()
    {
        var info = new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Red, IsAntialias = false, Style = SKPaintStyle.Fill };
        canvas.DrawRect(new SKRect(0, 20, Size, 44), paint);
        canvas.Flush();
        return bitmap;
    }

    [Fact]
    public void Render_Contiguous_ClearsSeededFrame_KeepsCenter()
    {
        using var src = WhiteFrameRedCenter();

        using var result = FloodFillPreviewEngine.Render(src, seedX: 2, seedY: 2, tolerance: 10, contiguous: true);

        // Seeded corner becomes transparent.
        Assert.Equal(0, result.GetPixel(2, 2).Alpha);
        // The opposite corner is reachable through the connected white frame.
        Assert.Equal(0, result.GetPixel(Size - 3, Size - 3).Alpha);
        // The red island is a different colour: alpha preserved and colour intact.
        var center = result.GetPixel(32, 32);
        Assert.Equal(255, center.Alpha);
        Assert.Equal(SKColors.Red.Red, center.Red);
        Assert.Equal(SKColors.Red.Green, center.Green);
        Assert.Equal(SKColors.Red.Blue, center.Blue);
    }

    [Fact]
    public void Render_Contiguous_DoesNotClearDisjointRegion()
    {
        using var src = DisjointWhiteBands();

        // Seed in the TOP white band; contiguous flood must not reach the bottom band.
        using var result = FloodFillPreviewEngine.Render(src, seedX: 2, seedY: 2, tolerance: 10, contiguous: true);

        Assert.Equal(0, result.GetPixel(2, 2).Alpha);        // top band cleared
        Assert.Equal(255, result.GetPixel(32, 60).Alpha);    // bottom band UNTOUCHED (disjoint)
        Assert.Equal(255, result.GetPixel(32, 32).Alpha);    // red band untouched
    }

    [Fact]
    public void Render_Global_ClearsAllMatchingRegions()
    {
        using var src = DisjointWhiteBands();

        // Global mode: every white pixel anywhere is cleared, disjoint or not.
        using var result = FloodFillPreviewEngine.Render(src, seedX: 2, seedY: 2, tolerance: 10, contiguous: false);

        Assert.Equal(0, result.GetPixel(2, 2).Alpha);        // top band
        Assert.Equal(0, result.GetPixel(32, 60).Alpha);      // bottom band cleared too
        Assert.Equal(255, result.GetPixel(32, 32).Alpha);    // red band kept opaque
    }

    [Fact]
    public void Render_ZeroTolerance_OnlyClearsExactColourMatches()
    {
        // A two-tone image: left half white, right half a near-white grey. With tolerance 0 the
        // grey must NOT match the white seed; raising tolerance later would catch it.
        var info = new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var src = new SKBitmap(info);
        using (var canvas = new SKCanvas(src))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = new SKColor(250, 250, 250), IsAntialias = false, Style = SKPaintStyle.Fill };
            canvas.DrawRect(new SKRect(Size / 2, 0, Size, Size), paint); // right half near-white grey
            canvas.Flush();
        }

        using var result = FloodFillPreviewEngine.Render(src, seedX: 2, seedY: 2, tolerance: 0, contiguous: false);

        // Exact white seed cleared; the near-white grey is outside zero-tolerance, so kept.
        Assert.Equal(0, result.GetPixel(2, 2).Alpha);
        Assert.Equal(255, result.GetPixel(Size - 2, 2).Alpha);
    }

    [Fact]
    public void Render_HighTolerance_MatchesNearbyColours()
    {
        // Same two-tone image; with a generous tolerance the near-white grey DOES match the seed.
        var info = new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var src = new SKBitmap(info);
        using (var canvas = new SKCanvas(src))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = new SKColor(250, 250, 250), IsAntialias = false, Style = SKPaintStyle.Fill };
            canvas.DrawRect(new SKRect(Size / 2, 0, Size, Size), paint);
            canvas.Flush();
        }

        using var result = FloodFillPreviewEngine.Render(src, seedX: 2, seedY: 2, tolerance: 50, contiguous: false);

        Assert.Equal(0, result.GetPixel(2, 2).Alpha);            // white seed cleared
        Assert.Equal(0, result.GetPixel(Size - 2, 2).Alpha);     // near-white grey now matched
    }

    [Fact]
    public void Render_SeedOutOfBounds_Throws()
    {
        using var src = WhiteFrameRedCenter();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => FloodFillPreviewEngine.Render(src, seedX: Size + 5, seedY: 2, tolerance: 10, contiguous: true));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FloodFillPreviewEngine.Render(src, seedX: 2, seedY: -1, tolerance: 10, contiguous: true));
    }

    [Fact]
    public void Render_NullSource_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => FloodFillPreviewEngine.Render(null!, seedX: 0, seedY: 0, tolerance: 10, contiguous: true));
    }

    [Fact]
    public void RenderPreviewPng_LargeImage_DownscalesButReturnsValidTransparentPng()
    {
        // A 2000x1000 white image with a red island — exceeds PreviewMaxSide (800), so the engine
        // downscales a copy for the preview. We assert the returned PNG decodes, is downscaled
        // (longest side <= 800), and the corner seed produced transparency.
        const int bigW = 2000, bigH = 1000;
        var info = new SKImageInfo(bigW, bigH, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        byte[] srcBytes;
        using (var bmp = new SKBitmap(info))
        {
            using (var canvas = new SKCanvas(bmp))
            {
                canvas.Clear(SKColors.White);
                using var paint = new SKPaint { Color = SKColors.Red, IsAntialias = false, Style = SKPaintStyle.Fill };
                canvas.DrawRect(new SKRect(900, 400, 1100, 600), paint);
                canvas.Flush();
            }
            using var image = SKImage.FromBitmap(bmp);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            srcBytes = data.ToArray();
        }

        // Seed in the full-res white background corner.
        var pngBytes = FloodFillPreviewEngine.RenderPreviewPng(srcBytes, seedX: 5, seedY: 5, tolerance: 10, contiguous: true);

        using var preview = SKBitmap.Decode(pngBytes);
        Assert.NotNull(preview);
        Assert.True(Math.Max(preview.Width, preview.Height) <= FloodFillPreviewEngine.PreviewMaxSide,
            $"preview longest side {Math.Max(preview.Width, preview.Height)} should be <= {FloodFillPreviewEngine.PreviewMaxSide}");
        // Corner of the downscaled preview is background and should be transparent.
        Assert.Equal(0, preview.GetPixel(2, 2).Alpha);
    }

    [Fact]
    public void RenderPreviewPng_SmallImage_NotDownscaled()
    {
        using var src = WhiteFrameRedCenter();
        using var image = SKImage.FromBitmap(src);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var srcBytes = data.ToArray();

        var pngBytes = FloodFillPreviewEngine.RenderPreviewPng(srcBytes, seedX: 2, seedY: 2, tolerance: 10, contiguous: true);

        using var preview = SKBitmap.Decode(pngBytes);
        Assert.NotNull(preview);
        // 64x64 is under the cap, so the preview keeps full resolution.
        Assert.Equal(Size, preview.Width);
        Assert.Equal(Size, preview.Height);
        Assert.Equal(0, preview.GetPixel(2, 2).Alpha);
        Assert.Equal(255, preview.GetPixel(32, 32).Alpha);
    }
}
