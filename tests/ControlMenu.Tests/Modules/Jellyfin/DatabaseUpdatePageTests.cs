using Bunit;
using ControlMenu.Common.Paths;
using ControlMenu.Modules.Jellyfin.Pages;
using ControlMenu.Modules.Jellyfin.Services;
using ControlMenu.Services;
using ControlMenu.Tests.TestHelpers;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ControlMenu.Tests.Modules.Jellyfin;

public class DatabaseUpdatePageTests : BunitContext
{
    [Fact]
    public void Steps_overview_names_the_configured_retention_not_a_hard_coded_five()
    {
        // The page said "older than 5 days" while retention has been a Settings field since the
        // Logging, Backup & Retention section shipped -- the copy and the behaviour disagreed for
        // anyone who changed it. The overview reads the setting the cleanup itself reads.
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var config = new Mock<IConfigurationService>();
            config.Setup(c => c.GetSettingAsync("jellyfin-backup-retention-days", It.IsAny<string?>()))
                  .ReturnsAsync("9");
            Services.AddSingleton(config.Object);
            Services.AddSingleton(Mock.Of<IJellyfinService>());
            Services.AddSingleton<IDataPathResolver>(new TestPathResolver(temp));

            var cut = Render<DatabaseUpdate>();

            Assert.Contains("older than 9 days", cut.Markup);
            Assert.DoesNotContain("5 days", cut.Markup);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task A_failed_backup_skips_the_sql_update_and_still_restarts_the_container()
    {
        // The SQL step used to run whether or not the backup had been written, so a run whose
        // backup failed rewrote the Jellyfin database with nothing to restore from. The container
        // has already been stopped by then, so it must still be started again.
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var jellyfin = new Mock<IJellyfinService>();
            jellyfin.Setup(j => j.GetContainerIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync("a4cc8b418cca");
            jellyfin.Setup(j => j.StopContainerAsync("a4cc8b418cca", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            jellyfin.Setup(j => j.BackupDatabaseAsync(It.IsAny<OperationLogger?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string?)null);
            jellyfin.Setup(j => j.StartContainerAsync("a4cc8b418cca", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            jellyfin.Setup(j => j.WaitForContainerReadyAsync("a4cc8b418cca", It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);

            Services.AddSingleton(Mock.Of<IConfigurationService>());
            Services.AddSingleton(jellyfin.Object);
            Services.AddSingleton<IDataPathResolver>(new TestPathResolver(temp));

            var cut = Render<DatabaseUpdate>();
            await cut.Find("button.btn-primary").ClickAsync(new());

            cut.WaitForAssertion(() => Assert.Contains("Some steps failed", cut.Markup));
            jellyfin.Verify(j => j.UpdateDateCreatedAsync(It.IsAny<OperationLogger?>(), It.IsAny<CancellationToken>()), Times.Never);
            jellyfin.Verify(j => j.StartContainerAsync("a4cc8b418cca", It.IsAny<CancellationToken>()), Times.Once);
            Assert.Contains("Skipped: no backup was taken", cut.Markup);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
