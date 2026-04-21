using ControlMenu.Data.Entities;
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Services;
using ControlMenu.Services.Network;
using Moq;

namespace ControlMenu.Tests.Services;

public class ScanLifecycleHandlerTests
{
    private readonly FakeNetworkScanService _scan = new();
    private readonly Mock<IAdbService> _adb = new();
    private readonly Mock<INetworkDiscoveryService> _net = new();
    private readonly Mock<IConfigurationService> _config = new();
    private readonly Mock<IDeviceService> _devices = new();

    private ScanLifecycleHandler CreateHandler() =>
        new(_scan, _adb.Object, _net.Object, _config.Object, _devices.Object);

    [Fact]
    public void Constructor_SubscribesToScanService_AndSeedsPhase()
    {
        _scan.Phase = ScanPhase.Scanning;

        using var handler = CreateHandler();

        Assert.Equal(1, _scan.SubscriberCount);
        Assert.Equal(ScanPhase.Scanning, handler.Phase);
    }

    [Fact]
    public void ScanHitEvent_AppendsToDiscovered_WhenAddressNotDismissed()
    {
        using var handler = CreateHandler();
        var stateChanges = 0;
        handler.OnStateChanged += () => stateChanges++;

        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "192.168.1.10:5555", "serial", "my-device", "", "aa:bb:cc:dd:ee:ff")));

        Assert.Single(handler.Discovered);
        Assert.Equal("192.168.1.10", handler.Discovered[0].Ip);
        Assert.Equal(5555, handler.Discovered[0].Port);
        Assert.Equal("my-device", handler.Discovered[0].ServiceName);
        Assert.Equal("aa:bb:cc:dd:ee:ff", handler.Discovered[0].Mac);
        Assert.Equal(1, stateChanges);
    }

    [Fact]
    public void ScanHitEvent_Skipped_WhenAddressIsDismissed()
    {
        using var handler = CreateHandler();
        // Seed one discovered row, then dismiss it.
        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "192.168.1.10:5555", "serial", "first", "", null)));
        handler.Dismiss(handler.Discovered[0]);

        // Re-emit the same address.
        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "192.168.1.10:5555", "serial2", "second", "", null)));

        Assert.Empty(handler.Discovered);
    }

    [Fact]
    public void Dismiss_RemovesFromDiscovered_AndRecordsAddress_AndRaisesStateChanged()
    {
        using var handler = CreateHandler();
        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "10.0.0.5:5555", "serial", "foo", "", null)));
        var stateChanges = 0;
        handler.OnStateChanged += () => stateChanges++;

        handler.Dismiss(handler.Discovered[0]);

        Assert.Empty(handler.Discovered);
        Assert.Equal(1, stateChanges);

        // Second hit at same address is skipped — proves DismissedAddresses is populated.
        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "10.0.0.5:5555", "serial2", "foo2", "", null)));
        Assert.Empty(handler.Discovered);
    }

    [Fact]
    public void ScanProgressEvent_UpdatesLastProgress()
    {
        using var handler = CreateHandler();

        _scan.Emit(new ScanProgressEvent(42, 256, 3));

        Assert.NotNull(handler.LastProgress);
        Assert.Equal(42, handler.LastProgress!.Checked);
        Assert.Equal(256, handler.LastProgress.Total);
        Assert.Equal(3, handler.LastProgress.FoundSoFar);
    }

    [Fact]
    public void ScanErrorEvent_PopulatesConsumableError_OneShot()
    {
        using var handler = CreateHandler();

        _scan.Emit(new ScanErrorEvent("ws-scan connection refused"));

        var first = handler.ConsumeLastError();
        Assert.Equal("ws-scan connection refused", first);

        var second = handler.ConsumeLastError();
        Assert.Null(second);
    }

    [Fact]
    public async Task StartFullScanAsync_ClearsState_AndDelegatesToScanService()
    {
        using var handler = CreateHandler();
        // Seed all three state sets.
        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "1.1.1.1:5555", "s", "n", "", null)));
        handler.Dismiss(handler.Discovered[0]);
        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "2.2.2.2:5555", "s", "n", "", null)));
        Assert.Single(handler.Discovered);

        IReadOnlyList<ParsedSubnet>? capturedSubnets = null;
        _scan.StartScanHook = subnets => { capturedSubnets = subnets; return Task.CompletedTask; };

        var input = new List<ParsedSubnet> { new("192.168.1.0/24", "192.168.1.0/24", 254) };
        await handler.StartFullScanAsync(input);

        Assert.Empty(handler.Discovered);
        Assert.Same(input, capturedSubnets);

        // Dismissed addresses cleared — the original 1.1.1.1:5555 would no longer be skipped.
        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "1.1.1.1:5555", "s", "n", "", null)));
        Assert.Single(handler.Discovered);
    }

    [Fact]
    public void ReplaceDiscovered_ReplacesList_LeavesOtherStateIntact()
    {
        using var handler = CreateHandler();
        // Seed Discovered + Dismissed via live flow.
        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "10.0.0.1:5555", "s", "pre", "", null)));
        handler.Dismiss(handler.Discovered[0]);

        var replacement = new[]
        {
            new DiscoveredDevice("fresh-1", "10.0.0.2", 5555, "aa:bb:cc:dd:ee:01"),
            new DiscoveredDevice("fresh-2", "10.0.0.3", 5555, "aa:bb:cc:dd:ee:02"),
        };

        var stateChanges = 0;
        handler.OnStateChanged += () => stateChanges++;

        handler.ReplaceDiscovered(replacement);

        Assert.Equal(2, handler.Discovered.Count);
        Assert.Equal("fresh-1", handler.Discovered[0].ServiceName);
        Assert.Equal(1, stateChanges);

        // Dismissed set preserved — re-emitting the previously dismissed address is still skipped.
        _scan.Emit(new ScanHitEvent(new ScanHit(
            DiscoverySource.Mdns, "10.0.0.1:5555", "s", "after", "", null)));
        Assert.Equal(2, handler.Discovered.Count);
    }
}
