# Imaging Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the SkiaSharp-based IconConversionService with a new Imaging Tools module backed by the `magick.exe` CLI as a `ModuleDependency`, exposing five tools (Icon Converter, Format Converter, Image Resize, SVG Rasterize, Magic Wand background remove) in a new top-level sidebar section.

**Architecture:** ImageMagick portable CLI bundled as a binary dep (full dep-manager parity with adb/scrcpy), invoked via `ICommandExecutor.ExecuteResolvedAsync`. Single `IImageService` with granular methods (`ConvertToIcoAsync`, `ConvertFormatAsync`, `ResizeAsync`, `RasterizeSvgAsync`, `RemoveBackgroundAsync`, `GetInfoAsync`). Magic Wand uses an in-process SkiaSharp flood-fill for live tolerance preview, then re-renders authoritatively via magick.exe on Apply before Save. SVG rasterization uses Svg.Skia (~1 MB managed NuGet) for Skia → bitmap, then magick for encoding.

**Tech Stack:** ImageMagick 7.x portable Q8 x64 (binary dep), Svg.Skia (managed NuGet), SkiaSharp (already in project), Blazor Server, xUnit + bUnit for tests.

**Spec:** `docs/superpowers/specs/2026-05-15-imaging-tools-magick-design.md`

---

## Prerequisites

- `feature/velopack-phase-1-hotfix` MUST have merged to master before starting.
- Required architectural pieces on master: `IDataPathResolver` (`ControlMenu.Common.Paths`), `SeedHydrator` (`ControlMenu.Common.Seeding`), `scripts/dependencies/_Fetcher.ps1`, `scripts/stage-seed.ps1`, `ControlMenu.Common` shared library.

## Branch

All work on `feature/imaging-tools-magick` branched off the post-Phase-1 `master`.

---

## Phase 0 — Branch + prerequisite verification

### Task 0.1: Verify prerequisites and create branch

**Files:**
- None (read-only verification)

- [ ] **Step 1: Verify Phase 1 is on master**

Run:
```
git -C C:/Users/jscha/source/repos/control-menu checkout master
git -C C:/Users/jscha/source/repos/control-menu pull
git -C C:/Users/jscha/source/repos/control-menu log --oneline -1
```

Expected: HEAD is the hot-fix merge commit (NOT v1.0.1). `src/ControlMenu.Common/Paths/IDataPathResolver.cs` exists. `src/ControlMenu.Common/Seeding/SeedHydrator.cs` exists. `scripts/dependencies/_Fetcher.ps1` exists.

If not present: STOP. Phase 1 hasn't landed yet. Do not proceed.

- [ ] **Step 2: Create feature branch**

Run:
```
git -C C:/Users/jscha/source/repos/control-menu checkout -b feature/imaging-tools-magick
```

Expected: Switched to a new branch 'feature/imaging-tools-magick'.

- [ ] **Step 3: Verify clean working tree**

Run:
```
git -C C:/Users/jscha/source/repos/control-menu status --short
```

Expected: empty output (clean tree).

---

## Phase A — Foundation

Goal: Get `magick.exe` as a `ModuleDependency` visible in `Settings → Dependencies` with version detection working, before writing any user-facing tooling. No user-visible changes.

### Task A.1: Add Svg.Skia NuGet reference

**Files:**
- Modify: `src/ControlMenu/ControlMenu.csproj`

- [ ] **Step 1: Add the PackageReference**

In `src/ControlMenu/ControlMenu.csproj`, add inside the existing `<ItemGroup>` that contains `<PackageReference Include="SkiaSharp" ...>`:

```xml
<PackageReference Include="Svg.Skia" Version="2.0.0.5" />
```

(Pin to whatever the current stable is at implementation time; the precise version is updated in CHANGELOG.)

- [ ] **Step 2: Restore + build**

Run:
```
dotnet restore src/ControlMenu/ControlMenu.csproj
dotnet build src/ControlMenu/ControlMenu.csproj -c Release --no-restore
```

Expected: build succeeds, no new errors.

- [ ] **Step 3: Commit**

```
git add src/ControlMenu/ControlMenu.csproj
git commit -m "feat(imaging): add Svg.Skia NuGet reference"
```

### Task A.2: Create magick-policy.xml resource

**Files:**
- Create: `src/ControlMenu/Modules/Imaging/Resources/magick-policy.xml`

- [ ] **Step 1: Create the directory and file**

Create `src/ControlMenu/Modules/Imaging/Resources/magick-policy.xml`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE policymap [
  <!ELEMENT policymap (policy)*>
  <!ELEMENT policy EMPTY>
  <!ATTLIST policy domain (delegate|coder|filter|path|resource|cache) #IMPLIED>
  <!ATTLIST policy name CDATA #IMPLIED>
  <!ATTLIST policy rights CDATA #IMPLIED>
  <!ATTLIST policy pattern CDATA #IMPLIED>
  <!ATTLIST policy value CDATA #IMPLIED>
]>
<policymap>
  <!-- Control Menu Imaging Tools policy override.
       Deny all formats by default, then explicitly allow the v1 allowlist.
       Read-only allowed for SVG (our pipeline reads via Svg.Skia, not magick MSVG). -->
  <policy domain="coder" rights="none" pattern="*" />
  <policy domain="coder" rights="read|write" pattern="{PNG,JPG,JPEG,WEBP,AVIF,TIFF,HEIC,BMP,GIF,ICO}" />
  <policy domain="coder" rights="read" pattern="SVG" />
  <!-- Deny known-CVE-historical coder formats explicitly even if the wildcard above
       would have done it (defense in depth). -->
  <policy domain="coder" rights="none" pattern="{MVG,MSL,XBM,EPHEMERAL,LABEL}" />
  <!-- Pin resource caps; these can also be overridden per-invocation via -limit -->
  <policy domain="resource" name="memory" value="512MiB" />
  <policy domain="resource" name="map" value="1GiB" />
  <policy domain="resource" name="area" value="256MP" />
</policymap>
```

- [ ] **Step 2: Mark as content in csproj**

In `src/ControlMenu/ControlMenu.csproj`, add inside an existing `<ItemGroup>` (or create one):

```xml
<Content Include="Modules\Imaging\Resources\magick-policy.xml">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```

This ensures the policy file is published into the build output so `SeedHydrator` can find it.

- [ ] **Step 3: Build and verify policy file lands in output**

Run:
```
dotnet build src/ControlMenu/ControlMenu.csproj -c Release
```

Then verify (PowerShell):
```
Test-Path src/ControlMenu/bin/Release/net10.0/Modules/Imaging/Resources/magick-policy.xml
```

Expected: `True`.

- [ ] **Step 4: Commit**

```
git add src/ControlMenu/Modules/Imaging/Resources/magick-policy.xml src/ControlMenu/ControlMenu.csproj
git commit -m "feat(imaging): add magick policy.xml override with v1 format allowlist"
```

### Task A.3: Create fetch-magick.ps1

**Files:**
- Create: `scripts/dependencies/fetch-magick.ps1`

- [ ] **Step 1: Author the fetcher script**

Create `scripts/dependencies/fetch-magick.ps1`:

```powershell
# Fetches ImageMagick portable Q8 x64 for Windows, verifies SHA-256, stages
# into publish/seed/dependencies/magick/.
#
# Run standalone: pwsh scripts/dependencies/fetch-magick.ps1
# Wired into:     scripts/stage-seed.ps1

. "$PSScriptRoot\_Fetcher.ps1"

# ---- Pinned constants (bump together) --------------------------------------
$Version    = '7.1.1-39'
$Url        = "https://imagemagick.org/archive/binaries/ImageMagick-$Version-portable-Q8-x64.zip"
$Sha256     = '<FILL-IN-AT-IMPLEMENTATION-TIME>'
# ----------------------------------------------------------------------------

Write-Host "[fetch-magick] ImageMagick portable Q8 x64 v$Version"
$cache = Get-CmCacheDir -Name 'magick' -Version $Version
$zip = Join-Path $cache 'magick.zip'
$extract = Join-Path $cache 'extract'

Invoke-CmDownload -Url $Url -DestFile $zip -ExpectedSha256 $Sha256
if (-not (Test-Path (Join-Path $extract "ImageMagick-$Version-portable-Q8-x64\magick.exe"))) {
    Expand-CmZip -Archive $zip -DestDir $extract
}

# The ImageMagick zip extracts to a top-level ImageMagick-VERSION-portable-Q8-x64 dir;
# flatten by staging from that subfolder so the binary lives at <magick>/magick.exe.
Copy-CmStage -From (Join-Path $extract "ImageMagick-$Version-portable-Q8-x64") -LeafName 'magick'
```

**Important:** the `$Sha256` placeholder MUST be replaced with the actual SHA-256 of the pinned ZIP at implementation time. Compute via `Get-FileHash -Algorithm SHA256 <path-to-downloaded-zip>` after a manual one-time download.

- [ ] **Step 2: Verify script syntax**

Run:
```
pwsh -NoProfile -Command "Get-Content scripts/dependencies/fetch-magick.ps1 | Out-Null"
```

Expected: no syntax errors.

- [ ] **Step 3: Run with placeholder SHA — verify expected failure**

Run:
```
pwsh -NoProfile scripts/dependencies/fetch-magick.ps1
```

Expected: download succeeds, but SHA-256 verification fails with "expected `<FILL-IN-AT-IMPLEMENTATION-TIME>`, got `<actual-hash>`". **Copy the actual hash from this error message into the script's `$Sha256` constant.**

- [ ] **Step 4: Re-run with real SHA**

Run:
```
pwsh -NoProfile scripts/dependencies/fetch-magick.ps1
```

Expected: download cached, SHA verifies, zip extracts, contents staged into `publish/seed/dependencies/magick/`. Verify `publish/seed/dependencies/magick/magick.exe` exists.

- [ ] **Step 5: Commit**

```
git add scripts/dependencies/fetch-magick.ps1
git commit -m "feat(imaging): fetch-magick.ps1 (ImageMagick portable Q8 x64)"
```

### Task A.4: Wire magick into stage-seed.ps1

**Files:**
- Modify: `scripts/stage-seed.ps1`

- [ ] **Step 1: Read current stage-seed.ps1**

Read `scripts/stage-seed.ps1`. Identify the section that aggregates fetches (typically a sequence of `& "$PSScriptRoot\dependencies\fetch-<name>.ps1"` calls).

- [ ] **Step 2: Add fetch-magick invocation**

Append after the last fetch call:

```powershell
& "$PSScriptRoot\dependencies\fetch-magick.ps1"
if ($LASTEXITCODE -ne 0) { throw "fetch-magick.ps1 failed" }
```

- [ ] **Step 3: Add policy.xml staging**

After the fetch-magick call but before the function returns, add:

```powershell
# Copy the custom policy.xml override into the staged magick dir so it's
# alongside magick.exe when SeedHydrator first-launch-copies into <dataRoot>.
$policySrc = Join-Path $PSScriptRoot '..\src\ControlMenu\Modules\Imaging\Resources\magick-policy.xml'
$policyDst = Join-Path $PSScriptRoot '..\publish\seed\dependencies\magick\policy.xml'
Copy-Item $policySrc $policyDst -Force
Write-Host "[stage-seed] staged custom magick policy.xml"
```

- [ ] **Step 4: Run stage-seed and verify**

Run:
```
pwsh -NoProfile scripts/stage-seed.ps1
```

Expected: success. Verify `publish/seed/dependencies/magick/magick.exe` AND `publish/seed/dependencies/magick/policy.xml` both exist.

- [ ] **Step 5: Commit**

```
git add scripts/stage-seed.ps1
git commit -m "feat(imaging): wire magick into stage-seed pipeline"
```

### Task A.5: Create ImagingModule.cs

**Files:**
- Create: `src/ControlMenu/Modules/Imaging/ImagingModule.cs`

- [ ] **Step 1: Author the module**

Create `src/ControlMenu/Modules/Imaging/ImagingModule.cs`:

```csharp
using ControlMenu.Data.Enums;

namespace ControlMenu.Modules.Imaging;

public class ImagingModule : IToolModule
{
    public string Id => "imaging";
    public string DisplayName => "Imaging Tools";
    public string Icon => "bi-image";
    public int SortOrder => 5;  // After Cameras (4); top-level

    private static readonly string DepsRoot = ControlMenu.Services.DepsRootHolder.Path;

    public IEnumerable<ModuleDependency> Dependencies =>
    [
        new ModuleDependency
        {
            Name = "magick",
            ExecutableName = "magick",
            VersionCommand = "magick --version",
            VersionPattern = @"ImageMagick ([\d.]+-\d+)",
            SourceType = UpdateSourceType.GitHub,
            GitHubRepo = "ImageMagick/ImageMagick",
            ProjectHomeUrl = "https://imagemagick.org",
            AssetPattern = OperatingSystem.IsWindows()
                ? @"ImageMagick-[\d.]+-portable-Q8-x64\.zip"
                : @"ImageMagick-[\d.]+-gcc-x86_64\.AppImage",
            InstallPath = Path.Combine(DepsRoot, "magick")
        }
    ];

    public IEnumerable<ConfigRequirement> ConfigRequirements => [];

    public IEnumerable<NavEntry> GetNavEntries() =>
    [
        // Pages added in later phases. Phase A leaves this empty so the module
        // registers cleanly and magick shows up in Settings → Dependencies
        // before any tool pages exist.
    ];

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}
```

- [ ] **Step 2: Build to verify**

Run:
```
dotnet build src/ControlMenu/ControlMenu.csproj -c Release
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```
git add src/ControlMenu/Modules/Imaging/ImagingModule.cs
git commit -m "feat(imaging): ImagingModule with magick ModuleDependency"
```

### Task A.6: Create IImageService interface

**Files:**
- Create: `src/ControlMenu/Modules/Imaging/Services/IImageService.cs`
- Create: `src/ControlMenu/Modules/Imaging/Services/ImageInfo.cs`
- Create: `src/ControlMenu/Modules/Imaging/Services/ImagingException.cs`

- [ ] **Step 1: Create ImageInfo record**

Create `src/ControlMenu/Modules/Imaging/Services/ImageInfo.cs`:

```csharp
namespace ControlMenu.Modules.Imaging.Services;

public record ImageInfo(int Width, int Height, string Format, bool HasAlpha, long SizeBytes);
```

- [ ] **Step 2: Create ImagingException**

Create `src/ControlMenu/Modules/Imaging/Services/ImagingException.cs`:

```csharp
namespace ControlMenu.Modules.Imaging.Services;

public class ImagingException : Exception
{
    public ImagingException(string message) : base(message) { }
    public ImagingException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 3: Create option records (forward declarations; bodies fleshed out in their phases)**

Create `src/ControlMenu/Modules/Imaging/Services/Options/IcoOptions.cs`:

```csharp
namespace ControlMenu.Modules.Imaging.Services.Options;

public record IcoOptions
{
    // No options needed in v1 — sizes are passed as a separate parameter.
    // Placeholder for future per-icon defines (e.g., color depth overrides).
}
```

Create `src/ControlMenu/Modules/Imaging/Services/Options/ConvertFormatOptions.cs`:

```csharp
namespace ControlMenu.Modules.Imaging.Services.Options;

public record ConvertFormatOptions
{
    /// <summary>Quality 0-100 for lossy formats (JPG, WebP, AVIF). Default 90.</summary>
    public int Quality { get; init; } = 90;
}
```

Create `src/ControlMenu/Modules/Imaging/Services/Options/ResizeOptions.cs`:

```csharp
namespace ControlMenu.Modules.Imaging.Services.Options;

