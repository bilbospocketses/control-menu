namespace ControlMenu.Modules.Imaging.Services.Options;

public record BackgroundRemoveOptions
{
    public int SeedX { get; init; }
    public int SeedY { get; init; }
    /// <summary>0-100 percent.</summary>
    public int Tolerance { get; init; } = 15;
    public bool Contiguous { get; init; } = true;
}
