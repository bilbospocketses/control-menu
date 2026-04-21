using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Services;

namespace ControlMenu.Services.Network;

public sealed class ScanLifecycleHandler : IScanLifecycleHandler
{
    private readonly INetworkScanService _scan;
    private readonly IAdbService _adb;
    private readonly INetworkDiscoveryService _net;
    private readonly IConfigurationService _config;
    private readonly IDeviceService _devices;
    private readonly IDisposable _subscription;

    private readonly List<DiscoveredDevice> _discovered = new();
    private readonly HashSet<string> _dismissedAddresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _stashedNamesByMac = new(StringComparer.OrdinalIgnoreCase);
    private ScanPhase _phase;
    private ScanProgressEvent? _lastProgress;
    private string? _lastError;

    public ScanLifecycleHandler(
        INetworkScanService scan,
        IAdbService adb,
        INetworkDiscoveryService net,
        IConfigurationService config,
        IDeviceService devices)
    {
        _scan = scan;
        _adb = adb;
        _net = net;
        _config = config;
        _devices = devices;
        _subscription = _scan.Subscribe(OnScanEvent);
        _phase = _scan.Phase;
    }

    public IReadOnlyList<DiscoveredDevice> Discovered => _discovered;
    public IReadOnlyDictionary<string, string> StashedNamesByMac => _stashedNamesByMac;
    public ScanPhase Phase => _phase;
    public ScanProgressEvent? LastProgress => _lastProgress;

    public event Action? OnStateChanged;

    public string? ConsumeLastError()
    {
        var err = _lastError;
        _lastError = null;
        return err;
    }

    public Task StartFullScanAsync(IReadOnlyList<ParsedSubnet> subnets) =>
        throw new NotImplementedException("Task 6");

    public Task CancelScanAsync() =>
        throw new NotImplementedException("Task 6");

    public void Dismiss(DiscoveredDevice d) =>
        throw new NotImplementedException("Task 4");

    public void ReplaceDiscovered(IEnumerable<DiscoveredDevice> devices) =>
        throw new NotImplementedException("Task 7");

    public void Dispose() => _subscription.Dispose();

    private void OnScanEvent(ScanEvent evt)
    {
        // Populated in Tasks 4, 5, 6, 8.
    }

    private void RaiseStateChanged() => OnStateChanged?.Invoke();
}
