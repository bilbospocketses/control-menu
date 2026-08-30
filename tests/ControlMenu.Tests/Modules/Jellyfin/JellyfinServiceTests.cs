using ControlMenu.Modules.Jellyfin.Services;
using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Modules.Jellyfin;

public class JellyfinServiceTests
{
    private readonly Mock<ICommandExecutor> _mockExecutor = new();
    private readonly Mock<IConfigurationService> _mockConfig = new();
    private readonly Mock<IHttpClientFactory> _mockHttpFactory = new();
    private readonly Mock<IDependencyPathResolver> _mockResolver = new();
    private readonly Mock<IJellyfinDirectoryResolver> _mockDirectoryResolver = new();

    public JellyfinServiceTests()
    {
        _mockResolver.Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string name, CancellationToken _) => name);
        _mockDirectoryResolver.Setup(r => r.GetBackupDirectoryAsync())
            .ReturnsAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
    }

    private JellyfinService CreateService() => new(_mockExecutor.Object, _mockConfig.Object, _mockHttpFactory.Object, _mockResolver.Object, _mockDirectoryResolver.Object);

    [Fact]
    public async Task GetContainerIdAsync_ParsesDockerPsOutput()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("jellyfin-container-name", null))
            .ReturnsAsync("jellyfin");
        _mockExecutor.Setup(e => e.ExecuteAsync("docker", "ps -a --filter name=^/jellyfin$ --format {{.ID}}", null, default))
            .ReturnsAsync(new CommandResult(0, "a1b2c3d4e5f6\n", "", false));

        var service = CreateService();
        var id = await service.GetContainerIdAsync();

        Assert.Equal("a1b2c3d4e5f6", id);
    }

    [Fact]
    public async Task GetContainerIdAsync_ReturnsNull_WhenNoContainer()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("jellyfin-container-name", null))
            .ReturnsAsync("jellyfin");
        _mockExecutor.Setup(e => e.ExecuteAsync("docker", "ps -a --filter name=^/jellyfin$ --format {{.ID}}", null, default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        var id = await service.GetContainerIdAsync();

        Assert.Null(id);
    }

    [Fact]
    public async Task StopContainerAsync_StopsWithGracePeriod()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("docker", "stop -t=15 abc123", null, default))
            .ReturnsAsync(new CommandResult(0, "abc123", "", false));

        var service = CreateService();
        var result = await service.StopContainerAsync("abc123");

        Assert.True(result);
    }

    [Fact]
    public async Task StartContainerAsync_StartsContainer()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("docker", "start abc123", null, default))
            .ReturnsAsync(new CommandResult(0, "abc123", "", false));

        var service = CreateService();
        var result = await service.StartContainerAsync("abc123");

        Assert.True(result);
    }

    [Fact]
    public async Task BackupDatabaseAsync_CopiesFile()
    {
        // Create a real source DB file in a temp dir so JellyfinService.BackupDatabaseAsync's
        // File.Exists(dbPath) check passes on any environment. Previous hardcoded
        // D:/DockerData/jellyfin/config/data/jellyfin.db only existed on the dev box; CI
        // (GitHub Actions windows-latest, no D: drive) returned null → assertion failed.
        var tempDir = Path.Combine(Path.GetTempPath(), "cm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourceDbPath = Path.Combine(tempDir, "jellyfin.db");
        await File.WriteAllBytesAsync(sourceDbPath, new byte[] { 0x53, 0x51, 0x4c, 0x69 }); // SQLi header bytes — fake but plausible

        try
        {
            _mockConfig.Setup(c => c.GetSettingAsync("jellyfin-db-path", null))
                .ReturnsAsync(sourceDbPath);

            _mockExecutor.Setup(e => e.ExecuteAsync(
                It.IsAny<CommandDefinition>(), default))
                .ReturnsAsync(new CommandResult(0, "", "", false));

            var service = CreateService();
            var backupPath = await service.BackupDatabaseAsync();

            Assert.NotNull(backupPath);
            Assert.Contains("jellyfin_", backupPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task UpdateDateCreatedAsync_RunsSqlUpdate()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("jellyfin-db-path", null))
            .ReturnsAsync("D:/DockerData/jellyfin/config/data/jellyfin.db");

        IReadOnlyList<string>? captured = null;
        _mockExecutor.Setup(e => e.ExecuteAsync("sqlite3",
                It.IsAny<IReadOnlyList<string>>(), null, It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, string?, CancellationToken>((_, a, _, _) => captured = a)
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        var result = await service.UpdateDateCreatedAsync();

        Assert.True(result);
        // dbPath and the SQL statement are passed as discrete, un-concatenated arguments —
        // a dbPath containing a quote/space can no longer inject extra sqlite3 arguments.
        Assert.NotNull(captured);
        Assert.Equal("D:/DockerData/jellyfin/config/data/jellyfin.db", captured![0]);
        Assert.Contains("UPDATE BaseItems SET DateCreated=PremiereDate", captured[1]);
    }

    [Fact]
    public async Task CleanupOldBackupsAsync_RemovesOldFiles()
    {
        var backupDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _mockDirectoryResolver.Setup(r => r.GetBackupDirectoryAsync())
            .ReturnsAsync(backupDir);

        var testTag = Guid.NewGuid().ToString("N");
        var oldFile = Path.Combine(backupDir, $"jellyfin_testold_{testTag}.db");
        var newFile = Path.Combine(backupDir, $"jellyfin_testnew_{testTag}.db");
        try
        {
            Directory.CreateDirectory(backupDir);
            File.WriteAllText(oldFile, "old");
            File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-10));
            File.WriteAllText(newFile, "new");

            _mockConfig.Setup(c => c.GetSettingAsync("jellyfin-backup-retention-days", null))
                .ReturnsAsync("5");

            var service = CreateService();
            await service.CleanupOldBackupsAsync();

            Assert.False(File.Exists(oldFile));
            Assert.True(File.Exists(newFile));
        }
        finally
        {
            if (File.Exists(oldFile)) File.Delete(oldFile);
            if (File.Exists(newFile)) File.Delete(newFile);
            if (Directory.Exists(backupDir)) Directory.Delete(backupDir);
        }
    }

    [Fact]
    public async Task UpdateDateCreatedAsync_ResolvesViaDependencyPathResolver_NotBareName()
    {
        // Local-Dependencies-Only regression guard (mirrors AdbService_ResolvesViaDependencyPathResolver_NotBareName):
        // sqlite3 must be invoked at the resolved local path, never as a bare "sqlite3" off PATH.
        var localExecutor = new Mock<ICommandExecutor>();
        var localConfig = new Mock<IConfigurationService>();
        var localResolver = new Mock<IDependencyPathResolver>();
        var localDirectoryResolver = new Mock<IJellyfinDirectoryResolver>();

        localConfig.Setup(c => c.GetSettingAsync("jellyfin-db-path", null))
            .ReturnsAsync("D:/DockerData/jellyfin/config/data/jellyfin.db");
        localResolver.Setup(r => r.ResolveAsync("jellyfin", "sqlite3", It.IsAny<CancellationToken>()))
            .ReturnsAsync("/cm/local/sqlite3.exe");
        localExecutor.Setup(e => e.ExecuteAsync("/cm/local/sqlite3.exe",
                It.IsAny<IReadOnlyList<string>>(),
                null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = new JellyfinService(localExecutor.Object, localConfig.Object,
            _mockHttpFactory.Object, localResolver.Object, localDirectoryResolver.Object);
        var result = await service.UpdateDateCreatedAsync();

        Assert.True(result);
        localExecutor.Verify(
            e => e.ExecuteAsync("sqlite3", It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "JellyfinService must NOT call the executor with bare 'sqlite3' — local-deps rule.");
        localExecutor.Verify(
            e => e.ExecuteAsync("/cm/local/sqlite3.exe", It.IsAny<IReadOnlyList<string>>(), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---------------------------------------------------------------------
    // WaitForContainerReadyAsync -- readiness must not depend on `docker logs --since`
    // ---------------------------------------------------------------------
    //
    // Regression: readiness was decided by `docker logs --since <timestamp>` grepping for
    // "Startup complete". On a long-lived container that call silently returns ZERO lines for any
    // recent timestamp (verified: --since 2026-06-01 -> 467k lines, --since <today> -> 0), so the
    // check could never succeed even though Jellyfin logs "Main: Startup complete" ~14s after
    // start. Every step of the db-date-update routine succeeded and the run was still reported as
    // failed. Readiness now prefers the container's own healthcheck and falls back to a --tail read
    // whose timestamps are compared against the container start time.

    private void SetupStartedAt(string containerId, string startedAtIso) =>
        _mockExecutor.Setup(e => e.ExecuteAsync("docker",
                It.Is<string>(a => a.Contains("StartedAt") && a.Contains(containerId)), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, startedAtIso + "\n", "", false));

    private void SetupHealth(string containerId, string status) =>
        _mockExecutor.Setup(e => e.ExecuteAsync("docker",
                It.Is<string>(a => a.Contains(".State.Health") && a.Contains(containerId)), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, status + "\n", "", false));

    private void SetupLogs(string containerId, string stdout) =>
        _mockExecutor.Setup(e => e.ExecuteAsync("docker",
                It.Is<string>(a => a.StartsWith("logs") && a.Contains(containerId)), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, stdout, "", false));

    [Fact]
    public async Task WaitForContainerReadyAsync_ReturnsTrue_WhenTheContainerReportsHealthy()
    {
        SetupStartedAt("abc123", "2026-08-26T19:03:34.701211748Z");
        SetupHealth("abc123", "healthy");
        SetupLogs("abc123", "");   // no marker at all -- health alone must be enough

        Assert.True(await CreateService().WaitForContainerReadyAsync("abc123", timeoutSeconds: 0));
    }

    [Fact]
    public async Task WaitForContainerReadyAsync_ReturnsTrue_WhenNoHealthcheckButLogMarkerFollowsStart()
    {
        SetupStartedAt("abc123", "2026-08-26T19:03:34.701211748Z");
        SetupHealth("abc123", "none");   // image defines no healthcheck
        SetupLogs("abc123",
            "2026-08-26T19:03:48.778080977Z [15:03:48] [INF] [9] Main: Startup complete 0:00:13.5322133\n");

        Assert.True(await CreateService().WaitForContainerReadyAsync("abc123", timeoutSeconds: 0));
    }

    [Fact]
    public async Task WaitForContainerReadyAsync_IgnoresAStartupMarkerFromBeforeThisStart()
    {
        // The whole reason the original used --since: a long-lived container's log holds the
        // "Startup complete" line from every PREVIOUS start. Matching one of those would report
        // ready instantly while Jellyfin is still booting -- worse than the bug being fixed.
        SetupStartedAt("abc123", "2026-08-26T19:03:34.701211748Z");
        SetupHealth("abc123", "starting");
        SetupLogs("abc123",
            "2026-06-30T18:21:58.000000000Z [14:21:58] [INF] [9] Main: Startup complete 0:00:12.1\n");

        Assert.False(await CreateService().WaitForContainerReadyAsync("abc123", timeoutSeconds: 0));
    }

    [Fact]
    public async Task WaitForContainerReadyAsync_ReturnsFalse_WhenNeitherSignalArrivesBeforeTheDeadline()
    {
        SetupStartedAt("abc123", "2026-08-26T19:03:34.701211748Z");
        SetupHealth("abc123", "starting");
        SetupLogs("abc123", "2026-08-26T19:03:40.000000000Z [15:03:40] [INF] Loading plugins\n");

        Assert.False(await CreateService().WaitForContainerReadyAsync("abc123", timeoutSeconds: 0));
    }

    [Fact]
    public async Task WaitForContainerReadyAsync_NeverUsesDockerLogsSince()
    {
        // --since is the broken primitive. Pin it out so it cannot creep back in.
        SetupStartedAt("abc123", "2026-08-26T19:03:34.701211748Z");
        SetupHealth("abc123", "healthy");
        SetupLogs("abc123", "");

        await CreateService().WaitForContainerReadyAsync("abc123", timeoutSeconds: 0);

        _mockExecutor.Verify(e => e.ExecuteAsync("docker",
            It.Is<string>(a => a.Contains("--since")), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "readiness must not depend on `docker logs --since` -- it returns nothing on long-lived containers");
    }

    private (JellyfinService Service, List<HttpRequestMessage> Requests) CreateServiceCapturingHttp()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new PersonRefreshCapturingHandler(requests.Add);
        _mockHttpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        return (CreateService(), requests);
    }

    [Fact]
    public async Task TriggerPersonImageDownloadAsync_PostsToTheRefreshEndpoint()
    {
        var (service, requests) = CreateServiceCapturingHttp();

        await service.TriggerPersonImageDownloadAsync(
            "abc-123", new JellyfinApiConfig("http://jf:8096", "key", "user-1"));

        var req = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        var uri = req.RequestUri!.ToString();
        Assert.Contains("/Items/abc-123/Refresh", uri);
        Assert.Contains("imageRefreshMode=FullRefresh", uri);
        Assert.Contains("replaceAllImages=false", uri);
    }

    [Fact]
    public async Task TriggerPersonImageDownloadAsync_NeverJustReadsTheItem()
    {
        // The original implementation GET-ed /Users/{userId}/Items/{personId}. That only reads the
        // item -- no metadata provider is ever contacted -- so the job reported success while
        // downloading nothing. Pin the regression out.
        var (service, requests) = CreateServiceCapturingHttp();

        await service.TriggerPersonImageDownloadAsync(
            "abc-123", new JellyfinApiConfig("http://jf:8096", "key", "user-1"));

        Assert.DoesNotContain(requests, r => r.Method == HttpMethod.Get);
        Assert.DoesNotContain(requests, r => r.RequestUri!.ToString().Contains("/Users/"));
    }

    [Fact]
    public async Task TriggerPersonImageDownloadAsync_StillRefreshes_WhenUserIdIsNotConfigured()
    {
        // Refresh is not user-scoped. The old UserId null-guard silently turned the whole job into
        // a no-op whenever jellyfin-user-id was unset.
        var (service, requests) = CreateServiceCapturingHttp();

        await service.TriggerPersonImageDownloadAsync(
            "abc-123", new JellyfinApiConfig("http://jf:8096", "key", null));

        var req = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Contains("/Items/abc-123/Refresh", req.RequestUri!.ToString());
    }
}

internal sealed class PersonRefreshCapturingHandler(Action<HttpRequestMessage> capture) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        capture(request);
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
    }
}
