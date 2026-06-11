using SkiaSharp;

namespace ControlMenu.Tests.Modules.Imaging;

/// <summary>
/// Generates small in-memory test images via SkiaSharp so the imaging integration
/// tests don't need any on-disk fixtures. All images carry an alpha channel so
/// HasAlpha-dependent assertions have something to assert against.
/// </summary>
internal static class TestImages
{
    /// <summary>
    /// Creates a <paramref name="w"/>x<paramref name="h"/> PNG filled with a single
    /// semi-transparent colour (so the encoded PNG has a real alpha channel), returned
    /// as the encoded PNG bytes.
    /// </summary>
    public static byte[] CreatePng(int w, int h)
    {
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        // Semi-transparent orange: alpha 0x80 guarantees a meaningful alpha channel.
        bitmap.Erase(new SKColor(0xFF, 0x80, 0x00, 0x80));

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
