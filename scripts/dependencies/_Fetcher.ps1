# Shared fetch helpers used by each scripts/dependencies/fetch-*.ps1 script.
# Pinned URL + SHA-256 download, deterministic extract, idempotent cache.
#
# Dot-source from each fetcher:  . "$PSScriptRoot\_Fetcher.ps1"

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Per ws-scrcpy-web's scripts/fetch-*.mjs pattern:
#   - download to <repo>/scripts/dependencies/cache/<name>/v<version>/
#   - verify SHA-256
#   - extract once to cache (idempotent)
#   - stage = copy from cache into publish/seed/dependencies/<leaf>/
#
# The cache layer survives across CI runs (when a runner caches the dir) and
# across local-pack iterations. CI without caching just re-downloads -- still
# correct, just slower.

function Get-CmRepoRoot {
    $here = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $here '..\..\')).Path.TrimEnd('\')
}

function Get-CmDotnet {
    # Resolve the dotnet used to run the Cm7zExtract build tool. Prefer the vendored SDK that
    # local-pack.ps1 bootstraps (scripts/dependencies/dotnet/<version>/dotnet.exe) so build-time
    # tooling uses the same supply-chain-disciplined toolchain when present; fall back to a PATH
    # `dotnet` (which release.yml provides via setup-dotnet). A net10 SDK is required either way.
    $vendoredRoot = Join-Path (Get-CmRepoRoot) 'scripts\dependencies\dotnet'
    if (Test-Path $vendoredRoot) {
        $exe = Get-ChildItem -Path $vendoredRoot -Directory -ErrorAction SilentlyContinue |
            ForEach-Object { Join-Path $_.FullName 'dotnet.exe' } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($exe) { return $exe }
    }
    return 'dotnet'
}

function Get-CmCacheDir {
    param([Parameter(Mandatory)][string] $Name,
          [Parameter(Mandatory)][string] $Version)
    $root = Get-CmRepoRoot
    return Join-Path $root "scripts\dependencies\cache\$Name\v$Version"
}

function Get-CmStageRoot {
    $root = Get-CmRepoRoot
    return Join-Path $root 'publish\seed\dependencies'
}

function Invoke-CmDownload {
    param(
        [Parameter(Mandatory)][string] $Url,
        [Parameter(Mandatory)][string] $DestFile,
        [Parameter(Mandatory)][string] $ExpectedSha256
    )

    $expected = $ExpectedSha256.ToLowerInvariant()
    if (Test-Path $DestFile) {
        $actual = (Get-FileHash -Algorithm SHA256 -Path $DestFile).Hash.ToLowerInvariant()
        if ($actual -eq $expected) {
            Write-Host "  cache HIT  : $DestFile"
            return
        }
        Write-Host "  cache STALE: $DestFile (sha mismatch -- re-downloading)"
        Remove-Item -LiteralPath $DestFile -Force
    }

    $destDir = Split-Path -Parent $DestFile
    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }

    Write-Host "  downloading: $Url"
    # Invoke-WebRequest -OutFile streams to disk (memory-safe for large zips).
    $oldPp = $ProgressPreference; $ProgressPreference = 'SilentlyContinue'
    try {
        Invoke-WebRequest -Uri $Url -OutFile $DestFile -UseBasicParsing
    } finally {
        $ProgressPreference = $oldPp
    }

    $actual = (Get-FileHash -Algorithm SHA256 -Path $DestFile).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        Remove-Item -LiteralPath $DestFile -Force
        throw "SHA256 mismatch for $Url`n  expected: $expected`n  actual:   $actual"
    }
    Write-Host "  sha OK     : $expected"
}

function Expand-CmZip {
    param(
        [Parameter(Mandatory)][string] $Archive,
        [Parameter(Mandatory)][string] $DestDir
    )
    if (Test-Path $DestDir) { Remove-Item -LiteralPath $DestDir -Recurse -Force }
    New-Item -ItemType Directory -Path $DestDir -Force | Out-Null
    Write-Host "  extracting : $Archive -> $DestDir"
    Expand-Archive -LiteralPath $Archive -DestinationPath $DestDir -Force
}

function Expand-Cm7z {
    param(
        [Parameter(Mandatory)][string] $Archive,
        [Parameter(Mandatory)][string] $DestDir
    )
    # Some deps (ImageMagick) ship .7z portables that Expand-Archive can't read. Extract them with
    # the bundled SharpCompress library via the Cm7zExtract build tool -- NOT a vendored 7zr.exe.
    # SharpCompress is already an app dependency (it also does the runtime dependency-update
    # extraction in ArchiveExtractor), so there is no separate, unversioned, unsigned binary to
    # vendor and re-pin on every 7-Zip release. Local-Dependencies-Only: the extractor is a declared
    # NuGet package restored into the tool's own build output, never a PATH-resolved or
    # trust-on-first-use binary.
    if (Test-Path $DestDir) { Remove-Item -LiteralPath $DestDir -Recurse -Force }
    New-Item -ItemType Directory -Path $DestDir -Force | Out-Null
    $tool = Join-Path (Get-CmRepoRoot) 'tools\Cm7zExtract\Cm7zExtract.csproj'
    $dotnet = Get-CmDotnet
    Write-Host "  extracting : $Archive -> $DestDir (SharpCompress via $dotnet)"
    & $dotnet run --project $tool -c Release --verbosity quiet -- $Archive $DestDir
    if ($LASTEXITCODE -ne 0) { throw "SharpCompress .7z extraction failed (exit $LASTEXITCODE): $Archive" }
}

function Copy-CmStage {
    param(
        [Parameter(Mandatory)][string] $From,
        [Parameter(Mandatory)][string] $LeafName
    )
    $stage = Join-Path (Get-CmStageRoot) $LeafName
    if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Write-Host "  staging    : $From -> $stage"
    Copy-Item -Path (Join-Path $From '*') -Destination $stage -Recurse -Force
}
