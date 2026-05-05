using System.Xml.Linq;

namespace ControlMenu.Modules.Cameras.Network;

public class HikvisionIsapiClient : IHikvisionIsapiClient
{
    private readonly ILogger<HikvisionIsapiClient> _logger;

    public HikvisionIsapiClient(ILogger<HikvisionIsapiClient> logger)
    {
        _logger = logger;
    }

    protected virtual HttpClient CreateHttpClient(string username, string password)
    {
        var handler = new HttpClientHandler
        {
            Credentials = new System.Net.NetworkCredential(username, password),
            PreAuthenticate = false,
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<HikvisionDeviceInfo?> GetDeviceInfoAsync(string ipAddress, int port, string username, string password, CancellationToken ct)
    {
        try
        {
            var url = $"http://{ipAddress}:{port}/ISAPI/System/deviceInfo";
            // Use HttpClientHandler with NetworkCredential to support BOTH Basic and
            // Digest auth via challenge-response. Older Hikvision firmware (V5.6.2 era)
            // requires Digest; newer firmware (V5.7+) accepts Basic. Letting HttpClient
            // negotiate via the WWW-Authenticate header covers both.
            using var http = CreateHttpClient(username, password);
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            return ParseDeviceInfo(body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Hikvision ISAPI deviceInfo fetch failed for {Ip}", ipAddress);
            return null;
        }
    }

    private static HikvisionDeviceInfo? ParseDeviceInfo(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root is null) return null;

            string? Get(string localName) =>
                root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?.Value;

            // Hikvision ISAPI returns MAC with colons (e.g., 14:2f:fd:02:1a:36).
            // Project convention is dashes (matches Device.MacAddress storage).
            var rawMac = Get("macAddress");
            var normalizedMac = string.IsNullOrEmpty(rawMac) ? null : rawMac.Replace(':', '-').ToLowerInvariant();

            var rawTelecontrol = Get("telecontrolID");
            var telecontrolId = int.TryParse(rawTelecontrol, out var n) ? n : (int?)null;

            return new HikvisionDeviceInfo(
                DeviceName: Get("deviceName"),
                Model: Get("model"),
                SerialNumber: Get("serialNumber"),
                FirmwareVersion: Get("firmwareVersion"),
                MacAddress: normalizedMac,
                TelecontrolId: telecontrolId);
        }
        catch
        {
            return null;
        }
    }
}
