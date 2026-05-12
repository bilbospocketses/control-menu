# Fetches go2rtc (Windows x64) from the AlexxIT GitHub release, verifies
# SHA-256, stages into publish/seed/dependencies/go2rtc/.
#
# Note: go2rtc ships go2rtc_win64.zip whose archive root contains the
# go2rtc.exe binary directly (no nested versioned dir).

. "$PSScriptRoot\_Fetcher.ps1"

# ---- Pinned constants (bump together) --------------------------------------
$Version = '1.9.9'
$Url     = "https://github.com/AlexxIT/go2rtc/releases/download/v$Version/go2rtc_win64.zip"
$Sha256  = '3259a04c402cd22b39092119151140d91b7d9898591c8d94422c6f84eddf7380'
# ----------------------------------------------------------------------------

Write-Host "[fetch-go2rtc] go2rtc v$Version"
$cache = Get-CmCacheDir -Name 'go2rtc' -Version $Version
$zip = Join-Path $cache 'go2rtc.zip'
$extract = Join-Path $cache 'extract'

Invoke-CmDownload -Url $Url -DestFile $zip -ExpectedSha256 $Sha256
if (-not (Test-Path (Join-Path $extract 'go2rtc.exe'))) {
    Expand-CmZip -Archive $zip -DestDir $extract
}

Copy-CmStage -From $extract -LeafName 'go2rtc'
