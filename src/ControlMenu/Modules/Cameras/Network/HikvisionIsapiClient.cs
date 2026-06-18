using System.Net;
using System.Net.Http.Headers;
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
        const string requestPath = "/ISAPI/System/deviceInfo";
        try
        {
            // Pooled client from IHttpClientFactory: it recycles a shared SocketsHttpHandler, so a
            // rapid camera scan no longer leaks a socket per probe (the previous code newed up an
            // HttpClientHandler + HttpClient on every call). Credentials are attached per request in
            // SendAuthenticatedAsync rather than living on the shared handler.
            var http = _httpFactory.CreateClient("hikvision-isapi");
            var url = $"http://{ipAddress}:{port}{requestPath}";

            using var response = await SendAuthenticatedAsync(http, url, requestPath, username, password, ct);
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

    /// <summary>
    /// Sends an unauthenticated GET, then answers a 401 Digest (preferred) or Basic challenge.
    /// This covers BOTH older Hikvision firmware (V5.6.2 era) that requires Digest and newer
    /// firmware that accepts Basic. Credentials are never sent preemptively.
    /// </summary>
    private static async Task<HttpResponseMessage> SendAuthenticatedAsync(
        HttpClient http, string url, string requestPath, string username, string password, CancellationToken ct)
    {
        var first = await http.GetAsync(url, ct);
        if (first.StatusCode != HttpStatusCode.Unauthorized) return first;

        var authValue = BuildChallengeResponse(first.Headers.WwwAuthenticate, username, password, requestPath);
        if (authValue is null) return first;   // no challenge we can answer → surface the 401
        first.Dispose();

        var retry = new HttpRequestMessage(HttpMethod.Get, url);
        retry.Headers.TryAddWithoutValidation("Authorization", authValue);
        return await http.SendAsync(retry, ct);
    }

    private static string? BuildChallengeResponse(
        IEnumerable<AuthenticationHeaderValue> wwwAuthenticate, string username, string password, string requestPath)
    {
        var challenges = wwwAuthenticate.ToList();

        var digest = challenges.FirstOrDefault(h => h.Scheme.Equals("Digest", StringComparison.OrdinalIgnoreCase));
        if (digest?.Parameter is { } digestParams)
            return DigestAuthHelper.BuildDigest(
                DigestAuthHelper.ParseChallengeParams(digestParams), username, password, "GET", requestPath);

        if (challenges.Any(h => h.Scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase)))
            return DigestAuthHelper.BuildBasic(username, password);

        return null;
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
