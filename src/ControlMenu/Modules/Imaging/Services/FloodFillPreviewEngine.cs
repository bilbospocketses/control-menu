using SkiaSharp;

namespace ControlMenu.Modules.Imaging.Services;

/// <summary>
/// In-process SkiaSharp flood-fill used ONLY for the Magic Wand live preview. It is a fast,
/// approximate stand-in for the authoritative magick.exe render (<see cref="IImageService.RemoveBackgroundAsync"/>):
/// it gives responsive per-keystroke tolerance feedback over the SignalR circuit, but the bytes
/// the user actually saves always come from magick, never from here.
///
/// MATCHING METRIC (mirrors magick's <c>-fuzz</c> percent): two colours match when their
/// Euclidean RGB distance, normalized by sqrt(3·255²) ≈ 441.6729559, is ≤ Tolerance/100. This
/// is the same normalization ImageMagick's fuzz uses, so the preview tracks the magick result
/// closely on clean-background images. Alpha is ignored for matching (we only zero alpha on a hit).
///
/// CONTIGUOUS vs GLOBAL — matches <see cref="ImageService.RemoveBackgroundAsync"/>:
///   * Contiguous (default): an explicit-stack 4-connectivity flood fill from the seed; clears
///     alpha only on the connected region reachable from the seed (magick <c>-floodfill</c>).
///   * Global: scans every pixel and clears alpha on every match anywhere (magick <c>-transparent</c>).
///
/// PERFORMANCE: works on the raw RGBA byte buffer (no per-pixel <c>GetPixel</c>/<c>SetPixel</c>
/// marshalling), and the page hands us a DOWNSCALED copy (longest side capped, see
/// <see cref="PreviewMaxSide"/>) so a flood fill over the SignalR circuit stays snappy. The
/// seed passed in is already in the (possibly downscaled) preview's pixel space.
/// </summary>
public static class FloodFillPreviewEngine
{
    /// <summary>sqrt(3 · 255²) — the maximum possible RGB Euclidean distance, used to normalize fuzz.</summary>
    private const double MaxRgbDistance = 441.6729559300637;

    /// <summary>
    /// Longest-side cap (px) for the preview bitmap. The page downscales a copy of the source to
    /// this before flood-filling so per-change work stays small; the full-res Apply is unaffected
    /// because it goes straight to magick with the source bytes and source-space seed.
    /// </summary>
    public const int PreviewMaxSide = 800;

    /// <summary>
    /// Returns a NEW bitmap that is <paramref name="source"/> with the seeded background flooded to
    /// transparent (alpha 0 on matched pixels, all other pixels copied unchanged). The seed is in
    /// <paramref name="source"/>'s own pixel coordinates.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The seed is outside the bitmap bounds.</exception>
    public static SKBitmap Render(SKBitmap source, int seedX, int seedY, int tolerance, bool contiguous)
    {
        ArgumentNullException.ThrowIfNull(source);

        var w = source.Width;
        var h = source.Height;
        if (seedX < 0 || seedX >= w || seedY < 0 || seedY >= h)
            throw new ArgumentOutOfRangeException(nameof(seedX),
                $"Seed ({seedX},{seedY}) is outside the bitmap bounds {w}x{h}");

        // Work in straight (unpremultiplied) RGBA8888 so reading the seed colour and zeroing alpha
        // are exact and the result encodes to a normal transparent PNG. We normalize the source into
        // a result bitmap of this layout, then mutate that bitmap's pixel span IN PLACE — no managed
        // round-trip, no IntPtr marshalling in the hot loop. The result IS the bitmap we return.
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);

        var result = new SKBitmap(info);
        // Draw the source into the normalized bitmap; this converts any source colour/alpha type to
        // our RGBA8888/Unpremul layout. Unmatched pixels are then left untouched below.
        using (var canvas = new SKCanvas(result))
        {
            canvas.Clear(SKColors.Transparent);
            // Nearest sampling, and exact 1:1 placement: this is a colour/alpha-type normalisation
            // blit, not a resize, so no interpolation must touch the pixels the flood fill reads.
            canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        }

        var threshold = Math.Clamp(tolerance, 0, 100) / 100.0;

        // Mutate the bitmap's backing pixels directly. GetPixelSpan() returns a writable Span<byte>
        // over the RGBA bytes (4 bytes/pixel, row-major, no padding for a tightly-packed RGBA8888).
        var px = result.GetPixelSpan();

        // Seed colour (R,G,B at byte offsets 0,1,2 of the seed pixel).
        var seedIdx = (seedY * w + seedX) * 4;
        byte seedR = px[seedIdx], seedG = px[seedIdx + 1], seedB = px[seedIdx + 2];

        if (contiguous)
        {
            FloodContiguous(px, w, h, seedX, seedY, seedR, seedG, seedB, threshold);
        }
        else
        {
            FloodGlobal(px, w, h, seedR, seedG, seedB, threshold);
        }

