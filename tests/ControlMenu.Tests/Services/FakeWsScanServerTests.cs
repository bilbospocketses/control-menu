using System.Net.WebSockets;

namespace ControlMenu.Tests.Services;

public class FakeWsScanServerTests
{
    [Fact]
    public async Task AcceptsWebSocketConnection()
    {
        await using var server = new FakeWsScanServer();
        using var client = new ClientWebSocket();
        var wsUrl = new Uri(server.Url.Replace("http://", "ws://") + "/ws-scan");
        await client.ConnectAsync(wsUrl, CancellationToken.None);
        var serverSocket = await server.GetClientAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(WebSocketState.Open, serverSocket.State);
    }

    [Fact]
    public async Task RoundTripsJsonMessage()
    {
        await using var server = new FakeWsScanServer();
        using var client = new ClientWebSocket();
        var wsUrl = new Uri(server.Url.Replace("http://", "ws://") + "/ws-scan");
        await client.ConnectAsync(wsUrl, CancellationToken.None);
        var serverSocket = await server.GetClientAsync(TimeSpan.FromSeconds(5));

        await server.SendAsync(serverSocket, new { type = "scan.started", totalHosts = 42, totalSubnets = 1, startedAt = 0 });

        var buffer = new byte[4096];
        var result = await client.ReceiveAsync(buffer, CancellationToken.None);
        var json = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
        Assert.Contains("\"type\":\"scan.started\"", json);
        Assert.Contains("\"totalHosts\":42", json);
    }
}
