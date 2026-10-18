using System;
using System.Reflection;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MelonLoader;
using Newtonsoft.Json;
using UnhollowerBaseLib;

[assembly: MelonInfo(typeof(NpcMonthActivity.ActivityLoader), "NPC月内活动", "0.3.8", "Local", null)]

namespace NpcMonthActivity
{
    // MelonLoader entry allows installation without forging workshop export metadata.
    public sealed class ActivityLoader : MelonMod
    {
        public override void OnApplicationStart() { ModMain.Start(); }
        public override void OnUpdate() { ModMain.Instance?.Update(); }
        public override void OnApplicationQuit() { ModMain.Stop(); }
    }

    public sealed class ModMain
    {
        internal const string SaveKey = "NpcMonthActivity.v1.snapshot";
        internal static ModMain Instance;
        internal ActivityEngine Engine;
        private readonly ActivityClock clock = new ActivityClock();
        private readonly GameAdapter world = new GameAdapter();
        private ActivityUi ui;
        private HarmonyLib.Harmony patches;
        private IntPtr worldPointer;
        private bool loadFailed;
        private readonly ActionGuard actionGuard = new ActionGuard();
        private int actionStarts, actionEnds;
        private int mainThread;
        private DateTime lastError = DateTime.MinValue;
        private DateTime lastHeartbeat = DateTime.MinValue;
        private static readonly object logLock = new object();

