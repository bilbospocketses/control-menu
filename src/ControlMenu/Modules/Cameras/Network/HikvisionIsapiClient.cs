using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace ControlMenu.Modules.Cameras.Network;

public class HikvisionIsapiClient : IHikvisionIsapiClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<HikvisionIsapiClient> _logger;

    public HikvisionIsapiClient(IHttpClientFactory httpFactory, ILogger<HikvisionIsapiClient> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<HikvisionDeviceInfo?> GetDeviceInfoAsync(string ipAddress, int port, string username, string password, CancellationToken ct)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);

            var url = $"http://{ipAddress}:{port}/ISAPI/System/deviceInfo";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

            using var response = await http.SendAsync(request, ct);
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
