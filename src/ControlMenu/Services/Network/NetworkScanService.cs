using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ControlMenu.Services;

namespace ControlMenu.Services.Network;

public sealed class NetworkScanService : INetworkScanService
{
    private readonly object _lock = new();
    private readonly List<Subscriber> _subscribers = new();
    private readonly List<ScanHit> _hits = new();
    private ScanStartedEvent? _lastStarted;
    private ScanProgressEvent? _lastProgress;

    private readonly WsScrcpyService _wsscrcpy;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _scanCts;

    public NetworkScanService(WsScrcpyService wsscrcpy)
    {
        _wsscrcpy = wsscrcpy;
    }

    public ScanPhase Phase { get; private set; } = ScanPhase.Idle;

    public IReadOnlyList<ScanHit> Hits
    {
        get { lock (_lock) return _hits.ToList(); }
    }

    public IDisposable Subscribe(Action<ScanEvent> onEvent)
    {
        var sub = new Subscriber(onEvent, this);

        // Snapshot everything under the lock to avoid a race with Dispatch
        // mutating _hits / _lastStarted / _lastProgress while we iterate.
        // Callbacks fire OUTSIDE the lock so a slow subscriber can't block
        // the producer. ScanErrorEvent is transient by design — it leaves
        // Phase=Idle so later subscribers see a clean "ready" state; errors
        // surface via at-the-moment notification, not replay.
        ScanPhase phase;
        ScanStartedEvent? started;
        ScanProgressEvent? progress;
        List<ScanHit> hitsSnapshot;
        lock (_lock)
        {
            _subscribers.Add(sub);
            phase = Phase;
            started = _lastStarted;
            progress = _lastProgress;
            hitsSnapshot = _hits.ToList();
        }

        if (phase != ScanPhase.Idle)
        {
            if (started is not null) onEvent(started);
            if (progress is not null) onEvent(progress);
            foreach (var hit in hitsSnapshot) onEvent(new ScanHitEvent(hit));
        }
        return sub;
    }

    // Test hook. Only visible to ControlMenu.Tests via [InternalsVisibleTo].
    internal void TestOnlyInject(ScanEvent evt) => Dispatch(evt);

    private void Dispatch(ScanEvent evt)
    {
        Subscriber[] snapshot;
        lock (_lock)
        {
            switch (evt)
            {
                case ScanStartedEvent started:
                    Phase = ScanPhase.Scanning;
                    _lastStarted = started;
                    _lastProgress = null;
                    _hits.Clear();
                    break;
                case ScanProgressEvent p:
                    _lastProgress = p;
                    break;
                case ScanHitEvent h:
                    _hits.Add(h.Hit);
                    break;
                case ScanDrainingEvent:
                    Phase = ScanPhase.Draining;
                    break;
                case ScanCompleteEvent:
                    Phase = ScanPhase.Complete;
                    break;
                case ScanCancelledEvent:
                    Phase = ScanPhase.Cancelled;
                    break;
                case ScanErrorEvent:
                    Phase = ScanPhase.Idle;
                    break;
            }
            snapshot = _subscribers.ToArray();
        }
        foreach (var s in snapshot) s.Invoke(evt);
    }

