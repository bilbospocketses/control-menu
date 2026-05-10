#!/usr/bin/env pwsh
# Local Velopack pack script. Builds Setup.exe locally without burning
# Trusted Signing quota. Used for fresh-VM smoke iteration.
#
# Usage: pwsh scripts/local-pack.ps1 -Version 1.1.0-alpha.1
#
# Prerequisites:
#   - .NET 10 SDK installed (verify: dotnet --list-sdks)
#   - vpk CLI installed: dotnet tool install -g Velopack.Vpk
#
# Note: --instLocation (PerMachine) is NOT a vpk pack CLI flag.
# PerMachine install is configured via VelopackApp.Build() in the C# launcher
# (src/ControlMenuLauncher/Program.cs). No pack-time flag needed.
#
# Note: --url (GitHub source feed) is NOT a vpk pack CLI flag.
# The GitHub release URL is configured via UpdateManager(new GitHubSource(...))
# in the C# update service (src/ControlMenu/Services/VelopackUpdateService.cs).
#
# Note: vpk does not support a vpk.config file; all args are CLI-only.
#
# Note: --icon expects a .ico file on Windows for best results. This script
# passes favicon.png as a fallback; create assets/app.ico (Phase 2) for a
# proper installer icon.

param(
    [Parameter(Mandatory)] [string]$Version
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$publishDir = Join-Path $repo 'publish'
$releasesDir = Join-Path $repo 'Releases'

Write-Host "Velopack Phase 1 local pack -- version $Version"
Write-Host ""

# Pre-flight: verify vpk is on PATH
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "vpk CLI not found on PATH. Install with: dotnet tool install -g Velopack.Vpk"
}

Write-Host "Cleaning publish + Releases dirs..."
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
if (Test-Path $releasesDir) { Remove-Item -Recurse -Force $releasesDir }

Write-Host "Publishing ControlMenu.exe (Blazor Server host)..."
dotnet publish "$repo/src/ControlMenu/ControlMenu.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDir `
    -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw "ControlMenu publish failed (exit $LASTEXITCODE)" }

Write-Host "Publishing ControlMenuLauncher.exe (Velopack supervisor)..."
dotnet publish "$repo/src/ControlMenuLauncher/ControlMenuLauncher.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDir `
    -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw "ControlMenuLauncher publish failed (exit $LASTEXITCODE)" }

Write-Host "Publishing ControlMenuTray.exe (Phase 1 stub)..."
dotnet publish "$repo/src/ControlMenuTray/ControlMenuTray.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDir `
    -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw "ControlMenuTray publish failed (exit $LASTEXITCODE)" }

Write-Host "Running vpk pack..."
# All args via CLI flags (vpk does not support a config file).
# Short aliases: -u=packId, -v=packVersion, -p=packDir, -e=mainExe, -i=icon, -o=outputDir
vpk pack `
    --packId ControlMenu `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe ControlMenuLauncher.exe `
    --packTitle "Control Menu" `
    --packAuthors "bilbospocketses" `
    --icon "$repo/src/ControlMenu/wwwroot/favicon.png" `
    --outputDir $releasesDir
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed (exit $LASTEXITCODE)" }

Write-Host ""
Write-Host "Pack complete. Output in: $releasesDir"
Get-ChildItem $releasesDir | Format-Table Name, Length -AutoSize
