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
    # Some deps (ImageMagick) ship .7z portables. Expand-Archive can't read .7z,
    # so shell out to 7-Zip. CI (windows-latest) has 7z on PATH; locally we fall
    # back to the default install dir. 7z is a BUILD tool (like vpk / dotnet),
    # not a bundled app dependency, so a system 7z is acceptable for staging.
    $sevenZip = (Get-Command 7z -ErrorAction SilentlyContinue).Source
    if (-not $sevenZip) {
        $candidate = Join-Path $env:ProgramFiles '7-Zip\7z.exe'
        if (Test-Path $candidate) { $sevenZip = $candidate }
    }
    if (-not $sevenZip) {
        throw "7-Zip not found. CI runners have 7z on PATH; install 7-Zip locally to extract .7z deps."
    }
    if (Test-Path $DestDir) { Remove-Item -LiteralPath $DestDir -Recurse -Force }
    New-Item -ItemType Directory -Path $DestDir -Force | Out-Null
    Write-Host "  extracting : $Archive -> $DestDir (7z)"
    & $sevenZip x $Archive "-o$DestDir" -y -bso0 -bsp0
    if ($LASTEXITCODE -ne 0) { throw "7z extraction failed (exit $LASTEXITCODE): $Archive" }
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
