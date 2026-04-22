using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Services;
using System.Linq;

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
    public IReadOnlySet<string> DismissedAddresses => _dismissedAddresses;
    public ScanPhase Phase => _phase;
    public ScanProgressEvent? LastProgress => _lastProgress;

    public event Action? OnStateChanged;

    public string? ConsumeLastError()
    {
        var err = _lastError;
        _lastError = null;
        return err;
    }

    public async Task StartFullScanAsync(IReadOnlyList<ParsedSubnet> subnets)
    {
        _discovered.Clear();
        _dismissedAddresses.Clear();
        _stashedNamesByMac.Clear();
        RaiseStateChanged();
        await _scan.StartScanAsync(subnets);
    }

    public Task CancelScanAsync() => _scan.CancelAsync();

    public void Dismiss(DiscoveredDevice d)
    {
        _discovered.Remove(d);
        _dismissedAddresses.Add(ScanMergeHelper.AddressKey(d.Ip, d.Port));
        RaiseStateChanged();
    }

    public void ReplaceDiscovered(IEnumerable<DiscoveredDevice> devices)
    {
        // Eagerly materialize before clearing — callers may pass a lazy sequence
        // derived from Handler.Discovered (e.g., Handler.Discovered.Where(...)).
        // If we cleared first, the lazy enumerable would evaluate over an empty
        // _discovered and AddRange would copy nothing, silently wiping the list.
        var materialized = devices.ToList();
        _discovered.Clear();
        _discovered.AddRange(materialized);
        RaiseStateChanged();
    }

    public void Dispose() => _subscription.Dispose();

    private void OnScanEvent(ScanEvent evt)
    {
        switch (evt)
        {
            case ScanStartedEvent:
                _lastProgress = null;
                break;
            case ScanProgressEvent p:
                _lastProgress = p;
                break;
            case ScanHitEvent h:
                AppendHitIfNotDismissed(h.Hit);
                break;
            case ScanDrainingEvent:
                break;
            case ScanCompleteEvent:
                _ = FinalizeScanAsync();
                break;
            case ScanCancelledEvent:
                break;
            case ScanErrorEvent err:
                _lastError = err.Reason;
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
        // Map the DiscoverySource enum to the DiscoveredDevice.Source string
        // so every row in the Discovered panel has a meaningful source badge.
        var sourceLabel = hit.Source switch
        {
            DiscoverySource.Mdns => "mdns",
            DiscoverySource.Tcp => "tcp",
            DiscoverySource.Adb => "adb",
            _ => null,
        };
        _discovered.Add(new DiscoveredDevice(hit.Name, ip, port, hit.Mac, sourceLabel));
    }

    private async Task FinalizeScanAsync()
    {
        try
        {
            var fromAdb = await DetermineAdbMergeCandidatesAsync();

            var needMacIps = _discovered.Where(d => d.Mac is null).Select(d => d.Ip)
                .Concat(fromAdb.Select(x => x.Ip))
                .Distinct()
                .ToList();

            var arpMap = await BuildArpMapWithPingsAsync(needMacIps);

            EnrichDiscoveredMacs(arpMap);
            AppendAdbMergeRows(fromAdb, arpMap);
            await PopulateStashedNamesAsync();
        }
        catch (Exception ex)
        {
            _lastError = $"Scan finalize failed: {ex.Message}";
        }
        finally
        {
            _phase = _scan.Phase;
            RaiseStateChanged();
        }
    }

    private async Task<IReadOnlyList<(string Ip, int Port)>> DetermineAdbMergeCandidatesAsync()
    {
        var devices = await _devices.GetAllDevicesAsync();
        var registeredIpPorts = devices
            .Where(d => !string.IsNullOrEmpty(d.LastKnownIp))
            .Select(d => ScanMergeHelper.AddressKey(d.LastKnownIp!, d.AdbPort))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var excludeIpPorts = _discovered
            .Select(d => ScanMergeHelper.AddressKey(d.Ip, d.Port))
            .Concat(registeredIpPorts)
            .Concat(_dismissedAddresses);

        var adbConnected = await _adb.GetConnectedDevicesAsync();
        return ScanMergeHelper.FindUnregisteredAdbConnected(adbConnected, excludeIpPorts);
    }

    private async Task<Dictionary<string, string>> BuildArpMapWithPingsAsync(IReadOnlyList<string> ipsToCover)
    {
        var arpMap = await BuildArpMapAsync();
        if (ipsToCover.Count == 0) return arpMap;

        var missing = ipsToCover.Where(ip => !arpMap.ContainsKey(ip)).ToList();
        if (missing.Count > 0)
        {
            await Task.WhenAll(missing.Select(ip => _net.PingAsync(ip)));
            arpMap = await BuildArpMapAsync();
        }
        return arpMap;
    }

    private async Task<Dictionary<string, string>> BuildArpMapAsync()
    {
        var entries = await _net.GetArpTableAsync();
        return entries
            .GroupBy(e => e.IpAddress)
            .ToDictionary(g => g.Key, g => g.First().MacAddress, StringComparer.OrdinalIgnoreCase);
    }

    private void EnrichDiscoveredMacs(IReadOnlyDictionary<string, string> arpMap)
    {
        for (var i = 0; i < _discovered.Count; i++)
        {
            if (_discovered[i].Mac is not null) continue;
            if (arpMap.TryGetValue(_discovered[i].Ip, out var mac))
                _discovered[i] = _discovered[i] with { Mac = mac };
        }
    }

    private void AppendAdbMergeRows(
        IReadOnlyList<(string Ip, int Port)> fromAdb,
        IReadOnlyDictionary<string, string> arpMap)
    {
        foreach (var x in fromAdb)
        {
            // R2 race fix. Between DetermineAdbMergeCandidatesAsync (which
            // captured _dismissedAddresses at t=0) and this loop, several
            // seconds of ARP+ping awaits elapsed during which the user may
            // have dismissed one of these addresses.
            if (_dismissedAddresses.Contains(ScanMergeHelper.AddressKey(x.Ip, x.Port)))
                continue;
            var mac = arpMap.TryGetValue(x.Ip, out var m) ? m : null;
            _discovered.Add(new DiscoveredDevice(
                ScanMergeHelper.AddressKey(x.Ip, x.Port),
                x.Ip, x.Port, mac, Source: "adb"));
        }
    }

    private async Task PopulateStashedNamesAsync()
    {
        foreach (var d in _discovered)
        {
            if (string.IsNullOrEmpty(d.Mac)) continue;
            if (_stashedNamesByMac.ContainsKey(d.Mac)) continue;
            var stashed = await _config.GetSettingAsync($"device-name-{d.Mac}");
            if (!string.IsNullOrEmpty(stashed))
                _stashedNamesByMac[d.Mac] = stashed;
        }
    }

    private void RaiseStateChanged() => OnStateChanged?.Invoke();
}
