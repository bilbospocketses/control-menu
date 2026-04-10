using ControlMenu.Data;
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Modules;
using ControlMenu.Services;
using ControlMenu.Tests.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Services;

public class DependencyScanTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<ICommandExecutor> _mockExecutor = new();
    private readonly Mock<IHttpClientFactory> _mockHttpFactory = new();

    public DependencyScanTests()
    {
        _db = TestDbContextFactory.Create();
    }

    public void Dispose() => _db.Dispose();

    private DependencyManagerService CreateService(params IToolModule[] modules)
    {
        return new DependencyManagerService(
            _db, modules, _mockExecutor.Object, _mockHttpFactory.Object,
            NullLogger<DependencyManagerService>.Instance);
    }

    [Fact]
    public async Task ScanForDependenciesAsync_FindsToolOnPath()
    {
        // Arrange: module declares adb dependency, no DB entity with InstalledVersion
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

        _mockExecutor.Setup(e => e.ExecuteAsync("adb", "--version", null, default))
            .ReturnsAsync(new CommandResult(0, "Android Debug Bridge version 36.0.0", "", false));

        var service = CreateService(module);
        var results = await service.ScanForDependenciesAsync();

        Assert.Single(results);
        var result = results[0];
        Assert.True(result.Found);
        Assert.Equal("adb", result.Name);
        Assert.Equal("android-module", result.ModuleId);
        Assert.Equal("36.0.0", result.Version);
        Assert.Equal("PATH", result.Source);
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

        _db.Dependencies.Add(new Dependency
        {
            Id = Guid.NewGuid(),
            ModuleId = "jellyfin-module",
            Name = "docker",
            SourceType = UpdateSourceType.Manual,
            Status = DependencyStatus.UpToDate,
            InstalledVersion = "27.1.0"
        });
        await _db.SaveChangesAsync();

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
