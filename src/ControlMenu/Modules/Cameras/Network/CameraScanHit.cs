namespace ControlMenu.Modules.Cameras.Network;

public sealed record CameraScanHit(
    string IpAddress,
    int Port,
    bool IsOnvif,
    string? Manufacturer,
    string? Model,
    string? OnvifServiceUrl)
{
    public string? FriendlyName { get; init; }
    public string? Location { get; init; }
    public string? MacAddress { get; init; }
}
