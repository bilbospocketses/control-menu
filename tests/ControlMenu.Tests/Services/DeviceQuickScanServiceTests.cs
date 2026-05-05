using ControlMenu.Data.Entities;
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Services;
using ControlMenu.Services.Network;
using Moq;

namespace ControlMenu.Tests.Services;

public class DeviceQuickScanServiceTests
{
    private readonly Mock<IAdbService> _adb = new();
    private readonly Mock<INetworkDiscoveryService> _net = new();
    private readonly Mock<IDeviceService> _devices = new();
    private readonly TestHandler _handler = new();

    private DeviceQuickScanService Create() =>
        new(_adb.Object, _net.Object, _handler, _devices.Object);

    [Fact]
    public async Task NewDevice_GetsAppendedToDiscovered_AndCountedAsNew()
    {
        _adb.Setup(a => a.ScanMdnsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MdnsAdbDevice>
            {
                new("adb-X._adb-tls-connect._tcp", "192.168.1.50", 5555),
            });
        _net.Setup(n => n.GetArpTableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ArpEntry>)new List<ArpEntry>
            {
                new("192.168.1.50", "AA:BB:CC:DD:EE:FF", "dynamic"),
            });

        var svc = Create();
        var result = await svc.RunMdnsAndMergeAsync(new List<Device>());

