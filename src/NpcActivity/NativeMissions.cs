using System;
using System.Collections.Generic;
using UnhollowerBaseLib;
using UnityEngine;

namespace NpcMonthActivity
{
    // Executes the game's NPC-specific goals, never the player's quest UI or battle simulation.
    internal static class NativeMissions
    {
        private sealed class Job
        {
            public string Id;
            public ActivityPlan Plan;
            public WorldUnitAIGoalBase Goal;
            public WorldUnitAIAction1013 Adventure;
            public Il2CppSystem.Action<bool> Callback;
            public bool Done, Success, InCall, NeedTime;
            public Point? Travel;
            public string Failure;
            public ActivityReservation TimeBudget;
            public double Required;
            public int StartFrame;
            public int BeforeMoney, BeforeExp;
            public int LastTaskLogFrame = -1000;
        }
        private static readonly Dictionary<string, Job> prepared = new Dictionary<string, Job>();
        private static Job active;
        internal static bool AnyPending { get { return active != null; } }
        internal static ActivityPlan PrepareAdventure(string id)
        {
            try
            {
                Cancel(id);
                var ai = g.world.unitAI.GetUnitAI(GameAdapter.Unit(id));
                if (ai == null) return null;
                var data = new Il2CppSystem.Collections.Generic.List<WorldUnitAIActionDataBase>();
                data.Add(new WorldUnitAIActionDataCommon { unitID = id, point = GameAdapter.Unit(id).data.unitData.GetPoint() });
                var action = new WorldUnitAIAction1013();
                action.Init(ai, 1013, data);
                if (!action.IsCreateAction() || !action.CreateActionInit() || action.monstData == null) return null;
                var plan = new ActivityPlan { Kind = NeedKind.Adventure, Target = GameAdapter.Read(action.point),
                    Label = "副本冒险目标", Reason = "寻找修炼资源，探索适合当前实力的副本", Token = Guid.NewGuid().ToString("N") };
                prepared[plan.Token] = new Job { Id = id, Plan = plan, Adventure = action };
                return plan;
            }
            catch (Exception ex) { ModMain.Log(id + " adventure-unavailable " + ex); return null; }
        }

        private static Job Prepare(NpcState npc, ActivityPlan plan)
        {
            Job job;
            if (!string.IsNullOrEmpty(plan.Token) && prepared.TryGetValue(plan.Token, out job)) return job;
            if (plan.Kind == NeedKind.Adventure)
                return null; // A saved native object is deliberately not reconstructed from invented encounter data.
            var ai = g.world.unitAI.GetUnitAI(GameAdapter.Unit(npc.Id));
            if (ai == null) return null;
            WorldUnitAIGoalBase goal = plan.Kind == NeedKind.SchoolTask
                ? (WorldUnitAIGoalBase)new WorldUnitAIGoal107() : new WorldUnitAIGoal122();
            goal.Init(ai, plan.Kind == NeedKind.SchoolTask ? 107 : 122);
            if (!goal.IsCreateGoal() || !goal.CreateGoalInit()) return null;
            plan.Token = Guid.NewGuid().ToString("N");
            job = new Job { Id = npc.Id, Plan = plan, Goal = goal };
            prepared[plan.Token] = job;
            return job;
        }

        internal static ActivityAttempt Execute(NpcState npc, ActivityPlan plan)
        {
            if (active != null && active.Id != npc.Id)
                return ActivityAttempt.Result(AttemptKind.Pending, "等待另一名 NPC 的原生计划结束");
            if (active != null) return Poll(npc, active);
            if (!string.IsNullOrEmpty(plan.TaskId) && !prepared.ContainsKey(plan.Token ?? ""))
                return ActivityAttempt.Result(AttemptKind.Failed, "已有任务进度仍由游戏保存，本次不重复领取");
            if (npc.Budget + .000001 < Math.Max(1, plan.RequiredDays))
                return ActivityAttempt.Result(AttemptKind.Wait, "已到达，等待原生计划所需时间");
            var job = Prepare(npc, plan);
            if (job == null) return ActivityAttempt.Result(AttemptKind.Failed, "当前没有符合条件的原生 NPC 任务或冒险");
            if (++plan.NativeAttempts > 8)
                return ActivityAttempt.Result(AttemptKind.Failed, "原生计划反复改道，停止本次计划并重新评估");
            job.Done = job.Success = job.NeedTime = false;
            job.Travel = null; job.Failure = null;
            job.TimeBudget = new ActivityReservation(npc.Budget, 1);
            job.StartFrame = Time.frameCount;
            job.BeforeMoney = GameAdapter.Unit(npc.Id).data.unitData.propData.GetPropsNum(10001);
            job.BeforeExp = GameAdapter.Unit(npc.Id).data.dynUnitData.exp.value;
            job.Callback = (Il2CppSystem.Action<bool>)(Action<bool>)(success => {
                if (active != job || job.Failure != null) return;
                job.Done = true; job.Success = success;
                ModMain.Log(job.Id + " native-mission-callback success=" + success + " kind=" + job.Plan.Kind);
            });
            active = job;
            ModMain.Log(job.Id + " native-mission-start kind=" + plan.Kind + " attempt=" + plan.NativeAttempts);
            try
            {
                job.InCall = true;
                if (job.Goal != null) job.Goal.GoalStart(job.Callback);
                else job.Adventure.ActionStart(job.Callback);
            }
            catch (Exception ex) { job.Done = true; job.Failure = "原生 NPC 计划接口异常"; ModMain.Log(npc.Id + " native-mission-error " + ex); }
            finally { job.InCall = false; }
            return Poll(npc, job);
        }

