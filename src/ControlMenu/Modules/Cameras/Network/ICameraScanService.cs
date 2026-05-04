using ControlMenu.Services.Network;

namespace ControlMenu.Modules.Cameras.Network;

public interface ICameraScanService
{
    ScanPhase Phase { get; }
    IReadOnlyList<CameraScanHit> Hits { get; }
    IDisposable Subscribe(Action<CameraScanEvent> onEvent);
    Task StartScanAsync(IReadOnlyList<ParsedSubnet> subnets, CancellationToken ct = default);
    Task CancelAsync(CancellationToken ct = default);
}
