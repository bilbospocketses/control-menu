using ControlMenu.Services.Network;

namespace ControlMenu.Modules.Cameras.Network;

public interface ICameraScanService
{
    ScanPhase Phase { get; }
    IReadOnlyList<CameraScanHit> Hits { get; }
    IDisposable Subscribe(Action<CameraScanEvent> onEvent);
    Task StartScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default);
    /// <summary>
    /// Runs ONVIF WS-Discovery only against the supplied subnets, skipping the
    /// TCP-554 RTSP sweep. Same Phase transitions, event bus, and Hits accumulation
    /// as <see cref="StartScanAsync"/>. Used by the Setup Wizard's Cameras step
    /// where the parallel to WizardDevices is mDNS-only quick discovery.
    /// </summary>
    Task StartOnvifOnlyScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default);
    Task CancelAsync(CancellationToken ct = default);
}
