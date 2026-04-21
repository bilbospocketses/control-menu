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
        _wsScrcpy = new WsScrcpyService(
            new Mock<IServiceScopeFactory>().Object,
            _mockConfig.Object,
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
}
