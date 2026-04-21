using System.Text.Json;
using System.Text.Json.Serialization;
using ControlMenu.Services;

namespace ControlMenu.Services.Network;

public sealed record DetectedSubnet(
    [property: JsonPropertyName("cidr")] string Cidr,
    [property: JsonPropertyName("hostCount")] int HostCount,
    [property: JsonPropertyName("source")] string Source);

public sealed class SubnetDetectionClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly WsScrcpyService _wsscrcpy;

    public SubnetDetectionClient(IHttpClientFactory httpFactory, WsScrcpyService wsscrcpy)
    {
        _httpFactory = httpFactory;
        _wsscrcpy = wsscrcpy;
    }

    /// <summary>
    /// Queries ws-scrcpy-web's <c>GET /api/devices/scan/subnet</c> endpoint
    /// (which runs <c>SubnetDetector.detectSubnet()</c> on the ws-scrcpy-web host).
    /// Returns the detected subnet, or null if the server returns a non-2xx or
    /// the call fails for any reason.
    /// </summary>
    public async Task<DetectedSubnet?> DetectAsync(CancellationToken ct = default)
    {
        try
        {
            var http = _httpFactory.CreateClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var url = $"{_wsscrcpy.BaseUrl.TrimEnd('/')}/api/devices/scan/subnet";
            var resp = await http.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(cts.Token);
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null") return null;
            return JsonSerializer.Deserialize<DetectedSubnet>(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException)
        {
            return null;
        }
    }
}
