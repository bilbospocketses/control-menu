using ControlMenu.Services;
using ControlMenu.Services.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Services;

public class NetworkScanServiceTests
{
    private readonly Mock<IConfigurationService> _mockConfig = new();
    private readonly WsScrcpyService _wsScrcpy;

    public NetworkScanServiceTests()
    {
        // Shared mock so we can stub wsscrcpy-mode / wsscrcpy-url for WsScrcpyService.
        var services = new ServiceCollection();
        services.AddScoped(_ => _mockConfig.Object);
        var provider = services.BuildServiceProvider();
        _wsScrcpy = new WsScrcpyService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WsScrcpyService>.Instance);
    }

    private NetworkScanService CreateService() => new(_wsScrcpy);

    [Fact]
    public void Initial_Phase_IsIdle()
    {
        var svc = CreateService();
        Assert.Equal(ScanPhase.Idle, svc.Phase);
    }

    [Fact]
    public void Subscribe_WhenIdle_ReceivesNoSnapshotEvents()
    {
        var svc = CreateService();
        var received = new List<ScanEvent>();
        using var sub = svc.Subscribe(e => received.Add(e));
        Assert.Empty(received);
    }

    [Fact]
    public void Subscribe_MidScan_ReceivesSnapshotReplay()
    {
        var svc = CreateService();
        // Simulate a running scan by directly feeding events through the internal bus.
        svc.TestOnlyInject(new ScanStartedEvent(256, 1, 0));
        svc.TestOnlyInject(new ScanProgressEvent(10, 256, 0));
        svc.TestOnlyInject(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "192.168.86.43:5555", "ABC123", "adb-ABC123", "", null)));

        var received = new List<ScanEvent>();
        using var sub = svc.Subscribe(e => received.Add(e));
        // Snapshot replay: last ScanStarted, last ScanProgress, all hits so far.
        Assert.Equal(3, received.Count);
        Assert.IsType<ScanStartedEvent>(received[0]);
        Assert.IsType<ScanProgressEvent>(received[1]);
        Assert.IsType<ScanHitEvent>(received[2]);
    }

    [Fact]
    public void Unsubscribe_StopsReceivingEvents()
    {
        var svc = CreateService();
        var received = new List<ScanEvent>();
        var sub = svc.Subscribe(e => received.Add(e));
        sub.Dispose();
        svc.TestOnlyInject(new ScanProgressEvent(1, 1, 0));
        Assert.Empty(received);
    }

    [Fact]
    public void Hits_ReflectsInjectedHitEvents()
    {
        var svc = CreateService();
        var hit1 = new ScanHit(DiscoverySource.Tcp, "10.0.0.5:5555", "S1", "n1", "", null);
        var hit2 = new ScanHit(DiscoverySource.Mdns, "10.0.0.6:5555", "S2", "n2", "", "aa:bb:cc:dd:ee:ff");
        svc.TestOnlyInject(new ScanStartedEvent(2, 1, 0));
        svc.TestOnlyInject(new ScanHitEvent(hit1));
        svc.TestOnlyInject(new ScanHitEvent(hit2));
        Assert.Equal(2, svc.Hits.Count);
        Assert.Contains(hit1, svc.Hits);
        Assert.Contains(hit2, svc.Hits);
    }

    [Fact]
    public void StartedEvent_ClearsHitsAndProgress()
    {
        var svc = CreateService();
        svc.TestOnlyInject(new ScanStartedEvent(2, 1, 0));
        svc.TestOnlyInject(new ScanHitEvent(new ScanHit(DiscoverySource.Tcp, "x:1", "", "", "", null)));
        svc.TestOnlyInject(new ScanStartedEvent(5, 1, 100));  // second scan starts
        Assert.Empty(svc.Hits);
    }

    [Fact]
    public void ErrorEvent_TransitionsPhaseToIdle_NoReplayForLateSubscribers()
    {
        // ScanErrorEvent is transient by design — start-time failures (e.g.
        // ws-scrcpy-web unreachable) leave the phase Idle so a subsequent
        // retry starts clean. The error surfaces via the at-the-moment
        // subscriber callback; it does not replay.
        var svc = CreateService();
        var firstReceived = new List<ScanEvent>();
        using var firstSub = svc.Subscribe(e => firstReceived.Add(e));
        svc.TestOnlyInject(new ScanErrorEvent("connection refused"));

        Assert.Equal(ScanPhase.Idle, svc.Phase);
        Assert.Single(firstReceived);
        Assert.IsType<ScanErrorEvent>(firstReceived[0]);

        var lateReceived = new List<ScanEvent>();
        using var lateSub = svc.Subscribe(e => lateReceived.Add(e));
        Assert.Empty(lateReceived);
    }

    [Fact]
    public void Subscribe_AfterComplete_ReplaysLastScan()
    {
        var svc = CreateService();
        svc.TestOnlyInject(new ScanStartedEvent(3, 1, 0));
        svc.TestOnlyInject(new ScanHitEvent(new ScanHit(DiscoverySource.Tcp, "10.0.0.5:5555", "", "", "", null)));
        svc.TestOnlyInject(new ScanCompleteEvent(1));

        var received = new List<ScanEvent>();
        using var sub = svc.Subscribe(e => received.Add(e));
        // Phase stays Complete so replay fires: Started, Hit. No progress event was ever sent.
        Assert.Equal(2, received.Count);
        Assert.IsType<ScanStartedEvent>(received[0]);
        Assert.IsType<ScanHitEvent>(received[1]);
    }

    [Fact]
    public void SnapshotReplay_PreservesHitInsertionOrder()
    {
        var svc = CreateService();
        var first = new ScanHit(DiscoverySource.Tcp, "10.0.0.5:5555", "S1", "first", "", null);
        var second = new ScanHit(DiscoverySource.Mdns, "10.0.0.6:5555", "S2", "second", "", null);
        svc.TestOnlyInject(new ScanStartedEvent(2, 1, 0));
        svc.TestOnlyInject(new ScanHitEvent(first));
        svc.TestOnlyInject(new ScanHitEvent(second));

        var received = new List<ScanEvent>();
        using var sub = svc.Subscribe(e => received.Add(e));
        // Expected: Started, Hit(first), Hit(second).
        Assert.Equal(3, received.Count);
        Assert.IsType<ScanStartedEvent>(received[0]);
        Assert.Same(first, ((ScanHitEvent)received[1]).Hit);
        Assert.Same(second, ((ScanHitEvent)received[2]).Hit);
    }

    // -------------------------------------------------------------------------
    // Integration tests — require FakeWsScanServer (Task 8)
    // -------------------------------------------------------------------------

    private async Task ConfigureWsScrcpyForAsync(string baseUrl)
    {
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-mode", It.IsAny<string?>())).ReturnsAsync("external");
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-url", It.IsAny<string?>())).ReturnsAsync(baseUrl);
        await _wsScrcpy.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartScan_HappyPath_StreamsEvents()
    {
        await using var fakeServer = new FakeWsScanServer();
        await ConfigureWsScrcpyForAsync(fakeServer.Url);
        var svc = CreateService();

        var received = new List<ScanEvent>();
        using var sub = svc.Subscribe(e => received.Add(e));

        var subnets = new[] { new ParsedSubnet("10.0.0.0/29", "10.0.0.0/29", 6) };
        var startTask = svc.StartScanAsync(subnets);

        var serverSocket = await fakeServer.GetClientAsync(TimeSpan.FromSeconds(5));

        // Verify the client sent scan.start with the raw subnet string.
        var clientMsg = await fakeServer.ReceiveAsync<System.Text.Json.JsonElement>(serverSocket);
        Assert.Equal("scan.start", clientMsg.GetProperty("type").GetString());
        var sentSubnets = clientMsg.GetProperty("subnets");
        Assert.Equal(1, sentSubnets.GetArrayLength());
        Assert.Equal("10.0.0.0/29", sentSubnets[0].GetString());

        // Server scripts the reply sequence.
        await fakeServer.SendAsync(serverSocket, new { type = "scan.started", totalHosts = 6, totalSubnets = 1, startedAt = 0L });
        await fakeServer.SendAsync(serverSocket, new { type = "scan.progress", @checked = 3, total = 6, foundSoFar = 0 });
        await fakeServer.SendAsync(serverSocket, new { type = "scan.hit", source = "tcp", address = "10.0.0.5:5555", serial = "xyz", name = "adb-xyz", label = "" });
        await fakeServer.SendAsync(serverSocket, new { type = "scan.complete", found = 1 });

        await startTask;
        // Allow the receive loop to drain.
        await Task.Delay(300);

        Assert.Contains(received, e => e is ScanStartedEvent started && started.TotalHosts == 6);
        Assert.Contains(received, e => e is ScanProgressEvent p && p.Checked == 3);
        Assert.Contains(received, e => e is ScanHitEvent h && h.Hit.Address == "10.0.0.5:5555" && h.Hit.Source == DiscoverySource.Tcp);
        Assert.Contains(received, e => e is ScanCompleteEvent c && c.Found == 1);
        Assert.Equal(ScanPhase.Complete, svc.Phase);
    }

    [Fact]
    public async Task StartScan_WhenUnreachable_EmitsScanError()
    {
        // Configure a URL that isn't listening (random free port, no server).
        var unreachablePort = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        unreachablePort.Start();
        var port = ((System.Net.IPEndPoint)unreachablePort.LocalEndpoint).Port;
        unreachablePort.Stop();
        await ConfigureWsScrcpyForAsync($"http://localhost:{port}");

        var svc = CreateService();
        var received = new List<ScanEvent>();
        using var sub = svc.Subscribe(e => received.Add(e));

        await svc.StartScanAsync(new[] { new ParsedSubnet("10.0.0.0/29", "10.0.0.0/29", 6) });

        Assert.Contains(received, e => e is ScanErrorEvent err && err.Reason.Contains("unreachable"));
        Assert.Equal(ScanPhase.Idle, svc.Phase);  // error does NOT transition to Scanning
    }

    [Fact]
    public async Task StartScan_WhileScanning_Throws()
    {
        await using var fakeServer = new FakeWsScanServer();
        await ConfigureWsScrcpyForAsync(fakeServer.Url);
        var svc = CreateService();
        using var sub = svc.Subscribe(_ => { });

        var subnets = new[] { new ParsedSubnet("10.0.0.0/29", "10.0.0.0/29", 6) };
        var first = svc.StartScanAsync(subnets);
        var serverSocket = await fakeServer.GetClientAsync(TimeSpan.FromSeconds(5));
        await fakeServer.ReceiveAsync<object>(serverSocket);
        await fakeServer.SendAsync(serverSocket, new { type = "scan.started", totalHosts = 6, totalSubnets = 1, startedAt = 0L });
        await Task.Delay(100);  // let scan.started flow through Dispatch

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.StartScanAsync(subnets));
    }

    // -------------------------------------------------------------------------
    // Task 9 — CancelAsync + cancel/drop integration tests
    // -------------------------------------------------------------------------

    // Small helper: create a TCS that completes when a matching event arrives.
    // Avoids Task.Delay timing flakiness in drain/phase assertions.
    private static (Task<T> task, IDisposable sub) WaitForEvent<T>(INetworkScanService svc) where T : ScanEvent
    {
        var tcs = new TaskCompletionSource<T>();
        var sub = svc.Subscribe(e =>
        {
            if (e is T match) tcs.TrySetResult(match);
        });
        return (tcs.Task, sub);
    }

    [Fact]
    public async Task Cancel_DuringScanning_TransitionsThroughDraining()
    {
        await using var fakeServer = new FakeWsScanServer();
        await ConfigureWsScrcpyForAsync(fakeServer.Url);
        var svc = CreateService();

        var (startedTask, startedSub) = WaitForEvent<ScanStartedEvent>(svc);
        using var startedSubDisposable = startedSub;

        var startTask = svc.StartScanAsync(new[] { new ParsedSubnet("10.0.0.0/29", "10.0.0.0/29", 6) });
        var serverSocket = await fakeServer.GetClientAsync(TimeSpan.FromSeconds(5));
        var startMsg = await fakeServer.ReceiveAsync<object>(serverSocket);  // drain scan.start
        await fakeServer.SendAsync(serverSocket, new { type = "scan.started", totalHosts = 6, totalSubnets = 1, startedAt = 0L });

        // Wait for scan.started to flow through Dispatch so Phase flips to Scanning.
        await startedTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ScanPhase.Scanning, svc.Phase);

        // Subscribe BEFORE calling Cancel so we catch the Draining + Cancelled events.
        var (cancelledTask, cancelledSub) = WaitForEvent<ScanCancelledEvent>(svc);
        using var cancelledSubDisposable = cancelledSub;

        await svc.CancelAsync();

        // Server receives scan.cancel, scripts draining + cancelled replies.
        var cancelMsg = await fakeServer.ReceiveAsync<System.Text.Json.JsonElement>(serverSocket);
        Assert.Equal("scan.cancel", cancelMsg.GetProperty("type").GetString());
        await fakeServer.SendAsync(serverSocket, new { type = "scan.draining" });
        await fakeServer.SendAsync(serverSocket, new { type = "scan.cancelled", found = 0 });

        await cancelledTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ScanPhase.Cancelled, svc.Phase);
    }

    [Fact]
    public async Task Cancel_WhenIdle_IsNoOp()
    {
        // Before any scan, _ws is null → CancelAsync returns silently.
        await ConfigureWsScrcpyForAsync("http://localhost:8000");
        var svc = CreateService();
        await svc.CancelAsync();  // must not throw
        Assert.Equal(ScanPhase.Idle, svc.Phase);
    }

    [Fact]
    public async Task WsDropsMidScan_ForcesCancelled()
    {
        await using var fakeServer = new FakeWsScanServer();
        await ConfigureWsScrcpyForAsync(fakeServer.Url);
        var svc = CreateService();

        var (startedTask, startedSub) = WaitForEvent<ScanStartedEvent>(svc);
        using var startedSubDisposable2 = startedSub;

        var startTask2 = svc.StartScanAsync(new[] { new ParsedSubnet("10.0.0.0/29", "10.0.0.0/29", 6) });
        var serverSocket = await fakeServer.GetClientAsync(TimeSpan.FromSeconds(5));
        var startMsg2 = await fakeServer.ReceiveAsync<object>(serverSocket);
        await fakeServer.SendAsync(serverSocket, new { type = "scan.started", totalHosts = 6, totalSubnets = 1, startedAt = 0L });
        await startedTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Subscribe before forcing the drop.
        var (cancelledTask, cancelledSub) = WaitForEvent<ScanCancelledEvent>(svc);
        using var cancelledSubDisposable2 = cancelledSub;

        // Server-side close without sending scan.cancelled — simulates container restart.
        // CloseOutputAsync sends the Close frame without waiting for the client echo;
        // this avoids a handshake race between the server-side test code and the
        // receive loop's finally-block CloseAsync. The SUT sees a Close frame and
        // dispatches ScanCancelledEvent — that's all we need to verify.
        await serverSocket.CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus.EndpointUnavailable, "boom", CancellationToken.None);

        await cancelledTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ScanPhase.Cancelled, svc.Phase);
    }
}
