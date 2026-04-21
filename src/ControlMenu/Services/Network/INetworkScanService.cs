namespace ControlMenu.Services.Network;

public interface INetworkScanService
{
    ScanPhase Phase { get; }
    IReadOnlyList<ScanHit> Hits { get; }

    /// <summary>
    /// Subscribe to scan events. On subscribe, if a scan is currently running or
    /// just completed, the subscriber receives a snapshot replay (last
    /// <c>ScanStartedEvent</c>, last <c>ScanProgressEvent</c> if any, all buffered
    /// <c>ScanHitEvent</c>s in order).
    /// </summary>
    IDisposable Subscribe(Action<ScanEvent> onEvent);

    /// <summary>
    /// Start a scan against the configured ws-scrcpy-web ws-scan endpoint.
    /// Implemented in Task 8.
    /// </summary>
    Task StartScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default);

    /// <summary>
    /// Cancel the current scan. Implemented in Task 9.
    /// </summary>
    Task CancelAsync(CancellationToken ct = default);
}
