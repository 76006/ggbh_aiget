param([string]$GameDir = 'D:\steam\steamapps\common\鬼谷八荒')
$ErrorActionPreference = 'Stop'
if (Get-Process -Name guigubahuang -ErrorAction SilentlyContinue) { throw '请先退出鬼谷八荒，再安装。' }
$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $packageRoot 'Mods\NpcMonthActivity.dll'
if (!(Test-Path -LiteralPath $source)) { throw '请从解压后的测试包运行 Install.ps1。' }
if (!(Test-Path -LiteralPath (Join-Path $GameDir 'MelonLoader\MelonLoader.dll'))) { throw '没有找到游戏使用的 MelonLoader。' }
if (!(Test-Path -LiteralPath (Join-Path $GameDir 'guigubahuang.exe'))) { throw '游戏目录不正确。' }
$mods = Join-Path $GameDir 'Mods'
New-Item -ItemType Directory -Path $mods -Force | Out-Null
$destination = Join-Path $mods 'NpcMonthActivity.dll'
if (Test-Path -LiteralPath $destination) {
    $backupRoot = Join-Path $GameDir 'UserData\NpcMonthActivity\Backups'
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $backup = Join-Path $backupRoot ('NpcMonthActivity-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.dll')
    Copy-Item -LiteralPath $destination -Destination $backup
    Write-Host "旧版本已备份：$backup"
}
Copy-Item -LiteralPath $source -Destination $destination -Force
Write-Host "已安装：$destination"
Write-Host '请用独立测试存档，按照 使用说明.md 验证。'