public enum ResizeMode { PixelDimensions, Percentage, MaxDimensionFit }

public record ResizeOptions
{
    public ResizeMode Mode { get; init; } = ResizeMode.PixelDimensions;
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? Percentage { get; init; }
    public int? MaxDimension { get; init; }
    public bool LockAspect { get; init; } = true;
}
```

Create `src/ControlMenu/Modules/Imaging/Services/Options/RasterizeOptions.cs`:

```csharp
namespace ControlMenu.Modules.Imaging.Services.Options;

public record RasterizeOptions
{
    public int[] Sizes { get; init; } = [256, 512];
    /// <summary>"png" or "ico". For "ico", all selected sizes bundle into one .ico.</summary>
    public string OutputFormat { get; init; } = "png";
    /// <summary>"transparent" or a hex color like "#ffffff".</summary>
    public string Background { get; init; } = "transparent";
}
```

Create `src/ControlMenu/Modules/Imaging/Services/Options/BackgroundRemoveOptions.cs`:

```csharp
namespace ControlMenu.Modules.Imaging.Services.Options;

public record BackgroundRemoveOptions
{
    public int SeedX { get; init; }
    public int SeedY { get; init; }
    /// <summary>0-100 percent.</summary>
    public int Tolerance { get; init; } = 15;
    public bool Contiguous { get; init; } = true;
}
```

- [ ] **Step 4: Create IImageService**

Create `src/ControlMenu/Modules/Imaging/Services/IImageService.cs`:

```csharp
using ControlMenu.Modules.Imaging.Services.Options;

namespace ControlMenu.Modules.Imaging.Services;

public interface IImageService
{
    Task<byte[]> ConvertFormatAsync(byte[] input, string targetFormat, ConvertFormatOptions? options = null, CancellationToken ct = default);
    Task<byte[]> ResizeAsync(byte[] input, ResizeOptions options, CancellationToken ct = default);
    Task<byte[]> ConvertToIcoAsync(byte[] input, int[] sizes, IcoOptions? options = null, CancellationToken ct = default);
    Task<byte[]> RemoveBackgroundAsync(byte[] input, BackgroundRemoveOptions options, CancellationToken ct = default);
    Task<byte[]> RasterizeSvgAsync(byte[] svgBytes, RasterizeOptions options, CancellationToken ct = default);
    Task<ImageInfo> GetInfoAsync(byte[] input, CancellationToken ct = default);
}
```

- [ ] **Step 5: Build**

Run:
```
dotnet build src/ControlMenu/ControlMenu.csproj -c Release
```

Expected: build succeeds.

- [ ] **Step 6: Commit**

```
git add src/ControlMenu/Modules/Imaging/Services
git commit -m "feat(imaging): IImageService interface + option records + ImageInfo + ImagingException"
```

### Task A.7: Create ImageService skeleton

**Files:**
- Create: `src/ControlMenu/Modules/Imaging/Services/ImageService.cs`

- [ ] **Step 1: Author the skeleton**

Create `src/ControlMenu/Modules/Imaging/Services/ImageService.cs`:

```csharp
using ControlMenu.Common.Paths;
using ControlMenu.Modules.Imaging.Services.Options;
using ControlMenu.Services;
using Serilog;

namespace ControlMenu.Modules.Imaging.Services;

public class ImageService : IImageService
{
    private const string ModuleId = "imaging";
    private const string MagickName = "magick";

    // Per-call resource caps — defense in depth alongside policy.xml.
    private const string LimitFlags = "-limit memory 512MB -limit area 16384x16384 -limit map 1GB";

    private readonly ICommandExecutor _executor;
    private readonly IDependencyPathResolver _resolver;
    private readonly IDataPathResolver _paths;

    public ImageService(
        ICommandExecutor executor,
        IDependencyPathResolver resolver,
        IDataPathResolver paths)
    {
        _executor = executor;
        _resolver = resolver;
        _paths = paths;
    }

    public Task<byte[]> ConvertFormatAsync(byte[] input, string targetFormat, ConvertFormatOptions? options = null, CancellationToken ct = default)
        => throw new NotImplementedException("Phase C");

    public Task<byte[]> ResizeAsync(byte[] input, ResizeOptions options, CancellationToken ct = default)
        => throw new NotImplementedException("Phase C");

    public Task<byte[]> ConvertToIcoAsync(byte[] input, int[] sizes, IcoOptions? options = null, CancellationToken ct = default)
        => throw new NotImplementedException("Phase B");

    public Task<byte[]> RemoveBackgroundAsync(byte[] input, BackgroundRemoveOptions options, CancellationToken ct = default)
        => throw new NotImplementedException("Phase E");

    public Task<byte[]> RasterizeSvgAsync(byte[] svgBytes, RasterizeOptions options, CancellationToken ct = default)
        => throw new NotImplementedException("Phase D");

    public Task<ImageInfo> GetInfoAsync(byte[] input, CancellationToken ct = default)
        => throw new NotImplementedException("Phase B");

    /// <summary>Allocate a per-call workdir under &lt;dataRoot&gt;/temp/imaging/&lt;guid&gt;/.</summary>
    private string CreateWorkDir()
    {
        var dir = Path.Combine(_paths.GetDataRoot(), "temp", "imaging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Build the env var dict pointing magick at our custom policy.xml location.</summary>
    private Dictionary<string, string> BuildMagickEnv()
    {
        var depsDir = Path.Combine(_paths.GetDependenciesDir(), "magick");
        var tmpDir = Path.Combine(_paths.GetDataRoot(), "temp", "imaging", "magick-scratch");
        Directory.CreateDirectory(tmpDir);
        return new Dictionary<string, string>
        {
            ["MAGICK_CONFIGURE_PATH"] = depsDir,
            ["MAGICK_TMPDIR"] = tmpDir,
            ["MAGICK_TEMPORARY_PATH"] = tmpDir,
        };
    }

    /// <summary>Invoke magick.exe with shared environment, parse logs, throw on non-zero exit.</summary>
    private async Task<CommandResult> InvokeMagickAsync(string args, CancellationToken ct)
    {
        var def = new CommandDefinition
        {
            Command = await _resolver.ResolveAsync(ModuleId, MagickName, ct),
            Arguments = args,
            Environment = BuildMagickEnv(),
        };
        var result = await _executor.ExecuteAsync(def, ct);

        if (result.ExitCode != 0)
        {
            Log.Error("magick exit {ExitCode}: {Stderr}", result.ExitCode, result.StandardError);
            throw new ImagingException($"magick failed (exit {result.ExitCode}): {result.StandardError}");
        }
        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            Log.Warning("magick: {Stderr}", result.StandardError);
        }
        return result;
    }
}
```

**Note:** if `CommandDefinition` doesn't currently support an `Environment` property, extend it (review `src/ControlMenu/Services/CommandDefinition.cs` and add `public Dictionary<string, string>? Environment { get; init; }`; ensure `CommandExecutor` applies it via `ProcessStartInfo.Environment` overrides). If this extension is non-trivial, fold it into Task A.7 as a sub-step before the skeleton compiles.

- [ ] **Step 2: Build**

Run:
```
dotnet build src/ControlMenu/ControlMenu.csproj -c Release
```

Expected: build succeeds (the `NotImplementedException` placeholders compile fine).

- [ ] **Step 3: Commit**

```
git add src/ControlMenu/Modules/Imaging/Services/ImageService.cs
git commit -m "feat(imaging): ImageService skeleton with magick invocation helper"
```

If you needed to extend `CommandDefinition` for env vars, separate that into its own commit BEFORE this one:

```
git add src/ControlMenu/Services/CommandDefinition.cs src/ControlMenu/Services/CommandExecutor.cs
git commit -m "feat(executor): allow per-invocation environment overrides"
```

### Task A.8: Register IImageService in DI

**Files:**
- Modify: `src/ControlMenu/Program.cs`

- [ ] **Step 1: Locate the existing DI registration block**

Read `src/ControlMenu/Program.cs`. Find the section where domain services are registered (look for `IDeviceService`, `IJellyfinService`, or similar).

- [ ] **Step 2: Add IImageService registration**

In the DI block, add:

```csharp
builder.Services.AddSingleton<ControlMenu.Modules.Imaging.Services.IImageService,
                              ControlMenu.Modules.Imaging.Services.ImageService>();
```

- [ ] **Step 3: Build**

Run:
```
dotnet build src/ControlMenu/ControlMenu.csproj -c Release
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```
git add src/ControlMenu/Program.cs
git commit -m "feat(imaging): register IImageService in DI"
```

### Task A.9: Phase A smoke gate

**Files:** none (manual verification)

- [ ] **Step 1: Run CM**

Run:
```
dotnet run --project src/ControlMenu/ControlMenu.csproj -c Release
```

Wait for "Now listening on: http://localhost:5159".

- [ ] **Step 2: Open browser to http://localhost:5159/settings/dependencies**

Expected: `magick` row appears alongside `adb`, `scrcpy`, `go2rtc`. Status: "Not installed" (we haven't deployed via Velopack, so seed hasn't hydrated). That's correct for the dev-run path.

- [ ] **Step 3: Stop CM and verify version-check call against GitHub works**

Stop CM. The dependency-check hosted service should have made one call to GitHub releases on startup. Check `controlmenu.log` for `ImageMagick/ImageMagick` upstream version detection — should show a string like "Latest upstream: 7.1.1-39".

- [ ] **Step 4: Stage a local magick install for dev iteration**

To test installed-mode in dev:

```
pwsh -NoProfile scripts/dependencies/fetch-magick.ps1
```

Then copy `publish/seed/dependencies/magick/` into your `<dataRoot>/dependencies/magick/` (in dev mode, dataRoot is `src/ControlMenu/bin/Release/net10.0/`).

Re-run CM, re-open `/settings/dependencies`, expect: magick status = "Installed", version = `7.1.1-39`.

- [ ] **Step 5: Mark Phase A done**

No commit needed; smoke verification only.

---

## Phase B — Icon Converter migration

Goal: migrate the existing icon converter to the new module, backed by `magick.exe`'s `icon:auto-resize` define (which handles BMP/PNG mixing automatically). Public service surface stable; old route redirects.

### Task B.1: Write GetInfoAsync test (TDD)

**Files:**
- Create: `tests/ControlMenu.Tests/Modules/Imaging/ImageServiceFixture.cs`
- Create: `tests/ControlMenu.Tests/Modules/Imaging/ImageService.GetInfoTests.cs`

- [ ] **Step 1: Create test fixture**

Create `tests/ControlMenu.Tests/Modules/Imaging/ImageServiceFixture.cs`:

```csharp
using ControlMenu.Common.Paths;
using ControlMenu.Modules.Imaging.Services;
using ControlMenu.Services;

namespace ControlMenu.Tests.Modules.Imaging;

/// <summary>
/// Shared xUnit collection fixture: resolves a real ImageService backed by a real
/// CommandExecutor + real DependencyPathResolver. Tests using this fixture spawn
/// actual magick.exe. If magick isn't installed locally or in CI, the resolver
/// throws DependencyNotInstalledException at first use; tests catch this in
/// constructors and skip via Skip.If.
/// </summary>
public class ImageServiceFixture : IDisposable
{
    public string TempRoot { get; }
    public ImageService Service { get; }
    public bool MagickAvailable { get; }

    public ImageServiceFixture()
    {
        TempRoot = Path.Combine(Path.GetTempPath(), "CM-Imaging-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempRoot);

        // Real wiring; resolver may throw, captured below
        var dataPaths = new TestDataPathResolver(TempRoot);
        var executor = new CommandExecutor();
        var depResolver = TestDependencyPathResolver.RealOrSkip();
        try
        {
            // probe: throws if magick not installed
            depResolver.ResolveAsync("imaging", "magick").GetAwaiter().GetResult();
            MagickAvailable = true;
        }
        catch
        {
            MagickAvailable = false;
        }

        Service = new ImageService(executor, depResolver, dataPaths);
    }

    public void Dispose()
    {
        try { Directory.Delete(TempRoot, recursive: true); } catch { }
    }
}

[CollectionDefinition(nameof(ImageServiceCollection))]
public class ImageServiceCollection : ICollectionFixture<ImageServiceFixture> { }
```

(Helper classes `TestDataPathResolver` and `TestDependencyPathResolver.RealOrSkip` follow existing patterns in the test project — adapt from how `AdbServiceTests` wires its resolver. If those test helpers don't exist as such, create minimal in-line versions inside the fixture file.)

- [ ] **Step 2: Write the failing test**

Create `tests/ControlMenu.Tests/Modules/Imaging/ImageService.GetInfoTests.cs`:

```csharp
using ControlMenu.Modules.Imaging.Services;
using Xunit;

namespace ControlMenu.Tests.Modules.Imaging;

[Collection(nameof(ImageServiceCollection))]
public class ImageServiceGetInfoTests
{
    private readonly ImageServiceFixture _fx;

    public ImageServiceGetInfoTests(ImageServiceFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task GetInfoAsync_ReturnsWidthHeightFormat_ForPng()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick.exe not installed");
        var pngBytes = TestImages.CreatePng(256, 128);

        var info = await _fx.Service.GetInfoAsync(pngBytes);

        Assert.Equal(256, info.Width);
        Assert.Equal(128, info.Height);
        Assert.Equal("PNG", info.Format);
        Assert.True(info.HasAlpha);
    }
}
```

(`TestImages.CreatePng(w, h)` is a small SkiaSharp-based test helper — create it at `tests/ControlMenu.Tests/Modules/Imaging/TestImages.cs` if it doesn't exist:

```csharp
using SkiaSharp;

namespace ControlMenu.Tests.Modules.Imaging;

internal static class TestImages
{
    public static byte[] CreatePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(255, 0, 0, 200));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
```
)

- [ ] **Step 3: Run test to verify it fails**

Run:
```
dotnet test tests/ControlMenu.Tests/ --filter "GetInfoAsync_ReturnsWidthHeightFormat_ForPng" --no-build
```

Expected: FAIL with `NotImplementedException` ("Phase B").

- [ ] **Step 4: Implement GetInfoAsync**

In `src/ControlMenu/Modules/Imaging/Services/ImageService.cs`, replace the `GetInfoAsync` body:

```csharp
public async Task<ImageInfo> GetInfoAsync(byte[] input, CancellationToken ct = default)
{
    var workDir = CreateWorkDir();
    try
    {
        var inputPath = Path.Combine(workDir, "in.bin");
        await File.WriteAllBytesAsync(inputPath, input, ct);

        // magick identify -format "%w %h %m %A" <input>
        // %w=width, %h=height, %m=format, %A=alpha-channel-name (True/False/Blend/etc)
        var def = new CommandDefinition
        {
            Command = await _resolver.ResolveAsync(ModuleId, MagickName, ct),
            Arguments = $"identify -format \"%w %h %m %A\" \"{inputPath}\"",
            Environment = BuildMagickEnv(),
        };
        var result = await _executor.ExecuteAsync(def, ct);
        if (result.ExitCode != 0)
            throw new ImagingException($"magick identify failed: {result.StandardError}");

        var parts = result.StandardOutput.Trim().Split(' ');
        if (parts.Length < 4)
            throw new ImagingException($"unparseable identify output: {result.StandardOutput}");

        return new ImageInfo(
            Width: int.Parse(parts[0]),
            Height: int.Parse(parts[1]),
            Format: parts[2],
            HasAlpha: !parts[3].Equals("False", StringComparison.OrdinalIgnoreCase) &&
                      !parts[3].Equals("Undefined", StringComparison.OrdinalIgnoreCase),
            SizeBytes: input.LongLength);
    }
    finally
    {
        try { Directory.Delete(workDir, recursive: true); } catch { }
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run:
```
dotnet test tests/ControlMenu.Tests/ --filter "GetInfoAsync_ReturnsWidthHeightFormat_ForPng"
```

Expected: PASS (assuming magick is installed locally).

- [ ] **Step 6: Commit**

```
git add tests/ControlMenu.Tests/Modules/Imaging/ src/ControlMenu/Modules/Imaging/Services/ImageService.cs
git commit -m "feat(imaging): GetInfoAsync + test fixture"
```

### Task B.2: Write ConvertToIcoAsync tests

**Files:**
- Create: `tests/ControlMenu.Tests/Modules/Imaging/ImageService.IconTests.cs`

- [ ] **Step 1: Write structural tests for the ICO output**

Create `tests/ControlMenu.Tests/Modules/Imaging/ImageService.IconTests.cs`:

```csharp
using Xunit;

namespace ControlMenu.Tests.Modules.Imaging;

[Collection(nameof(ImageServiceCollection))]
public class ImageServiceIconTests
{
    private readonly ImageServiceFixture _fx;
    public ImageServiceIconTests(ImageServiceFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task ConvertToIcoAsync_ProducesValidIco_With3Sizes()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick.exe not installed");
        var sourceBytes = TestImages.CreatePng(1024, 1024);

        var ico = await _fx.Service.ConvertToIcoAsync(sourceBytes, [64, 128, 256]);

        Assert.True(ico.Length > 22, "ICO too small");
        Assert.Equal(0, BitConverter.ToUInt16(ico, 0));   // reserved
        Assert.Equal(1, BitConverter.ToUInt16(ico, 2));   // type = icon
        Assert.Equal(3, BitConverter.ToUInt16(ico, 4));   // 3 entries
    }

    [SkippableFact]
    public async Task ConvertToIcoAsync_SmallSizesAreBmpEncoded_LargeSizesArePngEncoded()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick.exe not installed");
        var sourceBytes = TestImages.CreatePng(512, 512);

        var ico = await _fx.Service.ConvertToIcoAsync(sourceBytes, [32, 64, 256]);

        var entries = IcoParser.ParseEntries(ico);
        Assert.Equal(3, entries.Count);

        // 32px entry: BMP-encoded (starts with 0x28 = BITMAPINFOHEADER size = 40)
        var entry32 = entries.Single(e => e.Width == 32);
        Assert.Equal(0x28, entry32.PayloadFirstByte);

        // 64px entry: BMP-encoded (also ≤48? No, 64 > 48; should be PNG)
        var entry64 = entries.Single(e => e.Width == 64);
        Assert.Equal(0x89, entry64.PayloadFirstByte);  // 0x89 = PNG signature byte 1

        // 256px entry: PNG-encoded
        var entry256 = entries.Single(e => e.Width == 0);  // 256 stored as 0
        Assert.Equal(0x89, entry256.PayloadFirstByte);
    }

    [SkippableFact]
    public async Task ConvertToIcoAsync_DefaultSizes_Creates3Entries()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick.exe not installed");
        var sourceBytes = TestImages.CreatePng(256, 256);

        var ico = await _fx.Service.ConvertToIcoAsync(sourceBytes, [64, 128, 256]);

        Assert.Equal(3, BitConverter.ToUInt16(ico, 4));
    }

