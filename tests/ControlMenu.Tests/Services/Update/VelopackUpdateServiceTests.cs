using ControlMenu.Services.Update;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlMenu.Tests.Services.Update;

// Regression coverage for the locator-init bug: VelopackUpdateService's ctor
// constructs a Velopack UpdateManager, which reads the global static
// VelopackLocator.Current. That static is only populated by
// VelopackApp.Build().Run() — a per-process call that must happen before any
// Velopack API is touched. The Phase 1 hot-fix added that call to
// ControlMenu/Program.cs; these tests assert the service is constructible
// once it has run and that the dev-tree (not-installed) path short-circuits.
public class VelopackUpdateServiceTests
{
    private static readonly Lazy<bool> VelopackInit = new(() =>
    {
        Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        return true;
    });

    public VelopackUpdateServiceTests()
    {
        _ = VelopackInit.Value;
    }

    [Fact]
    public void Constructor_DoesNotThrow_WhenVelopackLocatorInitialized()
    {
        var service = new VelopackUpdateService(
            new StubAppLifetime(),
            NullLogger<VelopackUpdateService>.Instance);

        Assert.NotNull(service);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_NotInstalled_ReturnsNoUpdate()
    {
        var service = new VelopackUpdateService(
            new StubAppLifetime(),
            NullLogger<VelopackUpdateService>.Instance);

        var result = await service.CheckForUpdatesAsync();

        Assert.False(result.HasUpdate);
        Assert.Null(result.AvailableVersion);
        Assert.Null(result.CurrentVersion);
    }

    private sealed class StubAppLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
