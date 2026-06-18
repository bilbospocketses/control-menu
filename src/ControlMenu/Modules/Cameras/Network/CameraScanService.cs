using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using ControlMenu.Modules.Cameras.Services;
using ControlMenu.Services;
using ControlMenu.Services.Network;
using Microsoft.Extensions.Logging;

namespace ControlMenu.Modules.Cameras.Network;

public class CameraScanService : ICameraScanService
{
    private static readonly TimeSpan WsDiscoveryTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan TcpProbeTimeout = TimeSpan.FromSeconds(1);
    private const int TcpConcurrency = 64;
    private const int RtspPort = 554;

    private readonly IOnvifDiscoveryClient _onvif;
    private readonly IRtspProbeClient _rtsp;
    private readonly INetworkDiscoveryService _networkDiscovery;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CameraScanService> _logger;
    private Dictionary<string, string> _arpByIp = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Action<CameraScanEvent>> _subscribers = new();
    private readonly object _subscriberLock = new();
    private readonly List<CameraScanHit> _hits = new();
    private readonly object _hitsLock = new();
    private CancellationTokenSource? _cts;

    public ScanPhase Phase { get; private set; } = ScanPhase.Idle;
    public IReadOnlyList<CameraScanHit> Hits
    {
        get { lock (_hitsLock) return _hits.ToList(); }
    }

    public CameraScanService(
        IOnvifDiscoveryClient onvif,
        IRtspProbeClient rtsp,
        INetworkDiscoveryService networkDiscovery,
        IServiceScopeFactory scopeFactory,
        ILogger<CameraScanService> logger)
    {
        _onvif = onvif;
        _rtsp = rtsp;
        _networkDiscovery = networkDiscovery;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public IDisposable Subscribe(Action<CameraScanEvent> onEvent)
    {
        lock (_subscriberLock) _subscribers.Add(onEvent);
        return new Subscription(() => { lock (_subscriberLock) _subscribers.Remove(onEvent); });
    }

    public Task StartScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default) =>
        RunScanAsync(subnets, includeRtspSweep: true, ct);

