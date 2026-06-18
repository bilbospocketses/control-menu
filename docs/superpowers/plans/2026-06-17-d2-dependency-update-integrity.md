# D2 Dependency-Update Integrity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the runtime in-app dependency updater verify every download (pinned SHA-256 -> upstream checksum -> Authenticode), enforce transport hardening always, and require explicit user confirmation when nothing cryptographic can verify - without losing auto-update-to-latest.

**Architecture:** A verification pipeline runs inside `DependencyManagerService.DownloadAndInstallAsync` between download-to-temp and extract. A new `IArtifactVerifier` returns the best tier reached; a transport guard validates the final host; archive extraction gains `.7z` (managed SharpCompress) so magick can actually update; the UI handles a Tier-4 confirmation round-trip via a new `allowUnverified` flag.

**Tech Stack:** .NET 10, C#, EF Core 10, Blazor Server, xUnit, SharpCompress (new NuGet), Windows Authenticode via WinVerifyTrust P/Invoke.

**Spec:** `docs/superpowers/specs/2026-06-17-d2-dependency-update-integrity-design.md` (read it before starting).

## Global Constraints

- Repo root (use for all git/test commands): `C:/Users/jscha/source/repos/control-menu`. Git: `git -C "C:/Users/jscha/source/repos/control-menu" ...`.
- Branch is already cut: `fix/security-hardening-batch-2` (off `origin/master`). Do NOT create a new branch.
- **Local-Dependencies-Only:** no system PATH / env-var binary resolution. Managed NuGet libraries compiled into the app are allowed (SharpCompress). A vendored native binary (the 7za fallback) would be resolved via `IDependencyPathResolver`, never PATH.
- **Fail-closed:** any cryptographic tier that runs and mismatches aborts the update; unavailable tiers fall through; only true "no tier could verify" reaches the Tier-4 confirmation.
- **Commits:** Conventional Commit style. **No AI attribution / no Co-Authored-By trailer.** ASCII-only in any PowerShell script.
- **Green gate:** `dotnet test tests/ControlMenu.Tests` passes before every commit.
- `SHA3_256` requires OS support; guard with `SHA3_256.IsSupported` and fall through when false.
- Signer-pin compares the Authenticode subject's `CN` exactly (`CN=Google LLC`) AND requires a Valid trust status.
- Test commands target the project: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~<Name>"`.

---

## File Structure

- Create `src/ControlMenu/Services/Archive/IArchiveExtractor.cs` - extract .zip/.tar.gz/.7z to a directory.
- Create `src/ControlMenu/Services/Archive/ArchiveExtractor.cs` - the implementation (moves the inline extract logic out of `DownloadAndInstallAsync` and adds .7z).
- Create `src/ControlMenu/Services/Verification/VerificationResult.cs` - `VerificationResult` record + `VerificationTier` enum.
- Create `src/ControlMenu/Services/Verification/ChecksumSource.cs` - `ChecksumSource` record + `ChecksumFormat` / `ChecksumAlgorithm` enums.
- Create `src/ControlMenu/Services/Verification/IArtifactVerifier.cs` - the verifier interface.
- Create `src/ControlMenu/Services/Verification/ArtifactVerifier.cs` - the tiered pipeline (T1/T2/T3 -> Unverified).
- Create `src/ControlMenu/Services/Verification/IAuthenticodeInspector.cs` + `WindowsAuthenticodeInspector.cs` - abstracted signature read (testable tier logic).
- Create `src/ControlMenu/Services/Verification/TransportGuard.cs` - HTTPS + final-host allowlist check.
- Modify `src/ControlMenu/Modules/ModuleDependency.cs` - add `KnownHashes`, `Checksum`, `ExpectedSigner`, `AllowedHosts`.
- Modify `src/ControlMenu/Services/UpdateResult.cs` - add `Outcome` (enum) so the UI can detect "needs confirmation".
- Modify `src/ControlMenu/Services/DependencyManagerService.cs` - insert the gate + use `IArchiveExtractor` + `allowUnverified`.
- Modify `src/ControlMenu/ServiceCollectionExtensions.cs` - register new services; configure the `dependency-updates` client redirect policy.
- Modify the 4 module files (`Imaging/ImagingModule.cs`, `AndroidDevices/AndroidDevicesModule.cs`, `Cameras/CamerasModule.cs`, `Jellyfin/JellyfinModule.cs`) - per-dep integrity config.
- Modify `src/ControlMenu/Components/Pages/Settings/DependencyManagement.razor` (+ its code) - Tier-4 confirmation round-trip.
- Modify `src/ControlMenu/ControlMenu.csproj` - add SharpCompress `PackageReference`.
- Create `scripts/update-dependency-hashes.ps1` - maintainer hash refresher.
- Tests under `tests/ControlMenu.Tests/Services/Verification/` and `.../Archive/`, plus fixtures under `tests/ControlMenu.Tests/Fixtures/`.

---

### Task 1: `.7z` extraction via SharpCompress (go/no-go for managed extraction)

**Files:**
- Modify: `src/ControlMenu/ControlMenu.csproj`
- Create: `src/ControlMenu/Services/Archive/IArchiveExtractor.cs`
- Create: `src/ControlMenu/Services/Archive/ArchiveExtractor.cs`
- Create: `tests/ControlMenu.Tests/Services/Archive/ArchiveExtractorTests.cs`
- Create fixture: `tests/ControlMenu.Tests/Fixtures/bcj-sample.7z` (built once, see Step 1)

**Interfaces:**
- Produces: `IArchiveExtractor.Extract(string archivePath, string destDir)` (sync; throws on unsupported/failed). Supported: `.zip`, `.tar.gz`, `.7z`.

- [ ] **Step 1: Build the BCJ test fixture** (one-time, requires local 7-Zip)

```bash
# Create a tiny LZMA2+BCJ .7z that mirrors magick's codec, so the test proves
# SharpCompress can decode the exact filter magick uses.
mkdir -p /tmp/bcj && printf 'MZ\x90\x00fake-exe-body-for-bcj-filter' > /tmp/bcj/sample.exe
"/c/Program Files/7-Zip/7z.exe" a -t7z -m0=LZMA2 -mf=BCJ \
  "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests/Fixtures/bcj-sample.7z" /tmp/bcj/sample.exe
```
Expected: `Everything is Ok`. Commit the fixture in Step 7.

- [ ] **Step 2: Add the SharpCompress package**

Edit `src/ControlMenu/ControlMenu.csproj`, add inside the existing `<ItemGroup>` of `PackageReference`s:
```xml
<PackageReference Include="SharpCompress" Version="0.40.0" />
```
Run: `dotnet restore "C:/Users/jscha/source/repos/control-menu/src/ControlMenu/ControlMenu.csproj"`
Expected: restore succeeds.

- [ ] **Step 3: Write the failing test**

