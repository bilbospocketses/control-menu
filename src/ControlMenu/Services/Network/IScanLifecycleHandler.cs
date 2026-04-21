namespace ControlMenu.Services.Network;

/// <summary>
/// Per-circuit handler that owns the state behind the Device Management page's
/// Discovered panel. Subscribes to <see cref="INetworkScanService"/>; dispatches
/// scan events into internal state; exposes read-only snapshots + an
/// <see cref="OnStateChanged"/> notification the page uses to trigger
/// <c>StateHasChanged</c>.
/// </summary>
public interface IScanLifecycleHandler : IDisposable
{
    IReadOnlyList<DiscoveredDevice> Discovered { get; }
    IReadOnlyDictionary<string, string> StashedNamesByMac { get; }
    ScanPhase Phase { get; }
    ScanProgressEvent? LastProgress { get; }

    /// <summary>
    /// Raised after every state mutation. Handler is Blazor-agnostic — the
    /// page is responsible for marshaling onto the UI thread via
    /// <c>InvokeAsync(StateHasChanged)</c>.
    /// </summary>
    event Action? OnStateChanged;

    /// <summary>
    /// Returns the last scan-error reason (if any) and clears it. One-shot so
    /// repeated <see cref="OnStateChanged"/> firings don't re-display the same toast.
    /// </summary>
    string? ConsumeLastError();

    /// <summary>
    /// Clears Discovered, DismissedAddresses, and StashedNamesByMac, then starts
    /// a new scan. Events flow back through the internal subscription.
    /// </summary>
    Task StartFullScanAsync(IReadOnlyList<ParsedSubnet> subnets);

    /// <summary>Cancel an in-flight scan. Phase changes arrive via the event stream.</summary>
    Task CancelScanAsync();

    /// <summary>
    /// Remove <paramref name="d"/> from Discovered and record its address as dismissed.
    /// Subsequent <see cref="ScanHitEvent"/>s and adb-merge rows for the same address
    /// are skipped for the remainder of the scan session.
    /// </summary>
    void Dismiss(DiscoveredDevice d);

    /// <summary>
    /// Replace the Discovered list wholesale. Used by Quick Refresh, which builds
    /// its own mDNS-derived list. Does NOT touch DismissedAddresses or
    /// StashedNamesByMac (Quick Refresh is a separate flow from Full Scan).
    /// </summary>
    void ReplaceDiscovered(IEnumerable<DiscoveredDevice> devices);
}