    public Task StartOnvifOnlyScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default) =>
        RunScanAsync(subnets, includeRtspSweep: false, ct);

    private async Task RunScanAsync(
        IReadOnlyList<ParsedSubnet> subnets,
        bool includeRtspSweep,
        CancellationToken ct)
    {
        if (Phase == ScanPhase.Scanning) return;
        Phase = ScanPhase.Scanning;
        lock (_hitsLock) _hits.Clear();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sw = Stopwatch.StartNew();

        Emit(new CameraScanStartedEvent(subnets.Select(s => s.Normalized).ToList()));

        var arpEntries = await _networkDiscovery.GetArpTableAsync(_cts.Token);
        _arpByIp = arpEntries
            .GroupBy(e => e.IpAddress)
            .ToDictionary(g => g.Key, g => g.First().MacAddress, StringComparer.OrdinalIgnoreCase);

        using var scope = _scopeFactory.CreateScope();
        var cameraService = scope.ServiceProvider.GetRequiredService<ICameraService>();
        var existingByIp = (await cameraService.GetAllAsync())
            .ToDictionary(c => c.IpAddress, c => c.Id);
        var seenIps = new ConcurrentDictionary<string, byte>();

        var onvifTask = RunOnvifBranchAsync(subnets, existingByIp, seenIps, cameraService, _cts.Token);
        var tcpTask = includeRtspSweep
            ? RunTcpSweepAsync(subnets, existingByIp, seenIps, cameraService, _cts.Token)
            : Task.CompletedTask;

        await Task.WhenAll(onvifTask, tcpTask);

        sw.Stop();
        if (_cts.Token.IsCancellationRequested)
        {
            Phase = ScanPhase.Idle;
            Emit(new CameraScanCancelledEvent());
            return;
        }

        Phase = ScanPhase.Complete;
        var onvifCount = Hits.Count(h => h.IsOnvif);
        var rtspCount = Hits.Count(h => !h.IsOnvif);
        _logger.LogInformation(
            "Camera scan ({Mode}): {Subnets} subnet(s), {OnvifHits} ONVIF, {RtspHits} RTSP-only, {Duration}",
            includeRtspSweep ? "full" : "onvif-only",
            subnets.Count, onvifCount, rtspCount, sw.Elapsed);
        Emit(new CameraScanCompletedEvent(onvifCount, rtspCount, sw.Elapsed));
    }

    public Task CancelAsync(CancellationToken ct = default)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task RunOnvifBranchAsync(
        IReadOnlyList<ParsedSubnet> subnets,
        Dictionary<string, Guid> existing,
        ConcurrentDictionary<string, byte> seenIps,
        ICameraService cameraService,
        CancellationToken ct)
    {
        try
        {
            var responses = await _onvif.ProbeAsync(WsDiscoveryTimeout, ct);
            foreach (var r in responses)
            {
                if (!IsInAnySubnet(r.IpAddress, subnets)) continue;
                seenIps.TryAdd(r.IpAddress, 0);
                var hit = new CameraScanHit(r.IpAddress, RtspPort, true, r.Manufacturer, r.Model, r.OnvifServiceUrl)
                {
                    FriendlyName = r.FriendlyName,
                    Location = r.Location,
                    MacAddress = _arpByIp.TryGetValue(r.IpAddress, out var mac) ? mac : null,
                };
                await EmitHitOrBumpAsync(hit, existing, cameraService);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ONVIF branch error");
            Emit(new CameraScanErrorEvent("onvif", ex.Message));
        }
    }

    private async Task RunTcpSweepAsync(
        IReadOnlyList<ParsedSubnet> subnets,
        Dictionary<string, Guid> existing,
        ConcurrentDictionary<string, byte> seenIps,
        ICameraService cameraService,
        CancellationToken ct)
    {
        var allIps = subnets.SelectMany(SubnetMath.Enumerate).Distinct().ToList();
        using var sem = new SemaphoreSlim(TcpConcurrency);
        var tasks = allIps.Select(async ip =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var ipStr = ip.ToString();
                if (seenIps.ContainsKey(ipStr)) return;
                if (!await _rtsp.ProbeTcpAsync(ipStr, RtspPort, TcpProbeTimeout, ct)) return;
                if (!seenIps.TryAdd(ipStr, 0)) return; // ONVIF beat us to it
                var tcpHit = new CameraScanHit(ipStr, RtspPort, false, null, null, null)
                {
                    MacAddress = _arpByIp.TryGetValue(ipStr, out var tcpMac) ? tcpMac : null,
                };
                await EmitHitOrBumpAsync(tcpHit, existing, cameraService);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "TCP probe error for {Ip}", ip);
            }
            finally
            {
                sem.Release();
            }
        });
        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TCP sweep branch error");
            Emit(new CameraScanErrorEvent("tcp", ex.Message));
        }
    }

    private async Task EmitHitOrBumpAsync(CameraScanHit hit, Dictionary<string, Guid> existing, ICameraService cameraService)
    {
        if (existing.TryGetValue(hit.IpAddress, out var id))
        {
            await cameraService.UpdateLastSeenAsync(id);
            return;
        }

        bool shouldEmit;
        lock (_hitsLock)
        {
            var idx = _hits.FindIndex(h => h.IpAddress == hit.IpAddress);
            if (idx < 0)
            {
                // First time seeing this IP within this scan
                _hits.Add(hit);
                shouldEmit = true;
            }
            else if (hit.IsOnvif && !_hits[idx].IsOnvif)
            {
                // Upgrade: TCP-only entry replaced by richer ONVIF entry
                _hits[idx] = hit;
                shouldEmit = true;
            }
            else
            {
                // Duplicate (TCP-after-ONVIF, or TCP-after-TCP, or ONVIF-after-ONVIF) — skip
                shouldEmit = false;
            }
        }

        if (shouldEmit) Emit(new CameraScanHitEvent(hit));
    }

    private void Emit(CameraScanEvent ev)
    {
        Action<CameraScanEvent>[] subs;
        lock (_subscriberLock) subs = _subscribers.ToArray();
        foreach (var s in subs)
        {
            try { s(ev); }
            catch (Exception ex) { _logger.LogWarning(ex, "Subscriber error"); }
        }
    }

    private static bool IsInAnySubnet(string ip, IReadOnlyList<ParsedSubnet> subnets) =>
        IPAddress.TryParse(ip, out var addr) && subnets.Any(s => SubnetMath.Contains(s, addr));

    private sealed class Subscription : IDisposable
    {
        private readonly Action _dispose;
        public Subscription(Action dispose) => _dispose = dispose;
        public void Dispose() => _dispose();
    }
}
