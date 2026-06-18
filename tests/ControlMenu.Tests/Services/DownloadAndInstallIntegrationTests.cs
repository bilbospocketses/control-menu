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
/// Integration tests verifying that the integrity pipeline runs before extraction.
/// A tampered artifact must be rejected and never extracted; an Unverified artifact
/// without consent must return NeedsUnverifiedConfirmation without extracting.
/// </summary>
public class DownloadAndInstallIntegrationTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory;
    private readonly Mock<ICommandExecutor> _mockExecutor = new();
    private readonly Mock<IHttpClientFactory> _mockHttpFactory = new();
    private readonly Mock<IConfigurationService> _mockConfig = new();
    private readonly Mock<IGo2RtcService> _mockGo2Rtc = new();
    private readonly Mock<IDependencyPathResolver> _mockResolver = new();
    private readonly WsScrcpyService _wsScrcpy;
    private readonly string _tempRoot;

    // Fake extractor spy: tracks whether Extract was called.
    private readonly SpyArchiveExtractor _extractorSpy = new();

    public DownloadAndInstallIntegrationTests()
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

        _tempRoot = Path.Combine(Path.GetTempPath(), "ControlMenuIntegrationTests", Guid.NewGuid().ToString());
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

    private static byte[] BuildZip(string entryName = "tool/fake-tool.exe")
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
            ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(entryName);
            using var es = entry.Open();
            es.WriteByte(0);
        }
        return ms.ToArray();
    }

    private DependencyManagerService CreateService(IArtifactVerifier verifier, FakeModule? module = null)
    {
        IReadOnlyList<IToolModule> modules = module is not null
            ? [module]
            : [];
        return new DependencyManagerService(
            _dbFactory,
            modules,
            _mockExecutor.Object,
            _mockHttpFactory.Object,
            _mockConfig.Object,
            _wsScrcpy,
            _mockGo2Rtc.Object,
            _mockResolver.Object,
            NullLogger<DependencyManagerService>.Instance,
            verifier,
            _extractorSpy);
    }

    private async Task<(Guid DepId, FakeModule Module)> SeedDependencyAsync(
        string moduleId = "test-module",
        string name = "fake-tool",
        string installPath = "",
        Dictionary<string, string>? knownHashes = null,
        string latestKnownVersion = "2.0.0")
    {
        var installDir = string.IsNullOrEmpty(installPath)
            ? Path.Combine(_tempRoot, $"{name}-install")
            : installPath;
        Directory.CreateDirectory(installDir);

        var depId = Guid.NewGuid();
        using var db = _dbFactory.CreateDbContext();
        db.Dependencies.Add(new Dependency
        {
            Id = depId,
            ModuleId = moduleId,
            Name = name,
            SourceType = UpdateSourceType.GitHub,
            Status = DependencyStatus.UpdateAvailable,
            InstalledVersion = "1.0.0",
            LatestKnownVersion = latestKnownVersion,
            DownloadUrl = $"https://example.com/{name}.zip"
        });
        await db.SaveChangesAsync();

        var module = new FakeModule(moduleId, "Test",
        [
            new ModuleDependency
            {
                Name = name,
                ExecutableName = name,
                VersionCommand = $"{name} --version",
                VersionPattern = @"([\d.]+)",
                SourceType = UpdateSourceType.GitHub,
                GitHubRepo = $"example/{name}",
                InstallPath = installDir,
                KnownHashes = knownHashes ?? new Dictionary<string, string>()
            }
        ]);

        return (depId, module);
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task TamperedArtifact_IsNotExtracted_AndFails()
    {
        // Arrange: a dependency with a pinned hash for "2.0.0"
        const string pinnedHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"; // wrong hash
        var (depId, module) = await SeedDependencyAsync(
            knownHashes: new Dictionary<string, string> { ["2.0.0"] = pinnedHash },
            latestKnownVersion: "2.0.0");

        var zipBytes = BuildZip();
        _mockHttpFactory.Setup(f => f.CreateClient("dependency-updates"))
            .Returns(new HttpClient(new MockBinaryHttpHandler(zipBytes)));

        // ArtifactVerifier will compute real SHA-256 of the zip (which won't match pinnedHash)
        // Use the real verifier with a fake authenticode inspector that does nothing
        var fakeAuthenticode = new Mock<IAuthenticodeInspector>();
        fakeAuthenticode.Setup(a => a.Inspect(It.IsAny<string>()))
            .Returns(new AuthenticodeInfo(IsSigned: false, IsTrusted: false, SubjectCn: null));

        // Use a dummy HttpClient for the verifier's http client (T2 not used here)
        var verifierHttp = new HttpClient(new MockBinaryHttpHandler(zipBytes));
        var realVerifier = new ArtifactVerifier(fakeAuthenticode.Object, verifierHttp);

        var service = CreateService(realVerifier, module);
        var asset = new AssetMatch("fake-tool.zip", "https://example.com/fake-tool.zip", zipBytes.Length, AutoSelected: true);

        // Act
        var result = await service.DownloadAndInstallAsync(depId, asset, allowUnverified: false);

        // Assert: failed, extractor never called
        Assert.False(result.Success);
        Assert.Equal(UpdateOutcome.Failed, result.Outcome);
        Assert.False(_extractorSpy.Called, "Extractor must NOT be called when hash verification fails.");
        Assert.Contains("mismatch", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unverified_WithoutConsent_ReturnsNeedsConfirmation()
    {
        // Arrange: no knownHashes, no checksum, no signer → verifier returns Unverified
        var (depId, module) = await SeedDependencyAsync(
            latestKnownVersion: "2.0.0");

        var zipBytes = BuildZip();
        _mockHttpFactory.Setup(f => f.CreateClient("dependency-updates"))
            .Returns(new HttpClient(new MockBinaryHttpHandler(zipBytes)));

        // Fake verifier that always returns Unverified
        var fakeVerifier = new Mock<IArtifactVerifier>();
        fakeVerifier
            .Setup(v => v.VerifyAsync(It.IsAny<string>(), It.IsAny<ModuleDependency>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationResult(false, VerificationTier.Unverified, null, "no cryptographic tier available"));

        var service = CreateService(fakeVerifier.Object, module);
        var asset = new AssetMatch("fake-tool.zip", "https://example.com/fake-tool.zip", zipBytes.Length, AutoSelected: true);

        // Act
        var result = await service.DownloadAndInstallAsync(depId, asset, allowUnverified: false);

        // Assert: NeedsUnverifiedConfirmation, extractor never called
        Assert.False(result.Success);
        Assert.Equal(UpdateOutcome.NeedsUnverifiedConfirmation, result.Outcome);
        Assert.False(_extractorSpy.Called, "Extractor must NOT be called when artifact is Unverified without consent.");
        Assert.Equal("fake-tool", result.ConfirmTool);
        Assert.NotNull(result.ConfirmVersion);
        Assert.NotNull(result.ConfirmHost);
    }
}

/// <summary>Spy implementation of IArchiveExtractor: records whether Extract was called.</summary>
internal sealed class SpyArchiveExtractor : IArchiveExtractor
{
    public bool Called { get; private set; }

    public void Extract(string archivePath, string destDir)
    {
        Called = true;
        // Create the destDir so callers that check for it don't crash.
        Directory.CreateDirectory(destDir);
    }
}