        result.NotifyPixelsChanged();
        return result;
    }

    /// <summary>
    /// Convenience overload: decode <paramref name="sourceBytes"/>, downscale a copy so its longest
    /// side is ≤ <see cref="PreviewMaxSide"/>, run the flood fill in that downscaled space, and
    /// return the preview as encoded PNG bytes. <paramref name="seedX"/>/<paramref name="seedY"/>
    /// are in FULL-RESOLUTION source coordinates and are mapped into the downscaled space here, so
    /// the page can pass the same source-space seed it sends to the authoritative magick Apply.
    /// </summary>
    public static byte[] RenderPreviewPng(byte[] sourceBytes, int seedX, int seedY, int tolerance, bool contiguous)
    {
        ArgumentNullException.ThrowIfNull(sourceBytes);

        using var decoded = SKBitmap.Decode(sourceBytes)
            ?? throw new ArgumentException("Could not decode the source image.", nameof(sourceBytes));

        var fullW = decoded.Width;
        var fullH = decoded.Height;
        if (seedX < 0 || seedX >= fullW || seedY < 0 || seedY >= fullH)
            throw new ArgumentOutOfRangeException(nameof(seedX),
                $"Seed ({seedX},{seedY}) is outside the image bounds {fullW}x{fullH}");

        // Downscale a copy if either side exceeds the cap, preserving aspect ratio.
        var longest = Math.Max(fullW, fullH);
        SKBitmap working;
        int previewSeedX, previewSeedY;
        bool disposeWorking = false;

        if (longest > PreviewMaxSide)
        {
            var scale = (double)PreviewMaxSide / longest;
            var pw = Math.Max(1, (int)Math.Round(fullW * scale));
            var ph = Math.Max(1, (int)Math.Round(fullH * scale));
            var scaledInfo = new SKImageInfo(pw, ph, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            working = decoded.Resize(scaledInfo, SKSamplingOptions.Default)
                ?? throw new InvalidOperationException("Failed to downscale the source for preview.");
            disposeWorking = true;
            // Map the full-res seed into the downscaled space, clamped to bounds.
            previewSeedX = Math.Clamp((int)Math.Round(seedX * scale), 0, pw - 1);
            previewSeedY = Math.Clamp((int)Math.Round(seedY * scale), 0, ph - 1);
        }
        else
        {
            working = decoded;
            previewSeedX = seedX;
            previewSeedY = seedY;
        }

        try
        {
            using var filled = Render(working, previewSeedX, previewSeedY, tolerance, contiguous);
            using var image = SKImage.FromBitmap(filled);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        finally
        {
            if (disposeWorking) working.Dispose();
        }
    }

    /// <summary>
    /// Explicit-stack 4-connectivity flood from the seed over the RGBA <paramref name="px"/> buffer;
    /// zeroes the alpha byte of connected matched pixels in place.
    /// </summary>
    private static void FloodContiguous(Span<byte> px, int w, int h,
        int seedX, int seedY, byte seedR, byte seedG, byte seedB, double threshold)
    {
        var visited = new bool[w * h];
        var stack = new Stack<int>(); // pack (y*w + x) to avoid tuple allocs in the hot loop
        stack.Push(seedY * w + seedX);

        while (stack.Count > 0)
        {
            var p = stack.Pop();
            if (visited[p]) continue;
            visited[p] = true;

            var idx = p * 4;
            if (!Matches(px[idx], px[idx + 1], px[idx + 2], seedR, seedG, seedB, threshold))
                continue;

            px[idx + 3] = 0; // zero alpha

            var x = p % w;
            var y = p / w;
            if (x + 1 < w) stack.Push(p + 1);
            if (x - 1 >= 0) stack.Push(p - 1);
            if (y + 1 < h) stack.Push(p + w);
            if (y - 1 >= 0) stack.Push(p - w);
        }
    }

    /// <summary>Scans every pixel in <paramref name="px"/>; zeroes alpha on every match anywhere.</summary>
    private static void FloodGlobal(Span<byte> px, int w, int h,
        byte seedR, byte seedG, byte seedB, double threshold)
    {
        var count = w * h;
        for (int p = 0; p < count; p++)
        {
            var idx = p * 4;
            if (Matches(px[idx], px[idx + 1], px[idx + 2], seedR, seedG, seedB, threshold))
                px[idx + 3] = 0; // zero alpha
        }
    }

    /// <summary>True when (r,g,b) is within the normalized fuzz threshold of the seed colour.</summary>
    private static bool Matches(byte r, byte g, byte b, byte seedR, byte seedG, byte seedB, double threshold)
    {
        double dr = r - seedR;
        double dg = g - seedG;
        double db = b - seedB;
        var dist = Math.Sqrt(dr * dr + dg * dg + db * db) / MaxRgbDistance;
        return dist <= threshold;
    }
}
