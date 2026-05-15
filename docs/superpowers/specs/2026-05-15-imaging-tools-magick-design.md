# Imaging Tools — ImageMagick CLI Integration

**Date:** 2026-05-15
**Status:** Approved (implementation queued behind Velopack Phase 1 merge to master)
**Branch (planned):** `feature/imaging-tools-magick` (off post-Phase-1 master)

---

## 1. Motivation

The existing `IconConversionService` (`src/ControlMenu/Modules/Utilities/Services/IconConversionService.cs`) produces visibly poor output when converting a 1024×1024 PNG to ICO. Root causes, in order of severity:

1. **Single-pass downscale at extreme ratios.** Going 1024 → 16 in one Mitchell-Netravali step blurs and wave-aliases the result. ImageMagick and other reference tools cascade or use Lanczos.
2. **Mitchell filter, not Lanczos.** Mitchell trades sharpness for ringing suppression — designed for photos. Lanczos-3 is the icon-friendly choice; SkiaSharp does not ship it.
3. **Premultiplied alpha across resize, no straight-alpha conversion.** Subtle dark fringe on transparent edges (the same class of issue as `reference_magick_png_edge_cleanup.md`).
4. **All entries PNG-encoded.** Windows shell renders small-size (16/32/48) PNG-encoded entries slightly fuzzy; convention is BMP-with-AND-mask for ≤48 px and PNG for ≥64 px.

A free online ICO converter the user tried produced significantly cleaner output. Those services almost universally wrap ImageMagick — that's the engine we want.

This work also lays the foundation for additional image-manipulation features Control Menu is expected to host over time (e.g., to support svgedit integration, format conversion utility, etc.).

---

## 2. Scope

### 2.1 New top-level sidebar section: **Imaging Tools**

Rendered next to Cameras / Android / Utilities; not a sub-section of Utilities.

Five sub-pages in v1:

| Page | Route | Purpose |
|------|-------|---------|
| **Icon Converter** | `/imaging/icon-converter` | Raster image → multi-size ICO with Lanczos resize, BMP+AND-mask entries for ≤48 px, PNG for ≥64 px |
| **Format Converter** | `/imaging/format-converter` | Single image → choose target format from PNG, JPG, WebP, AVIF, TIFF, HEIC, BMP, GIF |
| **Image Resize** | `/imaging/resize` | Single image → resize by pixel dimensions / percentage / max-dimension fit, Lanczos filter |
| **SVG Rasterize** | `/imaging/svg-rasterize` | SVG → render via Svg.Skia → output at one or more PNG sizes, or single ICO bundle |
| **Magic Wand** | `/imaging/magic-wand` | Background-color remover with click-to-seed, tolerance slider, contiguous/global mode toggle, Apply step |

### 2.2 Utilities section keeps

- `File Unblock` and any other non-image tools that exist today
- `/utilities/icon-converter` → 301 redirect to `/imaging/icon-converter` for backwards compatibility

### 2.3 Out of scope for v1

- Multi-seed/additive magic-wand selection (Photoshop Shift+click). Deferred to v2.
- Multi-image batch operations across any tool.
- AI/ML background removal (rembg-style).
- Image diff / perceptual hash / similarity.
- Watermarking, EXIF editing, color profile conversion utilities.
- Animated GIF / APNG authoring or frame extraction.
- HDR / Q16-pipeline imaging (we ship Q8 only).

---

## 3. Architecture

### 3.1 Engine: `magick.exe` CLI subprocess

