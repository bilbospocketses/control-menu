#!/usr/bin/env pwsh
#requires -Version 7
<#
.SYNOPSIS
    Prints the current upstream SHA-256 for each managed dependency so a
    maintainer can refresh ModuleDependency.KnownHashes.

.DESCRIPTION
    For each hash-pinnable dependency, downloads the current upstream-latest
    artifact, computes SHA-256, and emits a line:

        <name>  <version-key>  <sha256-hex>

    Version-key contract (MUST match DependencyManagerService.ResolveTargetVersion):
      - GitHub source: latest release tag with leading 'v' stripped
            e.g. tag "v1.9.9" -> key "1.9.9"  (go2rtc, vtracer, magick)
            Note: vtracer tags carry no 'v' prefix - strip is a no-op.
      - DirectUrl / pinned URL: version resolved from the upstream version-check
        URL using the same regex the app applies (CheckDirectUrlVersionAsync).
            adb    -> major.minor.micro joined with '.' from repository2-3.xml
            sqlite -> x.y.z from the sqlite.org/download.html version line
            potrace -> version embedded in the pinned DownloadUrl (1.16)

    Paste each emitted line into the KnownHashes dictionary in the matching
    Module file, e.g.:
        KnownHashes = new Dictionary<string, string>
        {
            { "1.9.9", "abcdef..." }
        },

    ASCII-only. No external binaries - uses only Invoke-WebRequest,
    Invoke-RestMethod, and Get-FileHash.

.NOTES
    Requires PowerShell 7+ and outbound HTTPS.
    Downloads are staged to the system temp directory and cleaned up after
    each dep regardless of success or failure.
#>

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Resolve-GitHubLatest {
    <#
    .SYNOPSIS
        Returns @{ Tag = <tag without leading v>; AssetUrl = <download url> }
        for the first asset whose name matches AssetPattern.
    #>
    param(
        [string]$Repo,
        [string]$AssetPattern
    )

    $apiUrl = "https://api.github.com/repos/$Repo/releases/latest"
    $headers = @{
        'Accept'     = 'application/vnd.github+json'
        'User-Agent' = 'ControlMenu-HashRefresh/1.0'
    }

    $release = Invoke-RestMethod -Uri $apiUrl -Headers $headers -UseBasicParsing

    $tag = $release.tag_name -replace '^v', ''

    $asset = $release.assets |
        Where-Object { $_.name -match $AssetPattern } |
        Select-Object -First 1

    if (-not $asset) {
        throw "No asset matching '$AssetPattern' found in $Repo release $($release.tag_name)"
    }

    return @{ Tag = $tag; AssetUrl = $asset.browser_download_url }
}

function Get-RemoteHash {
    <#
    .SYNOPSIS
        Downloads $Url to a temp file, returns its SHA-256 hex string (lower).
        Cleans up the temp file in all cases.
    #>
    param([string]$Url)

    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("cm-hash-" + [Guid]::NewGuid().ToString("N"))
    try {
        Invoke-WebRequest -Uri $Url -OutFile $tmp -UseBasicParsing
        return (Get-FileHash -Algorithm SHA256 -LiteralPath $tmp).Hash.ToLower()
    }
    finally {
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    }
}

function Emit {
    param([string]$Name, [string]$Version, [string]$Hash)
    Write-Host ("{0,-10} {1,-20} {2}" -f $Name, $Version, $Hash)
}

function Fail {
    param([string]$Name, [string]$Reason)
    Write-Warning ("SKIP {0}: {1}" -f $Name, $Reason)
}

# ---------------------------------------------------------------------------
# Header
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Control Menu - Dependency Hash Refresh"
Write-Host "---------------------------------------"
Write-Host ("Name       Version              SHA-256")
Write-Host ("---------- -------------------- ----------------------------------------------------------------")

# ---------------------------------------------------------------------------
# go2rtc  (GitHub: AlexxIT/go2rtc, asset: go2rtc_win64.zip)
# ---------------------------------------------------------------------------

try {
    $r = Resolve-GitHubLatest -Repo "AlexxIT/go2rtc" -AssetPattern "go2rtc_win64\.zip"
    $hash = Get-RemoteHash -Url $r.AssetUrl
    Emit "go2rtc" $r.Tag $hash
}
catch {
    Fail "go2rtc" $_
}

