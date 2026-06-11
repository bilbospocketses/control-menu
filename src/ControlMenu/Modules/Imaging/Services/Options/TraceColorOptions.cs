namespace ControlMenu.Modules.Imaging.Services.Options;

/// <summary>
/// Knobs for color raster -> SVG tracing via vtracer (visioncortex VTracer 0.6.4).
/// Only the v1 subset of vtracer flags that we expose is modelled here; every property
/// maps to a real vtracer CLI option (verified against <c>vtracer --help</c>):
///   ColorMode      -> --colormode &lt;color|bw&gt;
///   Hierarchical   -> --hierarchical &lt;stacked|cutout&gt; (color mode only)
///   Mode           -> --mode &lt;pixel|polygon|spline&gt;
///   FilterSpeckle  -> -f/--filter_speckle &lt;px&gt;
///   ColorPrecision -> -p/--color_precision &lt;bits&gt;
///   PathPrecision  -> --path_precision &lt;decimals&gt;
/// Defaults mirror vtracer's own defaults so an options-less call behaves like the bare CLI.
/// </summary>
public record TraceColorOptions
{
    /// <summary>vtracer <c>--colormode</c>: "color" (default) or "bw" (binary).</summary>
    public string ColorMode { get; init; } = "color";

    /// <summary>
    /// vtracer <c>--hierarchical</c>: "stacked" (default) or "cutout". Only applies in
    /// color mode (ignored by vtracer for bw).
    /// </summary>
    public string Hierarchical { get; init; } = "stacked";

    /// <summary>vtracer <c>-m/--mode</c> curve-fitting: "pixel", "polygon", or "spline" (default).</summary>
    public string Mode { get; init; } = "spline";

    /// <summary>
    /// vtracer <c>-f/--filter_speckle</c>: discard patches smaller than this many pixels.
    /// vtracer's default is 4.
    /// </summary>
    public int FilterSpeckle { get; init; } = 4;

    /// <summary>
    /// vtracer <c>-p/--color_precision</c>: number of significant bits per RGB channel.
    /// vtracer's default is 6.
    /// </summary>
    public int ColorPrecision { get; init; } = 6;

    /// <summary>
    /// vtracer <c>--path_precision</c>: number of decimal places in path coordinates.
    /// vtracer's default is 8.
    /// </summary>
    public int PathPrecision { get; init; } = 8;
}
