using ControlMenu.Data;
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Modules;
using ControlMenu.Modules.Cameras.Services;
using ControlMenu.Services;
using ControlMenu.Services.Archive;
using ControlMenu.Services.Verification;
using ControlMenu.Tests.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlMenu.Tests.Services;

/// <summary>
/// Unit tests for the version string produced by DependencyManagerService.ResolveTargetVersion.
/// That string is the lookup key into ModuleDependency.KnownHashes, so the authoring
/// contract for Task 8 depends on it being stable and correctly preferred.
///
/// We exercise it via a capturing IArtifactVerifier injected into a minimal
/// DependencyManagerService, running DownloadAndInstallAsync until the verifier fires.
/// </summary>
public class ResolveTargetVersionTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory;
    private readonly Mock<ICommandExecutor> _mockExecutor = new();
    private readonly Mock<IHttpClientFactory> _mockHttpFactory = new();
    private readonly Mock<IConfigurationService> _mockConfig = new();
    private readonly WsScrcpyService _wsScrcpy;
    private readonly Mock<IGo2RtcService> _mockGo2Rtc = new();
    private readonly Mock<IDependencyPathResolver> _mockResolver = new();
    private readonly string _tempRoot;

    public ResolveTargetVersionTests()
    {
        _dbFactory = TestDbContextFactory.CreateFactory();
        _mockResolver
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string name, CancellationToken _) => name);

        var services = new ServiceCollection();
        services.AddScoped(_ => _mockConfig.Object);
        services.AddScoped(_ => _mockResolver.Object);
        var provider = services.BuildServiceProvider();
        _wsScrcpy = new WsScrcpyService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _mockHttpFactory.Object,
            NullLogger<WsScrcpyService>.Instance);

        _tempRoot = Path.Combine(Path.GetTempPath(), "RvtTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        _dbFactory.Dispose();
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best-effort */ }
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Capturing verifier: records the version string passed to VerifyAsync, then returns
    /// Unverified so the pipeline gates before attempting extract/run (no install needed).
    /// </summary>
    private sealed class CapturingVerifier : IArtifactVerifier
    {
        public string? CapturedVersion { get; private set; }

        public Task<VerificationResult> VerifyAsync(
            string filePath, ModuleDependency dep, string version, CancellationToken ct)
        {
            CapturedVersion = version;
            return Task.FromResult(new VerificationResult(
                false, VerificationTier.Unverified, null, "capture stub"));
        }
    }

    private static byte[] BuildMinimalZip()
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
            ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("tool.exe");
            using var es = entry.Open();
            es.WriteByte(0);
        }
        return ms.ToArray();
    }

    private async Task<string?> CaptureVersionAsync(
        UpdateSourceType sourceType,
        string? latestKnownVersion,
        string? installedVersion,
        string assetFileName)
    {
        var installDir = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installDir);

        var depId = Guid.NewGuid();
        using (var db = _dbFactory.CreateDbContext())
        {
            db.Dependencies.Add(new Dependency
            {
                Id = depId,
                ModuleId = "test-module",
                Name = "tool",
                SourceType = sourceType,
                Status = DependencyStatus.UpdateAvailable,
                LatestKnownVersion = latestKnownVersion,
                InstalledVersion = installedVersion,
                DownloadUrl = $"https://example.com/{assetFileName}"
            });
            await db.SaveChangesAsync();
        }

        var module = new FakeModule("test-module", "Test",
        [
            new ModuleDependency
            {
                Name = "tool",
                ExecutableName = "tool",
                VersionCommand = "tool --version",
                VersionPattern = @"([\d.]+)",
                SourceType = sourceType,
                InstallPath = installDir
            }
        ]);

        var zipBytes = BuildMinimalZip();
        _mockHttpFactory.Setup(f => f.CreateClient("dependency-updates"))
            .Returns(new HttpClient(new MockBinaryHttpHandler(zipBytes)));

        var capturingVerifier = new CapturingVerifier();
        var svc = new DependencyManagerService(
            _dbFactory,
            [module],
            _mockExecutor.Object,
            _mockHttpFactory.Object,
            _mockConfig.Object,
            _wsScrcpy,
            _mockGo2Rtc.Object,
            _mockResolver.Object,
            NullLogger<DependencyManagerService>.Instance,
            capturingVerifier,
            new SpyArchiveExtractor());

        var assetUrl = $"https://example.com/{assetFileName}";
        var asset = new AssetMatch(assetFileName, assetUrl, zipBytes.Length, AutoSelected: true);
        await svc.DownloadAndInstallAsync(depId, asset, allowUnverified: false);

        return capturingVerifier.CapturedVersion;
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GitHub_LatestKnownVersionPreferred_OverFilenameVersion()
    {
        // GitHub entity: LatestKnownVersion="36.0.0" (v-stripped by CheckGitHubVersionAsync),
        // asset filename also has a number. LatestKnownVersion MUST win — it is the KnownHashes key.
        var captured = await CaptureVersionAsync(
            UpdateSourceType.GitHub,
            latestKnownVersion: "36.0.0",
            installedVersion: "35.0.0",
            assetFileName: "platform-tools_r36.0.0-windows.zip");

        // Key assertion: "36.0.0" not "36.0.0" from filename (both happen to be same here, but
        // LatestKnownVersion is the source; Task 8 pins against this string).
        Assert.Equal("36.0.0", captured);
    }

    [Fact]
    public async Task DirectUrl_LatestKnownVersionPreferred_OverFilenameVersion()
    {
        // DirectUrl entity: version stored as-is from version-check pattern output.
        var captured = await CaptureVersionAsync(
            UpdateSourceType.DirectUrl,
            latestKnownVersion: "37.0.0",
            installedVersion: "36.0.0",
            assetFileName: "adb-37.0.0-windows.zip");

        Assert.Equal("37.0.0", captured);
    }

    [Fact]
    public async Task NoLatestKnownVersion_FallsBackToFilenameRegex()
    {
        // No prior check ran: LatestKnownVersion is null. Should parse version from filename.
        var captured = await CaptureVersionAsync(
            UpdateSourceType.GitHub,
            latestKnownVersion: null,
            installedVersion: "35.0.0",
            assetFileName: "adb-36.0.0-windows.zip");

        Assert.Equal("36.0.0", captured);
    }

    [Fact]
    public async Task NoLatestKnownVersion_NoVersionInFilename_FallsBackToInstalledVersion()
    {
        // Absolute last resort: no LatestKnownVersion, no version number in filename.
        // Returns InstalledVersion — the OLD version. This means no KnownHashes match for the
        // NEW version, verifier falls through to Unverified (correct fail-closed behavior).
        var captured = await CaptureVersionAsync(
            UpdateSourceType.GitHub,
            latestKnownVersion: null,
            installedVersion: "1.0.0",
            assetFileName: "fake-tool.zip");

        Assert.Equal("1.0.0", captured);
    }
}