    [SkippableFact]
    public async Task ConvertToIcoAsync_HandlesNonSquareInput()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick.exe not installed");
        var sourceBytes = TestImages.CreatePng(200, 100);

        var ico = await _fx.Service.ConvertToIcoAsync(sourceBytes, [64]);

        Assert.Equal(1, BitConverter.ToUInt16(ico, 4));
    }
}

/// <summary>Minimal ICO parser for structural test assertions.</summary>
internal static class IcoParser
{
    public record Entry(int Width, int Height, int PayloadOffset, int PayloadLength, byte PayloadFirstByte);

    public static List<Entry> ParseEntries(byte[] ico)
    {
        var count = BitConverter.ToUInt16(ico, 4);
        var entries = new List<Entry>(count);
        for (int i = 0; i < count; i++)
        {
            var off = 6 + 16 * i;
            var width = ico[off];
            var height = ico[off + 1];
            var payloadLen = BitConverter.ToInt32(ico, off + 8);
            var payloadOff = BitConverter.ToInt32(ico, off + 12);
            entries.Add(new Entry(width, height, payloadOff, payloadLen, ico[payloadOff]));
        }
        return entries;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```
dotnet test tests/ControlMenu.Tests/ --filter "ImageServiceIconTests"
```

Expected: all 4 tests FAIL with `NotImplementedException`.

- [ ] **Step 3: Commit failing tests**

```
git add tests/ControlMenu.Tests/Modules/Imaging/ImageService.IconTests.cs
git commit -m "test(imaging): ConvertToIcoAsync structural tests (failing)"
```

### Task B.3: Implement ConvertToIcoAsync

**Files:**
- Modify: `src/ControlMenu/Modules/Imaging/Services/ImageService.cs`

- [ ] **Step 1: Implement the method**

Replace `ConvertToIcoAsync` body in `ImageService.cs`:

```csharp
public async Task<byte[]> ConvertToIcoAsync(byte[] input, int[] sizes, IcoOptions? options = null, CancellationToken ct = default)
{
    if (sizes is null || sizes.Length == 0)
        throw new ArgumentException("At least one size required", nameof(sizes));

    var workDir = CreateWorkDir();
    try
    {
        var inputPath = Path.Combine(workDir, "in.bin");
        var outputPath = Path.Combine(workDir, "out.ico");
        await File.WriteAllBytesAsync(inputPath, input, ct);

        // -define icon:auto-resize=<csv> generates entries at each listed size.
        // Magick automatically picks BMP-with-AND-mask for ≤48 and PNG for ≥256;
        // 64/128 default to PNG. Resize is Lanczos by default for icon defines.
        var sizesCsv = string.Join(",", sizes.OrderBy(s => s));
        var args = $"{LimitFlags} \"{inputPath}\" -define icon:auto-resize={sizesCsv} \"{outputPath}\"";

        await InvokeMagickAsync(args, ct);

        return await File.ReadAllBytesAsync(outputPath, ct);
    }
    finally
    {
        try { Directory.Delete(workDir, recursive: true); } catch { }
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run:
```
dotnet test tests/ControlMenu.Tests/ --filter "ImageServiceIconTests"
```

Expected: all 4 tests PASS.

- [ ] **Step 3: Commit**

```
git add src/ControlMenu/Modules/Imaging/Services/ImageService.cs
git commit -m "feat(imaging): ConvertToIcoAsync via magick icon:auto-resize define"
```

### Task B.4: Migrate IconConverter.razor page

**Files:**
- Create: `src/ControlMenu/Modules/Imaging/Pages/IconConverter.razor`
- Create: `src/ControlMenu/Modules/Imaging/Pages/IconConverter.razor.css`

- [ ] **Step 1: Copy + adapt the existing razor file**

Copy `src/ControlMenu/Modules/Utilities/Pages/IconConverter.razor` to `src/ControlMenu/Modules/Imaging/Pages/IconConverter.razor`. Then modify:

- Change `@page "/utilities/icon-converter"` to `@page "/imaging/icon-converter"`.
- Change `@inject IIconConversionService IconService` to `@inject ControlMenu.Modules.Imaging.Services.IImageService IconService`.
- Change `await IconService.ConvertToIcoBytesAsync(_fileBytes, sizes)` to `await IconService.ConvertToIcoAsync(_fileBytes, sizes)`.
- Change `await IconService.ConvertToIcoAsync(source, targetPath, sizes)` to:
  ```csharp
  var sourceBytes = await File.ReadAllBytesAsync(source);
  var icoBytes = await IconService.ConvertToIcoAsync(sourceBytes, sizes);
  await File.WriteAllBytesAsync(targetPath, icoBytes);
  ```
  (the new `IImageService` is bytes-only; the on-disk wrapper is gone)

Full final content shown in step 2 below for completeness.

- [ ] **Step 2: Write the final IconConverter.razor**

Create `src/ControlMenu/Modules/Imaging/Pages/IconConverter.razor`:

```razor
@page "/imaging/icon-converter"
@using ControlMenu.Modules.Imaging.Services
@inject IImageService IconService
@inject IWebHostEnvironment Env
@inject IJSRuntime JS

<PageTitle>Icon Converter</PageTitle>

<h1><i class="bi bi-file-earmark-image"></i> Icon Converter</h1>
<p class="page-subtitle">Convert an image to an ICO file with multiple sizes. Supported formats: PNG, JPG, JPEG, BMP, GIF, WEBP, TIFF.</p>

<div class="converter-panel">
    <div class="form-group">
        <label class="form-label">Source Image</label>
        @if (_hasFileSystemAccess)
        {
            <div style="display:flex; gap:0.5rem; align-items:center;">
                <button class="btn btn-secondary" @onclick="PickFile">
                    <i class="bi bi-file-earmark-image"></i> Select Image
                </button>
                @if (!string.IsNullOrEmpty(_fileName))
                {
                    <span class="file-info">@_fileName (@ControlMenu.Services.FormatHelper.FormatSize(_fileBytes?.Length ?? 0))</span>
                }
            </div>
        }
        else
        {
            <input type="text" class="form-control" style="max-width:500px;"
                   placeholder="C:\path\to\image.png"
                   @bind="_sourcePath" />
            <div class="form-hint">File System Access API not available in this browser. Type the full path instead.</div>
        }
    </div>

    <div class="form-group">
        <label class="form-label">Icon Sizes</label>
        <div class="size-options">
            @foreach (var size in _availableSizes)
            {
                <label class="size-checkbox">
                    <input type="checkbox" checked="@_selectedSizes.Contains(size)" @onchange="e => ToggleSize(size, (bool)e.Value!)" />
                    @(size)px
                </label>
            }
        </div>
    </div>

    @if (!string.IsNullOrEmpty(_error))
    {
        <div class="error-panel">
            <i class="bi bi-exclamation-triangle"></i> @_error
        </div>
    }

    @if (_converting)
    {
        <div class="status-info">
            <i class="bi bi-arrow-repeat spin"></i> Converting...
        </div>
    }

    <button class="btn btn-primary btn-lg" @onclick="Convert"
            disabled="@(!_canConvert || _converting || _selectedSizes.Count == 0)">
        <i class="bi bi-arrow-right-circle"></i> Convert to ICO
    </button>

    @if (_resultMessage is not null)
    {
        <div class="download-panel">
            <i class="bi bi-check-circle-fill" style="color:var(--success-color); font-size:1.2rem;"></i>
            <div>
                <strong>@_resultMessage</strong>
                @if (_savedPath is not null)
                {
                    <div style="margin-top:0.25rem; font-size:0.85rem; color:var(--text-secondary);">
                        Saved to: <code>@_savedPath</code>
                    </div>
                }
            </div>
            @if (_downloadUrl is not null)
            {
                <a href="@_downloadUrl" download="@_icoFileName" class="btn-pill btn-pill-success">
                    <i class="bi bi-download"></i> Download Copy
                </a>
            }
        </div>
    }
</div>

@code {
    private readonly int[] _availableSizes = [16, 32, 48, 64, 128, 256];
    private HashSet<int> _selectedSizes = [64, 128, 256];
    private bool _converting;
    private string? _error;
    private string? _resultMessage;
    private string? _savedPath;
    private string? _downloadUrl;
    private string? _icoFileName;

    private bool _hasFileSystemAccess;
    private string? _fileName;
    private byte[]? _fileBytes;
    private string _sourcePath = "";

    private bool _canConvert => _fileBytes is not null || !string.IsNullOrWhiteSpace(_sourcePath);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _hasFileSystemAccess = await JS.InvokeAsync<bool>("hasFileSystemAccess");
            // Best-effort magick warm-up so first convert hits OS file cache
            _ = Task.Run(async () =>
            {
                try
                {
                    var dummy = TestImages.CreateEmptyPng();
                    _ = await IconService.GetInfoAsync(dummy);
                }
                catch { /* swallow */ }
            });
            StateHasChanged();
        }
    }

    private async Task PickFile()
    {
        _resultMessage = null;
        _downloadUrl = null;
        _savedPath = null;
        _error = null;

        try
        {
            var result = await JS.InvokeAsync<FilePickerResult?>("filePickerOpen",
                ".png,.jpg,.jpeg,.bmp,.gif,.webp,.tiff,.tif");
            if (result is null) return;

            _fileName = result.Name;
            _fileBytes = System.Convert.FromBase64String(result.BytesBase64);
            _icoFileName = Path.GetFileNameWithoutExtension(result.Name) + ".ico";
        }
        catch (Exception ex)
        {
            _error = $"Could not read selected file: {ex.Message}";
        }
    }

    private void ToggleSize(int size, bool selected)
    {
        if (selected) _selectedSizes.Add(size);
        else _selectedSizes.Remove(size);
    }

