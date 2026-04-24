using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ControlMenu.Services;

public sealed class ScrcpyProbeService : IScrcpyProbeService
{
    private readonly WsScrcpyService _wsScrcpy;
    private readonly ILogger<ScrcpyProbeService> _logger;

    public ScrcpyProbeService(WsScrcpyService wsScrcpy, ILogger<ScrcpyProbeService> logger)
    {
        _wsScrcpy = wsScrcpy;
        _logger = logger;
    }

    public async Task<ScrcpyProbeResult?> ProbeAsync(string udid, CancellationToken ct = default)
    {
        if (!_wsScrcpy.IsRunning) return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            using var ws = new ClientWebSocket();
            var baseUri = new Uri(_wsScrcpy.BaseUrl);
            var wsScheme = baseUri.Scheme == "https" ? "wss" : "ws";
            var probeUri = new Uri($"{wsScheme}://{baseUri.Host}:{baseUri.Port}/?action=probe&udid={Uri.EscapeDataString(udid)}");

            await ws.ConnectAsync(probeUri, cts.Token);

            var buffer = new byte[8192];
            var result = await ws.ReceiveAsync(buffer, cts.Token);

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                return JsonSerializer.Deserialize<ScrcpyProbeResult>(json);
            }

            return null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or JsonException or UriFormatException)
        {
            _logger.LogWarning(ex, "Probe failed for {Udid}", udid);
            return null;
        }
    }
}
