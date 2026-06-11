using System.Globalization;
using ControlMenu.Common.Paths;
using ControlMenu.Modules.Imaging.Services.Options;
using ControlMenu.Services;
using Serilog;

namespace ControlMenu.Modules.Imaging.Services;

/// <summary>
/// Raster -> SVG tracing backed by the bundled vtracer (color) and potrace (monochrome)
/// binaries, both resolved via <see cref="IDependencyPathResolver"/> per the
/// Local-Dependencies-Only rule. magick (also bundled under the same "imaging" module) is
/// used only as the format normalizer feeding potrace, never to parse SVG.
///
/// Pipelines (flags verified empirically against the staged 0.6.4 / 1.16 binaries):
///   * Color:      vtracer --input in.png --output out.svg --colormode color [knobs]
///   * Monochrome: magick "in.png" -colorspace Gray -threshold N% "mid.bmp"
///                 potrace --svg --turdsize n --alphamax a [--longcurve] [--invert]
///                         --output out.svg "mid.bmp"
///
/// BMP is the potrace intermediate (NOT PGM/PBM): the hardened policy.xml allowlist permits
/// BMP but denies the PNM coders, and potrace reads BMP natively.
/// </summary>
public class TracingService : ITracingService
{
    private const string ModuleId = "imaging";
    private const string MagickName = "magick";
    private const string VtracerName = "vtracer";
    private const string PotraceName = "potrace";

    // Per-call resource caps for the magick normalize step -- defense in depth alongside policy.xml.
    private const string MagickLimitFlags = "-limit memory 512MB -limit area 16384x16384 -limit map 1GB";

    private readonly ICommandExecutor _executor;
    private readonly IDependencyPathResolver _resolver;
    private readonly IDataPathResolver _paths;

    public TracingService(
        ICommandExecutor executor,
        IDependencyPathResolver resolver,
        IDataPathResolver paths)
    {
        _executor = executor;
        _resolver = resolver;
        _paths = paths;
    }

