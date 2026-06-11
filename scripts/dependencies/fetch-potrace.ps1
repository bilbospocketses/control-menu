# Fetches potrace (Windows x64) from the upstream SourceForge release, verifies
# SHA-256, stages into publish/seed/dependencies/potrace/.
#
# potrace is the B&W raster -> SVG tracer for the Imaging "Tracing" tool.
# Sourced direct from upstream (potrace.sourceforge.net) -- no GitHub mirror.
# potrace-1.16.win64.zip extracts to a NESTED potrace-1.16.win64/ dir that
# contains potrace.exe (+ mkbitmap.exe, docs), so stage that subfolder.
#
# Run standalone: pwsh scripts/dependencies/fetch-potrace.ps1
# Wired into:     scripts/stage-seed.ps1 (auto-discovers fetch-*.ps1)

. "$PSScriptRoot\_Fetcher.ps1"

# ---- Pinned constants (bump together) --------------------------------------
$Version = '1.16'
$Url     = "https://potrace.sourceforge.net/download/1.16/potrace-1.16.win64.zip"
$Sha256  = '63699f9885b5ed053977fd94452a7bdbb77ee62b77bf28a3f17b2f14b9b9c65d'
# ----------------------------------------------------------------------------

Write-Host "[fetch-potrace] potrace v$Version"
$cache   = Get-CmCacheDir -Name 'potrace' -Version $Version
$zip     = Join-Path $cache 'potrace.zip'
$extract = Join-Path $cache 'extract'

Invoke-CmDownload -Url $Url -DestFile $zip -ExpectedSha256 $Sha256
if (-not (Get-ChildItem -Path $extract -Recurse -Filter 'potrace.exe' -ErrorAction SilentlyContinue)) {
    Expand-CmZip -Archive $zip -DestDir $extract
}

# The upstream zip extracts to a nested potrace-1.16.win64/ dir containing
# potrace.exe; locate that dir and stage from it (flattening the version dir).
$exe = Get-ChildItem -Path $extract -Recurse -Filter 'potrace.exe' | Select-Object -First 1
if (-not $exe) { throw "potrace.exe not found under $extract after extraction" }
Copy-CmStage -From $exe.DirectoryName -LeafName 'potrace'
