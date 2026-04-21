using ControlMenu.Data;
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Modules;
using ControlMenu.Modules.Cameras.Services;
using ControlMenu.Services;
using ControlMenu.Tests.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Net;

namespace ControlMenu.Tests.Services;

public class DependencyManagerServiceTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory;
    private readonly Mock<ICommandExecutor> _mockExecutor = new();
    private readonly Mock<IHttpClientFactory> _mockHttpFactory = new();
    private readonly Mock<IConfigurationService> _mockConfig = new();
    private readonly WsScrcpyService _wsScrcpy;
    private readonly Mock<IGo2RtcService> _mockGo2Rtc = new();

    public DependencyManagerServiceTests()
    {
        _dbFactory = TestDbContextFactory.CreateFactory();
        _wsScrcpy = new WsScrcpyService(
            new Mock<IServiceScopeFactory>().Object,
            _mockConfig.Object,
            NullLogger<WsScrcpyService>.Instance);
    }

    public void Dispose() => _dbFactory.Dispose();

    private DependencyManagerService CreateService(params IToolModule[] modules)
    {
        return new DependencyManagerService(
            _dbFactory, modules, _mockExecutor.Object, _mockHttpFactory.Object,
            _mockConfig.Object, _wsScrcpy, _mockGo2Rtc.Object, NullLogger<DependencyManagerService>.Instance);
    }

    [Fact]
    public async Task SyncDependenciesAsync_InsertsNewDependencies()
    {
        var module = new FakeModule("test-module", "Test",
        [
            new ModuleDependency
            {
                Name = "tool-a",
                ExecutableName = "tool-a",
                VersionCommand = "tool-a --version",
                VersionPattern = @"([\d.]+)",
                SourceType = UpdateSourceType.Manual,
                ProjectHomeUrl = "https://example.com"
            }
        ]);

        _mockExecutor.Setup(e => e.ExecuteAsync("tool-a", "--version", null, default))
            .ReturnsAsync(new CommandResult(0, "tool-a version 1.2.3", "", false));

        var service = CreateService(module);
        await service.SyncDependenciesAsync();

        using var assertDb = _dbFactory.CreateDbContext();
        var deps = await assertDb.Dependencies.ToListAsync();
        Assert.Single(deps);
        Assert.Equal("tool-a", deps[0].Name);
        Assert.Equal("test-module", deps[0].ModuleId);
        Assert.Equal("1.2.3", deps[0].InstalledVersion);
        Assert.Equal(UpdateSourceType.Manual, deps[0].SourceType);
    }

    [Fact]
    public async Task SyncDependenciesAsync_RemovesOrphanedDependencies()
    {
        using var setupDb = _dbFactory.CreateDbContext();
        setupDb.Dependencies.Add(new Dependency
        {
            Id = Guid.NewGuid(),
            ModuleId = "removed-module",
            Name = "old-tool",
            SourceType = UpdateSourceType.Manual,
            Status = DependencyStatus.UpToDate
        });
        await setupDb.SaveChangesAsync();

        var service = CreateService(); // no modules
        await service.SyncDependenciesAsync();

        using var assertDb2 = _dbFactory.CreateDbContext();
        Assert.Empty(await assertDb2.Dependencies.ToListAsync());
    }

    [Fact]
    public async Task SyncDependenciesAsync_UpdatesStaticFieldsFromCode()
    {
        using var setupDb = _dbFactory.CreateDbContext();
        setupDb.Dependencies.Add(new Dependency
        {
            Id = Guid.NewGuid(),
            ModuleId = "test-module",
            Name = "tool-a",
            SourceType = UpdateSourceType.Manual,
            Status = DependencyStatus.UpToDate,
            ProjectHomeUrl = "https://old-url.com"
        });
        await setupDb.SaveChangesAsync();

        var module = new FakeModule("test-module", "Test",
        [
            new ModuleDependency
            {
                Name = "tool-a",
                ExecutableName = "tool-a",
                VersionCommand = "tool-a --version",
                VersionPattern = @"([\d.]+)",
                SourceType = UpdateSourceType.GitHub,
                GitHubRepo = "owner/repo",
                ProjectHomeUrl = "https://new-url.com"
            }
        ]);

        _mockExecutor.Setup(e => e.ExecuteAsync("tool-a", "--version", null, default))
            .ReturnsAsync(new CommandResult(0, "1.0.0", "", false));

        var service = CreateService(module);
        await service.SyncDependenciesAsync();

        using var assertDb3 = _dbFactory.CreateDbContext();
        var dep = await assertDb3.Dependencies.SingleAsync();
        Assert.Equal(UpdateSourceType.GitHub, dep.SourceType);
        Assert.Equal("https://new-url.com", dep.ProjectHomeUrl);
    }

    [Fact]
    public async Task SyncDependenciesAsync_HandlesVersionCommandFailure()
    {
        var module = new FakeModule("test-module", "Test",
        [
            new ModuleDependency
            {
                Name = "missing-tool",
                ExecutableName = "missing-tool",
                VersionCommand = "missing-tool --version",
                VersionPattern = @"([\d.]+)",
                SourceType = UpdateSourceType.Manual
            }
        ]);

        _mockExecutor.Setup(e => e.ExecuteAsync("missing-tool", "--version", null, default))
            .ReturnsAsync(new CommandResult(1, "", "not found", false));

        var service = CreateService(module);
        await service.SyncDependenciesAsync();

        using var assertDb3 = _dbFactory.CreateDbContext();
        var dep = await assertDb3.Dependencies.SingleAsync();
        Assert.Null(dep.InstalledVersion);
    }

    [Fact]
    public async Task GetUpdateAvailableCountAsync_ReturnsCorrectCount()
    {
        using var setupDb = _dbFactory.CreateDbContext();
        setupDb.Dependencies.Add(new Dependency
        {
            Id = Guid.NewGuid(), ModuleId = "m", Name = "a",
            Status = DependencyStatus.UpdateAvailable, SourceType = UpdateSourceType.Manual
        });
        setupDb.Dependencies.Add(new Dependency
        {
            Id = Guid.NewGuid(), ModuleId = "m", Name = "b",
            Status = DependencyStatus.UpToDate, SourceType = UpdateSourceType.Manual
        });
        setupDb.Dependencies.Add(new Dependency
        {
            Id = Guid.NewGuid(), ModuleId = "m", Name = "c",
            Status = DependencyStatus.UpdateAvailable, SourceType = UpdateSourceType.Manual
        });
        await setupDb.SaveChangesAsync();

        var service = CreateService();
        var count = await service.GetUpdateAvailableCountAsync();

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CheckDependencyAsync_GitHub_DetectsUpdateAvailable()
    {
        var module = new FakeModule("android-module", "Android",
        [
            new ModuleDependency
            {
                Name = "scrcpy",
                ExecutableName = "scrcpy",
                VersionCommand = "scrcpy --version",
                VersionPattern = @"scrcpy ([\d.]+)",
                SourceType = UpdateSourceType.GitHub,
                GitHubRepo = "Genymobile/scrcpy"
            }
        ]);

        _mockExecutor.Setup(e => e.ExecuteAsync("scrcpy", "--version", null, default))
            .ReturnsAsync(new CommandResult(0, "scrcpy 3.3.2", "", false));

        var depId = Guid.NewGuid();
        using var setupDb = _dbFactory.CreateDbContext();
        setupDb.Dependencies.Add(new Dependency
        {
            Id = depId,
            ModuleId = "android-module",
            Name = "scrcpy",
            SourceType = UpdateSourceType.GitHub,
            Status = DependencyStatus.UpToDate,
            InstalledVersion = "3.3.2"
        });
        await setupDb.SaveChangesAsync();

        var handler = new MockHttpHandler(@"{""tag_name"": ""v3.3.4"", ""assets"": []}");
        var httpClient = new HttpClient(handler);
        _mockHttpFactory.Setup(f => f.CreateClient("github-api")).Returns(httpClient);

        var service = CreateService(module);
        var result = await service.CheckDependencyAsync(depId);

        Assert.Equal(DependencyStatus.UpdateAvailable, result.Status);
        Assert.Equal("3.3.2", result.InstalledVersion);
        Assert.Equal("3.3.4", result.LatestVersion);
    }

    [Fact]
    public async Task CheckDependencyAsync_DirectUrl_ParsesXmlVersion()
    {
        var module = new FakeModule("android-module", "Android",
        [
            new ModuleDependency
            {
                Name = "adb",
                ExecutableName = "adb",
                VersionCommand = "adb --version",
                VersionPattern = @"Android Debug Bridge version ([\d.]+)",
                SourceType = UpdateSourceType.DirectUrl,
                VersionCheckUrl = "https://dl.google.com/android/repository/repository2-3.xml",
                VersionCheckPattern = @"<major>(\d+)</major>\s*<minor>(\d+)</minor>\s*<micro>(\d+)</micro>"
            }
        ]);

        _mockExecutor.Setup(e => e.ExecuteAsync("adb", "--version", null, default))
            .ReturnsAsync(new CommandResult(0, "Android Debug Bridge version 36.0.0", "", false));

        var depId = Guid.NewGuid();
        using var setupDb = _dbFactory.CreateDbContext();
        setupDb.Dependencies.Add(new Dependency
        {
            Id = depId,
            ModuleId = "android-module",
            Name = "adb",
            SourceType = UpdateSourceType.DirectUrl,
            Status = DependencyStatus.UpToDate,
            InstalledVersion = "36.0.0"
        });
        await setupDb.SaveChangesAsync();

        var xmlContent = "<repo><major>37</major><minor>0</minor><micro>0</micro></repo>";
        var handler = new MockHttpHandler(xmlContent);
        var httpClient = new HttpClient(handler);
        _mockHttpFactory.Setup(f => f.CreateClient("dependency-updates")).Returns(httpClient);

        var service = CreateService(module);
        var result = await service.CheckDependencyAsync(depId);

        Assert.Equal(DependencyStatus.UpdateAvailable, result.Status);
        Assert.Equal("36.0.0", result.InstalledVersion);
        Assert.Equal("37.0.0", result.LatestVersion);
    }

    [Fact]
    public async Task CheckDependencyAsync_Manual_StaysUpToDate()
    {
        var module = new FakeModule("jellyfin-module", "Jellyfin",
        [
            new ModuleDependency
            {
                Name = "docker",
                ExecutableName = "docker",
                VersionCommand = "docker --version",
                VersionPattern = @"Docker version ([\d.]+)",
                SourceType = UpdateSourceType.Manual
            }
        ]);

        _mockExecutor.Setup(e => e.ExecuteAsync("docker", "--version", null, default))
            .ReturnsAsync(new CommandResult(0, "Docker version 27.1.0, build abc123", "", false));

        var depId = Guid.NewGuid();
        using var setupDb = _dbFactory.CreateDbContext();
        setupDb.Dependencies.Add(new Dependency
        {
            Id = depId,
            ModuleId = "jellyfin-module",
            Name = "docker",
            SourceType = UpdateSourceType.Manual,
            Status = DependencyStatus.UpToDate,
            InstalledVersion = "27.1.0"
        });
        await setupDb.SaveChangesAsync();

        var service = CreateService(module);
        var result = await service.CheckDependencyAsync(depId);

        Assert.Equal(DependencyStatus.UpToDate, result.Status);
        Assert.Equal("27.1.0", result.InstalledVersion);
        Assert.Null(result.LatestVersion);
    }

    // --- External-mode ws-scrcpy-web health check tests ---

    private static HttpClient HttpClientReturning(HttpStatusCode status) =>
        new(new MockHttpHandler("", status));

    private static HttpClient HttpClientThatThrows() =>
        new(new ThrowingHttpHandler());

    private async Task<Dependency> SeedWsScrcpyWebDependencyAsync()
    {
        using var db = _dbFactory.CreateDbContext();
        var dep = new Dependency
        {
            Id = Guid.NewGuid(),
            Name = "ws-scrcpy-web",
            ModuleId = "android-devices",
            SourceType = UpdateSourceType.Manual,
            Status = DependencyStatus.UpToDate
        };
        db.Dependencies.Add(dep);
        await db.SaveChangesAsync();
        return dep;
    }

    private static ModuleDependency WsScrcpyModuleDep() => new()
    {
        Name = "ws-scrcpy-web",
        ExecutableName = "node",
        VersionCommand = "node --version",
        VersionPattern = @"v([\d.]+)",
        SourceType = UpdateSourceType.Manual,
        ProjectHomeUrl = "https://example.com"
    };

    [Fact]
    public async Task CheckDependency_WsScrcpyWeb_ExternalMode_PingReturns200_UpToDate()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-mode", It.IsAny<string?>())).ReturnsAsync("external");
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-url", It.IsAny<string?>())).ReturnsAsync("http://fake:8000");
        _mockHttpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(HttpClientReturning(HttpStatusCode.OK));

        var module = new FakeModule("android-devices", "Android", [WsScrcpyModuleDep()]);
        var dep = await SeedWsScrcpyWebDependencyAsync();

        var service = CreateService(module);
        var result = await service.CheckDependencyAsync(dep.Id);

        Assert.Equal(DependencyStatus.UpToDate, result.Status);
        Assert.Null(result.ErrorMessage);
        Assert.Equal("external", result.InstalledVersion);
    }

    [Fact]
    public async Task CheckDependency_WsScrcpyWeb_ExternalMode_PingReturns503_CheckFailed()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-mode", It.IsAny<string?>())).ReturnsAsync("external");
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-url", It.IsAny<string?>())).ReturnsAsync("http://fake:8000");
        _mockHttpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(HttpClientReturning(HttpStatusCode.ServiceUnavailable));

        var module = new FakeModule("android-devices", "Android", [WsScrcpyModuleDep()]);
        var dep = await SeedWsScrcpyWebDependencyAsync();

        var service = CreateService(module);
        var result = await service.CheckDependencyAsync(dep.Id);

        Assert.Equal(DependencyStatus.CheckFailed, result.Status);
        Assert.Contains("503", result.ErrorMessage ?? "");
    }

    [Fact]
    public async Task CheckDependency_WsScrcpyWeb_ExternalMode_Unreachable_CheckFailed()
    {
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-mode", It.IsAny<string?>())).ReturnsAsync("external");
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-url", It.IsAny<string?>())).ReturnsAsync("http://fake:8000");
        _mockHttpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(HttpClientThatThrows());

        var module = new FakeModule("android-devices", "Android", [WsScrcpyModuleDep()]);
        var dep = await SeedWsScrcpyWebDependencyAsync();

        var service = CreateService(module);
        var result = await service.CheckDependencyAsync(dep.Id);

        Assert.Equal(DependencyStatus.CheckFailed, result.Status);
        Assert.Contains("unreachable", result.ErrorMessage ?? "");
    }

    [Fact]
    public async Task CheckDependency_WsScrcpyWeb_ManagedMode_SkipsExternalBranch()
    {
        // Default mode = Managed (no stub for wsscrcpy-mode). External branch must not fire.
        // Managed path calls GetInstalledVersionAsync which shells out via _executor.
        // Verify HTTP factory was NOT called.
        _mockExecutor.Setup(e => e.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(1, "", "not found", false));

        var module = new FakeModule("android-devices", "Android", [WsScrcpyModuleDep()]);
        var dep = await SeedWsScrcpyWebDependencyAsync();

        var service = CreateService(module);
        await service.CheckDependencyAsync(dep.Id);

        _mockHttpFactory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }
}

// Test helpers at bottom of file

internal class FakeModule(string id, string displayName, ModuleDependency[] deps) : IToolModule
{
    public string Id => id;
    public string DisplayName => displayName;
    public string Icon => "bi-test";
    public int SortOrder => 0;
    public IEnumerable<ModuleDependency> Dependencies => deps;
    public IEnumerable<ConfigRequirement> ConfigRequirements => [];
    public IEnumerable<NavEntry> GetNavEntries() => [];
    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}

internal class MockHttpHandler(string responseContent, System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseContent)
        });
    }
}

internal class ThrowingHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused"));
}
