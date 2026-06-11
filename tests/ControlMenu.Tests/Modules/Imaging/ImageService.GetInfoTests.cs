using ControlMenu.Modules.Imaging.Services;

namespace ControlMenu.Tests.Modules.Imaging;

[Collection(nameof(ImageServiceCollection))]
public class ImageServiceGetInfoTests
{
    private readonly ImageServiceFixture _fx;

    public ImageServiceGetInfoTests(ImageServiceFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task GetInfoAsync_ReturnsWidthHeightFormat_ForPng()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var png = TestImages.CreatePng(256, 128);

        ImageInfo info = await _fx.Service.GetInfoAsync(png);

        Assert.Equal(256, info.Width);
        Assert.Equal(128, info.Height);
        Assert.Equal("PNG", info.Format);
        Assert.True(info.HasAlpha);
    }
}
