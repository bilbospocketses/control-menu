namespace ControlMenu.Modules.Imaging.Services.Options;

public record ConvertFormatOptions
{
    /// <summary>Quality 0-100 for lossy formats (JPG, WebP, AVIF). Default 90.</summary>
    public int Quality { get; init; } = 90;
}
