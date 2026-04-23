using System.Text.Json.Serialization;

namespace ControlMenu.Services;

public record ScrcpyProbeResult(
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("density")] int Density,
    [property: JsonPropertyName("videoEncoders")] string[] VideoEncoders,
    [property: JsonPropertyName("audioEncoders")] string[] AudioEncoders);