`tests/ControlMenu.Tests/Services/Archive/ArchiveExtractorTests.cs`:
```csharp
using ControlMenu.Services.Archive;

namespace ControlMenu.Tests.Services.Archive;

public class ArchiveExtractorTests
{
    private static string FixtureDir =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private readonly ArchiveExtractor _extractor = new();

    [Fact]
    public void Extract_SevenZipWithBcjFilter_ExtractsEntry()
    {
        var archive = Path.Combine(FixtureDir, "bcj-sample.7z");
        var dest = Path.Combine(Path.GetTempPath(), "cm-7z-" + Guid.NewGuid().ToString("N"));
        try
        {
            _extractor.Extract(archive, dest);
            var extracted = Path.Combine(dest, "sample.exe");
            Assert.True(File.Exists(extracted), "sample.exe should have been extracted");
            Assert.StartsWith("MZ", File.ReadAllText(extracted));
        }
        finally { if (Directory.Exists(dest)) Directory.Delete(dest, true); }
    }

    [Fact]
    public void Extract_UnsupportedExtension_Throws()
    {
        Assert.Throws<NotSupportedException>(
            () => _extractor.Extract("whatever.rar", "dest"));
    }
}
```

Ensure the fixture copies to output. In `ControlMenu.Tests.csproj` add (if not already globbed):
```xml
<ItemGroup>
  <None Include="Fixtures/**/*" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests" --filter "FullyQualifiedName~ArchiveExtractorTests"`
Expected: FAIL - `ArchiveExtractor` / `IArchiveExtractor` do not exist (compile error).

- [ ] **Step 5: Implement**

`src/ControlMenu/Services/Archive/IArchiveExtractor.cs`:
```csharp
namespace ControlMenu.Services.Archive;

public interface IArchiveExtractor
{
    /// <summary>Extract a .zip, .tar.gz, or .7z archive into <paramref name="destDir"/>.</summary>
    void Extract(string archivePath, string destDir);
}
```

`src/ControlMenu/Services/Archive/ArchiveExtractor.cs`:
```csharp
using System.Formats.Tar;
using System.IO.Compression;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;

namespace ControlMenu.Services.Archive;

public sealed class ArchiveExtractor : IArchiveExtractor
{
    public void Extract(string archivePath, string destDir)
    {
        Directory.CreateDirectory(destDir);
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, destDir, overwriteFiles: true);
        }
        else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            using var fs = File.OpenRead(archivePath);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gz, destDir, overwriteFiles: true);
        }
        else if (archivePath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = SevenZipArchive.Open(archivePath);
            var opts = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };
            foreach (var entry in archive.Entries)
                if (!entry.IsDirectory)
                    entry.WriteToDirectory(destDir, opts);
        }
        else
        {
            throw new NotSupportedException($"Unsupported archive type: {archivePath}");
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests" --filter "FullyQualifiedName~ArchiveExtractorTests"`
Expected: PASS.

**GO/NO-GO:** If `Extract_SevenZipWithBcjFilter_ExtractsEntry` fails with a SharpCompress codec error, STOP. SharpCompress cannot decode BCJ; switch to the spec's documented fallback (vendor `7za.exe`, resolve via `IDependencyPathResolver`, shell out). Record the decision and re-plan Task 1 before proceeding.

- [ ] **Step 7: Commit**

```bash
git -C "C:/Users/jscha/source/repos/control-menu" add src/ControlMenu/ControlMenu.csproj src/ControlMenu/Services/Archive tests/ControlMenu.Tests/Services/Archive tests/ControlMenu.Tests/Fixtures/bcj-sample.7z tests/ControlMenu.Tests/ControlMenu.Tests.csproj
git -C "C:/Users/jscha/source/repos/control-menu" commit -m "feat(deps): managed .7z extraction via SharpCompress"
```

---

### Task 2: Integrity data model

**Files:**
- Modify: `src/ControlMenu/Modules/ModuleDependency.cs`
- Create: `src/ControlMenu/Services/Verification/VerificationResult.cs`
- Create: `src/ControlMenu/Services/Verification/ChecksumSource.cs`
- Test: `tests/ControlMenu.Tests/Modules/ModuleDependencyIntegrityTests.cs`

**Interfaces:**
- Produces: `VerificationTier { PinnedHash, UpstreamChecksum, Authenticode, Unverified }`; `VerificationResult(bool Verified, VerificationTier Tier, string? Algorithm, string Detail)`; `ChecksumSource(string UrlOrTemplate, ChecksumFormat Format, ChecksumAlgorithm Algorithm)`; `ChecksumFormat { SqliteDownloadPage, InTotoJsonl, Sha256SumsFile }`; `ChecksumAlgorithm { Sha256, Sha3_256 }`. New `ModuleDependency` members: `IReadOnlyDictionary<string,string> KnownHashes`, `ChecksumSource? Checksum`, `string? ExpectedSigner`, `string[] AllowedHosts`.

- [ ] **Step 1: Write the failing test**

`tests/ControlMenu.Tests/Modules/ModuleDependencyIntegrityTests.cs`:
```csharp
using ControlMenu.Modules;
using ControlMenu.Services.Verification;

namespace ControlMenu.Tests.Modules;

public class ModuleDependencyIntegrityTests
{
    [Fact]
    public void IntegrityFields_DefaultToEmpty()
    {
        var dep = new ModuleDependency
        {
            Name = "x", ExecutableName = "x",
            VersionCommand = "x --version", VersionPattern = "(.+)"
        };
        Assert.Empty(dep.KnownHashes);
        Assert.Empty(dep.AllowedHosts);
        Assert.Null(dep.Checksum);
        Assert.Null(dep.ExpectedSigner);
    }

    [Fact]
    public void Checksum_RecordHoldsFormatAndAlgorithm()
    {
        var c = new ChecksumSource("https://x/page", ChecksumFormat.SqliteDownloadPage, ChecksumAlgorithm.Sha3_256);
        Assert.Equal(ChecksumFormat.SqliteDownloadPage, c.Format);
        Assert.Equal(ChecksumAlgorithm.Sha3_256, c.Algorithm);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests" --filter "FullyQualifiedName~ModuleDependencyIntegrityTests"`
Expected: FAIL (members/types missing).

- [ ] **Step 3: Implement**

`src/ControlMenu/Services/Verification/VerificationResult.cs`:
```csharp
namespace ControlMenu.Services.Verification;

public enum VerificationTier { PinnedHash, UpstreamChecksum, Authenticode, Unverified }

public record VerificationResult(bool Verified, VerificationTier Tier, string? Algorithm, string Detail);
```

`src/ControlMenu/Services/Verification/ChecksumSource.cs`:
```csharp
namespace ControlMenu.Services.Verification;

public enum ChecksumFormat { SqliteDownloadPage, InTotoJsonl, Sha256SumsFile }
public enum ChecksumAlgorithm { Sha256, Sha3_256 }

public record ChecksumSource(string UrlOrTemplate, ChecksumFormat Format, ChecksumAlgorithm Algorithm);
```

