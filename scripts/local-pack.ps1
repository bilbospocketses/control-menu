#!/usr/bin/env pwsh
# Local Velopack pack script. Builds Setup.msi locally without burning
# Trusted Signing quota. Used for fresh-VM smoke iteration.
#
# Usage: pwsh scripts/local-pack.ps1 -Version 1.1.0-beta.1
#
# Prerequisites:
#   - Internet access on first run (bootstraps vendored .NET 10 SDK +
#     installs vpk globally if absent)
#   - ~700MB free disk for the vendored .NET 10 SDK
#
# .NET 10 SDK: auto-vendored to scripts/dependencies/dotnet/10.0.203/
#   on first run via dot.net/v1/dotnet-install.ps1. Subsequent runs are
#   offline-capable once the SDK is present.
#
# vpk CLI: installed globally via dotnet tool install -g, matching
#   ws-scrcpy-web's release.yml pattern (NOT vendored under
#   scripts/dependencies/). Version pinned to match the NuGet package pin
#   in the csprojs.
#
# Output: Releases/ControlMenu-<version>-Setup.msi + delta/full nupkgs +
#   RELEASES.win.json feed. The MSI installs PerMachine to
#   C:\Program Files\ControlMenu\ with a UAC elevation prompt.

param(
    [Parameter(Mandatory)] [string]$Version
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$publishDir = Join-Path $repo 'publish'
$releasesDir = Join-Path $repo 'Releases'

Write-Host "Velopack Phase 1 local pack -- version $Version"
Write-Host ""

# ---------------------------------------------------------------------------
# Resolve vendored .NET 10 SDK
# ---------------------------------------------------------------------------
function Resolve-VendoredDotnet {
    $dotnetVersion = '10.0.203'
    $dotnetDir = Join-Path $repo "scripts/dependencies/dotnet/$dotnetVersion"
    $dotnetExe = Join-Path $dotnetDir 'dotnet.exe'
    if (-not (Test-Path $dotnetExe)) {
        Write-Host "Bootstrapping vendored dotnet $dotnetVersion to $dotnetDir..."
        $installScript = Join-Path $env:TEMP 'dotnet-install-bootstrap.ps1'
        Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installScript
        & $installScript -InstallDir $dotnetDir -Version $dotnetVersion
        Remove-Item $installScript
        if (-not (Test-Path $dotnetExe)) {
            throw "dotnet bootstrap failed: $dotnetExe still absent after install"
        }
        Write-Host "Vendored dotnet $dotnetVersion ready."
    }
    return $dotnetExe
}

# ---------------------------------------------------------------------------
# Resolve vpk (global tool, mirrors ws-scrcpy-web release.yml)
# ---------------------------------------------------------------------------
function Resolve-Vpk {
    $vpkVersion = '1.2.0'
    $listed = dotnet tool list -g | Select-String -Pattern "^vpk\s+$([regex]::Escape($vpkVersion))\s"
    if (-not $listed) {
        Write-Host "Installing vpk $vpkVersion globally (matches the Velopack NuGet pin)..."
        # Uninstall any older vpk first to avoid version conflict
        dotnet tool uninstall -g vpk 2>$null | Out-Null
        dotnet tool install -g vpk --version $vpkVersion
        if ($LASTEXITCODE -ne 0) { throw "vpk install failed (exit $LASTEXITCODE)" }
        Write-Host "vpk $vpkVersion installed."
    }
    return 'vpk'
}

# ---------------------------------------------------------------------------
# Clean output dirs
# ---------------------------------------------------------------------------
Write-Host "Cleaning publish + Releases dirs..."
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
if (Test-Path $releasesDir) { Remove-Item -Recurse -Force $releasesDir }

# ---------------------------------------------------------------------------
# Publish all three projects via vendored dotnet
# ---------------------------------------------------------------------------
$dotnetExe = Resolve-VendoredDotnet

Write-Host "Publishing ControlMenu.exe (Blazor Server host)..."
& $dotnetExe publish "$repo/src/ControlMenu/ControlMenu.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDir `
    -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw "ControlMenu publish failed (exit $LASTEXITCODE)" }

Write-Host "Publishing ControlMenuLauncher.exe (Velopack supervisor)..."
& $dotnetExe publish "$repo/src/ControlMenuLauncher/ControlMenuLauncher.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDir `
    -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw "ControlMenuLauncher publish failed (exit $LASTEXITCODE)" }

Write-Host "Publishing ControlMenuTray.exe (Phase 1 stub)..."
& $dotnetExe publish "$repo/src/ControlMenuTray/ControlMenuTray.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDir `
    -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw "ControlMenuTray publish failed (exit $LASTEXITCODE)" }

# ---------------------------------------------------------------------------
# vpk pack -- PerMachine MSI, matching ws-scrcpy-web release.yml:99-110
# ---------------------------------------------------------------------------
Resolve-Vpk | Out-Null

Write-Host "Running vpk pack..."
vpk pack `
    --packId ControlMenu `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe ControlMenuLauncher.exe `
    --packTitle "Control Menu" `
    --packAuthors "bilbospocketses" `
    --channel stable `
    --icon "$repo/src/ControlMenu/wwwroot/favicon.ico" `
    --msi `
    --instLocation PerMachine `
    -o $releasesDir
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed (exit $LASTEXITCODE)" }

Write-Host ""
Write-Host "Pack complete. Output in: $releasesDir"
Get-ChildItem $releasesDir | Format-Table Name, Length -AutoSize
