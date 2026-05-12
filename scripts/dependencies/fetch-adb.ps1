# Fetches Android platform-tools (adb) for Windows, verifies SHA-256, stages
# into publish/seed/dependencies/platform-tools/.
#
# Run standalone: pwsh scripts/dependencies/fetch-adb.ps1
# Wired into:     scripts/stage-seed.ps1

. "$PSScriptRoot\_Fetcher.ps1"

# ---- Pinned constants (bump together) --------------------------------------
$Version    = '35.0.2'
$Url        = "https://dl.google.com/android/repository/platform-tools_r$Version-win.zip"
$Sha256     = '2975a3eac0b19182748d64195375ad056986561d994fffbdc64332a516300bb9'
# ----------------------------------------------------------------------------

Write-Host "[fetch-adb] platform-tools v$Version"
$cache = Get-CmCacheDir -Name 'platform-tools' -Version $Version
$zip = Join-Path $cache 'platform-tools.zip'
$extract = Join-Path $cache 'extract'

Invoke-CmDownload -Url $Url -DestFile $zip -ExpectedSha256 $Sha256
if (-not (Test-Path (Join-Path $extract 'platform-tools\adb.exe'))) {
    Expand-CmZip -Archive $zip -DestDir $extract
}

# The Google zip extracts to a top-level platform-tools\ dir; flatten by
# staging from that subfolder.
Copy-CmStage -From (Join-Path $extract 'platform-tools') -LeafName 'platform-tools'
