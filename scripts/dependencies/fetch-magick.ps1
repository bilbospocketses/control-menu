# Fetches ImageMagick portable Q8 x64 for Windows from the GitHub release,
# verifies SHA-256, stages into publish/seed/dependencies/magick/.
#
# ImageMagick ships Windows portables as .7z on GitHub releases (the
# imagemagick.org/archive/binaries .zip path returns 404 for current builds).
# The .7z extracts FLAT -- magick.exe + DLLs + *.xml config at the archive root,
# no nested versioned dir.
#
# Run standalone: pwsh scripts/dependencies/fetch-magick.ps1
# Wired into:     scripts/stage-seed.ps1

. "$PSScriptRoot\_Fetcher.ps1"

# ---- Pinned constants (bump together) --------------------------------------
$Version = '7.1.2-25'
$Url     = "https://github.com/ImageMagick/ImageMagick/releases/download/$Version/ImageMagick-$Version-portable-Q8-x64.7z"
$Sha256  = 'ff7c559f51bad365e3662f004aaed0e18c937d110f6e01183363602c07246e40'
# ----------------------------------------------------------------------------

Write-Host "[fetch-magick] ImageMagick portable Q8 x64 v$Version"
$cache   = Get-CmCacheDir -Name 'magick' -Version $Version
$archive = Join-Path $cache 'magick.7z'
$extract = Join-Path $cache 'extract'

Invoke-CmDownload -Url $Url -DestFile $archive -ExpectedSha256 $Sha256
if (-not (Test-Path (Join-Path $extract 'magick.exe'))) {
    Expand-Cm7z -Archive $archive -DestDir $extract
}

# The portable .7z extracts magick.exe + DLLs + config xml flat at the root,
# so stage the extract dir directly (no nested subfolder to flatten).
Copy-CmStage -From $extract -LeafName 'magick'

# Overwrite the bundled default policy.xml with our hardened override (deny-all
# coders + v1 format allowlist + resource caps). magick.exe reads policy.xml
# from its own directory, so placing it alongside the staged magick.exe is all
# that is needed -- no MAGICK_CONFIGURE_PATH env var required.
$policySrc = Join-Path $PSScriptRoot '..\..\src\ControlMenu\Modules\Imaging\Resources\magick-policy.xml'
$policyDst = Join-Path (Get-CmStageRoot) 'magick\policy.xml'
Copy-Item -LiteralPath $policySrc -Destination $policyDst -Force
Write-Host "  policy     : staged custom magick-policy.xml -> $policyDst"
