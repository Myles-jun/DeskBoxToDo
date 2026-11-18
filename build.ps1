# Builds DeskBoxToDo.exe (with icon + version resource) and a portable zip.
# No .NET SDK needed: it targets the in-box .NET Framework 4.8 and compiles with
# the Roslyn csc that ships with Visual Studio.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$out = Join-Path $root 'DeskBoxToDo.exe'

function Find-Roslyn {
    $candidates = @(
        'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
    )
    $found = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($found) { return $found }

    # vswhere covers every edition/install path (CI runners included)
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $hit = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find '**\Roslyn\csc.exe' 2>$null |
            Select-Object -First 1
        if ($hit -and (Test-Path $hit.Trim())) { return $hit.Trim() }
    }

    foreach ($vsRoot in @('C:\Program Files\Microsoft Visual Studio', 'C:\Program Files (x86)\Microsoft Visual Studio')) {
        if (-not (Test-Path $vsRoot)) { continue }
        $hit = Get-ChildItem $vsRoot -Recurse -Filter csc.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\Roslyn\\csc\.exe$' } |
            Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }

    # A plain .NET SDK also ships Roslyn, as csc.dll (run through dotnet).
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        $sdkCsc = Get-ChildItem (Join-Path $env:ProgramFiles 'dotnet\sdk') -Recurse -Filter csc.dll -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\Roslyn\\bincore\\csc\.dll$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($sdkCsc) { return $sdkCsc.FullName }
    }

    throw 'Roslyn csc.exe not found. Install Visual Studio 2022 build tools or a .NET SDK.'
}

function Find-Rc {
    $kits = 'C:\Program Files (x86)\Windows Kits\10\bin'
    if (-not (Test-Path $kits)) { return $null }
    return Get-ChildItem $kits -Recurse -Filter rc.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\rc\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
}

$csc = Find-Roslyn
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$gac = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL'

$references = @(
    (Join-Path $framework 'System.dll'),
    (Join-Path $framework 'System.Core.dll'),
    (Join-Path $framework 'System.Windows.Forms.dll'),
    (Join-Path $framework 'System.Drawing.dll'),
    (Join-Path $framework 'System.Web.Extensions.dll'),
    (Join-Path $framework 'System.Net.Http.dll'),
    (Join-Path $gac 'UIAutomationClient\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationClient.dll'),
    (Join-Path $gac 'UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll')
)
foreach ($reference in $references) {
    if (-not (Test-Path $reference)) { throw "Missing reference: $reference" }
}

$sources = Get-ChildItem -Path $src -Filter '*.cs' | ForEach-Object { $_.FullName }

function Invoke-Csc([string[]]$extra, [switch]$NoManifest) {
    $arguments = @(
        '/nologo',
        '/target:winexe',
        '/platform:anycpu',
        '/optimize+',
        '/langversion:latest',
        '/codepage:65001',
        '/utf8output',
        "/out:$out"
    )
    if (-not $NoManifest) { $arguments += "/win32manifest:$src\app.manifest" }
    $arguments += $extra + ($references | ForEach-Object { "/r:$_" }) + $sources

    if ($csc -like '*.dll') {
        & dotnet exec $csc @arguments
    }
    else {
        & $csc @arguments
    }
    if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }
}

# Pass 1: plain exe, used to render the icon set.
Invoke-Csc @()
$icoPath = Join-Path $root 'app.ico'
& $out --export-icon $icoPath | Out-Null
if (-not (Test-Path $icoPath)) { throw 'icon export failed' }

# Pass 2: embed icon + version info when the resource compiler is available.
$resPath = Join-Path $root 'version.res'
$rc = Find-Rc
if ($rc) {
    Push-Location $root
    try {
        & $rc.FullName /nologo /fo $resPath (Join-Path $src 'version.rc')
        if ($LASTEXITCODE -ne 0) { throw "rc failed with exit code $LASTEXITCODE" }
    }
    finally {
        Pop-Location
    }
    Invoke-Csc @("/win32res:$resPath") -NoManifest
    Write-Output 'embedded icon + version resource'
}
else {
    Invoke-Csc @("/win32icon:$icoPath")
    Write-Output 'rc.exe not found; embedded icon only (no version info)'
}

$info = Get-Item $out
'{0}  {1:N0} bytes  {2}' -f $info.Name, $info.Length, $info.LastWriteTime

# Portable package.
$distRoot = Join-Path $root 'dist'
$packageName = 'DeskBoxToDo_1.5.6'
$packageDir = Join-Path $distRoot $packageName
if (Test-Path $distRoot) { Remove-Item $distRoot -Recurse -Force }
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

Copy-Item $out $packageDir
Copy-Item (Join-Path $root 'README.md') $packageDir
Copy-Item (Join-Path $root 'docs') (Join-Path $packageDir 'screenshots') -Recurse
foreach ($extra in @('LICENSE.txt', '快速开始.txt', 'DEVELOPMENT.md', 'CHANGELOG.md')) {
    $candidate = Join-Path $root $extra
    if (Test-Path $candidate) { Copy-Item $candidate $packageDir }
}

$zip = Join-Path $distRoot "$packageName.zip"
$sevenZip = 'C:\Program Files\7-Zip\7z.exe'
if (Test-Path $sevenZip) {
    Push-Location $distRoot
    try {
        & $sevenZip a -tzip -mx=9 $zip $packageName -r | Out-Null
    }
    finally {
        Pop-Location
    }
}
else {
    Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zip -Force
}

'{0}  {1:N0} bytes' -f (Split-Path $zip -Leaf), (Get-Item $zip).Length

# ---------------- installer ----------------
$installerSrc = Join-Path $root 'installer'
$payload = Join-Path $distRoot 'payload.zip'
if (Test-Path $installerSrc) {
    if (Test-Path $payload) { Remove-Item $payload -Force }
    Push-Location $packageDir
    try {
        $sevenZipExe = 'C:\Program Files\7-Zip\7z.exe'
        if (Test-Path $sevenZipExe) {
            & $sevenZipExe a -tzip -mx=9 $payload '*' -r | Out-Null
        } else {
            Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $payload -Force
        }
    }
    finally { Pop-Location }

    $setup = Join-Path $distRoot 'DeskBoxToDo_Setup_1.5.6.exe'
    $installerFiles = Get-ChildItem $installerSrc -Filter *.cs | ForEach-Object { $_.FullName }
    & $csc /nologo /target:winexe /platform:anycpu /optimize+ /utf8output `
        "/out:$setup" `
        "/win32icon:$icoPath" `
        "/resource:$payload,payload.zip" `
        '/r:System.dll' '/r:System.Core.dll' '/r:System.Drawing.dll' '/r:System.Windows.Forms.dll' `
        '/r:System.IO.Compression.dll' '/r:System.IO.Compression.FileSystem.dll' `
        $installerFiles 2>&1 | Where-Object { $_ -match 'error|warning CS' } | ForEach-Object { $_ }
    if (-not (Test-Path $setup)) { throw 'installer build failed' }
    Remove-Item $payload -Force
    '{0}  {1:N0} bytes' -f (Split-Path $setup -Leaf), (Get-Item $setup).Length
}
