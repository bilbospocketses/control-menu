using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ControlMenu.Modules.Utilities.Services;

public class IconConversionService : IIconConversionService
{
    private static readonly int[] DefaultSizes = [64, 128, 256];

    public Task ConvertToIcoAsync(string sourcePath, string targetPath, int[]? sizes = null)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Source image not found.", sourcePath);

        sizes ??= DefaultSizes;

        using var sourceImage = new Bitmap(sourcePath);
        using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write);

        // Prepare PNG data for each size
        var pngEntries = new List<byte[]>();
        foreach (var size in sizes)
        {
            using var resized = ResizeImage(sourceImage, size, size);
            using var ms = new MemoryStream();
            resized.Save(ms, ImageFormat.Png);
            pngEntries.Add(ms.ToArray());
        }

        using var writer = new BinaryWriter(output);

        // ICONDIR header
        writer.Write((ushort)0);               // reserved
        writer.Write((ushort)1);               // type = icon
        writer.Write((ushort)pngEntries.Count); // image count

        // Calculate data offset: header (6) + dir entries (16 each)
        var dataOffset = 6 + 16 * pngEntries.Count;

        // ICONDIRENTRY for each image
        for (var i = 0; i < pngEntries.Count; i++)
        {
            var size = sizes[i];
            var data = pngEntries[i];

            writer.Write((byte)(size >= 256 ? 0 : size)); // width (0 = 256)
            writer.Write((byte)(size >= 256 ? 0 : size)); // height (0 = 256)
            writer.Write((byte)0);     // color count (0 for 32bpp)
            writer.Write((byte)0);     // reserved
            writer.Write((ushort)1);   // color planes
            writer.Write((ushort)32);  // bits per pixel
            writer.Write((uint)data.Length);    // bytes in resource
            writer.Write((uint)dataOffset);     // offset to data

            dataOffset += data.Length;
        }

        // Image data (PNG blobs)
        foreach (var data in pngEntries)
        {
            writer.Write(data);
        }

        return Task.CompletedTask;
    }

    private static Bitmap ResizeImage(Image source, int width, int height)
    {
        var destRect = new Rectangle(0, 0, width, height);
        var destImage = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        destImage.SetResolution(96, 96);

        using var graphics = Graphics.FromImage(destImage);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Handle non-square source images: scale to fit, center
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

        using var wrapMode = new ImageAttributes();
        wrapMode.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
        graphics.DrawImage(source, new Rectangle(drawX, drawY, drawWidth, drawHeight),
            0, 0, source.Width, source.Height, GraphicsUnit.Pixel, wrapMode);

        return destImage;
    }
}
