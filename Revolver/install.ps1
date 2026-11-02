# Builds Revolver and copies it into the game's Mods folder.
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
# A dotnet.exe with only the runtime cannot build, so look for one that lists an SDK.
function Test-Sdk($exe) { $exe -and (Test-Path $exe) -and (& $exe --list-sdks 2>$null | Select-Object -First 1) }
$localDotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
$dotnet = $null
$systemDotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (Test-Sdk $systemDotnet) { $dotnet = $systemDotnet }
elseif (Test-Sdk $localDotnet) { $dotnet = $localDotnet }
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

$modDir = "$GameDir\BladeAndSorcery_Data\StreamingAssets\Mods\Revolver"
New-Item -ItemType Directory -Force $modDir | Out-Null
Copy-Item "bin\Release\Revolver.dll" $modDir -Force
Copy-Item "manifest.json" $modDir -Force
New-Item -ItemType Directory -Force "$modDir\Items" | Out-Null
Copy-Item "Items\Item_Revolver.json" "$modDir\Items" -Force
Write-Host "Installed to $modDir" -ForegroundColor Green
