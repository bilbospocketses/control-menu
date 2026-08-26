using ControlMenu.Modules.Cameras.Entities;
using ControlMenu.Modules.Cameras.Network;
using ControlMenu.Modules.Cameras.Services;
using ControlMenu.Services;
using ControlMenu.Services.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using ControlMenu.Tests.TestHelpers;

namespace ControlMenu.Tests.Modules.Cameras.Network;

public class CameraScanServiceTests
{
    private readonly Mock<IOnvifDiscoveryClient> _onvifDisc = new();
    private readonly Mock<IRtspProbeClient> _rtsp = new();
    private readonly Mock<ICameraService> _cameraSvc = new();
    private readonly Mock<INetworkDiscoveryService> _network = new();

    private IServiceScopeFactory BuildScopeFactory()
    {
        var sp = new Mock<IServiceProvider>();
        sp.Setup(p => p.GetService(typeof(ICameraService))).Returns(_cameraSvc.Object);

        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(sp.Object);

        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }

    private CameraScanService CreateSut()
    {
        _cameraSvc.Setup(s => s.GetAllAsync()).ReturnsAsync(Array.Empty<Camera>());
        _network.Setup(n => n.GetArpTableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ArpEntry>());
        return new CameraScanService(_onvifDisc.Object, _rtsp.Object, _network.Object, BuildScopeFactory(), NullLogger<CameraScanService>.Instance);
    }

    private static ParsedSubnet Subnet(string cidr) => new(cidr, cidr, 254);

