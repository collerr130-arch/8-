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
# Find the .NET SDK, or install it for this user (no admin rights, no winget needed).
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
$localDotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
if (-not $dotnet -and (Test-Path $localDotnet)) { $dotnet = $localDotnet }
if (-not $dotnet) {
    Write-Host "Installing .NET SDK 8 (one time, a few minutes)..." -ForegroundColor Yellow
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $installer = "$env:TEMP\dotnet-install.ps1"
    Invoke-WebRequest "https://dot.net/v1/dotnet-install.ps1" -OutFile $installer -UseBasicParsing
    & $installer -Channel 8.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"
    $dotnet = $localDotnet
}

& $dotnet build -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$modDir = "$GameDir\BladeAndSorcery_Data\StreamingAssets\Mods\HomingThrow"
New-Item -ItemType Directory -Force $modDir | Out-Null
Copy-Item "bin\Release\HomingThrow.dll" $modDir -Force
Copy-Item "manifest.json" $modDir -Force
Write-Host "Installed to $modDir" -ForegroundColor Green
