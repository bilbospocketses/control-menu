using ControlMenu.Services;

namespace ControlMenu.Tests.Services.Fakes;

public sealed class FakeScrcpyProbeService : IScrcpyProbeService
{
    public ScrcpyProbeResult? Result { get; set; }

    public Task<ScrcpyProbeResult?> ProbeAsync(string udid, CancellationToken ct = default)
        => Task.FromResult(Result);
}
