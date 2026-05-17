# Imaging Tools — Tracing Page (vtracer + potrace) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Tracing page to the Imaging Tools module hosting two raster-to-vector engines (vtracer for color, potrace for B&W) wired through Control Menu's existing `ModuleDependency` + `ICommandExecutor` pattern.

**Architecture:** Phase G of the Item 30 Imaging Tools work, landing on `feature/velopack-phase-1-hotfix` after Phases A-F (magick-backed tools) complete. Two new CLI subprocess engines, both pulled via `UpdateSourceType.GitHub` — vtracer from upstream `visioncortex/vtracer`, potrace from a new `bilbospocketses/potrace-builds` repo that builds 1.16 source via MSYS2/MinGW64 (Windows) and gcc (Linux) in CI.

**Tech Stack:** Blazor Server (.NET 10), xUnit + bUnit, SkiaSharp for in-process downsample, magick.exe for input normalization, vtracer + potrace as new external CLI deps. PowerShell + GitHub Actions for the potrace-builds CI workflow.

**Spec:** `docs/superpowers/specs/2026-05-17-imaging-tools-tracing-design.md` (commit `8ae94e1`).

**Prerequisite:** Phases A-F of the Item 30 magick plan (`docs/superpowers/plans/2026-05-15-imaging-tools-magick.md`, commit `fd1644d`) must be complete. This plan extends `ImagingModule`, `IImageService`, `ImageService`, and the Imaging Tools sidebar section — all created by that earlier work.

---

## File Structure

### New files (created by this plan)

**External repo (`bilbospocketses/potrace-builds`):**
- `README.md` — project description + provenance
- `LICENSE` — GPL v2+ (copy of upstream `COPYING`)
- `ATTRIBUTION.md` — Peter Selinger credit
- `source/potrace-1.16.tar.gz` — vendored upstream source
- `source/SHA256SUMS` — sha256 of vendored tarball
- `.github/workflows/build.yml` — CI build workflow
- `.gitignore` — excludes build artifacts

**control-menu repo (this branch):**
- `src/ControlMenu/Modules/Imaging/Pages/Tracing.razor` — page
- `src/ControlMenu/Modules/Imaging/Pages/Tracing.razor.css` — scoped styles
- `src/ControlMenu/Modules/Imaging/Services/Options/TraceEngine.cs` — enum
- `src/ControlMenu/Modules/Imaging/Services/Options/TraceOptions.cs` — record
- `src/ControlMenu/Modules/Imaging/Services/Options/VtracerOptions.cs` — record + enums
- `src/ControlMenu/Modules/Imaging/Services/Options/PotraceOptions.cs` — record
- `scripts/dependencies/fetch-vtracer.ps1` — seed fetch script
- `scripts/dependencies/fetch-potrace.ps1` — seed fetch script
- `tests/ControlMenu.Tests/Modules/Imaging/Services/BuildVtracerArgsTests.cs`
- `tests/ControlMenu.Tests/Modules/Imaging/Services/BuildPotraceArgsTests.cs`
- `tests/ControlMenu.Tests/Modules/Imaging/Services/NormalizeForEngineAsyncTests.cs`
- `tests/ControlMenu.Tests/Modules/Imaging/Services/ImageServiceTraceTests.cs`
- `tests/ControlMenu.Tests/Modules/Imaging/Pages/TracingPageTests.cs`

### Modified files

- `src/ControlMenu/Modules/Imaging/ImagingModule.cs` — add 2 ModuleDependency entries + 1 NavEntry
- `src/ControlMenu/Modules/Imaging/Services/IImageService.cs` — add `TraceAsync` method
- `src/ControlMenu/Modules/Imaging/Services/ImageService.cs` — implement `TraceAsync` + `BuildVtracerArgs` + `BuildPotraceArgs` + `NormalizeForEngineAsync`
- `scripts/stage-seed.ps1` — aggregate vtracer + potrace alongside magick
- `CHANGELOG.md` — Added entry under `[Unreleased]` → `### Added`
- `docs/TECHNICAL_GUIDE.md` — brief Tracing section
- `docs/manual-test-checklist.md` — new Imaging → Tracing section

---

## Phase G.0 — `bilbospocketses/potrace-builds` repo (parallel to G.1)

This phase happens entirely outside the control-menu repo. Can run in parallel with G.1; both must complete before G.2 begins. New repo will be at `C:\Users\jscha\source\repos\potrace-builds\` per the `reference_source_repo.md` convention.

### Task G.0.1: Create the GitHub repo and clone locally

**Files:**
- Create: `C:\Users\jscha\source\repos\potrace-builds\` (working tree)

- [ ] **Step 1: Create the GitHub repo (private initially; flip to public after CI verification)**

Run:
```powershell
gh repo create bilbospocketses/potrace-builds --private --description "Build/packaging repo for potrace (Peter Selinger, GPL v2+) — vendored upstream source built for Windows (MinGW64) + Linux (gcc)"
```

Expected: GitHub returns the repo URL. No clone yet — empty repo.

- [ ] **Step 2: Clone the empty repo to local working tree**

Run:
```powershell
cd C:\Users\jscha\source\repos
gh repo clone bilbospocketses/potrace-builds
cd potrace-builds
```

Expected: Empty directory with only `.git/` and a default branch (`main`).

- [ ] **Step 3: Set up branch + `.gitattributes` for LF line endings**

Per `feedback_git_line_endings.md` + `feedback_standard_gitattributes.md`. Write `.gitattributes`:

```gitattributes
# Default to LF everywhere
* text=auto eol=lf

# Binary files
*.tar.gz binary
*.zip binary
*.exe binary
```

- [ ] **Step 4: Commit the .gitattributes**

Run:
```powershell
git add .gitattributes
git commit -m "chore: enforce LF line endings"
```

Expected: One commit on `main`.

### Task G.0.2: Vendor potrace 1.16 source tarball

**Files:**
- Create: `source/potrace-1.16.tar.gz`
- Create: `source/SHA256SUMS`

- [ ] **Step 1: Download the source tarball from SourceForge**

Run (PowerShell, in the potrace-builds working tree):
```powershell
New-Item -ItemType Directory -Path source -Force | Out-Null
Invoke-WebRequest -Uri "https://potrace.sourceforge.net/download/1.16/potrace-1.16.tar.gz" -OutFile "source\potrace-1.16.tar.gz"
```

Expected: ~480 KB file at `source\potrace-1.16.tar.gz`.

- [ ] **Step 2: Compute and record SHA-256**

Run:
```powershell
$hash = (Get-FileHash -Algorithm SHA256 source\potrace-1.16.tar.gz).Hash.ToLower()
"$hash  potrace-1.16.tar.gz" | Out-File -Encoding ascii source\SHA256SUMS
Get-Content source\SHA256SUMS
```

Expected output (the upstream sha256 for potrace-1.16.tar.gz is published on SourceForge; verify against `https://sourceforge.net/projects/potrace/files/1.16/`):
```
<64-hex-chars>  potrace-1.16.tar.gz
```

- [ ] **Step 3: Verify the file extracts cleanly (sanity check; do not commit the extracted tree)**

Run:
```powershell
$tmp = New-Item -ItemType Directory -Path (Join-Path $env:TEMP "potrace-extract-test") -Force
tar -xzf source\potrace-1.16.tar.gz -C $tmp.FullName
Get-ChildItem $tmp.FullName\potrace-1.16 | Select-Object Name | Format-Table
Remove-Item -Recurse -Force $tmp.FullName
```

Expected: lists `configure`, `Makefile.in`, `src/`, `COPYING`, `README`, etc. — confirms the tarball is well-formed.

- [ ] **Step 4: Commit the vendored source**

Run:
```powershell
git add source/potrace-1.16.tar.gz source/SHA256SUMS
git commit -m "feat: vendor potrace 1.16 source from sourceforge"
```

Expected: One commit; the tarball is tracked as binary per `.gitattributes`.

### Task G.0.3: Write README, LICENSE, ATTRIBUTION

**Files:**
- Create: `README.md`
- Create: `LICENSE`
- Create: `ATTRIBUTION.md`

- [ ] **Step 1: Extract upstream COPYING to seed LICENSE**

Run:
```powershell
$tmp = New-Item -ItemType Directory -Path (Join-Path $env:TEMP "potrace-license-extract") -Force
tar -xzf source\potrace-1.16.tar.gz -C $tmp.FullName potrace-1.16/COPYING
Copy-Item $tmp.FullName\potrace-1.16\COPYING LICENSE
Remove-Item -Recurse -Force $tmp.FullName
Get-Content LICENSE -TotalCount 5
```

Expected: First 5 lines of the GPL v2+ text from upstream.

- [ ] **Step 2: Write ATTRIBUTION.md**

Create `ATTRIBUTION.md`:

```markdown
# Attribution

## potrace

This repository builds and packages [potrace](https://potrace.sourceforge.net/) by Peter Selinger.

- **Author:** Peter Selinger <selinger@mathstat.dal.ca>
- **Upstream:** https://potrace.sourceforge.net/
- **License:** GNU General Public License v2 or later
- **Source vendored:** `potrace-1.16.tar.gz` (released 2019-09-17)

The source tarball under `source/potrace-1.16.tar.gz` is the unmodified upstream distribution. SHA-256 hash recorded in `source/SHA256SUMS` matches the upstream-published value.

The binaries published under GitHub Releases for this repo are produced by a standard `./configure && make` build of that vendored source, using MinGW64 via MSYS2 on Windows and gcc via build-essential on Linux. No source modifications.

For all questions about the potrace algorithm, behavior, or bugs in the engine itself, please refer upstream to https://potrace.sourceforge.net/.

This repository's role is packaging only.
```

- [ ] **Step 3: Write README.md**

Create `README.md`:

```markdown
# potrace-builds

Build and packaging repository for [potrace](https://potrace.sourceforge.net/) by Peter Selinger. Produces Windows (MinGW64) and Linux (gcc) binaries from vendored upstream 1.16 source, published as GitHub Release assets for consumption by [Control Menu](https://github.com/bilbospocketses/control-menu) and other downstream tools.

## What's in here

- `source/potrace-1.16.tar.gz` — vendored upstream source (unmodified, SHA-256 in `source/SHA256SUMS`)
- `.github/workflows/build.yml` — CI workflow that builds and releases binaries on tag push
- `LICENSE` — GPL v2+ (matches upstream)
- `ATTRIBUTION.md` — credit and provenance details

## Release assets per tag

For each `v*` tag, CI builds and attaches:
- `potrace-1.16-win64.zip` — Windows x64 binary built with MinGW64 via MSYS2
- `potrace-1.16-linux64.tar.gz` — Linux x64 binary built with gcc via build-essential

Both archives include the binary, the GPL v2+ LICENSE text, and ATTRIBUTION.md.

## Cutting a new release

1. Vendor any updated upstream source under `source/` and update `SHA256SUMS`.
2. Tag (e.g. `git tag v1.16 && git push origin v1.16`).
3. Verify the CI workflow run on Actions tab completes both build jobs.
4. Verify the GitHub Release for the tag has both assets attached.

## License

GPL v2+ matches upstream. See `LICENSE` and `ATTRIBUTION.md`.

This repository contains no algorithmic modifications to potrace; it is build/packaging only. Bugs in the tracing engine itself should be reported upstream at https://potrace.sourceforge.net/.
```

- [ ] **Step 4: Commit the docs**

Run:
```powershell
git add README.md LICENSE ATTRIBUTION.md
git commit -m "docs: README + LICENSE + ATTRIBUTION"
```

Expected: One commit.

### Task G.0.4: Write the CI build workflow

**Files:**
- Create: `.github/workflows/build.yml`

- [ ] **Step 1: Create the workflow file**