Add to `ModuleDependency` (in `src/ControlMenu/Modules/ModuleDependency.cs`, before the closing brace; add `using ControlMenu.Services.Verification;` at top):
```csharp
    /// <summary>Vetted SHA-256 hashes keyed by version string (T1, the backbone).</summary>
    public IReadOnlyDictionary<string, string> KnownHashes { get; init; } =
        new Dictionary<string, string>();
    /// <summary>Upstream-published checksum source (T2), or null.</summary>
    public ChecksumSource? Checksum { get; init; }
    /// <summary>Expected Authenticode subject, e.g. "CN=Google LLC" (T3), or null.</summary>
    public string? ExpectedSigner { get; init; }
    /// <summary>Permitted final download hosts (transport hard gate). Empty = unconstrained (avoid).</summary>
    public string[] AllowedHosts { get; init; } = [];
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests" --filter "FullyQualifiedName~ModuleDependencyIntegrityTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C "C:/Users/jscha/source/repos/control-menu" add src/ControlMenu/Modules/ModuleDependency.cs src/ControlMenu/Services/Verification tests/ControlMenu.Tests/Modules/ModuleDependencyIntegrityTests.cs
git -C "C:/Users/jscha/source/repos/control-menu" commit -m "feat(deps): integrity data model on ModuleDependency"
```

---

### Task 3: ArtifactVerifier - T1 pinned hash + Unverified fallthrough

**Files:**
- Create: `src/ControlMenu/Services/Verification/IArtifactVerifier.cs`
- Create: `src/ControlMenu/Services/Verification/ArtifactVerifier.cs`
- Test: `tests/ControlMenu.Tests/Services/Verification/ArtifactVerifierPinnedTests.cs`

**Interfaces:**
- Consumes: `ModuleDependency`, `VerificationResult` (Task 2).
- Produces: `IArtifactVerifier.VerifyAsync(string filePath, ModuleDependency dep, string version, CancellationToken ct) -> Task<VerificationResult>`. T1 only for now; everything else returns `Unverified`.

- [ ] **Step 1: Write the failing test**

`tests/ControlMenu.Tests/Services/Verification/ArtifactVerifierPinnedTests.cs`:
```csharp
using System.Security.Cryptography;
using ControlMenu.Modules;
using ControlMenu.Services.Verification;

namespace ControlMenu.Tests.Services.Verification;

public class ArtifactVerifierPinnedTests
{
    private static string WriteTemp(byte[] bytes)
    {
        var p = Path.Combine(Path.GetTempPath(), "cm-art-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(p, bytes);
        return p;
    }
    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private readonly ArtifactVerifier _verifier =
        new(new NullAuthenticodeInspector(), new HttpClient());

    [Fact]
    public async Task PinnedHash_ExactMatch_Verified()
    {
        var bytes = "payload-v1"u8.ToArray();
        var file = WriteTemp(bytes);
        var dep = DepWithHashes(("1.0", Sha256Hex(bytes)));
        var r = await _verifier.VerifyAsync(file, dep, "1.0", default);
        Assert.True(r.Verified);
        Assert.Equal(VerificationTier.PinnedHash, r.Tier);
    }

    [Fact]
    public async Task PinnedHash_Mismatch_HardFail()
    {
        var file = WriteTemp("tampered"u8.ToArray());
        var dep = DepWithHashes(("1.0", Sha256Hex("original"u8.ToArray())));
        var r = await _verifier.VerifyAsync(file, dep, "1.0", default);
        Assert.False(r.Verified);
        Assert.Equal(VerificationTier.PinnedHash, r.Tier); // mismatch is attributed to the tier that ran
    }

    [Fact]
    public async Task NoTierAvailable_Unverified()
    {
        var file = WriteTemp("anything"u8.ToArray());
        var dep = DepWithHashes(); // no known hash for "9.9", no checksum, no signer
        var r = await _verifier.VerifyAsync(file, dep, "9.9", default);
        Assert.False(r.Verified);
        Assert.Equal(VerificationTier.Unverified, r.Tier);
    }

    private static ModuleDependency DepWithHashes(params (string v, string h)[] hashes) =>
        new()
        {
            Name = "t", ExecutableName = "t", VersionCommand = "t", VersionPattern = "(.+)",
            KnownHashes = hashes.ToDictionary(x => x.v, x => x.h)
        };
}

// Minimal inspector that reports "not signed" so T3 always falls through in these tests.
file sealed class NullAuthenticodeInspector : IAuthenticodeInspector
{
    public AuthenticodeInfo Inspect(string filePath) => new(false, false, null);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests" --filter "FullyQualifiedName~ArtifactVerifierPinnedTests"`
Expected: FAIL (`ArtifactVerifier`, `IAuthenticodeInspector`, `AuthenticodeInfo` missing).

- [ ] **Step 3: Implement the inspector seam + verifier skeleton**

`src/ControlMenu/Services/Verification/IAuthenticodeInspector.cs`:
```csharp
namespace ControlMenu.Services.Verification;

public record AuthenticodeInfo(bool IsSigned, bool IsTrusted, string? SubjectCn);

public interface IAuthenticodeInspector
{
    /// <summary>Read Authenticode state for a file. Non-Windows / unsigned -> IsSigned=false.</summary>
    AuthenticodeInfo Inspect(string filePath);
}
```

`src/ControlMenu/Services/Verification/IArtifactVerifier.cs`:
```csharp
using ControlMenu.Modules;

namespace ControlMenu.Services.Verification;

public interface IArtifactVerifier
{
    Task<VerificationResult> VerifyAsync(
        string filePath, ModuleDependency dep, string version, CancellationToken ct);
}
```

`src/ControlMenu/Services/Verification/ArtifactVerifier.cs`:
```csharp
using System.Security.Cryptography;
using ControlMenu.Modules;

namespace ControlMenu.Services.Verification;

public sealed class ArtifactVerifier(IAuthenticodeInspector authenticode, HttpClient http)
    : IArtifactVerifier
{
    public async Task<VerificationResult> VerifyAsync(
        string filePath, ModuleDependency dep, string version, CancellationToken ct)
    {
        // T1 - pinned hash
        if (dep.KnownHashes.TryGetValue(version, out var expected))
        {
            var actual = await Sha256HexAsync(filePath, ct);
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
                ? new VerificationResult(true, VerificationTier.PinnedHash, "SHA-256", $"pinned {version}")
                : new VerificationResult(false, VerificationTier.PinnedHash, "SHA-256",
                    $"pinned hash mismatch (expected {expected}, got {actual})");
        }

        // T2/T3 added in Tasks 4-5. For now, fall through.
        return new VerificationResult(false, VerificationTier.Unverified, null,
            "no cryptographic tier available");
    }

    private static async Task<string> Sha256HexAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexStringLower(hash);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests" --filter "FullyQualifiedName~ArtifactVerifierPinnedTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C "C:/Users/jscha/source/repos/control-menu" add src/ControlMenu/Services/Verification tests/ControlMenu.Tests/Services/Verification/ArtifactVerifierPinnedTests.cs
git -C "C:/Users/jscha/source/repos/control-menu" commit -m "feat(deps): artifact verifier with pinned-hash tier"
```

---

### Task 4: T2 upstream checksum (sqlite SHA3-256 page + magick in-toto SHA-256)

