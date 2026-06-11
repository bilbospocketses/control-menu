using System.Text;
using ControlMenu.Modules.Imaging.Services;
using ControlMenu.Modules.Imaging.Services.Options;
using SkiaSharp;

namespace ControlMenu.Tests.Modules.Imaging;

/// <summary>
/// Integration tests for <see cref="TracingService"/>. These drive the REAL bundled tracers
/// end-to-end (via <see cref="TracingServiceFixture"/>):
///   * <see cref="TracingService.TraceColorAsync"/> -> vtracer (reads the normalized PNG).
///   * <see cref="TracingService.TraceMonochromeAsync"/> -> magick (PNG->bilevel BMP) then potrace.
/// Both spawn real processes, so every test that needs the engines is gated on
/// <see cref="TracingServiceFixture.EnginesAvailable"/> (all three of magick/vtracer/potrace
/// staged) and Skips rather than fails when the seed isn't present.
///
/// We assert on the ACTUAL SVG output: the bytes must decode to text that opens like an SVG
/// document (XML prolog / &lt;svg&gt; root) and carries real geometry (&lt;path&gt; ... &lt;/svg&gt;).
/// </summary>
[Collection(nameof(TracingServiceCollection))]
public class TracingServiceTests
{
    private readonly TracingServiceFixture _fx;

    public TracingServiceTests(TracingServiceFixture fx) => _fx = fx;

    /// <summary>A multi-color PNG (white bg, red square, blue circle, black bar) so the color
    /// tracer has several regions to vectorize.</summary>
    private static byte[] CreateMultiColorPng(int w, int h)
    {
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using (var red = new SKPaint { Color = SKColors.Red, IsAntialias = false })
                canvas.DrawRect(SKRect.Create(w * 0.10f, h * 0.10f, w * 0.35f, h * 0.35f), red);
            using (var blue = new SKPaint { Color = SKColors.Blue, IsAntialias = false })
                canvas.DrawCircle(w * 0.70f, h * 0.65f, Math.Min(w, h) * 0.20f, blue);
            using (var black = new SKPaint { Color = SKColors.Black, IsAntialias = false })
                canvas.DrawRect(SKRect.Create(w * 0.12f, h * 0.70f, w * 0.30f, h * 0.12f), black);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A black-on-white PNG (white bg with solid black shapes) for the monochrome tracer.</summary>
    private static byte[] CreateBlackOnWhitePng(int w, int h)
    {
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var black = new SKPaint { Color = SKColors.Black, IsAntialias = false };
            canvas.DrawRect(SKRect.Create(w * 0.15f, h * 0.15f, w * 0.40f, h * 0.40f), black);
            canvas.DrawCircle(w * 0.65f, h * 0.65f, Math.Min(w, h) * 0.22f, black);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static string AsText(byte[] svg) => Encoding.UTF8.GetString(svg);

    private static void AssertLooksLikeSvg(byte[] output)
    {
        Assert.NotNull(output);
        Assert.NotEmpty(output);

        var text = AsText(output).TrimStart('﻿').TrimStart();
        // Opens like an SVG document: either an XML prolog or straight into the <svg> root.
        Assert.True(
            text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("<!DOCTYPE svg", StringComparison.OrdinalIgnoreCase),
            $"Output does not open like an SVG document. First 80 chars: '{text[..Math.Min(80, text.Length)]}'");

        // Carries real geometry and closes the root element.
        Assert.Contains("<svg", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<path", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("</svg>", text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task TraceColorAsync_ProducesSvg()
    {
        Skip.IfNot(_fx.EnginesAvailable, "tracing engines (magick/vtracer/potrace) not installed");

        var png = CreateMultiColorPng(96, 96);
        var options = new TraceColorOptions(); // defaults: colormode color, spline, etc.

        byte[] svg = await _fx.Service.TraceColorAsync(png, options);

        AssertLooksLikeSvg(svg);
        // vtracer stamps its generator comment — confirms the COLOR path (vtracer), not potrace.
        Assert.Contains("VTracer", AsText(svg), StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task TraceMonochromeAsync_ProducesSvg()
    {
        Skip.IfNot(_fx.EnginesAvailable, "tracing engines (magick/vtracer/potrace) not installed");

        var png = CreateBlackOnWhitePng(96, 96);
        var options = new TraceMonochromeOptions { Threshold = 0.5 };

        byte[] svg = await _fx.Service.TraceMonochromeAsync(png, options);

        AssertLooksLikeSvg(svg);
    }

    [SkippableFact]
    public async Task TraceColorAsync_EmptyInput_Throws()
    {
        // Empty input is rejected by argument validation BEFORE any process spawns, so this is
        // safe to run even without the engines — but keep it gated for consistency with the suite.
        Skip.IfNot(_fx.EnginesAvailable, "tracing engines (magick/vtracer/potrace) not installed");

        await Assert.ThrowsAsync<ArgumentException>(
            () => _fx.Service.TraceColorAsync([], new TraceColorOptions()));
    }

    [SkippableFact]
    public async Task TraceMonochromeAsync_NonImageInput_ThrowsImagingException()
    {
        Skip.IfNot(_fx.EnginesAvailable, "tracing engines (magick/vtracer/potrace) not installed");

        // Non-empty but not a decodable image: magick's BMP-conversion step must fail and surface
        // as our typed ImagingException, not an unhandled crash.
        var garbage = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0x10, 0x20, 0x42, 0x4D, 0x00 };

        await Assert.ThrowsAsync<ImagingException>(
            () => _fx.Service.TraceMonochromeAsync(garbage, new TraceMonochromeOptions()));
    }
}