Create `.github/workflows/build.yml`:

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
        run: |
          sudo apt-get update
          sudo apt-get install -y build-essential

      - name: Extract source
        run: |
          mkdir -p build
          tar -xzf source/potrace-1.16.tar.gz -C build

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
          if-no-files-found: error

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
          mkdir -p build
          tar -xzf source/potrace-1.16.tar.gz -C build

      - name: Configure and build (MinGW64)
        working-directory: build/potrace-1.16
        run: |
          ./configure --host=x86_64-w64-mingw32
          make

      - name: Stage package contents
        run: |
          mkdir -p artifacts/potrace-1.16-win64
          cp build/potrace-1.16/src/potrace.exe artifacts/potrace-1.16-win64/
          cp build/potrace-1.16/COPYING artifacts/potrace-1.16-win64/LICENSE
          cp ATTRIBUTION.md artifacts/potrace-1.16-win64/

      - name: Zip via PowerShell
        shell: pwsh
        run: |
          Compress-Archive -Path artifacts/potrace-1.16-win64/* -DestinationPath potrace-1.16-win64.zip

      - uses: actions/upload-artifact@v4
        with:
          name: potrace-1.16-win64
          path: potrace-1.16-win64.zip
          if-no-files-found: error

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

- [ ] **Step 2: Create .gitignore for build artifacts**

Create `.gitignore`:

```gitignore
# Build artifacts
build/
artifacts/
*.zip
!source/*.zip
*.tar.gz
!source/*.tar.gz
```

- [ ] **Step 3: Commit the workflow and gitignore**

Run:
```powershell
git add .github/workflows/build.yml .gitignore
git commit -m "feat: CI workflow for win64 + linux64 builds on tag push"
git push -u origin main
```

Expected: Pushed to `origin/main`.

### Task G.0.5: Tag v1.16 and verify CI

- [ ] **Step 1: Tag and push**

Run:
```powershell
git tag v1.16
git push origin v1.16
```

Expected: Tag pushed; GitHub Actions kicks off the `Build potrace` workflow on the tag.

- [ ] **Step 2: Watch the CI run complete**

Run:
```powershell
gh run watch --repo bilbospocketses/potrace-builds
```

Expected: All three jobs (`build-linux`, `build-windows`, `release`) complete successfully in ~3-5 minutes total. If any fail, examine logs via `gh run view --log-failed --repo bilbospocketses/potrace-builds`.

- [ ] **Step 3: Verify the Release was created with both assets**

Run:
```powershell
gh release view v1.16 --repo bilbospocketses/potrace-builds --json assets,tagName,name,publishedAt | ConvertFrom-Json | Select-Object -ExpandProperty assets | Format-Table name, size
```

Expected: Two assets — `potrace-1.16-linux64.tar.gz` (~150 KB) and `potrace-1.16-win64.zip` (~250 KB).

- [ ] **Step 4: Smoke-test fetch from a third location**

Run (in any temp dir):
```powershell
$tmp = New-Item -ItemType Directory -Path (Join-Path $env:TEMP "potrace-fetch-smoke") -Force
cd $tmp
gh release download v1.16 --repo bilbospocketses/potrace-builds --pattern "*win64.zip"
Expand-Archive -Path potrace-1.16-win64.zip -DestinationPath extracted
.\extracted\potrace.exe --version
cd ..
Remove-Item -Recurse -Force $tmp
```

Expected: `potrace 1.16. Copyright (C) 2001-2019 Peter Selinger.` (or similar — proves the win64 binary runs).

- [ ] **Step 5: Flip the repo to public**

Run:
```powershell
gh repo edit bilbospocketses/potrace-builds --visibility public --accept-visibility-change-consequences
```

Expected: Repo now public so the control-menu `DependencyManagerService` can fetch from it without authentication.

---

## Phase G.1 — `ImagingModule` extension + seed pipeline (control-menu repo)

All tasks now in `C:\Users\jscha\source\repos\control-menu\` on `feature/velopack-phase-1-hotfix`. Assumes Phases A-F of the magick plan are complete (i.e., `ImagingModule.cs`, `IImageService`, `ImageService`, the 5 magick-backed pages, `fetch-magick.ps1`, `stage-seed.ps1` all exist).

### Task G.1.1: Add vtracer ModuleDependency

**Files:**
- Modify: `src/ControlMenu/Modules/Imaging/ImagingModule.cs`

- [ ] **Step 1: Locate the existing `Dependencies` collection initializer**

Run:
```powershell
Select-String -Path src\ControlMenu\Modules\Imaging\ImagingModule.cs -Pattern "magick" -Context 2,2
```

Expected: Shows the existing `new ModuleDependency { Name = "magick", ... }` block. Note its surrounding `Dependencies = new[] { ... }` (or `List<>`) collection.

- [ ] **Step 2: Append the vtracer ModuleDependency entry**

Add to the `Dependencies` collection in `ImagingModule.cs`, immediately after the existing magick entry:

```csharp
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
},
```

- [ ] **Step 3: Build and verify no compile errors**

Run:
```powershell
dotnet build src\ControlMenu\ControlMenu.csproj -c Debug --nologo /clp:ErrorsOnly
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Commit**

Run:
```powershell
git add src/ControlMenu/Modules/Imaging/ImagingModule.cs
git commit -m "feat(imaging): add vtracer ModuleDependency"
```

### Task G.1.2: Add potrace ModuleDependency

**Files:**
- Modify: `src/ControlMenu/Modules/Imaging/ImagingModule.cs`

- [ ] **Step 1: Append the potrace ModuleDependency entry**

Add to the `Dependencies` collection in `ImagingModule.cs`, immediately after the vtracer entry from Task G.1.1:

```csharp
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
},
```

- [ ] **Step 2: Build and verify**

Run:
```powershell
dotnet build src\ControlMenu\ControlMenu.csproj -c Debug --nologo /clp:ErrorsOnly
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

Run:
```powershell
git add src/ControlMenu/Modules/Imaging/ImagingModule.cs
git commit -m "feat(imaging): add potrace ModuleDependency"
```

### Task G.1.3: Write `fetch-vtracer.ps1`

**Files:**
- Create: `scripts/dependencies/fetch-vtracer.ps1`

- [ ] **Step 1: Mirror the `fetch-magick.ps1` pattern**

First inspect the existing pattern:
```powershell
Get-Content scripts\dependencies\fetch-magick.ps1
```

Then create `scripts/dependencies/fetch-vtracer.ps1` following the same structure (pinned version, SHA-256 verify, idempotent cache to `publish/seed/dependencies/vtracer/`):

```powershell
#requires -Version 7.0

<#
.SYNOPSIS
Fetch vtracer 0.6.4 from upstream GitHub release and stage under publish/seed/dependencies/vtracer/.

.DESCRIPTION
Idempotent. If the cached download already exists and matches the pinned SHA-256, no network call is made.

Mirrors the pattern of fetch-magick.ps1 / fetch-adb.ps1.
#>

param(
    [string]$Version = "0.6.4",
    [string]$SeedRoot = "$PSScriptRoot\..\..\publish\seed\dependencies\vtracer"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Pinned SHA-256 of vtracer-x86_64-pc-windows-msvc.zip from
# https://github.com/visioncortex/vtracer/releases/download/0.6.4/vtracer-x86_64-pc-windows-msvc.zip
$ExpectedSha256 = "<FILL_IN_AT_FIRST_RUN>"

$AssetName = "vtracer-x86_64-pc-windows-msvc.zip"
$Url = "https://github.com/visioncortex/vtracer/releases/download/$Version/$AssetName"

$cacheDir = Join-Path $env:TEMP "cm-seed-cache\vtracer-$Version"
New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
$cachedZip = Join-Path $cacheDir $AssetName

if (-not (Test-Path $cachedZip)) {
    Write-Host "Downloading $Url"
    Invoke-WebRequest -Uri $Url -OutFile $cachedZip
}

$actualSha = (Get-FileHash -Algorithm SHA256 $cachedZip).Hash.ToLower()
if ($ExpectedSha256 -eq "<FILL_IN_AT_FIRST_RUN>") {
    Write-Warning "First-run SHA-256 pin needed. Update fetch-vtracer.ps1 with: $actualSha"
} elseif ($actualSha -ne $ExpectedSha256.ToLower()) {
    throw "SHA-256 mismatch for $AssetName. Expected $ExpectedSha256, got $actualSha"
}

if (Test-Path $SeedRoot) { Remove-Item -Recurse -Force $SeedRoot }
New-Item -ItemType Directory -Path $SeedRoot -Force | Out-Null

Expand-Archive -Path $cachedZip -DestinationPath $SeedRoot -Force

Write-Host "Staged vtracer $Version -> $SeedRoot"
```

- [ ] **Step 2: Run it once to populate the SHA-256 pin**

Run:
```powershell
pwsh scripts\dependencies\fetch-vtracer.ps1
```

Expected: Warning prints the actual SHA-256. Copy it.

- [ ] **Step 3: Update the `$ExpectedSha256` literal with the real hash**

Edit `scripts/dependencies/fetch-vtracer.ps1` and replace `<FILL_IN_AT_FIRST_RUN>` with the hash printed in Step 2.

- [ ] **Step 4: Re-run to verify no warning**

Run:
```powershell
pwsh scripts\dependencies\fetch-vtracer.ps1
```

Expected: No warning. Final line: `Staged vtracer 0.6.4 -> publish\seed\dependencies\vtracer`.

- [ ] **Step 5: Verify the binary runs**

Run:
```powershell
.\publish\seed\dependencies\vtracer\vtracer.exe --version
```

Expected: `vtracer 0.6.4` (or similar).

- [ ] **Step 6: Commit**

Run:
```powershell
git add scripts/dependencies/fetch-vtracer.ps1
git commit -m "feat(seed): fetch-vtracer.ps1 for upstream 0.6.4"
```

### Task G.1.4: Write `fetch-potrace.ps1`

**Files:**
- Create: `scripts/dependencies/fetch-potrace.ps1`

- [ ] **Step 1: Create the fetch script mirroring fetch-vtracer**

Create `scripts/dependencies/fetch-potrace.ps1`:

```powershell
#requires -Version 7.0

<#
.SYNOPSIS
Fetch potrace 1.16 from bilbospocketses/potrace-builds GitHub release and stage under publish/seed/dependencies/potrace/.

.DESCRIPTION
Idempotent. If the cached download already exists and matches the pinned SHA-256, no network call is made.

Source repo is our own build/packaging fork of upstream potrace (Peter Selinger, GPL v2+).
#>

param(
    [string]$Version = "1.16",
    [string]$SeedRoot = "$PSScriptRoot\..\..\publish\seed\dependencies\potrace"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Pinned SHA-256 of potrace-1.16-win64.zip from
# https://github.com/bilbospocketses/potrace-builds/releases/download/v1.16/potrace-1.16-win64.zip
$ExpectedSha256 = "<FILL_IN_AT_FIRST_RUN>"

$AssetName = "potrace-$Version-win64.zip"
$Url = "https://github.com/bilbospocketses/potrace-builds/releases/download/v$Version/$AssetName"

$cacheDir = Join-Path $env:TEMP "cm-seed-cache\potrace-$Version"
New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
$cachedZip = Join-Path $cacheDir $AssetName

if (-not (Test-Path $cachedZip)) {
    Write-Host "Downloading $Url"
    Invoke-WebRequest -Uri $Url -OutFile $cachedZip
}

$actualSha = (Get-FileHash -Algorithm SHA256 $cachedZip).Hash.ToLower()
if ($ExpectedSha256 -eq "<FILL_IN_AT_FIRST_RUN>") {
    Write-Warning "First-run SHA-256 pin needed. Update fetch-potrace.ps1 with: $actualSha"
} elseif ($actualSha -ne $ExpectedSha256.ToLower()) {
    throw "SHA-256 mismatch for $AssetName. Expected $ExpectedSha256, got $actualSha"
}

if (Test-Path $SeedRoot) { Remove-Item -Recurse -Force $SeedRoot }
New-Item -ItemType Directory -Path $SeedRoot -Force | Out-Null

Expand-Archive -Path $cachedZip -DestinationPath $SeedRoot -Force

Write-Host "Staged potrace $Version -> $SeedRoot"
```

- [ ] **Step 2: Run it once to populate the SHA-256 pin**

Run:
```powershell
pwsh scripts\dependencies\fetch-potrace.ps1
```

Expected: Warning prints the actual SHA-256. Copy it.

- [ ] **Step 3: Update the `$ExpectedSha256` literal**

Edit `scripts/dependencies/fetch-potrace.ps1` and replace `<FILL_IN_AT_FIRST_RUN>` with the hash from Step 2.

- [ ] **Step 4: Re-run to verify no warning**

Run:
```powershell
pwsh scripts\dependencies\fetch-potrace.ps1
```

Expected: No warning. Final line: `Staged potrace 1.16 -> publish\seed\dependencies\potrace`.

- [ ] **Step 5: Verify the binary runs and ships with LICENSE + ATTRIBUTION**

Run:
```powershell
.\publish\seed\dependencies\potrace\potrace.exe --version
Get-ChildItem .\publish\seed\dependencies\potrace
```

Expected: `potrace 1.16. ...` plus a directory listing showing `potrace.exe`, `LICENSE`, `ATTRIBUTION.md`.

- [ ] **Step 6: Commit**

Run:
```powershell
git add scripts/dependencies/fetch-potrace.ps1
git commit -m "feat(seed): fetch-potrace.ps1 from bilbospocketses/potrace-builds v1.16"
```

### Task G.1.5: Extend `stage-seed.ps1` to aggregate the two new deps

**Files:**
- Modify: `scripts/stage-seed.ps1`

- [ ] **Step 1: Inspect current aggregation pattern**

Run:
```powershell
Get-Content scripts\stage-seed.ps1
```

Note where `fetch-magick.ps1` is invoked and where its output is aggregated into the final `publish/seed/dependencies/` tree.

- [ ] **Step 2: Add vtracer + potrace invocations following the magick pattern**

Edit `scripts/stage-seed.ps1`. Find the block that calls `& "$PSScriptRoot\dependencies\fetch-magick.ps1"` (or equivalent). Add immediately after:

```powershell
& "$PSScriptRoot\dependencies\fetch-vtracer.ps1"
& "$PSScriptRoot\dependencies\fetch-potrace.ps1"
```

- [ ] **Step 3: Run the aggregator end-to-end**

Run:
```powershell
pwsh scripts\stage-seed.ps1
```

Expected: All three fetch scripts run; final `publish/seed/dependencies/` contains `magick/`, `vtracer/`, `potrace/` subdirectories with the respective binaries.

- [ ] **Step 4: Verify the directory tree**

Run:
```powershell
Get-ChildItem publish\seed\dependencies | Format-Table Name, Mode
```

Expected: Three directories (magick, vtracer, potrace).

- [ ] **Step 5: Commit**

Run:
```powershell
git add scripts/stage-seed.ps1
git commit -m "feat(seed): aggregate vtracer + potrace alongside magick"
```

### Task G.1.6: Smoke-gate G.1 — verify Settings → Dependencies surfaces both

- [ ] **Step 1: Build and run the app in Debug**

Run:
```powershell
dotnet run --project src\ControlMenu\ControlMenu.csproj -c Debug
```

Expected: App starts, listening on http://localhost:5159 (or whatever WebPort the existing config resolves).

- [ ] **Step 2: Navigate to Settings → Dependencies in browser**

Open `http://localhost:5159/settings/dependencies` in the default browser.

Expected: Both `vtracer` and `potrace` appear in the dependency list alongside `magick` and the other module deps. Each shows its installed version (resolved via `VersionCommand`).

- [ ] **Step 3: Stop the dev server**

`Ctrl+C` in the terminal running the app.

- [ ] **Step 4: No commit** — this is a verification step, no code changes.

---

## Phase G.2 — Service contract (options types + arg builders)

### Task G.2.1: Create `TraceEngine` enum

**Files:**
- Create: `src/ControlMenu/Modules/Imaging/Services/Options/TraceEngine.cs`

- [ ] **Step 1: Write the enum**

Create the file:

```csharp
namespace ControlMenu.Modules.Imaging.Services.Options;

public enum TraceEngine
{
    Vtracer,
    Potrace
}
```

- [ ] **Step 2: Build to verify namespace/import work**

Run:
```powershell
dotnet build src\ControlMenu\ControlMenu.csproj -c Debug --nologo /clp:ErrorsOnly
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

Run:
```powershell
git add src/ControlMenu/Modules/Imaging/Services/Options/TraceEngine.cs
git commit -m "feat(imaging): TraceEngine enum (Vtracer | Potrace)"
```

### Task G.2.2: Create `VtracerOptions` record + supporting enums

**Files:**
- Create: `src/ControlMenu/Modules/Imaging/Services/Options/VtracerOptions.cs`

- [ ] **Step 1: Write the file**

Create:

```csharp
namespace ControlMenu.Modules.Imaging.Services.Options;

public enum VtracerColorMode
{
    Color,
    Bw
}

public enum VtracerMode
{
    Pixel,
    Polygon,
    Spline
}

public enum VtracerHierarchical
{
    Stacked,
    Cutout
}

public record VtracerOptions(
    VtracerColorMode ColorMode = VtracerColorMode.Color,
    VtracerMode Mode = VtracerMode.Spline,
    string? Preset = "photo",
    int FilterSpeckle = 4,
    int ColorPrecision = 6,
    int GradientStep = 16,
    int CornerThreshold = 60,
    int SegmentLength = 4,
    int SpliceThreshold = 45,
    int PathPrecision = 8,
    VtracerHierarchical Hierarchical = VtracerHierarchical.Stacked);
```

- [ ] **Step 2: Build**

Run:
```powershell
dotnet build src\ControlMenu\ControlMenu.csproj -c Debug --nologo /clp:ErrorsOnly
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

Run:
```powershell
git add src/ControlMenu/Modules/Imaging/Services/Options/VtracerOptions.cs
git commit -m "feat(imaging): VtracerOptions record + supporting enums"
```

### Task G.2.3: Create `PotraceOptions` record

**Files:**
- Create: `src/ControlMenu/Modules/Imaging/Services/Options/PotraceOptions.cs`

- [ ] **Step 1: Write the file**

Create:

```csharp
namespace ControlMenu.Modules.Imaging.Services.Options;

public record PotraceOptions(
    int Turdsize = 2,
    double Alphamax = 1.0,
    double OptTolerance = 0.2,
    bool LongCurve = true,
    int BinarizationThreshold = 50);
```

- [ ] **Step 2: Build**

Run:
```powershell
dotnet build src\ControlMenu\ControlMenu.csproj -c Debug --nologo /clp:ErrorsOnly
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

Run:
```powershell
git add src/ControlMenu/Modules/Imaging/Services/Options/PotraceOptions.cs
git commit -m "feat(imaging): PotraceOptions record"
```

### Task G.2.4: Create `TraceOptions` umbrella record

**Files:**
- Create: `src/ControlMenu/Modules/Imaging/Services/Options/TraceOptions.cs`

- [ ] **Step 1: Write the file**

Create:

```csharp
namespace ControlMenu.Modules.Imaging.Services.Options;

public record TraceOptions(
    VtracerOptions? Vtracer = null,
    PotraceOptions? Potrace = null);
```

- [ ] **Step 2: Build**

Run:
```powershell
dotnet build src\ControlMenu\ControlMenu.csproj -c Debug --nologo /clp:ErrorsOnly
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

Run:
```powershell
git add src/ControlMenu/Modules/Imaging/Services/Options/TraceOptions.cs
git commit -m "feat(imaging): TraceOptions umbrella record"
```

### Task G.2.5: Extend `IImageService` with `TraceAsync` signature

**Files:**
- Modify: `src/ControlMenu/Modules/Imaging/Services/IImageService.cs`

- [ ] **Step 1: Inspect current interface**

Run:
```powershell
Get-Content src\ControlMenu\Modules\Imaging\Services\IImageService.cs
```

Note the existing using directives and method signatures.

- [ ] **Step 2: Add the new using if needed and the new method**

Add to the using block:
```csharp
using ControlMenu.Modules.Imaging.Services.Options;
```

Add to the interface, after the existing methods:

```csharp
Task<byte[]> TraceAsync(
    byte[] input,
    TraceEngine engine,
    TraceOptions options,
    CancellationToken ct = default);
```

- [ ] **Step 3: Build — will fail because `ImageService` doesn't yet implement `TraceAsync`**

Run:
```powershell
dotnet build src\ControlMenu\ControlMenu.csproj -c Debug --nologo /clp:ErrorsOnly
```

Expected: Build FAILS with `'ImageService' does not implement interface member 'IImageService.TraceAsync(...)'`. This is intentional — Task G.2.6 implements the body.

- [ ] **Step 4: Stub `ImageService.TraceAsync` with NotImplementedException so the build passes**

Edit `src/ControlMenu/Modules/Imaging/Services/ImageService.cs`. Add the new using if not present:
```csharp
using ControlMenu.Modules.Imaging.Services.Options;
```

Add the stub method:
```csharp
public Task<byte[]> TraceAsync(byte[] input, TraceEngine engine, TraceOptions options, CancellationToken ct = default)
    => throw new NotImplementedException("Implemented in subsequent tasks G.2.6 onward.");
```

- [ ] **Step 5: Verify build passes now**

Run:
```powershell
dotnet build src\ControlMenu\ControlMenu.csproj -c Debug --nologo /clp:ErrorsOnly
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 6: Commit**

Run:
```powershell
git add src/ControlMenu/Modules/Imaging/Services/IImageService.cs src/ControlMenu/Modules/Imaging/Services/ImageService.cs
git commit -m "feat(imaging): IImageService.TraceAsync signature + stub"
```

### Task G.2.6: `BuildVtracerArgs` — write failing tests

**Files:**
- Create: `tests/ControlMenu.Tests/Modules/Imaging/Services/BuildVtracerArgsTests.cs`

- [ ] **Step 1: Write the test file**

Create:

```csharp
using ControlMenu.Modules.Imaging.Services;
using ControlMenu.Modules.Imaging.Services.Options;
using Xunit;

namespace ControlMenu.Tests.Modules.Imaging.Services;

public class BuildVtracerArgsTests
{
    private const string InPath = "C:\\temp\\in.png";
    private const string OutPath = "C:\\temp\\out.svg";

    [Fact]
    public void Defaults_UsesPhotoPresetOnly()
    {
        var opts = new VtracerOptions();
        var args = ImageService.BuildVtracerArgs(opts, InPath, OutPath);
        Assert.Equal(
            "--input \"C:\\temp\\in.png\" --output \"C:\\temp\\out.svg\" --colormode color --preset photo",
            args);
    }

    [Fact]
    public void BwPreset_UsesBwColormodeAndBwPreset()
    {
        var opts = new VtracerOptions(ColorMode: VtracerColorMode.Bw, Preset: "bw");
        var args = ImageService.BuildVtracerArgs(opts, InPath, OutPath);
        Assert.Equal(
            "--input \"C:\\temp\\in.png\" --output \"C:\\temp\\out.svg\" --colormode bw --preset bw",
            args);
    }

    [Fact]
    public void CustomMode_SuppressesPresetAndEmitsAllRawParams()
    {
        var opts = new VtracerOptions(
            Preset: null,
            ColorMode: VtracerColorMode.Color,
            Mode: VtracerMode.Polygon,
            FilterSpeckle: 8,
            ColorPrecision: 5,
            GradientStep: 32,
            CornerThreshold: 90,
            SegmentLength: 6,
            SpliceThreshold: 30,
            PathPrecision: 4,
            Hierarchical: VtracerHierarchical.Cutout);
        var args = ImageService.BuildVtracerArgs(opts, InPath, OutPath);
        Assert.Equal(
            "--input \"C:\\temp\\in.png\" --output \"C:\\temp\\out.svg\" --colormode color " +
            "--mode polygon --filter_speckle 8 --color_precision 5 --gradient_step 32 " +
            "--corner_threshold 90 --segment_length 6 --splice_threshold 30 --path_precision 4 " +
            "--hierarchical cutout",
            args);
    }

    [Fact]
    public void PixelMode_EmitsPixelKeyword()
    {
        var opts = new VtracerOptions(Preset: null, Mode: VtracerMode.Pixel);
        var args = ImageService.BuildVtracerArgs(opts, InPath, OutPath);
        Assert.Contains("--mode pixel", args);
    }

    [Fact]
    public void SplineModeWithCustom_EmitsSplineKeyword()
    {
        var opts = new VtracerOptions(Preset: null, Mode: VtracerMode.Spline);
        var args = ImageService.BuildVtracerArgs(opts, InPath, OutPath);
        Assert.Contains("--mode spline", args);
    }

    [Fact]
    public void PosterPreset_UsesPosterAndIgnoresPerParamFields()
    {
        var opts = new VtracerOptions(Preset: "poster", FilterSpeckle: 99);
        var args = ImageService.BuildVtracerArgs(opts, InPath, OutPath);
        Assert.Equal(
            "--input \"C:\\temp\\in.png\" --output \"C:\\temp\\out.svg\" --colormode color --preset poster",
            args);
        Assert.DoesNotContain("filter_speckle", args);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure (method doesn't exist yet)**

Run:
```powershell
dotnet test tests\ControlMenu.Tests\ControlMenu.Tests.csproj --filter "FullyQualifiedName~BuildVtracerArgsTests" --no-restore 2>&1 | Select-String -Pattern "error|FAIL|passed|failed" | Select-Object -First 20
```

Expected: Compilation errors referencing `'ImageService' does not contain a definition for 'BuildVtracerArgs'`.

- [ ] **Step 3: No commit yet** — tests must pass before commit.

### Task G.2.7: `BuildVtracerArgs` — implement to pass tests

**Files:**
- Modify: `src/ControlMenu/Modules/Imaging/Services/ImageService.cs`

- [ ] **Step 1: Add the static method**

Add to `ImageService` class (anywhere; convention is at the bottom in a `// --- arg builders ---` region):

```csharp
internal static string BuildVtracerArgs(VtracerOptions o, string inPath, string outPath)
{
    var sb = new System.Text.StringBuilder();
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

- [ ] **Step 2: Make `ImageService.Tests` see internal members via `InternalsVisibleTo` if not already configured**

Check if already configured:
```powershell
Select-String -Path src\ControlMenu\ControlMenu.csproj -Pattern "InternalsVisibleTo"
```

If not present, add to `src/ControlMenu/ControlMenu.csproj` inside `<PropertyGroup>` or `<ItemGroup>`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="ControlMenu.Tests" />
</ItemGroup>
```

(Skip this step if the project already exposes internals to the test assembly.)

- [ ] **Step 3: Run tests — expect all 6 passing**

Run:
```powershell
dotnet test tests\ControlMenu.Tests\ControlMenu.Tests.csproj --filter "FullyQualifiedName~BuildVtracerArgsTests" --no-restore 2>&1 | Select-String -Pattern "Passed|Failed|error" | Select-Object -First 5
```

Expected: `Passed! - Failed: 0, Passed: 6, Skipped: 0, ...`.

- [ ] **Step 4: Commit**

Run:
```powershell
git add src/ControlMenu/Modules/Imaging/Services/ImageService.cs tests/ControlMenu.Tests/Modules/Imaging/Services/BuildVtracerArgsTests.cs
git commit -m "feat(imaging): BuildVtracerArgs + 6 unit tests"
```

(If `ControlMenu.csproj` was modified for `InternalsVisibleTo`, add it to the same commit.)

### Task G.2.8: `BuildPotraceArgs` — write failing tests

**Files:**
- Create: `tests/ControlMenu.Tests/Modules/Imaging/Services/BuildPotraceArgsTests.cs`

- [ ] **Step 1: Write the test file**

Create:

```csharp
using ControlMenu.Modules.Imaging.Services;
using ControlMenu.Modules.Imaging.Services.Options;
using Xunit;

namespace ControlMenu.Tests.Modules.Imaging.Services;

public class BuildPotraceArgsTests
{
    private const string InPath = "C:\\temp\\in.bmp";
    private const string OutPath = "C:\\temp\\out.svg";

    [Fact]
    public void Defaults_LongCurveTrue_DoesNotEmitLongcurveFlag()
    {
        var opts = new PotraceOptions();
        var args = ImageService.BuildPotraceArgs(opts, InPath, OutPath);
        Assert.Equal(
            "\"C:\\temp\\in.bmp\" --svg --output \"C:\\temp\\out.svg\" " +
            "--turdsize 2 --alphamax 1.0 --opttolerance 0.2",
            args);
        Assert.DoesNotContain("--longcurve", args);
    }

    [Fact]
    public void LongCurveFalse_EmitsLongcurveFlag()
    {
        var opts = new PotraceOptions(LongCurve: false);
        var args = ImageService.BuildPotraceArgs(opts, InPath, OutPath);
        Assert.Contains(" --longcurve", args);
    }

    [Fact]
    public void CustomTurdsize_EmitsCustomValue()
    {
        var opts = new PotraceOptions(Turdsize: 10);
        var args = ImageService.BuildPotraceArgs(opts, InPath, OutPath);
        Assert.Contains("--turdsize 10", args);
    }

    [Fact]
    public void AlphamaxMax_FormatsAsDecimal()
    {
        var opts = new PotraceOptions(Alphamax: 1.3334);
        var args = ImageService.BuildPotraceArgs(opts, InPath, OutPath);
        Assert.Contains("--alphamax 1.333", args);  // .0## format gives 3 decimals
    }

    [Fact]
    public void OptToleranceMin_FormatsAsDecimal()
    {
        var opts = new PotraceOptions(OptTolerance: 0.05);
        var args = ImageService.BuildPotraceArgs(opts, InPath, OutPath);
        Assert.Contains("--opttolerance 0.05", args);
    }

    [Fact]
    public void BinarizationThreshold_DoesNotAffectPotraceArgs()
    {
        // Threshold is consumed by NormalizeForEngineAsync (magick pre-step),
        // not by the potrace arg builder.
        var opts = new PotraceOptions(BinarizationThreshold: 75);
        var args = ImageService.BuildPotraceArgs(opts, InPath, OutPath);
        Assert.DoesNotContain("threshold", args, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("75", args);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

Run:
```powershell
dotnet test tests\ControlMenu.Tests\ControlMenu.Tests.csproj --filter "FullyQualifiedName~BuildPotraceArgsTests" --no-restore 2>&1 | Select-String -Pattern "error|FAIL" | Select-Object -First 10
```

Expected: Compilation error: `'ImageService' does not contain a definition for 'BuildPotraceArgs'`.

- [ ] **Step 3: No commit yet.**

### Task G.2.9: `BuildPotraceArgs` — implement to pass tests

**Files:**
- Modify: `src/ControlMenu/Modules/Imaging/Services/ImageService.cs`

- [ ] **Step 1: Add the static method**

Add to `ImageService` class adjacent to `BuildVtracerArgs`:

```csharp
internal static string BuildPotraceArgs(PotraceOptions o, string inPath, string outPath)
{
    var sb = new System.Text.StringBuilder();
    sb.Append($"\"{inPath}\" --svg --output \"{outPath}\"");
    sb.Append($" --turdsize {o.Turdsize}");
    sb.Append(System.Globalization.CultureInfo.InvariantCulture, $" --alphamax {o.Alphamax:0.0##}");
    sb.Append(System.Globalization.CultureInfo.InvariantCulture, $" --opttolerance {o.OptTolerance:0.0##}");
    if (!o.LongCurve) sb.Append(" --longcurve");  // potrace --longcurve = "turn off curves"; see spec §3.7
    return sb.ToString();
}
```

- [ ] **Step 2: Run tests — expect all 6 passing**

Run:
```powershell
dotnet test tests\ControlMenu.Tests\ControlMenu.Tests.csproj --filter "FullyQualifiedName~BuildPotraceArgsTests" --no-restore 2>&1 | Select-String -Pattern "Passed|Failed" | Select-Object -First 5
```

Expected: `Passed! - Failed: 0, Passed: 6, Skipped: 0, ...`.

- [ ] **Step 3: Commit**

Run:
```powershell
git add src/ControlMenu/Modules/Imaging/Services/ImageService.cs tests/ControlMenu.Tests/Modules/Imaging/Services/BuildPotraceArgsTests.cs
git commit -m "feat(imaging): BuildPotraceArgs + 6 unit tests (incl. --longcurve inversion)"
```

---

## Phase G.3 — Service integration (`TraceAsync` + `NormalizeForEngineAsync`)

### Task G.3.1: `NormalizeForEngineAsync` — write failing tests

**Files:**
- Create: `tests/ControlMenu.Tests/Modules/Imaging/Services/NormalizeForEngineAsyncTests.cs`

- [ ] **Step 1: Write the test file**

Create:

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ControlMenu.Modules.Imaging.Services;
using ControlMenu.Modules.Imaging.Services.Options;
using Xunit;

namespace ControlMenu.Tests.Modules.Imaging.Services;

public class NormalizeForEngineAsyncTests : System.IDisposable
{
    private readonly string _workDir;
    private readonly ImageService _service;

    public NormalizeForEngineAsyncTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "CM-Imaging-Tests", System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
        _service = TestImageServiceFactory.CreateWithRealMagick();
    }

    [Fact]
    [SkippableFact]
    public async Task Vtracer_PngInput_WritesThroughAsIs()
    {
        SkipIfMagickMissing();
        var pngBytes = SyntheticImages.SolidColorPng(64, 64);
        var outPath = await _service.NormalizeForEngineAsync(pngBytes, TraceEngine.Vtracer, _workDir, CancellationToken.None);
        Assert.True(File.Exists(outPath));
        Assert.EndsWith(".png", outPath);
        Assert.Equal(pngBytes, await File.ReadAllBytesAsync(outPath));  // identity
    }

    [Fact]
    [SkippableFact]
    public async Task Vtracer_WebpInput_ConvertsToPngViaMagick()
    {
        SkipIfMagickMissing();
        var webpBytes = SyntheticImages.SolidColorWebp(64, 64);
        var outPath = await _service.NormalizeForEngineAsync(webpBytes, TraceEngine.Vtracer, _workDir, CancellationToken.None);
        Assert.True(File.Exists(outPath));
        Assert.EndsWith(".png", outPath);
        // Bytes will differ from input (re-encoded); we just check it's a valid PNG.
        var outBytes = await File.ReadAllBytesAsync(outPath);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, outBytes[..4]);  // PNG magic
    }

    [Fact]
    [SkippableFact]
    public async Task Potrace_ColorPngInput_ConvertsToBmpWithGrayThreshold()
    {
        SkipIfMagickMissing();
        var pngBytes = SyntheticImages.SolidColorPng(64, 64);
        var outPath = await _service.NormalizeForEngineAsync(pngBytes, TraceEngine.Potrace, _workDir, CancellationToken.None);
        Assert.True(File.Exists(outPath));
        Assert.EndsWith(".bmp", outPath);
        var outBytes = await File.ReadAllBytesAsync(outPath);
        Assert.Equal(new byte[] { 0x42, 0x4D }, outBytes[..2]);  // BMP magic 'BM'
    }

    [Fact]
    [SkippableFact]
    public async Task MagickFailure_ThrowsImagingException()
    {
        SkipIfMagickMissing();
        // Garbage input that magick can't decode
        var garbage = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        await Assert.ThrowsAsync<ImagingException>(
            () => _service.NormalizeForEngineAsync(garbage, TraceEngine.Vtracer, _workDir, CancellationToken.None));
    }

    private static void SkipIfMagickMissing()
        => Skip.IfNot(TestEnvironment.HasMagick(), "magick.exe not available in test environment");

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); } catch { /* swallow */ }
    }
}
```

- [ ] **Step 2: Confirm `TestImageServiceFactory`, `SyntheticImages`, and `TestEnvironment` helpers exist (created by Phase A of magick plan)**

Run:
```powershell
Select-String -Path tests\ControlMenu.Tests\Modules\Imaging\*.cs -Pattern "TestImageServiceFactory|SyntheticImages|TestEnvironment" -List
```

Expected: All three are found (created by the magick plan). If missing, defer this task until they are added.

- [ ] **Step 3: Run tests — expect compile failure (`NormalizeForEngineAsync` doesn't exist yet)**

Run:
```powershell
dotnet test tests\ControlMenu.Tests\ControlMenu.Tests.csproj --filter "FullyQualifiedName~NormalizeForEngineAsyncTests" --no-restore 2>&1 | Select-String -Pattern "error|FAIL" | Select-Object -First 10
```

Expected: Compilation error: `'ImageService' does not contain a definition for 'NormalizeForEngineAsync'`.

- [ ] **Step 4: No commit yet.**

### Task G.3.2: `NormalizeForEngineAsync` — implement to pass tests

**Files:**
- Modify: `src/ControlMenu/Modules/Imaging/Services/ImageService.cs`

- [ ] **Step 1: Add the method**

Add to `ImageService` class:

```csharp
internal async Task<string> NormalizeForEngineAsync(
    byte[] input,
    TraceEngine engine,
    string workDir,
    CancellationToken ct)
{
    // Detect input format via magick's identify-on-bytes pattern (uses the existing helper).
    var detectedExt = DetectExtension(input);  // existing helper from Phase A
    var rawInPath = Path.Combine(workDir, $"src{detectedExt}");
    await File.WriteAllBytesAsync(rawInPath, input, ct);

    switch (engine)
    {
        case TraceEngine.Vtracer:
        {
            // vtracer accepts PNG and JPG natively
            if (detectedExt is ".png" or ".jpg" or ".jpeg")
                return rawInPath;

            var pngPath = Path.Combine(workDir, "in.png");
            var args = $"-limit memory 512MB -limit area 16384x16384 \"{rawInPath}\" \"{pngPath}\"";
            var result = await _executor.ExecuteResolvedAsync(_resolver, "imaging", "magick", args, cancellationToken: ct);
            if (result.ExitCode != 0)
                throw new ImagingException($"magick normalization for vtracer failed: {result.StandardError}");
            return pngPath;
        }
        case TraceEngine.Potrace:
        {
            // potrace wants BMP3 + grayscale + thresholded
            var bmpPath = Path.Combine(workDir, "in.bmp");
            // Default 50% threshold matches PotraceOptions.BinarizationThreshold default;
            // caller-controlled threshold is applied at TraceAsync level (see G.3.3).
            var args = $"-limit memory 512MB -limit area 16384x16384 \"{rawInPath}\" -colorspace Gray -threshold 50% BMP3:\"{bmpPath}\"";
            var result = await _executor.ExecuteResolvedAsync(_resolver, "imaging", "magick", args, cancellationToken: ct);
            if (result.ExitCode != 0)
                throw new ImagingException($"magick normalization for potrace failed: {result.StandardError}");
            return bmpPath;
        }
        default:
            throw new System.ArgumentOutOfRangeException(nameof(engine));
    }
}
```

- [ ] **Step 2: Run tests — expect 4 passing (or `Skipped` if magick is not staged)**

Run:
```powershell
dotnet test tests\ControlMenu.Tests\ControlMenu.Tests.csproj --filter "FullyQualifiedName~NormalizeForEngineAsyncTests" --no-restore 2>&1 | Select-String -Pattern "Passed|Failed|Skipped" | Select-Object -First 5
```

Expected: `Passed: 4` (or `Skipped: 4` if magick isn't staged). If `Failed > 0`, investigate the failure — most likely a magick arg-syntax issue.

- [ ] **Step 3: Commit**

Run:
```powershell
git add src/ControlMenu/Modules/Imaging/Services/ImageService.cs tests/ControlMenu.Tests/Modules/Imaging/Services/NormalizeForEngineAsyncTests.cs
git commit -m "feat(imaging): NormalizeForEngineAsync + 4 integration tests"
```

### Task G.3.3: `TraceAsync` — full implementation

**Files:**
- Modify: `src/ControlMenu/Modules/Imaging/Services/ImageService.cs`

- [ ] **Step 1: Replace the stub with the real implementation**

Find the existing stub in `ImageService.cs`:

```csharp
public Task<byte[]> TraceAsync(byte[] input, TraceEngine engine, TraceOptions options, CancellationToken ct = default)
    => throw new NotImplementedException("Implemented in subsequent tasks G.2.6 onward.");