        Assert.Equal(1, result.NewOnNetwork);
        Assert.Equal(0, result.RegisteredFound);
        Assert.Equal(0, result.PortsUpdated);
        Assert.Single(_handler.Discovered);
        Assert.Equal("192.168.1.50", _handler.Discovered[0].Ip);
        Assert.Equal(5555, _handler.Discovered[0].Port);
        Assert.Equal("AA:BB:CC:DD:EE:FF", _handler.Discovered[0].Mac);
        Assert.Equal("mdns", _handler.Discovered[0].Source);
    }

    [Fact]
    public async Task RegisteredDevice_DoesNotAppearInDiscovered_AndUpdatesLastSeen()
    {
        var deviceId = Guid.NewGuid();
        var registered = new Device
        {
            Id = deviceId,
            Name = "phone",
            MacAddress = "AA:BB:CC:DD:EE:01",
            ModuleId = "android-devices",
            AdbPort = 5555,
        };
        _adb.Setup(a => a.ScanMdnsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MdnsAdbDevice>
            {
                new("svc", "10.0.0.5", 5555),
            });
        _net.Setup(n => n.GetArpTableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ArpEntry>)new List<ArpEntry>
            {
                new("10.0.0.5", "AA:BB:CC:DD:EE:01", "dynamic"),
            });

        var svc = Create();
        var result = await svc.RunMdnsAndMergeAsync(new List<Device> { registered });

        Assert.Equal(1, result.RegisteredFound);
        Assert.Equal(0, result.NewOnNetwork);
        Assert.Empty(_handler.Discovered);
        _devices.Verify(d => d.UpdateLastSeenAsync(deviceId, "10.0.0.5"), Times.Once);
    }

    [Fact]
    public async Task PortChange_OnRegisteredDevice_BumpsPortUpdatedCount()
    {
        var registered = new Device
        {
            Id = Guid.NewGuid(),
            Name = "phone",
            MacAddress = "AA:BB:CC:DD:EE:02",
            ModuleId = "android-devices",
            AdbPort = 5555,
        };
        _adb.Setup(a => a.ScanMdnsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MdnsAdbDevice>
            {
                new("svc", "10.0.0.6", 5556),
            });
        _net.Setup(n => n.GetArpTableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ArpEntry>)new List<ArpEntry>
            {
                new("10.0.0.6", "AA:BB:CC:DD:EE:02", "dynamic"),
            });

        var svc = Create();
        var result = await svc.RunMdnsAndMergeAsync(new List<Device> { registered });

        Assert.Equal(1, result.PortsUpdated);
        Assert.Equal(5556, registered.AdbPort);
        _devices.Verify(d => d.UpdateDeviceAsync(registered), Times.Once);
    }

    [Fact]
    public async Task DismissedAddress_IsSkipped()
    {
        _handler.DismissedAddressesSet.Add("192.168.1.99:5555");
        _adb.Setup(a => a.ScanMdnsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MdnsAdbDevice>
            {
                new("svc", "192.168.1.99", 5555),
            });
        _net.Setup(n => n.GetArpTableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ArpEntry>)new List<ArpEntry>());

        var svc = Create();
        var result = await svc.RunMdnsAndMergeAsync(new List<Device>());

        Assert.Equal(0, result.NewOnNetwork);
        Assert.Empty(_handler.Discovered);
    }

    [Fact]
    public async Task MissingFromArp_TriggersPing_AndRereadsArp()
    {
        var arpCalls = 0;
        _adb.Setup(a => a.ScanMdnsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MdnsAdbDevice>
            {
                new("svc", "10.0.0.10", 5555),
            });
        _net.Setup(n => n.GetArpTableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                arpCalls++;
                if (arpCalls == 1)
                    return new List<ArpEntry>(); // empty initially
                return new List<ArpEntry> { new("10.0.0.10", "AA:BB:CC:DD:EE:99", "dynamic") };
            });
        _net.Setup(n => n.PingAsync("10.0.0.10", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var svc = Create();
        var result = await svc.RunMdnsAndMergeAsync(new List<Device>());

        _net.Verify(n => n.PingAsync("10.0.0.10", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(2, arpCalls);
        Assert.Equal(1, result.NewOnNetwork);
        Assert.Equal("AA:BB:CC:DD:EE:99", _handler.Discovered[0].Mac);
    }

    [Fact]
    public async Task RegisteredDeviceMissingFromMdns_FallsBackToArpRefresh()
    {
        var deviceId = Guid.NewGuid();
        var registered = new Device
        {
            Id = deviceId,
            Name = "phone",
            MacAddress = "AA:BB:CC:DD:EE:03",
            ModuleId = "android-devices",
            AdbPort = 5555,
        };
        _adb.Setup(a => a.ScanMdnsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MdnsAdbDevice>());
        _net.Setup(n => n.GetArpTableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ArpEntry>)new List<ArpEntry>());
        _net.Setup(n => n.ResolveIpFromMacAsync("AA:BB:CC:DD:EE:03", It.IsAny<CancellationToken>()))
            .ReturnsAsync("10.0.0.77");

        var svc = Create();
        var result = await svc.RunMdnsAndMergeAsync(new List<Device> { registered });

        Assert.Equal(1, result.RegisteredFound);
        _devices.Verify(d => d.UpdateLastSeenAsync(deviceId, "10.0.0.77"), Times.Once);
    }

    [Fact]
    public async Task ExistingDiscoveredEntries_ArePreserved_ThroughReplaceDiscovered()
    {
        _handler.SeedDiscovered(new DiscoveredDevice("prior", "10.0.0.1", 5555, null, "tcp"));
        _adb.Setup(a => a.ScanMdnsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MdnsAdbDevice>
            {
                new("new-svc", "10.0.0.2", 5555),
            });
        _net.Setup(n => n.GetArpTableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ArpEntry>)new List<ArpEntry>());

        var svc = Create();
        await svc.RunMdnsAndMergeAsync(new List<Device>());

        Assert.Equal(2, _handler.Discovered.Count);
        Assert.Contains(_handler.Discovered, d => d.Ip == "10.0.0.1");
        Assert.Contains(_handler.Discovered, d => d.Ip == "10.0.0.2");
    }

    /// <summary>
    /// Minimal in-memory IScanLifecycleHandler stub. The handler interface is
    /// big — we only need Discovered, DismissedAddresses, and ReplaceDiscovered.
    /// </summary>
    private sealed class TestHandler : IScanLifecycleHandler
    {
        private List<DiscoveredDevice> _discovered = new();
        public HashSet<string> DismissedAddressesSet { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<DiscoveredDevice> Discovered => _discovered;
        public IReadOnlySet<string> DismissedAddresses => DismissedAddressesSet;
        public ScanPhase Phase => ScanPhase.Idle;
        public ScanProgressEvent? LastProgress => null;
        public event Action? OnStateChanged;

        public void SeedDiscovered(params DiscoveredDevice[] items) => _discovered.AddRange(items);
        public string? ConsumeLastError() => null;
        public Task StartFullScanAsync(IReadOnlyList<ParsedSubnet> subnets) => Task.CompletedTask;
        public Task CancelScanAsync() => Task.CompletedTask;
        public void Dismiss(DiscoveredDevice d) { }
        public void ReplaceDiscovered(IEnumerable<DiscoveredDevice> devices)
        {
            _discovered = devices.ToList();
            OnStateChanged?.Invoke();
        }
        public void Dispose() { }
    }
}
