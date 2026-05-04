using System.Net;
using System.Net.Sockets;
using System.Text;
using ControlMenu.Modules.Cameras.Network;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlMenu.Tests.Modules.Cameras.Network;

public class RtspProbeClientTests
{
    private readonly RtspProbeClient _sut = new(NullLogger<RtspProbeClient>.Instance);

    [Fact]
    public async Task ProbeTcpAsync_ReturnsTrue_WhenPortListening()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var result = await _sut.ProbeTcpAsync("127.0.0.1", port, TimeSpan.FromSeconds(1), default);
            Assert.True(result);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ProbeTcpAsync_ReturnsFalse_WhenPortClosed()
    {
        // Port 1 is privileged + virtually never open
        var result = await _sut.ProbeTcpAsync("127.0.0.1", 1, TimeSpan.FromMilliseconds(500), default);
        Assert.False(result);
    }

    [Fact]
    public async Task DescribeAsync_Returns200_WithSdpBody_WhenServerAccepts()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buf = new byte[4096];
            await stream.ReadAsync(buf);
            var resp = "RTSP/1.0 200 OK\r\nCSeq: 1\r\nContent-Type: application/sdp\r\nContent-Length: 22\r\n\r\nv=0\r\no=- 0 0 IN IP4 0\r\n";
            var bytes = Encoding.ASCII.GetBytes(resp);
            await stream.WriteAsync(bytes);
        });
        try
        {
            var result = await _sut.DescribeAsync($"rtsp://127.0.0.1:{port}/test", TimeSpan.FromSeconds(2), default);
            Assert.True(result.Success);
            Assert.Equal(200, result.StatusCode);
            Assert.NotNull(result.Sdp);
            Assert.Contains("v=0", result.Sdp);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task DescribeAsync_Returns401_OnUnauthorized()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buf = new byte[4096];
            await stream.ReadAsync(buf);
            var resp = "RTSP/1.0 401 Unauthorized\r\nCSeq: 1\r\nWWW-Authenticate: Basic realm=\"cam\"\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(resp));
        });
        try
        {
            var result = await _sut.DescribeAsync($"rtsp://user:pass@127.0.0.1:{port}/test", TimeSpan.FromSeconds(2), default);
            Assert.False(result.Success);
            Assert.Equal(401, result.StatusCode);
        }
        finally
        {
            listener.Stop();
        }
    }
}