# ---------------------------------------------------------------------------
# vtracer  (GitHub: visioncortex/vtracer, asset: vtracer-x86_64-pc-windows-msvc.zip)
# Note: vtracer tags carry no 'v' prefix (e.g. "0.6.4") - TrimStart('v') is a no-op.
# ---------------------------------------------------------------------------

try {
    $r = Resolve-GitHubLatest -Repo "visioncortex/vtracer" -AssetPattern "vtracer-x86_64-pc-windows-msvc\.zip"
    $hash = Get-RemoteHash -Url $r.AssetUrl
    Emit "vtracer" $r.Tag $hash
}
catch {
    Fail "vtracer" $_
}

# ---------------------------------------------------------------------------
# magick  (GitHub: ImageMagick/ImageMagick, asset: portable Q8-x64 .7z)
# ---------------------------------------------------------------------------

try {
    $r = Resolve-GitHubLatest -Repo "ImageMagick/ImageMagick" -AssetPattern "ImageMagick-[\d.]+-\d+-portable-Q8-x64\.7z"
    $hash = Get-RemoteHash -Url $r.AssetUrl
    Emit "magick" $r.Tag $hash
}
catch {
    Fail "magick" $_
}

# ---------------------------------------------------------------------------
# adb  (DirectUrl: platform-tools-latest-windows.zip)
# Version key: major.minor.micro joined with '.' from repository2-3.xml
# (matches CheckDirectUrlVersionAsync with VersionCheckPattern having 3 groups)
# ---------------------------------------------------------------------------

try {
    $xmlUrl = "https://dl.google.com/android/repository/repository2-3.xml"
    $xmlContent = (Invoke-WebRequest -Uri $xmlUrl -UseBasicParsing).Content
    $versionPattern = 'path="platform-tools".*?<major>(\d+)</major>\s*<minor>(\d+)</minor>\s*<micro>(\d+)</micro>'
    $m = [regex]::Match($xmlContent, $versionPattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $m.Success) { throw "Could not parse adb version from repository2-3.xml" }
    $adbVersion = "$($m.Groups[1].Value).$($m.Groups[2].Value).$($m.Groups[3].Value)"

    $hash = Get-RemoteHash -Url "https://dl.google.com/android/repository/platform-tools-latest-windows.zip"
    Emit "adb" $adbVersion $hash
}
catch {
    Fail "adb" $_
}

# ---------------------------------------------------------------------------
# potrace  (DirectUrl: pinned 1.16 from SourceForge)
# No VersionCheckUrl - version is embedded in the pinned DownloadUrl.
# Key: "1.16"
# ---------------------------------------------------------------------------

try {
    $hash = Get-RemoteHash -Url "https://potrace.sourceforge.net/download/1.16/potrace-1.16.win64.zip"
    Emit "potrace" "1.16" $hash
}
catch {
    Fail "potrace" $_
}

# ---------------------------------------------------------------------------
# sqlite3  (DirectUrl: version from sqlite.org/download.html)
# Version key: x.y.z from the structured CSV comment block SQLite embeds in
# download.html for script consumption (format: PRODUCT,VERSION,RELATIVE-URL,...).
# The href attributes start as hp1.html and are rewritten by JavaScript, so
# the CSV comment block is the only reliable machine-readable source.
# ---------------------------------------------------------------------------

try {
    $dlPage = (Invoke-WebRequest -Uri "https://www.sqlite.org/download.html" -UseBasicParsing).Content

    # SQLite embeds a CSV block in an HTML comment for script consumption.
    # Lines look like: PRODUCT,3.53.2,2026/sqlite-tools-win-x64-3530200.zip,...
    $csvLine = ($dlPage -split "`n") |
        Where-Object { $_ -match '^PRODUCT,[\d.]+,\d{4}/sqlite-tools-win-x64-' } |
        Select-Object -First 1

    if (-not $csvLine) { throw "Could not locate sqlite-tools-win-x64 CSV line in download.html" }

    $parts = $csvLine.Trim() -split ','
    $sqliteVersion = $parts[1]
    $relativeUrl   = $parts[2]
    $sqliteUrl     = "https://sqlite.org/$relativeUrl"

    $hash = Get-RemoteHash -Url $sqliteUrl
    Emit "sqlite3" $sqliteVersion $hash
}
catch {
    Fail "sqlite3" $_
}

Write-Host ""
Write-Host "Paste the version/hash pairs above into the KnownHashes dictionaries"
Write-Host "in src/ControlMenu/Modules/*Module.cs files."
Write-Host ""
