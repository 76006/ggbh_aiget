using System.Collections.Generic;

namespace NpcMonthActivity
{
    internal sealed partial class GameAdapter
    {
        // A lock at the entrance to an ALREADY attained phase must not cap cultivation in that phase.
        // Never infer this from a full progress bar: inspect the actual built-in luck and its effects.
        private static List<int> ObsoletePhaseLocks(WorldUnitBase unit)
        {
            var result=new List<int>();
            if(unit==null || unit.data.unitData.unitID==g.world.playerUnit.data.unitData.unitID) return result;
            var property=unit.data.unitData.propertyData;
            if(property.gradeID!=unit.data.dynUnitData.gradeID.value) return result;
            var current=g.conf.roleGrade.GetItem(property.gradeID);
            if(current==null) return result;
            for(int phase=2;phase<=current.phase && phase<=3;phase++)
            {
                var entry=g.conf.roleGrade.GetGradeItem(current.grade,phase);
                if(entry==null || entry.lockFeature<=0) continue;
                var luck=unit.GetLuck(entry.lockFeature);
                if(luck?.effects==null || luck.effects.Count==0) continue;
                bool onlyObsoleteLimits=true;
                for(int i=0;i<luck.effects.Count;i++)
                {
                    var limit=luck.effects[i]?.TryCast<WorldUnitEffectSpecialLimitGradeExp>();
                    if(limit==null || !CultivationPolicy.ObsoletePhaseLimit(current.grade,current.phase,
                        entry.grade,entry.phase,entry.exp,property.exp,unit.data.dynUnitData.exp.value,limit.grade,limit.phase))
                    { onlyObsoleteLimits=false; break; }
                }
                if(onlyObsoleteLimits) result.Add(entry.lockFeature);
            }
            return result;
        }
        private static ActivityAttempt RepairObsoletePhaseLocks(NpcState npc,ActivityPlan plan)
        {
            if(NativeActions.HasPending(npc.Id)) return NativeActions.Execute(npc,plan,null,null);
            if(plan.Submitted) return ActivityAttempt.Result(AttemptKind.Failed,"旧限制修复已提交，重新核对，不重复执行");
            var unit=Unit(npc.Id);
            var engine=ModMain.Instance?.Engine;
            if(engine==null || !engine.Enabled(npc.Id) || unit==null ||
                unit.data.unitData.unitID==g.world.playerUnit.data.unitData.unitID || unit.data.unitData.unitID!=npc.Id)
                return ActivityAttempt.Result(AttemptKind.Failed,"人物不在赋予名单内或身份不符");
            if(!ObsoletePhaseLocks(unit).Contains(plan.ItemId))
                return ActivityAttempt.Result(AttemptKind.Complete,"旧阶段限制状态已变化，重新核对");
            int grade=unit.data.unitData.propertyData.gradeID, exp=unit.data.unitData.propertyData.exp;
            return NativeActions.Execute(npc,plan,()=>new UnitActionLuckDel(plan.ItemId),a=> {
                bool verified=unit.GetLuck(plan.ItemId)==null && unit.data.unitData.propertyData.gradeID==grade &&
                    unit.data.unitData.propertyData.exp==exp;
                ModMain.Log(npc.Id+" phase-lock-repair luck="+plan.ItemId+" grade="+grade+" exp="+exp+" verified="+verified);
                if(!verified) return ActivityAttempt.Result(AttemptKind.Failed,"旧阶段限制修复未获确认，保留丹药，停止重放本次请求");
                CultivationPolicy.ClearNoGain(npc);
                npc.SupplyStatus="旧阶段限制已修复；当前修为 "+exp+"，重新核对所需丹药与修炼门槛";
                return ActivityAttempt.Result(AttemptKind.Complete,npc.SupplyStatus);
            });
        }
    }
}
