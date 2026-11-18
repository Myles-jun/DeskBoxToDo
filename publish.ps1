# 发布到 GitHub（只需运行一次）
# 前置：安装 git（winget install --id Git.Git -e）并准备好 GitHub 仓库地址与 PAT
# 用法：powershell -ExecutionPolicy Bypass -File .\publish.ps1 -RepoUrl https://github.com/你的用户名/DeskBoxToDo.git -Token ghp_xxx

param(
    [Parameter(Mandatory = $true)][string]$RepoUrl,
    [string]$Token = "",
    [string]$Message = "DeskBoxToDo 源码"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Host "没有找到 git。先安装：winget install --id Git.Git -e --source winget" -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path (Join-Path $root '.git'))) {
    git init | Out-Null
    git branch -M main
}

git add -A
git -c user.name="DeskBoxToDo" -c user.email="noreply@example.com" commit -m $Message 2>$null

$pushUrl = $RepoUrl
if ($Token -ne "") {
    $pushUrl = $RepoUrl -replace 'https://', "https://x-access-token:$Token@"
}

git remote remove origin 2>$null
git remote add origin $pushUrl
git push -u origin main
Write-Host "完成：$RepoUrl" -ForegroundColor Green
