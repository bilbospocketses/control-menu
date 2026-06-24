using ControlMenu.Services.Update;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlMenu.Tests.Services.Update;

// Regression coverage for the locator-init bug: VelopackUpdateService's ctor constructs a Velopack
// UpdateManager, which reads the global static VelopackLocator.Current. That static is only
// populated by VelopackApp.Build().Run() — a per-process call that must happen before any Velopack
// API is touched. These tests assert the service is constructible once it has run, that the dev-tree
// (not-installed) path short-circuits, and that the apply-exit-code contract holds (#50).
public class VelopackUpdateServiceTests
{
    private static readonly Lazy<bool> VelopackInit = new(() =>
    {
        Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        return true;
    });

    public VelopackUpdateServiceTests() => _ = VelopackInit.Value;

    private static VelopackUpdateService Create(UpdateApplyState? state = null) =>
        new(new StubAppLifetime(), state ?? new UpdateApplyState(), NullLogger<VelopackUpdateService>.Instance);

    [Fact]
    public void Constructor_DoesNotThrow_WhenVelopackLocatorInitialized()
    {
        Assert.NotNull(Create());
    }

    [Fact]
    public async Task CheckForUpdatesAsync_NotInstalled_ReturnsNoUpdate()
    {
        var result = await Create().CheckForUpdatesAsync();

        Assert.False(result.HasUpdate);
        Assert.Null(result.AvailableVersion);
        Assert.Null(result.CurrentVersion);
    }

    [Fact]
    public void RequestApplyUpdate_NoPending_Throws_AndDoesNotRequestApply()
    {
        var state = new UpdateApplyState();
        var service = Create(state);
        Assert.Throws<InvalidOperationException>(() => service.RequestApplyUpdate());
        Assert.False(state.ApplyRequested);
    }

    [Fact]
    public void ExitCodeApplyUpdate_StaysInSyncAt75()
    {
        // Must match ControlMenu.Launcher.Supervisor.ChildSupervisor.ExitCodeApplyUpdate.
        Assert.Equal(75, VelopackUpdateService.ExitCodeApplyUpdate);
    }

    private sealed class StubAppLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
