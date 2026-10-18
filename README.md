# 鬼谷八荒：NPC 月内活动

当前版本：**0.3.8 测试版**。C# / .NET Framework 4.7.2，使用 MelonLoader 与 Harmony。

只有手动点击“赋予 AI”的 NPC 才参与本模组调度。使用本地需求规则，**不需要 API Key**。主角推进游戏时间后，NPC 使用各自累积的活动时间移动、休整、修炼、筹备突破或参与已适配活动。

## 下载与安装

- [0.3.8 安装包](dist/NpcMonthActivity-0.3.8.zip)
- [完整使用说明](docs/NpcMonthActivity-0.3.8-使用说明.md)
- [版本记录](docs/CHANGELOG.md) · [验证记录](docs/VALIDATION-0.3.8.md)

游戏需要已安装兼容的 MelonLoader，且已生成当前游戏版本的互操作程序集。

1. 保存存档，退出游戏到桌面。
2. 解压安装包；运行其中的 `Install.ps1`，或将 `Mods/NpcMonthActivity.dll` 放入游戏的 `Mods` 目录。
3. 启动游戏并读取存档，在 NPC 人物页点击“赋予 AI”，然后让主角正常推进时间。

安装脚本默认游戏目录为 `D:\steam\steamapps\common\鬼谷八荒`，自定义路径示例：

```powershell
powershell -ExecutionPolicy Bypass -File .\Install.ps1 -GameDir 'E:\SteamLibrary\steamapps\common\鬼谷八荒'
```

脚本会备份被替换的旧 DLL。停用时先退出游戏，再运行 `Uninstall.ps1`。建议先用独立测试存档验证。

## 当前行为

- 赋予名单按存档保存，普通 NPC 不会因本模组自动获得同步活动能力。
- NPC 在有效灵气点修炼一次花费 **1 天活动时间**，收益及有效条件使用原生计算；主角的修炼时间不变。
- 通过地图真实阵法入口与出口交通，不收取额外阵法费用。
- 小阶段突破使用原生材料列表和服药流程；确认达到目标境界后才扣一颗药。失败保留原药，本次已知误扣按事务记录恢复，避免读档重复扣补。
- 两类大境界材料按真实库存核对，空槽、失效物品或超额重复选择不算已持有。
- 保留已适配的巡查委托、NPC 交互与原生冒险入口；不足条件时显示原因并暂停。

## 已知限制

- 大境界突破目前只适配炼气到筑基，筑基以上尚未完整支持。
- 材料获取途径未覆盖全部天材地宝；无法获取时会等待，不会生成虚构材料或强行突破。
- 独立巡查委托属于模组的本地委托；原生城镇、宗门任务和冒险并非全部兼容。
- 与修改 NPC、移动、战斗或物品消耗的其他模组共存时，仍需要实际存档测试。
- 0.3.8 的真实缺材显示、后续材料获取及筑基结果待实测；离线检查通过不代表完整游戏链路已验证。

## 源码与构建

```text
src/NpcActivity/          模组源码
tests/NpcActivity.Tests/  不依赖运行中游戏的逻辑验证
scripts/                 构建、打包、检查和安装脚本
docs/                    使用说明、版本和验证记录
dist/                    当前版本的安装包与 DLL
```

本地构建需要：Windows、.NET SDK、.NET Framework 4.7.2 Developer Pack，以及本地游戏目录中的 MelonLoader、Harmony 和已生成的 IL2CPP 互操作程序集。仓库不分发游戏程序集和这些第三方依赖。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build.ps1 -GameDir 'D:\steam\steamapps\common\鬼谷八荒'
powershell -ExecutionPolicy Bypass -File .\scripts\Package.ps1 -Version '0.3.8'
powershell -ExecutionPolicy Bypass -File .\scripts\Verify-Package.ps1 -GameDir 'D:\steam\steamapps\common\鬼谷八荒' -Version '0.3.8'
```

当前构建脚本使用 `C:\Program Files\dotnet\dotnet.exe`；项目的框架引用位置为标准的 .NET Framework 4.7.2 引用程序集目录。`NuGet.Config` 禁用远程包源，所需游戏依赖从 `GameDir` 解析。

安装包 DLL 的 SHA256：

```text
039175A8C267BD597BCFCC5093AEE0F315C553988F18ED4B02FD0889A25633B4
```

源码、DLL 和压缩包均来自本次 0.3.8 构建。仓库不包含本地研究日志、存档、反编译的游戏或其他模组代码。