    private async Task Convert()
    {
        if (_selectedSizes.Count == 0) return;

        _converting = true;
        _resultMessage = null;
        _savedPath = null;
        _downloadUrl = null;
        _error = null;

        try
        {
            var sizes = _selectedSizes.OrderBy(s => s).ToArray();

            if (_fileBytes is not null)
            {
                var icoBytes = await IconService.ConvertToIcoAsync(_fileBytes, sizes);
                var base64 = System.Convert.ToBase64String(icoBytes);
                var savedName = await JS.InvokeAsync<string?>("filePickerSave", _icoFileName, base64);

                if (savedName is not null)
                {
                    _resultMessage = "Icon created successfully.";
                    _savedPath = savedName;

                    var tempDir = Path.Combine(Env.WebRootPath, "temp");
                    Directory.CreateDirectory(tempDir);
                    var webCopy = Path.Combine(tempDir, $"{Guid.NewGuid():N}.ico");
                    await File.WriteAllBytesAsync(webCopy, icoBytes);
                    _downloadUrl = $"/temp/{Path.GetFileName(webCopy)}";

                    _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
                    {
                        try { if (File.Exists(webCopy)) File.Delete(webCopy); } catch { }
                    });
                }
                else
                {
                    _error = "Save cancelled.";
                }
            }
            else if (!string.IsNullOrWhiteSpace(_sourcePath))
            {
                var source = _sourcePath.Trim();
                if (!File.Exists(source))
                {
                    _error = $"File not found: {source}";
                    return;
                }

                var sourceDir = Path.GetDirectoryName(source)!;
                var baseName = Path.GetFileNameWithoutExtension(source);
                _icoFileName = $"{baseName}.ico";
                var targetPath = Path.Combine(sourceDir, _icoFileName);

                var sourceBytes = await File.ReadAllBytesAsync(source);
                var icoBytes = await IconService.ConvertToIcoAsync(sourceBytes, sizes);
                await File.WriteAllBytesAsync(targetPath, icoBytes);

                _resultMessage = "Icon created successfully.";
                _savedPath = targetPath;

                var tempDir = Path.Combine(Env.WebRootPath, "temp");
                Directory.CreateDirectory(tempDir);
                var webCopy = Path.Combine(tempDir, $"{Guid.NewGuid():N}.ico");
                File.Copy(targetPath, webCopy);
                _downloadUrl = $"/temp/{Path.GetFileName(webCopy)}";

                _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
                {
                    try { if (File.Exists(webCopy)) File.Delete(webCopy); } catch { }
                });
            }
        }
        catch (Exception ex)
        {
            _error = $"Conversion failed: {ex.Message}";
        }
        finally
        {
            _converting = false;
        }
    }

    private record FilePickerResult(string Name, string BytesBase64);

    /// <summary>Tiny inline helper: produces a 1×1 transparent PNG used for warm-up.</summary>
    private static class TestImages
    {
        public static byte[] CreateEmptyPng()
        {
            using var bitmap = new SkiaSharp.SKBitmap(1, 1, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
            using var canvas = new SkiaSharp.SKCanvas(bitmap);
            canvas.Clear(SkiaSharp.SKColors.Transparent);
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }
}
```

- [ ] **Step 3: Copy the existing .razor.css**

Copy `src/ControlMenu/Modules/Utilities/Pages/IconConverter.razor.css` to `src/ControlMenu/Modules/Imaging/Pages/IconConverter.razor.css` unchanged.

- [ ] **Step 4: Add the nav entry to ImagingModule**

In `src/ControlMenu/Modules/Imaging/ImagingModule.cs`, replace `GetNavEntries()`:

```csharp
public IEnumerable<NavEntry> GetNavEntries() =>
[
    new NavEntry("Icon Converter", "/imaging/icon-converter", "🖼️", 0),
];
```

- [ ] **Step 5: Build**

Run:
```
dotnet build src/ControlMenu/ControlMenu.csproj -c Release
```

Expected: build succeeds.

- [ ] **Step 6: Commit**

```
git add src/ControlMenu/Modules/Imaging
git commit -m "feat(imaging): migrate Icon Converter page to /imaging/icon-converter"
```

### Task B.5: Remove old icon converter and add redirect

**Files:**
- Delete: `src/ControlMenu/Modules/Utilities/Pages/IconConverter.razor`
- Delete: `src/ControlMenu/Modules/Utilities/Pages/IconConverter.razor.css`
- Delete: `src/ControlMenu/Modules/Utilities/Services/IconConversionService.cs`
- Delete: `src/ControlMenu/Modules/Utilities/Services/IIconConversionService.cs`
- Delete: `tests/ControlMenu.Tests/Modules/Utilities/IconConversionServiceTests.cs`
- Modify: `src/ControlMenu/Modules/Utilities/UtilitiesModule.cs`
- Modify: `src/ControlMenu/Components/App.razor`
- Modify: `src/ControlMenu/Program.cs` (remove DI registration of old service)

- [ ] **Step 1: Delete the old icon-converter assets**

Run:
```
git rm src/ControlMenu/Modules/Utilities/Pages/IconConverter.razor
git rm src/ControlMenu/Modules/Utilities/Pages/IconConverter.razor.css
git rm src/ControlMenu/Modules/Utilities/Services/IconConversionService.cs
git rm src/ControlMenu/Modules/Utilities/Services/IIconConversionService.cs
git rm tests/ControlMenu.Tests/Modules/Utilities/IconConversionServiceTests.cs
```

- [ ] **Step 2: Remove icon converter from UtilitiesModule**

Edit `src/ControlMenu/Modules/Utilities/UtilitiesModule.cs`, remove the `NavEntry("Icon Converter", ...)` line:

```csharp
public IEnumerable<NavEntry> GetNavEntries() =>
[
    new NavEntry("File Unblocker", "/utilities/file-unblocker", "🔓", 0)
];
```

- [ ] **Step 3: Remove IIconConversionService DI registration**

Find and remove the line in `src/ControlMenu/Program.cs`:

```csharp
builder.Services.AddSingleton<IIconConversionService, IconConversionService>();
```

- [ ] **Step 4: Add redirect for the old route**

In `src/ControlMenu/Components/App.razor`, find the routing block (`<Router ...>`) and add a redirect component, OR (simpler) create a new page at the old route that redirects:

Create `src/ControlMenu/Modules/Utilities/Pages/IconConverterRedirect.razor`:

```razor
@page "/utilities/icon-converter"
@inject NavigationManager Nav

@code {
    protected override void OnInitialized()
    {
        Nav.NavigateTo("/imaging/icon-converter", replace: true);
    }
}
```

- [ ] **Step 5: Build**

Run:
```
dotnet build src/ControlMenu/ControlMenu.csproj -c Release
```

Expected: build succeeds with no errors. Any remaining references to `IIconConversionService` will fail compilation — search and remove.

- [ ] **Step 6: Run all tests**

Run:
```
dotnet test
```

Expected: all green (the deleted `IconConversionServiceTests` is gone; new `ImageServiceIconTests` cover the same ground).

- [ ] **Step 7: Commit**

```
git add -A
git commit -m "feat(imaging): remove old SkiaSharp IconConversionService; redirect /utilities/icon-converter → /imaging/icon-converter"
```

### Task B.6: Phase B smoke gate

**Files:** none (manual verification)

- [ ] **Step 1: Run CM**

Run:
```
dotnet run --project src/ControlMenu/ControlMenu.csproj -c Release
```

- [ ] **Step 2: Navigate the new route**

Browser → `http://localhost:5159/imaging/icon-converter`.

Expected: Icon Converter page renders identically to before. Sidebar shows "Imaging Tools" section with Icon Converter sub-entry.

- [ ] **Step 3: Old route redirects**

Browser → `http://localhost:5159/utilities/icon-converter`.

Expected: redirects automatically to `/imaging/icon-converter`.

- [ ] **Step 4: Convert a 1024×1024 PNG**

Pick a 1024×1024 PNG (the original problem case). Select sizes 16, 32, 48, 64, 128, 256. Click "Convert to ICO". Save the output.

Expected:
- Conversion completes in <2 seconds.
- Resulting `.ico` opens cleanly in Windows Explorer at all 6 sizes.
- Visual quality is **dramatically improved** compared to the pre-migration SkiaSharp output. Small-size entries (16/32/48) are crisp BMP-encoded with proper anti-aliasing; large entries (64/128/256) are clean PNG.

- [ ] **Step 5: Inspect ICO structure**

Use a hex viewer to confirm:
- `entries[0]` (16px): payload first byte = `0x28` (BMP BITMAPINFOHEADER size).
- `entries[5]` (256px): payload first byte = `0x89` (PNG signature).

---

## Phase C — Format Converter + Image Resize

Goal: ship the second and third tools, sharing the `IImageService` infrastructure.

### Task C.1: Write ConvertFormatAsync tests

**Files:**
- Create: `tests/ControlMenu.Tests/Modules/Imaging/ImageService.FormatConvertTests.cs`

- [ ] **Step 1: Write tests**

Create `tests/ControlMenu.Tests/Modules/Imaging/ImageService.FormatConvertTests.cs`:

```csharp
using ControlMenu.Modules.Imaging.Services.Options;
using Xunit;

namespace ControlMenu.Tests.Modules.Imaging;

[Collection(nameof(ImageServiceCollection))]
public class ImageServiceFormatConvertTests
{
    private readonly ImageServiceFixture _fx;
    public ImageServiceFormatConvertTests(ImageServiceFixture fx) => _fx = fx;

    [SkippableTheory]
    [InlineData("jpg", new byte[] { 0xFF, 0xD8 })]
    [InlineData("webp", new byte[] { 0x52, 0x49, 0x46, 0x46 })]
    [InlineData("bmp", new byte[] { 0x42, 0x4D })]
    [InlineData("gif", new byte[] { 0x47, 0x49, 0x46 })]
    public async Task ConvertFormatAsync_PngToFormat_ProducesValidMagicBytes(string format, byte[] expectedMagic)
    {
        Skip.IfNot(_fx.MagickAvailable, "magick.exe not installed");
        var png = TestImages.CreatePng(256, 256);

        var output = await _fx.Service.ConvertFormatAsync(png, format);

        Assert.True(output.Length > expectedMagic.Length);
        for (int i = 0; i < expectedMagic.Length; i++)
            Assert.Equal(expectedMagic[i], output[i]);
    }

    [SkippableFact]
    public async Task ConvertFormatAsync_QualityOptionAffectsJpgSize()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick.exe not installed");
        var png = TestImages.CreatePng(512, 512);

        var hi = await _fx.Service.ConvertFormatAsync(png, "jpg", new ConvertFormatOptions { Quality = 95 });
        var lo = await _fx.Service.ConvertFormatAsync(png, "jpg", new ConvertFormatOptions { Quality = 30 });

        Assert.True(lo.Length < hi.Length, $"low quality ({lo.Length}) should be smaller than high quality ({hi.Length})");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```
dotnet test --filter "ImageServiceFormatConvertTests"
```

Expected: FAIL with `NotImplementedException`.

- [ ] **Step 3: Commit failing tests**

```
git add tests/ControlMenu.Tests/Modules/Imaging/ImageService.FormatConvertTests.cs
git commit -m "test(imaging): ConvertFormatAsync round-trip tests (failing)"
```

### Task C.2: Implement ConvertFormatAsync

**Files:**
- Modify: `src/ControlMenu/Modules/Imaging/Services/ImageService.cs`

- [ ] **Step 1: Implement**

Replace `ConvertFormatAsync` body:

```csharp
public async Task<byte[]> ConvertFormatAsync(byte[] input, string targetFormat, ConvertFormatOptions? options = null, CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(targetFormat))
        throw new ArgumentException("Target format required", nameof(targetFormat));

    var opts = options ?? new ConvertFormatOptions();
    var workDir = CreateWorkDir();
    try
    {
        var inputPath  = Path.Combine(workDir, "in.bin");
        var outputPath = Path.Combine(workDir, $"out.{targetFormat.ToLowerInvariant()}");
        await File.WriteAllBytesAsync(inputPath, input, ct);

        var qualityArg = IsLossyFormat(targetFormat) ? $"-quality {opts.Quality}" : "";
        var args = $"{LimitFlags} \"{inputPath}\" {qualityArg} \"{outputPath}\"";
        await InvokeMagickAsync(args, ct);

        return await File.ReadAllBytesAsync(outputPath, ct);
    }
    finally
    {
        try { Directory.Delete(workDir, recursive: true); } catch { }
    }
}

private static bool IsLossyFormat(string format) =>
    format.Equals("jpg", StringComparison.OrdinalIgnoreCase) ||
    format.Equals("jpeg", StringComparison.OrdinalIgnoreCase) ||
    format.Equals("webp", StringComparison.OrdinalIgnoreCase) ||
    format.Equals("avif", StringComparison.OrdinalIgnoreCase) ||
    format.Equals("heic", StringComparison.OrdinalIgnoreCase);
```

- [ ] **Step 2: Run tests**

Run:
```
dotnet test --filter "ImageServiceFormatConvertTests"
```

Expected: all PASS.

- [ ] **Step 3: Commit**

```
git add src/ControlMenu/Modules/Imaging/Services/ImageService.cs
git commit -m "feat(imaging): ConvertFormatAsync with per-format quality"
```

### Task C.3: Write ResizeAsync tests

**Files:**
- Create: `tests/ControlMenu.Tests/Modules/Imaging/ImageService.ResizeTests.cs`

- [ ] **Step 1: Write tests**

Create:

```csharp
using ControlMenu.Modules.Imaging.Services.Options;
using Xunit;

namespace ControlMenu.Tests.Modules.Imaging;

[Collection(nameof(ImageServiceCollection))]
public class ImageServiceResizeTests
{
    private readonly ImageServiceFixture _fx;
    public ImageServiceResizeTests(ImageServiceFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task ResizeAsync_PixelDimensions_ProducesExactSize()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick.exe not installed");
        var src = TestImages.CreatePng(1024, 768);

        var output = await _fx.Service.ResizeAsync(src, new ResizeOptions
        {
            Mode = ResizeMode.PixelDimensions,
            Width = 200,
            Height = 150,
            LockAspect = false,
        });

        var info = await _fx.Service.GetInfoAsync(output);
        Assert.Equal(200, info.Width);
        Assert.Equal(150, info.Height);
    }

    [SkippableFact]
    public async Task ResizeAsync_Percentage_HalvesBothDims()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick.exe not installed");
        var src = TestImages.CreatePng(1000, 500);

        var output = await _fx.Service.ResizeAsync(src, new ResizeOptions
        {
            Mode = ResizeMode.Percentage,
            Percentage = 50,
        });

        var info = await _fx.Service.GetInfoAsync(output);
        Assert.Equal(500, info.Width);
        Assert.Equal(250, info.Height);
    }

    [SkippableFact]
    public async Task ResizeAsync_MaxDimensionFit_LongestSideBecomesN_AspectPreserved()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick.exe not installed");
        var src = TestImages.CreatePng(2000, 1000);

        var output = await _fx.Service.ResizeAsync(src, new ResizeOptions
        {
            Mode = ResizeMode.MaxDimensionFit,
            MaxDimension = 500,
        });

        var info = await _fx.Service.GetInfoAsync(output);
        Assert.Equal(500, info.Width);
        Assert.Equal(250, info.Height);
    }

    [SkippableFact]
    public async Task ResizeAsync_PixelDimensions_LockAspect_PicksMaxFit()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick.exe not installed");
        var src = TestImages.CreatePng(2000, 1000);

        var output = await _fx.Service.ResizeAsync(src, new ResizeOptions
        {
            Mode = ResizeMode.PixelDimensions,
            Width = 500,
            Height = 500,
            LockAspect = true,
        });

        var info = await _fx.Service.GetInfoAsync(output);
        // With lock aspect, output fits within 500×500 keeping 2:1 ratio → 500×250
        Assert.Equal(500, info.Width);
        Assert.Equal(250, info.Height);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```
dotnet test --filter "ImageServiceResizeTests"
```

Expected: FAIL with `NotImplementedException`.

- [ ] **Step 3: Commit failing tests**

```
git add tests/ControlMenu.Tests/Modules/Imaging/ImageService.ResizeTests.cs
git commit -m "test(imaging): ResizeAsync mode tests (failing)"
```

### Task C.4: Implement ResizeAsync

**Files:**
- Modify: `src/ControlMenu/Modules/Imaging/Services/ImageService.cs`

- [ ] **Step 1: Implement**

Replace `ResizeAsync` body:

```csharp
public async Task<byte[]> ResizeAsync(byte[] input, ResizeOptions options, CancellationToken ct = default)
{
    if (options is null) throw new ArgumentNullException(nameof(options));

    var workDir = CreateWorkDir();
    try
    {
        var inputPath  = Path.Combine(workDir, "in.bin");
        // Preserve the input extension via identify so the output format matches input.
        await File.WriteAllBytesAsync(inputPath, input, ct);
        var info = await GetInfoAsync(input, ct);
        var ext = info.Format.ToLowerInvariant();
        var outputPath = Path.Combine(workDir, $"out.{ext}");

        var resizeArg = BuildResizeArg(options);
        var args = $"{LimitFlags} \"{inputPath}\" -filter Lanczos -resize {resizeArg} \"{outputPath}\"";
        await InvokeMagickAsync(args, ct);

        return await File.ReadAllBytesAsync(outputPath, ct);
    }
    finally
    {
        try { Directory.Delete(workDir, recursive: true); } catch { }
    }
}

private static string BuildResizeArg(ResizeOptions o) => o.Mode switch
{
    ResizeMode.PixelDimensions =>
        o.LockAspect
            ? $"{o.Width ?? 0}x{o.Height ?? 0}"           // magick default: fits within box, preserves aspect
            : $"{o.Width ?? 0}x{o.Height ?? 0}!",         // ! = exact, ignore aspect
    ResizeMode.Percentage =>
        $"{(o.Percentage ?? 100):F2}%",
    ResizeMode.MaxDimensionFit =>
        $"{o.MaxDimension ?? 0}x{o.MaxDimension ?? 0}",  // same as pixel-with-lock-aspect; fits within box
    _ => throw new ArgumentException($"Unknown mode {o.Mode}")
};
```

