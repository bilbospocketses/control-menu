using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ControlMenu.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Services;

public class ScrcpyProbeServiceTests
{
    [Fact]
    public async Task ReadFullTextMessageAsync_AssemblesMessageSpanningMultipleFrames()
    {
        // A realistic probe response with a long videoEncoders list easily exceeds one 8 KiB frame.
        var encoders = string.Join(",", Enumerable.Range(0, 2000).Select(i => $"\"encoder_number_{i}\""));
        var full = $"{{\"width\":1080,\"height\":1920,\"density\":480,\"videoEncoders\":[{encoders}],\"audioEncoders\":[],\"sdkInt\":34}}";
        var bytes = Encoding.UTF8.GetBytes(full);
        Assert.True(bytes.Length > 8192, "test payload must span multiple frames");

        var ws = ScriptedWebSocket.Fragmented(bytes, frameSize: 4096, WebSocketMessageType.Text);

        var json = await ScrcpyProbeService.ReadFullTextMessageAsync(ws, default);

        Assert.Equal(full, json);
        var parsed = JsonSerializer.Deserialize<ScrcpyProbeResult>(json!);
        Assert.Equal(1080, parsed!.Width);
        Assert.Equal(2000, parsed.VideoEncoders.Length);   // nothing truncated
    }

    [Fact]
    public async Task ReadFullTextMessageAsync_ReturnsNull_OnCloseFrame()
    {
        var ws = new ScriptedWebSocket((Array.Empty<byte>(), true, WebSocketMessageType.Close));
        var json = await ScrcpyProbeService.ReadFullTextMessageAsync(ws, default);
        Assert.Null(json);
    }

    [Fact]
    public async Task ProbeAsync_DialsTheUrlConfiguredNow_NotTheOneCachedAtStartup()
    {
        // A device probe against the URL resolved at startup keeps dialling the old host after
        // the user changes the setting -- so a probe against a moved ws-scrcpy-web can only fail.
        var live = new TcpListener(IPAddress.Loopback, 0);
        live.Start();
        try
        {
            var livePort = ((IPEndPoint)live.LocalEndpoint).Port;

            var dead = new TcpListener(IPAddress.Loopback, 0);
            dead.Start();
            var deadPort = ((IPEndPoint)dead.LocalEndpoint).Port;
            dead.Stop();

            var config = new Mock<IConfigurationService>();
            config.Setup(c => c.GetSettingAsync("wsscrcpy-url", It.IsAny<string?>()))
                  .ReturnsAsync($"http://127.0.0.1:{deadPort}");
            var provider = new ServiceCollection().AddScoped(_ => config.Object).BuildServiceProvider();
            var wsScrcpy = new WsScrcpyService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Mock.Of<IHttpClientFactory>(),
                NullLogger<WsScrcpyService>.Instance);
            await wsScrcpy.StartAsync(CancellationToken.None);

            config.Setup(c => c.GetSettingAsync("wsscrcpy-url", It.IsAny<string?>()))
                  .ReturnsAsync($"http://127.0.0.1:{livePort}");

            var accepted = live.AcceptTcpClientAsync();
            var svc = new ScrcpyProbeService(wsScrcpy, NullLogger<ScrcpyProbeService>.Instance);
            var probe = svc.ProbeAsync("device-1");

            // The connection arriving at the port configured *now* is the whole assertion:
            // against the cached value nothing ever reaches this listener.
            var first = await Task.WhenAny(accepted, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(accepted, first);

            (await accepted).Dispose();   // refuse the upgrade so the probe gives up promptly
            Assert.Null(await probe);
        }
        finally
        {
            live.Stop();
        }
    }

    /// <summary>A WebSocket that replays scripted receive frames; everything else is inert.</summary>
    private sealed class ScriptedWebSocket : WebSocket
    {
        private readonly Queue<(byte[] Data, bool End, WebSocketMessageType Type)> _frames;

        public ScriptedWebSocket(params (byte[] Data, bool End, WebSocketMessageType Type)[] frames)
            => _frames = new Queue<(byte[], bool, WebSocketMessageType)>(frames);

        public static ScriptedWebSocket Fragmented(byte[] data, int frameSize, WebSocketMessageType type)
        {
            var frames = new List<(byte[], bool, WebSocketMessageType)>();
            for (var offset = 0; offset < data.Length; offset += frameSize)
            {
                var len = Math.Min(frameSize, data.Length - offset);
                frames.Add((data[offset..(offset + len)], offset + len >= data.Length, type));
            }
            return new ScriptedWebSocket(frames.ToArray());
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
        {
            var (data, end, type) = _frames.Dequeue();
            if (type == WebSocketMessageType.Close)
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true,
                    WebSocketCloseStatus.NormalClosure, ""));
            Array.Copy(data, 0, buffer.Array!, buffer.Offset, data.Length);
            return Task.FromResult(new WebSocketReceiveResult(data.Length, type, end));
        }

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override Task CloseAsync(WebSocketCloseStatus s, string? d, CancellationToken c) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus s, string? d, CancellationToken c) => Task.CompletedTask;
        public override void Dispose() { }
        public override Task SendAsync(ArraySegment<byte> b, WebSocketMessageType t, bool e, CancellationToken c) => Task.CompletedTask;
    }
}
