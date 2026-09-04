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
}
