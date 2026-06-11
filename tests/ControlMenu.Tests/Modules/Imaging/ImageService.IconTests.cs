using System.Buffers.Binary;
using ControlMenu.Modules.Imaging.Services;

namespace ControlMenu.Tests.Modules.Imaging;

/// <summary>
/// Integration tests for <see cref="ImageService.ConvertToIcoAsync"/>. These drive the
/// REAL bundled magick.exe (via <see cref="ImageServiceFixture"/>) and assert against the
/// actual bytes of the produced .ico — specifically the ICONDIR header, which is:
///   offset 0 (uint16 LE): reserved, must be 0
///   offset 2 (uint16 LE): image type, 1 = icon
///   offset 4 (uint16 LE): number of images (directory entries)
/// Each ICONDIRENTRY is 16 bytes beginning at offset 6.
/// </summary>
[Collection(nameof(ImageServiceCollection))]
public class ImageServiceIconTests
{
    private readonly ImageServiceFixture _fx;

    public ImageServiceIconTests(ImageServiceFixture fx) => _fx = fx;

    private static ushort Reserved(byte[] ico) => BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(0, 2));
    private static ushort IconType(byte[] ico) => BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2, 2));
    private static ushort EntryCount(byte[] ico) => BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4, 2));

    [SkippableFact]
    public async Task ConvertToIcoAsync_ProducesValidIco_With3Sizes()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var png = TestImages.CreatePng(1024, 1024);

        byte[] ico = await _fx.Service.ConvertToIcoAsync(png, new[] { 64, 128, 256 });

        // 6-byte ICONDIR + 3 * 16-byte entries = 54 bytes minimum before any pixel payload.
        Assert.True(ico.Length > 22, $"ICO too small: {ico.Length} bytes");
        Assert.Equal(0, Reserved(ico));    // reserved
        Assert.Equal(1, IconType(ico));    // type = icon
        Assert.Equal(3, EntryCount(ico));  // three sizes -> three directory entries
    }

    [SkippableFact]
    public async Task ConvertToIcoAsync_DefaultSizes_Creates3Entries()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var png = TestImages.CreatePng(512, 512);

        // Default sizes (16,32,48) supplied explicitly; the service has no implicit default,
        // so the caller passes the canonical small-icon trio.
        byte[] ico = await _fx.Service.ConvertToIcoAsync(png, new[] { 16, 32, 48 });

        Assert.Equal(0, Reserved(ico));
        Assert.Equal(1, IconType(ico));
        Assert.Equal(3, EntryCount(ico));
    }

    [SkippableFact]
    public async Task ConvertToIcoAsync_HandlesNonSquareInput()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var png = TestImages.CreatePng(200, 100);

        byte[] ico = await _fx.Service.ConvertToIcoAsync(png, new[] { 64 });

        Assert.Equal(0, Reserved(ico));
        Assert.Equal(1, IconType(ico));
        Assert.Equal(1, EntryCount(ico));  // single size -> single entry
    }

    [SkippableFact]
    public async Task ConvertToIcoAsync_NullSizes_Throws()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var png = TestImages.CreatePng(64, 64);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _fx.Service.ConvertToIcoAsync(png, null!));
    }

    [SkippableFact]
    public async Task ConvertToIcoAsync_EmptySizes_Throws()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick not installed");

        var png = TestImages.CreatePng(64, 64);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _fx.Service.ConvertToIcoAsync(png, Array.Empty<int>()));
    }
}
