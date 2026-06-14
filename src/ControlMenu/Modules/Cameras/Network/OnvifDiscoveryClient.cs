using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace ControlMenu.Modules.Cameras.Network;

public class OnvifDiscoveryClient : IOnvifDiscoveryClient
{
    private static readonly IPEndPoint MulticastEndpoint =
        new(IPAddress.Parse("239.255.255.250"), 3702);
    private static readonly XNamespace SoapNs = "http://www.w3.org/2003/05/soap-envelope";
    private static readonly XNamespace WsdNs = "http://schemas.xmlsoap.org/ws/2005/04/discovery";
    private static readonly XNamespace WsaNs = "http://schemas.xmlsoap.org/ws/2004/08/addressing";

    private readonly ILogger<OnvifDiscoveryClient> _logger;

    public OnvifDiscoveryClient(ILogger<OnvifDiscoveryClient> logger) => _logger = logger;

    public async Task<IReadOnlyList<OnvifProbeResponse>> ProbeAsync(TimeSpan timeout, CancellationToken ct)
    {
        var results = new Dictionary<string, OnvifProbeResponse>(StringComparer.Ordinal);
        var interfaces = EnumerateProbeInterfaces();

        if (interfaces.Count == 0)
        {
            _logger.LogWarning("No suitable IPv4 interfaces found for WS-Discovery probe");
            return [];
        }

        var probeBody = BuildProbeEnvelope();
        var probeBytes = Encoding.UTF8.GetBytes(probeBody);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var probeTasks = interfaces.Select(ip =>
            ProbeViaInterfaceAsync(ip, probeBytes, results, cts.Token)).ToList();

        try { await Task.WhenAll(probeTasks); }
        catch (OperationCanceledException) { /* timeout reached or cancelled */ }
        catch (Exception ex) { _logger.LogWarning(ex, "WS-Discovery probe error"); }

        _logger.LogInformation("WS-Discovery: probed {Count} interface(s), found {Hits} unique device(s)",
            interfaces.Count, results.Count);

        return results.Values.ToList();
    }

    private static List<IPAddress> EnumerateProbeInterfaces()
    {
        var list = new List<IPAddress>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            if (!nic.SupportsMulticast) continue;

            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                var ip = addr.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                // Skip APIPA (169.254/16)
                var bytes = ip.GetAddressBytes();
                if (bytes[0] == 169 && bytes[1] == 254) continue;
                list.Add(ip);
            }
        }
        return list;
    }

    private async Task ProbeViaInterfaceAsync(
        IPAddress localIp,
        byte[] probeBytes,
        Dictionary<string, OnvifProbeResponse> results,
        CancellationToken ct)
    {
        UdpClient? client = null;
        try
        {
            client = new UdpClient();
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(localIp, 0));
            client.MulticastLoopback = false;
            client.Ttl = 1;

            await client.SendAsync(probeBytes, MulticastEndpoint, ct);

            // Listen for responses until ct fires (timeout from caller)
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult recv;
                try { recv = await client.ReceiveAsync(ct); }
                catch (OperationCanceledException) { break; }

                var sourceIp = recv.RemoteEndPoint.Address.ToString();
                var responseText = Encoding.UTF8.GetString(recv.Buffer);
                var parsed = TryParseProbeMatch(sourceIp, responseText);
                if (parsed is null) continue;

                lock (results) results.TryAdd(sourceIp, parsed);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Probe via {LocalIp} failed", localIp);
        }
        finally
        {
            client?.Dispose();
        }
    }

    private static string BuildProbeEnvelope()
    {
        var messageId = Guid.NewGuid();
        return $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <e:Envelope xmlns:e="{{SoapNs}}"
                        xmlns:w="{{WsaNs}}"
                        xmlns:d="{{WsdNs}}"
                        xmlns:dn="http://www.onvif.org/ver10/network/wsdl">
              <e:Header>
                <w:MessageID>uuid:{{messageId}}</w:MessageID>
                <w:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>
                <w:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action>
              </e:Header>
              <e:Body>
                <d:Probe>
                  <d:Types>dn:NetworkVideoTransmitter</d:Types>
                </d:Probe>
              </e:Body>
            </e:Envelope>
            """;
    }

    private static OnvifProbeResponse? TryParseProbeMatch(string sourceIp, string responseXml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(responseXml); }
        catch (Exception) { return null; }

        var probeMatch = doc.Descendants(WsdNs + "ProbeMatch").FirstOrDefault();
        if (probeMatch is null) return null;

        var xaddrs = probeMatch.Element(WsdNs + "XAddrs")?.Value ?? "";
        var serviceUrl = ResolveTrustedServiceUrl(sourceIp, xaddrs);
        if (string.IsNullOrEmpty(serviceUrl)) return null;

        var scopes = probeMatch.Element(WsdNs + "Scopes")?.Value ?? "";
        var (manufacturer, model, friendlyName, location) = ParseScopes(scopes);

        return new OnvifProbeResponse(sourceIp, manufacturer, model, serviceUrl)
        {
            FriendlyName = friendlyName,
            Location = location,
        };
    }

    /// <summary>
    /// Picks the first http(s) URL from a (possibly space-separated) WS-Discovery <c>XAddrs</c>
    /// value and forces its host to <paramref name="sourceIp"/> — the device that actually
    /// answered the probe. The advertised XAddrs host is attacker-controlled (any LAN device can
    /// answer WS-Discovery and name an arbitrary host), so pinning the host to the responder
    /// prevents camera credentials from being POSTed to a foreign host (anti-SSRF /
    /// anti-credential-harvest). The device-advertised scheme, port, and path are preserved; the
    /// responder IP is independently validated in-subnet by <c>CameraScanService</c>.
    /// Returns null when no http(s) URL is present.
    /// </summary>
    internal static string? ResolveTrustedServiceUrl(string sourceIp, string xaddrs)
    {
        var serviceUrl = xaddrs.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(s => s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                              || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(serviceUrl)) return null;

        if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return new UriBuilder(uri) { Host = sourceIp }.Uri.ToString();
    }

    private static (string? Manufacturer, string? Model, string? FriendlyName, string? Location) ParseScopes(string scopesText)
    {
        string? manufacturer = null;
        string? model = null;
        string? friendlyName = null;
        string? location = null;
        foreach (var scope in scopesText.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = scope.TrimEnd('/');
            var lastSlash = trimmed.LastIndexOf('/');
            if (lastSlash < 0) continue;
            var prefix = trimmed[..lastSlash];
            var value = Uri.UnescapeDataString(trimmed[(lastSlash + 1)..]);
            if (string.IsNullOrEmpty(value)) continue;

            if (prefix.EndsWith("/manufacturer", StringComparison.OrdinalIgnoreCase) && manufacturer is null)
                manufacturer = value;
            else if (prefix.EndsWith("/hardware", StringComparison.OrdinalIgnoreCase) && model is null)
                model = value;
            else if (prefix.EndsWith("/name", StringComparison.OrdinalIgnoreCase) && friendlyName is null)
                friendlyName = value;
            else if (prefix.EndsWith("/location", StringComparison.OrdinalIgnoreCase) && location is null)
                location = value;
        }
        return (manufacturer, model, friendlyName, location);
    }
}
