#Requires -Version 5.1
<#
  Builds the app payload and wraps it into a single-file installer:
    dist\DeskBoxToDo_2.0.0_Setup.exe
  The installer is per-user (no UAC), overwrites an existing 1.x/2.x install in
  place and keeps user data in %LOCALAPPDATA%\DeskBoxToDo.
#>
param(
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "installer\DeskBoxToDo.Setup"
$version = "2.0.3"

if (-not $SkipPublish) {
    & (Join-Path $root "publish-winui.ps1")
    if ($LASTEXITCODE -ne 0) { throw "publish-winui.ps1 failed" }
}

$payload = Join-Path $root "dist\DeskBoxToDo_$version.zip"
if (-not (Test-Path $payload)) { throw "payload not found: $payload" }

Copy-Item $payload (Join-Path $project "payload.zip") -Force
Copy-Item (Join-Path $root "artifacts\DeskBoxToDo_$version\app.ico") (Join-Path $project "app.ico") -Force

Write-Host "building uninstaller stub..."
$uninstallerOut = Join-Path $root "artifacts\uninstaller"
if (Test-Path $uninstallerOut) { Remove-Item $uninstallerOut -Recurse -Force }
dotnet publish (Join-Path $project "DeskBoxToDo.Setup.csproj") -c Release -r win-x64 --self-contained false `
    -p:SetupPayload=0 -p:PublishSingleFile=true `
    -o $uninstallerOut --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw "uninstaller build failed" }

Copy-Item (Join-Path $uninstallerOut "Uninstall.exe") (Join-Path $project "uninstaller.exe") -Force

Write-Host "building setup..."
$setupOut = Join-Path $root "artifacts\setup"
if (Test-Path $setupOut) { Remove-Item $setupOut -Recurse -Force }
dotnet publish (Join-Path $project "DeskBoxToDo.Setup.csproj") -c Release -r win-x64 --self-contained false `
    -p:SetupPayload=1 -p:PublishSingleFile=true `
    -o $setupOut --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw "setup build failed" }

$setupExe = Join-Path $setupOut "DeskBoxToDo_$version`_Setup.exe"
if (-not (Test-Path $setupExe)) { throw "setup exe missing: $setupExe" }

Copy-Item $setupExe (Join-Path $root "dist\DeskBoxToDo_$version`_Setup.exe") -Force
$size = [math]::Round((Get-Item (Join-Path $root "dist\DeskBoxToDo_$version`_Setup.exe")).Length / 1MB, 1)
Write-Host "DeskBoxToDo_$version`_Setup.exe  $size MB"
