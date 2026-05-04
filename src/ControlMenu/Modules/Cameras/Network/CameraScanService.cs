using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using ControlMenu.Modules.Cameras.Services;
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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CameraScanService> _logger;
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
        IServiceScopeFactory scopeFactory,
        ILogger<CameraScanService> logger)
    {
        _onvif = onvif;
        _rtsp = rtsp;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public IDisposable Subscribe(Action<CameraScanEvent> onEvent)
    {
        lock (_subscriberLock) _subscribers.Add(onEvent);
        return new Subscription(() => { lock (_subscriberLock) _subscribers.Remove(onEvent); });
    }

    public async Task StartScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default)
    {
        if (Phase == ScanPhase.Scanning) return;
        Phase = ScanPhase.Scanning;
        lock (_hitsLock) _hits.Clear();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sw = Stopwatch.StartNew();

        Emit(new CameraScanStartedEvent(subnets.Select(s => s.Normalized).ToList()));

        using var scope = _scopeFactory.CreateScope();
        var cameraService = scope.ServiceProvider.GetRequiredService<ICameraService>();
        var existingByIp = (await cameraService.GetAllAsync())
            .ToDictionary(c => c.IpAddress, c => c.Id);
        var seenIps = new ConcurrentDictionary<string, byte>();

        var onvifTask = RunOnvifBranchAsync(subnets, existingByIp, seenIps, cameraService, _cts.Token);
        var tcpTask = RunTcpSweepAsync(subnets, existingByIp, seenIps, cameraService, _cts.Token);

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
        _logger.LogInformation("Camera scan: {Subnets} subnet(s), {OnvifHits} ONVIF, {RtspHits} RTSP-only, {Duration}",
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
                await EmitHitOrBumpAsync(
                    new CameraScanHit(r.IpAddress, RtspPort, true, r.Manufacturer, r.Model, r.OnvifServiceUrl),
                    existing,
                    cameraService);
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
        var allIps = subnets.SelectMany(EnumerateAddresses).Distinct().ToList();
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
                await EmitHitOrBumpAsync(
                    new CameraScanHit(ipStr, RtspPort, false, null, null, null),
                    existing,
                    cameraService);
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
        lock (_hitsLock) _hits.Add(hit);
        Emit(new CameraScanHitEvent(hit));
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
        IPAddress.TryParse(ip, out var addr) && subnets.Any(s => SubnetContains(s, addr));

    private static bool SubnetContains(ParsedSubnet subnet, IPAddress addr) =>
        EnumerateAddresses(subnet).Any(a => a.Equals(addr));

    private static IEnumerable<IPAddress> EnumerateAddresses(ParsedSubnet subnet)
    {
        var n = subnet.Normalized;
        if (n.Contains('/'))
        {
            var parts = n.Split('/');
            var baseIp = IPAddress.Parse(parts[0]).GetAddressBytes();
            var prefix = int.Parse(parts[1]);
            var hostBits = 32 - prefix;
            var count = 1u << hostBits;
            var network = (uint)(baseIp[0] << 24 | baseIp[1] << 16 | baseIp[2] << 8 | baseIp[3]);
            network &= uint.MaxValue << hostBits;
            for (uint i = 1; i < count - 1; i++) // skip network + broadcast
            {
                var v = network + i;
                yield return new IPAddress(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });
            }
        }
        else if (n.Contains('-'))
        {
            var parts = n.Split('-');
            var startBytes = IPAddress.Parse(parts[0]).GetAddressBytes();
            var endBytes = IPAddress.Parse(parts[1]).GetAddressBytes();
            var start = (uint)(startBytes[0] << 24 | startBytes[1] << 16 | startBytes[2] << 8 | startBytes[3]);
            var end = (uint)(endBytes[0] << 24 | endBytes[1] << 16 | endBytes[2] << 8 | endBytes[3]);
            for (var i = start; i <= end; i++)
                yield return new IPAddress(new[] { (byte)(i >> 24), (byte)(i >> 16), (byte)(i >> 8), (byte)i });
        }
        else
        {
            yield return IPAddress.Parse(n);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _dispose;
        public Subscription(Action dispose) => _dispose = dispose;
        public void Dispose() => _dispose();
    }
}