    [Fact]
    public async Task StartScanAsync_OnvifHits_PopulateHitsList()
    {
        _onvifDisc.Setup(d => d.ProbeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OnvifProbeResponse>
            {
                new("192.168.1.50", "Hikvision", "DS-2CD2143G0-I", "http://192.168.1.50/onvif/device_service")
            });
        _rtsp.Setup(r => r.ProbeTcpAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut();
        await sut.StartScanAsync(new[] { Subnet("192.168.1.0/24") });

        Assert.Single(sut.Hits);
        Assert.True(sut.Hits[0].IsOnvif);
        Assert.Equal("Hikvision", sut.Hits[0].Manufacturer);
    }

    [Fact]
    public async Task StartScanAsync_TcpHits_NotDuplicatingOnvif()
    {
        _onvifDisc.Setup(d => d.ProbeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OnvifProbeResponse>
            {
                new("192.168.1.50", null, null, "http://192.168.1.50/onvif/device_service")
            });
        _rtsp.Setup(r => r.ProbeTcpAsync("192.168.1.50", 554, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _rtsp.Setup(r => r.ProbeTcpAsync("192.168.1.99", 554, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _rtsp.Setup(r => r.ProbeTcpAsync(It.Is<string>(s => s != "192.168.1.50" && s != "192.168.1.99"), 554, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut();
        await sut.StartScanAsync(new[] { Subnet("192.168.1.0/24") });

        Assert.Equal(2, sut.Hits.Count);
        Assert.Single(sut.Hits, h => h.IsOnvif && h.IpAddress == "192.168.1.50");
        Assert.Single(sut.Hits, h => !h.IsOnvif && h.IpAddress == "192.168.1.99");
    }

    [Fact]
    public async Task StartScanAsync_AlreadyRegisteredCamera_BumpsLastSeen_NotInHits()
    {
        // CreateSut() sets up GetAllAsync → empty; override AFTER so the camera is visible
        var sut = CreateSut();
        _cameraSvc.Setup(s => s.GetAllAsync()).ReturnsAsync(new[]
        {
            new Camera { Id = Guid.NewGuid(), Name = "Existing", IpAddress = "192.168.1.50" }
        });
        _onvifDisc.Setup(d => d.ProbeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OnvifProbeResponse>
            {
                new("192.168.1.50", "Hikvision", "DS-2CD", "http://192.168.1.50/onvif/device_service")
            });
        _rtsp.Setup(r => r.ProbeTcpAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        await sut.StartScanAsync(new[] { Subnet("192.168.1.0/24") });

        Assert.Empty(sut.Hits);
        _cameraSvc.Verify(s => s.UpdateLastSeenAsync(It.IsAny<Guid>()), Times.Once);
    }

    [Fact]
    public async Task StartScanAsync_OnvifBranchFails_RtspBranchStillRuns()
    {
        _onvifDisc.Setup(d => d.ProbeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("multicast bind failed"));
        _rtsp.Setup(r => r.ProbeTcpAsync("192.168.1.99", 554, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _rtsp.Setup(r => r.ProbeTcpAsync(It.Is<string>(s => s != "192.168.1.99"), 554, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut();
        await sut.StartScanAsync(new[] { Subnet("192.168.1.0/24") });

        Assert.Single(sut.Hits);
        Assert.False(sut.Hits[0].IsOnvif);
    }

    [Fact]
    public async Task StartScanAsync_OnvifResponseAfterTcpHit_UpgradesToOnvif()
    {
        // Simulate the race: TCP probe returns first (synchronous mock), ONVIF probe returns later
        // with richer metadata for the SAME IP. The Hits list should end with ONE entry, IsOnvif=true,
        // with the manufacturer/model populated.
        _onvifDisc.Setup(d => d.ProbeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(async (TimeSpan _, CancellationToken _) =>
            {
                // Deliberate latency *inside the mock* to shape the scenario — this is not the test
                // waiting on the SUT, it is the fake ONVIF probe simulating a slower network round
                // trip so its response lands after the (synchronous) TCP probe. Gating it on the TCP
                // probe instead would deadlock if the service awaits ONVIF before starting TCP.
                await Task.Delay(50);
                return new List<OnvifProbeResponse>
                {
                    new("192.168.1.10", "Hikvision", "DS-2CD2143G0-I", "http://192.168.1.10/onvif/device_service")
                };
            });
        _rtsp.Setup(r => r.ProbeTcpAsync("192.168.1.10", 554, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _rtsp.Setup(r => r.ProbeTcpAsync(It.Is<string>(s => s != "192.168.1.10"), 554, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut();
        await sut.StartScanAsync(new[] { Subnet("192.168.1.0/24") });

        Assert.Single(sut.Hits);
        Assert.True(sut.Hits[0].IsOnvif);
        Assert.Equal("Hikvision", sut.Hits[0].Manufacturer);
    }

    [Fact]
    public async Task StartOnvifOnlyScanAsync_DoesNotInvokeRtspProbe()
    {
        // Arrange: ONVIF probe returns no hits (we don't care about hit handling here,
        // just that the TCP-554 branch never fires).
        _onvifDisc.Setup(o => o.ProbeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<OnvifProbeResponse>());

        var sut = CreateSut();

        // Act
        await sut.StartOnvifOnlyScanAsync(new[] { Subnet("192.168.1.0/24") });

        // Assert
        _onvifDisc.Verify(o => o.ProbeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
        _rtsp.Verify(
            r => r.ProbeTcpAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Equal(ScanPhase.Complete, sut.Phase);
    }

    [Fact]
    public async Task StartScanAsync_ConcurrentStarts_StartExactlyOneScan()
    {
        // Gate the ONVIF probe so the winning scan blocks with Phase=Scanning held while a burst of
        // concurrent starts all hit the guard. A non-atomic check-then-set lets several slip past
        // (each emits a CameraScanStartedEvent and clears the in-flight scan's hits); the atomic
        // guard admits exactly one.
        var gate = new TaskCompletionSource();
        _onvifDisc.Setup(d => d.ProbeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(async (TimeSpan _, CancellationToken _) =>
            {
                await gate.Task;
                return new List<OnvifProbeResponse>();
            });
        _rtsp.Setup(r => r.ProbeTcpAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var started = 0;
        var sut = CreateSut();
        using var sub = sut.Subscribe(e => { if (e is CameraScanStartedEvent) Interlocked.Increment(ref started); });

        var subnets = new[] { Subnet("192.168.1.0/24") };
        var tasks = Enumerable.Range(0, 32).Select(_ => Task.Run(() => sut.StartScanAsync(subnets))).ToArray();

        // Wait until a scan has actually begun — a fixed delay flaked on loaded CI runners where the
        // Task.Run bodies hadn't been scheduled yet (started == 0).
        await AsyncSignal.BecomesTrueAsync(
            () => Volatile.Read(ref started) >= 1, "one of the 32 concurrent starts should have begun a scan");

        // Then settle: a non-atomic guard would have let several of the 32 slip past and increment by
        // now, while the atomic guard holds the winner on the gate so the count stays at exactly one.
        // This one is a genuine negative assertion, so a bounded real wait is the only way to make it.
        await AsyncSignal.SettleAsync();

        Assert.Equal(1, Volatile.Read(ref started));

        gate.SetResult();
        await Task.WhenAll(tasks);
        Assert.Equal(1, started);
    }

    [Fact]
    public async Task Subscribe_ReceivesEvents()
    {
        _onvifDisc.Setup(d => d.ProbeAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OnvifProbeResponse>
            {
                new("192.168.1.50", "Hikvision", "X", "http://192.168.1.50/onvif/device_service")
            });
        _rtsp.Setup(r => r.ProbeTcpAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var events = new List<CameraScanEvent>();
        var sut = CreateSut();
        using var sub = sut.Subscribe(events.Add);

        await sut.StartScanAsync(new[] { Subnet("192.168.1.0/24") });

        Assert.Contains(events, e => e is CameraScanStartedEvent);
        Assert.Contains(events, e => e is CameraScanHitEvent);
        Assert.Contains(events, e => e is CameraScanCompletedEvent);
    }
}
