namespace ControlMenu.Modules.Cameras.Network;

public sealed record CameraScanHit(
    string IpAddress,
    int Port,
    bool IsOnvif,
    string? Manufacturer,
    string? Model,
    string? OnvifServiceUrl);