**Files:**
- Modify: `src/ControlMenu/Services/Verification/ArtifactVerifier.cs`
- Create: `src/ControlMenu/Services/Verification/UpstreamChecksum.cs` (parsers)
- Test: `tests/ControlMenu.Tests/Services/Verification/UpstreamChecksumTests.cs`
- Fixtures: `tests/ControlMenu.Tests/Fixtures/sqlite-download-snippet.html`, `tests/ControlMenu.Tests/Fixtures/imagemagick.intoto.jsonl`

**Interfaces:**
- Consumes: `ChecksumSource`, `VerificationResult`.
- Produces: `UpstreamChecksum.ExtractExpectedHash(ChecksumFormat format, string payload, string assetFileName) -> string?` (pure; returns the expected hex digest or null).

- [ ] **Step 1: Create fixtures**

`tests/ControlMenu.Tests/Fixtures/sqlite-download-snippet.html` (a real-shaped fragment):
```html
<a href='2026/sqlite-tools-win-x64-3530000.zip'>sqlite-tools-win-x64-3530000.zip</a>
(3.94 MiB)
(SHA3-256: 7b1d2c0f9a4e6b8d3c5f1029384756abcdef0123456789abcdef0123456789ab)
```

`tests/ControlMenu.Tests/Fixtures/imagemagick.intoto.jsonl` (one statement line; digest is sha256):
```json
{"_type":"https://in-toto.io/Statement/v1","subject":[{"name":"ImageMagick-7.1.2-25-portable-Q8-x64.7z","digest":{"sha256":"ff7c559f51bad365e3662f004aaed0e18c937d110f6e01183363602c07246e40"}}],"predicateType":"https://slsa.dev/provenance/v1","predicate":{}}
```

- [ ] **Step 2: Write the failing test**

`tests/ControlMenu.Tests/Services/Verification/UpstreamChecksumTests.cs`:
```csharp
using ControlMenu.Services.Verification;

namespace ControlMenu.Tests.Services.Verification;

public class UpstreamChecksumTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void SqlitePage_ExtractsSha3ForNamedAsset()
    {
        var hash = UpstreamChecksum.ExtractExpectedHash(
            ChecksumFormat.SqliteDownloadPage,
            Fixture("sqlite-download-snippet.html"),
            "sqlite-tools-win-x64-3530000.zip");
        Assert.Equal("7b1d2c0f9a4e6b8d3c5f1029384756abcdef0123456789abcdef0123456789ab", hash);
    }

    [Fact]
    public void InToto_ExtractsSha256ForNamedAsset()
    {
        var hash = UpstreamChecksum.ExtractExpectedHash(
            ChecksumFormat.InTotoJsonl,
            Fixture("imagemagick.intoto.jsonl"),
            "ImageMagick-7.1.2-25-portable-Q8-x64.7z");
        Assert.Equal("ff7c559f51bad365e3662f004aaed0e18c937d110f6e01183363602c07246e40", hash);
    }

    [Fact]
    public void UnknownAsset_ReturnsNull()
    {
        var hash = UpstreamChecksum.ExtractExpectedHash(
            ChecksumFormat.InTotoJsonl, Fixture("imagemagick.intoto.jsonl"), "not-present.7z");
        Assert.Null(hash);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests" --filter "FullyQualifiedName~UpstreamChecksumTests"`
Expected: FAIL (`UpstreamChecksum` missing).

- [ ] **Step 4: Implement the parsers**

`src/ControlMenu/Services/Verification/UpstreamChecksum.cs`:
```csharp
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ControlMenu.Services.Verification;

public static class UpstreamChecksum
{
    public static string? ExtractExpectedHash(ChecksumFormat format, string payload, string assetFileName)
        => format switch
        {
            ChecksumFormat.SqliteDownloadPage => SqlitePage(payload, assetFileName),
            ChecksumFormat.InTotoJsonl        => InToto(payload, assetFileName),
            ChecksumFormat.Sha256SumsFile     => Sha256Sums(payload, assetFileName),
            _ => null
        };

    private static string? SqlitePage(string html, string asset)
    {
        // Find the asset reference, then the nearest "(SHA3-256: <hex>)" that follows it.
        var idx = html.IndexOf(asset, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var m = Regex.Match(html[idx..], @"SHA3-256:\s*([0-9a-fA-F]{64})");
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    private static string? InToto(string jsonl, string asset)
    {
        foreach (var line in jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("subject", out var subjects)) continue;
            foreach (var s in subjects.EnumerateArray())
            {
                if (s.TryGetProperty("name", out var n) && n.GetString() == asset
                    && s.TryGetProperty("digest", out var d)
                    && d.TryGetProperty("sha256", out var h))
                    return h.GetString()?.ToLowerInvariant();
            }
        }
        return null;
    }

    private static string? Sha256Sums(string text, string asset)
    {
        // "<hex>  <filename>" lines.
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = Regex.Match(line, @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
            if (m.Success && m.Groups[2].Value.Trim().EndsWith(asset, StringComparison.OrdinalIgnoreCase))
                return m.Groups[1].Value.ToLowerInvariant();
        }
        return null;
    }
}
```

- [ ] **Step 5: Wire T2 into the verifier**

In `ArtifactVerifier.VerifyAsync`, AFTER the T1 block and BEFORE the final Unverified return, insert:
```csharp
        // T2 - upstream checksum
        if (dep.Checksum is { } src && dep.AllowedHosts.Length > 0)
        {
            try
            {
                if (src.Algorithm == ChecksumAlgorithm.Sha3_256 && !SHA3_256.IsSupported)
                {
                    // OS lacks SHA3; cannot verify this tier -> fall through.
                }
                else
                {
                    var url = src.UrlOrTemplate.Replace("{version}", version);
                    var payload = await http.GetStringAsync(url, ct);
                    var assetName = Path.GetFileName(new Uri(url).AbsolutePath);
                    var expected = UpstreamChecksum.ExtractExpectedHash(src.Format, payload, assetName)
                                   ?? UpstreamChecksum.ExtractExpectedHash(src.Format, payload, Path.GetFileName(filePath));
                    if (expected is not null)
                    {
                        var actual = await HashHexAsync(filePath, src.Algorithm, ct);
                        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
                            ? new VerificationResult(true, VerificationTier.UpstreamChecksum, src.Algorithm.ToString(), "upstream checksum match")
                            : new VerificationResult(false, VerificationTier.UpstreamChecksum, src.Algorithm.ToString(),
                                $"upstream checksum mismatch (expected {expected}, got {actual})");
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // checksum source unreachable -> fall through (do not hard-fail on a network blip)
            }
        }
```
Add helper + `using System.Security.Cryptography;` (already present):
```csharp
    private static async Task<string> HashHexAsync(string path, ChecksumAlgorithm algo, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        byte[] hash = algo switch
        {
            ChecksumAlgorithm.Sha3_256 => await SHA3_256.HashDataAsync(fs, ct),
            _                          => await SHA256.HashDataAsync(fs, ct)
        };
        return Convert.ToHexStringLower(hash);
    }
```
Add a T2 unit test in a new `ArtifactVerifierChecksumTests` that points `Checksum` at a `file://` fixture URL (or refactor `http` behind a tiny fetch seam). Minimal approach: assert the pure `UpstreamChecksum` path (already covered) and add one verifier test using a `file://` URL to the sqlite fixture with a matching SHA-256 pinned-off case. Keep network out of tests.

