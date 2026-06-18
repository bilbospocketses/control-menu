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

            var json = await ReadFullTextMessageAsync(ws, cts.Token);
            return json is null ? null : JsonSerializer.Deserialize<ScrcpyProbeResult>(json);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or JsonException or UriFormatException)
        {
            _logger.LogWarning(ex, "Probe failed for {Udid}", udid);
            return null;
        }
    }

    /// <summary>
    /// Reads a complete WebSocket text message, accumulating fragments until
    /// <see cref="WebSocketReceiveResult.EndOfMessage"/>. A single <c>ReceiveAsync</c> only
    /// returns the first ≤8 KiB frame, which truncated larger probe payloads (long
    /// videoEncoders/audioEncoders lists). Returns null for a Close/non-Text message or if the
    /// payload exceeds a 1 MiB safety cap.
    /// </summary>
    internal static async Task<string?> ReadFullTextMessageAsync(WebSocket ws, CancellationToken ct)
    {
        const int maxProbeBytes = 1024 * 1024;
        var buffer = new byte[8192];
        using var ms = new MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, result.Count);
            if (ms.Length > maxProbeBytes) return null;
        } while (!result.EndOfMessage);

        if (result.MessageType != WebSocketMessageType.Text) return null;
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }
}
