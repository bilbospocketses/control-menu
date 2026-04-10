using ControlMenu.Modules.Utilities.Services;

namespace ControlMenu.Tests.Modules.Utilities;

public class IconConversionServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IconConversionService _service = new();

    public IconConversionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ControlMenu-Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task ConvertToIcoAsync_ProducesValidIcoFile()
    {
        var sourcePng = CreateTestPng(128, 128);
        var targetIco = Path.Combine(_tempDir, "test.ico");
        await _service.ConvertToIcoAsync(sourcePng, targetIco, [32, 64]);
        Assert.True(File.Exists(targetIco));
        var bytes = await File.ReadAllBytesAsync(targetIco);
        Assert.True(bytes.Length > 22);
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0)); // reserved
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2)); // type = icon
        Assert.Equal(2, BitConverter.ToUInt16(bytes, 4)); // 2 images
    }

    [Fact]
    public async Task ConvertToIcoAsync_HandlesNonSquareImage()
    {
        var sourcePng = CreateTestPng(200, 100);
        var targetIco = Path.Combine(_tempDir, "wide.ico");
        await _service.ConvertToIcoAsync(sourcePng, targetIco, [64]);
        Assert.True(File.Exists(targetIco));
        var bytes = await File.ReadAllBytesAsync(targetIco);
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 4)); // 1 image
    }

    [Fact]
    public async Task ConvertToIcoAsync_Handles256Size()
    {
        var sourcePng = CreateTestPng(512, 512);
        var targetIco = Path.Combine(_tempDir, "large.ico");
        await _service.ConvertToIcoAsync(sourcePng, targetIco, [256]);
        var bytes = await File.ReadAllBytesAsync(targetIco);
        Assert.Equal(0, bytes[6]); // width = 0 means 256
        Assert.Equal(0, bytes[7]); // height = 0 means 256
    }

    [Fact]
    public async Task ConvertToIcoAsync_ThrowsForMissingFile()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _service.ConvertToIcoAsync("/nonexistent.png", "/out.ico"));
    }

    [Fact]
    public async Task ConvertToIcoAsync_DefaultSizes_Creates3Sizes()
    {
        var sourcePath = CreateTestPng(256, 256);
        var targetPath = Path.Combine(_tempDir, "default.ico");

        await _service.ConvertToIcoAsync(sourcePath, targetPath);

        Assert.True(File.Exists(targetPath));
        var bytes = await File.ReadAllBytesAsync(targetPath);
        Assert.Equal(3, BitConverter.ToUInt16(bytes, 4)); // default is 64, 128, 256
    }

    [Fact]
    public async Task ConvertToIcoAsync_OutputIsValidIcoFormat()
    {
        var sourcePath = CreateTestPng(128, 128);
        var targetPath = Path.Combine(_tempDir, "valid.ico");

        await _service.ConvertToIcoAsync(sourcePath, targetPath, [32, 64]);

        var bytes = await File.ReadAllBytesAsync(targetPath);
        Assert.True(bytes.Length > 6 + 16 * 2); // header + 2 dir entries minimum
    }

    private string CreateTestPng(int width, int height)
    {
        var path = Path.Combine(_tempDir, $"test_{width}x{height}.png");
        using var bitmap = new SkiaSharp.SKBitmap(width, height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Clear(new SkiaSharp.SKColor(255, 0, 0, 128));
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        data.SaveTo(fs);
        return path;
    }
}
