param([string]$GameDir = 'D:\steam\steamapps\common\鬼谷八荒', [string]$Version = '0.3.8')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $GameDir 'MelonLoader\Mono.Cecil.dll')
$module = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $taskRoot 'src\NpcActivity\bin\Release\NpcMonthActivity.dll'))
$game = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameDir 'MelonLoader\Managed\Assembly-CSharp.dll'))
try {
    if ($module.Assembly.Name.Name -ne 'NpcMonthActivity') { throw '程序集身份不匹配' }
    if ($module.Assembly.Name.Version.ToString(3) -ne $Version) { throw '构建版本与测试包版本不一致' }
    $adapter=$module.Types | Where-Object FullName -eq 'NpcMonthActivity.GameAdapter'
    $transit=$adapter.Methods | Where-Object Name -eq 'Transfer'
    $transitCalls=@($transit.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object Operand)
    if (!($transitCalls | Where-Object { $_.Name -eq 'SetPoint' -and $_.DeclaringType.FullName -eq 'DataUnit/UnitInfoData' })) { throw '阵法未使用指定角色位置入口' }
    if ($transitCalls | Where-Object { $_.Name -match 'OpenTransfer|OptionsFunction|AddProps|DelProps|CostProps' }) { throw '免费阵法包含玩家界面、剧情或费用入口' }
    $minor=$adapter.Methods | Where-Object Name -eq 'ReadPhaseGrowth'
    $activePhase=$adapter.Methods | Where-Object Name -eq 'ActivePhaseTarget'
    $phaseCalls=@($activePhase.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object Operand)
    if (!($phaseCalls | Where-Object Name -eq 'GetNextGradeItem')) { throw '小阶段目标未取下一阶段配置' }
    if ($phaseCalls | Where-Object { $_.Name -match '^set_|^DelLuck$|^AddLuck$|^CreateAction$' }) { throw '瓶颈识别包含属性或气运修改' }
    $minorCalls=@($minor.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object Operand)
    if (!($minorCalls | Where-Object { $_.Name -eq '.ctor' -and $_.DeclaringType.FullName -eq 'UnitActionPropUse' })) { throw '小阶段未使用原生丹药入口' }
    if ($minorCalls | Where-Object { $_.Name -eq '.ctor' -and $_.DeclaringType.FullName -eq 'UnitActionRoleUpGrade' }) { throw '小阶段仍可绕过丹药直接提升' }
    $candidates=$adapter.Methods | Where-Object Name -eq 'PhaseCandidates'
    if (!($candidates.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'GetPhaseProps' })) { throw '丹药来源不是原生下一阶段材料列表' }
    $repair=$adapter.Methods | Where-Object Name -eq 'RepairObsoletePhaseLocks'
    $repairCalls=@($repair.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object Operand)
    $repairClosures=@($repairCalls | Where-Object {$_.DeclaringType.FullName -like 'NpcMonthActivity.GameAdapter/*'} | ForEach-Object {$_.Resolve()} | Where-Object HasBody)
    $closureCalls=@($repairClosures | ForEach-Object {$_.Body.Instructions} | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object Operand)
    if (!(($repairCalls+$closureCalls) | Where-Object { $_.Name -eq '.ctor' -and $_.DeclaringType.FullName -eq 'UnitActionLuckDel' })) { throw '旧限制修复未使用原生气运行动' }
    if (!($repairCalls | Where-Object Name -eq 'Enabled') -or !($repairCalls | Where-Object Name -eq 'get_playerUnit')) { throw '旧限制修复缺少赋予名单或玩家排除检查' }
    if (!($repairCalls | Where-Object {$_.Name -eq 'Execute' -and $_.DeclaringType.FullName -eq 'NpcMonthActivity.NativeActions'})) { throw '旧限制修复未经过持久化提交会话' }
    if (($repairCalls+$closureCalls) | Where-Object { $_.Name -match '^DelLuck$|^SetGradeID$|^AddExp$|^AddFixExp$|^set_exp$|^set_gradeID$' }) { throw '旧限制修复直接修改气运或成长属性' }
    $nativeActions=$module.GetType('NpcMonthActivity.NativeActions')
    $before=$nativeActions.Methods | Where-Object Name -eq 'BeforeAction'
    $beforeInstructions=@($before.Body.Instructions)
    $initIndex=-1; $flagIndex=-1
    for($i=0;$i -lt $beforeInstructions.Count;$i++) {
        $operand=$beforeInstructions[$i].Operand
        if($operand -isnot [Mono.Cecil.MethodReference]) {continue}
        if($operand.Name -eq 'Init' -and $operand.DeclaringType.FullName -eq 'UnitActionBase') {$initIndex=$i}
        if($operand.Name -eq 'set_isInit') {$flagIndex=$i;break}
    }
    if($initIndex -lt 0 -or $flagIndex -le $initIndex) {throw '嵌套行动未在设置isInit之前执行真正Init'}
    $begin=$adapter.Methods | Where-Object Name -eq 'BeginPhaseUse'
    $beginCalls=@($begin.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object Operand)
    if(!($beginCalls | Where-Object Name -eq 'Enabled') -or !($beginCalls | Where-Object Name -eq 'get_playerUnit')) {throw '丹药交易缺少赋予名单或玩家排除'}
    $defer=$adapter.Methods | Where-Object Name -eq 'DeferPhasePillCost'
    $deferCalls=@($defer.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object Operand)
    if(!($deferCalls | Where-Object Name -eq 'Matches') -or !($deferCalls | Where-Object Name -eq 'Clone')) {throw '扣药保留未核对指定交易及复制原药'}
    if($deferCalls | Where-Object {$_.Name -match '^CostPropItem$|^AddProps$|^DelProps$|^RewardPropItem$'}) {throw '延后扣药入口直接改了库存'}
    $settle=$adapter.Methods | Where-Object Name -eq 'SettlePhaseUse'
    $settleCalls=@($settle.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object Operand)
    if(!($settleCalls | Where-Object Name -eq 'Decide') -or !($settleCalls | Where-Object Name -eq 'CostPropItem') -or !($settleCalls | Where-Object Name -eq 'RewardPropItem')) {throw '丹药结算缺少状态判断或真实扣补药入口'}
    if($settleCalls | Where-Object {$_.Name -match '^SetGradeID$|^AddExp$|^AddFixExp$|^set_gradeID$|^set_exp$|^DelLuck$'}) {throw '丹药结算直接修改了境界修为或瓶颈'}
    $growth=$adapter.Methods | Where-Object Name -eq 'ReadGrowth'
    $growthCalls=@($growth.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object Operand)
    if(@($growthCalls | Where-Object Name -eq 'OwnedGrowthMaterials').Count -ne 2) {throw '突破两类材料未分别核对真实库存'}
    $owned=$adapter.Methods | Where-Object Name -eq 'OwnedGrowthMaterials'
    $ownedCalls=@($owned.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object Operand)
    if(!($ownedCalls | Where-Object {$_.Name -eq 'OwnedSelection' -and $_.DeclaringType.FullName -eq 'NpcMonthActivity.GrowthMaterialPolicy'})) {throw '真实材料选择未使用空槽及数量过滤'}
    $rarity=$adapter.Methods | Where-Object Name -eq 'GrowthRarityCandidates'
    $rarityCalls=@($rarity.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object Operand)
    if(!($rarityCalls | Where-Object Name -eq 'get_grade') -or !($rarityCalls | Where-Object Name -eq 'get_rarityPropsIDs')) {throw '天材地宝候选未按目标境界查原生目录'}
    if($rarityCalls | Where-Object {$_.Name -eq 'GetGrade' -and $_.DeclaringType.FullName -eq 'WorldUnitDynData'}) {throw '天材地宝仍误用当前境界目录'}
    $entry = $module.Types | Where-Object FullName -eq 'NpcMonthActivity.ActivityLoader'
    if ($entry.BaseType.FullName -ne 'MelonLoader.MelonMod') { throw '加载入口不匹配' }
    $unsafePaths = @($module.GetMemberReferences() | Where-Object { $_.Name -eq 'PathSearch' -and $_.DeclaringType.FullName -eq 'DataGrid/Grid' })
    if ($unsafePaths.Count -ne 0) { throw '构建仍然引用不兼容的原生泛型寻路入口' }
    $patchCount = 0
    foreach ($type in $module.Types) {
        foreach ($attribute in $type.CustomAttributes) {
            if ($attribute.AttributeType.FullName -ne 'HarmonyLib.HarmonyPatch') { continue }
            if ($attribute.ConstructorArguments.Count -eq 0) {
                if ($type.Name -ne 'MissionPlayerBattlePatch') { throw '未识别的动态补丁声明' }
                $battleType = $game.Types | Where-Object Name -eq 'WorldBattleMgr'
                $battleMethods = @($battleType.Methods | Where-Object Name -eq 'IntoBattle')
                if ($battleMethods.Count -ne 3) { throw '玩家战斗保护入口不匹配' }
                $patchCount += 3
                Write-Host '接口已核实：WorldBattleMgr.IntoBattle 的 3 个重载'
                continue
            }
            $targetName = [string]$attribute.ConstructorArguments[0].Value
            $methodName = [string]$attribute.ConstructorArguments[1].Value
            $target = $game.GetType($targetName)
            $methods = @($target.Methods | Where-Object Name -eq $methodName)
            if ($attribute.ConstructorArguments.Count -ge 3) {
                $signature = @($attribute.ConstructorArguments[2].Value | ForEach-Object { [string]$_.Value }) -join ','
                $methods = @($methods | Where-Object { (@($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',') -eq $signature })
            }
            if ($methods.Count -ne 1) { throw "补丁入口缺失或不唯一：$targetName.$methodName" }
            $patchCount++
            Write-Host "接口已核实：$targetName.$methodName"
        }
    }
    if ($patchCount -ne 21) { throw "补丁数量不匹配：$patchCount" }
} finally { $module.Dispose(); $game.Dispose() }
$packageRoot = Join-Path $taskRoot ('dist\NpcMonthActivity-' + $Version)
$dlls = @(Get-ChildItem -LiteralPath $packageRoot -Filter '*.dll' -Recurse -File)
if ($dlls.Count -ne 1 -or $dlls[0].Name -ne 'NpcMonthActivity.dll') { throw '测试包包含非预期程序集' }
$sourceHash = (Get-FileHash -LiteralPath (Join-Path $taskRoot 'src\NpcActivity\bin\Release\NpcMonthActivity.dll')).Hash
if ($sourceHash -ne (Get-FileHash -LiteralPath $dlls[0].FullName).Hash) { throw '测试包不是最新构建' }
foreach ($name in @('Install.ps1', 'Uninstall.ps1')) {
    $tokens = $null; $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $packageRoot $name), [ref]$tokens, [ref]$parseErrors) | Out-Null
    if ($parseErrors.Count) { throw "$name 语法检查失败" }
}
Write-Host '程序集入口、21 个补丁目标、嵌套初始化、服药结算、真实突破材料、测试包及安装脚本检查通过。'
