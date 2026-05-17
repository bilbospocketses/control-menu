# Imaging Tools — Tracing Page (vtracer + potrace)

**Date:** 2026-05-17
**Status:** Approved (implementation queued behind Velopack Phase 1 merge to master; lands as Phase G of the existing `feature/velopack-phase-1-hotfix` branch alongside the magick-backed tools)
**Branch (planned):** Phase G of `feature/velopack-phase-1-hotfix` (Item 30's existing branch — not a new branch)

**Companion spec:** `2026-05-15-imaging-tools-magick-design.md` covers the Imaging Tools module foundation, 5 magick-backed tools, and shared infrastructure (`IImageService`, `ImagingModule`, seed pipeline, page conventions, cross-cutting concerns). This spec extends that work with a 6th page — Tracing — that uses two non-magick CLI engines via the same `ModuleDependency` pattern.

---

## 1. Motivation

Raster→vector tracing converts bitmap images into scalable SVG paths. Use cases driving the addition:

- **Logo cleanup** — Take a low-res raster logo and produce a scalable vector version for any output size.
- **Icon vectorization** — Turn a found-online raster icon into an SVG suitable for further editing in svgedit.
- **Line-art digitization** — Scan a B&W sketch and produce smooth Bezier curves.
- **Asset preparation** — Tracing as an upstream step before svgedit cleanup, before SVG-to-multi-PNG rasterization, or before bundling into an icon set.

The Imaging Tools module already plans an SVG Rasterize tool (vector→raster). Tracing closes the loop in the other direction — bitmap→vector — and makes the module a complete two-way raster/vector pipeline.

Two open-source CLI engines together cover the problem space well:

- **[vtracer](https://github.com/visioncortex/vtracer)** (Rust, MIT, ~1 MB binary) — modern hierarchical color tracer. Best fit for color photos, illustrations, multi-color cliparts.
- **[potrace](https://potrace.sourceforge.net/)** (C, GPL v2+, ~150 KB binary) — Peter Selinger's reference Bezier-fit B&W tracer, gold standard since 2001. Inkscape's "Trace Bitmap" wraps it. Best fit for B&W silhouettes, logos, glyphs, line art.

Wrapping both via the existing `ModuleDependency` CLI-subprocess pattern (same shape as adb, scrcpy, magick) keeps the architecture consistent and Local-Dependencies-Only compliant.

---

## 2. Scope

### 2.1 New page in the Imaging Tools sidebar section

One new sub-page added to the `Imaging Tools` section defined in the companion magick spec:

| Page | Route | Engine(s) |
|------|-------|-----------|
| **Tracing** | `/imaging/tracing` | vtracer (color) + potrace (B&W) — single page with engine selector |

Updated Imaging Tools v1 lineup (6 pages, 3 binary deps):

| Page | Route | Engine(s) |
|------|-------|-----------|
| Icon Converter | `/imaging/icon-converter` | magick |
| Format Converter | `/imaging/format-converter` | magick |
| Image Resize | `/imaging/resize` | magick |
| SVG Rasterize | `/imaging/svg-rasterize` | Svg.Skia + magick |
| Magic Wand | `/imaging/magic-wand` | SkiaSharp + magick |
| **Tracing** ← new | `/imaging/tracing` | **vtracer + potrace** |

### 2.2 New binary dependencies

Two `ModuleDependency` entries added to `ImagingModule`:

- **vtracer** — pulled from upstream `visioncortex/vtracer` GitHub releases (already on `UpdateSourceType.GitHub`-compatible distribution).
- **potrace** — pulled from `bilbospocketses/potrace-builds`, a new repo (see Section 6) that builds potrace 1.16 from vendored source for win64 (MinGW64 via MSYS2) and linux64 (gcc via build-essential).

Both deps use the existing `UpdateSourceType.GitHub` source type — **no architectural extensions to `ModuleDependency`.** The previous design draft considered adding `UpdateSourceType.DirectUrl` to fetch potrace directly from SourceForge; that was rejected in favor of the build-our-own approach to keep the dep system uniform.

Both deps appear in `Settings → Dependencies` alongside magick with version display, "Check for update," and one-click upgrade — identical UX to every other module dep.

### 2.3 Out of scope for v1

- Multi-engine simultaneous trace (run both engines and let user compare side-by-side). Could be useful but adds UI complexity; deferred to v2 if user demand surfaces.
- Per-pixel comparison overlay between trace result and original. Visually nice; significant implementation cost. Deferred.
- Custom potrace post-processing (path simplification beyond what potrace's own options offer). Deferred.
- A Rust-native unified vectorization tool combining vtracer and potrace algorithms. Discussed during brainstorm as a future strategic project — explicitly **not** in this work's scope. Captured here for cross-reference: it's a multi-month commitment with GPL viral implications for any potrace-derived port; warrants its own brainstorm + spec + plan cycle when timing makes sense.
- "Open in svgedit" actually opens svgedit. Button ships as a disabled stub in v1 (tooltip explains it activates when svgedit lands as a CM-embedded page). Wires up when [svgedit integration](../../../C:/Users/jscha/.claude/projects/C--Users-jscha/memory/project_svgedit.md) ships per its own roadmap.
- Parameter-aware Quick Preview (scale `filter_speckle` / `turdsize` proportionally on downsampled preview so it matches final output more closely). v1 caption explicitly tells the user "Quick Preview is approximate." Logged as v2 enhancement.
- Saving intermediate states (e.g. "save this parameter combination as a custom preset"). Deferred.
- Batch tracing across multiple images. Module-wide v1 decision per the magick spec — no batch operations in any tool.

---

## 3. Architecture

### 3.1 Engines: vtracer + potrace, both invoked as CLI subprocesses

Both engines plug into the existing `ICommandExecutor.ExecuteResolvedAsync` pattern, resolved through `IDependencyPathResolver`. Identical to how `AdbService` and the magick-backed Item 30 services work. No new infrastructure on Control Menu's side beyond the two `ModuleDependency` entries.

### 3.2 Dep declarations (added to `ImagingModule.cs`)

```csharp
// vtracer — upstream GitHub releases
new ModuleDependency
{
    Name = "vtracer",
    ExecutableName = "vtracer",
    VersionCommand = "vtracer --version",
    VersionPattern = @"vtracer ([\d.]+)",
    SourceType = UpdateSourceType.GitHub,
    GitHubRepo = "visioncortex/vtracer",
    ProjectHomeUrl = "https://github.com/visioncortex/vtracer",
    AssetPattern = OperatingSystem.IsWindows()
        ? @"vtracer-x86_64-pc-windows-msvc\.zip"
        : @"vtracer-x86_64-unknown-linux-musl\.tar\.gz",
    InstallPath = Path.Combine(DepsRoot, "vtracer")
}

// potrace — our own builds repo (see Section 6)
new ModuleDependency
{
    Name = "potrace",
    ExecutableName = "potrace",
    VersionCommand = "potrace --version",
    VersionPattern = @"potrace ([\d.]+)",
    SourceType = UpdateSourceType.GitHub,
    GitHubRepo = "bilbospocketses/potrace-builds",
    ProjectHomeUrl = "https://potrace.sourceforge.net/",
    AssetPattern = OperatingSystem.IsWindows()
        ? @"potrace-[\d.]+-win64\.zip"
        : @"potrace-[\d.]+-linux64\.tar\.gz",
    InstallPath = Path.Combine(DepsRoot, "potrace")
}
```

The magick `ModuleDependency` from the companion spec stays unchanged.

### 3.3 Codebase layout additions

Building on the layout in the companion spec:

```
src/ControlMenu/
  Modules/
    Imaging/
      ImagingModule.cs                        ← extended: +2 ModuleDependency entries, +1 NavEntry
      Pages/
        Tracing.razor                         ← new
        Tracing.razor.css                     ← new
      Services/
        IImageService.cs                      ← extended: +TraceAsync method
        ImageService.cs                       ← extended: +TraceAsync + Build*Args + NormalizeForEngineAsync
        Options/
          TraceOptions.cs                     ← new
          VtracerOptions.cs                   ← new
          PotraceOptions.cs                   ← new
          TraceEngine.cs                      ← new enum
```

### 3.4 `IImageService` extension

```csharp
public interface IImageService
{
    // ... existing 6 methods from Item 30 ...

    Task<byte[]> TraceAsync(
        byte[] input,
        TraceEngine engine,
        TraceOptions options,
        CancellationToken ct = default);
}
```

Options types (`Options/` subdirectory):

```csharp
public enum TraceEngine { Vtracer, Potrace }

public record TraceOptions(
    VtracerOptions? Vtracer = null,
    PotraceOptions? Potrace = null);

public record VtracerOptions(
    VtracerColorMode ColorMode = VtracerColorMode.Color,
    VtracerMode Mode = VtracerMode.Spline,
    string? Preset = "photo",                 // "photo" | "poster" | "bw" | null (uses raw params)
    int FilterSpeckle = 4,
    int ColorPrecision = 6,
    int GradientStep = 16,
    int CornerThreshold = 60,
    int SegmentLength = 4,
    int SpliceThreshold = 45,
    int PathPrecision = 8,
    VtracerHierarchical Hierarchical = VtracerHierarchical.Stacked);

public enum VtracerColorMode { Color, Bw }
public enum VtracerMode { Pixel, Polygon, Spline }
public enum VtracerHierarchical { Stacked, Cutout }

public record PotraceOptions(
    int Turdsize = 2,                         // suppress speckles ≤ N pixels
    double Alphamax = 1.0,                    // corner threshold (0.0-1.3334)
    double OptTolerance = 0.2,                // curve fitting tolerance
    bool LongCurve = true,                    // false = polygon-only output
    int BinarizationThreshold = 50);          // 0-100; passed to magick -threshold pre-step
```

When the `Preset` field on `VtracerOptions` is set, the engine's preset takes precedence over the individual numeric fields (vtracer's CLI honors `--preset` over the per-param flags). When `Preset = null`, the numeric fields are passed verbatim.

### 3.5 `ImageService.TraceAsync` invocation pattern

Mirrors `ConvertFormatAsync` from the companion spec:

```csharp
public async Task<byte[]> TraceAsync(byte[] input, TraceEngine engine, TraceOptions options, CancellationToken ct)
{
    var workDir = Path.Combine(_paths.GetDataRoot(), "temp", "imaging", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(workDir);
    try
    {
        var inPath = await NormalizeForEngineAsync(input, engine, workDir, ct);
        var outPath = Path.Combine(workDir, "out.svg");

        var (exe, args) = engine switch
        {
            TraceEngine.Vtracer => ("vtracer", BuildVtracerArgs(options.Vtracer ?? new(), inPath, outPath)),
            TraceEngine.Potrace => ("potrace", BuildPotraceArgs(options.Potrace ?? new(), inPath, outPath)),
            _ => throw new ArgumentOutOfRangeException(nameof(engine))
        };

        var result = await _executor.ExecuteResolvedAsync(_resolver, "imaging", exe, args, cancellationToken: ct);
        if (result.ExitCode != 0)
            throw new ImagingException($"{exe} failed (exit {result.ExitCode}): {result.StandardError}");

        return await File.ReadAllBytesAsync(outPath, ct);
    }
    finally
    {
        try { Directory.Delete(workDir, recursive: true); } catch { /* swallow */ }
    }
}
```

Per-call working directory under `<dataRoot>/temp/imaging/<guid>/` — guaranteed cleanup in `finally`.

### 3.6 Input normalization (`NormalizeForEngineAsync`)

Both engines have narrow input-format support; magick (already a dep) handles the upgrade-to-uniform-input.

**vtracer:**
- Accepts: PNG, JPG natively.
- If input is detected PNG/JPG, write through as-is to `<workDir>/in.<ext>`.
- Otherwise run `magick -limit memory 512MB <input> <workDir>/in.png` to normalize.

**potrace:**
- Accepts: BMP, PBM, PGM, PPM. Operates on B&W bitmaps.
- Always pre-process: `magick -limit memory 512MB <input> -colorspace Gray -threshold {N}% BMP3:<workDir>/in.bmp` where `N` is `PotraceOptions.BinarizationThreshold` (default 50).
- BMP3 (Microsoft BMP v3) is the variant potrace's own parser prefers.

Magick's `-limit memory 512MB -limit area 16384x16384 -limit map 1GB` resource-limit block from the companion spec applies to all normalization invocations.

### 3.7 Argument construction

**`BuildVtracerArgs`:**

```csharp
private static string BuildVtracerArgs(VtracerOptions o, string inPath, string outPath)
{
    var sb = new StringBuilder();
    sb.Append($"--input \"{inPath}\" --output \"{outPath}\"");
    sb.Append($" --colormode {o.ColorMode.ToString().ToLowerInvariant()}");

    if (!string.IsNullOrEmpty(o.Preset))
    {
        sb.Append($" --preset {o.Preset}");
    }
    else
    {
        sb.Append($" --mode {o.Mode.ToString().ToLowerInvariant()}");
        sb.Append($" --filter_speckle {o.FilterSpeckle}");
        sb.Append($" --color_precision {o.ColorPrecision}");
        sb.Append($" --gradient_step {o.GradientStep}");
        sb.Append($" --corner_threshold {o.CornerThreshold}");
        sb.Append($" --segment_length {o.SegmentLength}");
        sb.Append($" --splice_threshold {o.SpliceThreshold}");
        sb.Append($" --path_precision {o.PathPrecision}");
        sb.Append($" --hierarchical {o.Hierarchical.ToString().ToLowerInvariant()}");
    }
    return sb.ToString();
}
```

**`BuildPotraceArgs`:**

```csharp
private static string BuildPotraceArgs(PotraceOptions o, string inPath, string outPath)
{
    var sb = new StringBuilder();
    sb.Append($"\"{inPath}\" --svg --output \"{outPath}\"");
    sb.Append($" --turdsize {o.Turdsize}");
    sb.Append($" --alphamax {o.Alphamax:0.0##}");
    sb.Append($" --opttolerance {o.OptTolerance:0.0##}");
    if (!o.LongCurve) sb.Append(" --longcurve");  // see note below on flag semantics
    return sb.ToString();
}
```

**Note on `--longcurve`:** potrace's CLI flag `--longcurve` actually means *"turn off curve optimization"* (it produces polygon output). So our `LongCurve = true` default (curves ON) corresponds to *not* passing the flag, and `LongCurve = false` (polygon-only) corresponds to passing it. Counter-intuitive naming preserved here so the user-facing option reads naturally ("long, smooth curves: yes/no"); the inversion is hidden in the arg builder. Implementation MUST pin the exact arg string via unit test against this spec to prevent drift.

### 3.8 Dependency registration

`ImagingModule` is already auto-discovered through the existing `IToolModule` reflection registration per the companion spec. Adding two more `ModuleDependency` entries to its constructor is the only code change needed for dep wiring; nothing in `Program.cs` changes.

---

## 4. Per-engine page specification

### 4.1 Engine selector

Toggle-button group near the top of the page (parallels Image Resize's "Mode" toggles). Single-select. Default: `vtracer`. Brief helper text below:

> vtracer: color photos, illustrations, cliparts. potrace: B&W silhouettes, logos, line art (input is converted to B&W internally).

Switching engines:
- Resets the preset to that engine's default (vtracer → Photo; potrace → Default).
- Collapses the Advanced expander.
- Invalidates the current Quick Preview / Full Trace result with a "Stale — re-trace required" badge on the preview pane.

### 4.2 vtracer presets

Three built-in vtracer CLI presets are exposed in the preset dropdown:

| Preset | CLI flag | Use case |
|--------|----------|----------|
| **Photo** (default) | `--preset photo` | Color photos, photographic illustrations |
| **Poster** | `--preset poster` | High-contrast posterized art, color cliparts |
| **B&W** | `--preset bw` | Black-and-white tracing without per-param tuning |

A fourth dropdown value, **Custom**, suppresses `--preset` from the CLI and enables individual advanced sliders. Default selection when first opening Advanced is whichever preset was active (the slider values are pre-filled with the preset's effective defaults so the user has a sensible starting point).

### 4.3 vtracer advanced controls (collapsed by default)

| Control | Range / values | Default | CLI flag |
|---------|----------------|---------|----------|
| ColorMode | Color / B&W | Color | `--colormode {color,bw}` |
| Mode | Pixel / Polygon / Spline | Spline | `--mode {pixel,polygon,spline}` |
| Filter Speckle | 0-10 | 4 | `--filter_speckle N` |
| Color Precision | 1-8 | 6 | `--color_precision N` |
| Gradient Step | 0-128 | 16 | `--gradient_step N` |
| Corner Threshold | 0-180 | 60 | `--corner_threshold N` |
| Segment Length | 1-10 | 4 | `--segment_length N` |
| Splice Threshold | 0-180 | 45 | `--splice_threshold N` |
| Path Precision | 0-32 | 8 | `--path_precision N` |
| Hierarchical | Stacked / Cutout | Stacked | `--hierarchical {stacked,cutout}` |

### 4.4 potrace presets

| Preset | Parameters (CM-curated) | Use case |
|--------|-------------------------|----------|
| **Default** (default) | turdsize=2, alphamax=1.0, opttolerance=0.2, longcurve=true | potrace's own sensible defaults |
| **Logo Sharp** | turdsize=10, alphamax=0.8, opttolerance=0.5, longcurve=true | Clean logos, sharper corners |
| **Smooth** | turdsize=2, alphamax=1.3334, opttolerance=0.1, longcurve=true | Maximally smooth Bezier output |
| **Polygon-only** | turdsize=2, alphamax=1.0, opttolerance=0.2, longcurve=false | Polygon output (no curves) |

A fifth value, **Custom**, exposes the raw sliders.

### 4.5 potrace advanced controls (collapsed by default)

| Control | Range / values | Default | CLI flag |
|---------|----------------|---------|----------|
| Turdsize | 0-100 | 2 | `--turdsize N` |
| Alphamax | 0.0-1.3334 | 1.0 | `--alphamax F` |
| Opt Tolerance | 0.0-1.0 | 0.2 | `--opttolerance F` |
| Long Curve | toggle | true | absence vs `--longcurve` |
| Binarization Threshold (magick pre-step) | 0-100 | 50 | (not a potrace flag; controls the `magick -threshold N%` pre-conversion) |

The Binarization Threshold slider is the highest-leverage knob for potrace output quality — it controls where the magick pre-step decides "this pixel is black vs white" before potrace sees it. Surfaced as an Advanced control even though it's technically a normalization step, not a potrace flag.

### 4.6 Page layout

```
┌─ Tracing ─────────────────────────────────────────────────┐
│                                                            │
│ [ Pick image... ]   filename.png · 2400×1600 · PNG · 1.2MB │
│                                                            │
│ Engine:  ( vtracer — color/photo )  ( potrace — B&W/logo ) │
│                                                            │
│ Preset:  [ Photo ▾ ]                                       │
│                                                            │
│ ▸ Advanced parameters                                       │
│                                                            │
│ ┌─ Preview ──────────────────────────────────────────────┐ │
│ │                                                          │ │
│ │       (input image OR rendered SVG result here)          │ │
│ │                                                          │ │
│ │   [ Quick Preview ]   [ Trace at full resolution ]       │ │
│ │   ⏵ Cancel  (while in flight)                            │ │
│ │                                                          │ │
│ └──────────────────────────────────────────────────────────┘ │
│                                                              │
│ [ Save as SVG... ]    [ Open in svgedit ⨯ ] (disabled)     │
└──────────────────────────────────────────────────────────────┘
```

### 4.7 Event flow

1. **File pick** → `_sourceBytes` loaded. Preview pane shows input. Engine defaults to vtracer, preset to Photo. No trace yet.
2. **Engine switch / preset change / advanced control change** → flips a "Stale — re-preview or re-trace" badge. Doesn't auto-fire.
3. **Quick Preview click** → page code downsamples `_sourceBytes` to **512 px longest edge** via SkiaSharp (in-process, ~30ms). Calls `TraceAsync` with current engine + options on the downsampled bytes. Renders returned SVG inline with caption "Quick Preview at 512px — final result will differ in detail level."
4. **Trace at full resolution click** → calls `TraceAsync` with `_sourceBytes` at full size. Spinner with elapsed-time counter (typical: 1-10s vtracer color; sub-second for potrace; longer for high-res). Renders returned SVG inline with caption "Final result." **Enables Save as SVG button.**
5. **Cancel** → cancels the in-flight `CancellationTokenSource`. `ICommandExecutor` propagates the cancellation to kill the child subprocess.
6. **Save as SVG** → file-save picker → writes SVG bytes to user-picked path. Default filename: `<input-basename>.svg`.
7. **Open in svgedit** — disabled stub for v1. Tooltip: "Requires svgedit integration (planned per project_svgedit.md)."

### 4.8 Stale-result handling

After Full Trace produces a result, if the user changes any parameter (engine, preset, advanced slider), the rendered preview keeps showing the old result with a **"Stale — parameters changed since trace"** overlay badge on the preview pane (same badge text as §4.7 step 2 uses on parameter change before any trace).

The Save button stays enabled. The displayed SVG bytes are valid and saveable — they just no longer reflect the current parameter state. User can:
- Re-trace to update the preview to reflect new params, then save the updated result.
- Save the current result as-is (accept that it reflects the parameters at trace time, not the current UI state).

This is intentionally more permissive than the Magic Wand pattern's hard `_isAuthoritative = false` re-Apply gate: tracing has no two-engine fidelity contract to enforce, and the user can always tell visually whether the preview matches their intent.

---

## 5. `bilbospocketses/potrace-builds` repository

### 5.1 Repository contents

```
README.md            — Project description, source provenance, build/release process
LICENSE              — GPL v2 or later (matches upstream potrace)
ATTRIBUTION.md       — Peter Selinger credit, source provenance, link to upstream
source/
  potrace-1.16.tar.gz   — vendored upstream source tarball
  SHA256SUMS            — sha256sum of the vendored tarball
.github/
  workflows/
    build.yml          — CI: build win64 + linux64 binaries on tag push
.gitignore           — excludes build artifacts, extracted source trees
```

### 5.2 CI workflow (`.github/workflows/build.yml`)

```yaml
name: Build potrace

on:
  push:
    tags: ["v*"]
  workflow_dispatch:

permissions:
  contents: write

jobs:
  build-linux:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Install build toolchain
        run: sudo apt-get update && sudo apt-get install -y build-essential
      - name: Extract source
        run: |
          mkdir -p build && cd build
          tar -xzf ../source/potrace-1.16.tar.gz
      - name: Configure and build
        working-directory: build/potrace-1.16
        run: |
          ./configure
          make
      - name: Package linux64 asset
        run: |
          mkdir -p artifacts/potrace-1.16-linux64
          cp build/potrace-1.16/src/potrace artifacts/potrace-1.16-linux64/
          cp build/potrace-1.16/COPYING artifacts/potrace-1.16-linux64/LICENSE
          cp ATTRIBUTION.md artifacts/potrace-1.16-linux64/
          tar -czf potrace-1.16-linux64.tar.gz -C artifacts potrace-1.16-linux64
      - uses: actions/upload-artifact@v4
        with:
          name: potrace-1.16-linux64
          path: potrace-1.16-linux64.tar.gz

  build-windows:
    runs-on: windows-latest
    defaults:
      run:
        shell: msys2 {0}
    steps:
      - uses: actions/checkout@v4
      - uses: msys2/setup-msys2@v2
        with:
          msystem: MINGW64
          install: >-
            mingw-w64-x86_64-gcc
            mingw-w64-x86_64-make
            base-devel
            tar
      - name: Extract source
        run: |
          mkdir -p build && cd build
          tar -xzf ../source/potrace-1.16.tar.gz
      - name: Configure and build (MinGW64)
        working-directory: build/potrace-1.16
        run: |
          ./configure --host=x86_64-w64-mingw32
          make
      - name: Package win64 asset
        run: |
          mkdir -p artifacts/potrace-1.16-win64
          cp build/potrace-1.16/src/potrace.exe artifacts/potrace-1.16-win64/
          cp build/potrace-1.16/COPYING artifacts/potrace-1.16-win64/LICENSE
          cp ATTRIBUTION.md artifacts/potrace-1.16-win64/
          # zip from PowerShell because msys2's zip may not be on PATH
          powershell -Command "Compress-Archive -Path artifacts/potrace-1.16-win64/* -DestinationPath potrace-1.16-win64.zip"
      - uses: actions/upload-artifact@v4
        with:
          name: potrace-1.16-win64
          path: potrace-1.16-win64.zip

  release:
    needs: [build-linux, build-windows]
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - uses: actions/download-artifact@v4
        with:
          path: artifacts
      - uses: softprops/action-gh-release@v2
        with:
          files: |
            artifacts/potrace-1.16-linux64/potrace-1.16-linux64.tar.gz
            artifacts/potrace-1.16-win64/potrace-1.16-win64.zip
```

### 5.3 First-release procedure

1. Create the repo `bilbospocketses/potrace-builds`.
2. Download `potrace-1.16.tar.gz` from `potrace.sourceforge.net`, verify sha256.
3. Commit the tarball under `source/` along with `SHA256SUMS`, `README.md`, `LICENSE` (copied from upstream `COPYING`), `ATTRIBUTION.md`, and `.github/workflows/build.yml`.
4. Tag `v1.16` and push.
5. Verify the CI run completes and the GitHub Release for `v1.16` has both asset files attached with expected sha256 hashes.
6. Test fetch from the control-menu side via a one-off run of `fetch-potrace.ps1`.

### 5.4 License compliance

- Both built assets ship with `LICENSE` (GPL v2+ text from upstream `COPYING`) and `ATTRIBUTION.md` (Peter Selinger credit + source provenance).
- Control Menu invokes potrace as a CLI subprocess. This is mere aggregation, not derivative work, so Control Menu's own MIT/personal license is unaffected.
- The repo's `README.md` makes the build/packaging role explicit: "This repository builds and packages potrace 1.16 (Peter Selinger) from vendored upstream source. Source is the unmodified `potrace-1.16.tar.gz` distribution; the binaries are the output of a standard `./configure && make` build."

---

## 6. Cross-cutting concerns

### 6.1 Resource limits and timeouts

- magick normalization steps use the same `-limit memory 512MB -limit area 16384x16384 -limit map 1GB` policy as Item 30's existing tools.
- vtracer and potrace are single-threaded CLIs without external resource-limit flags; both have low memory footprints by design. No extra config.
- Per-trace timeout: 60 seconds wall-clock (sufficient for any reasonable input under the 100 MB upload cap; protects against pathological inputs). Hardcoded for v1; could be lifted into an `appsettings.json` option in a future iteration if real workflows hit the cap.

### 6.2 Logging integration

Per-call stderr handling matches the companion spec's pattern:
- Exit code 0 + empty stderr → silent.
- Exit code 0 + non-empty stderr → log at Warning via Serilog with the stderr text.
- Exit code ≠ 0 → log at Error, throw `ImagingException` with `result.StandardError` exposed.

### 6.3 Warm-up pattern

On `OnInitializedAsync` of `Tracing.razor`:

```csharp
_ = Task.Run(async () =>
{
    try
    {
        await _executor.ExecuteResolvedAsync(_resolver, "imaging", "vtracer", "--version", cancellationToken: CancellationToken.None);
        await _executor.ExecuteResolvedAsync(_resolver, "imaging", "potrace", "--version", cancellationToken: CancellationToken.None);
    }
    catch { /* swallow — best-effort warm-up */ }
});
```

Loads both binaries into the OS file cache. First real trace starts faster.

### 6.4 Temp directory hygiene

Per-call working directories under `<dataRoot>/temp/imaging/<guid>/` (shared with the other Imaging Tools). Cleaned in `finally`.

### 6.5 File size limits at the page boundary

Same 100 MB upload cap as the other Imaging Tools pages. Enforced before invoking the service. The trace timeout (Section 6.1) is the second line of defense for pathological inputs.

### 6.6 Cross-platform readiness

- vtracer ships musl-static Linux binary out of the box — no extra deps on Linux hosts.
- potrace Linux binary built via CI on ubuntu-latest with gcc — statically links what it can; depends on glibc at minimum. Verify Linux portability once the AppImage line of Item 30's Linux port lands.
- `IDataPathResolver` already abstracts win/linux paths.
- `chmod +x` may be required during seed-hydrate on Linux for the unpacked potrace binary (same edge case as magick AppImage).

### 6.7 Velopack packaging impact

New seed pipeline pieces (mirroring `fetch-magick.ps1` from the companion spec):

- `scripts/dependencies/fetch-vtracer.ps1` — pins version, downloads from `visioncortex/vtracer` GitHub release, SHA-256 verify, extracts to `publish/seed/dependencies/vtracer/`.
- `scripts/dependencies/fetch-potrace.ps1` — pins version, downloads from `bilbospocketses/potrace-builds` GitHub release, SHA-256 verify, extracts to `publish/seed/dependencies/potrace/`.
- `scripts/stage-seed.ps1` — extended to aggregate vtracer + potrace alongside magick.
- `release.yml` — `prepare → build-windows` invokes the two new fetch scripts alongside `fetch-magick.ps1`.

No managed code path change beyond the new module additions and service extension. Same `dotnet publish` + `vpk pack` flow.

### 6.8 Settings → Dependencies UI

vtracer and potrace appear alongside magick, adb, scrcpy, etc. — version check, update notification, one-click upgrade. Same pattern as every other binary dep. No code changes to `DependencyManagement.razor`.

---

## 7. Testing strategy

### 7.1 Test categories

| Category | Pattern | Approx count | Notes |
|----------|---------|-------------:|-------|
| `ImageService.TraceAsync` integration | Real vtracer + potrace CLI invocation via dep resolver; `[SkippableFact]` if binaries not installed | ~8 | Round-trip known PNG to SVG via each engine; assert SVG non-empty + parseable XML + `<path>` count > 0; assert error path (exit ≠ 0 by passing malformed input) for each engine |
| `BuildVtracerArgs` / `BuildPotraceArgs` unit | Pure functions; no subprocess; fast | ~6 | Assert exact argument strings for: default options (preset path); custom (raw params with `Preset=null`); B&W variant; potrace default; potrace polygon-only (`LongCurve=false`); potrace edge cases (alphamax at 1.3334 max) |
| `NormalizeForEngineAsync` unit | Pure-ish; uses magick subprocess for non-trivial inputs | ~4 | PNG-through-as-is for vtracer; format conversion (WebP → PNG for vtracer); BMP+gray conversion for potrace; magick failure surfaces as `ImagingException` |
| bUnit page tests | `Tracing.razor` rendering + interaction sanity | 5 | Page renders, engine toggle swaps preset dropdown items, preset change flips Stale badge, Quick Preview button disabled without file loaded, Save button disabled until Full Trace completes |
| **Total new** | | **~23 tests** | Brings Imaging Tools total to ~78-83 tests across the module |

### 7.2 Test isolation

Same convention as the companion spec:
- Every test uses `Path.Combine(Path.GetTempPath(), "CM-Imaging-Tests", Guid.NewGuid().ToString("N"))` as workdir.
- Cleanup in `Dispose`.
- vtracer and potrace resolved via the real `IDependencyPathResolver` in tests — assumes both are staged via seed pipeline or installed for local dev. Tests that hit `DependencyNotInstalledException` at fixture init skip via xUnit `[SkippableFact]`.

### 7.3 What we don't test

- Pixel-perfect output quality from either engine (flaky; not the point — we trust upstream's own test suite).
- Exhaustive preset coverage (one representative per preset; preset semantics are an upstream concern).
- vtracer/potrace internal correctness (delegated to upstream).
- Cross-engine output comparison (no fidelity contract between vtracer and potrace — they're different tools for different jobs).

---

## 8. Migration / rollout plan

### 8.1 Branch strategy

This work bundles into the existing `feature/velopack-phase-1-hotfix` branch as **Phase G**, landing alongside the magick-backed Phases A-F from the companion spec. Single feature branch keeps Imaging Tools v1 atomic.

Prerequisite: Velopack Phase 1 hot-fix VM smoke must clear before the imaging-tools work begins (the architectural foundation — `IDataPathResolver`, `SeedHydrator`, `scripts/dependencies/`, `ControlMenu.Common` — lives only on the hot-fix branch until merged).

### 8.2 Phases (additions to the companion spec's A-F roadmap)

**Phase G.0 — potrace-builds repo (parallel to G.1):**
- Create `bilbospocketses/potrace-builds`.
- Vendor `potrace-1.16.tar.gz` source + SHA256SUMS + README + LICENSE + ATTRIBUTION + build workflow.
- Tag `v1.16`. Verify CI succeeds and both assets attach.
- Test one-off fetch from a dev box to validate the asset URL pattern.
- This phase happens entirely outside the control-menu repo and can run in parallel with G.1.

**Phase G.1 — ImagingModule extension + seed-pipeline wiring:**
- Add vtracer + potrace `ModuleDependency` entries to `ImagingModule.cs`.
- Add `scripts/dependencies/fetch-vtracer.ps1` + `fetch-potrace.ps1` mirroring `fetch-magick.ps1`.
- Extend `scripts/stage-seed.ps1` for vtracer + potrace aggregation.
- Smoke: CM starts; `Settings → Dependencies` lists vtracer + potrace alongside magick; both `vtracer --version` and `potrace --version` resolve through `IDependencyPathResolver`.

**Phase G.2 — Service contract:**
- `IImageService.TraceAsync` interface extension.
- Options types (`TraceOptions`, `VtracerOptions`, `PotraceOptions`, enums) in `Options/` subdirectory.
- `ImageService.BuildVtracerArgs` + `BuildPotraceArgs` implementations.
- Unit tests for arg construction (6 tests).

**Phase G.3 — Service integration:**
- `ImageService.NormalizeForEngineAsync` implementation (PNG/JPG passthrough for vtracer; magick-mediated BMP+gray conversion for potrace).
- `ImageService.TraceAsync` full implementation including timeout + cancellation.
- Integration tests: round-trip PNG → SVG via each engine + error paths (8 tests).
- Normalization unit tests (4 tests).

**Phase G.4 — Tracing page:**
- `Tracing.razor` + `Tracing.razor.css`.
- Engine toggle, preset dropdown, Advanced expander with all per-engine controls, preview pane, Quick Preview + Full Trace buttons, Cancel button (visible only while in flight), Save block.
- Quick Preview downsample logic in the page code (SkiaSharp in-process).
- Stale-result badge handling on parameter changes.
- bUnit page tests (5 tests).

**Phase G.5 — Polish:**
- svgedit-handoff stub button (disabled state with tooltip).
- Warm-up calls on `OnInitializedAsync`.
- Cancellation wiring through `CancellationTokenSource`.
- Logging integration (Warning/Error levels per spec).

**Phase G.6 — Smoke + doc:**
- Manual smoke against representative inputs: color photo via vtracer Photo preset; logo PNG via potrace Default; vtracer in B&W mode for comparison; same logo via potrace Logo Sharp to demonstrate B&W superiority.
- CHANGELOG entry under `### Added` for v1.1.0.
- TECHNICAL_GUIDE: brief Tracing section describing both engines + the engine-selector UX.
- manual-test-checklist.md: new section for Imaging → Tracing (parallel to other Imaging Tool sections).
- Merge Phase G into the hot-fix branch alongside Phases A-F → on to master with the rest of Imaging Tools v1.

**Total Phase G effort estimate: 3-5 days** including the one-time `potrace-builds` CI setup (~1-3 hr of that).

### 8.3 Prerequisite chain

1. Velopack Phase 1 hot-fix VM smoke clears (separate from this work).
2. `feature/velopack-phase-1-hotfix` merges to master.
3. Imaging Tools branch (the same hot-fix branch, with Phases A-G now landed) merges to master as one unit.
4. Subsequent Imaging Tools work (svgedit handoff activation, v2 tracing enhancements) cuts new branches off the post-merge master.

---

## 9. Key decisions log

Decisions captured for provenance — questions reopened later should reference these and explain why circumstances have changed.

| # | Decision | Reason |
|---|----------|--------|
| 1 | Add Tracing as a 6th tool bundled into Item 30 v1, not a follow-on Item 31 | Atomic Imaging Tools v1 launch; reuses the same branch + smoke + CHANGELOG entry; modest scope creep (~3-5 days) |
| 2 | Multi-engine "Tracing" page with engine selector, not separate per-engine pages | Anticipates the natural pairing of B&W tracer (potrace) with color tracer (vtracer); avoids two near-identical pages; engine-selector pattern reusable if a third tracer is ever added |
| 3 | Presets + collapsed Advanced expander, per-engine | Clean default UX (presets are the easy path); power users get full knob access; matches design density of vtracer's parameter surface |
| 4 | Downsampled Quick Preview (512px longest edge) + full-resolution Final Trace | Sub-second iteration during parameter tuning; honest full-resolution authoritative result for save; same engine for both (no preview/save fidelity contract needed) |
| 5 | Inline SVG render + Save + disabled "Open in svgedit" stub button | Save is the v1 sink; svgedit integration is anticipated but not yet present; stubbing the button now gates future activation cleanly |
| 6 | vtracer from upstream `visioncortex/vtracer` GitHub releases | Already fits `UpdateSourceType.GitHub` pattern; well-distributed pre-built binaries; MIT licensed; ~1 MB |
| 7 | potrace from our own `bilbospocketses/potrace-builds` repo (not SourceForge direct fetch) | Keeps every binary dep on `UpdateSourceType.GitHub`; eliminates the ~80-150 LOC `UpdateSourceType.DirectUrl` extension; reproducible builds with modern MinGW64 toolchain; license compliance is self-evident (source IS the repo content) |
| 8 | NOT vendoring potrace binaries directly into control-menu repo | Loses Settings → Dependencies UI visibility; reduces upgrade flexibility; defeats the dep-manager pattern that every other binary uses |
| 9 | NOT porting either engine to Rust for v1 | Multi-month effort per direction; GPL-viral implications for any potrace-derived port; unified Rust tool is a separate strategic project, not coupled to this work |
| 10 | Both engines invoked via CLI subprocess (not via library bindings) | Same rule that drove magick to CLI in the companion spec — independent update cadence, version transparency, no managed/native version-lock — generalized in `feedback_cli_subprocess_over_library_for_deps.md` |
| 11 | potrace input pre-processed via magick to BMP+gray with configurable threshold | potrace's narrow input format support (BMP/PBM/PGM/PPM only) makes pre-conversion mandatory; threshold value is the highest-leverage knob for B&W tracing quality; exposing it gives users meaningful control without a separate "binarize first" UI step |
| 12 | Quick Preview uses naive downsample (no proportional parameter scaling) | Simple v1; honestly captioned ("approximate"); proportional-scaling logic deferred to v2 if user friction surfaces |
| 13 | Custom preset shows in dropdown alongside engine's built-in presets | Discoverable; preset → Custom is a one-click transition; numeric fields pre-fill with the active preset's effective values so the Custom starting point is sensible |

---

## 10. References

- `docs/superpowers/specs/2026-05-15-imaging-tools-magick-design.md` — companion spec; defines the Imaging Tools module foundation, IImageService, ImagingModule, seed pipeline, page conventions, cross-cutting concerns
- `src/ControlMenu/Modules/AndroidDevices/AndroidDevicesModule.cs:15-46` — `ModuleDependency` reference pattern (adb, scrcpy, magick precedent)
- `src/ControlMenu/Services/DependencyPathResolver.cs` — binary resolution pattern
- `src/ControlMenu/Services/ResolvedExecutorExtensions.cs` — `ExecuteResolvedAsync` invocation helper
- `src/ControlMenu.Common/Paths/IDataPathResolver.cs` — writable-state path abstraction (Phase 1 dependency)
- `src/ControlMenu.Common/Seeding/SeedHydrator.cs` — first-launch dep copy pattern (Phase 1 dependency)
- `scripts/dependencies/fetch-magick.ps1` — fetch-script reference pattern (template for fetch-vtracer.ps1 and fetch-potrace.ps1)
- `docs/superpowers/specs/2026-05-09-velopack-packaging-design.md` — packaging architecture this builds atop
- `C:\Users\jscha\.claude\CLAUDE.md` — Local-Dependencies-Only architecture rule (followed by the GitHub-mirror approach for potrace)
- `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/feedback_cli_subprocess_over_library_for_deps.md` — general rule that drives the CLI-subprocess engine pattern for both vtracer and potrace
- `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/feedback_inprocess_preview_authoritative_apply.md` — general pattern referenced by the companion spec's Magic Wand design; Tracing's Quick Preview is a variant (same engine on downsampled input rather than a separate engine in-process)
- `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/project_svgedit.md` — svgedit project context; the "Open in svgedit" disabled stub button anticipates this integration landing
- [vtracer GitHub](https://github.com/visioncortex/vtracer) — upstream source and release distribution for vtracer
- [potrace SourceForge](https://potrace.sourceforge.net/) — upstream source and reference for potrace 1.16
