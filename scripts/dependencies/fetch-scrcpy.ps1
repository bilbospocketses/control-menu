# Fetches scrcpy (Windows x64) from the Genymobile GitHub release, verifies
# SHA-256, stages into publish/seed/dependencies/scrcpy/.

. "$PSScriptRoot\_Fetcher.ps1"

# ---- Pinned constants (bump together) --------------------------------------
$Version = '3.1'
$Url     = "https://github.com/Genymobile/scrcpy/releases/download/v$Version/scrcpy-win64-v$Version.zip"
$Sha256  = '0c05ea395d95cfe36bee974eeb435a3db87ea5594ff738370d5dc3068a9538ca'
# ----------------------------------------------------------------------------

Write-Host "[fetch-scrcpy] scrcpy v$Version"
$cache = Get-CmCacheDir -Name 'scrcpy' -Version $Version
$zip = Join-Path $cache "scrcpy.zip"
$extract = Join-Path $cache 'extract'

Invoke-CmDownload -Url $Url -DestFile $zip -ExpectedSha256 $Sha256
if (-not (Test-Path (Join-Path $extract "scrcpy-win64-v$Version\scrcpy.exe"))) {
    Expand-CmZip -Archive $zip -DestDir $extract
}

# scrcpy zip extracts to a versioned top-level dir; flatten.
Copy-CmStage -From (Join-Path $extract "scrcpy-win64-v$Version") -LeafName 'scrcpy'
