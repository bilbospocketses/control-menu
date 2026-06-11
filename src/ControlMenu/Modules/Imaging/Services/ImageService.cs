using ControlMenu.Common.Paths;
using ControlMenu.Modules.Imaging.Services.Options;
using ControlMenu.Services;
using Serilog;

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

    public Task<byte[]> RasterizeSvgAsync(byte[] svgBytes, RasterizeOptions options, CancellationToken ct = default)
        => throw new NotImplementedException("Phase D");

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
