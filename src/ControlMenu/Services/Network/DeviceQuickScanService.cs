using ControlMenu.Data.Entities;
using ControlMenu.Modules.AndroidDevices.Services;

namespace ControlMenu.Services.Network;

/// <summary>
/// Shared mDNS + ARP merge pipeline. Extracted from
/// <c>Settings/DeviceManagement.QuickRefresh</c> so the Home page's Scan Android
/// button can reuse the same merge semantics. Both call sites end up with hits
/// in <see cref="IScanLifecycleHandler.Discovered"/>.
/// </summary>
public sealed class DeviceQuickScanService : IDeviceQuickScanService
{
    private readonly IAdbService _adb;
    private readonly INetworkDiscoveryService _net;
    private readonly IScanLifecycleHandler _handler;
    private readonly IDeviceService _devices;

    public DeviceQuickScanService(
        IAdbService adb,
        INetworkDiscoveryService net,
        IScanLifecycleHandler handler,
        IDeviceService devices)
    {
        _adb = adb;
        _net = net;
        _handler = handler;
        _devices = devices;
    }

    public async Task<DeviceQuickScanResult> RunMdnsAndMergeAsync(
        IReadOnlyList<Device> registered,
        CancellationToken ct = default)
    {
        // 1. Query mDNS — each hit gives us IP + advertised ADB port + service name.
        var mdnsResults = await _adb.ScanMdnsAsync(ct);

        // 2. Build IP->MAC map from ARP. If any mDNS IPs aren't in ARP yet, ping them
        //    in parallel to force the OS to resolve them, then re-read ARP.
        var arpMap = await BuildArpMapAsync(ct);
        var missing = mdnsResults.Where(m => !arpMap.ContainsKey(m.IpAddress)).Select(m => m.IpAddress).ToList();
        if (missing.Count > 0)
        {
            await Task.WhenAll(missing.Select(ip => _net.PingAsync(ip, ct)));
            arpMap = await BuildArpMapAsync(ct);
        }

        // 3. Walk the mDNS hits. Registered devices: update IP/port in-place (no
        //    Discovered entry). Unregistered devices: merge into the panel
        //    (preserving non-mDNS entries from a prior Full Scan, respecting dismissed).
        var devicesByMac = registered.ToDictionary(d => d.MacAddress, d => d, StringComparer.OrdinalIgnoreCase);

        var merged = new List<DiscoveredDevice>(_handler.Discovered);
        var mergedKeys = new HashSet<string>(
            merged.Select(d => ScanMergeHelper.AddressKey(d.Ip, d.Port)),
            StringComparer.OrdinalIgnoreCase);

        var coveredRegistered = new HashSet<Guid>();
        int updated = 0;
        int portUpdated = 0;
        int newOnes = 0;
        foreach (var m in mdnsResults)
        {
            var mac = arpMap.TryGetValue(m.IpAddress, out var x) ? x : null;
            if (mac is not null && devicesByMac.TryGetValue(mac, out var registeredDevice))
            {
                await _devices.UpdateLastSeenAsync(registeredDevice.Id, m.IpAddress);
                if (registeredDevice.AdbPort != m.Port)
                {
                    registeredDevice.AdbPort = m.Port;
                    await _devices.UpdateDeviceAsync(registeredDevice);
                    portUpdated++;
                }
                coveredRegistered.Add(registeredDevice.Id);
                updated++;
                continue;
            }

            var key = ScanMergeHelper.AddressKey(m.IpAddress, m.Port);

            if (_handler.DismissedAddresses.Contains(key))
                continue;

            if (mergedKeys.Contains(key))
                continue;

            merged.Add(new DiscoveredDevice(m.ServiceName, m.IpAddress, m.Port, mac, Source: "mdns"));
            mergedKeys.Add(key);
            newOnes++;
        }

        // 4. For registered devices not seen via mDNS, fall back to ARP-only IP refresh.
        foreach (var device in registered.Where(d => !coveredRegistered.Contains(d.Id)))
        {
            var ip = await _net.ResolveIpFromMacAsync(device.MacAddress, ct);
            if (ip is not null)
            {
                await _devices.UpdateLastSeenAsync(device.Id, ip);
                updated++;
            }
        }

        // 5. Drop any carried-over Discovered rows whose device is now registered. The seed list
        //    (_handler.Discovered) is never re-checked against the registered set as we merge, so
        //    without this a device registered after a prior scan resurfaces as a ghost here.
        var registeredMacs = devicesByMac.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var registeredIps = registered
            .Where(d => !string.IsNullOrEmpty(d.LastKnownIp))
            .Select(d => d.LastKnownIp!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        merged = merged
            .Where(d => !ScanMergeHelper.MatchesRegistered(d, registeredMacs, arpMap, registeredIps))
            .ToList();

        _handler.ReplaceDiscovered(merged);

        return new DeviceQuickScanResult(updated, portUpdated, newOnes);
    }

    private async Task<Dictionary<string, string>> BuildArpMapAsync(CancellationToken ct)
    {
        var entries = await _net.GetArpTableAsync(ct);
        return entries
            .GroupBy(e => e.IpAddress)
            .ToDictionary(g => g.Key, g => g.First().MacAddress, StringComparer.OrdinalIgnoreCase);
    }
}
