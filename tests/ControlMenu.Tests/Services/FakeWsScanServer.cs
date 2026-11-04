using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ControlMenu.Tests.TestHelpers;

namespace ControlMenu.Tests.Services;

public sealed class FakeWsScanServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<WebSocket> _socketTcs = new();
    public string Url { get; }

    public FakeWsScanServer()
    {
        var port = GetFreePort();
        Url = $"http://localhost:{port}";
        _listener = new HttpListener();
        _listener.Prefixes.Add($"{Url}/");
        _listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    /// <summary>The server end of the client's WebSocket, once it has connected; fails with a HUNG
    /// message if the client never does.</summary>
    public Task<WebSocket> GetClientAsync() =>
        AsyncSignal.ArrivesAsync(_socketTcs.Task, "the client's WebSocket connection to /ws-scan");

    public async Task SendAsync(WebSocket ws, object message)
    {
        var json = JsonSerializer.Serialize(message,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    public async Task<T?> ReceiveAsync<T>(WebSocket ws)
    {
        var buffer = new byte[4096];
        // A client that never sends fails with a HUNG message instead of hanging the run.
        var result = await AsyncSignal.ArrivesAsync(
            ws.ReceiveAsync(buffer, CancellationToken.None), "a message from the client on /ws-scan");
        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        return JsonSerializer.Deserialize<T>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private async Task AcceptLoop()
    {
        try
        {
            var ctx = await _listener.GetContextAsync();
            if (ctx.Request.IsWebSocketRequest && ctx.Request.Url?.AbsolutePath == "/ws-scan")
            {
                var wsCtx = await ctx.AcceptWebSocketAsync(subProtocol: null);
                _socketTcs.TrySetResult(wsCtx.WebSocket);
            }
            else
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
            }
        }
        catch { /* listener shut down */ }
    }

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        _listener.Close();
        await Task.CompletedTask;
    }
}
