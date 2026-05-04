using Microsoft.Extensions.Logging;
using OnvifDiscovery;

namespace ControlMenu.Modules.Cameras.Network;

public class OnvifDiscoveryClient : IOnvifDiscoveryClient
{
    private readonly ILogger<OnvifDiscoveryClient> _logger;

    public OnvifDiscoveryClient(ILogger<OnvifDiscoveryClient> logger) => _logger = logger;

    public async Task<IReadOnlyList<OnvifProbeResponse>> ProbeAsync(TimeSpan timeout, CancellationToken ct)
    {
        var discovery = new Discovery();
        var responses = new List<OnvifProbeResponse>();
        try
        {
            await foreach (var d in discovery.DiscoverAsync((int)timeout.TotalSeconds, ct))
            {
                var serviceUrl = d.XAddresses?.FirstOrDefault();
                if (string.IsNullOrEmpty(serviceUrl)) continue;
                responses.Add(new OnvifProbeResponse(
                    IpAddress: d.Address,
                    Manufacturer: string.IsNullOrEmpty(d.Mfr) ? null : d.Mfr,
                    Model: string.IsNullOrEmpty(d.Model) ? null : d.Model,
                    OnvifServiceUrl: serviceUrl));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "ONVIF WS-Discovery probe failed");
            return [];
        }
        return responses;
    }
}