- [ ] **Step 2: Run tests**

Run:
```
dotnet test --filter "ImageServiceResizeTests"
```

Expected: all PASS.

- [ ] **Step 3: Commit**

```
git add src/ControlMenu/Modules/Imaging/Services/ImageService.cs
git commit -m "feat(imaging): ResizeAsync with Lanczos and 3 modes"
```

### Task C.5: Create FormatConverter.razor

**Files:**
- Create: `src/ControlMenu/Modules/Imaging/Pages/FormatConverter.razor`
- Create: `src/ControlMenu/Modules/Imaging/Pages/FormatConverter.razor.css`

- [ ] **Step 1: Author the page**

Create `src/ControlMenu/Modules/Imaging/Pages/FormatConverter.razor`:

```razor
@page "/imaging/format-converter"
@using ControlMenu.Modules.Imaging.Services
@using ControlMenu.Modules.Imaging.Services.Options
@inject IImageService ImageService
@inject IWebHostEnvironment Env
@inject IJSRuntime JS

<PageTitle>Format Converter</PageTitle>

<h1><i class="bi bi-arrow-left-right"></i> Format Converter</h1>
<p class="page-subtitle">Convert an image between formats: PNG, JPG, WebP, AVIF, TIFF, HEIC, BMP, GIF.</p>

<div class="converter-panel">
    <div class="form-group">
        <label class="form-label">Source Image</label>
        <button class="btn btn-secondary" @onclick="PickFile">
            <i class="bi bi-file-earmark-image"></i> Select Image
        </button>
        @if (_info is not null)
        {
            <div class="file-info">
                @_fileName — @_info.Width × @_info.Height @_info.Format
                (@ControlMenu.Services.FormatHelper.FormatSize(_fileBytes?.Length ?? 0))
            </div>
        }
    </div>

    <div class="form-group">
        <label class="form-label">Target Format</label>
        <select class="form-control" style="max-width:200px;" @bind="_targetFormat">
            @foreach (var fmt in _formats)
            {
                <option value="@fmt">@fmt.ToUpperInvariant()</option>
            }
        </select>
    </div>

    @if (IsLossy(_targetFormat))
    {
        <div class="form-group">
            <label class="form-label">Quality: @_quality</label>
            <input type="range" min="1" max="100" step="1" @bind="_quality" @bind:event="oninput" />
        </div>
    }

    @if (!string.IsNullOrEmpty(_error))
    {
        <div class="error-panel"><i class="bi bi-exclamation-triangle"></i> @_error</div>
    }

    @if (_converting)
    {
        <div class="status-info"><i class="bi bi-arrow-repeat spin"></i> Converting...</div>
    }

    <button class="btn btn-primary btn-lg" @onclick="Convert"
            disabled="@(_fileBytes is null || _converting)">
        <i class="bi bi-arrow-right-circle"></i> Convert
    </button>

    @if (_resultMessage is not null)
    {
        <div class="download-panel">
            <i class="bi bi-check-circle-fill"></i>
            <div>
                <strong>@_resultMessage</strong>
                @if (_savedPath is not null) { <div><code>@_savedPath</code></div> }
            </div>
        </div>
    }
</div>

@code {
    private readonly string[] _formats = ["png", "jpg", "webp", "avif", "tiff", "heic", "bmp", "gif"];
    private string _targetFormat = "webp";
    private int _quality = 90;
    private byte[]? _fileBytes;
    private string? _fileName;
    private ImageInfo? _info;
    private bool _converting;
    private string? _error;
    private string? _resultMessage;
    private string? _savedPath;

    private static bool IsLossy(string fmt) =>
        fmt is "jpg" or "jpeg" or "webp" or "avif" or "heic";

    private async Task PickFile()
    {
        try
        {
            var result = await JS.InvokeAsync<FilePickerResult?>("filePickerOpen",
                ".png,.jpg,.jpeg,.bmp,.gif,.webp,.tiff,.tif,.avif,.heic");
            if (result is null) return;
            _fileName = result.Name;
            _fileBytes = System.Convert.FromBase64String(result.BytesBase64);
            _info = await ImageService.GetInfoAsync(_fileBytes);
            _error = null;
        }
        catch (Exception ex) { _error = $"Could not read file: {ex.Message}"; }
    }

    private async Task Convert()
    {
        if (_fileBytes is null) return;
        _converting = true; _error = null; _resultMessage = null;
        try
        {
            var output = await ImageService.ConvertFormatAsync(_fileBytes, _targetFormat,
                new ConvertFormatOptions { Quality = _quality });
            var newName = Path.GetFileNameWithoutExtension(_fileName) + "." + _targetFormat;
            var saved = await JS.InvokeAsync<string?>("filePickerSave", newName,
                System.Convert.ToBase64String(output));
            if (saved is not null)
            {
                _resultMessage = "Converted successfully.";
                _savedPath = saved;
            }
        }
        catch (Exception ex) { _error = $"Conversion failed: {ex.Message}"; }
        finally { _converting = false; }
    }

    private record FilePickerResult(string Name, string BytesBase64);
}
```

Create an empty `src/ControlMenu/Modules/Imaging/Pages/FormatConverter.razor.css` (Bootstrap classes suffice; component-specific styles can be added later).

- [ ] **Step 2: Add NavEntry**

In `ImagingModule.GetNavEntries()`:

```csharp
public IEnumerable<NavEntry> GetNavEntries() =>
[
    new NavEntry("Icon Converter", "/imaging/icon-converter", "🖼️", 0),
    new NavEntry("Format Converter", "/imaging/format-converter", "🔄", 1),
];
```

- [ ] **Step 3: Build + smoke**

Run:
```
dotnet build src/ControlMenu/ControlMenu.csproj -c Release
dotnet run --project src/ControlMenu/ControlMenu.csproj -c Release
```

Browser → `/imaging/format-converter`. Convert a PNG to WebP at quality 85. Verify output file is valid WebP.

- [ ] **Step 4: Commit**

```
git add src/ControlMenu/Modules/Imaging/Pages/FormatConverter.razor src/ControlMenu/Modules/Imaging/Pages/FormatConverter.razor.css src/ControlMenu/Modules/Imaging/ImagingModule.cs
git commit -m "feat(imaging): FormatConverter page"
```

### Task C.6: Create ImageResize.razor

**Files:**
- Create: `src/ControlMenu/Modules/Imaging/Pages/ImageResize.razor`
- Create: `src/ControlMenu/Modules/Imaging/Pages/ImageResize.razor.css`

- [ ] **Step 1: Author the page**

Create `src/ControlMenu/Modules/Imaging/Pages/ImageResize.razor`:

```razor
@page "/imaging/resize"
@using ControlMenu.Modules.Imaging.Services
@using ControlMenu.Modules.Imaging.Services.Options
@inject IImageService ImageService
@inject IJSRuntime JS

<PageTitle>Image Resize</PageTitle>

<h1><i class="bi bi-aspect-ratio"></i> Image Resize</h1>
<p class="page-subtitle">Resize an image by pixel dimensions, percentage, or max-dimension fit.</p>

<div class="converter-panel">
    <div class="form-group">
        <label class="form-label">Source Image</label>
        <button class="btn btn-secondary" @onclick="PickFile">
            <i class="bi bi-file-earmark-image"></i> Select Image
        </button>
        @if (_info is not null)
        {
            <div class="file-info">@_fileName — @_info.Width × @_info.Height</div>
        }
    </div>

    <div class="form-group">
        <label class="form-label">Mode</label>
        <select class="form-control" style="max-width:240px;" @bind="_mode">
            <option value="@ResizeMode.PixelDimensions">By Pixel Dimensions</option>
            <option value="@ResizeMode.Percentage">By Percentage</option>
            <option value="@ResizeMode.MaxDimensionFit">Max Dimension Fit</option>
        </select>
    </div>

    @if (_mode == ResizeMode.PixelDimensions)
    {
        <div class="form-group">
            <label class="form-label">Width × Height</label>
            <div style="display:flex; gap:0.5rem; align-items:center;">
                <input type="number" class="form-control" style="width:120px;" @bind="_width" />
                <span>×</span>
                <input type="number" class="form-control" style="width:120px;" @bind="_height" />
                <label class="size-checkbox">
                    <input type="checkbox" @bind="_lockAspect" /> Lock aspect ratio
                </label>
            </div>
        </div>
    }
    else if (_mode == ResizeMode.Percentage)
    {
        <div class="form-group">
            <label class="form-label">Percentage</label>
            <input type="number" class="form-control" style="width:120px;" @bind="_percentage" /> %
        </div>
    }
    else
    {
        <div class="form-group">
            <label class="form-label">Max Dimension (px)</label>
            <input type="number" class="form-control" style="width:120px;" @bind="_maxDim" />
        </div>
    }

    @if (!string.IsNullOrEmpty(_error)) { <div class="error-panel">@_error</div> }
    @if (_resizing) { <div class="status-info"><i class="bi bi-arrow-repeat spin"></i> Resizing...</div> }

    <button class="btn btn-primary btn-lg" @onclick="DoResize"
            disabled="@(_fileBytes is null || _resizing)">
        <i class="bi bi-arrow-right-circle"></i> Resize
    </button>

    @if (_resultMessage is not null)
    {
        <div class="download-panel">
            <i class="bi bi-check-circle-fill"></i>
            <strong>@_resultMessage</strong>
            @if (_savedPath is not null) { <div><code>@_savedPath</code></div> }
        </div>
    }
</div>

@code {
    private byte[]? _fileBytes;
    private string? _fileName;
    private ImageInfo? _info;
    private ResizeMode _mode = ResizeMode.PixelDimensions;
    private int _width = 1024;
    private int _height = 1024;
    private bool _lockAspect = true;
    private double _percentage = 50;
    private int _maxDim = 1024;
    private bool _resizing;
    private string? _error;
    private string? _resultMessage;
    private string? _savedPath;

    private async Task PickFile()
    {
        try
        {
            var result = await JS.InvokeAsync<FilePickerResult?>("filePickerOpen",
                ".png,.jpg,.jpeg,.bmp,.gif,.webp,.tiff,.tif");
            if (result is null) return;
            _fileName = result.Name;
            _fileBytes = System.Convert.FromBase64String(result.BytesBase64);
            _info = await ImageService.GetInfoAsync(_fileBytes);
            _width = _info.Width;
            _height = _info.Height;
            _error = null;
        }
        catch (Exception ex) { _error = $"Could not read file: {ex.Message}"; }
    }

    private async Task DoResize()
    {
        if (_fileBytes is null || _info is null) return;
        _resizing = true; _error = null; _resultMessage = null;
        try
        {
            var output = await ImageService.ResizeAsync(_fileBytes, new ResizeOptions
            {
                Mode = _mode,
                Width = _width,
                Height = _height,
                Percentage = _percentage,
                MaxDimension = _maxDim,
                LockAspect = _lockAspect,
            });
            var outInfo = await ImageService.GetInfoAsync(output);
            var ext = Path.GetExtension(_fileName);
            var newName = Path.GetFileNameWithoutExtension(_fileName) + $"-{outInfo.Width}x{outInfo.Height}" + ext;
            var saved = await JS.InvokeAsync<string?>("filePickerSave", newName,
                System.Convert.ToBase64String(output));
            if (saved is not null)
            {
                _resultMessage = $"Resized to {outInfo.Width} × {outInfo.Height}.";
                _savedPath = saved;
            }
        }
        catch (Exception ex) { _error = $"Resize failed: {ex.Message}"; }
        finally { _resizing = false; }
    }

    private record FilePickerResult(string Name, string BytesBase64);
}
```

Create empty `src/ControlMenu/Modules/Imaging/Pages/ImageResize.razor.css`.

- [ ] **Step 2: Add NavEntry**

In `ImagingModule.GetNavEntries()`:

```csharp
new NavEntry("Image Resize", "/imaging/resize", "📐", 2),
```

- [ ] **Step 3: Build + smoke**

Run + browse to `/imaging/resize`. Resize a 1024×1024 PNG to 50% (expect 512×512). Then resize to max-dim 600 (expect aspect-preserved fit).

- [ ] **Step 4: Commit**

```
git add src/ControlMenu/Modules/Imaging
git commit -m "feat(imaging): ImageResize page"
```

---

## Phase D — SVG Rasterize

Goal: rasterize SVG via Svg.Skia (Skia render) then hand to magick for final encoding (PNG-per-size or single-ICO bundle).

### Task D.1: Write RasterizeSvgAsync tests

**Files:**
- Create: `tests/ControlMenu.Tests/Modules/Imaging/ImageService.SvgRasterizeTests.cs`

- [ ] **Step 1: Write tests**

```csharp
using ControlMenu.Modules.Imaging.Services.Options;
using System.Text;
using Xunit;

namespace ControlMenu.Tests.Modules.Imaging;

[Collection(nameof(ImageServiceCollection))]
public class ImageServiceSvgRasterizeTests
{
    private readonly ImageServiceFixture _fx;
    public ImageServiceSvgRasterizeTests(ImageServiceFixture fx) => _fx = fx;

    private static readonly string SimpleSvg =
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
             <circle cx="50" cy="50" r="40" fill="red"/>
           </svg>""";

    [SkippableFact]
    public async Task RasterizeSvgAsync_PngFormat_SingleSize_ReturnsValidPng()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick.exe not installed");
        var svg = Encoding.UTF8.GetBytes(SimpleSvg);

        var output = await _fx.Service.RasterizeSvgAsync(svg, new RasterizeOptions
        {
            Sizes = [128],
            OutputFormat = "png",
        });

        // PNG signature
        Assert.Equal(0x89, output[0]);
        var info = await _fx.Service.GetInfoAsync(output);
        Assert.Equal(128, info.Width);
        Assert.Equal(128, info.Height);
    }

    [SkippableFact]
    public async Task RasterizeSvgAsync_IcoFormat_BundlesAllSizes()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick.exe not installed");
        var svg = Encoding.UTF8.GetBytes(SimpleSvg);

        var output = await _fx.Service.RasterizeSvgAsync(svg, new RasterizeOptions
        {
            Sizes = [32, 128, 256],
            OutputFormat = "ico",
        });

        // ICO header: reserved(2) + type=1(2) + count=3(2)
        Assert.Equal(0, BitConverter.ToUInt16(output, 0));
        Assert.Equal(1, BitConverter.ToUInt16(output, 2));
        Assert.Equal(3, BitConverter.ToUInt16(output, 4));
    }
}
```

- [ ] **Step 2: Run + verify fail**

```
dotnet test --filter "ImageServiceSvgRasterizeTests"
```

Expected: FAIL with `NotImplementedException`.

- [ ] **Step 3: Commit failing tests**

```
git add tests/ControlMenu.Tests/Modules/Imaging/ImageService.SvgRasterizeTests.cs
git commit -m "test(imaging): RasterizeSvgAsync tests (failing)"
```

