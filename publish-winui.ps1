# Publishes the WinUI 2.x build as a self-contained portable folder + zip.
# Run from anywhere:  powershell -ExecutionPolicy Bypass -File publish-winui.ps1
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Version = '2.0.4'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root 'winui\DeskBoxToDo.WinUI\DeskBoxToDo.WinUI.csproj'
$publishDir = Join-Path $root "artifacts\DeskBoxToDo_$Version"
$distDir = Join-Path $root 'dist'

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

dotnet publish $proj `
    -c $Configuration `
    -r $Runtime `
    -p:Platform=x64 `
    -p:PublishSingleFile=false `
    -p:Version=$Version `
    -o $publishDir `
    --nologo

if (-not (Test-Path (Join-Path $publishDir 'DeskBoxToDo.exe'))) {
    throw "publish did not produce DeskBoxToDo.exe"
}

foreach ($extra in @('README.md', 'CHANGELOG.md', 'LICENSE.txt', '快速开始.txt')) {
    $candidate = Join-Path $root $extra
    if (Test-Path $candidate) { Copy-Item $candidate $publishDir }
}

if (Test-Path (Join-Path $root 'docs')) {
    Copy-Item (Join-Path $root 'docs') (Join-Path $publishDir 'screenshots') -Recurse
}

New-Item -ItemType Directory -Path $distDir -Force | Out-Null
$zip = Join-Path $distDir "DeskBoxToDo_$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

$sevenZip = 'C:\Program Files\7-Zip\7z.exe'
if (Test-Path $sevenZip) {
    Push-Location $publishDir
    try { & $sevenZip a -tzip -mx=9 $zip '*' -r | Out-Null } finally { Pop-Location }
}
else {
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip -Force
}

$info = Get-Item $zip
'{0}  {1:N1} MB' -f $info.Name, ($info.Length / 1MB)
