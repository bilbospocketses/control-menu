# Fetches sqlite3 tools (Windows x64) from sqlite.org, verifies SHA-256,
# stages into publish/seed/dependencies/sqlite3/.

. "$PSScriptRoot\_Fetcher.ps1"

# ---- Pinned constants (bump together) --------------------------------------
$Version    = '3.53.0'
$VersionTag = '3530000'  # sqlite.org uses 3xxYYzz packed form: 3.53.0 -> 3530000
$Url        = "https://sqlite.org/2026/sqlite-tools-win-x64-$VersionTag.zip"
$Sha256     = '64ab964e164e6f34790ac6ef1ee67ba6fa938fe0d5df4fc3dcc9f7ac79064aa8'
# ----------------------------------------------------------------------------

Write-Host "[fetch-sqlite3] sqlite-tools v$Version"
$cache = Get-CmCacheDir -Name 'sqlite3' -Version $Version
$zip = Join-Path $cache 'sqlite-tools.zip'
$extract = Join-Path $cache 'extract'

Invoke-CmDownload -Url $Url -DestFile $zip -ExpectedSha256 $Sha256
if (-not (Test-Path (Join-Path $extract 'sqlite3.exe'))) {
    Expand-CmZip -Archive $zip -DestDir $extract
}

# sqlite-tools zip is flat (sqlite3.exe at archive root) -- no nested dir.
Copy-CmStage -From $extract -LeafName 'sqlite3'
