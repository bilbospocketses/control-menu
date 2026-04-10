using ControlMenu.Modules.Utilities.Services;

namespace ControlMenu.Tests.Modules.Utilities;

public class IconConversionServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IconConversionService _service = new();

    public IconConversionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"icontest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string CreateTestPng(int width = 64, int height = 64)
    {
        var path = Path.Combine(_tempDir, $"test_{width}x{height}.png");
        using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bitmap);
        g.Clear(System.Drawing.Color.Blue);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    [Fact]
    public async Task ConvertToIcoAsync_CreatesIcoFile()
    {
        var sourcePath = CreateTestPng(256, 256);
        var targetPath = Path.Combine(_tempDir, "output.ico");

        await _service.ConvertToIcoAsync(sourcePath, targetPath, [64, 128, 256]);

        Assert.True(File.Exists(targetPath));
        var bytes = await File.ReadAllBytesAsync(targetPath);
        // ICO file header: reserved (2 bytes) + type 1 (2 bytes) + image count (2 bytes)
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0)); // reserved = 0
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2)); // type = 1 (icon)
        Assert.Equal(3, BitConverter.ToUInt16(bytes, 4)); // 3 images
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
    public async Task ConvertToIcoAsync_SingleSize_Creates1Image()
    {
        var sourcePath = CreateTestPng(64, 64);
        var targetPath = Path.Combine(_tempDir, "single.ico");

        await _service.ConvertToIcoAsync(sourcePath, targetPath, [64]);

        Assert.True(File.Exists(targetPath));
        var bytes = await File.ReadAllBytesAsync(targetPath);
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 4)); // 1 image
    }

    [Fact]
    public async Task ConvertToIcoAsync_ThrowsForMissingSource()
    {
        var targetPath = Path.Combine(_tempDir, "out.ico");
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _service.ConvertToIcoAsync("/nonexistent/file.png", targetPath));
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
}
