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

    public void Dismiss(DiscoveredDevice d)
    {
        _discovered.Remove(d);
        _dismissedAddresses.Add(ScanMergeHelper.AddressKey(d.Ip, d.Port));
        RaiseStateChanged();
    }

    public void ReplaceDiscovered(IEnumerable<DiscoveredDevice> devices) =>
        throw new NotImplementedException("Task 7");

    public void Dispose() => _subscription.Dispose();

    private void OnScanEvent(ScanEvent evt)
    {
        switch (evt)
        {
            case ScanHitEvent h:
                AppendHitIfNotDismissed(h.Hit);
                break;
        }
        _phase = _scan.Phase;
        RaiseStateChanged();
    }

    private void AppendHitIfNotDismissed(ScanHit hit)
    {
        var parts = hit.Address.Split(':');
        var ip = parts[0];
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 5555;
        if (_dismissedAddresses.Contains(ScanMergeHelper.AddressKey(ip, port)))
            return;
        _discovered.Add(new DiscoveredDevice(hit.Name, ip, port, hit.Mac));
    }

    private void RaiseStateChanged() => OnStateChanged?.Invoke();
}
