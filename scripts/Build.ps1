param([string]$GameDir = 'D:\steam\steamapps\common\鬼谷八荒')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
& $dotnet build (Join-Path $taskRoot 'src\NpcActivity\NpcActivity.csproj') -c Release "-p:GameDir=$GameDir" --nologo
if ($LASTEXITCODE -ne 0) { throw '模组构建失败' }
& $dotnet build (Join-Path $taskRoot 'tests\NpcActivity.Tests\NpcActivity.Tests.csproj') -c Release "-p:GameDir=$GameDir" --nologo
if ($LASTEXITCODE -ne 0) { throw '验证程序构建失败' }
& (Join-Path $taskRoot 'tests\NpcActivity.Tests\bin\Release\NpcActivity.Tests.exe')
if ($LASTEXITCODE -ne 0) { throw '逻辑验证失败' }