### Task D.2: Implement RasterizeSvgAsync

**Files:**
- Modify: `src/ControlMenu/Modules/Imaging/Services/ImageService.cs`

- [ ] **Step 1: Implement**

Replace `RasterizeSvgAsync`:

```csharp
public async Task<byte[]> RasterizeSvgAsync(byte[] svgBytes, RasterizeOptions options, CancellationToken ct = default)
{
    if (options is null) throw new ArgumentNullException(nameof(options));
    if (options.Sizes is null || options.Sizes.Length == 0)
        throw new ArgumentException("At least one size required", nameof(options));

    var workDir = CreateWorkDir();
    try
    {
        // Parse SVG once via Svg.Skia, then render at each size to a temp PNG.
        using var svgStream = new MemoryStream(svgBytes);
        using var svg = new Svg.Skia.SKSvg();
        svg.Load(svgStream);
        if (svg.Picture is null)
            throw new ImagingException("Svg.Skia could not parse the SVG document");

        var pngPaths = new List<string>();
        foreach (var size in options.Sizes.OrderBy(s => s))
        {
            var pngPath = Path.Combine(workDir, $"raster-{size}.png");
            RenderSvgToPng(svg, size, options.Background, pngPath);
            pngPaths.Add(pngPath);
        }

        if (options.OutputFormat.Equals("png", StringComparison.OrdinalIgnoreCase))
        {
            // Multi-PNG mode: caller expects ONE result; we return the largest size's PNG.
            // (The page is responsible for invoking once per size to a folder picker.)
            return await File.ReadAllBytesAsync(pngPaths.Last(), ct);
        }
        else if (options.OutputFormat.Equals("ico", StringComparison.OrdinalIgnoreCase))
        {
            var icoPath = Path.Combine(workDir, "bundle.ico");
            var inputArg = string.Join(' ', pngPaths.Select(p => $"\"{p}\""));
            var sizesCsv = string.Join(",", options.Sizes.OrderBy(s => s));
            var args = $"{LimitFlags} {inputArg} -define icon:auto-resize={sizesCsv} \"{icoPath}\"";
            await InvokeMagickAsync(args, ct);
            return await File.ReadAllBytesAsync(icoPath, ct);
        }
        else
        {
            throw new ArgumentException($"Unsupported OutputFormat: {options.OutputFormat}");
        }
    }
    finally
    {
        try { Directory.Delete(workDir, recursive: true); } catch { }
    }
}

private static void RenderSvgToPng(Svg.Skia.SKSvg svg, int size, string background, string outputPath)
{
    using var bitmap = new SkiaSharp.SKBitmap(size, size, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
    using var canvas = new SkiaSharp.SKCanvas(bitmap);

    if (background.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        canvas.Clear(SkiaSharp.SKColors.Transparent);
    else
        canvas.Clear(SkiaSharp.SKColor.Parse(background));

    var rect = svg.Picture!.CullRect;
    var scale = Math.Min(size / rect.Width, size / rect.Height);
    canvas.Scale(scale, scale);
    canvas.DrawPicture(svg.Picture);
    canvas.Flush();

    using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
    using var fs = File.Create(outputPath);
    data.SaveTo(fs);
}
```

**Note:** the PNG-format-mode behavior here returns only the largest size as a single byte[]. The page's multi-size, multi-file save flow is handled at the page level by invoking the service once per size with `Sizes = [singleSize]`. This keeps the service surface simple (one call, one result).

- [ ] **Step 2: Run tests**

```
dotnet test --filter "ImageServiceSvgRasterizeTests"
```

Expected: PASS.

- [ ] **Step 3: Commit**

```
git add src/ControlMenu/Modules/Imaging/Services/ImageService.cs
git commit -m "feat(imaging): RasterizeSvgAsync via Svg.Skia + magick"
```

### Task D.3: Create SvgRasterize.razor

**Files:**
- Create: `src/ControlMenu/Modules/Imaging/Pages/SvgRasterize.razor`
- Create: `src/ControlMenu/Modules/Imaging/Pages/SvgRasterize.razor.css`

- [ ] **Step 1: Author the page**

Create `src/ControlMenu/Modules/Imaging/Pages/SvgRasterize.razor`:

```razor
@page "/imaging/svg-rasterize"
@using ControlMenu.Modules.Imaging.Services
@using ControlMenu.Modules.Imaging.Services.Options
@inject IImageService ImageService
@inject IJSRuntime JS

<PageTitle>SVG Rasterize</PageTitle>

<h1><i class="bi bi-file-earmark-code"></i> SVG Rasterize</h1>
<p class="page-subtitle">Render an SVG to one or more PNGs (or bundle as ICO).</p>

<div class="converter-panel">
    <div class="form-group">
        <label class="form-label">Source SVG</label>
        <button class="btn btn-secondary" @onclick="PickFile">
            <i class="bi bi-file-earmark-code"></i> Select SVG
        </button>
        @if (!string.IsNullOrEmpty(_fileName)) { <div class="file-info">@_fileName</div> }
    </div>

    <div class="form-group">
        <label class="form-label">Output Sizes</label>
        <div class="size-options">
            @foreach (var size in _availableSizes)
            {
                <label class="size-checkbox">
                    <input type="checkbox" checked="@_selectedSizes.Contains(size)"
                           @onchange="e => ToggleSize(size, (bool)e.Value!)" />
                    @(size)px
                </label>
            }
        </div>
    </div>

    <div class="form-group">
        <label class="form-label">Output Format</label>
        <select class="form-control" style="max-width:200px;" @bind="_outputFormat">
            <option value="png">Multiple PNGs</option>
            <option value="ico">Single ICO</option>
        </select>
    </div>

    <div class="form-group">
        <label class="form-label">Background</label>
        <select class="form-control" style="max-width:200px;" @bind="_background">
            <option value="transparent">Transparent</option>
            <option value="#ffffff">White</option>
            <option value="#000000">Black</option>
        </select>
    </div>

    @if (!string.IsNullOrEmpty(_error)) { <div class="error-panel">@_error</div> }
    @if (_rasterizing) { <div class="status-info"><i class="bi bi-arrow-repeat spin"></i> Rasterizing...</div> }

    <button class="btn btn-primary btn-lg" @onclick="Rasterize"
            disabled="@(_svgBytes is null || _selectedSizes.Count == 0 || _rasterizing)">
        <i class="bi bi-arrow-right-circle"></i> Rasterize
    </button>

    @if (_resultMessage is not null)
    {
        <div class="download-panel">
            <i class="bi bi-check-circle-fill"></i>
            <strong>@_resultMessage</strong>
        </div>
    }
</div>

@code {
    private readonly int[] _availableSizes = [16, 32, 48, 64, 128, 256, 512, 1024];
    private HashSet<int> _selectedSizes = [256, 512];
    private string _outputFormat = "png";
    private string _background = "transparent";
    private byte[]? _svgBytes;
    private string? _fileName;
    private bool _rasterizing;
    private string? _error;
    private string? _resultMessage;

    private void ToggleSize(int s, bool on) { if (on) _selectedSizes.Add(s); else _selectedSizes.Remove(s); }

    private async Task PickFile()
    {
        try
        {
            var result = await JS.InvokeAsync<FilePickerResult?>("filePickerOpen", ".svg");
            if (result is null) return;
            _fileName = result.Name;
            _svgBytes = System.Convert.FromBase64String(result.BytesBase64);
            _error = null;
        }
        catch (Exception ex) { _error = $"Could not read file: {ex.Message}"; }
    }

    private async Task Rasterize()
    {
        if (_svgBytes is null || _selectedSizes.Count == 0) return;
        _rasterizing = true; _error = null; _resultMessage = null;
        try
        {
            var baseName = Path.GetFileNameWithoutExtension(_fileName);
            if (_outputFormat == "ico")
            {
                var output = await ImageService.RasterizeSvgAsync(_svgBytes, new RasterizeOptions
                {
                    Sizes = _selectedSizes.OrderBy(s => s).ToArray(),
                    OutputFormat = "ico",
                    Background = _background,
                });
                var saved = await JS.InvokeAsync<string?>("filePickerSave", baseName + ".ico",
                    System.Convert.ToBase64String(output));
                if (saved is not null) _resultMessage = $"Bundled {_selectedSizes.Count} sizes into ICO.";
            }
            else
            {
                // Multi-PNG: one save dialog per size. UX could be improved with folder picker;
                // v1 uses repeated filePickerSave calls, each defaulting to a sensible filename.
                var saved = 0;
                foreach (var size in _selectedSizes.OrderBy(s => s))
                {
                    var output = await ImageService.RasterizeSvgAsync(_svgBytes, new RasterizeOptions
                    {
                        Sizes = [size],
                        OutputFormat = "png",
                        Background = _background,
                    });
                    var name = $"{baseName}-{size}.png";
                    var s = await JS.InvokeAsync<string?>("filePickerSave", name,
                        System.Convert.ToBase64String(output));
                    if (s is not null) saved++;
                }
                _resultMessage = $"Saved {saved} of {_selectedSizes.Count} PNGs.";
            }
        }
        catch (Exception ex) { _error = $"Rasterize failed: {ex.Message}"; }
        finally { _rasterizing = false; }
    }

    private record FilePickerResult(string Name, string BytesBase64);
}
```

Create empty `src/ControlMenu/Modules/Imaging/Pages/SvgRasterize.razor.css`.

- [ ] **Step 2: Add NavEntry**

```csharp
new NavEntry("SVG Rasterize", "/imaging/svg-rasterize", "🎨", 3),
```

- [ ] **Step 3: Build + smoke**

Run. Browse to `/imaging/svg-rasterize`. Rasterize a Bootstrap icon SVG (e.g. `https://icons.getbootstrap.com/assets/icons/heart-fill.svg`) at sizes 64, 128, 256 → multi-PNG. Verify three saves.

Then rasterize same SVG at 32/64/128/256 → single ICO. Verify ICO opens in Explorer.

- [ ] **Step 4: Commit**

```
git add src/ControlMenu/Modules/Imaging
git commit -m "feat(imaging): SvgRasterize page (Svg.Skia + magick)"
```

---

## Phase E — Magic Wand

Goal: implement the most complex tool with in-process Skia preview, magick.exe Apply, and the cross-engine fidelity contract.

### Task E.1: Write WandPreviewRenderer unit tests

**Files:**
- Create: `tests/ControlMenu.Tests/Modules/Imaging/WandPreviewRendererTests.cs`

- [ ] **Step 1: Write tests**

Create:

```csharp
using ControlMenu.Modules.Imaging.Preview;
using SkiaSharp;
using Xunit;

namespace ControlMenu.Tests.Modules.Imaging;

public class WandPreviewRendererTests
{
    private static SKBitmap BuildBitmap(int w, int h, Func<int, int, SKColor> colorAt)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                bmp.SetPixel(x, y, colorAt(x, y));
        return bmp;
    }

    [Fact]
    public void Render_ContiguousMode_ClearsConnectedRegion()
    {
        // 4×4 image: top-left 2×2 white, rest black
        using var src = BuildBitmap(4, 4, (x, y) =>
            (x < 2 && y < 2) ? SKColors.White : SKColors.Black);

        var result = WandPreviewRenderer.Render(src, seedX: 0, seedY: 0, tolerance: 10, contiguous: true);

        Assert.Equal(0, result.GetPixel(0, 0).Alpha);  // cleared
        Assert.Equal(0, result.GetPixel(1, 1).Alpha);  // cleared (contiguous)
        Assert.Equal(255, result.GetPixel(2, 2).Alpha); // not connected
    }

    [Fact]
    public void Render_GlobalMode_ClearsAllMatchingPixelsEvenDisjoint()
    {
        // 4×4 image: corners white, rest black
        using var src = BuildBitmap(4, 4, (x, y) =>
            ((x == 0 || x == 3) && (y == 0 || y == 3)) ? SKColors.White : SKColors.Black);

        var result = WandPreviewRenderer.Render(src, seedX: 0, seedY: 0, tolerance: 10, contiguous: false);

        Assert.Equal(0, result.GetPixel(0, 0).Alpha);  // cleared
        Assert.Equal(0, result.GetPixel(3, 0).Alpha);  // cleared (disjoint, global mode)
        Assert.Equal(0, result.GetPixel(0, 3).Alpha);
        Assert.Equal(0, result.GetPixel(3, 3).Alpha);
        Assert.Equal(255, result.GetPixel(1, 1).Alpha); // black, not matched
    }

    [Fact]
    public void Render_ToleranceZero_ClearsOnlyExactMatches()
    {
        using var src = BuildBitmap(3, 3, (x, y) =>
            (x == 1 && y == 1) ? SKColors.White : new SKColor(254, 254, 254));

        var result = WandPreviewRenderer.Render(src, seedX: 1, seedY: 1, tolerance: 0, contiguous: false);

        Assert.Equal(0, result.GetPixel(1, 1).Alpha);
        Assert.Equal(255, result.GetPixel(0, 0).Alpha);  // off by 1; not matched at tolerance 0
    }

    [Fact]
    public void Render_DoesNotModifySource()
    {
        using var src = BuildBitmap(2, 2, (_, _) => SKColors.White);
        var origPixel = src.GetPixel(0, 0);

        _ = WandPreviewRenderer.Render(src, 0, 0, 50, true);

        Assert.Equal(origPixel, src.GetPixel(0, 0));
    }
}
```