```

Replace with:

```csharp
public async Task<byte[]> TraceAsync(
    byte[] input,
    TraceEngine engine,
    TraceOptions options,
    CancellationToken ct = default)
{
    var workDir = Path.Combine(_paths.GetDataRoot(), "temp", "imaging", System.Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(workDir);

    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(System.TimeSpan.FromSeconds(60));  // per-trace cap, see spec §6.1
    var effectiveCt = timeoutCts.Token;

    try
    {
        // For potrace, threshold from options governs the binarization magick pre-step.
        // Re-run normalization with the caller-specified threshold if non-default.
        string inPath;
        if (engine == TraceEngine.Potrace && options.Potrace is { } pOpts && pOpts.BinarizationThreshold != 50)
        {
            inPath = await NormalizeForPotraceWithThresholdAsync(input, pOpts.BinarizationThreshold, workDir, effectiveCt);
        }
        else
        {
            inPath = await NormalizeForEngineAsync(input, engine, workDir, effectiveCt);
        }

        var outPath = Path.Combine(workDir, "out.svg");

        var (exe, args) = engine switch
        {
            TraceEngine.Vtracer => ("vtracer", BuildVtracerArgs(options.Vtracer ?? new(), inPath, outPath)),
            TraceEngine.Potrace => ("potrace", BuildPotraceArgs(options.Potrace ?? new(), inPath, outPath)),
            _ => throw new System.ArgumentOutOfRangeException(nameof(engine))
        };

        var result = await _executor.ExecuteResolvedAsync(_resolver, "imaging", exe, args, cancellationToken: effectiveCt);
        if (result.ExitCode != 0)
            throw new ImagingException($"{exe} failed (exit {result.ExitCode}): {result.StandardError}");

        return await File.ReadAllBytesAsync(outPath, effectiveCt);
    }
    finally
    {
        try { Directory.Delete(workDir, recursive: true); } catch { /* swallow */ }
    }
}

private async Task<string> NormalizeForPotraceWithThresholdAsync(
    byte[] input,
    int threshold,
    string workDir,
    CancellationToken ct)
{
    var detectedExt = DetectExtension(input);
    var rawInPath = Path.Combine(workDir, $"src{detectedExt}");
    await File.WriteAllBytesAsync(rawInPath, input, ct);

    var bmpPath = Path.Combine(workDir, "in.bmp");
    var args = $"-limit memory 512MB -limit area 16384x16384 \"{rawInPath}\" -colorspace Gray -threshold {threshold}% BMP3:\"{bmpPath}\"";
    var result = await _executor.ExecuteResolvedAsync(_resolver, "imaging", "magick", args, cancellationToken: ct);
    if (result.ExitCode != 0)
        throw new ImagingException($"magick normalization for potrace failed: {result.StandardError}");
    return bmpPath;
}
```

- [ ] **Step 2: Build to verify**

Run:
```powershell
dotnet build src\ControlMenu\ControlMenu.csproj -c Debug --nologo /clp:ErrorsOnly
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit (tests for full TraceAsync added in next task)**

Run:
```powershell
git add src/ControlMenu/Modules/Imaging/Services/ImageService.cs
git commit -m "feat(imaging): TraceAsync full implementation (timeout, threshold, cleanup)"
```

### Task G.3.4: `TraceAsync` — integration tests (vtracer + potrace round-trips)

**Files:**
- Create: `tests/ControlMenu.Tests/Modules/Imaging/Services/ImageServiceTraceTests.cs`

- [ ] **Step 1: Write the test file**

Create:

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using ControlMenu.Modules.Imaging.Services;
using ControlMenu.Modules.Imaging.Services.Options;
using Xunit;

namespace ControlMenu.Tests.Modules.Imaging.Services;

public class ImageServiceTraceTests : System.IDisposable
{
    private readonly ImageService _service;

    public ImageServiceTraceTests()
    {
        _service = TestImageServiceFactory.CreateWithRealMagickVtracerAndPotrace();
    }

    [Fact]
    [SkippableFact]
    public async Task Vtracer_PhotoPreset_ProducesValidSvgWithPaths()
    {
        SkipIfDepsMissing(TraceEngine.Vtracer);
        var pngBytes = SyntheticImages.GradientPng(256, 256);
        var svg = await _service.TraceAsync(pngBytes, TraceEngine.Vtracer, new TraceOptions(Vtracer: new()), CancellationToken.None);
        AssertIsValidSvgWithPaths(svg);
    }

    [Fact]
    [SkippableFact]
    public async Task Vtracer_BwPreset_ProducesValidSvg()
    {
        SkipIfDepsMissing(TraceEngine.Vtracer);
        var pngBytes = SyntheticImages.HighContrastPng(256, 256);
        var opts = new TraceOptions(Vtracer: new VtracerOptions(ColorMode: VtracerColorMode.Bw, Preset: "bw"));
        var svg = await _service.TraceAsync(pngBytes, TraceEngine.Vtracer, opts, CancellationToken.None);
        AssertIsValidSvgWithPaths(svg);
    }

    [Fact]
    [SkippableFact]
    public async Task Vtracer_CustomMode_ProducesValidSvg()
    {
        SkipIfDepsMissing(TraceEngine.Vtracer);
        var pngBytes = SyntheticImages.HighContrastPng(128, 128);
        var opts = new TraceOptions(Vtracer: new VtracerOptions(Preset: null, FilterSpeckle: 8));
        var svg = await _service.TraceAsync(pngBytes, TraceEngine.Vtracer, opts, CancellationToken.None);
        AssertIsValidSvgWithPaths(svg);
    }

    [Fact]
    [SkippableFact]
    public async Task Vtracer_GarbageInput_ThrowsImagingException()
    {
        SkipIfDepsMissing(TraceEngine.Vtracer);
        var garbage = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        await Assert.ThrowsAsync<ImagingException>(
            () => _service.TraceAsync(garbage, TraceEngine.Vtracer, new TraceOptions(Vtracer: new()), CancellationToken.None));
    }

    [Fact]
    [SkippableFact]
    public async Task Potrace_Default_ProducesValidSvgWithPaths()
    {
        SkipIfDepsMissing(TraceEngine.Potrace);
        var pngBytes = SyntheticImages.HighContrastPng(256, 256);
        var svg = await _service.TraceAsync(pngBytes, TraceEngine.Potrace, new TraceOptions(Potrace: new()), CancellationToken.None);
        AssertIsValidSvgWithPaths(svg);
    }

    [Fact]
    [SkippableFact]
    public async Task Potrace_PolygonOnly_ProducesValidSvg()
    {
        SkipIfDepsMissing(TraceEngine.Potrace);
        var pngBytes = SyntheticImages.HighContrastPng(256, 256);
        var opts = new TraceOptions(Potrace: new PotraceOptions(LongCurve: false));
        var svg = await _service.TraceAsync(pngBytes, TraceEngine.Potrace, opts, CancellationToken.None);
        AssertIsValidSvgWithPaths(svg);
    }

    [Fact]
    [SkippableFact]
    public async Task Potrace_CustomThreshold_ProducesValidSvg()
    {
        SkipIfDepsMissing(TraceEngine.Potrace);
        var pngBytes = SyntheticImages.GradientPng(256, 256);
        var opts = new TraceOptions(Potrace: new PotraceOptions(BinarizationThreshold: 30));
        var svg = await _service.TraceAsync(pngBytes, TraceEngine.Potrace, opts, CancellationToken.None);
        AssertIsValidSvgWithPaths(svg);
    }

    [Fact]
    [SkippableFact]
    public async Task Potrace_GarbageInput_ThrowsImagingException()
    {
        SkipIfDepsMissing(TraceEngine.Potrace);
        var garbage = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        await Assert.ThrowsAsync<ImagingException>(
            () => _service.TraceAsync(garbage, TraceEngine.Potrace, new TraceOptions(Potrace: new()), CancellationToken.None));
    }

    private static void SkipIfDepsMissing(TraceEngine engine)
    {
        Skip.IfNot(TestEnvironment.HasMagick(), "magick.exe not available");
        Skip.IfNot(TestEnvironment.HasEngine(engine), $"{engine} not available in test environment");
    }

    private static void AssertIsValidSvgWithPaths(byte[] svgBytes)
    {
        Assert.NotEmpty(svgBytes);
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(svgBytes));
        Assert.NotNull(doc.Root);
        Assert.Equal("svg", doc.Root!.Name.LocalName);
        var ns = doc.Root.Name.Namespace;
        var paths = doc.Root.Descendants(ns + "path");
        Assert.NotEmpty(paths);  // at least one <path> element produced
    }

    public void Dispose() { /* nothing to dispose */ }
}
```

- [ ] **Step 2: Verify `TestEnvironment.HasEngine` exists; if not, add it**

Check:
```powershell
Select-String -Path tests\ControlMenu.Tests\Modules\Imaging\TestEnvironment.cs -Pattern "HasEngine"
```

If missing, extend `TestEnvironment`:

```csharp
public static bool HasEngine(TraceEngine engine) => engine switch
{
    TraceEngine.Vtracer => HasBinary("vtracer"),
    TraceEngine.Potrace => HasBinary("potrace"),
    _ => false
};

