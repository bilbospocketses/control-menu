namespace ControlMenu.Services;

public interface IScrcpyProbeService
{
    Task<ScrcpyProbeResult?> ProbeAsync(string udid, CancellationToken ct = default);
}