- [ ] **Step 6: Run to verify all verification tests pass**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests" --filter "FullyQualifiedName~Verification"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git -C "C:/Users/jscha/source/repos/control-menu" add src/ControlMenu/Services/Verification tests/ControlMenu.Tests/Services/Verification tests/ControlMenu.Tests/Fixtures
git -C "C:/Users/jscha/source/repos/control-menu" commit -m "feat(deps): upstream-checksum tier (sqlite SHA3-256, magick in-toto)"
```

---

### Task 5: T3 Authenticode (adb, signer-pinned to Google LLC)

**Files:**
- Create: `src/ControlMenu/Services/Verification/WindowsAuthenticodeInspector.cs`
- Modify: `src/ControlMenu/Services/Verification/ArtifactVerifier.cs`
- Test: `tests/ControlMenu.Tests/Services/Verification/ArtifactVerifierAuthenticodeTests.cs`

**Interfaces:**
- Consumes: `IAuthenticodeInspector` (Task 3), `AuthenticodeInfo`.
- Produces: T3 branch in the verifier: when `dep.ExpectedSigner` set, require `IsSigned && IsTrusted && SubjectCn == ExpectedSigner`.

- [ ] **Step 1: Write the failing test (tier logic via a fake inspector)**

`tests/ControlMenu.Tests/Services/Verification/ArtifactVerifierAuthenticodeTests.cs`:
```csharp
using ControlMenu.Modules;
using ControlMenu.Services.Verification;

namespace ControlMenu.Tests.Services.Verification;

