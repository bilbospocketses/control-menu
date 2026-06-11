namespace ControlMenu.Modules.Imaging.Services.Options;

public record RasterizeOptions
{
    public int[] Sizes { get; init; } = [256, 512];
    /// <summary>"png" or "ico". For "ico", all selected sizes bundle into one .ico.</summary>
    public string OutputFormat { get; init; } = "png";
    /// <summary>"transparent" or a hex color like "#ffffff".</summary>
    public string Background { get; init; } = "transparent";
}
