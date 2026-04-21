using ControlMenu.Services.Network;

namespace ControlMenu.Tests.Services;

/// <summary>
/// Minimal in-memory <see cref="INetworkScanService"/> for handler tests.
/// Subscribers are fired synchronously on <see cref="Emit"/>. Tests mutate
/// <see cref="Phase"/> and <see cref="Hits"/> directly.
/// </summary>
internal sealed class FakeNetworkScanService : INetworkScanService
{
    private readonly List<Action<ScanEvent>> _subscribers = new();

    public ScanPhase Phase { get; set; } = ScanPhase.Idle;
    public IReadOnlyList<ScanHit> Hits { get; set; } = Array.Empty<ScanHit>();

    public Func<IReadOnlyList<ParsedSubnet>, Task>? StartScanHook { get; set; }
    public Func<Task>? CancelHook { get; set; }

    public IDisposable Subscribe(Action<ScanEvent> onEvent)
    {
        _subscribers.Add(onEvent);
        return new Subscription(() => _subscribers.Remove(onEvent));
    }

    public Task StartScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default)
        => StartScanHook?.Invoke(subnets) ?? Task.CompletedTask;

    public Task CancelAsync(CancellationToken ct = default)
        => CancelHook?.Invoke() ?? Task.CompletedTask;

    /// <summary>Push an event to every current subscriber.</summary>
    public void Emit(ScanEvent evt)
    {
        foreach (var s in _subscribers.ToList())
            s(evt);
    }

    public int SubscriberCount => _subscribers.Count;

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        private Action? _onDispose = onDispose;
        public void Dispose()
        {
            _onDispose?.Invoke();
            _onDispose = null;
        }
    }
}