public class ArtifactVerifierAuthenticodeTests
{
    private sealed class FakeInspector(AuthenticodeInfo info) : IAuthenticodeInspector
    {
        public AuthenticodeInfo Inspect(string filePath) => info;
    }
    private static string TempFile()
    {
        var p = Path.Combine(Path.GetTempPath(), "cm-sig-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(p, "x");
        return p;
    }
    private static ModuleDependency AdbDep() => new()
    {
        Name = "adb", ExecutableName = "adb", VersionCommand = "adb", VersionPattern = "(.+)",
        ExpectedSigner = "CN=Google LLC", AllowedHosts = ["dl.google.com"]
    };

    [Fact]
    public async Task ValidGoogleSignature_Verified()
    {
        var v = new ArtifactVerifier(new FakeInspector(new(true, true, "CN=Google LLC")), new HttpClient());
        var r = await v.VerifyAsync(TempFile(), AdbDep(), "37.0.0", default);
        Assert.True(r.Verified);
        Assert.Equal(VerificationTier.Authenticode, r.Tier);
    }

    [Fact]
    public async Task WrongSigner_HardFail()
    {
        var v = new ArtifactVerifier(new FakeInspector(new(true, true, "CN=Evil Corp")), new HttpClient());
        var r = await v.VerifyAsync(TempFile(), AdbDep(), "37.0.0", default);
        Assert.False(r.Verified);
        Assert.Equal(VerificationTier.Authenticode, r.Tier);
    }

    [Fact]
    public async Task Unsigned_FallsThroughToUnverified()
    {
        var v = new ArtifactVerifier(new FakeInspector(new(false, false, null)), new HttpClient());
        var r = await v.VerifyAsync(TempFile(), AdbDep(), "37.0.0", default);
        Assert.Equal(VerificationTier.Unverified, r.Tier);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests" --filter "FullyQualifiedName~ArtifactVerifierAuthenticodeTests"`
Expected: FAIL (no T3 branch yet -> returns Unverified for the signed cases).

- [ ] **Step 3: Add the T3 branch**

In `ArtifactVerifier.VerifyAsync`, AFTER T2 and BEFORE the final Unverified return:
```csharp
        // T3 - Authenticode signer pin
        if (dep.ExpectedSigner is { } expectedSigner)
        {
            var info = authenticode.Inspect(filePath);
            if (info.IsSigned)
            {
                var ok = info.IsTrusted
                    && string.Equals(info.SubjectCn, expectedSigner, StringComparison.OrdinalIgnoreCase);
                return ok
                    ? new VerificationResult(true, VerificationTier.Authenticode, "Authenticode", $"signed by {info.SubjectCn}")
                    : new VerificationResult(false, VerificationTier.Authenticode, "Authenticode",
                        $"signature check failed (trusted={info.IsTrusted}, subject={info.SubjectCn})");
            }
            // unsigned -> fall through
        }
```

- [ ] **Step 4: Implement the Windows inspector**

`src/ControlMenu/Services/Verification/WindowsAuthenticodeInspector.cs`:
```csharp
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace ControlMenu.Services.Verification;

/// <summary>
/// Reads Authenticode signer + trust via WinVerifyTrust. On non-Windows returns IsSigned=false.
/// </summary>
public sealed class WindowsAuthenticodeInspector : IAuthenticodeInspector
{
    public AuthenticodeInfo Inspect(string filePath)
    {
        if (!OperatingSystem.IsWindows()) return new AuthenticodeInfo(false, false, null);

        string? subjectCn = null;
        var signed = false;
        try
        {
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
            signed = true;
            subjectCn = cert.Subject; // full subject; compared with StartsWith/Equals on "CN=..."
        }
        catch { return new AuthenticodeInfo(false, false, null); }

        var trusted = WinVerifyTrustValid(filePath);
        // Normalise: callers compare against "CN=Google LLC", so surface the CN= component.
        var cn = ExtractCn(subjectCn);
        return new AuthenticodeInfo(signed, trusted, cn);
    }

    private static string? ExtractCn(string? subject)
    {
        if (subject is null) return null;
        foreach (var part in subject.Split(','))
        {
            var p = part.Trim();
            if (p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)) return p;
        }
        return subject;
    }

    [SupportedOSPlatform("windows")]
    private static bool WinVerifyTrustValid(string filePath)
    {
        var actionId = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath
        };
        var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = 2,            // WTD_UI_NONE
                fdwRevocationChecks = 0,   // WTD_REVOKE_NONE
                dwUnionChoice = 1,         // WTD_CHOICE_FILE
                pFile = pFile,
                dwStateAction = 0,
                dwProvFlags = 0x10         // WTD_SAFER_FLAG
            };
            int hr = WinVerifyTrust(IntPtr.Zero, ref actionId, ref data);
            return hr == 0; // 0 == trusted
        }
        finally { Marshal.FreeHGlobal(pFile); }
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid actionId, ref WINTRUST_DATA data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
}
```

- [ ] **Step 5: Run to verify the tier-logic tests pass**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests" --filter "FullyQualifiedName~ArtifactVerifierAuthenticodeTests"`
Expected: PASS. (The Windows inspector itself is validated manually against the real signed adb.exe during smoke - note in the task PR.)

- [ ] **Step 6: Commit**

```bash
git -C "C:/Users/jscha/source/repos/control-menu" add src/ControlMenu/Services/Verification tests/ControlMenu.Tests/Services/Verification/ArtifactVerifierAuthenticodeTests.cs
git -C "C:/Users/jscha/source/repos/control-menu" commit -m "feat(deps): Authenticode tier with signer pinning"
```

---

### Task 6: Transport hard gate (HTTPS + final-host allowlist)

**Files:**
- Create: `src/ControlMenu/Services/Verification/TransportGuard.cs`
- Test: `tests/ControlMenu.Tests/Services/Verification/TransportGuardTests.cs`

**Interfaces:**
- Produces: `TransportGuard.IsAllowedFinalUri(Uri finalUri, string[] allowedHosts) -> bool` (pure; HTTPS required, host must match an allowlist entry, supporting a leading `*.` wildcard).

- [ ] **Step 1: Write the failing test**

`tests/ControlMenu.Tests/Services/Verification/TransportGuardTests.cs`:
```csharp
using ControlMenu.Services.Verification;

namespace ControlMenu.Tests.Services.Verification;

public class TransportGuardTests
{
    private static readonly string[] GitHub = ["github.com", "*.githubusercontent.com"];

    [Theory]
    [InlineData("https://github.com/x/y/releases/download/v1/a.zip", true)]
    [InlineData("https://objects.githubusercontent.com/abc", true)]   // wildcard CDN
    [InlineData("http://github.com/x", false)]                         // not HTTPS
    [InlineData("https://evil.com/github.com", false)]                 // host not allowlisted
    public void IsAllowedFinalUri_EnforcesSchemeAndHost(string uri, bool expected)
        => Assert.Equal(expected, TransportGuard.IsAllowedFinalUri(new Uri(uri), GitHub));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests" --filter "FullyQualifiedName~TransportGuardTests"`
Expected: FAIL (`TransportGuard` missing).

- [ ] **Step 3: Implement**

`src/ControlMenu/Services/Verification/TransportGuard.cs`:
```csharp
namespace ControlMenu.Services.Verification;

public static class TransportGuard
{
    public static bool IsAllowedFinalUri(Uri finalUri, string[] allowedHosts)
    {
        if (finalUri.Scheme != Uri.UriSchemeHttps) return false;
        if (allowedHosts.Length == 0) return false;
        var host = finalUri.Host;
        foreach (var allowed in allowedHosts)
        {
            if (allowed.StartsWith("*.", StringComparison.Ordinal))
            {
                var suffix = allowed[1..]; // ".githubusercontent.com"
                if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests" --filter "FullyQualifiedName~TransportGuardTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C "C:/Users/jscha/source/repos/control-menu" add src/ControlMenu/Services/Verification/TransportGuard.cs tests/ControlMenu.Tests/Services/Verification/TransportGuardTests.cs
git -C "C:/Users/jscha/source/repos/control-menu" commit -m "feat(deps): transport guard (https + host allowlist)"
```

---

### Task 7: Integrate the pipeline into DownloadAndInstallAsync

**Files:**
- Modify: `src/ControlMenu/Services/UpdateResult.cs`
- Modify: `src/ControlMenu/Services/IDependencyManagerService.cs` (signature change)
- Modify: `src/ControlMenu/Services/DependencyManagerService.cs`
- Modify: `src/ControlMenu/ServiceCollectionExtensions.cs` (register verifier + extractor + inspector; redirect policy)
- Test: `tests/ControlMenu.Tests/Services/DownloadAndInstallIntegrationTests.cs`

**Interfaces:**
- Consumes: `IArtifactVerifier`, `IArchiveExtractor`, `TransportGuard`, `VerificationTier`.
- Produces: `UpdateResult` gains `UpdateOutcome Outcome`; `DownloadAndInstallAsync(Guid, AssetMatch, bool allowUnverified = false)`.

- [ ] **Step 1: Extend UpdateResult**

`src/ControlMenu/Services/UpdateResult.cs`:
```csharp
using ControlMenu.Data.Enums;

namespace ControlMenu.Services;

public enum UpdateOutcome { Installed, Failed, NeedsUnverifiedConfirmation }

public record UpdateResult(
    bool Success,
    string? NewVersion,
    string? ErrorMessage,
    StaleUrlAction? UrlAction,
    UpdateOutcome Outcome = UpdateOutcome.Installed,
    string? ConfirmTool = null,
    string? ConfirmVersion = null,
    string? ConfirmHost = null);
```
Note: existing `new UpdateResult(true, v, null, urlAction)` call sites keep compiling (new params are optional). Update the failure sites to pass `Outcome: UpdateOutcome.Failed` where appropriate (search the file).

- [ ] **Step 2: Write the failing integration test**

`tests/ControlMenu.Tests/Services/DownloadAndInstallIntegrationTests.cs` - construct a `DependencyManagerService` with fakes/in-memory EF, drive a download whose bytes do NOT match a pinned hash and assert the artifact is NOT extracted and the result is `Failed` with no install. (Model this on existing `DependencyManagerService` tests; use the in-memory `IDbContextFactory` pattern already in the suite. Inject a fake `IArtifactVerifier` returning a hard-mismatch and assert `ArchiveExtractor` is never called via a spy.)
```csharp
// Skeleton — fill EF/fakes to match existing DependencyManagerService test setup.
[Fact]
public async Task TamperedArtifact_IsNotExtracted_AndFails()
{
    // arrange: verifier returns Verified=false, Tier=PinnedHash (mismatch)
    // act: DownloadAndInstallAsync(depId, asset, allowUnverified: false)
    // assert: result.Success == false; extractor spy.Called == false
}

[Fact]
public async Task Unverified_WithoutConsent_ReturnsNeedsConfirmation()
{
    // verifier returns Tier=Unverified; allowUnverified:false
    // assert result.Outcome == UpdateOutcome.NeedsUnverifiedConfirmation and ConfirmTool/Version set
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests" --filter "FullyQualifiedName~DownloadAndInstallIntegrationTests"`
Expected: FAIL (signature/behaviour not present).

- [ ] **Step 4: Implement the gate**

In `DependencyManagerService`: add `IArtifactVerifier _verifier` and `IArchiveExtractor _extractor` constructor params (assign to fields). Change the interface + method signature to `DownloadAndInstallAsync(Guid dependencyId, AssetMatch asset, bool allowUnverified = false)`. Replace the inline extract block (`:300-314`) with `_extractor.Extract(tempFile, extractDir);`. Insert BETWEEN the temp-file write (`:298`) and extract:
```csharp
            // Transport: validate the FINAL host (after any redirect) against the allowlist.
            var finalUri = response.RequestMessage?.RequestUri ?? new Uri(asset.DownloadUrl);
            if (moduleDep.AllowedHosts.Length > 0 && !TransportGuard.IsAllowedFinalUri(finalUri, moduleDep.AllowedHosts))
            {
                return new UpdateResult(false, null,
                    $"Blocked: download host not allowed ({finalUri.Host})", StaleUrlAction.Invalid,
                    UpdateOutcome.Failed);
            }

            // Integrity: verify BEFORE extract/run.
            var verification = await _verifier.VerifyAsync(tempFile, moduleDep, asset.ResolvedVersionOrTag(entity, asset), default);
            if (verification.Tier != VerificationTier.Unverified && !verification.Verified)
            {
                return new UpdateResult(false, null,
                    $"Integrity check failed: {verification.Detail}", urlAction, UpdateOutcome.Failed);
            }
            if (verification.Tier == VerificationTier.Unverified && !allowUnverified)
            {
                return new UpdateResult(false, null,
                    "Update could not be cryptographically verified", urlAction,
                    UpdateOutcome.NeedsUnverifiedConfirmation,
                    ConfirmTool: entity.Name, ConfirmVersion: /* resolved version */ null, ConfirmHost: finalUri.Host);
            }
```
Notes for the implementer:
- "Resolved version" is the version string the updater is installing. Compute it the same way the existing flow derives `newVersion`, but you need it BEFORE the run-to-verify. Simplest: pass `asset` version if present, else `entity.LatestKnownVersion`. Add a small private helper `ResolveTargetVersion(entity, asset)` and use it for both the verifier `version` arg and `ConfirmVersion`. Do NOT call the downloaded binary to get the version before verifying it.
- Also gate the redirect-persist line (`:281`): only persist `entity.DownloadUrl = finalUrl` when `TransportGuard.IsAllowedFinalUri(finalUri, moduleDep.AllowedHosts)` (or AllowedHosts empty).

- [ ] **Step 5: Register services + redirect policy**

In `ServiceCollectionExtensions.cs` near line 143-147:
```csharp
        services.AddSingleton<IArchiveExtractor, ArchiveExtractor>();
        services.AddSingleton<IAuthenticodeInspector, WindowsAuthenticodeInspector>();
        services.AddScoped<IArtifactVerifier>(sp => new ArtifactVerifier(
            sp.GetRequiredService<IAuthenticodeInspector>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("dependency-updates")));
```
Keep `AddHttpClient("dependency-updates", ...)`. Redirects stay enabled (GitHub assets need them); the final-host check in Step 4 is what constrains them. (Do not set `AllowAutoRedirect = false`.)

- [ ] **Step 6: Run the full suite**

Run: `dotnet test "C:/Users/jscha/source/repos/control-menu/tests/ControlMenu.Tests"`
Expected: PASS (all). Fix any call sites broken by the signature/`UpdateResult` change.

- [ ] **Step 7: Commit**

```bash
git -C "C:/Users/jscha/source/repos/control-menu" add -A
git -C "C:/Users/jscha/source/repos/control-menu" commit -m "feat(deps): verify downloads before extract/run; allowUnverified gate"
```

---

### Task 8: Per-dependency integrity configuration

**Files:**
- Modify: `Imaging/ImagingModule.cs`, `AndroidDevices/AndroidDevicesModule.cs`, `Cameras/CamerasModule.cs`, `Jellyfin/JellyfinModule.cs`
- Test: `tests/ControlMenu.Tests/Modules/DependencyIntegrityConfigTests.cs`

**Interfaces:** Consumes the Task 2 fields. No new types.

- [ ] **Step 1: Write the failing test**
```csharp
// tests/ControlMenu.Tests/Modules/DependencyIntegrityConfigTests.cs
using ControlMenu.Modules;
using ControlMenu.Modules.AndroidDevices;
using ControlMenu.Modules.Cameras;
using ControlMenu.Modules.Imaging;
using ControlMenu.Modules.Jellyfin;
using ControlMenu.Services.Verification;

namespace ControlMenu.Tests.Modules;

public class DependencyIntegrityConfigTests
{
    private static ModuleDependency Dep(IToolModule m, string name) =>
        m.Dependencies.Single(d => d.Name == name);

    [Fact]
    public void Adb_UsesAuthenticodeSignerPin()
    {
        var adb = Dep(new AndroidDevicesModule(), "adb");
        Assert.Equal("CN=Google LLC", adb.ExpectedSigner);
        Assert.Contains("dl.google.com", adb.AllowedHosts);
        Assert.Null(adb.Checksum); // SHA-1 rejected
    }

    [Fact]
    public void Sqlite_UsesSha3PageChecksum()
    {
        var s = Dep(new JellyfinModule(), "sqlite3");
        Assert.Equal(ChecksumFormat.SqliteDownloadPage, s.Checksum!.Format);
        Assert.Equal(ChecksumAlgorithm.Sha3_256, s.Checksum!.Algorithm);
    }

    [Fact]
    public void Go2rtcAndVtracer_HaveHostsButNoCryptoSource()
    {
        var go2rtc = Dep(new CamerasModule(), "go2rtc");
        var vtracer = Dep(new ImagingModule(), "vtracer");
        foreach (var d in new[] { go2rtc, vtracer })
        {
            Assert.NotEmpty(d.AllowedHosts);
            Assert.Null(d.Checksum);
            Assert.Null(d.ExpectedSigner); // verified unsigned -> Tier 4
        }
    }
}
```
(Adjust namespaces/ctors to the real module classes - check each module file's namespace and whether it has a parameterless ctor; some take constructor args. If a module needs services to construct, instead read its `Dependencies` via the DI-built module or expose a static dependency list. Pick whichever matches the existing module shape.)

- [ ] **Step 2: Run to verify it fails.** `--filter "FullyQualifiedName~DependencyIntegrityConfigTests"` -> FAIL.

- [ ] **Step 3: Populate config.** Add to each `new ModuleDependency { ... }` initializer the relevant fields. Examples:

adb (`AndroidDevicesModule.cs`):
```csharp
            AllowedHosts = ["dl.google.com"],
            ExpectedSigner = "CN=Google LLC",
            KnownHashes = new Dictionary<string, string>
            {
                // populate with the current pinned platform-tools SHA-256 via scripts/update-dependency-hashes.ps1
            },
```
sqlite3 (`JellyfinModule.cs`):
```csharp
            AllowedHosts = ["sqlite.org"],
            Checksum = new ControlMenu.Services.Verification.ChecksumSource(
                "https://www.sqlite.org/download.html",
                ControlMenu.Services.Verification.ChecksumFormat.SqliteDownloadPage,
                ControlMenu.Services.Verification.ChecksumAlgorithm.Sha3_256),
```
magick (`ImagingModule.cs`):
```csharp
            AllowedHosts = ["github.com", "*.githubusercontent.com"],
            Checksum = new ControlMenu.Services.Verification.ChecksumSource(
                "https://github.com/ImageMagick/ImageMagick/releases/download/{version}/ImageMagick-{version}.intoto.jsonl",
                ControlMenu.Services.Verification.ChecksumFormat.InTotoJsonl,
                ControlMenu.Services.Verification.ChecksumAlgorithm.Sha256),
```
go2rtc (`CamerasModule.cs`) and vtracer (`ImagingModule.cs`):
```csharp
            AllowedHosts = ["github.com", "*.githubusercontent.com"],
```
potrace (`ImagingModule.cs`):
```csharp
            AllowedHosts = ["potrace.sourceforge.net", "*.dl.sourceforge.net"],
            KnownHashes = new Dictionary<string, string> { ["1.16"] = "<sha256 of potrace-1.16.win64.zip>" },
```
(Fill the potrace 1.16 hash and seed adb/sqlite/magick/go2rtc/vtracer current-version hashes from Task 10's script output.)

- [ ] **Step 4: Run to verify it passes.** `--filter "FullyQualifiedName~DependencyIntegrityConfigTests"` -> PASS.

- [ ] **Step 5: Commit**
```bash
git -C "C:/Users/jscha/source/repos/control-menu" add src/ControlMenu/Modules
git -C "C:/Users/jscha/source/repos/control-menu" commit -m "feat(deps): per-dependency integrity config (tiers + hosts)"
```

---

### Task 9: Tier-4 confirmation dialog (UI round-trip)

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Settings/DependencyManagement.razor` (+ its `@code`)
- Manual/bUnit test as available.

**Interfaces:** Consumes `UpdateResult.Outcome == NeedsUnverifiedConfirmation` and the `ConfirmTool/Version/Host` fields.

- [ ] **Step 1: Handle the outcome in `ConfirmDownload` / `UpdateAll`.** After `var result = await DepManager.DownloadAndInstallAsync(_updateTargetId, _resolvedAsset!);` (line ~256) and in the `UpdateAll` loop (line ~300), branch:
```csharp
        if (result.Outcome == UpdateOutcome.NeedsUnverifiedConfirmation)
        {
            _unverified = result;            // new field: UpdateResult? _unverified
            StateHasChanged();
            return;                          // wait for the user; UpdateAll: skip this dep, continue
        }
```

- [ ] **Step 2: Add the dialog markup** (shown when `_unverified is not null`), copy faithful to the spec:
```razor
@if (_unverified is not null)
{
    <div class="modal-backdrop-custom">
      <div class="modal-card">
        <h3>@_unverified.ConfirmTool @_unverified.ConfirmVersion could not be cryptographically verified</h3>
        <p>
          The maintainer of @_unverified.ConfirmTool does not publish checksums or
          signatures for their releases, so this update cannot be cryptographically
          confirmed - only that it arrived over HTTPS from the expected source
          (@_unverified.ConfirmHost).
        </p>
        <p>
          Control Menu uses @_unverified.ConfirmTool because it is well-built, but you
          should be mindful: installing an update we cannot verify means trusting it as
          delivered. If you have any concern, verify the download yourself before accepting.
        </p>
        <div class="modal-actions">
          <button class="btn btn-secondary" @onclick="() => _unverified = null">Cancel</button>
          <button class="btn btn-primary" @onclick="ConfirmUnverified">Install anyway</button>
        </div>
      </div>
    </div>
}
```

- [ ] **Step 3: Implement `ConfirmUnverified`** - re-invoke with consent:
```csharp
    private async Task ConfirmUnverified()
    {
        var target = _unverified!;
        _unverified = null;
        _updatingId = /* the dep id captured when the update started */;
        StateHasChanged();
        var asset = await DepManager.ResolveDownloadAssetAsync(_updateTargetId);
        var result = await DepManager.DownloadAndInstallAsync(_updateTargetId, asset!, allowUnverified: true);
        // existing post-update handling (toast, refresh, _updatingId = null)
    }
```

- [ ] **Step 4: Manual verification.** Run the app, point a dep (e.g. go2rtc) at a version with no pinned hash, confirm the dialog appears with the required copy and that "Install anyway" proceeds while "Cancel" aborts. Document the manual check in the PR.

- [ ] **Step 5: Commit**
```bash
git -C "C:/Users/jscha/source/repos/control-menu" add src/ControlMenu/Components/Pages/Settings/DependencyManagement.razor
git -C "C:/Users/jscha/source/repos/control-menu" commit -m "feat(deps): Tier-4 unverified-update confirmation dialog"
```

---

### Task 10: Maintenance script - pin current hashes

**Files:**
- Create: `scripts/update-dependency-hashes.ps1`

- [ ] **Step 1: Write the script** (ASCII-only). It downloads each GitHub/DirectUrl dep's current upstream-latest, computes SHA-256, and prints a `KnownHashes` snippet per dep for pasting into the module files (v1: print; a later iteration can rewrite the files / open a PR).
```powershell
#requires -Version 7
# Prints current upstream SHA-256 for each managed dependency so a maintainer can
# refresh ModuleDependency.KnownHashes. ASCII-only. No external binaries.
$ErrorActionPreference = 'Stop'
$targets = @(
  @{ Name='go2rtc';  Url='https://github.com/AlexxIT/go2rtc/releases/latest/download/go2rtc_win64.zip' }
  @{ Name='vtracer'; Url='https://github.com/visioncortex/vtracer/releases/latest/download/vtracer-x86_64-pc-windows-msvc.zip' }
  @{ Name='potrace'; Url='https://potrace.sourceforge.net/download/1.16/potrace-1.16.win64.zip' }
  @{ Name='adb';     Url='https://dl.google.com/android/repository/platform-tools-latest-windows.zip' }
)
foreach ($t in $targets) {
  $tmp = Join-Path ([IO.Path]::GetTempPath()) ("cm-hash-" + [Guid]::NewGuid())
  try {
    Invoke-WebRequest -Uri $t.Url -OutFile $tmp -UseBasicParsing
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $tmp).Hash.ToLower()
    Write-Host ("{0,-9} {1}" -f $t.Name, $hash)
  } finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
}
```
- [ ] **Step 2: Run it** and paste the resulting hashes into the Task 8 `KnownHashes` dictionaries (commit those in a follow-up to Task 8 if not already filled).
Run: `pwsh "C:/Users/jscha/source/repos/control-menu/scripts/update-dependency-hashes.ps1"`
Expected: one `name  <64-hex>` line per dep.
- [ ] **Step 3: Commit**
```bash
git -C "C:/Users/jscha/source/repos/control-menu" add scripts/update-dependency-hashes.ps1
git -C "C:/Users/jscha/source/repos/control-menu" commit -m "chore(deps): maintainer hash-refresh script"
```

---

## Self-Review

- **Spec coverage:** Transport gate (T6/T7), T1 (T3), T2 sqlite+magick (T4), T3 adb Authenticode (T5), Tier-4 dialog (T9), `.7z` extraction (T1), per-dep config (T8), maintenance (T10), redirect-persist fix (T7 Step 4). All spec sections map to a task.
- **Open items the implementer must resolve against real code:** exact module ctor shapes (T8 Step 1 note), the in-memory EF setup mirrored from existing `DependencyManagerService` tests (T7 Step 2), and the precise `_updatingId`/`_updateTargetId` capture in the UI (T9). These are flagged inline, not hidden.
- **Type consistency:** `VerificationResult`/`VerificationTier`, `ChecksumSource`/`ChecksumFormat`/`ChecksumAlgorithm`, `AuthenticodeInfo`, `UpdateOutcome` are defined once (T2/T3/T5/T7) and consumed with the same names everywhere.
- **Order:** T1-T6 are independent units; T7 integrates them; T8-T10 configure/expose. Each task is independently testable and committable.
