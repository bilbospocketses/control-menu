using ControlMenu.Modules.Imaging.Services;
using ControlMenu.Modules.Imaging.Services.Options;

namespace ControlMenu.Tests.Modules.Imaging;

/// <summary>
/// Integration tests for <see cref="ImageService.ResizeAsync"/>. These drive the REAL
/// bundled magick.exe (via <see cref="ImageServiceFixture"/>) and re-identify the output
/// with <see cref="ImageService.GetInfoAsync"/> to assert the resulting pixel dimensions
/// AND that resizing preserves the input format (a resize must not transcode).
/// </summary>
[Collection(nameof(ImageServiceCollection))]
public class ImageServiceResizeTests
{
    private readonly ImageServiceFixture _fx;

    public ImageServiceResizeTests(ImageServiceFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task ResizeAsync_PixelDimensions_ExactWhenAspectUnlocked()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var png = TestImages.CreatePng(256, 256);

        byte[] outBytes = await _fx.Service.ResizeAsync(png, new ResizeOptions
        {
            Mode = ResizeMode.PixelDimensions,
            Width = 200,
            Height = 100,
            LockAspect = false, // force exact -> WxH!
        });

        ImageInfo info = await _fx.Service.GetInfoAsync(outBytes);
        Assert.Equal(200, info.Width);
        Assert.Equal(100, info.Height);
        Assert.Equal("PNG", info.Format); // resize preserves input format
    }

    [SkippableFact]
    public async Task ResizeAsync_PixelDimensions_PreservesAspectWhenLocked()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        // 256x256 fit within a 200x100 box, aspect locked -> bounded by the smaller axis (100),
        // so a square collapses to 100x100.
        var png = TestImages.CreatePng(256, 256);

        byte[] outBytes = await _fx.Service.ResizeAsync(png, new ResizeOptions
        {
            Mode = ResizeMode.PixelDimensions,
            Width = 200,
            Height = 100,
            LockAspect = true, // WxH (fit within box)
        });

        ImageInfo info = await _fx.Service.GetInfoAsync(outBytes);
        Assert.Equal(100, info.Width);
        Assert.Equal(100, info.Height);
    }

    [SkippableFact]
    public async Task ResizeAsync_Percentage_HalvesDimensions()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var png = TestImages.CreatePng(256, 256);

        byte[] outBytes = await _fx.Service.ResizeAsync(png, new ResizeOptions
        {
            Mode = ResizeMode.Percentage,
            Percentage = 50,
        });

        ImageInfo info = await _fx.Service.GetInfoAsync(outBytes);
        Assert.Equal(128, info.Width);
        Assert.Equal(128, info.Height);
    }

    [SkippableFact]
    public async Task ResizeAsync_MaxDimensionFit_BoundsLongestEdge()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        // 400x200 fit into a 100x100 box -> longest edge (400) scales to 100, the other to 50.
        var png = TestImages.CreatePng(400, 200);

        byte[] outBytes = await _fx.Service.ResizeAsync(png, new ResizeOptions
        {
            Mode = ResizeMode.MaxDimensionFit,
            MaxDimension = 100,
        });

        ImageInfo info = await _fx.Service.GetInfoAsync(outBytes);
        Assert.Equal(100, info.Width);
        Assert.Equal(50, info.Height);
    }

    [SkippableFact]
    public async Task ResizeAsync_PixelDimensions_NoWidthOrHeight_Throws()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var png = TestImages.CreatePng(64, 64);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _fx.Service.ResizeAsync(png, new ResizeOptions
            {
                Mode = ResizeMode.PixelDimensions,
                Width = null,
                Height = null,
            }));
    }

    [SkippableFact]
    public async Task ResizeAsync_Percentage_NullPercentage_Throws()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var png = TestImages.CreatePng(64, 64);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _fx.Service.ResizeAsync(png, new ResizeOptions
            {
                Mode = ResizeMode.Percentage,
                Percentage = null,
            }));
    }

    [SkippableFact]
    public async Task ResizeAsync_MaxDimensionFit_NullMaxDimension_Throws()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var png = TestImages.CreatePng(64, 64);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _fx.Service.ResizeAsync(png, new ResizeOptions
            {
                Mode = ResizeMode.MaxDimensionFit,
                MaxDimension = null,
            }));
    }
}
