namespace ControlMenu.Modules.Imaging.Services.Options;

/// <summary>
/// Knobs for monochrome (black &amp; white) raster -> SVG tracing via potrace 1.16.
///
/// IMPORTANT: potrace cannot read PNG. The pipeline is
///   input PNG --(magick)--> bilevel BMP --(potrace)--> SVG.
/// BMP is used as the intermediate (NOT PGM/PBM) because the hardened magick policy.xml
/// allowlist permits BMP but DENIES the PNM coders. <see cref="Threshold"/> drives magick's
/// black/white cutoff when producing the bilevel BMP; the remaining knobs map to potrace flags
/// (verified against <c>potrace --help</c>):
///   TurdSize  -> -t/--turdsize &lt;n&gt;   (suppress speckles up to this size)
///   AlphaMax  -> -a/--alphamax &lt;n&gt;   (corner threshold; lower = more corners)
///   OptimizeCurve(false) -> -n/--longcurve (turn OFF curve optimization)
///   Invert    -> -i/--invert         (invert black/white)
/// Defaults mirror potrace's own defaults.
/// </summary>
public record TraceMonochromeOptions
{
    /// <summary>
    /// Black/white cutoff as a fraction 0.0-1.0, applied by magick (<c>-threshold N%</c>) when
    /// rasterizing the input down to a bilevel BMP before potrace runs. 0.5 (50%) is the
    /// neutral midpoint; lower keeps more pixels white, higher keeps more black.
    /// </summary>
    public double Threshold { get; init; } = 0.5;

    /// <summary>
    /// potrace <c>-t/--turdsize</c>: suppress speckles of up to this many pixels.
    /// potrace's default is 2.
    /// </summary>
    public int TurdSize { get; init; } = 2;

    /// <summary>
    /// potrace <c>-a/--alphamax</c>: corner-rounding threshold. Higher = smoother/rounder
    /// corners, lower = sharper. potrace's default is 1.0.
    /// </summary>
    public double AlphaMax { get; init; } = 1.0;

    /// <summary>
    /// When false, passes potrace <c>-n/--longcurve</c> to turn OFF curve optimization
    /// (more, shorter segments). Default true = optimization ON (potrace's default).
    /// </summary>
    public bool OptimizeCurve { get; init; } = true;

    /// <summary>potrace <c>-i/--invert</c>: invert the bitmap (trace white-on-black). Default false.</summary>
    public bool Invert { get; init; }
}