        public void Init() { Start(); }
        public void Destroy() { Stop(); }
        internal static void Start()
        {
            if (Instance != null) return;
            var instance = new ModMain();
            Instance = instance;
            instance.mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            instance.ui = new ActivityUi(instance);
            try
            {
                instance.patches = new HarmonyLib.Harmony("Local.NpcMonthActivity.v1");
                instance.patches.PatchAll(Assembly.GetExecutingAssembly());
                Log("loaded v"+Assembly.GetExecutingAssembly().GetName().Version.ToString(3)+"; active phase locks, native pill use and portal travel");
            }
            catch (Exception ex)
            {
                instance.patches?.UnpatchSelf();
                Instance = null;
                Log("initialization-failed " + ex);
            }
        }
        internal static void Stop()
        {
            var instance = Instance;
            if (instance == null) return;
            try { instance.Persist(); instance.ui.Clear(); }
            finally { instance.patches?.UnpatchSelf(); Instance = null; }
        }
        internal static void Log(string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [NpcMonthActivity] " + message;
            Console.WriteLine(line);
            try
            {
                lock (logLock)
                {
                    string root = Path.Combine(MelonUtils.GameDirectory, "UserData", "NpcMonthActivity");
                    Directory.CreateDirectory(root);
                    string path = Path.Combine(root, "Activity.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
                        File.WriteAllText(path, "[log rotated]\n", Encoding.UTF8);
                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { /* Diagnostics must never interrupt a native action or save. */ }
        }
        private bool MainThread { get { return System.Threading.Thread.CurrentThread.ManagedThreadId == mainThread; } }

        private bool Ready()
        {
            return g.world != null && g.world.isIntoWorld && !g.world.isDestroy &&
                g.world.playerUnit != null && g.data != null && g.data.world != null;
        }
        private static int ReadDay()
        {
            // roundDay is the within-month consumed time; worldDay alone is not a sufficient clock.
            return checked(g.world.run.roundMonth * 30 + g.world.run.roundDay);
        }
        internal void Invalidate()
        {
            world.Reset();
            worldPointer = IntPtr.Zero;
            Engine = null;
            actionGuard.Reset();
            actionStarts = actionEnds = 0;
            loadFailed = false;
            ui.Clear();
        }
        private bool EnsureWorld()
        {
            if (!Ready())
            {
                if (Engine != null) Invalidate();
                return false;
            }
            var pointer = g.data.world.Pointer;
            if (pointer != worldPointer)
            {
                Invalidate();
                worldPointer = pointer;
                var player = g.world.playerUnit.data.unitData;
                try
                {
                    var state = player.objData.ContainsKey(SaveKey)
                        ? JsonConvert.DeserializeObject<ActivityState>(player.objData.GetString(SaveKey))
                        : new ActivityState { PlayerId = player.unitID };
                    if (state == null) throw new InvalidOperationException("空存档活动数据");
                    state.Validate(player.unitID);
                    state.UpgradeRuntime();
                    Engine = new ActivityEngine(state);
                    clock.Reset(ReadDay());
                    Log("world-loaded player=" + player.unitID + " enabled=" + state.Npcs.Count);
                }
                catch (Exception ex) { loadFailed = true; Log("snapshot-error; state preserved, activity disabled: " + ex); }
            }
            return Engine != null && !loadFailed;
        }

        internal void Update()
        {
            try { if (!EnsureWorld()) return; }
            catch (Exception ex) { ReportError("world", ex); return; }
            // Keep the control surface available even if a native movement or terrain call fails.
            try { ui.Update(); }
            catch (Exception ex) { ReportError("ui", ex); }
            try
            {
                int currentFrame = UnityEngine.Time.frameCount;
                if (g.world.run.isRunning) NativeMissions.PauseForSettlement();
                int stale = actionGuard.ReleaseStale(currentFrame);
                if (stale > 0) Log("released-stale-action-guard count=" + stale);
                if (!actionGuard.IsActive(currentFrame) && !g.world.run.isRunning) ObserveClock();
                // isSaveData is a static game data switch, not a reliable "save in progress" guard.
                // Treating it as a busy flag can disable the scheduler for the entire session.
                if (!g.world.run.isRunning && !g.world.battle.isBattle && !g.scene.isLoadScene && !actionGuard.IsActive(currentFrame))
                {
                    if (Engine.Pump(world)) Persist();
                    world.RefreshMap();
                }
            }
            catch (Exception ex) { ReportError("activity", ex); }
            try { Heartbeat(); }
            catch (Exception ex) { ReportError("heartbeat", ex); }
        }
        private void ReportError(string phase, Exception ex)
        {
            if ((DateTime.UtcNow - lastError).TotalSeconds <= 5) return;
            lastError = DateTime.UtcNow;
            Log(phase + "-error " + ex);
        }
        private void Heartbeat()
        {
            if (Engine.State.Npcs.Count == 0 || (DateTime.UtcNow - lastHeartbeat).TotalSeconds < 5) return;
            lastHeartbeat = DateTime.UtcNow;
            var player = g.world.playerUnit.data.unitData;
            Log("heartbeat player=" + GameAdapter.Read(player.GetPoint()) + " worldDay=" + g.data.world.worldDay +
                " month=" + g.world.run.roundMonth + " roundDay=" + g.world.run.roundDay +
                " monthly=" + g.world.run.isRunning + " battle=" + g.world.battle.isBattle +
                " actionDepth=" + actionGuard.Depth(UnityEngine.Time.frameCount) + " starts=" + actionStarts + " ends=" + actionEnds +
                " staticSaveSwitch=" + DataMgr.isSaveData);
            int n = 0;
            foreach (var npc in Engine.State.Npcs.Values)
            {
                if (n++ >= 8) break;
                var unit = GameAdapter.Unit(npc.Id);
                Log("npc id=" + npc.Id + " pos=" + (unit == null ? "missing" : unit.data.unitData.GetPoint().ToString()) +
                    " name="+(unit==null ? "missing" : unit.data.unitData.propertyData.GetName())+
                    " stage="+(unit==null ? "missing" : GameAdapter.StageName(unit.data.dynUnitData.gradeID.value))+
                    " exp="+(unit==null ? -1 : unit.data.dynUnitData.exp.value)+
                    " budget=" + npc.Budget.ToString("F4") + " needed=" + npc.NeededBudget.ToString("F4") + " status=" + npc.Status);
            }
        }
        private void ObserveClock()
        {
            double elapsed = clock.Observe(ReadDay());
            Credit(elapsed, "world-day");
        }
        private void Credit(double days, string source)
        {
            if (days <= 0) return;
            Engine.Advance(days);
            Log("time source=" + source + " days=" + days.ToString("F4") + " total=" + Engine.State.SimulatedDays.ToString("F4") +
                " worldDay=" + g.data.world.worldDay + " month=" + g.world.run.roundMonth + " roundDay=" + g.world.run.roundDay);
            Persist();
        }
        internal void Toggle(string id)
        {
            if (!EnsureWorld()) return;
            ObserveClock(); // A newly enabled NPC must not receive time from before the click.
            Engine.Toggle(id);
            if (!Engine.Enabled(id)) world.Cancel(id);
            Log("toggle id=" + id + " enabled=" + Engine.Enabled(id));
            Persist();
        }
        internal void Persist()
        {
            if (Engine == null || loadFailed || !Ready() || g.data.world.Pointer != worldPointer) return;
            g.world.playerUnit.data.unitData.objData.SetString(SaveKey, JsonConvert.SerializeObject(Engine.State));
        }

        internal MoveFrame BeforeAction(WorldUnitBase unit, UnitActionBase action)
        {
            if (!MainThread || !EnsureWorld() || unit == null || action == null ||
                unit.data.unitData.unitID != g.world.playerUnit.data.unitData.unitID) return null;
            var move = action.TryCast<UnitActionMovePlayer>();
            if (move == null) return null;
            int currentFrame = UnityEngine.Time.frameCount;
            if (!actionGuard.IsActive(currentFrame)) ObserveClock();
            actionGuard.Begin(currentFrame);
            actionStarts++;
            return new MoveFrame { Before = GameAdapter.Read(unit.data.unitData.GetPoint()), Action = move, FrameNumber = currentFrame };
        }
        internal void AfterAction(MoveFrame frame, bool error)
        {
            if (frame == null || frame.Closed) return;
            frame.Closed = true;
            actionGuard.End(frame.FrameNumber);
            actionEnds++;
            if (Engine == null || !Ready() || actionGuard.IsActive(UnityEngine.Time.frameCount)) return;
            var after = GameAdapter.Read(g.world.playerUnit.data.unitData.GetPoint());
            double elapsed = clock.CommitMove(ReadDay(), error ? 0 : frame.Action.costDay,
                !error && !frame.Before.Equals(after));
            Credit(elapsed, error ? "failed-action-day" : "player-move");
        }
        internal bool SuppressMonthlyMove(WorldUnitBase unit)
        {
            // A targeted guard: only vanilla movement for explicitly enabled NPCs during month settlement.
            return !GameAdapter.OwnMove && Engine != null && unit != null &&
                Engine.Enabled(unit.data.unitData.unitID) && g.world.run.isRunning;
        }
    }

    internal sealed class MoveFrame
    {
        public Point Before;
        public UnitActionMovePlayer Action;
        public bool Closed;
        public int FrameNumber;
    }
    [HarmonyPatch(typeof(WorldUnitBase), nameof(WorldUnitBase.CreateAction))]
    internal static class PlayerMovePatch
    {
        private static bool Prefix(WorldUnitBase __instance, UnitActionBase action, ref int __result, out MoveFrame __state)
        {
            __state = null;
            try
            {
                if (!NativeActions.BeforeAction(__instance, action, ref __result)) return false;
                if (!NativeMissions.AllowAction(__instance, action, ref __result)) return false;
                __state = ModMain.Instance?.BeforeAction(__instance, action);
            }
            catch (Exception ex)
            {
                ModMain.Log("before-action-error " + ex);
                if (NativeMissions.FilterError(__instance, ex)) { __result = 3; return false; }
            }
            return true;
        }
        private static void Postfix(WorldUnitBase __instance, UnitActionBase action, MoveFrame __state)
        {
            try { NativeMissions.ActionReturned(__instance, action); ModMain.Instance?.AfterAction(__state, false); }
            catch (Exception ex) { ModMain.Log("after-action-error " + ex); }
        }
    }
    [HarmonyPatch(typeof(UnitActionTownHotel), nameof(UnitActionTownHotel.Init))]
    internal static class HotelTimePatch
    {
        private static void Postfix(UnitActionTownHotel __instance)
        { if (!string.IsNullOrEmpty(GameAdapter.OwnActivityId) && GameAdapter.OwnActivityId == __instance.unit?.data.unitData.unitID) __instance.day = 0; }
    }
    [HarmonyPatch(typeof(UnitActionGridGetExp), nameof(UnitActionGridGetExp.Init))]
    internal static class CultivationTimePatch
    {
        private static void Postfix(UnitActionGridGetExp __instance)
        { if (!string.IsNullOrEmpty(GameAdapter.OwnActivityId) && GameAdapter.OwnActivityId == __instance.unit?.data.unitData.unitID) __instance.day = 0; }
    }
    [HarmonyPatch(typeof(DataUnit.UnitInfoData), nameof(DataUnit.UnitInfoData.SetPoint))]
    internal static class MissionPositionPatch
    {
        private static bool Prefix(DataUnit.UnitInfoData __instance, UnityEngine.Vector2Int point)
        {
            var runtime=ModMain.Instance;
            if(runtime?.Engine!=null && g.world?.run?.isRunning==true && runtime.Engine.Enabled(__instance.unitID) &&
                runtime.SuppressMonthlyMove(__instance.unit)) return false;
            if (!NativeMissions.Owns(__instance.unitID)) return true;
            return NativeMissions.AllowMove(__instance.unit, point);
        }
    }
    [HarmonyPatch(typeof(WorldUnitAITool), nameof(WorldUnitAITool.MovePoint), new[] { typeof(WorldUnitBase), typeof(UnityEngine.Vector2Int) })]
    internal static class MissionMovePatch
    {
        private static bool Prefix(WorldUnitBase unit, UnityEngine.Vector2Int point, ref bool __result)
        {
            if(ModMain.Instance?.SuppressMonthlyMove(unit)??false) { __result=false; return false; }
            if (NativeMissions.AllowMove(unit, point)) return true;
            __result = false; return false;
        }
    }
    [HarmonyPatch(typeof(WorldUnitAITool), nameof(WorldUnitAITool.MovePoint), new[] { typeof(WorldUnitBase), typeof(WorldUnitBase) })]
    internal static class MissionMoveUnitPatch
    {
        private static bool Prefix(WorldUnitBase unit, WorldUnitBase toUnit, ref bool __result)
        {
            if(ModMain.Instance?.SuppressMonthlyMove(unit)??false) { __result=false; return false; }
            if (toUnit == null || NativeMissions.AllowMove(unit, toUnit.data.unitData.GetPoint())) return true;
            __result = false; return false;
        }
    }
    [HarmonyPatch]
    internal static class MissionPlayerBattlePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        { return typeof(WorldBattleMgr).GetMethods().Where(m => m.Name == nameof(WorldBattleMgr.IntoBattle)); }
        private static bool Prefix() { return !NativeActions.InCall && NativeMissions.AllowBattle(); }
    }
    [HarmonyPatch(typeof(UnitActionMoveNPC), nameof(UnitActionMoveNPC.OnCreate))]
    internal static class MonthlyMovePatch
    {
        private static bool Prefix(UnitActionMoveNPC __instance)
        {
            try { return !(ModMain.Instance?.SuppressMonthlyMove(__instance.unit) ?? false); }
            catch (Exception ex) { ModMain.Log("monthly-guard-error " + ex); return true; }
        }
    }
    [HarmonyPatch(typeof(UnitActionMoveNPC), nameof(UnitActionMoveNPC.Init))]
    internal static class ScheduledMoveCostPatch
    {
        private static void Postfix(UnitActionMoveNPC __instance)
        {
            // The scheduler already pays this travel time from its own elapsed-time budget.
            // Do not also consume the NPC's separate month-settlement day allowance.
            if (GameAdapter.OwnMove) __instance.day = 0;
        }
    }
    [HarmonyPatch(typeof(DataMgr), nameof(DataMgr.SaveData))]
    internal static class SavePatch
    {
        private static void Prefix()
        {
            try { ModMain.Instance?.Persist(); }
            catch (Exception ex) { ModMain.Log("save-snapshot-error " + ex); }
        }
    }
    [HarmonyPatch(typeof(UnitActionRoleUpGrade), nameof(UnitActionRoleUpGrade.Init))]
    internal static class GrowthInitPatch
    {
        private static void Postfix(UnitActionRoleUpGrade __instance) { NativeActions.AdjustInit(__instance); }
    }
    [HarmonyPatch(typeof(UnitActionPropUse), nameof(UnitActionPropUse.Init))]
    internal static class PhasePillInitPatch
    {
        private static void Postfix(UnitActionPropUse __instance) { NativeActions.AdjustInit(__instance); }
    }
    [HarmonyPatch(typeof(WorldUnitData), nameof(WorldUnitData.CostPropItem),new[] { typeof(string),typeof(int),typeof(bool) })]
    internal static class PhasePillCostPatch
    {
        private static bool Prefix(WorldUnitData __instance,string __0,int __1,ref DataProps.PropsData __result)
        { return NativeActions.BeforePillCost(__instance,__0,__1,ref __result); }
    }
    [HarmonyPatch(typeof(UnitActionRoleAttack), nameof(UnitActionRoleAttack.Init))]
    internal static class SocialAttackInitPatch
    {
        private static void Postfix(UnitActionRoleAttack __instance) { NativeActions.AdjustInit(__instance); }
    }
    [HarmonyPatch(typeof(UnitActionRoleBattle), nameof(UnitActionRoleBattle.Init))]
    internal static class SocialBattleInitPatch
    {
        private static void Postfix(UnitActionRoleBattle __instance) { NativeActions.AdjustInit(__instance); }
    }
    [HarmonyPatch(typeof(UnitActionRoleToUnitBase), nameof(UnitActionRoleToUnitBase.Init))]
    internal static class SocialInitPatch
    {
        private static void Postfix(UnitActionRoleToUnitBase __instance) { NativeActions.AdjustInit(__instance); }
    }
    [HarmonyPatch(typeof(UnitActionRoleUpGrade), nameof(UnitActionRoleUpGrade.ActionUpGradeComplete))]
    internal static class GrowthCompletionPatch
    {
        private static void Postfix(WorldUnitBase unit) { NativeActions.UpgradeCompleted(unit); }
    }
    [HarmonyPatch(typeof(WorldMgr), nameof(WorldMgr.IntoWorld))]
    internal static class WorldLoadPatch
    {
        private static void Postfix() { ModMain.Instance?.Invalidate(); }
    }
    [HarmonyPatch(typeof(WorldMgr), nameof(WorldMgr.Destroy))]
    internal static class WorldExitPatch
    {
        private static void Prefix() { ModMain.Instance?.Invalidate(); }
    }
}
