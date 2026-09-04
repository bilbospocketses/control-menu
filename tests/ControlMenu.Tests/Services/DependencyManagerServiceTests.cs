using ControlMenu.Data;
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Modules;
using ControlMenu.Modules.Cameras.Services;
using ControlMenu.Services;
using ControlMenu.Services.Archive;
using ControlMenu.Services.Verification;
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
    private readonly Mock<IDependencyPathResolver> _mockResolver = new();
    private readonly string _tempRoot;

    public DependencyManagerServiceTests()
    {
        _dbFactory = TestDbContextFactory.CreateFactory();
        _mockResolver.Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string name, CancellationToken _) => name);
        var services = new ServiceCollection();
        services.AddScoped(_ => _mockConfig.Object);
        services.AddScoped(_ => _mockResolver.Object);
        var provider = services.BuildServiceProvider();
        _wsScrcpy = new WsScrcpyService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _mockHttpFactory.Object,
            NullLogger<WsScrcpyService>.Instance);
        _tempRoot = Path.Combine(Path.GetTempPath(), "ControlMenuTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        _dbFactory.Dispose();
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best-effort */ }
    }

    /// <summary>
    /// Creates a fake local install dir under the test temp root with an empty file
    /// matching <paramref name="exeName"/> (with .exe suffix on Windows). Returns the
    /// install dir and the resolved exe path so tests can wire the executor mock to it.
    /// </summary>
    private (string InstallDir, string LocalExePath) MakeLocalInstall(string subdir, string exeName)
    {
        var installDir = Path.Combine(_tempRoot, subdir);
        Directory.CreateDirectory(installDir);
        var resolvedExe = exeName;
        if (OperatingSystem.IsWindows() && !resolvedExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            resolvedExe += ".exe";
        var localExe = Path.Combine(installDir, resolvedExe);
        File.WriteAllText(localExe, "");
        return (installDir, localExe);
    }

    // Default: always-verified stub so existing unit tests aren't affected by the integrity gate.
    private static readonly IArtifactVerifier _alwaysVerifiedVerifier =
        new StubArtifactVerifier(new VerificationResult(true, VerificationTier.PinnedHash, "SHA-256", "stub — always verified"));
    private static readonly IArchiveExtractor _realExtractor = new ArchiveExtractor();

    private DependencyManagerService CreateService(params IToolModule[] modules)
    {
        return new DependencyManagerService(
            _dbFactory, modules, _mockExecutor.Object, _mockHttpFactory.Object,
            _mockConfig.Object, _wsScrcpy, _mockGo2Rtc.Object, _mockResolver.Object,
            NullLogger<DependencyManagerService>.Instance,
            _alwaysVerifiedVerifier, _realExtractor);
    }

    [Fact]
    public async Task SyncDependenciesAsync_InsertsNewDependencies()
    {
        var (installDir, localExe) = MakeLocalInstall("tool-a-install", "tool-a");
        var module = new FakeModule("test-module", "Test",
        [
            new ModuleDependency
            {
                Name = "tool-a",
                ExecutableName = "tool-a",
                VersionCommand = "tool-a --version",
                VersionPattern = @"([\d.]+)",
                SourceType = UpdateSourceType.Manual,
                ProjectHomeUrl = "https://example.com",
                InstallPath = installDir
            }
        ]);

        _mockExecutor.Setup(e => e.ExecuteAsync(localExe, "--version", null, default))
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

        var (installDir, localExe) = MakeLocalInstall("tool-a-update", "tool-a");
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
                ProjectHomeUrl = "https://new-url.com",
                InstallPath = installDir
            }
        ]);

        _mockExecutor.Setup(e => e.ExecuteAsync(localExe, "--version", null, default))
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
        var (installDir, localExe) = MakeLocalInstall("fake-tool-install", "fake-tool");
        var module = new FakeModule("android-module", "Android",
        [
            new ModuleDependency
            {
                Name = "fake-tool",
                ExecutableName = "fake-tool",
                VersionCommand = "fake-tool --version",
                VersionPattern = @"fake-tool ([\d.]+)",
                SourceType = UpdateSourceType.GitHub,
                GitHubRepo = "example/fake-tool",
                InstallPath = installDir
            }
        ]);

        _mockExecutor.Setup(e => e.ExecuteAsync(localExe, "--version", null, default))
            .ReturnsAsync(new CommandResult(0, "fake-tool 3.3.2", "", false));

        var depId = Guid.NewGuid();
        using var setupDb = _dbFactory.CreateDbContext();
        setupDb.Dependencies.Add(new Dependency
        {
            Id = depId,
            ModuleId = "android-module",
            Name = "fake-tool",
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
        var (installDir, localExe) = MakeLocalInstall("adb-install", "adb");
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
                VersionCheckPattern = @"<major>(\d+)</major>\s*<minor>(\d+)</minor>\s*<micro>(\d+)</micro>",
                InstallPath = installDir
            }
        ]);

        _mockExecutor.Setup(e => e.ExecuteAsync(localExe, "--version", null, default))
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
        var (installDir, localExe) = MakeLocalInstall("docker-install", "docker");
        var module = new FakeModule("jellyfin-module", "Jellyfin",
        [
            new ModuleDependency
            {
                Name = "docker",
                ExecutableName = "docker",
                VersionCommand = "docker --version",
                VersionPattern = @"Docker version ([\d.]+)",
                SourceType = UpdateSourceType.Manual,
                InstallPath = installDir
            }
        ]);

        _mockExecutor.Setup(e => e.ExecuteAsync(localExe, "--version", null, default))
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
    public async Task CheckDependency_WsScrcpyWeb_BlankUrlSetting_PingsTheDefault_InsteadOfThrowing()
    {
        // The health check read the raw setting and guarded it with `?? default`, which a stored
        // blank does not trigger: HttpClient then got a relative URI with no BaseAddress and threw
        // InvalidOperationException straight out of the check -- past the catch, which filtered on
        // HttpRequestException. One blank Settings field took the whole dependency sweep with it.
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-mode", It.IsAny<string?>())).ReturnsAsync("external");
        _mockConfig.Setup(c => c.GetSettingAsync("wsscrcpy-url", It.IsAny<string?>())).ReturnsAsync("   ");
        string? captured = null;
        _mockHttpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(new CapturingHttpHandler(req => captured = req.RequestUri?.ToString())));

        var module = new FakeModule("android-devices", "Android", [WsScrcpyModuleDep()]);
        var dep = await SeedWsScrcpyWebDependencyAsync();

        var result = await CreateService(module).CheckDependencyAsync(dep.Id);

        Assert.Equal("http://localhost:8000/", captured);
        Assert.Equal(DependencyStatus.CheckFailed, result.Status);   // the capturing handler answers 404
    }

    // NOTE: CheckDependency_WsScrcpyWeb_ManagedMode_SkipsExternalBranch removed in Task 1 —
    // WsScrcpyService is now always External; the Managed-mode branch no longer exists.
    // Task 5 will add a replacement test covering the always-external HTTP-ping path.

    [Fact]
    public async Task DownloadAndInstallAsync_AdbKillServer_ResolvesViaDependencyPathResolver_NotBareName()
    {
        // Local-Dependencies-Only regression guard. The adb update path stops the running ADB
        // server before swapping the binary. That kill-server MUST resolve adb to its local path,
        // never invoke a bare "adb" off PATH. Drives the full download -> extract -> verify -> swap
        // pipeline (otherwise uncovered) far enough to reach the kill-server call.
        var exeLeaf = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        byte[] zipBytes;
        using (var ms = new MemoryStream())
        {
            using (var zip = new System.IO.Compression.ZipArchive(
                ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = zip.CreateEntry($"platform-tools/{exeLeaf}");
                await using var es = entry.Open();
                es.WriteByte(0);
            }
            zipBytes = ms.ToArray();
        }

        var installDir = Path.Combine(_tempRoot, "adb-install-target");
        var module = new FakeModule("android-devices", "Android",
        [
            new ModuleDependency
            {
                Name = "adb",
                ExecutableName = "adb",
                VersionCommand = "adb --version",
                VersionPattern = @"Android Debug Bridge version ([\d.]+)",
                SourceType = UpdateSourceType.GitHub,
                GitHubRepo = "example/adb",
                InstallPath = installDir
            }
        ]);

        var depId = Guid.NewGuid();
        using (var setupDb = _dbFactory.CreateDbContext())
        {
            setupDb.Dependencies.Add(new Dependency
            {
                Id = depId,
                ModuleId = "android-devices",
                Name = "adb",
                SourceType = UpdateSourceType.GitHub,
                Status = DependencyStatus.UpToDate,
                InstalledVersion = "35.0.0"
            });
            await setupDb.SaveChangesAsync();
        }

        // Download serves our in-memory zip on the "dependency-updates" client.
        _mockHttpFactory.Setup(f => f.CreateClient("dependency-updates"))
            .Returns(new HttpClient(new MockBinaryHttpHandler(zipBytes)));

        // Post-extract verify (adb --version) on the freshly extracted binary succeeds.
        _mockExecutor.Setup(e => e.ExecuteAsync(
                It.Is<string>(s => Path.GetFileNameWithoutExtension(s) == "adb"),
                "--version", null, default))
            .ReturnsAsync(new CommandResult(0, "Android Debug Bridge version 36.0.0", "", false));

        // Resolver maps (android-devices, adb) to a distinct local path; kill-server must use it.
        const string resolvedAdb = "/cm/local/platform-tools/adb.exe";
        _mockResolver.Setup(r => r.ResolveAsync("android-devices", "adb", It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedAdb);
        _mockExecutor.Setup(e => e.ExecuteAsync(resolvedAdb, "kill-server", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService(module);
        var asset = new AssetMatch("adb.zip", "https://example.com/adb.zip", zipBytes.Length, AutoSelected: true);
        await service.DownloadAndInstallAsync(depId, asset);

        _mockExecutor.Verify(
            e => e.ExecuteAsync(resolvedAdb, "kill-server", null, It.IsAny<CancellationToken>()),
            Times.Once,
            "adb kill-server must run via the resolved local path.");
        _mockExecutor.Verify(
            e => e.ExecuteAsync("adb", "kill-server", It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "DependencyManagerService must NOT kill-server via bare 'adb' — local-deps rule.");
    }

    [Fact]
    public async Task CheckAllAsync_RunsChecksConcurrently_BoundedByLimit()
    {
        const int depCount = 6; // > MaxConcurrentChecks so the bound is observable

        var deps = new ModuleDependency[depCount];
        for (int i = 0; i < depCount; i++)
        {
            deps[i] = new ModuleDependency
            {
                Name = $"dep-{i}",
                ExecutableName = $"dep-{i}",
                VersionCommand = $"dep-{i} --version",
                VersionPattern = @"([\d.]+)",
                SourceType = UpdateSourceType.GitHub,
                GitHubRepo = $"example/dep-{i}",
            };
        }
        var module = new FakeModule("concurrency-module", "Concurrency", deps);

        // Connection-per-context file DB so genuinely concurrent checks are safe (mirrors prod).
        using var fileFactory = TestDbContextFactory.CreateFileBackedFactory();
        using (var setupDb = fileFactory.CreateDbContext())
        {
            for (int i = 0; i < depCount; i++)
            {
                setupDb.Dependencies.Add(new Dependency
                {
                    Id = Guid.NewGuid(),
                    ModuleId = "concurrency-module",
                    Name = $"dep-{i}",
                    SourceType = UpdateSourceType.GitHub,
                    Status = DependencyStatus.UpToDate,
                    InstalledVersion = "1.0.0"
                });
            }
            await setupDb.SaveChangesAsync();
        }

        // The version command resolves instantly; the GitHub HTTP call is the concurrency probe.
        _mockExecutor.Setup(e => e.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, "tool 1.0.0", "", false));

        var handler = new ConcurrencyTrackingHandler(@"{""tag_name"": ""v9.9.9"", ""assets"": []}");
        _mockHttpFactory.Setup(f => f.CreateClient("github-api")).Returns(new HttpClient(handler));

        var service = new DependencyManagerService(
            fileFactory, [module], _mockExecutor.Object, _mockHttpFactory.Object,
            _mockConfig.Object, _wsScrcpy, _mockGo2Rtc.Object, _mockResolver.Object,
            NullLogger<DependencyManagerService>.Instance, _alwaysVerifiedVerifier, _realExtractor);

        var results = await service.CheckAllAsync();

        Assert.Equal(depCount, results.Count);
        Assert.True(handler.MaxObserved >= 2,
            $"checks ran sequentially (max in-flight = {handler.MaxObserved}); expected concurrent execution");
        Assert.True(handler.MaxObserved <= DependencyManagerService.MaxConcurrentChecks,
            $"in-flight concurrency {handler.MaxObserved} exceeded the bound {DependencyManagerService.MaxConcurrentChecks}");
    }

    // ---------------------------------------------------------------------
    // GitHub asset resolution -- platform-token veto regression
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("vtracer", @"vtracer-x86_64-pc-windows-msvc\.zip", "vtracer-x86_64-pc-windows-msvc.zip")]
    [InlineData("magick", @"ImageMagick-[\d.]+-\d+-portable-Q8-x64\.7z", "ImageMagick-7.1.2-30-portable-Q8-x64.7z")]
    public async Task ResolveDownloadAssetAsync_ExplicitAssetPattern_MatchesRegardlessOfPlatformToken(
        string name, string assetPattern, string assetName)
    {
        // Regression: asset matching required BOTH the declared AssetPattern AND the asset name to
        // contain a platform token ("win64" on Windows x64). Real assets do not name the platform
        // that way -- ImageMagick uses "-Q8-x64", vtracer uses "x86_64-pc-windows-msvc" -- so both
        // were vetoed despite their patterns matching exactly, and neither could be installed or
        // updated in-app. Version checks still succeeded (they read tag_name, not assets), so the
        // symptom was a dependency stuck at "update available" with a blank installed version.
        // Packaged builds masked it -- the MSI seeds these binaries -- so only dev installs and
        // in-app updates ever reached the resolver.
        var (installDir, _) = MakeLocalInstall(name + "-install", name);
        var module = new FakeModule("imaging", "Imaging",
        [
            new ModuleDependency
            {
                Name = name,
                ExecutableName = name,
                VersionCommand = name + " --version",
                VersionPattern = @"([\d.]+)",
                SourceType = UpdateSourceType.GitHub,
                GitHubRepo = "example/" + name,
                AssetPattern = assetPattern,
                InstallPath = installDir
            }
        ]);

        var depId = Guid.NewGuid();
        using var setupDb = _dbFactory.CreateDbContext();
        setupDb.Dependencies.Add(new Dependency
        {
            Id = depId,
            ModuleId = "imaging",
            Name = name,
            SourceType = UpdateSourceType.GitHub,
            Status = DependencyStatus.UpdateAvailable
        });
        await setupDb.SaveChangesAsync();

        var json = "{\"tag_name\":\"v1.0.0\",\"assets\":[{\"name\":\"" + assetName +
                   "\",\"browser_download_url\":\"https://example.com/" + assetName + "\",\"size\":4242}]}";
        _mockHttpFactory.Setup(f => f.CreateClient("github-api")).Returns(new HttpClient(new MockHttpHandler(json)));

        var asset = await CreateService(module).ResolveDownloadAssetAsync(depId);

        Assert.NotNull(asset);
        Assert.Equal(assetName, asset!.FileName);
        Assert.Equal(4242, asset.SizeBytes);
    }

    [Fact]
    public async Task ResolveDownloadAssetAsync_NoAssetPattern_StillFiltersByPlatformToken()
    {
        // The platform-token filter is the heuristic for dependencies that declare no AssetPattern
        // and fall back to matching on the bare executable name. That fallback must keep filtering,
        // otherwise a Linux asset could be selected on Windows.
        var (installDir, _) = MakeLocalInstall("tool-b-install", "tool-b");
        var module = new FakeModule("test-module", "Test",
        [
            new ModuleDependency
            {
                Name = "tool-b",
                ExecutableName = "tool-b",
                VersionCommand = "tool-b --version",
                VersionPattern = @"([\d.]+)",
                SourceType = UpdateSourceType.GitHub,
                GitHubRepo = "example/tool-b",
                InstallPath = installDir
            }
        ]);

        var depId = Guid.NewGuid();
        using var setupDb = _dbFactory.CreateDbContext();
        setupDb.Dependencies.Add(new Dependency
        {
            Id = depId,
            ModuleId = "test-module",
            Name = "tool-b",
            SourceType = UpdateSourceType.GitHub,
            Status = DependencyStatus.UpdateAvailable
        });
        await setupDb.SaveChangesAsync();

        var json = "{\"tag_name\":\"v1.0.0\",\"assets\":[{\"name\":\"tool-b-linux-x86_64.tar.gz\"," +
                   "\"browser_download_url\":\"https://example.com/tool-b-linux.tar.gz\",\"size\":10}]}";
        _mockHttpFactory.Setup(f => f.CreateClient("github-api")).Returns(new HttpClient(new MockHttpHandler(json)));

        var asset = await CreateService(module).ResolveDownloadAssetAsync(depId);

        Assert.Null(asset);
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

internal class MockBinaryHttpHandler(byte[] payload) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });
}

/// <summary>
/// Stub verifier that always returns a fixed result, used so existing download/install
/// tests are not derailed by the integrity gate.
/// </summary>
internal class StubArtifactVerifier(ControlMenu.Services.Verification.VerificationResult result)
    : ControlMenu.Services.Verification.IArtifactVerifier
{
    public Task<ControlMenu.Services.Verification.VerificationResult> VerifyAsync(
        string filePath, ControlMenu.Modules.ModuleDependency dep,
        string version, CancellationToken ct)
        => Task.FromResult(result);
}

/// <summary>
/// Tracks the maximum number of HTTP requests in flight at once, so a test can prove that
/// callers run concurrently (max &gt; 1) and stay within a bound. Each request lingers briefly
/// so overlap is observable.
/// </summary>
internal sealed class ConcurrencyTrackingHandler(string body, int delayMs = 100) : HttpMessageHandler
{
    private int _current;
    private int _max;
    public int MaxObserved => Volatile.Read(ref _max);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var now = Interlocked.Increment(ref _current);
        int observed;
        while (now > (observed = Volatile.Read(ref _max)))
            Interlocked.CompareExchange(ref _max, now, observed);
        try
        {
            await Task.Delay(delayMs, cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            };
        }
        finally
        {
            Interlocked.Decrement(ref _current);
        }
    }
}
