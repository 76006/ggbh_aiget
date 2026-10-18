using System;

namespace NpcMonthActivity
{
    // One retained native call at a time. Budgets are reserved in the save BEFORE native mutation.
    internal static class NativeActions
    {
        private sealed class Call
        {
            internal string Id, OtherId;
            internal ActivityPlan Plan;
            internal UnitActionBase Action;
            internal Func<UnitActionBase, ActivityAttempt> Verify;
            internal Il2CppSystem.Action End;
            internal int Frame;
            internal int BeforeItemCount;
            internal bool Done, InCall;
        }
        private static Call active;
        internal static bool AnyPending { get { return active != null; } }
        internal static bool HasPending(string id) { return active != null && active.Id == id; }
        internal static bool Busy(string id) { return active != null && (active.Id == id || active.OtherId == id); }
        internal static bool BusyOther(string id) { return active != null && active.Id != id && active.OtherId == id; }
        internal static bool InCall { get { return active != null && active.InCall; } }

        internal static ActivityAttempt Execute(NpcState npc, ActivityPlan plan, Func<UnitActionBase> create,
            Func<UnitActionBase, ActivityAttempt> verify)
        {
            if (active != null)
            {
                if (active.Id != npc.Id) return ActivityAttempt.Result(AttemptKind.Wait, "等待另一场 NPC 活动结束");
                if (!ReferenceEquals(active.Plan, plan)) return ActivityAttempt.Result(AttemptKind.Failed, "当前原生活动身份已变化");
                return Poll(npc);
            }
            if (plan.Submitted)
                return ActivityAttempt.Result(AttemptKind.Failed, "上次活动已提交，重新读取现状，不重复执行");
            if (NativeMissions.AnyPending) return ActivityAttempt.Result(AttemptKind.Wait,"等待原生冒险流程结束");
            var actor = GameAdapter.Unit(npc.Id);
            var action = create();
            action.Init(actor);
            double cost = Math.Max(1, action.day);
            plan.RequiredDays = cost;
            if (npc.Budget + .000001 < cost)
                return ActivityAttempt.Result(AttemptKind.Wait, "等待 " + cost + " 天活动时间");
            action.day = 0; action.isInit = true;
            int allowed = actor.IsCreateAction(action, false);
            if (allowed != 0)
                return ActivityAttempt.Result(AttemptKind.Failed, "原生条件不满足，代码 " + allowed);
            if(plan.Kind==NeedKind.PhasePill && !GameAdapter.BeginPhaseUse(npc,plan,actor))
                return ActivityAttempt.Result(AttemptKind.Failed,"所选丹药或阶段已变化，未提交服药");
            // Persist both quota and lock before native code can dispatch a save callback.
            if(!LifePolicy.ReserveSubmission(npc,plan,cost))
                return ActivityAttempt.Result(AttemptKind.Failed,"活动已提交或时间预算发生变化");
            if(plan.Kind==NeedKind.PhasePill) npc.PillUse.Submitted=true;
            var state = ModMain.Instance.Engine.State;
            if (!string.IsNullOrEmpty(plan.OtherId))
            {
                state.SocialReadyAt[npc.Id] = state.SimulatedDays + 7;
                state.SocialReadyAt[plan.OtherId] = state.SimulatedDays + 7;
            }
            var call = new Call { Id=npc.Id, OtherId=plan.OtherId, Plan=plan, Action=action,
                Verify=verify, Frame=UnityEngine.Time.frameCount,
                BeforeItemCount=plan.ItemId>0 ? actor.data.unitData.propData.GetPropsNum(plan.ItemId) : 0 };
            call.End = (Il2CppSystem.Action)(Action)(() => { if (active == call) call.Done = true; });
            action.TryCast<UnitActionRoleBase>()?.AddEndCall(call.End);
            active = call;
            ModMain.Instance.Persist();
            ModMain.Log(npc.Id + " life-native-start kind=" + plan.Kind + " other=" + plan.OtherId + " paid=" + cost);
            string failure=null;
            try
            {
                call.InCall = true;
                action.day = 0; action.isInit = true;
                int result = actor.CreateAction(action, false);
                if (result != 0)
                {
                    failure="游戏拒绝活动，代码 " + result + "，已保留提交记录";
                }
            }
            catch (Exception ex)
            {
                ModMain.Log(npc.Id + " life-native-error " + ex);
                failure="活动异常，停止重放本次请求";
            }
            finally { call.InCall = false; }
            if(plan.Kind==NeedKind.PhasePill) call.Done=true; // NPC PropUse executes its upgrade/effects synchronously.
            else if(failure!=null) { active=null; return ActivityAttempt.Result(AttemptKind.Failed,failure); }
            return Poll(npc);
        }
        private static ActivityAttempt Poll(NpcState npc)
        {
            var c = active;
            if (c == null) return ActivityAttempt.Result(AttemptKind.Failed, "活动会话不存在");
            if(c.Plan.Kind==NeedKind.PhasePill && UnityEngine.Time.frameCount==c.Frame)
                return ActivityAttempt.Result(AttemptKind.Pending,"已尝试突破，等待境界数据与丹药结算");
            bool complete = c.Done;
            var give = c.Action.TryCast<UnitActionRoleGive>(); if (give != null) complete |= give.isComplete;
            var ask = c.Action.TryCast<UnitActionRoleAskfor>(); if (ask != null) complete |= ask.isComplete;
            var attack = c.Action.TryCast<UnitActionRoleAttack>(); if (attack != null) complete |= attack.isComplete;
            var upgrade = c.Action.TryCast<UnitActionRoleUpGrade>();
            if (upgrade != null && GameAdapter.Unit(c.Id).data.dynUnitData.gradeID.value == c.Plan.GradeId) complete = true;
            if(c.Plan.Kind==NeedKind.PhaseRepair)
            {
                var unit=GameAdapter.Unit(c.Id);
                if(unit!=null && unit.GetLuck(c.Plan.ItemId)==null) complete=true;
            }
            if(c.Plan.Kind==NeedKind.PhasePill)
            {
                var unit=GameAdapter.Unit(c.Id);
                if(unit!=null && (unit.data.unitData.propData.GetPropsNum(c.Plan.ItemId)<c.BeforeItemCount ||
                    unit.data.dynUnitData.gradeID.value!=c.Plan.GradeId || !GameAdapter.SmallBottleneck(unit))) complete=true;
            }
            if (!complete)
            {
                if (UnityEngine.Time.frameCount - c.Frame <= 1800)
                    return ActivityAttempt.Result(AttemptKind.Pending, "已支付时间，等待原生" + c.Plan.Label + "结果");
                active = null;
                return ActivityAttempt.Result(AttemptKind.Failed, "原生活动结果尚未确认，不重复提交");
            }
            active = null;
            var outcome = c.Verify(c.Action);
            npc.LastEvent = outcome.Detail;
            var state=ModMain.Instance.Engine.State;
            state.RecentEvents[c.Id]=outcome.Detail;
            if(!string.IsNullOrEmpty(c.OtherId))
            {
                var actor=GameAdapter.Unit(c.Id);
                state.RecentEvents[c.OtherId]=(actor==null ? c.Id : actor.data.unitData.propertyData.GetName())+"："+outcome.Detail;
            }
            ModMain.Log(c.Id + " life-native-result kind=" + c.Plan.Kind + " other=" + c.OtherId + " " + outcome.Detail);
            return outcome;
        }
        // Only zero time inside the retained actor's own synchronous native call. Other mods/vanilla are unaffected.
        internal static bool BeforeAction(WorldUnitBase unit, UnitActionBase action, ref int result)
        {
            if (!InCall || unit == null || action == null) return true;
            string id = unit.data.unitData.unitID;
            if (id != active.Id && id != active.OtherId) return true;
            if (action.TryCast<UnitActionMoveNPC>() != null || action.TryCast<UnitActionSetPoint>() != null || action.TryCast<UnitActionBattleMap>() != null)
            { result = 3; return false; }
            try
            {
                if(PillPolicy.NeedsInit(action.isInit,action.unit!=null,action.unit?.Pointer==unit.Pointer))
                    action.Init(unit);
                if(action.unit==null || action.unit.Pointer!=unit.Pointer) { result=3; return false; }
            }
            catch(Exception ex)
            { ModMain.Log(id+" nested-action-init-error "+ex); result=3; return false; }
            var role = action.TryCast<UnitActionRoleToUnitBase>();
            if (role?.toUnit != null && role.toUnit.data.unitData.unitID == g.world.playerUnit.data.unitData.unitID)
            { result = 3; return false; }
            action.day = 0; action.isInit = true;
            return true;
        }
        internal static bool BeforePillCost(WorldUnitData data,string sole,int count,ref DataProps.PropsData result)
        {
            var c=active;
            if(c==null || !c.InCall || c.Plan.Kind!=NeedKind.PhasePill || data==null) return true;
            NpcState npc;
            if(!ModMain.Instance.Engine.State.Npcs.TryGetValue(c.Id,out npc)) return true;
            try { return GameAdapter.DeferPhasePillCost(npc,c.Plan,data,sole,count,ref result); }
            catch(Exception ex)
            {
                if(PillPolicy.Matches(npc.PillUse,npc.Id,c.Plan.Token,sole,count) && data.unit?.data.unitData.unitID==npc.Id)
                { result=null; ModMain.Log(npc.Id+" phase-pill-escrow-error; cost blocked "+ex); return false; }
                throw;
            }
        }
        internal static void AdjustInit(UnitActionBase action)
        {
            if(InCall && action.unit!=null && (action.unit.data.unitData.unitID==active.Id || action.unit.data.unitData.unitID==active.OtherId))
                action.day=0;
        }
        internal static void UpgradeCompleted(WorldUnitBase unit)
        {
            if(active!=null && (active.Plan.Kind==NeedKind.Breakthrough || active.Plan.Kind==NeedKind.PhasePill) && unit!=null && unit.data.unitData.unitID==active.Id)
                active.Done=true;
        }
        internal static void Cancel(string id) { if (active != null && (active.Id == id || active.OtherId == id)) active = null; }
        internal static void Reset() { active = null; }
    }
}