        private static ActivityAttempt Poll(NpcState npc, Job job)
        {
            CaptureTask(job);
            double delta = job.TimeBudget.Collect();
            if (Time.frameCount - job.StartFrame > 1800)
            { job.Done = true; job.Failure = "原生计划回调未返回，暂停等待重新评估"; }
            if (job.Failure != null)
            {
                active = null; prepared.Remove(job.Plan.Token);
                return ActivityAttempt.Result(AttemptKind.Failed, job.Failure, delta);
            }
            if (job.Travel.HasValue)
            {
                if (!job.Done) return ActivityAttempt.Result(AttemptKind.Pending, "等待本次任务步骤结束后赶路", delta);
                if (job.Success)
                {
                    active = null; prepared.Remove(job.Plan.Token);
                    return ActivityAttempt.Result(AttemptKind.Failed, "游戏同时返回远程目标与完成结果，停止重复执行", delta);
                }
                var point = job.Travel.Value;
                active = null;
                ModMain.Log(job.Id + " native-route-request " + point);
                return new ActivityAttempt { Kind = AttemptKind.Travel, Destination = point, Spent = delta, Detail = "任务需要赶路，前往 " + point };
            }
            if (job.NeedTime)
            {
                if (!job.Done) return ActivityAttempt.Result(AttemptKind.Pending, "等待任务步骤返回", delta);
                active = null;
                job.Plan.RequiredDays = Math.Max(1, job.Required + 1);
                return ActivityAttempt.Result(AttemptKind.Wait, "原生计划需要更多时间，等待积累", delta);
            }
            if (job.Done)
            {
                active = null; prepared.Remove(job.Plan.Token);
                var unit = GameAdapter.Unit(job.Id);
                ModMain.Log(job.Id + " native-mission-result kind=" + job.Plan.Kind + " callback=" + job.Success +
                    " task=" + job.Plan.TaskId + " money=" + job.BeforeMoney + "->" + unit.data.unitData.propData.GetPropsNum(10001) +
                    " exp=" + job.BeforeExp + "->" + unit.data.dynUnitData.exp.value);
                return ActivityAttempt.Result(job.Success && job.Failure == null ? AttemptKind.Complete : AttemptKind.Failed,
                    job.Success && job.Failure == null ? "原生 NPC " + (job.Plan.Kind == NeedKind.Adventure ? "冒险" : "任务") + "流程返回成功" : job.Failure ?? "原生 NPC 计划未完成，游戏拒绝或条件变化", delta);
            }
            job.TimeBudget.Refresh(Math.Max(0, npc.Budget - delta));
            return ActivityAttempt.Result(AttemptKind.Pending, "正在执行原生 NPC 计划", delta);
        }
        private static void CaptureTask(Job job)
        {
            if (job.Goal == null) return;
            var school = job.Goal.TryCast<WorldUnitAIGoal107>();
            var town = job.Goal.TryCast<WorldUnitAIGoal122>();
            var task = school?.task?.taskBase ?? town?.task?.taskBase;
            if (task == null || task.taskData == null) return;
            string id = task.taskData.soleID;
            if (string.IsNullOrEmpty(id)) return;
            if (string.IsNullOrEmpty(job.Plan.TaskId)) job.Plan.TaskId = id;
            else if (job.Plan.TaskId != id)
            {
                job.Failure = "任务身份发生变化，停止重复执行"; job.Done = true;
            }
            if (Time.frameCount - job.LastTaskLogFrame >= 120)
            {
                job.LastTaskLogFrame = Time.frameCount;
                ModMain.Log(job.Id + " native-task-state id=" + id + " state=" + task.taskData.state +
                    " progress=" + task.GetCurValue() + "/" + task.GetMaxValue());
            }
        }