- [ ] **Step 2: Run tests; verify they fail (class doesn't exist yet)**

```
dotnet test --filter "WandPreviewRendererTests"
```

Expected: FAIL with type-not-found.

- [ ] **Step 3: Commit failing tests**

```
git add tests/ControlMenu.Tests/Modules/Imaging/WandPreviewRendererTests.cs
git commit -m "test(imaging): WandPreviewRenderer unit tests (failing)"
```

### Task E.2: Implement WandPreviewRenderer

**Files:**
- Create: `src/ControlMenu/Modules/Imaging/Preview/WandPreviewRenderer.cs`

- [ ] **Step 1: Implement**

Create:

```csharp
using SkiaSharp;

namespace ControlMenu.Modules.Imaging.Preview;

/// <summary>
/// In-process flood-fill for Magic Wand live preview. NOT a substitute for magick.exe —
/// produces a fast, visually-accurate preview that the page reconciles with the
/// authoritative magick render on Apply (before Save). The cross-engine fidelity test
/// (CrossEngineFidelityTests) enforces &gt;99% pixel agreement on synthetic clean-background
/// images, so the preview faithfully predicts the save result.
///
/// Matching metric mirrors magick's -fuzz: Euclidean RGB distance normalized by
/// sqrt(3*255²) ≈ 441.6729559. Tolerance is 0-100 percent; matches occur when
/// distance ≤ tolerance/100.
/// </summary>
public static class WandPreviewRenderer
{
    private const double MaxRgbDistance = 441.6729559300637;  // sqrt(3 * 255²)

    public static SKBitmap Render(SKBitmap source, int seedX, int seedY, int tolerance, bool contiguous)
    {
        if (seedX < 0 || seedX >= source.Width || seedY < 0 || seedY >= source.Height)
            throw new ArgumentOutOfRangeException(nameof(seedX), "seed outside bitmap bounds");

        var w = source.Width;
        var h = source.Height;
        var seedColor = source.GetPixel(seedX, seedY);
        var threshold = tolerance / 100.0;

        var result = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        // Copy source unchanged first
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                result.SetPixel(x, y, source.GetPixel(x, y));

        if (contiguous)
        {
            // Stack-based 4-connectivity flood fill from seed
            var visited = new bool[w, h];
            var stack = new Stack<(int X, int Y)>();
            stack.Push((seedX, seedY));
            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                if (x < 0 || x >= w || y < 0 || y >= h) continue;
                if (visited[x, y]) continue;
                visited[x, y] = true;

                var c = source.GetPixel(x, y);
                if (Distance(c, seedColor) > threshold) continue;

                // Clear alpha
                result.SetPixel(x, y, new SKColor(c.Red, c.Green, c.Blue, 0));
                stack.Push((x + 1, y));
                stack.Push((x - 1, y));
                stack.Push((x, y + 1));
                stack.Push((x, y - 1));
            }
        }
        else
        {
            // Global mode: every matching pixel, anywhere
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var c = source.GetPixel(x, y);
                    if (Distance(c, seedColor) <= threshold)
                        result.SetPixel(x, y, new SKColor(c.Red, c.Green, c.Blue, 0));
                }
            }
        }

        return result;
    }

    private static double Distance(SKColor a, SKColor b)
    {
        double dr = a.Red - b.Red;
        double dg = a.Green - b.Green;
        double db = a.Blue - b.Blue;
        return Math.Sqrt(dr * dr + dg * dg + db * db) / MaxRgbDistance;
    }
}
```

- [ ] **Step 2: Run tests**

```
dotnet test --filter "WandPreviewRendererTests"
```

Expected: all PASS.

- [ ] **Step 3: Commit**

```
git add src/ControlMenu/Modules/Imaging/Preview/WandPreviewRenderer.cs
git commit -m "feat(imaging): WandPreviewRenderer (in-process Skia flood-fill)"
```

### Task E.3: Write RemoveBackgroundAsync tests

**Files:**
- Create: `tests/ControlMenu.Tests/Modules/Imaging/ImageService.BackgroundRemoveTests.cs`

- [ ] **Step 1: Write tests**

```csharp
using ControlMenu.Modules.Imaging.Services.Options;
using Xunit;

namespace ControlMenu.Tests.Modules.Imaging;

[Collection(nameof(ImageServiceCollection))]
public class ImageServiceBackgroundRemoveTests
{
    private readonly ImageServiceFixture _fx;
    public ImageServiceBackgroundRemoveTests(ImageServiceFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task RemoveBackgroundAsync_Contiguous_ClearsConnectedBackground()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick.exe not installed");
        var src = TestImages.CreateLogoOnWhite(256, 256);

        var output = await _fx.Service.RemoveBackgroundAsync(src, new BackgroundRemoveOptions
        {
            SeedX = 5,   // a white pixel
            SeedY = 5,
            Tolerance = 10,
            Contiguous = true,
        });

        // After background removal, corner pixel should have alpha 0
        var pixel = TestImages.GetPixelFromPng(output, 5, 5);
        Assert.Equal(0, pixel.Alpha);

        // Center pixel (logo) should remain opaque
        var center = TestImages.GetPixelFromPng(output, 128, 128);
        Assert.True(center.Alpha > 0);
    }

    [SkippableFact]
    public async Task RemoveBackgroundAsync_Global_ClearsAllMatchingPixels()
    {
        Skip.IfNot(_fx.MagickAvailable, "magick.exe not installed");
        var src = TestImages.CreateLogoWithDisjointWhiteRegions(256, 256);

        var output = await _fx.Service.RemoveBackgroundAsync(src, new BackgroundRemoveOptions
        {
            SeedX = 5,
            SeedY = 5,
            Tolerance = 10,
            Contiguous = false,
        });

        // Both disjoint white regions should be cleared
        Assert.Equal(0, TestImages.GetPixelFromPng(output, 5, 5).Alpha);
        Assert.Equal(0, TestImages.GetPixelFromPng(output, 200, 200).Alpha);
    }
}
```

(Add `CreateLogoOnWhite` and `CreateLogoWithDisjointWhiteRegions` and `GetPixelFromPng` helpers to `TestImages.cs`. They draw a small black square in the center on white; the disjoint variant has two square regions in opposite corners.)

- [ ] **Step 2: Run + verify fail**

```
dotnet test --filter "ImageServiceBackgroundRemoveTests"
```

Expected: FAIL with `NotImplementedException`.

- [ ] **Step 3: Commit failing tests**

```
git add tests/ControlMenu.Tests/Modules/Imaging
git commit -m "test(imaging): RemoveBackgroundAsync tests (failing)"
```

### Task E.4: Implement RemoveBackgroundAsync

**Files:**
- Modify: `src/ControlMenu/Modules/Imaging/Services/ImageService.cs`

- [ ] **Step 1: Implement**

Replace `RemoveBackgroundAsync`:

```csharp
public async Task<byte[]> RemoveBackgroundAsync(byte[] input, BackgroundRemoveOptions options, CancellationToken ct = default)
{
    if (options is null) throw new ArgumentNullException(nameof(options));

    var workDir = CreateWorkDir();
    try
    {
        var inputPath = Path.Combine(workDir, "in.bin");
        var outputPath = Path.Combine(workDir, "out.png");  // always PNG for alpha
        await File.WriteAllBytesAsync(inputPath, input, ct);

        var fuzzPct = options.Tolerance;
        string args;
        if (options.Contiguous)
        {
            // -floodfill +X+Y none with -fuzz N%
            args = $"{LimitFlags} \"{inputPath}\" -alpha set -fuzz {fuzzPct}% -fill none -floodfill +{options.SeedX}+{options.SeedY} none \"{outputPath}\"";
        }
        else
        {
            // -transparent <seed-color> with -fuzz N%
            // We don't know the seed color directly here; ask magick to sample it via
            // a chained operation: read pixel via -format, then re-invoke. Simpler: use
            // the seed pixel's color extracted in C# via SkiaSharp before invoking.
            var seedHex = await SampleSeedHexAsync(input, options.SeedX, options.SeedY, ct);
            args = $"{LimitFlags} \"{inputPath}\" -alpha set -fuzz {fuzzPct}% -transparent \"{seedHex}\" \"{outputPath}\"";
        }

        await InvokeMagickAsync(args, ct);
        return await File.ReadAllBytesAsync(outputPath, ct);
    }
    finally
    {
        try { Directory.Delete(workDir, recursive: true); } catch { }
    }
}

private static async Task<string> SampleSeedHexAsync(byte[] input, int x, int y, CancellationToken ct)
{
    // Decode in-process via SkiaSharp (no extra magick call needed)
    using var ms = new MemoryStream(input);
    using var bmp = SkiaSharp.SKBitmap.Decode(ms);
    if (bmp is null) throw new ImagingException("Could not decode source image for seed sampling");
    var c = bmp.GetPixel(x, y);
    return $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";
}
```

- [ ] **Step 2: Run tests**

```
dotnet test --filter "ImageServiceBackgroundRemoveTests"
```

Expected: PASS.

- [ ] **Step 3: Commit**

```
git add src/ControlMenu/Modules/Imaging/Services/ImageService.cs
git commit -m "feat(imaging): RemoveBackgroundAsync (floodfill + global modes)"
```

### Task E.5: Write cross-engine fidelity test

**Files:**
- Create: `tests/ControlMenu.Tests/Modules/Imaging/CrossEngineFidelityTests.cs`

- [ ] **Step 1: Write the test**

Create:

```csharp
using ControlMenu.Modules.Imaging.Preview;
using ControlMenu.Modules.Imaging.Services.Options;
using SkiaSharp;
using Xunit;

namespace ControlMenu.Tests.Modules.Imaging;

[Collection(nameof(ImageServiceCollection))]
public class CrossEngineFidelityTests
{
    private readonly ImageServiceFixture _fx;
    public CrossEngineFidelityTests(ImageServiceFixture fx) => _fx = fx;

    [SkippableTheory]
    [InlineData(true, 15)]
    [InlineData(true, 30)]
    [InlineData(false, 15)]
    [InlineData(false, 30)]
    public async Task SkiaPreview_AgreesWithMagick_OnSyntheticCleanBackground(bool contiguous, int tolerance)
    {
        Skip.IfNot(_fx.MagickAvailable, "magick.exe not installed");

        // Build a 256×256 source: black filled circle radius 60 at center on white
        using var srcBitmap = TestImages.BuildBlackCircleOnWhite(256, 256, 60);
        var pngBytes = TestImages.EncodeAsPng(srcBitmap);

        // Run Skia preview
        using var skiaResult = WandPreviewRenderer.Render(srcBitmap, seedX: 5, seedY: 5, tolerance, contiguous);

        // Run magick
        var magickPng = await _fx.Service.RemoveBackgroundAsync(pngBytes, new BackgroundRemoveOptions
        {
            SeedX = 5, SeedY = 5, Tolerance = tolerance, Contiguous = contiguous
        });
        using var magickBitmap = TestImages.DecodePng(magickPng);

        var agreement = PixelAlphaAgreement(skiaResult, magickBitmap);
        Assert.True(agreement > 0.99,
            $"Skia↔magick alpha agreement {agreement:P3} (mode={contiguous}, tol={tolerance})");
    }

    private static double PixelAlphaAgreement(SKBitmap a, SKBitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return 0;
        int total = a.Width * a.Height;
        int agree = 0;
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                var ag = a.GetPixel(x, y).Alpha == 0;
                var bg = b.GetPixel(x, y).Alpha == 0;
                if (ag == bg) agree++;
            }
        }
        return (double)agree / total;
    }
}
```

- [ ] **Step 2: Run the test**

```
dotnet test --filter "CrossEngineFidelityTests"
```

Expected: PASS. If FAIL: this is the canary — the `WandPreviewRenderer` matching metric is wrong. Inspect the diff (where do they disagree?) and tune:
- If Skia clears MORE pixels than magick: Skia's tolerance is too loose; check the distance normalization.
- If Skia clears FEWER pixels: too tight.
- If disagreement is at boundary pixels only: 4-connectivity vs other; verify.

Tune `WandPreviewRenderer.Distance` (the normalization factor or the metric itself) until agreement >99% for all four test cases.

- [ ] **Step 3: Commit**

```
git add tests/ControlMenu.Tests/Modules/Imaging/CrossEngineFidelityTests.cs
git commit -m "test(imaging): cross-engine fidelity contract for Magic Wand preview"
```

### Task E.6: Create MagicWand.razor

**Files:**
- Create: `src/ControlMenu/Modules/Imaging/Pages/MagicWand.razor`
- Create: `src/ControlMenu/Modules/Imaging/Pages/MagicWand.razor.css`

- [ ] **Step 1: Author the page**

Create `src/ControlMenu/Modules/Imaging/Pages/MagicWand.razor`:

```razor
@page "/imaging/magic-wand"
@using ControlMenu.Modules.Imaging.Preview
@using ControlMenu.Modules.Imaging.Services
@using ControlMenu.Modules.Imaging.Services.Options
@using SkiaSharp
@inject IImageService ImageService
@inject IJSRuntime JS

<PageTitle>Magic Wand</PageTitle>

<h1><i class="bi bi-magic"></i> Magic Wand</h1>
<p class="page-subtitle">Click on a background color in the preview; adjust tolerance; Apply to commit.</p>

<div class="converter-panel">
    <div class="form-group">
        <label class="form-label">Source Image</label>
        <button class="btn btn-secondary" @onclick="PickFile">
            <i class="bi bi-file-earmark-image"></i> Select Image
        </button>
        @if (!string.IsNullOrEmpty(_fileName)) { <div class="file-info">@_fileName</div> }
    </div>

    @if (_previewDataUrl is not null)
    {
        <div class="preview-container" @onclick="OnPreviewClick" style="position:relative; max-width:600px;">
            <img src="@_previewDataUrl" id="wand-preview" style="display:block; max-width:100%; height:auto;" />
            @if (_seedX is not null)
            {
                <div class="seed-marker" style="position:absolute; left:@(_displaySeedX)px; top:@(_displaySeedY)px;
                     width:10px; height:10px; border:2px solid red; border-radius:50%; transform:translate(-50%,-50%); pointer-events:none;"></div>
            }
        </div>

        <div class="form-group">
            <label class="form-label">Tolerance: @_tolerance%</label>
            <input type="range" min="0" max="100" step="1" @bind="_tolerance" @bind:event="oninput"
                   @bind:after="DebouncedSkiaPreview" />
        </div>

        <div class="form-group">
            <label class="form-label">Mode</label>
            <label class="size-checkbox">
                <input type="radio" name="mode" checked="@_contiguous" @onchange="() => SetMode(true)" /> Contiguous
            </label>
            <label class="size-checkbox">
                <input type="radio" name="mode" checked="@(!_contiguous)" @onchange="() => SetMode(false)" /> Global
            </label>
        </div>

        @if (!string.IsNullOrEmpty(_error)) { <div class="error-panel">@_error</div> }
        @if (_applying) { <div class="status-info"><i class="bi bi-arrow-repeat spin"></i> Applying...</div> }

        <div style="display:flex; gap:0.5rem;">
            <button class="btn btn-secondary" @onclick="Reset" disabled="@(_sourceBytes is null)">
                <i class="bi bi-arrow-counterclockwise"></i> Reset
            </button>
            <button class="btn btn-primary" @onclick="Apply" disabled="@(_seedX is null || _applying)">
                <i class="bi bi-check2"></i> Apply
            </button>
            <button class="btn btn-success" @onclick="Save" disabled="@(!_isAuthoritative)">
                <i class="bi bi-download"></i> Save
            </button>
        </div>
    }
</div>

@code {
    private byte[]? _sourceBytes;
    private string? _fileName;
    private SKBitmap? _sourceBitmap;  // decoded once on pick; reused for preview rendering

    private int? _seedX;    // source-image coords
    private int? _seedY;
    private int _displaySeedX;  // display-pane coords (for the marker)
    private int _displaySeedY;
    private double _displayScale = 1.0;  // source → display ratio

    private int _tolerance = 15;
    private bool _contiguous = true;

    private string? _previewDataUrl;     // current preview content (data: URL)
    private byte[]? _authoritativeBytes; // magick output after Apply
    private bool _isAuthoritative;

    private bool _applying;
    private string? _error;

    private CancellationTokenSource? _debounceCts;

    private async Task PickFile()
    {
        try
        {
            var result = await JS.InvokeAsync<FilePickerResult?>("filePickerOpen", ".png,.jpg,.jpeg,.bmp,.gif,.webp");
            if (result is null) return;
            _fileName = result.Name;
            _sourceBytes = System.Convert.FromBase64String(result.BytesBase64);
            _sourceBitmap?.Dispose();
            using var ms = new MemoryStream(_sourceBytes);
            _sourceBitmap = SKBitmap.Decode(ms);
            _seedX = _seedY = null;
            _isAuthoritative = false;
            _authoritativeBytes = null;
            _previewDataUrl = $"data:image/{Path.GetExtension(_fileName).TrimStart('.')};base64,{result.BytesBase64}";
            _error = null;
        }
        catch (Exception ex) { _error = $"Could not read file: {ex.Message}"; }
    }

    private async Task OnPreviewClick(Microsoft.AspNetCore.Components.Web.MouseEventArgs e)
    {
        if (_sourceBitmap is null) return;
        // OffsetX/Y are display-pane coords; transform back to source-image coords via the natural size
        // We need the display element's natural-vs-rendered size. JS interop:
        var rect = await JS.InvokeAsync<DimRect>("getElementRect", "wand-preview");
        _displayScale = rect.NaturalWidth / rect.RenderedWidth;
        _displaySeedX = (int)e.OffsetX;
        _displaySeedY = (int)e.OffsetY;
        _seedX = (int)(e.OffsetX * _displayScale);
        _seedY = (int)(e.OffsetY * _displayScale);
        _isAuthoritative = false;
        await RenderSkiaPreviewAsync();
    }

    private async Task DebouncedSkiaPreview()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;
        try
        {
            await Task.Delay(100, token);
            if (token.IsCancellationRequested) return;
            _isAuthoritative = false;
            await RenderSkiaPreviewAsync();
        }
        catch (TaskCanceledException) { }
    }

    private async Task RenderSkiaPreviewAsync()
    {
        if (_sourceBitmap is null || _seedX is null || _seedY is null) return;
        try
        {
            using var result = WandPreviewRenderer.Render(_sourceBitmap, _seedX.Value, _seedY.Value, _tolerance, _contiguous);
            using var img = SKImage.FromBitmap(result);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            var bytes = data.ToArray();
            _previewDataUrl = $"data:image/png;base64,{System.Convert.ToBase64String(bytes)}";
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex) { _error = $"Preview failed: {ex.Message}"; }
    }

    private async Task SetMode(bool contiguous)
    {
        _contiguous = contiguous;
        _isAuthoritative = false;
        await RenderSkiaPreviewAsync();
    }

    private async Task Apply()
    {
        if (_sourceBytes is null || _seedX is null || _seedY is null) return;
        _applying = true; _error = null;
        try
        {
            _authoritativeBytes = await ImageService.RemoveBackgroundAsync(_sourceBytes, new BackgroundRemoveOptions
            {
                SeedX = _seedX.Value, SeedY = _seedY.Value, Tolerance = _tolerance, Contiguous = _contiguous
            });
            _previewDataUrl = $"data:image/png;base64,{System.Convert.ToBase64String(_authoritativeBytes)}";
            _isAuthoritative = true;
        }
        catch (Exception ex) { _error = $"Apply failed: {ex.Message}"; }
        finally { _applying = false; }
    }

    private async Task Reset()
    {
        if (_sourceBytes is null) return;
        _seedX = _seedY = null;
        _isAuthoritative = false;
        _authoritativeBytes = null;
        _previewDataUrl = $"data:image/png;base64,{System.Convert.ToBase64String(_sourceBytes)}";
        await Task.CompletedTask;
    }

    private async Task Save()
    {
        if (_authoritativeBytes is null) return;
        var name = Path.GetFileNameWithoutExtension(_fileName) + "-transparent.png";
        await JS.InvokeAsync<string?>("filePickerSave", name, System.Convert.ToBase64String(_authoritativeBytes));
    }

    private record FilePickerResult(string Name, string BytesBase64);
    private record DimRect(double NaturalWidth, double NaturalHeight, double RenderedWidth, double RenderedHeight);
}
```

- [ ] **Step 2: Add JS helper for element size**

In `src/ControlMenu/wwwroot/js/file-picker.js` (or create `magic-wand.js` and reference it from `App.razor`), add:

```javascript
window.getElementRect = function (id) {
    const el = document.getElementById(id);
    if (!el) return null;
    return {
        naturalWidth: el.naturalWidth,
        naturalHeight: el.naturalHeight,
        renderedWidth: el.clientWidth,
        renderedHeight: el.clientHeight
    };
};
```

- [ ] **Step 3: Empty CSS**

Create empty `src/ControlMenu/Modules/Imaging/Pages/MagicWand.razor.css`.

- [ ] **Step 4: Add NavEntry**

```csharp
new NavEntry("Magic Wand", "/imaging/magic-wand", "🪄", 4),
```

- [ ] **Step 5: Build + smoke**

Run. Browse `/imaging/magic-wand`. Pick a logo PNG with a white background. Click on the white area → preview shows transparent corner immediately. Adjust tolerance slider → preview updates as you release. Click Apply → ~300ms wait, preview replaced with magick render. Click Save → file saves as `<basename>-transparent.png`.

Verify: saved file opens correctly with transparency in image viewer.

- [ ] **Step 6: Commit**

```
git add src/ControlMenu/Modules/Imaging src/ControlMenu/wwwroot/js
git commit -m "feat(imaging): MagicWand page with Skia preview + magick Apply"
```

---

## Phase F — Nav promotion + cleanup

### Task F.0: Retrofit 100MB file-size guard across all Imaging pages

**Files:**
- Modify: `src/ControlMenu/Modules/Imaging/Pages/IconConverter.razor`
- Modify: `src/ControlMenu/Modules/Imaging/Pages/FormatConverter.razor`
- Modify: `src/ControlMenu/Modules/Imaging/Pages/ImageResize.razor`
- Modify: `src/ControlMenu/Modules/Imaging/Pages/SvgRasterize.razor`
- Modify: `src/ControlMenu/Modules/Imaging/Pages/MagicWand.razor`

- [ ] **Step 1: Add helper constant + check**

In each page's `PickFile` method, immediately after the bytes are decoded and BEFORE any further work, add:

```csharp
const int MaxFileBytes = 100 * 1024 * 1024;  // 100 MB
if (_fileBytes is { Length: > MaxFileBytes })
{
    _error = $"File too large ({_fileBytes.Length / (1024 * 1024)} MB); maximum is 100 MB.";
    _fileBytes = null;
    _fileName = null;
    return;
}
```

(For `MagicWand.razor` the byte buffer variable is `_sourceBytes`; for `SvgRasterize.razor` it's `_svgBytes`. Adapt the variable name accordingly.)

- [ ] **Step 2: Build**

```
dotnet build src/ControlMenu/ControlMenu.csproj -c Release
```

Expected: success.

- [ ] **Step 3: Commit**

```
git add src/ControlMenu/Modules/Imaging/Pages
git commit -m "feat(imaging): enforce 100 MB page-side upload cap with friendly error"
```

### Task F.1: bUnit smoke test per Imaging page

**Files:**
- Create: `tests/ControlMenu.Tests/Modules/Imaging/Pages/IconConverterPageTests.cs`
- Create: `tests/ControlMenu.Tests/Modules/Imaging/Pages/FormatConverterPageTests.cs`
- Create: `tests/ControlMenu.Tests/Modules/Imaging/Pages/ImageResizePageTests.cs`
- Create: `tests/ControlMenu.Tests/Modules/Imaging/Pages/SvgRasterizePageTests.cs`
- Create: `tests/ControlMenu.Tests/Modules/Imaging/Pages/MagicWandPageTests.cs`

- [ ] **Step 1: Write a representative bUnit test per page**

For each Imaging page, write a single test asserting the page renders and a key control is present. Pattern (apply to all five; this example is for IconConverter):

```csharp
using Bunit;
using ControlMenu.Modules.Imaging.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ControlMenu.Tests.Modules.Imaging.Pages;

public class IconConverterPageTests : TestContext
{
    public IconConverterPageTests()
    {
        Services.AddSingleton<IImageService>(Mock.Of<IImageService>());
        // ... existing bUnit setup helpers from this test project (Env, JS, etc.) ...
    }

    [Fact]
    public void Renders_WithDefaultSizes_AndConvertButtonDisabledWhenNoFile()
    {
        var cut = RenderComponent<ControlMenu.Modules.Imaging.Pages.IconConverter>();
        Assert.Contains("Icon Converter", cut.Markup);
        var btn = cut.Find("button.btn-primary");
        Assert.True(btn.HasAttribute("disabled"));
    }
}
```

Use the same shape for FormatConverter, ImageResize, SvgRasterize, MagicWand — adjust the key control assertion to match what's distinctive on each page (Format dropdown, Mode dropdown, multi-size checkboxes, the preview pane respectively).

- [ ] **Step 2: Run tests**

```
dotnet test --filter "IconConverterPageTests|FormatConverterPageTests|ImageResizePageTests|SvgRasterizePageTests|MagicWandPageTests"
```

Expected: 5 tests PASS.

- [ ] **Step 3: Commit**

```
git add tests/ControlMenu.Tests/Modules/Imaging/Pages
git commit -m "test(imaging): bUnit smoke test per Imaging page"
```

### Task F.2: Verify sidebar nav rendering

**Files:** none (verification)

- [ ] **Step 1: Run + visually inspect sidebar**

Run CM. Confirm sidebar shows:
- Home
- Android Devices
- Jellyfin
- Utilities (with File Unblocker only)
- Cameras
- **Imaging Tools** (with Icon Converter, Format Converter, Image Resize, SVG Rasterize, Magic Wand)
- Settings

If the order is wrong, adjust `SortOrder` values in `ImagingModule.cs` (currently 5; reorder if needed).

### Task F.3: Update CHANGELOG.md

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add Unreleased entries**

In `CHANGELOG.md` under `[Unreleased]`, add:

```markdown
### Added
- New **Imaging Tools** top-level section with five tools:
  - **Icon Converter** — migrated from Utilities; now Lanczos-resampled with BMP-with-AND-mask for ≤48 px entries and PNG for ≥64 px entries (dramatic quality improvement over the prior single-pass SkiaSharp/Mitchell pipeline).
  - **Format Converter** — single image → PNG / JPG / WebP / AVIF / TIFF / HEIC / BMP / GIF with per-format quality controls.
  - **Image Resize** — pixel-dimension / percentage / max-dimension-fit modes; Lanczos filter.
  - **SVG Rasterize** — SVG → one or more PNG sizes or a single ICO bundle; rendering via Svg.Skia.
  - **Magic Wand** — click-to-seed background remover with tolerance slider and contiguous/global mode toggle; in-process Skia flood-fill for instant tolerance preview, explicit Apply step that re-renders authoritatively via magick.exe before Save.
- `magick` (ImageMagick portable Q8 x64) as a new `ModuleDependency` with full dep-manager parity: auto-update check, one-click upgrade, version display under `Settings → Dependencies`.
- `Svg.Skia` NuGet reference for in-process SVG rasterization.
- Cross-engine fidelity test (`CrossEngineFidelityTests`) that pins Skia preview vs magick.exe pixel-agreement >99% on synthetic clean-background images.

### Changed
- Old `/utilities/icon-converter` route now 301-redirects to `/imaging/icon-converter` for backwards compatibility.
- `Utilities` sidebar section keeps File Unblocker; Icon Converter removed from this section.

### Removed
- `IIconConversionService` / `IconConversionService` (SkiaSharp single-pass Mitchell-filter pipeline) — replaced by `IImageService.ConvertToIcoAsync` backed by `magick.exe`.
- `Modules/Utilities/Pages/IconConverter.razor` — superseded by `Modules/Imaging/Pages/IconConverter.razor`.
```

- [ ] **Step 2: Commit**

```
git add CHANGELOG.md
git commit -m "docs(changelog): record Imaging Tools module + magick CLI integration"
```

### Task F.4: Update manual-test-checklist

**Files:**
- Modify: `docs/manual-test-checklist.md`

- [ ] **Step 1: Add new Imaging Tools section**

After the existing `## Section <N>: Utilities` section, insert:

```markdown
## Section <N+1>: Imaging Tools

### Icon Converter (`/imaging/icon-converter`)
- [ ] Page loads; size checkboxes default to 64/128/256
- [ ] Convert 1024×1024 PNG with all 6 sizes selected; output ICO opens cleanly in Explorer
- [ ] Verify hex inspection: 16/32/48 entries have BMP payload (first byte `0x28`); 64/128/256 entries have PNG payload (first byte `0x89`)
- [ ] Old route `/utilities/icon-converter` redirects to new route

### Format Converter (`/imaging/format-converter`)
- [ ] Convert PNG → WebP at quality 85; output is valid WebP
- [ ] Convert PNG → JPG; quality slider visibly affects output file size
- [ ] Convert PNG → HEIC; output opens in Windows Photos

### Image Resize (`/imaging/resize`)
- [ ] By Pixel Dimensions with Lock Aspect on: 1024×1024 → 500×500 produces 500×500
- [ ] By Pixel Dimensions with Lock Aspect off: 1024×768 → 200×200 produces 200×200 (squashed)
- [ ] By Percentage: 1000×500 at 50% → 500×250
- [ ] Max Dimension Fit: 2000×1000 at 500 → 500×250

### SVG Rasterize (`/imaging/svg-rasterize`)
- [ ] Pick Bootstrap heart-fill.svg; rasterize at 64+128+256 → multi-PNG; three saves prompted
- [ ] Same SVG at 32+64+128+256 → single ICO; ICO opens correctly in Explorer
- [ ] Background = transparent vs white — verify behavior

### Magic Wand (`/imaging/magic-wand`)
- [ ] Pick logo PNG with white background; click on white → red seed marker appears at click location
- [ ] Tolerance slider updates preview instantly (~0 latency)
- [ ] Toggle Contiguous ↔ Global → preview reflects change
- [ ] Save button disabled until Apply clicked
- [ ] Apply re-renders via magick (~300ms wait, status spinner visible)
- [ ] After Apply, Save persists `<basename>-transparent.png`; saved file has working transparency

### Settings → Dependencies
- [ ] `magick` row appears alongside `adb`, `scrcpy`, `go2rtc`
- [ ] Version string detected (e.g., "7.1.1-39")
- [ ] "Update available" badge appears if a newer release is on GitHub
```

- [ ] **Step 2: Commit**

```
git add docs/manual-test-checklist.md
git commit -m "docs(manual-checklist): add Imaging Tools section"
```

### Task F.5: Full manual smoke

**Files:** none (verification)

- [ ] **Step 1: Run all tests one more time**

```
dotnet test
```

Expected: all green (existing + new).

- [ ] **Step 2: Run the new Section <N+1> manual checklist**

Walk through every item in `docs/manual-test-checklist.md` Section <N+1>. Note any failures.

- [ ] **Step 3: Address any failures**

Fix and commit per the bug found.

### Task F.6: Merge to master

**Files:** none

- [ ] **Step 1: Push branch**

```
git push -u origin feature/imaging-tools-magick
```

- [ ] **Step 2: Fast-forward merge to master**

```
git checkout master
git merge --ff-only feature/imaging-tools-magick
git push origin master
```

- [ ] **Step 3: Delete feature branch (optional)**

```
git branch -d feature/imaging-tools-magick
git push origin --delete feature/imaging-tools-magick
```

---

## Self-review notes

- **Spec coverage:** every section of the design doc maps to one or more tasks in this plan. Confirmed.
- **Placeholders:** the only `<FILL-IN-AT-IMPLEMENTATION-TIME>` is the SHA-256 in `fetch-magick.ps1`, which has explicit instructions in Task A.3 step 3 for resolving it at implementation time (computed from the first download). Acceptable for a fetch pipeline — every other fetch-*.ps1 in the repo has the same per-version SHA value.
- **Type consistency:** `IImageService` method signatures used identically across the implementation, tests, and Razor pages. `BackgroundRemoveOptions` / `ResizeOptions` / `ConvertFormatOptions` / `RasterizeOptions` / `IcoOptions` consistent throughout.
- **Cross-engine fidelity contract:** load-bearing — Task E.5 explicitly notes the tuning loop if the test fails.
- **Architectural dependencies:** plan front-loads prerequisite verification in Phase 0; if `IDataPathResolver` or `SeedHydrator` aren't on master, the plan halts before any code is written.