    public async Task StartScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (Phase is ScanPhase.Scanning or ScanPhase.Draining)
                throw new InvalidOperationException("scan already in progress");
        }

        var baseUrl = _wsscrcpy.BaseUrl;
        Uri wsUrl;
        try
        {
            // UriBuilder handles trailing slashes, case-insensitive schemes, and
            // any query-string quirks that a raw string Replace would miss.
            var builder = new UriBuilder(baseUrl);
            builder.Scheme = builder.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
            builder.Path = builder.Path.TrimEnd('/') + "/ws-scan";
            wsUrl = builder.Uri;
        }
        catch (Exception ex)
        {
            Dispatch(new ScanErrorEvent($"invalid ws-scrcpy-web BaseUrl '{baseUrl}': {ex.Message}"));
            return;
        }

        var ws = new ClientWebSocket();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            await ws.ConnectAsync(wsUrl, cts.Token);
        }
        catch (Exception ex)
        {
            ws.Dispose();
            cts.Dispose();
            Dispatch(new ScanErrorEvent($"ws-scrcpy-web unreachable at {baseUrl} — {ex.Message}"));
            return;
        }

        // Stash before sending scan.start — cancellation path in Task 9 will need them.
        lock (_lock)
        {
            _ws?.Dispose();
            _scanCts?.Dispose();
            _ws = ws;
            _scanCts = cts;
        }

        var startJson = JsonSerializer.Serialize(new
        {
            type = "scan.start",
            subnets = subnets.Select(s => s.Raw).ToArray(),
        });
        await ws.SendAsync(Encoding.UTF8.GetBytes(startJson), WebSocketMessageType.Text, true, cts.Token);

        // Fire-and-forget receive loop. The loop dispatches events to subscribers
        // and cleans up when the server closes the socket or an exception fires.
        _ = Task.Run(() => ReceiveLoopAsync(ws, cts.Token));
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                // Loop until EndOfMessage — scan.error with many invalid-subnet
                // details, or scan.hit with a long label, can exceed 8 KiB.
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    // Server closed cleanly without dispatching scan.complete /
                    // scan.cancelled (SIGTERM'd mid-scan, container restart).
                    // Force Cancelled so subscribers unstick from Scanning.
                    if (Phase is ScanPhase.Scanning or ScanPhase.Draining)
                    {
                        int count;
                        lock (_lock) count = _hits.Count;
                        Dispatch(new ScanCancelledEvent(count));
                    }
                    break;
                }
                if (ms.Length == 0) continue;

                var json = Encoding.UTF8.GetString(ms.ToArray());
                var evt = ParseServerMessage(json);
                if (evt is not null) Dispatch(evt);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected on cancel — Task 9 will send scan.cancel and close; no-op here.
        }
        catch (Exception ex)
        {
            // Unexpected drop (upstream crash, container restart). Force Cancelled
            // so listeners can react; preserve buffered hits.
            Dispatch(new ScanCancelledEvent(Hits.Count));
            Dispatch(new ScanErrorEvent($"upstream disconnect: {ex.Message}"));
        }
        finally
        {
            try { if (ws.State == WebSocketState.Open) await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); }
            catch { /* best-effort close */ }
            ws.Dispose();
        }
    }

    private static ScanEvent? ParseServerMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var typeProp)) return null;
        var type = typeProp.GetString();
        return type switch
        {
            "scan.started" => new ScanStartedEvent(
                root.GetProperty("totalHosts").GetInt32(),
                root.GetProperty("totalSubnets").GetInt32(),
                root.GetProperty("startedAt").GetInt64()),
            "scan.progress" => new ScanProgressEvent(
                root.GetProperty("checked").GetInt32(),
                root.GetProperty("total").GetInt32(),
                root.GetProperty("foundSoFar").GetInt32()),
            "scan.hit" => new ScanHitEvent(new ScanHit(
                string.Equals(root.GetProperty("source").GetString(), "mdns", StringComparison.OrdinalIgnoreCase)
                    ? DiscoverySource.Mdns : DiscoverySource.Tcp,
                root.GetProperty("address").GetString() ?? "",
                root.GetProperty("serial").GetString() ?? "",
                root.GetProperty("name").GetString() ?? "",
                root.TryGetProperty("label", out var labelProp) ? labelProp.GetString() ?? "" : "",
                null)),
            "scan.draining" => new ScanDrainingEvent(),
            "scan.complete" => new ScanCompleteEvent(root.GetProperty("found").GetInt32()),
            "scan.cancelled" => new ScanCancelledEvent(root.GetProperty("found").GetInt32()),
            "scan.error" => new ScanErrorEvent(root.GetProperty("reason").GetString() ?? "unknown"),
            _ => null,
        };
    }

    public Task CancelAsync(CancellationToken ct = default) =>
        throw new NotImplementedException("Task 9");

    private sealed class Subscriber : IDisposable
    {
        private Action<ScanEvent>? _handler;
        private readonly NetworkScanService _parent;

        public Subscriber(Action<ScanEvent> h, NetworkScanService p)
        {
            _handler = h;
            _parent = p;
        }

        public void Invoke(ScanEvent e) => _handler?.Invoke(e);

        public void Dispose()
        {
            lock (_parent._lock) _parent._subscribers.Remove(this);
            _handler = null;
        }
    }
}