private static bool HasBinary(string name)
{
    // Probe the staged seed directory the test bootstrap copies for IDependencyPathResolver.
    var exe = OperatingSystem.IsWindows() ? $"{name}.exe" : name;
    return File.Exists(Path.Combine(TestSeedDir, name, exe));
}
```

(Adapt `TestSeedDir` to the existing convention used by `HasMagick` — they share infrastructure.)

- [ ] **Step 3: Add the `CreateWithRealMagickVtracerAndPotrace` factory if missing**

Check:
```powershell
Select-String -Path tests\ControlMenu.Tests\Modules\Imaging\TestImageServiceFactory.cs -Pattern "CreateWithRealMagickVtracerAndPotrace"
```

If missing, extend the factory to register all three binaries in the `IDependencyPathResolver` test setup. Pattern mirrors the existing `CreateWithRealMagick`.

- [ ] **Step 4: Run tests**

Run:
```powershell
dotnet test tests\ControlMenu.Tests\ControlMenu.Tests.csproj --filter "FullyQualifiedName~ImageServiceTraceTests" --no-restore 2>&1 | Select-String -Pattern "Passed|Failed|Skipped" | Select-Object -First 5
```

Expected: `Passed: 8` (if all three deps are staged), or `Skipped: 8` (if not).

- [ ] **Step 5: Commit**

Run:
```powershell
git add tests/ControlMenu.Tests/Modules/Imaging/Services/ImageServiceTraceTests.cs tests/ControlMenu.Tests/Modules/Imaging/TestEnvironment.cs tests/ControlMenu.Tests/Modules/Imaging/TestImageServiceFactory.cs
git commit -m "test(imaging): TraceAsync integration tests (8 per-engine + error paths)"
```

---

## Phase G.4 — Tracing page UI

### Task G.4.1: Add NavEntry for the Tracing page to `ImagingModule`

**Files:**
- Modify: `src/ControlMenu/Modules/Imaging/ImagingModule.cs`

- [ ] **Step 1: Locate the existing `NavEntries` collection**

Run:
```powershell
Select-String -Path src\ControlMenu\Modules\Imaging\ImagingModule.cs -Pattern "NavEntry|NavEntries" -Context 1,1
```

- [ ] **Step 2: Append the Tracing nav entry**

Add to the `NavEntries` collection, ordered after the existing 5 entries (use the next available `SortOrder`):

```csharp
new NavEntry
{
    Label = "Tracing",
    Route = "/imaging/tracing",
    Icon = "bi-vector-pen",
    SortOrder = 60   // pick the next free slot after the other 5 imaging tools
}
```

(Verify the icon name against the existing Bootstrap-icons set used elsewhere; `bi-vector-pen` is the natural fit for raster→vector. If unavailable in the project's icon set, fall back to `bi-pencil-square` or another close match.)

- [ ] **Step 3: Build to verify**

Run:
```powershell
dotnet build src\ControlMenu\ControlMenu.csproj -c Debug --nologo /clp:ErrorsOnly
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Commit**