    public async Task<byte[]> TraceColorAsync(byte[] input, TraceColorOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (input is null || input.Length == 0)
            throw new ArgumentException("Input image is empty", nameof(input));

        var workDir = CreateWorkDir();
        try
        {
            // vtracer reads PNG (and a few other rasters) directly. We always normalize the
            // incoming bytes to a real PNG via magick so the tracer gets a format it accepts
            // regardless of what the caller handed us, and so a corrupt/non-image input fails
            // here with a clear ImagingException rather than deep inside vtracer.
            // The source file is EXTENSIONLESS on purpose: magick sniffs the coder from the
            // file's magic bytes, never from the extension — a ".raw"/".bin" name would make
            // magick pick the RAW coder, which policy.xml denies.
            var rawPath = Path.Combine(workDir, "in");
            var pngPath = Path.Combine(workDir, "in.png");
            var svgPath = Path.Combine(workDir, "out.svg");
            await File.WriteAllBytesAsync(rawPath, input, ct);

            await InvokeMagickAsync($"{MagickLimitFlags} \"{rawPath}\" \"{pngPath}\"", ct);

            // colormode is constrained to the two values vtracer accepts; everything else maps
            // 1:1 to a verified vtracer flag. Numbers are invariant-culture so a non-US locale
            // never emits a comma vtracer would reject.
            var colorMode = NormalizeColorMode(options.ColorMode);
            var args =
                $"--input \"{pngPath}\" --output \"{svgPath}\" " +
                $"--colormode {colorMode} " +
                $"--hierarchical {NormalizeHierarchical(options.Hierarchical)} " +
                $"--mode {NormalizeMode(options.Mode)} " +
                $"--filter_speckle {options.FilterSpeckle.ToString(CultureInfo.InvariantCulture)} " +
                $"--color_precision {options.ColorPrecision.ToString(CultureInfo.InvariantCulture)} " +
                $"--path_precision {options.PathPrecision.ToString(CultureInfo.InvariantCulture)}";

            await InvokeTracerAsync(VtracerName, args, ct);

            return await File.ReadAllBytesAsync(svgPath, ct);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    public async Task<byte[]> TraceMonochromeAsync(byte[] input, TraceMonochromeOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (input is null || input.Length == 0)
            throw new ArgumentException("Input image is empty", nameof(input));

        var workDir = CreateWorkDir();
        try
        {
            // Extensionless source so magick sniffs the coder from magic bytes, not a
            // ".raw"/".bin" extension that policy.xml would reject as the RAW coder.
            var rawPath = Path.Combine(workDir, "in");
            var bmpPath = Path.Combine(workDir, "mid.bmp");
            var svgPath = Path.Combine(workDir, "out.svg");
            await File.WriteAllBytesAsync(rawPath, input, ct);

            // Step 1: magick rasterizes to a GRAYSCALE, bilevel-thresholded BMP. BMP (not PGM/PBM)
            // because policy.xml allows BMP and denies PNM. -threshold turns the greymap pure B/W
            // so potrace's tracing is deterministic w.r.t. our Threshold knob rather than potrace's
            // internal 0.5 blacklevel guess. Threshold is a 0-1 fraction -> percent for magick.
            var pct = (Math.Clamp(options.Threshold, 0.0, 1.0) * 100.0)
                .ToString("0.###", CultureInfo.InvariantCulture);
            await InvokeMagickAsync(
                $"{MagickLimitFlags} \"{rawPath}\" -colorspace Gray -threshold {pct}% \"{bmpPath}\"", ct);

            // Step 2: potrace traces the BMP to SVG. --longcurve only appears when curve
            // optimization is disabled; --invert only when requested. Numbers invariant-culture.
            var args =
                "--svg " +
                $"--turdsize {options.TurdSize.ToString(CultureInfo.InvariantCulture)} " +
                $"--alphamax {options.AlphaMax.ToString("0.###", CultureInfo.InvariantCulture)} " +
                (options.OptimizeCurve ? string.Empty : "--longcurve ") +
                (options.Invert ? "--invert " : string.Empty) +
                $"--output \"{svgPath}\" \"{bmpPath}\"";

            await InvokeTracerAsync(PotraceName, args, ct);

            return await File.ReadAllBytesAsync(svgPath, ct);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    // vtracer rejects unknown enum values, so constrain to its documented sets and fall back
    // to the default rather than forwarding a typo'd value that would fail the whole trace.
    private static string NormalizeColorMode(string? mode) =>
        string.Equals(mode, "bw", StringComparison.OrdinalIgnoreCase) ? "bw" : "color";

    private static string NormalizeHierarchical(string? h) =>
        string.Equals(h, "cutout", StringComparison.OrdinalIgnoreCase) ? "cutout" : "stacked";

    private static string NormalizeMode(string? m)
    {
        var v = (m ?? string.Empty).Trim().ToLowerInvariant();
        return v is "pixel" or "polygon" or "spline" ? v : "spline";
    }

    /// <summary>Allocate a per-call workdir under &lt;dataRoot&gt;/temp/imaging/&lt;guid&gt;/.</summary>
    private string CreateWorkDir()
    {
        var dir = Path.Combine(_paths.GetDataRoot(), "temp", "imaging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Invoke the bundled magick.exe (resolved per Local-Dependencies-Only). Used only as the
    /// format normalizer feeding the tracers. Throws <see cref="ImagingException"/> on non-zero exit.
    /// </summary>
    private async Task<CommandResult> InvokeMagickAsync(string args, CancellationToken ct)
    {
        var result = await _executor.ExecuteResolvedAsync(_resolver, ModuleId, MagickName, args, cancellationToken: ct);
        if (result.ExitCode != 0)
        {
            Log.Error("magick exit {ExitCode}: {Stderr}", result.ExitCode, result.StandardError);
            throw new ImagingException($"magick failed (exit {result.ExitCode}): {result.StandardError}");
        }
        return result;
    }

    /// <summary>
    /// Invoke a bundled tracer (vtracer or potrace), resolved per Local-Dependencies-Only.
    /// Throws <see cref="ImagingException"/> on non-zero exit. Note: vtracer prints
    /// "Conversion successful." to stdout and potrace is silent on success, so a clean exit
    /// code is the success signal; stderr is logged but not treated as failure on its own.
    /// </summary>
    private async Task<CommandResult> InvokeTracerAsync(string name, string args, CancellationToken ct)
    {
        var result = await _executor.ExecuteResolvedAsync(_resolver, ModuleId, name, args, cancellationToken: ct);
        if (result.ExitCode != 0)
        {
            Log.Error("{Tool} exit {ExitCode}: {Stderr}", name, result.ExitCode, result.StandardError);
            throw new ImagingException($"{name} failed (exit {result.ExitCode}): {result.StandardError}");
        }
        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            Log.Warning("{Tool}: {Stderr}", name, result.StandardError);
        }
        return result;
    }
}
