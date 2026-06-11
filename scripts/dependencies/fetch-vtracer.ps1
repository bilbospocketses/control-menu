# Fetches vtracer (Windows x64) from the visioncortex GitHub release, verifies
# SHA-256, stages into publish/seed/dependencies/vtracer/.
#
# vtracer is the color raster -> SVG tracer for the Imaging "Tracing" tool.
# The release tag has NO "v" prefix (0.6.4, not v0.6.4); the asset is
# vtracer-x86_64-pc-windows-msvc.zip whose archive root contains vtracer.exe
# directly (no nested versioned dir).
#
# Run standalone: pwsh scripts/dependencies/fetch-vtracer.ps1
# Wired into:     scripts/stage-seed.ps1 (auto-discovers fetch-*.ps1)

. "$PSScriptRoot\_Fetcher.ps1"

# ---- Pinned constants (bump together) --------------------------------------
$Version = '0.6.4'
$Url     = "https://github.com/visioncortex/vtracer/releases/download/$Version/vtracer-x86_64-pc-windows-msvc.zip"
$Sha256  = '6b5bc17a6b017129ee40461df254f65d16f3b494c001a8541d41861066b716bf'
# ----------------------------------------------------------------------------

Write-Host "[fetch-vtracer] vtracer v$Version"
$cache   = Get-CmCacheDir -Name 'vtracer' -Version $Version
$zip     = Join-Path $cache 'vtracer.zip'
$extract = Join-Path $cache 'extract'

Invoke-CmDownload -Url $Url -DestFile $zip -ExpectedSha256 $Sha256
if (-not (Get-ChildItem -Path $extract -Recurse -Filter 'vtracer.exe' -ErrorAction SilentlyContinue)) {
    Expand-CmZip -Archive $zip -DestDir $extract
}

# The Windows zip may extract vtracer.exe flat at the root or nested in a
# subfolder (handle both like fetch-magick / fetch-adb do). Locate the dir
# that actually contains vtracer.exe and stage from there.
$exe = Get-ChildItem -Path $extract -Recurse -Filter 'vtracer.exe' | Select-Object -First 1
if (-not $exe) { throw "vtracer.exe not found under $extract after extraction" }
Copy-CmStage -From $exe.DirectoryName -LeafName 'vtracer'
