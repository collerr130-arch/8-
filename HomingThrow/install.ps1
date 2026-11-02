# Builds HomingThrow and copies it into the game's Mods folder.
# Usage:  powershell -ExecutionPolicy Bypass -File install.ps1 [-GameDir "D:\Games\Blade & Sorcery"]
param([string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Blade & Sorcery")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Test-Path "$GameDir\BladeAndSorcery_Data\Managed\ThunderRoad.dll")) {
    Write-Host "Blade & Sorcery not found in: $GameDir" -ForegroundColor Red
    Write-Host "Steam -> Blade & Sorcery -> Manage -> Browse local files, copy that path and run:"
    Write-Host '  powershell -ExecutionPolicy Bypass -File install.ps1 -GameDir "<path>"'
    exit 1
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "Install the .NET SDK first: winget install Microsoft.DotNet.SDK.8" -ForegroundColor Red
    exit 1
}

dotnet build -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$modDir = "$GameDir\BladeAndSorcery_Data\StreamingAssets\Mods\HomingThrow"
New-Item -ItemType Directory -Force $modDir | Out-Null
Copy-Item "bin\Release\HomingThrow.dll" $modDir -Force
Copy-Item "manifest.json" $modDir -Force
Write-Host "Installed to $modDir" -ForegroundColor Green
