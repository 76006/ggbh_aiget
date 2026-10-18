param([string]$GameDir = 'D:\steam\steamapps\common\鬼谷八荒')
$ErrorActionPreference = 'Stop'
if (Get-Process -Name guigubahuang -ErrorAction SilentlyContinue) { throw '请先退出鬼谷八荒，再卸载。' }
$source = Join-Path $GameDir 'Mods\NpcMonthActivity.dll'
if (!(Test-Path -LiteralPath $source)) { Write-Host '当前未安装此测试模组。'; return }
$backupRoot = Join-Path $GameDir 'UserData\NpcMonthActivity\Backups'
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
$destination = Join-Path $backupRoot ('NpcMonthActivity-disabled-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.dll')
Move-Item -LiteralPath $source -Destination $destination
Write-Host "已停用，程序保存在：$destination"
Write-Host '存档中的启用名单会保留；未安装模组时它们不会被本模组调度。'
