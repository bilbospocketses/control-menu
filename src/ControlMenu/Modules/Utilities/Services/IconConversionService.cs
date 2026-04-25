using SkiaSharp;

namespace ControlMenu.Modules.Utilities.Services;

public class IconConversionService : IIconConversionService
{
    private static readonly int[] DefaultSizes = [64, 128, 256];

    public Task ConvertToIcoAsync(string sourcePath, string targetPath, int[]? sizes = null)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Source image not found.", sourcePath);

        sizes ??= DefaultSizes;

        return Task.Run(() =>
        {
            using var sourceImage = SKBitmap.Decode(sourcePath);
            if (sourceImage is null)
                throw new InvalidOperationException($"Could not decode image: {sourcePath}");

            var pngEntries = new List<byte[]>();
            foreach (var size in sizes)
            {
                using var resized = ResizeImage(sourceImage, size, size);
                using var image = SKImage.FromBitmap(resized);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                pngEntries.Add(encoded.ToArray());
            }

            using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(output);

            // ICONDIR header
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)pngEntries.Count);

            var dataOffset = 6 + 16 * pngEntries.Count;
            for (var i = 0; i < pngEntries.Count; i++)
            {
                var size = sizes[i];
                var data = pngEntries[i];
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)data.Length);
                writer.Write((uint)dataOffset);
                dataOffset += data.Length;
            }

            foreach (var data in pngEntries)
                writer.Write(data);
        });
    }

    public Task<byte[]> ConvertToIcoBytesAsync(byte[] sourceImageBytes, int[]? sizes = null)
    {
        sizes ??= DefaultSizes;

        return Task.Run(() =>
        {
            using var sourceImage = SKBitmap.Decode(sourceImageBytes);
            if (sourceImage is null)
                throw new InvalidOperationException("Could not decode image from bytes.");

            var pngEntries = new List<byte[]>();
            foreach (var size in sizes)
            {
                using var resized = ResizeImage(sourceImage, size, size);
                using var image = SKImage.FromBitmap(resized);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                pngEntries.Add(encoded.ToArray());
            }

            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output);

            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)pngEntries.Count);

            var dataOffset = 6 + 16 * pngEntries.Count;
            for (var i = 0; i < pngEntries.Count; i++)
            {
                var size = sizes[i];
                var data = pngEntries[i];
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)data.Length);
                writer.Write((uint)dataOffset);
                dataOffset += data.Length;
            }

            foreach (var data in pngEntries)
                writer.Write(data);

            return output.ToArray();
        });
    }

    private static SKBitmap ResizeImage(SKBitmap source, int width, int height)
    {
        var destBitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(destBitmap);
        canvas.Clear(SKColors.Transparent);

        var srcAspect = (float)source.Width / source.Height;
        int drawWidth, drawHeight, drawX, drawY;

        if (srcAspect > 1)
        {
            drawWidth = width;
            drawHeight = (int)(height / srcAspect);
            drawX = 0;
            drawY = (height - drawHeight) / 2;
        }
        else if (srcAspect < 1)
        {
            drawWidth = (int)(width * srcAspect);
            drawHeight = height;
            drawX = (width - drawWidth) / 2;
            drawY = 0;
        }
        else
        {
            drawWidth = width;
            drawHeight = height;
            drawX = 0;
            drawY = 0;
        }

        var destRect = new SKRect(drawX, drawY, drawX + drawWidth, drawY + drawHeight);
        var sourceRect = new SKRect(0, 0, source.Width, source.Height);

        using var paint = new SKPaint
        {
            IsAntialias = true
        };

        using var sourceImage = SKImage.FromBitmap(source);
        var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
        canvas.DrawImage(sourceImage, sourceRect, destRect, sampling, paint);
        return destBitmap;
    }
}