Run:
```powershell
git add src/ControlMenu/Modules/Imaging/ImagingModule.cs
git commit -m "feat(imaging): Tracing nav entry"
```

### Task G.4.2: Create `Tracing.razor` skeleton

**Files:**
- Create: `src/ControlMenu/Modules/Imaging/Pages/Tracing.razor`
- Create: `src/ControlMenu/Modules/Imaging/Pages/Tracing.razor.css`

- [ ] **Step 1: Write the minimal Tracing.razor skeleton with page route + injected service**

Create `src/ControlMenu/Modules/Imaging/Pages/Tracing.razor`:

```razor
@page "/imaging/tracing"
@using ControlMenu.Modules.Imaging.Services
@using ControlMenu.Modules.Imaging.Services.Options
@inject IImageService ImageService
@inject Microsoft.JSInterop.IJSRuntime JS

<h2>Tracing</h2>

<p class="page-intro">
    Convert raster images (PNG/JPG) into scalable SVG vector graphics.
</p>

@if (_sourceBytes is null)
{
    <button class="btn btn-primary" @onclick="PickFile">Pick image...</button>
}
else
{
    <div class="source-info">
        <strong>@_sourceFilename</strong>
        — @_sourceDimensions
        — @_sourceFormat
        — @_sourceSizeBytes.ToString("N0") bytes
    </div>
    <button class="btn btn-sm btn-secondary" @onclick="ClearFile">Pick different image</button>
}

@if (_sourceBytes is not null)
{
    <div class="engine-selector">
        <span class="label">Engine:</span>
        <button class="engine-btn @(_engine == TraceEngine.Vtracer ? "active" : "")" @onclick="@(() => SwitchEngine(TraceEngine.Vtracer))">
            vtracer — color/photo
        </button>
        <button class="engine-btn @(_engine == TraceEngine.Potrace ? "active" : "")" @onclick="@(() => SwitchEngine(TraceEngine.Potrace))">
            potrace — B&amp;W/logo
        </button>
    </div>

    <p class="engine-help">
        <em>vtracer: color photos, illustrations, cliparts. potrace: B&amp;W silhouettes, logos, line art
        (input is converted to B&amp;W internally).</em>
    </p>

    @* Preset dropdown, Advanced expander, preview pane, action buttons added in subsequent tasks. *@
}

@code {
    private byte[]? _sourceBytes;
    private string? _sourceFilename;
    private string? _sourceDimensions;
    private string? _sourceFormat;
    private long _sourceSizeBytes;
    private TraceEngine _engine = TraceEngine.Vtracer;

    private Task PickFile() => Task.CompletedTask;  // wired in Task G.4.3

    private void ClearFile()
    {
        _sourceBytes = null;
        _sourceFilename = null;
        _sourceDimensions = null;
        _sourceFormat = null;
        _sourceSizeBytes = 0;
    }

    private void SwitchEngine(TraceEngine engine)
    {
        _engine = engine;
        // Preset/Advanced reset + Stale-badge wiring added in subsequent tasks.
    }
}
```

- [ ] **Step 2: Write the matching scoped CSS skeleton**

Create `src/ControlMenu/Modules/Imaging/Pages/Tracing.razor.css`:

