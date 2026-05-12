# Runs every scripts/dependencies/fetch-*.ps1 in sequence.
# Each fetcher places its output under publish/seed/dependencies/<leafname>/.
# After all fetchers run, the publish/ directory is ready for vpk pack.
#
# Inputs:  scripts/dependencies/fetch-*.ps1 (each downloads + verifies + stages)
# Output:  publish/seed/dependencies/
#           platform-tools/     adb.exe + DLLs
#           scrcpy/             scrcpy.exe + libs
#           sqlite3/            sqlite3.exe
#           go2rtc/             go2rtc.exe
#
# Run standalone:        pwsh scripts/stage-seed.ps1
# Wired into CI release: .github/workflows/release.yml (between dotnet publish
#                        and vpk pack).

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$here = Split-Path -Parent $PSCommandPath
$repoRoot = (Resolve-Path (Join-Path $here '..\')).Path.TrimEnd('\')
$stageRoot = Join-Path $repoRoot 'publish\seed\dependencies'

Write-Host "stage-seed: cleaning $stageRoot"
if (Test-Path $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null

$fetchers = Get-ChildItem -Path (Join-Path $here 'dependencies') -Filter 'fetch-*.ps1' -File
foreach ($f in $fetchers) {
    Write-Host ""
    Write-Host "=== $($f.Name) ==="
    # Dot-source so $ErrorActionPreference = 'Stop' propagates; any throw inside
    # a fetcher then halts stage-seed.ps1 with the original error. Using `&`
    # would isolate exit codes and lose the throw context.
    & $f.FullName
}

Write-Host ""
Write-Host "stage-seed: done. Staged leaves:"
Get-ChildItem -Path $stageRoot -Directory | ForEach-Object {
    $bytes = (Get-ChildItem -Path $_.FullName -Recurse -File | Measure-Object Length -Sum).Sum
    Write-Host ("  {0,-20} {1,12:N0} bytes" -f $_.Name, $bytes)
}
