namespace ControlMenu.Modules.Cameras.Network;

public interface IOnvifDiscoveryClient
{
    Task<IReadOnlyList<OnvifProbeResponse>> ProbeAsync(TimeSpan timeout, CancellationToken ct);
}

public sealed record OnvifProbeResponse(
    string IpAddress,
    string? Manufacturer,
    string? Model,
    string OnvifServiceUrl)
{
    public string? FriendlyName { get; init; }
    public string? Location { get; init; }
}
