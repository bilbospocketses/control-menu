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

    public Task StartScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default) =>
        throw new NotImplementedException("Task 8");

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
