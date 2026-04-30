using ControlMenu.Data;
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Modules;
using ControlMenu.Modules.Cameras.Services;
using ControlMenu.Services;
using ControlMenu.Tests.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Services;

public class DependencyScanTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory;
    private readonly Mock<ICommandExecutor> _mockExecutor = new();
    private readonly Mock<IHttpClientFactory> _mockHttpFactory = new();
    private readonly Mock<IConfigurationService> _mockConfig = new();
    private readonly WsScrcpyService _wsScrcpy;
    private readonly Mock<IGo2RtcService> _mockGo2Rtc = new();
    private readonly Mock<IDependencyPathResolver> _mockResolver = new();

    public DependencyScanTests()
    {
        _dbFactory = TestDbContextFactory.CreateFactory();
        _mockResolver.Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string name, CancellationToken _) => name);
        var services = new ServiceCollection();
        services.AddScoped<IConfigurationService>(_ => new Mock<IConfigurationService>().Object);
        services.AddScoped(_ => _mockResolver.Object);
        var provider = services.BuildServiceProvider();
        _wsScrcpy = new WsScrcpyService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WsScrcpyService>.Instance);
    }

    public void Dispose() => _dbFactory.Dispose();

    private DependencyManagerService CreateService(params IToolModule[] modules)
    {
        return new DependencyManagerService(
            _dbFactory, modules, _mockExecutor.Object, _mockHttpFactory.Object,
            _mockConfig.Object, _wsScrcpy, _mockGo2Rtc.Object, _mockResolver.Object,
            NullLogger<DependencyManagerService>.Instance);
    }

    [Fact]
    public async Task ScanForDependenciesAsync_DoesNotProbePath()
    {
        // CLAUDE.md / local-deps-only architecture: with no local install present
        // and no DB record, scan must report Not Found — never "Source = PATH"
        // and never invoke the executor against a bare tool name.
        var module = new FakeScanModule("android-module", "Android",
        [
            new ModuleDependency
            {
                Name = "adb",
                ExecutableName = "adb",
                VersionCommand = "adb --version",
                VersionPattern = @"Android Debug Bridge version ([\d.]+)",
                SourceType = UpdateSourceType.DirectUrl,
                ProjectHomeUrl = "https://developer.android.com/tools/adb"
            }
        ]);

        var service = CreateService(module);
        var results = await service.ScanForDependenciesAsync();

        Assert.Single(results);
        var result = results[0];
        Assert.False(result.Found);
        Assert.Equal("Not found", result.Source);
        Assert.All(results, r => Assert.NotEqual("PATH", r.Source));

        // No PATH probing should ever happen
        _mockExecutor.Verify(
            e => e.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), default),
            Times.Never);
    }

    [Fact]
    public async Task ScanForDependenciesAsync_ReportsNotFoundWhenMissing()
    {
        // Arrange: module declares a tool, executor returns failure for all calls
        var module = new FakeScanModule("android-module", "Android",
        [
            new ModuleDependency
            {
                Name = "scrcpy",
                ExecutableName = "scrcpy",
                VersionCommand = "scrcpy --version",
                VersionPattern = @"scrcpy ([\d.]+)",
                SourceType = UpdateSourceType.GitHub,
                ProjectHomeUrl = "https://github.com/Genymobile/scrcpy"
            }
        ]);

        _mockExecutor.Setup(e => e.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), default))
            .ReturnsAsync(new CommandResult(1, "", "not found", false));

        var service = CreateService(module);
        var results = await service.ScanForDependenciesAsync();

        Assert.Single(results);
        var result = results[0];
        Assert.False(result.Found);
        Assert.Equal("scrcpy", result.Name);
        Assert.Equal("Not found", result.Source);
        Assert.Null(result.Version);
        Assert.Null(result.Path);
    }

    [Fact]
    public async Task ScanForDependenciesAsync_ReportsAlreadyConfigured()
    {
        // Arrange: DB entity already has InstalledVersion
        var module = new FakeScanModule("jellyfin-module", "Jellyfin",
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

        using (var setupDb = _dbFactory.CreateDbContext())
        {
            setupDb.Dependencies.Add(new Dependency
            {
                Id = Guid.NewGuid(),
                ModuleId = "jellyfin-module",
                Name = "docker",
                SourceType = UpdateSourceType.Manual,
                Status = DependencyStatus.UpToDate,
                InstalledVersion = "27.1.0"
            });
            await setupDb.SaveChangesAsync();
        }

        var service = CreateService(module);
        var results = await service.ScanForDependenciesAsync();

        Assert.Single(results);
        var result = results[0];
        Assert.True(result.Found);
        Assert.Equal("docker", result.Name);
        Assert.Equal("27.1.0", result.Version);
        Assert.Equal("Previously configured", result.Source);

        // Verify no executor calls were made
        _mockExecutor.Verify(
            e => e.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), default),
            Times.Never);
    }
}

// Local FakeModule for scan tests (FakeModule in DependencyManagerServiceTests is internal to that file)
internal class FakeScanModule(string id, string displayName, ModuleDependency[] deps) : IToolModule
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
