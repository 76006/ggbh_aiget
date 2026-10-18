param([string]$Version = '0.3.8')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$packageRoot = Join-Path $taskRoot ('dist\NpcMonthActivity-' + $Version)
$mods = Join-Path $packageRoot 'Mods'
New-Item -ItemType Directory -Path $mods -Force | Out-Null
$dll = Join-Path $taskRoot 'src\NpcActivity\bin\Release\NpcMonthActivity.dll'
if (!(Test-Path -LiteralPath $dll)) { throw '请先运行 Build.ps1。' }
Copy-Item -LiteralPath $dll -Destination (Join-Path $mods 'NpcMonthActivity.dll') -Force
foreach ($scriptName in @('Install.ps1', 'Uninstall.ps1')) {
    $scriptText = [IO.File]::ReadAllText((Join-Path $PSScriptRoot $scriptName), [Text.Encoding]::UTF8)
    [IO.File]::WriteAllText((Join-Path $packageRoot $scriptName), $scriptText, [Text.UTF8Encoding]::new($true))
}
$guide = if ($Version -eq '0.3.8') { 'docs\NpcMonthActivity-0.3.8-使用说明.md' } elseif ($Version -eq '0.3.7') { 'docs\NpcMonthActivity-0.3.7-使用说明.md' } elseif ($Version -eq '0.3.6') { 'docs\NpcMonthActivity-0.3.6-使用说明.md' } elseif ($Version -eq '0.3.5') { 'docs\NpcMonthActivity-0.3.5-使用说明.md' } elseif ($Version -eq '0.3.4') { 'docs\NpcMonthActivity-0.3.4-使用说明.md' } elseif ($Version -eq '0.3.3') { 'docs\NpcMonthActivity-0.3.3-使用说明.md' } elseif ($Version -eq '0.3.2') { 'docs\NpcMonthActivity-0.3.2-使用说明.md' } elseif ($Version -eq '0.3.1') { 'docs\NpcMonthActivity-0.3.1-使用说明.md' } elseif ($Version.StartsWith('0.3.')) { 'docs\NpcMonthActivity-0.3-使用说明.md' } elseif ($Version.StartsWith('0.2.')) { 'docs\NpcMonthActivity-0.2-使用说明.md' } else { 'docs\NpcMonthActivity-使用说明.md' }
Copy-Item -LiteralPath (Join-Path $taskRoot $guide) -Destination (Join-Path $packageRoot '使用说明.md') -Force
$hash = (Get-FileHash -LiteralPath (Join-Path $mods 'NpcMonthActivity.dll') -Algorithm SHA256).Hash
"$hash  Mods/NpcMonthActivity.dll" | Set-Content -LiteralPath (Join-Path $packageRoot 'SHA256.txt') -Encoding utf8
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath (Join-Path $taskRoot ('dist\NpcMonthActivity-' + $Version + '.zip')) -Force
Write-Host "测试包：$packageRoot"