```css
.page-intro {
    margin-bottom: 1rem;
    color: var(--bs-body-color);
}

.source-info {
    padding: 0.5rem 0.75rem;
    background: var(--bs-tertiary-bg);
    border-radius: 4px;
    margin-bottom: 0.5rem;
    font-size: 0.9rem;
}

.engine-selector {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin: 1rem 0 0.5rem 0;
}

.engine-selector .label {
    font-weight: 600;
    min-width: 60px;
}

.engine-btn {
    padding: 0.4rem 0.9rem;
    background: var(--bs-secondary-bg);
    border: 1px solid var(--bs-border-color);
    border-radius: 4px;
    cursor: pointer;
    transition: background-color 0.15s ease;
}

.engine-btn:hover {
    background: var(--bs-tertiary-bg);
}

.engine-btn.active {
    background: var(--bs-primary);
    color: var(--bs-light);
    border-color: var(--bs-primary);
}

.engine-help {
    font-size: 0.85rem;
    color: var(--bs-secondary-color);
    margin: 0 0 1rem 0;
}
```

- [ ] **Step 3: Build and verify the route registers**

Run:
```powershell
dotnet build src\ControlMenu\ControlMenu.csproj -c Debug --nologo /clp:ErrorsOnly
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Commit**

Run:
```powershell
git add src/ControlMenu/Modules/Imaging/Pages/Tracing.razor src/ControlMenu/Modules/Imaging/Pages/Tracing.razor.css
git commit -m "feat(imaging): Tracing page skeleton + engine selector"
```

### Task G.4.3: File picker wiring (File System Access API)

**Files:**
- Modify: `src/ControlMenu/Modules/Imaging/Pages/Tracing.razor`

- [ ] **Step 1: Replace the stub `PickFile` and add bytes-handling logic**

Edit `Tracing.razor`. Replace `private Task PickFile() => Task.CompletedTask;` and any associated bits with:

```csharp
private async Task PickFile()
{
    // Reuse the existing file picker helper from Phase A of magick plan.
    // (Look at IconConverter.razor for the canonical usage.)
    var picked = await ImagingFilePickerJsInterop.PickImageAsync(JS, acceptExtensions: new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tiff", ".gif" });
    if (picked is null) return;

    _sourceBytes = picked.Bytes;
    _sourceFilename = picked.FileName;
    _sourceSizeBytes = picked.Bytes.Length;

    // Probe dimensions + format via IImageService.GetInfoAsync (existing from Phase A).
    var info = await ImageService.GetInfoAsync(picked.Bytes);
    _sourceDimensions = $"{info.Width}×{info.Height}";
    _sourceFormat = info.Format;

    StateHasChanged();
}
```

- [ ] **Step 2: Build and run dev server**

Run:
```powershell
dotnet build src\ControlMenu\ControlMenu.csproj -c Debug --nologo /clp:ErrorsOnly
dotnet run --project src\ControlMenu\ControlMenu.csproj -c Debug
```

Expected: App starts; visit `http://localhost:5159/imaging/tracing`. Click "Pick image..." → File System Access API picker opens. Pick a PNG. Filename + dimensions render. Stop dev server with Ctrl+C.

- [ ] **Step 3: Commit**

Run:
```powershell
git add src/ControlMenu/Modules/Imaging/Pages/Tracing.razor
git commit -m "feat(imaging): Tracing file picker + source-info display"
```

### Task G.4.4: Preset dropdowns + Advanced expander (per-engine controls)

**Files:**
- Modify: `src/ControlMenu/Modules/Imaging/Pages/Tracing.razor`
- Modify: `src/ControlMenu/Modules/Imaging/Pages/Tracing.razor.css`

- [ ] **Step 1: Add the preset dropdown markup after the engine helper text**

Add to `Tracing.razor` inside the `_sourceBytes is not null` block, after the `.engine-help` paragraph:

```razor
<div class="preset-row">
    <label for="preset">Preset:</label>
    <select id="preset" class="form-select form-select-sm" @bind="_preset">
        @if (_engine == TraceEngine.Vtracer)
        {
            <option value="photo">Photo</option>
            <option value="poster">Poster</option>
            <option value="bw">B&amp;W</option>
            <option value="custom">Custom</option>
        }
        else
        {
            <option value="default">Default</option>
            <option value="logo-sharp">Logo Sharp</option>
            <option value="smooth">Smooth</option>
            <option value="polygon-only">Polygon-only</option>
            <option value="custom">Custom</option>
        }
    </select>
</div>

<details class="advanced-expander" @bind="_advancedExpanded">
    <summary>Advanced parameters</summary>
    @if (_engine == TraceEngine.Vtracer)
    {
        <VtracerAdvancedControls Options="_vtracerOpts" OptionsChanged="@(o => { _vtracerOpts = o; MarkStale(); })" />
    }
    else
    {
        <PotraceAdvancedControls Options="_potraceOpts" OptionsChanged="@(o => { _potraceOpts = o; MarkStale(); })" />
    }
</details>
```

- [ ] **Step 2: Add the new field declarations and helper methods to `@code`**

In the `@code` block of `Tracing.razor`, add:

```csharp
private string _preset = "photo";
private bool _advancedExpanded = false;
private VtracerOptions _vtracerOpts = new();
private PotraceOptions _potraceOpts = new();
private bool _isStale = false;

private void MarkStale()
{
    if (_lastFullTraceResult is not null) _isStale = true;
    StateHasChanged();
}
```

And update `SwitchEngine` to reset preset/expander/stale state per spec §4.1:

```csharp
private void SwitchEngine(TraceEngine engine)
{
    if (_engine == engine) return;
    _engine = engine;
    _preset = engine == TraceEngine.Vtracer ? "photo" : "default";
    _advancedExpanded = false;
    _isStale = false;
    _lastFullTraceResult = null;
    _lastPreviewResult = null;
    StateHasChanged();
}
```

- [ ] **Step 3: Create the per-engine advanced-controls components**

Create `src/ControlMenu/Modules/Imaging/Pages/VtracerAdvancedControls.razor`:

```razor
@using ControlMenu.Modules.Imaging.Services.Options

<div class="advanced-grid">
    <label>Color Mode</label>
    <select class="form-select form-select-sm" @bind="ColorModeStr">
        <option value="color">Color</option>
        <option value="bw">B&amp;W</option>
    </select>

    <label>Mode</label>
    <select class="form-select form-select-sm" @bind="ModeStr">
        <option value="pixel">Pixel</option>
        <option value="polygon">Polygon</option>
        <option value="spline">Spline</option>
    </select>

    <label>Filter Speckle (0-10)</label>
    <input type="range" min="0" max="10" @bind="FilterSpeckle" />
    <span>@Options.FilterSpeckle</span>

    <label>Color Precision (1-8)</label>
    <input type="range" min="1" max="8" @bind="ColorPrecision" />
    <span>@Options.ColorPrecision</span>

    <label>Corner Threshold (0-180)</label>
    <input type="range" min="0" max="180" @bind="CornerThreshold" />
    <span>@Options.CornerThreshold</span>

    @* Add Gradient Step, Segment Length, Splice Threshold, Path Precision, Hierarchical with the same pattern as needed. *@
</div>

@code {
    [Parameter] public VtracerOptions Options { get; set; } = new();
    [Parameter] public EventCallback<VtracerOptions> OptionsChanged { get; set; }

    private string ColorModeStr
    {
        get => Options.ColorMode.ToString().ToLowerInvariant();
        set => Push(Options with { ColorMode = System.Enum.Parse<VtracerColorMode>(value, ignoreCase: true) });
    }

    private string ModeStr
    {
        get => Options.Mode.ToString().ToLowerInvariant();
        set => Push(Options with { Mode = System.Enum.Parse<VtracerMode>(value, ignoreCase: true) });
    }

    private int FilterSpeckle { get => Options.FilterSpeckle; set => Push(Options with { FilterSpeckle = value }); }
    private int ColorPrecision { get => Options.ColorPrecision; set => Push(Options with { ColorPrecision = value }); }
    private int CornerThreshold { get => Options.CornerThreshold; set => Push(Options with { CornerThreshold = value }); }

    private void Push(VtracerOptions next) => _ = OptionsChanged.InvokeAsync(next);
}
```

Create `src/ControlMenu/Modules/Imaging/Pages/PotraceAdvancedControls.razor`:

```razor
@using ControlMenu.Modules.Imaging.Services.Options

<div class="advanced-grid">
    <label>Turdsize (0-100)</label>
    <input type="range" min="0" max="100" @bind="Turdsize" />
    <span>@Options.Turdsize</span>

    <label>Alphamax (0.0-1.3334)</label>
    <input type="range" min="0" max="1.3334" step="0.01" @bind="Alphamax" />
    <span>@Options.Alphamax.ToString("0.00")</span>

    <label>Opt Tolerance (0.0-1.0)</label>
    <input type="range" min="0" max="1.0" step="0.01" @bind="OptTolerance" />
    <span>@Options.OptTolerance.ToString("0.00")</span>

    <label>Long Curve</label>
    <input type="checkbox" @bind="LongCurve" />

    <label>Binarization Threshold (0-100)</label>
    <input type="range" min="0" max="100" @bind="BinarizationThreshold" />
    <span>@Options.BinarizationThreshold</span>
</div>

@code {
    [Parameter] public PotraceOptions Options { get; set; } = new();
    [Parameter] public EventCallback<PotraceOptions> OptionsChanged { get; set; }

    private int Turdsize { get => Options.Turdsize; set => Push(Options with { Turdsize = value }); }
    private double Alphamax { get => Options.Alphamax; set => Push(Options with { Alphamax = value }); }
    private double OptTolerance { get => Options.OptTolerance; set => Push(Options with { OptTolerance = value }); }
    private bool LongCurve { get => Options.LongCurve; set => Push(Options with { LongCurve = value }); }
    private int BinarizationThreshold { get => Options.BinarizationThreshold; set => Push(Options with { BinarizationThreshold = value }); }

    private void Push(PotraceOptions next) => _ = OptionsChanged.InvokeAsync(next);
}
```

- [ ] **Step 4: Add scoped CSS for the new controls**

Append to `Tracing.razor.css`:

```css
.preset-row {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin: 0.5rem 0 1rem 0;
    max-width: 320px;
}

.preset-row select {
    flex: 1;
}

.advanced-expander {
    margin: 0.5rem 0 1rem 0;
}

.advanced-expander summary {
    cursor: pointer;
    font-weight: 600;
    padding: 0.25rem 0;
}

.advanced-grid {
    display: grid;
    grid-template-columns: max-content 1fr 60px;
    gap: 0.5rem 1rem;
    align-items: center;
    margin-top: 0.5rem;
}
```

- [ ] **Step 5: Build and visually verify in dev server**

Run:
```powershell
dotnet build src\ControlMenu\ControlMenu.csproj -c Debug --nologo /clp:ErrorsOnly
dotnet run --project src\ControlMenu\ControlMenu.csproj -c Debug
```

Visit `/imaging/tracing`, pick an image, verify preset dropdown switches between vtracer/potrace preset lists when engine toggles, verify Advanced expander shows correct per-engine sliders. Stop dev server.

- [ ] **Step 6: Commit**

Run:
```powershell
git add src/ControlMenu/Modules/Imaging/Pages/Tracing.razor src/ControlMenu/Modules/Imaging/Pages/Tracing.razor.css src/ControlMenu/Modules/Imaging/Pages/VtracerAdvancedControls.razor src/ControlMenu/Modules/Imaging/Pages/PotraceAdvancedControls.razor
git commit -m "feat(imaging): Tracing preset dropdown + Advanced controls per engine"
```

### Task G.4.5: Preview pane + Quick Preview button + Full Trace button

**Files:**
- Modify: `src/ControlMenu/Modules/Imaging/Pages/Tracing.razor`
- Modify: `src/ControlMenu/Modules/Imaging/Pages/Tracing.razor.css`

- [ ] **Step 1: Add the preview pane markup**

Add to `Tracing.razor` after the Advanced expander:

```razor
<div class="preview-pane">
    @if (_isStale)
    {
        <div class="stale-badge">Stale — parameters changed since trace. Re-preview or re-trace to update.</div>
    }

    @if (_lastFullTraceResult is not null)
    {
        <div class="preview-caption">Final result</div>
        <div class="svg-render" @key="_lastFullTraceResult.Length">
            @((MarkupString)System.Text.Encoding.UTF8.GetString(_lastFullTraceResult))
        </div>
    }
    else if (_lastPreviewResult is not null)
    {
        <div class="preview-caption">Quick Preview at 512px — final result will differ in detail level.</div>
        <div class="svg-render" @key="_lastPreviewResult.Length">
            @((MarkupString)System.Text.Encoding.UTF8.GetString(_lastPreviewResult))
        </div>
    }
    else
    {
        <div class="preview-caption">Source image (no trace yet)</div>
        <img class="source-img" src="data:image/@(_sourceFormat?.ToLower());base64,@(System.Convert.ToBase64String(_sourceBytes))" alt="@_sourceFilename" />
    }

    <div class="preview-actions">
        @if (_inFlightCts is null)
        {
            <button class="btn btn-secondary" @onclick="QuickPreview">Quick Preview</button>
            <button class="btn btn-primary" @onclick="FullTrace">Trace at full resolution</button>
        }
        else
        {
            <span class="elapsed">@_elapsedSeconds.ToString("0.0")s</span>
            <button class="btn btn-warning" @onclick="Cancel">Cancel</button>
        }
    </div>
</div>
```

- [ ] **Step 2: Add the new fields + handler methods to `@code`**

Add to the `@code` block:

```csharp
private byte[]? _lastPreviewResult;
private byte[]? _lastFullTraceResult;
private CancellationTokenSource? _inFlightCts;
private double _elapsedSeconds;

private async Task QuickPreview()
{
    if (_sourceBytes is null) return;
    var downsampled = DownsampleHelper.ResizeLongestEdge(_sourceBytes, 512);
    await RunTraceAsync(downsampled, isFullRes: false);
}

private async Task FullTrace()
{
    if (_sourceBytes is null) return;
    await RunTraceAsync(_sourceBytes, isFullRes: true);
}

private async Task RunTraceAsync(byte[] bytes, bool isFullRes)
{
    _inFlightCts = new CancellationTokenSource();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var timer = new System.Threading.Timer(_ =>
    {
        _elapsedSeconds = sw.Elapsed.TotalSeconds;
        InvokeAsync(StateHasChanged);
    }, null, 100, 100);

    try
    {
        var opts = BuildTraceOptions();
        var svg = await ImageService.TraceAsync(bytes, _engine, opts, _inFlightCts.Token);
        if (isFullRes)
        {
            _lastFullTraceResult = svg;
            _lastPreviewResult = null;
        }
        else
        {
            _lastPreviewResult = svg;
            _lastFullTraceResult = null;
        }
        _isStale = false;
    }
    catch (System.OperationCanceledException)
    {
        // Surface a brief notification via the existing INotificationService if available;
        // otherwise silent — cancel is user-initiated.
    }
    catch (ImagingException ex)
    {
        // Surface via INotificationService (existing pattern from other Imaging Tools pages).
        await JS.InvokeVoidAsync("alert", $"Trace failed: {ex.Message}");
    }
    finally
    {
        await timer.DisposeAsync();
        _inFlightCts?.Dispose();
        _inFlightCts = null;
        _elapsedSeconds = 0;
        StateHasChanged();
    }
}

private void Cancel() => _inFlightCts?.Cancel();

private TraceOptions BuildTraceOptions()
{
    if (_engine == TraceEngine.Vtracer)
    {
        var v = _preset switch
        {
            "photo" => _vtracerOpts with { Preset = "photo" },
            "poster" => _vtracerOpts with { Preset = "poster" },
            "bw" => _vtracerOpts with { Preset = "bw", ColorMode = VtracerColorMode.Bw },
            "custom" => _vtracerOpts with { Preset = null },
            _ => _vtracerOpts
        };
        return new TraceOptions(Vtracer: v);
    }
    else
    {
        var p = _preset switch
        {
            "default" => new PotraceOptions(),
            "logo-sharp" => _potraceOpts with { Turdsize = 10, Alphamax = 0.8, OptTolerance = 0.5, LongCurve = true },
            "smooth" => _potraceOpts with { Turdsize = 2, Alphamax = 1.3334, OptTolerance = 0.1, LongCurve = true },
            "polygon-only" => _potraceOpts with { Turdsize = 2, Alphamax = 1.0, OptTolerance = 0.2, LongCurve = false },
            "custom" => _potraceOpts,
            _ => _potraceOpts
        };
        return new TraceOptions(Potrace: p);
    }
}
```

- [ ] **Step 3: Create the `DownsampleHelper` (SkiaSharp-based, in-process)**

Create `src/ControlMenu/Modules/Imaging/Services/DownsampleHelper.cs`:

```csharp
using System.IO;
using SkiaSharp;

namespace ControlMenu.Modules.Imaging.Services;

public static class DownsampleHelper
{
    public static byte[] ResizeLongestEdge(byte[] inputBytes, int targetLongestEdge)
    {
        using var inputStream = new MemoryStream(inputBytes);
        using var bitmap = SKBitmap.Decode(inputStream);
        if (bitmap is null)
            throw new ImagingException("DownsampleHelper: SKBitmap.Decode returned null (unsupported format?)");

        var maxDim = System.Math.Max(bitmap.Width, bitmap.Height);
        if (maxDim <= targetLongestEdge)
            return inputBytes;  // already small enough; passthrough

        var scale = (double)targetLongestEdge / maxDim;
        var newW = (int)(bitmap.Width * scale);
        var newH = (int)(bitmap.Height * scale);

        using var resized = bitmap.Resize(new SKImageInfo(newW, newH), SKFilterQuality.High);
        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
```

- [ ] **Step 4: Add scoped CSS for the preview pane**

Append to `Tracing.razor.css`:

```css
.preview-pane {
    border: 1px solid var(--bs-border-color);
    border-radius: 6px;
    padding: 1rem;
    margin: 1rem 0;
    background: var(--bs-body-bg);
    min-height: 300px;
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
}

.preview-caption {
    font-size: 0.85rem;
    color: var(--bs-secondary-color);
    margin-bottom: 0.25rem;
}

.svg-render {
    flex: 1;
    display: flex;
    align-items: center;
    justify-content: center;
    overflow: auto;
    background: repeating-linear-gradient(
        45deg, var(--bs-tertiary-bg), var(--bs-tertiary-bg) 10px,
        var(--bs-body-bg) 10px, var(--bs-body-bg) 20px);
}

.svg-render svg { max-width: 100%; max-height: 600px; }

.source-img { max-width: 100%; max-height: 600px; }

.stale-badge {
    background: var(--bs-warning-bg-subtle);
    color: var(--bs-warning-text-emphasis);
    padding: 0.4rem 0.75rem;
    border-radius: 4px;
    font-size: 0.85rem;
    border-left: 4px solid var(--bs-warning);
}

.preview-actions {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin-top: 0.5rem;
}

.preview-actions .elapsed {
    font-family: monospace;
    color: var(--bs-secondary-color);
}
```

- [ ] **Step 5: Build and manually smoke-test in dev server**

Run:
```powershell
dotnet build src\ControlMenu\ControlMenu.csproj -c Debug --nologo /clp:ErrorsOnly
dotnet run --project src\ControlMenu\ControlMenu.csproj -c Debug
```

Visit `/imaging/tracing`, pick a small color PNG, click "Quick Preview" → wait for the SVG to render inline. Then click "Trace at full resolution" → SVG renders. Switch engine to potrace, repeat with a high-contrast image. Cancel mid-trace to verify cancellation works. Stop dev server.

- [ ] **Step 6: Commit**

Run:
```powershell
git add src/ControlMenu/Modules/Imaging/Pages/Tracing.razor src/ControlMenu/Modules/Imaging/Pages/Tracing.razor.css src/ControlMenu/Modules/Imaging/Services/DownsampleHelper.cs
git commit -m "feat(imaging): Tracing preview pane, Quick Preview + Full Trace + Cancel"
```

### Task G.4.6: Save as SVG + disabled "Open in svgedit" stub

**Files:**
- Modify: `src/ControlMenu/Modules/Imaging/Pages/Tracing.razor`
- Modify: `src/ControlMenu/Modules/Imaging/Pages/Tracing.razor.css`

- [ ] **Step 1: Add the action row markup**

Add to `Tracing.razor` after the closing `</div>` of `.preview-pane`:

```razor
<div class="action-row">
    <button class="btn btn-success" @onclick="SaveSvg" disabled="@(_lastFullTraceResult is null)">
        Save as SVG...
    </button>
    <button class="btn btn-secondary" disabled="true" title="Requires svgedit integration (planned per project_svgedit.md)">
        Open in svgedit ✕
    </button>
</div>
```

- [ ] **Step 2: Add the `SaveSvg` handler**

Add to `@code`:

```csharp
private async Task SaveSvg()
{
    if (_lastFullTraceResult is null) return;
    var defaultName = (_sourceFilename ?? "trace") + ".svg";
    // Replace double extensions (e.g., "foo.png.svg") with single (".svg") if present.
    if (defaultName.EndsWith(".png.svg")) defaultName = defaultName[..^8] + ".svg";
    else if (defaultName.EndsWith(".jpg.svg")) defaultName = defaultName[..^8] + ".svg";
    else if (defaultName.EndsWith(".jpeg.svg")) defaultName = defaultName[..^9] + ".svg";

    await ImagingFilePickerJsInterop.SaveBytesAsync(JS, _lastFullTraceResult, defaultName, "image/svg+xml");
}
```

- [ ] **Step 3: Add scoped CSS for action-row**

Append to `Tracing.razor.css`:

```css
.action-row {
    display: flex;
    gap: 0.5rem;
    margin: 1rem 0;
}

.action-row button:disabled {
    opacity: 0.5;
    cursor: not-allowed;
}
```

- [ ] **Step 4: Build and manually verify**

Run:
```powershell
dotnet build src\ControlMenu\ControlMenu.csproj -c Debug --nologo /clp:ErrorsOnly
dotnet run --project src\ControlMenu\ControlMenu.csproj -c Debug
```

Visit `/imaging/tracing`, pick + full-trace an image. Click "Save as SVG..." → File System Access save picker opens. Save. Verify file written to disk; open in a browser to confirm valid SVG. Stop dev server.

- [ ] **Step 5: Commit**

Run:
```powershell
git add src/ControlMenu/Modules/Imaging/Pages/Tracing.razor src/ControlMenu/Modules/Imaging/Pages/Tracing.razor.css
git commit -m "feat(imaging): Tracing Save as SVG + disabled svgedit-handoff stub"
```

### Task G.4.7: bUnit page tests

**Files:**
- Create: `tests/ControlMenu.Tests/Modules/Imaging/Pages/TracingPageTests.cs`

- [ ] **Step 1: Write the test file**

Create:

```csharp
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using Bunit;
using ControlMenu.Modules.Imaging.Pages;
using ControlMenu.Modules.Imaging.Services;
using ControlMenu.Modules.Imaging.Services.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using Xunit;

namespace ControlMenu.Tests.Modules.Imaging.Pages;

public class TracingPageTests : TestContext
{
    public TracingPageTests()
    {
        Services.AddSingleton(Mock.Of<IImageService>());
        JSInterop.SetupVoid("alert", _ => true);
    }

    [Fact]
    public void Renders_WithPageHeadingAndPickButton()
    {
        var cut = RenderComponent<Tracing>();
        Assert.Contains("Tracing", cut.Find("h2").TextContent);
        Assert.NotNull(cut.Find("button.btn-primary"));  // "Pick image..." button
    }

    [Fact]
    public void EngineToggle_NotVisible_BeforeFilePicked()
    {
        var cut = RenderComponent<Tracing>();
        Assert.Empty(cut.FindAll(".engine-selector"));
    }

    [Fact]
    public void EngineSwitch_FromVtracerToPotrace_ChangesPresetDropdown()
    {
        var cut = RenderComponent<Tracing>();
        // Simulate file picked (set internal state via inspection)
        SimulateFilePicked(cut);

        // Default engine is vtracer; switching to potrace should change preset options.
        var potraceBtn = cut.FindAll(".engine-btn").First(b => b.TextContent.Contains("potrace"));
        potraceBtn.Click();

        var presetOptions = cut.FindAll("#preset option").Select(o => o.TextContent).ToList();
        Assert.Contains("Default", presetOptions);
        Assert.Contains("Logo Sharp", presetOptions);
        Assert.DoesNotContain("Photo", presetOptions);
    }

    [Fact]
    public void QuickPreviewButton_Disabled_BeforeFilePicked()
    {
        var cut = RenderComponent<Tracing>();
        // Engine selector + preview-pane not rendered without file → buttons not present
        Assert.Empty(cut.FindAll(".preview-actions button"));
    }

    [Fact]
    public void SaveButton_Disabled_BeforeFullTraceCompletes()
    {
        var cut = RenderComponent<Tracing>();
        SimulateFilePicked(cut);
        var saveBtn = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Save as SVG"));
        Assert.NotNull(saveBtn);
        Assert.True(saveBtn!.HasAttribute("disabled"));
    }

    private static void SimulateFilePicked(IRenderedComponent<Tracing> cut)
    {
        var instance = cut.Instance;
        var field = typeof(Tracing).GetField("_sourceBytes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(instance, new byte[] { 1, 2, 3 });
        var fileNameField = typeof(Tracing).GetField("_sourceFilename", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        fileNameField.SetValue(instance, "test.png");
        var sourceSizeField = typeof(Tracing).GetField("_sourceSizeBytes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        sourceSizeField.SetValue(instance, 3L);
        var formatField = typeof(Tracing).GetField("_sourceFormat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        formatField.SetValue(instance, "PNG");
        cut.Render();
    }
}
```

- [ ] **Step 2: Run tests — expect 5 passing**

Run:
```powershell
dotnet test tests\ControlMenu.Tests\ControlMenu.Tests.csproj --filter "FullyQualifiedName~TracingPageTests" --no-restore 2>&1 | Select-String -Pattern "Passed|Failed" | Select-Object -First 5
```

Expected: `Passed: 5`.

- [ ] **Step 3: Commit**

Run:
```powershell
git add tests/ControlMenu.Tests/Modules/Imaging/Pages/TracingPageTests.cs
git commit -m "test(imaging): bUnit page tests for Tracing (5 tests)"
```

---

## Phase G.5 — Polish

### Task G.5.1: Warm-up on `OnInitializedAsync`

**Files:**
- Modify: `src/ControlMenu/Modules/Imaging/Pages/Tracing.razor`

- [ ] **Step 1: Inject `ICommandExecutor` + `IDependencyPathResolver`**

Add at the top of `Tracing.razor`:

```razor
@inject ICommandExecutor Executor
@inject IDependencyPathResolver PathResolver
```

- [ ] **Step 2: Override `OnInitializedAsync`**

Add to `@code`:

```csharp
protected override Task OnInitializedAsync()
{
    // Fire-and-forget warm-up for both engines per spec §6.3
    _ = Task.Run(async () =>
    {
        try { await Executor.ExecuteResolvedAsync(PathResolver, "imaging", "vtracer", "--version", cancellationToken: CancellationToken.None); } catch { }
        try { await Executor.ExecuteResolvedAsync(PathResolver, "imaging", "potrace", "--version", cancellationToken: CancellationToken.None); } catch { }
    });
    return Task.CompletedTask;
}
```

- [ ] **Step 3: Build to verify**

Run:
```powershell
dotnet build src\ControlMenu\ControlMenu.csproj -c Debug --nologo /clp:ErrorsOnly
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Commit**

Run:
```powershell
git add src/ControlMenu/Modules/Imaging/Pages/Tracing.razor
git commit -m "feat(imaging): warm-up vtracer + potrace on Tracing page load"
```

### Task G.5.2: Run full test suite to confirm nothing regressed

- [ ] **Step 1: Run all imaging tests**

Run:
```powershell
dotnet test tests\ControlMenu.Tests\ControlMenu.Tests.csproj --filter "FullyQualifiedName~Imaging" --no-restore 2>&1 | Select-String -Pattern "Passed|Failed|Skipped|Total" | Select-Object -First 10
```

Expected: All ~80 imaging tests pass (or skip with magick/vtracer/potrace not staged); 0 failures.

- [ ] **Step 2: Run full project test suite**

Run:
```powershell
dotnet test tests\ControlMenu.Tests\ControlMenu.Tests.csproj --no-restore 2>&1 | Select-String -Pattern "Passed|Failed|Total" | Select-Object -First 5
```

Expected: Total test count ≥ 444 (pre-G.0 baseline) + new tests; 0 failures.

- [ ] **Step 3: No commit** — verification only.

---

## Phase G.6 — Docs + smoke

### Task G.6.1: CHANGELOG entry

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Find the `[Unreleased]` section's `### Added` subsection**

Run:
```powershell
Select-String -Path CHANGELOG.md -Pattern "^## \[Unreleased\]" -Context 0,30 | Select-Object -First 1
```

Note the section structure.

- [ ] **Step 2: Append the Tracing entry under `### Added`**

Add to `CHANGELOG.md` under `[Unreleased]` → `### Added`:

```markdown
- **Imaging Tools → Tracing page** — raster-to-vector conversion via two CLI engines: vtracer (color/photo, upstream `visioncortex/vtracer`) and potrace (B&W/logo, our own `bilbospocketses/potrace-builds` repo that builds 1.16 source via MSYS2/MinGW64 on Windows and gcc on Linux). Engine selector toggle, per-engine preset dropdowns (vtracer: Photo/Poster/B&W/Custom; potrace: Default/Logo Sharp/Smooth/Polygon-only/Custom), collapsed Advanced expander with raw parameter sliders, downsampled Quick Preview (512px) for fast iteration + Full Trace at full resolution for the saveable result, Cancel button while in flight, Save as SVG, disabled "Open in svgedit" stub button (activates when svgedit lands as a CM-embedded page).
```

- [ ] **Step 3: Commit**

Run:
```powershell
git add CHANGELOG.md
git commit -m "docs(changelog): Imaging Tools Tracing page entry under [Unreleased]"
```

### Task G.6.2: TECHNICAL_GUIDE entry

**Files:**
- Modify: `docs/TECHNICAL_GUIDE.md`

- [ ] **Step 1: Locate the Imaging Tools section**

Run:
```powershell
Select-String -Path docs\TECHNICAL_GUIDE.md -Pattern "Imaging Tools|imaging-tools" -Context 0,5 | Select-Object -First 3
```

- [ ] **Step 2: Add a Tracing subsection within Imaging Tools**

Append under the existing Imaging Tools section (immediately after Magic Wand if present):

```markdown
### Tracing (`/imaging/tracing`)

Raster-to-vector conversion. Two CLI engines selectable per trace:

- **vtracer** (Rust, MIT, ~1 MB) — color/photo tracing. Wrapped from upstream `visioncortex/vtracer` v0.6.4 GitHub release.
- **potrace** (C, GPL v2+, ~150 KB) — B&W silhouette tracing (Peter Selinger's reference Bezier-fit algorithm). Built from vendored 1.16 source via our `bilbospocketses/potrace-builds` CI pipeline (MSYS2/MinGW64 for Windows, gcc/build-essential for Linux). potrace operates on B&W; input is converted via magick `-colorspace Gray -threshold N%` pre-step where the threshold is user-configurable.

Both engines wired through `IImageService.TraceAsync` and the existing `ICommandExecutor.ExecuteResolvedAsync` + `IDependencyPathResolver` infrastructure. Per-trace 60s wall-clock timeout. Per-call working directory under `<dataRoot>/temp/imaging/<guid>/`, cleaned in `finally`.

UI exposes per-engine preset dropdowns plus a collapsed Advanced expander for raw parameter access. Iteration loop: downsampled Quick Preview (512px longest edge, sub-second) for parameter tuning, then Full Trace at full resolution for the authoritative saveable output.

License note: potrace's GPL v2+ stays at the binary-subprocess boundary (mere aggregation); Control Menu's own MIT/personal license is unaffected.
```

- [ ] **Step 3: Commit**

Run:
```powershell
git add docs/TECHNICAL_GUIDE.md
git commit -m "docs(guide): Tracing section under Imaging Tools"
```

### Task G.6.3: Manual-test-checklist new section

**Files:**
- Modify: `docs/manual-test-checklist.md`

- [ ] **Step 1: Add a Tracing test section parallel to the other Imaging Tools sections**

Append under the existing Imaging section in `docs/manual-test-checklist.md`:

```markdown
### Imaging → Tracing (`/imaging/tracing`)

Pre-req: vtracer + potrace + magick all installed (verify in Settings → Dependencies).

1. Navigate to Imaging → Tracing. Verify page loads with intro text and "Pick image..." button.
2. Click "Pick image...", select a color PNG (≥256px). Verify filename, dimensions, format, byte-size render in the source-info row.
3. Verify engine toggle defaults to vtracer (color/photo).
4. Verify preset dropdown shows vtracer options: Photo, Poster, B&W, Custom.
5. Click "Quick Preview". Verify spinner + elapsed-time counter, then SVG renders inline with "Quick Preview at 512px" caption.
6. Click "Trace at full resolution". Verify SVG renders inline with "Final result" caption. Verify Save button becomes enabled.
7. Change a preset. Verify "Stale" badge appears on preview pane. Verify Save stays enabled (per spec §4.8).
8. Click "Save as SVG...". Save to disk. Open the saved file in a browser; verify it's a valid SVG with vector paths.
9. Click engine toggle to switch to potrace. Verify preset list changes to: Default, Logo Sharp, Smooth, Polygon-only, Custom. Verify Advanced expander shows potrace-specific controls (Turdsize, Alphamax, Opt Tolerance, Long Curve, Binarization Threshold).
10. With a high-contrast logo PNG, click "Trace at full resolution" with potrace Default. Verify B&W SVG renders. Save and verify.
11. With same image, switch to potrace Logo Sharp preset, re-trace, verify sharper corners visually.
12. Verify "Open in svgedit" button is visible but disabled (greyed); hover for tooltip "Requires svgedit integration".
13. Test Cancel: pick a large image, start Full Trace, hit Cancel mid-flight. Verify the trace stops and the page returns to "no trace" state cleanly.
14. Test error path: deliberately misconfigure (e.g., uninstall magick mid-session via Settings → Dependencies, then trace). Verify an error alert appears with a useful message; verify the page doesn't hang.
15. Test File System Access cancellation: click "Pick image...", press Esc in the file picker. Verify no error; page state unchanged.
```

- [ ] **Step 2: Commit**

Run:
```powershell
git add docs/manual-test-checklist.md
git commit -m "docs(checklist): Tracing manual-test section (15 items)"
```

### Task G.6.4: Manual smoke pass

**Files:** none — verification only.

- [ ] **Step 1: Stage all deps via the seed pipeline**

Run:
```powershell
pwsh scripts\stage-seed.ps1
```

Expected: magick + vtracer + potrace all staged.

- [ ] **Step 2: Run the dev server**

Run:
```powershell
dotnet run --project src\ControlMenu\ControlMenu.csproj -c Release
```

- [ ] **Step 3: Walk through every item in `docs/manual-test-checklist.md` § Imaging → Tracing (items 1-15)**

Use the new manual-test section as your runbook. Take notes on any items that fail.

- [ ] **Step 4: If any items fail, fix on this branch and re-run those items only.**

If multiple items fail with different root causes, escalate to a triage session.

- [ ] **Step 5: If all items pass, stop the dev server and proceed to merge prep.**

`Ctrl+C` in the running terminal.

- [ ] **Step 6: No commit** — smoke is verification only.

---

## Final merge sequence (after all phases of Item 30 — A through G — are complete and smoke-clean)

This sequence is shared with the magick plan; repeated here for reference.

- [ ] Merge `feature/velopack-phase-1-hotfix` → `feature/velopack-phase-1` (already gated on Phase 1 hot-fix smoke per todo_control_menu.md).
- [ ] Merge `feature/velopack-phase-1` → `master`.
- [ ] Tag the next CM release (e.g., `v1.1.0-rc.1` or `v1.1.0`).
- [ ] Watch CI release pipeline cut the MSI.
- [ ] Update `todo_control_menu.md` — move Items 6, 30 from Active to Shipped sections.

---

## Plan summary

| Phase | Tasks | Effort estimate |
|-------|------:|----------------:|
| G.0 — potrace-builds repo | 5 | 1-3 hr |
| G.1 — ImagingModule + seed | 6 | 2-3 hr |
| G.2 — Service contract | 9 | 4-6 hr |
| G.3 — Service integration | 4 | 3-4 hr |
| G.4 — Tracing page | 7 | 8-12 hr |
| G.5 — Polish | 2 | 1 hr |
| G.6 — Docs + smoke | 4 | 2-4 hr |
| **Total** | **37** | **3-5 days** |

**New tests added: ~23** (6 BuildVtracerArgs + 6 BuildPotraceArgs + 4 NormalizeForEngineAsync + 8 TraceAsync integration + 5 bUnit page tests = 29; some overlap may merge), bringing the Imaging Tools module total to ~80-85 tests.

**Files created: 14** (4 docs/scripts external to control-menu; 10 in control-menu).
**Files modified: 6** in control-menu.

---

## Self-review

**Spec coverage:**
- Section 1 (Motivation) — N/A, no code
- Section 2 (Scope: page, deps, out-of-scope) — Task G.4.1 (NavEntry), G.1.1/G.1.2 (deps), out-of-scope items are not implemented (correct)
- Section 3 (Architecture) — G.1.1, G.1.2 (deps), G.2.x (options + interface), G.3.x (TraceAsync + normalization), G.4.x (page)
- Section 4 (Per-engine UX) — G.4.2 (skeleton), G.4.3 (file pick), G.4.4 (presets + advanced), G.4.5 (preview + buttons), G.4.6 (save + svgedit stub), G.5.1 (warm-up)
- Section 5 (potrace-builds repo) — G.0.x (full coverage)
- Section 6 (Cross-cutting concerns) — Timeouts in G.3.3, warm-up in G.5.1, logging via existing ImageService pattern (no separate task — uses inherited convention), file-size cap deferred to page-level upload check (existing magick-spec scope; reused), temp dir hygiene in G.3.3, Settings → Dependencies UI surfaces automatically via the existing dep manager
- Section 7 (Testing) — G.2.6/G.2.7 (vtracer args), G.2.8/G.2.9 (potrace args), G.3.1/G.3.2 (normalization), G.3.4 (TraceAsync integration), G.4.7 (bUnit pages)
- Section 8 (Migration / phase plan) — direct 1:1 mapping with G.0-G.6
- Section 9 (Decisions log) — N/A, no code; decisions baked into task structure

All spec sections have corresponding tasks. No coverage gaps.

**Placeholder scan:** searched for "TBD", "TODO", "fill in", "implement later", "appropriate error handling", "similar to Task". The only intentional "FILL_IN" markers are the `<FILL_IN_AT_FIRST_RUN>` SHA-256 pin placeholders in G.1.3 and G.1.4, which are first-run-only and the tasks explicitly walk through populating them in steps 2-3.

**Type consistency:** verified — `TraceEngine`, `TraceOptions`, `VtracerOptions`, `PotraceOptions`, `VtracerColorMode`, `VtracerMode`, `VtracerHierarchical` are defined in G.2.1-G.2.4 and used consistently in G.2.5-G.3.4. Field names (`FilterSpeckle`, `ColorPrecision`, `Turdsize`, `Alphamax`, etc.) are stable across the option types and the arg-builder test expectations.

**Scope check:** plan is focused on one cohesive feature (the Tracing page) with one bounded external repo (potrace-builds). Within reach of a single implementation session, though large enough to benefit from subagent-per-task execution.

**Ambiguity check:** the `--longcurve` flag inversion is explicitly called out in G.2.9 with a comment + reference to spec §3.7. The bUnit test for engine-switch uses reflection to set private fields — a known pattern from the existing bUnit tests; if Phase A established a different pattern (e.g., a `MakeRenderable` helper), the writer should mirror that instead.
