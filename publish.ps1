# DeskBoxToDo → GitHub 一键发布
#
# 做三件事：
#   1. 找到/安装 GitHub CLI（gh），没装就用 winget 装
#   2. 用系统凭据或浏览器登录 gh
#   3. 创建仓库并推送当前目录的源码
#
# 用法（在你的终端里，本目录下执行）：
#   powershell -ExecutionPolicy Bypass -File .\publish.ps1
#   powershell -ExecutionPolicy Bypass -File .\publish.ps1 -RepoName DeskBoxToDo -Private
#   powershell -ExecutionPolicy Bypass -File .\publish.ps1 -DryRun     # 只检查环境，不推送

param(
    [string]$RepoName = "DeskBoxToDo",
    [switch]$Private,
    [switch]$DryRun
)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

function Find-Git {
    $cmd = Get-Command git -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($candidate in @(
        'D:\GIT\Git\cmd\git.exe',
        'C:\Program Files\Git\cmd\git.exe',
        'C:\Program Files (x86)\Git\cmd\git.exe',
        "$env:LOCALAPPDATA\Programs\Git\cmd\git.exe"
    )) {
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

function Find-Gh {
    $cmd = Get-Command gh -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($candidate in @(
        'C:\Program Files\GitHub CLI\gh.exe',
        "$env:LOCALAPPDATA\GitHubCLI\gh.exe",
        'D:\GIT\GitHub CLI\gh.exe'
    )) {
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

Write-Host "== DeskBoxToDo 发布检查 ==" -ForegroundColor Cyan

$git = Find-Git
if (-not $git) {
    Write-Host "没找到 git。请先安装：winget install --id Git.Git -e --source winget" -ForegroundColor Yellow
    exit 1
}
Write-Host "git: $git  ($(& $git --version))"

$gh = Find-Gh
if (-not $gh) {
    Write-Host "没找到 GitHub CLI（gh）。" -ForegroundColor Yellow
    if ($DryRun) {
        Write-Host "（DryRun）下一步会用 winget 安装 gh。" -ForegroundColor DarkGray
    } else {
        $answer = Read-Host "现在用 winget 安装 GitHub CLI 吗？(Y/n)"
        if ($answer -eq "" -or $answer -match '^[Yy]') {
            winget install --id GitHub.cli -e --source winget --accept-package-agreements --accept-source-agreements
            $gh = Find-Gh
        }
    }
}

if ($gh) { Write-Host "gh:   $gh" }

# 本地仓库
if (-not (Test-Path (Join-Path $root '.git'))) {
    & $git init | Out-Null
    & $git branch -M main
    Write-Host "已初始化本地仓库"
}
$dirty = (& $git status --porcelain | Measure-Object).Count
if ($dirty -gt 0) {
    $version = '1.5.0'
    $match = Select-String -Path .\src\AppInfo.cs -Pattern 'Version = "([^"]+)"'
    if ($match) { $version = $match.Matches[0].Groups[1].Value }
    $null = & $git add -A 2>&1
    $null = & $git -c user.name="DeskBoxToDo" -c user.email="noreply@example.com" commit -q -m ("DeskBoxToDo " + $version + " 源码") 2>&1
    Write-Host "已提交 $dirty 处改动（版本 $version）"
}

if ($DryRun) {
    Write-Host ""
    Write-Host "DryRun 完成：环境看起来没问题。去掉 -DryRun 就会真正建仓库并推送。" -ForegroundColor Green
    Write-Host "之后可能发生：浏览器弹出 GitHub 授权页，点 Approve；再次运行本脚本即可创建仓库并推送。" -ForegroundColor DarkGray
    exit 0
}

if (-not $gh) {
    Write-Host ""
    Write-Host "没有 gh 也可以手动上传：" -ForegroundColor Yellow
    Write-Host "  1) GitHub 网页新建空仓库 $RepoName（不要勾 README/License）"
    Write-Host "  2) 运行： git remote add origin https://github.com/你的用户名/$RepoName.git"
    Write-Host "           git push -u origin main"
    exit 1
}

function Test-GhAuth {
    & $gh auth status *> $null
    return ($LASTEXITCODE -eq 0)
}

if (-not (Test-GhAuth)) {
    Write-Host ""
    Write-Host "还没登录 GitHub，正在启动授权流程。" -ForegroundColor Cyan
    Write-Host "接下来：浏览器会打开 GitHub，复制页面上的 8 位代码粘贴回车，然后点 Authorize。" -ForegroundColor DarkGray
    & $gh auth login --hostname github.com --git-protocol https --web
    if (-not (Test-GhAuth)) {
        Write-Host "登录没成功。可以手动运行： gh auth login" -ForegroundColor Yellow
        exit 1
    }
}

# 创建仓库并推送
$visibility = if ($Private) { "--private" } else { "--public" }
Write-Host "正在创建仓库 $RepoName 并推送…" -ForegroundColor Cyan
& $gh repo create $RepoName $visibility --source . --remote origin --push --description "选中一句话，直接变成 DeskBox 待办（毛玻璃 UI）"

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "创建失败。常见原因：同名仓库已存在，或账号是新注册的限制。" -ForegroundColor Yellow
    Write-Host "已经存在的话，直接推过去即可：" -ForegroundColor DarkGray
    Write-Host "  git remote add origin https://github.com/你的用户名/$RepoName.git; git push -u origin main"
    exit 1
}

$user = (& $gh api user --jq .login) 2>$null
Write-Host ""
Write-Host "完成：https://github.com/$user/$RepoName" -ForegroundColor Green
Write-Host "发版时把 dist\DeskBoxToDo_Setup_*.exe 传到 Releases，tag 写成 vX.Y.Z，自动更新就能识别。" -ForegroundColor DarkGray