        internal static bool AllowAction(WorldUnitBase unit, UnitActionBase action, ref int result)
        {
            var job = active;
            if (job == null || unit == null || unit.data.unitData.unitID != job.Id || GameAdapter.OwnMove || GameAdapter.OwnActivityId == job.Id) return true;
            if (g.world.run.isRunning) { PauseForSettlement(); return true; }
            if (job.Done || job.Failure != null) { result = 3; return false; }
            var move = action.TryCast<UnitActionMoveNPC>();
            var set = action.TryCast<UnitActionSetPoint>();
            if (move != null || set != null)
            {
                var p = move != null ? move.point : set.point;
                if (!GameAdapter.Read(p).Equals(GameAdapter.Read(unit.data.unitData.GetPoint()))) job.Travel = GameAdapter.Read(p);
                result = 3; return false;
            }
            if (action.TryCast<UnitActionRoleBase>() != null || action.TryCast<UnitActionBattleMap>() != null)
            {
                job.Failure = "原生计划需要玩家交互或战斗，未自动开启";
                job.Done = true; result = 3; return false;
            }
            bool allowed = action.TryCast<UnitActionNpcSchoolTask>() != null || action.TryCast<UnitActionTaskAdd>() != null ||
                action.TryCast<UnitActionTaskSchoolAdd>() != null || action.TryCast<UnitActionTaskGive>() != null ||
                action.TryCast<UnitActionTaskGetReward>() != null || action.TryCast<UnitActionGridMine>() != null ||
                action.TryCast<UnitActionGridAddSafety>() != null || action.TryCast<UnitActionTownHotel>() != null ||
                action.TryCast<UnitActionGridGetExp>() != null;
            if (!allowed)
            {
                job.Failure = "原生计划包含尚未验证的活动类型"; job.Done = true; result = 3; return false;
            }
            if (!job.TimeBudget.Contains(action.Pointer.ToInt64()))
            {
                action.Init(unit);
                double cost = Math.Max(0, action.day);
                if (!job.TimeBudget.TryReserve(action.Pointer.ToInt64(), cost))
                { job.NeedTime = true; job.Required = cost; result = 3; return false; }
                ModMain.Log(job.Id + " native-step-reserved days=" + cost + " total=" + job.TimeBudget.Spent);
            }
            action.day = 0; action.isInit = true;
            return true;
        }
        internal static void ActionReturned(WorldUnitBase unit, UnitActionBase action)
        {
            if (active != null && unit != null && action != null && unit.data.unitData.unitID == active.Id)
                active.TimeBudget.ReleaseKey(action.Pointer.ToInt64());
        }
        internal static bool AllowMove(WorldUnitBase unit, Vector2Int point)
        {
            var job = active;
            if (job == null || unit == null || GameAdapter.OwnMove || unit.data.unitData.unitID != job.Id) return true;
            if (g.world.run.isRunning) { PauseForSettlement(); return true; }
            if (GameAdapter.Read(unit.data.unitData.GetPoint()).Equals(GameAdapter.Read(point))) return true;
            job.Travel = GameAdapter.Read(point); return false;
        }
        internal static bool AllowBattle()
        {
            if (active == null || !active.InCall) return true;
            active.Done = true; active.Failure = "原生 NPC 流程尝试打开玩家战斗，已停止";
            return false;
        }
        internal static bool FilterError(WorldUnitBase unit, Exception error)
        {
            if (active == null || unit == null || unit.data.unitData.unitID != active.Id) return false;
            active.Done = true; active.Failure = "原生任务动作检查失败，已停止";
            ModMain.Log(active.Id + " native-filter-error " + error);
            return true;
        }
        internal static bool HasPending(string id) { return active != null && active.Id == id; }
        internal static bool Owns(string id) { return active != null && active.Id == id; }
        internal static void PauseForSettlement()
        {
            if (active == null) return;
            active.Done = true;
            active.Failure = "月结开始，暂停当前额外任务，保留游戏已有进度";
        }
        internal static void Cancel(string id)
        {
            if (active != null && active.Id == id) active = null;
            var remove = new List<string>();
            foreach (var p in prepared) if (p.Value.Id == id) remove.Add(p.Key);
            foreach (var key in remove) prepared.Remove(key);
        }
        internal static void Reset() { active = null; prepared.Clear(); }
    }
}
