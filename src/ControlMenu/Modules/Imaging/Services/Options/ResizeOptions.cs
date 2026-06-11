namespace ControlMenu.Modules.Imaging.Services.Options;

public enum ResizeMode { PixelDimensions, Percentage, MaxDimensionFit }

public record ResizeOptions
{
    public ResizeMode Mode { get; init; } = ResizeMode.PixelDimensions;
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? Percentage { get; init; }
    public int? MaxDimension { get; init; }
    public bool LockAspect { get; init; } = true;
}
