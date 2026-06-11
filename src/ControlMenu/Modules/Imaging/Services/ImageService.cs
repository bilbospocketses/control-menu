using ControlMenu.Common.Paths;
using ControlMenu.Modules.Imaging.Services.Options;
using ControlMenu.Services;
using Serilog;
using SkiaSharp;
using Svg.Skia;

namespace ControlMenu.Modules.Imaging.Services;

public class ImageService : IImageService
{
    private const string ModuleId = "imaging";
    private const string MagickName = "magick";

    // Per-call resource caps -- defense in depth alongside policy.xml.
    private const string LimitFlags = "-limit memory 512MB -limit area 16384x16384 -limit map 1GB";

    private readonly ICommandExecutor _executor;
    private readonly IDependencyPathResolver _resolver;
    private readonly IDataPathResolver _paths;

    public ImageService(
        ICommandExecutor executor,
        IDependencyPathResolver resolver,
        IDataPathResolver paths)
    {
        _executor = executor;
        _resolver = resolver;
        _paths = paths;
    }

    public async Task<byte[]> ConvertFormatAsync(byte[] input, string targetFormat, ConvertFormatOptions? options = null, CancellationToken ct = default)
    {
        // Normalize: trim, drop a leading dot, lower-case. magick selects the encoder from
        // the OUTPUT file extension, so the normalized token becomes our out.<ext>.
        var ext = (targetFormat ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        if (ext.Length == 0)
            throw new ArgumentException("Target format is required", nameof(targetFormat));

        var quality = (options ?? new ConvertFormatOptions()).Quality;

        var workDir = CreateWorkDir();
        try
        {
            var inputPath = Path.Combine(workDir, "in.bin");
            var outputPath = Path.Combine(workDir, $"out.{ext}");
            await File.WriteAllBytesAsync(inputPath, input, ct);

            // -quality applies to lossy encoders (JPG/WebP/AVIF); lossless coders ignore it
            // harmlessly. magick infers the target format from the out.<ext> name.
            await InvokeMagickAsync(
                $"{LimitFlags} \"{inputPath}\" -quality {quality} \"{outputPath}\"", ct);

            return await File.ReadAllBytesAsync(outputPath, ct);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    public async Task<byte[]> ResizeAsync(byte[] input, ResizeOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Build the -resize geometry up front so invalid option combinations fail fast,
        // before we touch magick or the filesystem.
        var geometry = BuildResizeGeometry(options);

        // A resize must NOT transcode: detect the input format and re-encode to the same one.
        var info = await GetInfoAsync(input, ct);
        var ext = info.Format.ToLowerInvariant();

        var workDir = CreateWorkDir();
        try
        {
            var inputPath = Path.Combine(workDir, "in.bin");
            var outputPath = Path.Combine(workDir, $"out.{ext}");
            await File.WriteAllBytesAsync(inputPath, input, ct);

            await InvokeMagickAsync(
                $"{LimitFlags} \"{inputPath}\" -resize {geometry} \"{outputPath}\"", ct);

            return await File.ReadAllBytesAsync(outputPath, ct);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Translates <see cref="ResizeOptions"/> into an ImageMagick -resize geometry string.
    /// Throws <see cref="ArgumentException"/> for option combinations that don't carry the
    /// data their mode requires.
    /// </summary>
    private static string BuildResizeGeometry(ResizeOptions options)
    {
        switch (options.Mode)
        {
            case ResizeMode.PixelDimensions:
                if (options.Width is null && options.Height is null)
                    throw new ArgumentException(
                        "PixelDimensions requires Width and/or Height", nameof(options));

                // "WxH" fits within the WxH box preserving aspect; a missing dimension is
                // simply omitted (e.g. "200x" scales by width). "WxH!" forces exact size.
                var w = options.Width?.ToString() ?? string.Empty;
                var h = options.Height?.ToString() ?? string.Empty;
                var bang = options.LockAspect ? string.Empty : "!";
                return $"{w}x{h}{bang}";

            case ResizeMode.Percentage:
                if (options.Percentage is null)
                    throw new ArgumentException(
                        "Percentage mode requires Percentage", nameof(options));
                // Invariant culture so a non-US locale doesn't emit a comma decimal separator.
                return $"{options.Percentage.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}%";

            case ResizeMode.MaxDimensionFit:
                if (options.MaxDimension is null)
                    throw new ArgumentException(
                        "MaxDimensionFit mode requires MaxDimension", nameof(options));
                var m = options.MaxDimension.Value;
                // "MxM" fits within an MxM box, preserving aspect -> bounds the longest edge.
                return $"{m}x{m}";

            default:
                throw new ArgumentException($"Unsupported resize mode: {options.Mode}", nameof(options));
        }
    }

    public async Task<byte[]> ConvertToIcoAsync(byte[] input, int[] sizes, IcoOptions? options = null, CancellationToken ct = default)
    {
        if (sizes is null || sizes.Length == 0)
            throw new ArgumentException("At least one size required", nameof(sizes));

        // magick's icon:auto-resize wants the target sizes ascending, comma-separated.
        var csv = string.Join(",", sizes.OrderBy(s => s));

        var workDir = CreateWorkDir();
        try
        {
            var inputPath = Path.Combine(workDir, "in.bin");
            var outputPath = Path.Combine(workDir, "out.ico");
            await File.WriteAllBytesAsync(inputPath, input, ct);

            await InvokeMagickAsync(
                $"{LimitFlags} \"{inputPath}\" -define icon:auto-resize={csv} \"{outputPath}\"", ct);

            return await File.ReadAllBytesAsync(outputPath, ct);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    public Task<byte[]> RemoveBackgroundAsync(byte[] input, BackgroundRemoveOptions options, CancellationToken ct = default)
        => throw new NotImplementedException("Phase E");

    /// <summary>
    /// Rasterizes an SVG to a raster image. The SVG is rendered IN-PROCESS with Svg.Skia
    /// (managed) — it is NEVER handed to magick, by design: our hardened policy.xml only
    /// permits SVG <i>read</i>, and the security stance is to keep SVG out of magick's
    /// parser entirely. magick is only reached, with already-rasterized PNG pixels, when
    /// the "ico" output bundles the sizes via <see cref="ConvertToIcoAsync"/>.
    ///
    /// "png": renders once at the LARGEST requested size and returns the PNG bytes.
    /// "ico": renders once at the largest size to PNG, then bundles <c>options.Sizes</c>
    /// into a single .ico via the existing magick-backed ConvertToIcoAsync.
    /// </summary>
    public async Task<byte[]> RasterizeSvgAsync(byte[] svgBytes, RasterizeOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Sizes is null || options.Sizes.Length == 0)
            throw new ArgumentException("At least one size required", nameof(options));

        var format = (options.OutputFormat ?? "png").Trim().ToLowerInvariant();
        var largest = options.Sizes.Max();

        // Render at the largest size; the ico path resamples down to the smaller sizes,
        // and the png path returns this directly. Background-clearing happens inside.
        var pngBytes = RenderSvgToPng(svgBytes, largest, options.Background, ct);

        switch (format)
        {
            case "png":
                return pngBytes;

            case "ico":
                // Hand the RASTERIZED png (not the SVG) to magick to bundle the sizes.
                return await ConvertToIcoAsync(pngBytes, options.Sizes, ct: ct);

            default:
                throw new ArgumentException($"Unsupported output format: {options.OutputFormat}", nameof(options));
        }
    }

    /// <summary>
    /// Renders <paramref name="svgBytes"/> into a <paramref name="size"/>x<paramref name="size"/>
    /// PNG using Svg.Skia. The SVG is scaled to fit the square preserving aspect ratio and
    /// centered; the canvas is cleared to <paramref name="background"/> first ("transparent"
    /// or a hex like "#ffffff"). Any load/parse failure (e.g. Svg.Skia's XmlException on
    /// garbage input) is wrapped as <see cref="ImagingException"/>.
    /// </summary>
    private static byte[] RenderSvgToPng(byte[] svgBytes, int size, string? background, CancellationToken ct)
    {
        if (svgBytes is null || svgBytes.Length == 0)
            throw new ImagingException("SVG input is empty");

        ct.ThrowIfCancellationRequested();

        var bg = ParseBackground(background);

        // CRITICAL: Svg.Skia 5.0.0's SKSvg OWNS its SKPicture and DISPOSES it when the SKSvg
        // is disposed. Touching the picture after the SKSvg is gone is a native use-after-free
        // (0xC0000005). So the SKSvg MUST stay alive for the ENTIRE render+encode below — we
        // never let it dispose until the PNG bytes are produced.
        using var svg = new SKSvg();

        SKPicture? picture;
        try
        {
            using var input = new MemoryStream(svgBytes, writable: false);
            // SKSvg.Load(Stream) parses the SVG and returns the SKPicture (also exposed as
            // svg.Picture). It THROWS (XmlException) on malformed/empty XML rather than
            // returning null, so a thrown load is the invalid-SVG signal.
            picture = svg.Load(input);
        }
        catch (Exception ex)
        {
            throw new ImagingException($"Failed to parse SVG: {ex.Message}", ex);
        }

        if (picture is null)
            throw new ImagingException("SVG produced no renderable picture");

        ct.ThrowIfCancellationRequested();

        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ImagingException("SVG has no intrinsic size (empty CullRect)");

        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(bg);

            // Fit-within-square preserving aspect ratio, then center the scaled picture.
            var scale = Math.Min(size / bounds.Width, size / bounds.Height);
            var dx = (size - bounds.Width * scale) / 2f;
            var dy = (size - bounds.Height * scale) / 2f;

            canvas.Translate(dx, dy);
            canvas.Scale(scale);
            // CullRect may not be origin-anchored; shift so its top-left maps to (0,0).
            canvas.Translate(-bounds.Left, -bounds.Top);

            canvas.DrawPicture(picture);
            canvas.Flush();
        }

        ct.ThrowIfCancellationRequested();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
        // svg disposes here (method scope) — AFTER the picture is fully consumed.
    }

    /// <summary>
    /// Parses the background spec into an <see cref="SKColor"/>. "transparent" (default,
    /// case-insensitive) maps to <see cref="SKColors.Transparent"/>; anything else is parsed
    /// as a hex color via <see cref="SKColor.Parse"/>. An unparseable value is an
    /// <see cref="ArgumentException"/>.
    /// </summary>
    private static SKColor ParseBackground(string? background)
    {
        var spec = (background ?? "transparent").Trim();
        if (spec.Length == 0 || spec.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            return SKColors.Transparent;

        if (SKColor.TryParse(spec, out var color))
            return color;

        throw new ArgumentException($"Unrecognized background color: '{background}'", nameof(background));
    }

    public async Task<ImageInfo> GetInfoAsync(byte[] input, CancellationToken ct = default)
    {
        var workDir = CreateWorkDir();
        try
        {
            var inputPath = Path.Combine(workDir, "input");
            await File.WriteAllBytesAsync(inputPath, input, ct);

            // %w=width %h=height %m=format code (e.g. PNG) %A=alpha/matte state.
            var result = await InvokeMagickAsync($"identify -format \"%w %h %m %A\" \"{inputPath}\"", ct);

            var parts = result.StandardOutput.Trim().Split(' ');
            if (parts.Length < 4)
                throw new ImagingException($"Unexpected identify output: '{result.StandardOutput.Trim()}'");

            // %A reports "True"/"Blend"/"False"/"Undefined"; treat anything that isn't
            // an explicit no-alpha state as having an alpha channel.
            var alpha = parts[3];
            var hasAlpha = !alpha.Equals("False", StringComparison.OrdinalIgnoreCase)
                        && !alpha.Equals("Undefined", StringComparison.OrdinalIgnoreCase);

            return new ImageInfo(
                Width: int.Parse(parts[0]),
                Height: int.Parse(parts[1]),
                Format: parts[2],
                HasAlpha: hasAlpha,
                SizeBytes: input.LongLength);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>Allocate a per-call workdir under &lt;dataRoot&gt;/temp/imaging/&lt;guid&gt;/.</summary>
    private string CreateWorkDir()
    {
        var dir = Path.Combine(_paths.GetDataRoot(), "temp", "imaging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Invoke the bundled magick.exe (resolved via IDependencyPathResolver per the
    /// Local-Dependencies-Only rule) with the given args. magick reads our hardened
    /// policy.xml from its own directory, so no environment overrides are needed.
    /// Throws <see cref="ImagingException"/> on non-zero exit.
    /// </summary>
    private async Task<CommandResult> InvokeMagickAsync(string args, CancellationToken ct)
    {
        var result = await _executor.ExecuteResolvedAsync(_resolver, ModuleId, MagickName, args, cancellationToken: ct);

        if (result.ExitCode != 0)
        {
            Log.Error("magick exit {ExitCode}: {Stderr}", result.ExitCode, result.StandardError);
            throw new ImagingException($"magick failed (exit {result.ExitCode}): {result.StandardError}");
        }
        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            Log.Warning("magick: {Stderr}", result.StandardError);
        }
        return result;
    }
}
