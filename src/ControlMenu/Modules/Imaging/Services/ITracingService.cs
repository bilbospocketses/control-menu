using ControlMenu.Modules.Imaging.Services.Options;

namespace ControlMenu.Modules.Imaging.Services;

/// <summary>
/// Raster -> SVG vector tracing for the Imaging Tools module. Kept SEPARATE from the
/// magick-focused <see cref="IImageService"/> because tracing drives different bundled
/// binaries (vtracer for color, potrace for monochrome) with their own pipelines:
///   * Color: vtracer reads the PNG directly and emits SVG.
///   * Monochrome: potrace cannot read PNG, so magick first rasterizes the input to a
///     bilevel BMP (the policy-allowed intermediate; PNM is denied by policy.xml), then
///     potrace traces the BMP to SVG.
/// All methods return the SVG document as UTF-8 bytes.
/// </summary>
public interface ITracingService
{
    /// <summary>
    /// Traces a color raster image into a multi-color SVG via vtracer. <paramref name="input"/>
    /// should be PNG bytes; if it is another format it is first normalized to PNG with magick
    /// (vtracer only reads PNG/JPG-family rasters). Returns the SVG document as UTF-8 bytes.
    /// Throws <see cref="ImagingException"/> on a non-zero tracer exit and
    /// <see cref="ArgumentException"/> on empty input.
    /// </summary>
    Task<byte[]> TraceColorAsync(byte[] input, TraceColorOptions options, CancellationToken ct = default);

    /// <summary>
    /// Traces a black &amp; white raster image into a single-color SVG via potrace. The input
    /// is first converted to a bilevel BMP with magick (using <see cref="TraceMonochromeOptions.Threshold"/>),
    /// then potrace traces that BMP. Returns the SVG document as UTF-8 bytes. Throws
    /// <see cref="ImagingException"/> on a non-zero magick/potrace exit and
    /// <see cref="ArgumentException"/> on empty input.
    /// </summary>
    Task<byte[]> TraceMonochromeAsync(byte[] input, TraceMonochromeOptions options, CancellationToken ct = default);
}