**Pivot from initial Magick.NET library proposal to CLI subprocess.** Reason: dep-manager parity. Magick.NET (managed C# wrapper) pairs the managed DLL with a matching native DLL at NuGet-package time, version-locked. There is no way to make Magick.NET independently updatable from upstream the way adb/scrcpy are. The CLI binary, on the other hand, slots into the existing `ModuleDependency` pattern exactly.

ImageMagick is invoked via `ICommandExecutor.ExecuteResolvedAsync` (existing helper, used today for adb / scrcpy), which resolves the binary through `IDependencyPathResolver`. Identical pattern to `AdbService`.

### 3.2 Dep declaration

`Modules/Imaging/ImagingModule.cs`:

```csharp
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
```

`magick` appears in `Settings → Dependencies` like every other dep. One-click upgrade. Auto-update notifications. Local-Dependencies-Only compliant in both letter and spirit.

### 3.3 Codebase layout

```
src/ControlMenu/
  Modules/
    Imaging/                            ← new module
      ImagingModule.cs                  ← IToolModule with magick ModuleDependency + 5 NavEntries
      Resources/
        magick-policy.xml               ← format allowlist, copied into magick deps dir at hydrate time
      Pages/
        IconConverter.razor             ← migrated from Modules/Utilities/Pages
        IconConverter.razor.css
        FormatConverter.razor
        FormatConverter.razor.css
        ImageResize.razor
        ImageResize.razor.css
        SvgRasterize.razor
        SvgRasterize.razor.css
        MagicWand.razor
        MagicWand.razor.css
      Services/
        IImageService.cs                ← single interface, granular methods
        ImageService.cs                 ← CLI wrapper using ICommandExecutor
        Options/
          ConvertFormatOptions.cs
          ResizeOptions.cs
          IcoOptions.cs
          BackgroundRemoveOptions.cs
          RasterizeOptions.cs
        ImageInfo.cs
        ImagingException.cs
      Preview/
        WandPreviewRenderer.cs          ← SkiaSharp flood-fill, ~40 LOC, preview ONLY

  Modules/Utilities/                    ← keeps non-imaging tools
    Pages/FileUnblock.razor             ← stays
    Services/                           ← IconConversionService DELETED
```

### 3.4 `IImageService` API

```csharp
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

All methods are bytes-in / bytes-out so the same service supports browser File-System-Access-API mode (picker bytes) and on-disk path mode (read → service → write).

### 3.5 `ImageService` invocation pattern

Every method follows the same shape:

```csharp
public async Task<byte[]> ConvertFormatAsync(byte[] input, string targetFormat, ConvertFormatOptions? opts, CancellationToken ct)
{
    var workDir = Path.Combine(_paths.GetDataRoot(), "temp", "imaging", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(workDir);
    try
    {
        var inputPath  = Path.Combine(workDir, $"in{DetectExtension(input)}");
        var outputPath = Path.Combine(workDir, $"out.{targetFormat.ToLowerInvariant()}");
        await File.WriteAllBytesAsync(inputPath, input, ct);

        var args = $"-limit memory 512MB -limit area 16384x16384 \"{inputPath}\" {BuildOpArgs(opts)} \"{outputPath}\"";
        var result = await _executor.ExecuteResolvedAsync(_resolver, "imaging", "magick", args, cancellationToken: ct);

        if (result.ExitCode != 0)
            throw new ImagingException($"magick failed: {result.StandardError}");

        return await File.ReadAllBytesAsync(outputPath, ct);
    }
    finally
    {
        try { Directory.Delete(workDir, recursive: true); } catch { /* swallow cleanup errors */ }
    }
}
```

Per-call working directory under `<dataRoot>/temp/imaging/<guid>/` — guaranteed cleanup in `finally`.

### 3.6 Svg.Skia integration

Pure managed NuGet (~1 MB). Used only by `RasterizeSvgAsync`:

```csharp
public async Task<byte[]> RasterizeSvgAsync(byte[] svgBytes, RasterizeOptions opts, CancellationToken ct)
{
    using var svgStream = new MemoryStream(svgBytes);
    using var svg = new Svg.Skia.SKSvg();
    svg.Load(svgStream);
    // Render to SKBitmap at requested size, encode as PNG to a temp file,
    // then hand to magick.exe for final encoding (ICO / multi-size PNG bundle).
}
```

No init step required. Svg.Skia is fully managed; works on Windows and Linux without extra deps.

### 3.7 Dependency registration

`Program.cs`:
```csharp
builder.Services.AddSingleton<IImageService, ImageService>();
```

`ImagingModule` is auto-discovered through the existing `IToolModule` reflection-based registration; nothing manual.

---

## 4. Per-tool specifications

### 4.1 Icon Converter (migration)

- **Input:** unchanged — File System Access API or typed path.
- **Sizes UI:** unchanged — checkboxes for 16 / 32 / 48 / 64 / 128 / 256; defaults 64 / 128 / 256.
- **Internals:** `IImageService.ConvertToIcoAsync` invokes `magick <input> -define icon:auto-resize=<sizes-csv> <output>`. The `icon:auto-resize` define automatically applies Lanczos resize per size and picks BMP-with-AND-mask for ≤48 px and PNG for ≥256 px. Manual mixed-encoding code from the current implementation is removed.
- **Routing:** new canonical `/imaging/icon-converter`; old `/utilities/icon-converter` → redirect.
- **Tests:** existing `IconConversionServiceTests` move to `Tests/Modules/Imaging/ImageService.IconTests.cs`; structural ICO assertions stay; one new test asserts BMP-encoded ≤48 px entries vs PNG-encoded ≥64 px entries.

### 4.2 Format Converter

- **Input:** file pick.
- **Display:** detected source format + dimensions + file size.
- **Target format dropdown:** PNG, JPG, WebP, AVIF, TIFF, HEIC, BMP, GIF.
- **Per-format options (conditional):**
  - JPG / WebP / AVIF: **Quality slider** (0-100, default 90).
  - PNG / HEIC / TIFF / BMP / GIF: no options in v1.
- **Output:** same basename + new extension.
- **Service call:** `ConvertFormatAsync(bytes, "webp", new ConvertFormatOptions { Quality = 90 })`.

**HEIC note:** Magick portable Q8 builds for Windows ship with libheif/libde265 for HEIC encoding. Patent considerations apply to commercial redistribution; for personal/internal use the dropdown stays. Flag tracked but kept in v1 per user direction.

### 4.3 Image Resize

- **Input:** file pick.
- **Display:** source dimensions.
- **Mode dropdown:**
  1. **By Pixel Dimensions** — two inputs (W, H) + "Lock aspect ratio" toggle (default on).
  2. **By Percentage** — single input applies to both dims.
  3. **Max Dimension Fit** — single input; longest side becomes N, aspect preserved.
- **Filter:** Lanczos (hardcoded; no UI exposure in v1).
- **Output:** same basename + `-{newW}x{newH}` suffix + same extension as source.
- **Service call:** `ResizeAsync(bytes, new ResizeOptions { Width, Height, Mode = ResizeMode.PixelDimensions })`.

### 4.4 SVG Rasterize

- **Input:** SVG file pick.
- **Output sizes:** multi-select checkboxes for 16 / 32 / 48 / 64 / 128 / 256 / 512 / 1024; defaults 256 + 512.
- **Output format toggle:**
  - **Multiple PNGs** (default) — folder picker once, then writes `<basename>-<size>.png` per selected size.
  - **Single ICO** — bundles all selected sizes into one .ico via the same `icon:auto-resize` path.
- **Background:** Transparent (default) / Color picker. Solid background flattens onto chosen color.
- **Service call:** `RasterizeSvgAsync(svgBytes, new RasterizeOptions { Sizes = [256, 512], OutputFormat = "png", Background = "transparent" })`.
- **Pipeline:** Svg.Skia parses SVG → renders to `SKBitmap` at each size → writes a temp PNG per size → hands paths to magick for final encoding (ICO mode) or copies/saves to user target (multi-PNG mode).

### 4.5 Magic Wand (background remove)

The most complex of the five.

**Page state:**
- `_sourceBytes` — original picked image.
- `_seedPoint` — (X, Y) in **source-image coordinates** (transformed from preview-pane click coords).
- `_tolerance` — 0-100 slider value, default 15.
- `_contiguous` — toggle, default `true`.
- `_previewBytes` — current preview content (Skia preview OR magick result, depending on stage).
- `_isAuthoritative` — `false` until user clicks Apply; gates the Save button.

**Flow:**
1. User picks file → `_sourceBytes` loaded → displayed in preview pane.
2. User clicks pixel in preview → click coords transformed to source-image coords via the preview pane's render scale → `_seedPoint` stored → preview re-renders via `WandPreviewRenderer.Render(...)` (instant, in-process Skia).
3. User drags tolerance slider → 100 ms debounce → preview re-renders via Skia (instant).
4. User toggles contiguous/global → preview re-renders via Skia.
5. User clicks **Apply** → preview re-renders via `IImageService.RemoveBackgroundAsync(...)` at full resolution (~200-400 ms with warm-up). `_isAuthoritative = true`.
6. User clicks **Save** (only enabled when `_isAuthoritative == true`) → writes `_previewBytes` to output file as `<basename>-transparent.png` (output is always PNG since alpha is required regardless of source format).

If the user changes tolerance/mode/seed after Apply, `_isAuthoritative` flips back to `false` and Save disables — forces an explicit re-Apply before commit.

**`WandPreviewRenderer` algorithm:**
- Take `SKBitmap` source, seed point, tolerance (0-100 → 0.0-1.0 normalized), contiguous flag.
- Sample seed color (single pixel read at `(seedX, seedY)`).
- **Match metric:** Euclidean RGB distance, normalized by `sqrt(3*255²) = 441.6729...`. Threshold = tolerance/100.
- **Contiguous mode:** stack-based flood-fill with 4-connectivity from seed; mark visited pixels as alpha=0 on match.
- **Global mode:** scan every pixel; mark alpha=0 on match without considering connectivity.
- Encode result as PNG → return bytes.

**Cross-engine fidelity contract:** the Skia preview MUST agree with magick.exe `-floodfill` / `-fuzz/-transparent` on >99% of pixels for synthetic clean-background test images. Enforced via the cross-engine fidelity test (Section 6).

**Service call:** `RemoveBackgroundAsync(bytes, new BackgroundRemoveOptions { SeedX, SeedY, Tolerance, Contiguous = true })`. CLI form: `magick <input> -fuzz N% -floodfill +X+Y none <output>` (contiguous) or `magick <input> -fuzz N% -transparent <hex-seed-color> <output>` (global).

---

## 5. Cross-cutting concerns

### 5.1 Resource limits (per-invocation)

Every `magick.exe` invocation prepends:
```
-limit memory 512MB -limit area 16384x16384 -limit map 1GB
```

Prevents a pathological input from OOMing the host. Tunable later if real workflows require more.

### 5.2 Format allowlist (custom policy.xml)

Magick portable's default `policy.xml` enables all formats. We override via a custom `magick-policy.xml` resource shipped at `src/ControlMenu/Modules/Imaging/Resources/magick-policy.xml`, copied next to the magick.exe at first launch by the seed-hydrator pattern.

`ImageService` sets the `MAGICK_CONFIGURE_PATH` environment variable on every invocation pointing at the deps directory containing the override.

**Allowed formats (v1):** PNG, JPG/JPEG, WebP, AVIF, TIFF, HEIC, BMP, GIF, ICO, SVG (read-only — actual rasterize is Svg.Skia).
**Denied:** everything else, including known-CVE-historical formats (MVG/MSL, XBM).

### 5.3 Logging integration

Per-call parsing of magick's stderr:
- Exit code 0 + empty stderr → silent.
- Exit code 0 + non-empty stderr → log at Warning level via Serilog.
- Exit code ≠ 0 → log at Error level, throw `ImagingException` with the stderr text exposed.

### 5.4 Warm-up pattern

On `OnInitializedAsync` of any Imaging page, fire-and-forget:
```csharp
_ = Task.Run(async () =>
{
    try { await _executor.ExecuteResolvedAsync(_resolver, "imaging", "magick", "--version", cancellationToken: CancellationToken.None); }
    catch { /* swallow — best-effort warm-up */ }
});
```

Loads `magick.exe` + DLLs into the OS file cache. Next real call starts in ~50-80 ms instead of ~150-200 ms cold.

### 5.5 Temp directory hygiene

- Per-call working directories under `<dataRoot>/temp/imaging/<guid>/`, cleaned in `finally` blocks.
- Per-page `/temp/` web-copy pattern (existing — auto-delete after 5 min) reused for download-copy links on every tool.
- Magick's own scratch via `MAGICK_TMPDIR` env var pointing at `<dataRoot>/temp/imaging/magick-scratch/`.

### 5.6 File size limits at the page boundary

Pages enforce a 100 MB upload cap before invoking the service. Magick's `-limit` flags are the second line of defense.

### 5.7 Cross-platform readiness (Linux port)

- `IDataPathResolver` already abstracts win/linux path differences.
- `AssetPattern` in the `ModuleDependency` already conditionals on `OperatingSystem.IsWindows()`.
- `Svg.Skia` is fully managed; no native deps.
- `magick` on Linux is the AppImage; same dep-resolver pattern applies — `chmod +x` may be needed during seed-hydrate on Linux (added in the `SeedHydrator` extension).

### 5.8 Velopack packaging impact

New seed pipeline pieces:
- `scripts/dependencies/fetch-magick.ps1` — pins version, downloads from ImageMagick GitHub release, SHA-256 verify, extracts the portable ZIP to `publish/seed/dependencies/magick/`.
- `scripts/stage-seed.ps1` — extended to include magick aggregation.
- `magick-policy.xml` copied alongside via the same script.
- `release.yml` — `prepare → build-windows` invokes `fetch-magick.ps1` alongside existing fetches.

No managed code path change beyond the new module. Same `dotnet publish` + `vpk pack` flow.

### 5.9 Settings → Dependencies UI

`magick` appears alongside adb / scrcpy / go2rtc — version check, update notification, one-click upgrade. Same pattern as every other binary dep.

`Svg.Skia` does NOT appear there — it's a managed NuGet, pinned at csproj build time. Out of scope for the dep manager UI (which is binary-mode only).

---

## 6. Testing strategy

### 6.1 Test categories

| Category | Pattern | Approx count | Notes |
|----------|---------|-------------:|-------|
| `ImageService` integration | Real `magick.exe` invocation via dep resolver; `[SkippableFact]` if magick not installed | ~15 | Format round-trips, resize dimensions, ICO structural validity, alpha-channel changes on background remove, SVG raster produces non-empty output, info parsing |
| `WandPreviewRenderer` unit | Pure SkiaSharp, no magick.exe; fast | ~8 | Seed sampling, tolerance threshold edges, 4-connectivity vs global, coord-transform correctness |
| Cross-engine fidelity | Synthetic test images; run both Skia preview AND magick.exe; assert >99% pixel agreement | 3-5 | **Load-bearing for the two-engine Magic Wand promise** |
| bUnit page tests | Rendering + key controls + interaction sanity | ~25 (5 per page × 5 pages) | Same pattern as existing component tests |
| Migrated `IconConversionServiceTests` | Existing 6 byte-level structural tests, adjusted for BMP/PNG-mix output | 6 | Most assertions reused; new one asserts ≤48 px = BMP, ≥64 px = PNG |
| **Total new** | | **~55-60 tests** | |

### 6.2 Test isolation

- Every test uses `Path.Combine(Path.GetTempPath(), "CM-Imaging-Tests", Guid.NewGuid().ToString("N"))` as workdir.
- Cleanup in `Dispose`.
- Magick resolved via the real `IDependencyPathResolver` in tests — assumes magick is staged via seed pipeline or installed for local dev. Tests that hit `DependencyNotInstalledException` at fixture init skip (xUnit `[SkippableFact]`).

### 6.3 Cross-engine fidelity test (the contract)

```csharp
[Fact]
public async Task WandPreview_AgreesWithMagickFloodfill_OnSyntheticImage()
{
    var source = BuildSyntheticImage(256, 256);                    // black square on white
    var seed = new SKPoint(10, 10);                                 // a white pixel
    const int tolerance = 15;

    var skiaResult = WandPreviewRenderer.Render(source, seed, tolerance, contiguous: true);

    var magickResult = await imageService.RemoveBackgroundAsync(
        sourceBytes,
        new BackgroundRemoveOptions { SeedX = 10, SeedY = 10, Tolerance = 15, Contiguous = true });

    var agreement = ComparePixelAgreement(skiaResult, magickResult);
    Assert.True(agreement > 0.99, $"Pixel agreement was {agreement:P2}, expected >99%");
}
```

If this test ever drops below 99% on clean-background synthetic inputs, it's a bug in `WandPreviewRenderer`'s tolerance formula and must be tuned until passing. This test is the safety net that makes the two-engine Magic Wand pattern honest — without it, preview/save drift would silently degrade trust.

### 6.4 What we don't test

- Pixel-perfect output quality (flaky; not the point).
- Exhaustive format coverage (we trust magick's own test suite).
- GPU rendering paths (we don't have them).

---

## 7. Migration / rollout plan

### 7.1 Branch strategy

Single feature branch `feature/imaging-tools-magick` off `master` **after** `feature/velopack-phase-1-hotfix` lands. Phases A through F land as one or more commits within that branch. Manual smoke at each phase boundary; final merge to master only after Phase F + full manual run.

If the branch grows too large for one review surface, split into three:
- A + B (foundation + icon)
- C + D (format / resize / SVG)
- E + F (wand + nav promotion)

### 7.2 Phases

**Phase A — Foundation (no user-visible changes):**
- `scripts/dependencies/fetch-magick.ps1` (pinned Magick portable Q8 x64 ZIP, SHA-256 verify, extract).
- Extend `scripts/stage-seed.ps1` for magick aggregation + `magick-policy.xml` copy.
- Add `Svg.Skia` NuGet ref to `ControlMenu.csproj`.
- Create `Modules/Imaging/ImagingModule.cs` with the `magick` `ModuleDependency`.
- Auto-discovered via existing `IToolModule` registration.
- Smoke gate: CM starts; `Settings → Dependencies` lists magick; `magick --version` resolves through `IDependencyPathResolver`.

**Phase B — `IImageService` + Icon Converter migration:**
- Implement `IImageService` skeleton with `ConvertToIcoAsync` + `GetInfoAsync`.
- Move `IconConverter.razor` to `Modules/Imaging/Pages/`; rewire to `IImageService`.
- Delete `Modules/Utilities/Services/IconConversionService.cs` (SkiaSharp original).
- Add redirect for the old `/utilities/icon-converter` route.
- Adjust existing `IconConversionServiceTests` to the new location + BMP/PNG-mix structural assertion.
- Smoke gate: convert a 1024×1024 PNG, verify result in Windows Explorer at every selected size.

**Phase C — Format Converter + Image Resize:**
- Add `ConvertFormatAsync` + `ResizeAsync` to `ImageService`.
- Build `FormatConverter.razor` + `ImageResize.razor`.
- Tests + manual smoke (round-trip a PNG through each output format).

**Phase D — SVG Rasterize:**
- Add `RasterizeSvgAsync` (Svg.Skia + magick).
- Build `SvgRasterize.razor` with multi-PNG / single-ICO toggle + folder picker.
- Smoke against known SVGs (Bootstrap icons, GTV Streamer logo).

**Phase E — Magic Wand:**
- Implement `WandPreviewRenderer` (Skia flood-fill).
- Add `RemoveBackgroundAsync` to `ImageService`.
- Build `MagicWand.razor` with click-to-seed, tolerance slider, mode toggle, Apply button, `_isAuthoritative` gate on Save.
- Cross-engine fidelity tests.
- Smoke: remove white background from a logo PNG, verify save matches Apply preview.

**Phase F — Nav promotion + cleanup:**
- Promote "Imaging Tools" to top-level sidebar section.
- Old `/utilities/icon-converter` redirect verified.
- Manual end-to-end nav flow + all 5 tools.

### 7.3 Prerequisite

This work is **queued behind Velopack Phase 1 hot-fix merging to master.** Currently:
- `feature/velopack-phase-1-hotfix` is at v1.1.0-beta.3, 31 commits ahead of master.
- Awaiting fresh-VM smoke per `todo_control_menu.md` next-session resume banner.
- The architectural foundation imaging builds on (`IDataPathResolver`, `SeedHydrator`, `scripts/dependencies/`, `ControlMenu.Common`) lives only on that branch.

Implementation start gated on: smoke pass → hot-fix merge to master → branch off the new master.

---

## 8. Key decisions log

Decisions captured for provenance — questions reopened later should reference these and explain why circumstances have changed.

| # | Decision | Reason |
|---|----------|--------|
| 1 | Pivot from Magick.NET library to `magick.exe` CLI subprocess | Only path to true dep-manager parity with adb / scrcpy. Managed/native version-locking of Magick.NET prevents independent updates |
| 2 | AnyCPU NuGet rejected upfront for AnyCPU footprint (~25 MB) — but moot after CLI pivot | Original objection was AnyCPU ships all platforms' natives. CLI sidesteps the whole question |
| 3 | Build a general `IImageService` foundation now, not just an icon-converter fix | User explicitly anticipates additional image features (format conversion, SVG raster from svgedit, etc.) |
| 4 | Five tools in v1 | Icon Converter (trigger), Format Converter, Image Resize, SVG Rasterize, Magic Wand background remove. User-selected |
| 5 | Magic Wand supports both contiguous (default) and global modes | Both serve real cases; ~5 LOC delta |
| 6 | SVG Rasterize uses Svg.Skia, not Magick MSVG and not librsvg | Skia is already in CM; Svg.Skia is small (~1 MB managed); librsvg bundle is 60-80 MB cross-platform for marginal fidelity gain on svgedit-typical output |
| 7 | HEIC stays in v1 dropdown despite patent considerations | Personal-use project; user-affirmed |
| 8 | "Imaging Tools" promoted to top-level sidebar section, not nested under Utilities | Imaging is a distinct enough domain; 5 tools is too many to nest |
| 9 | Magic Wand uses Path B (Skia preview + magick.exe save) with explicit Apply step | Best UX (instant preview during exploration) + authoritative magick result before commit; cross-engine fidelity test enforces preview/save agreement |
| 10 | Cross-engine fidelity contract: >99% pixel agreement on synthetic clean-background images | Load-bearing for the two-engine pattern's honesty |
| 11 | Implementation queued behind Velopack Phase 1 merge | Required architectural pieces live only on the hot-fix branch today |

---

## 9. References

- `src/ControlMenu/Modules/Utilities/Services/IconConversionService.cs` — current SkiaSharp implementation being replaced.
- `src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs:15-46` — `ModuleDependency` reference pattern (adb, scrcpy).
- `src/ControlMenu/Services/DependencyPathResolver.cs` — binary resolution pattern.
- `src/ControlMenu/Services/ResolvedExecutorExtensions.cs` — `ExecuteResolvedAsync` invocation helper.
- `src/ControlMenu.Common/Paths/IDataPathResolver.cs` — writable-state path abstraction (Phase 1 dependency).
- `src/ControlMenu.Common/Seeding/SeedHydrator.cs` — first-launch dep copy pattern (Phase 1 dependency).
- `scripts/dependencies/fetch-adb.ps1` — fetch-script reference pattern.
- `docs/superpowers/specs/2026-05-09-velopack-packaging-design.md` — packaging architecture this builds atop.
- `reference_magick_png_edge_cleanup.md` — historical edge-fringe context.
- `C:\Users\jscha\.claude\CLAUDE.md` — Local-Dependencies-Only architecture rule.
