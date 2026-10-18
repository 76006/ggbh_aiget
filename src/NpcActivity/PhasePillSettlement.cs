using System;
using System.Linq;
using UnhollowerBaseLib;

namespace NpcMonthActivity
{
    internal sealed partial class GameAdapter
    {
        internal static bool BeginPhaseUse(NpcState npc,ActivityPlan plan,WorldUnitBase actor)
        {
            if(npc.PillUse?.NeedsSettlement==true || npc.PillUse?.Stage==PillStage.Uncertain) return false;
            var item=actor.data.unitData.propData.GetProps(plan.ItemSoleId);
            var target=ActivePhaseTarget(actor);
            if(item==null || item.propsID!=plan.ItemId || item.propsCount<1 || target==null ||
                actor.data.unitData.propertyData.gradeID!=plan.GradeId || actor.data.dynUnitData.gradeID.value!=plan.GradeId ||
                actor.data.unitData.unitID!=npc.Id || npc.Id==g.world.playerUnit.data.unitData.unitID ||
                !ModMain.Instance.Engine.Enabled(npc.Id)) return false;
            var values=item.values??item.GetValues();
            if(values==null || values.Length==0) return false;
            plan.Token=Guid.NewGuid().ToString("N");
            npc.PillUse=new PillUseRecord { ActorId=npc.Id,Token=plan.Token,SoleId=plan.ItemSoleId,ItemId=plan.ItemId,
                FromGrade=plan.GradeId,ToGrade=target.id,StartExp=actor.data.unitData.propertyData.exp,
                BeforeCount=actor.data.unitData.propData.GetPropsNum(plan.ItemId),PropsType=(int)item.propsType,
                Values=values.ToArray(),ValuesStr=item.valuesStr??"",Stage=PillStage.Prepared };
            return true;
        }
        internal static bool DeferPhasePillCost(NpcState npc,ActivityPlan plan,WorldUnitData data,string sole,int count,ref DataProps.PropsData result)
        {
            var r=npc.PillUse;
            if(count!=1 && PillPolicy.Matches(r,npc.Id,plan.Token,sole,1) && data.unit?.data.unitData.unitID==npc.Id)
            { result=null; ModMain.Log(npc.Id+" phase-pill-escrow unexpected count="+count+"; blocked cost"); return false; }
            if(!PillPolicy.Matches(r,npc.Id,plan.Token,sole,count) || data.unit?.data.unitData.unitID!=npc.Id) return true;
            var original=data.unitData.propData.GetProps(sole);
            if(original==null || original.propsID!=r.ItemId || original.propsCount<1)
            { result=null; ModMain.Log(npc.Id+" phase-pill-escrow missing reserved item; blocked cost"); return false; }
            var copy=original.Clone();
            copy.SetValues(CopyPillValues(r.Values)); copy.propsCount=1;
            r.Stage=PillStage.Deferred;
            ModMain.Instance.Persist();
            result=copy;
            ModMain.Log(npc.Id+" phase-pill-escrow deferred item="+r.ItemId+" token="+r.Token);
            return false;
        }
        private static ActivityPlan PhaseSettlementPlan(NpcState npc,Point origin)
        {
            var r=npc.PillUse;
            return r?.NeedsSettlement==true ? new ActivityPlan { Kind=NeedKind.PhasePill,Target=origin,Submitted=true,
                GradeId=r.FromGrade,ItemId=r.ItemId,ItemSoleId=r.SoleId,Token=r.Token,Label="核对突破与丹药结算",
                Reason="已有服药提交记录，只核对结果，不重放服药" } : null;
        }
        internal static ActivityAttempt SettlePhaseUse(NpcState npc,ActivityPlan plan)
        {
            var r=npc.PillUse; var unit=Unit(npc.Id);
            if(r==null || !r.Submitted || r.ActorId!=npc.Id || r.Token!=plan.Token || r.ItemId!=plan.ItemId ||
                unit==null || unit.data.unitData.unitID!=npc.Id || npc.Id==g.world.playerUnit.data.unitData.unitID)
                return ActivityAttempt.Result(AttemptKind.Failed,"服药结算记录或人物身份不符，停止自动扣补药");
            var props=unit.data.unitData.propData;
            int raw=unit.data.unitData.propertyData.gradeID, dynamic=unit.data.dynUnitData.gradeID.value;
            int count=props.GetPropsNum(r.ItemId);
            var reserved=props.GetProps(r.SoleId);
            var decision=PillPolicy.Decide(r,raw,dynamic,count,reserved!=null && reserved.propsID==r.ItemId && reserved.propsCount>0);
            if(decision==PillSettlement.Closed)
                return ActivityAttempt.Result(AttemptKind.Complete,"本次服药已结算，不重复扣补药");
            if(decision==PillSettlement.Commit || decision==PillSettlement.Restore)
            {
                r.Stage=decision==PillSettlement.Commit ? PillStage.CommitIntent : PillStage.RefundIntent;
                ModMain.Instance.Persist();
                try
                {
                    if(decision==PillSettlement.Commit) unit.data.CostPropItem(r.SoleId,1,false);
                    else
                    {
                        var restored=DataProps.PropsData.NewProps(r.ItemId,1);
                        restored.SetValues(CopyPillValues(r.Values));
                        restored.valuesStr=r.ValuesStr; restored.propsType=(DataProps.PropsDataType)r.PropsType; restored.propsCount=1;
                        unit.data.RewardPropItem(restored,false);
                    }
                }
                catch(Exception ex) { ModMain.Log(npc.Id+" phase-pill-settlement exception="+ex); }
                count=props.GetPropsNum(r.ItemId);
                decision=PillPolicy.Decide(r,unit.data.unitData.propertyData.gradeID,unit.data.dynUnitData.gradeID.value,count,false);
                // A mutation is attempted at most once per call. Recheck count before any saved recovery.
                if(decision==PillSettlement.Commit || decision==PillSettlement.Restore)
                {
                    r.Stage=PillStage.Uncertain;
                    ModMain.Instance.Persist();
                    return ActivityAttempt.Result(AttemptKind.Failed,"丹药结算未获确认，停止重复扣补，保留记录等待核对");
                }
            }
            string detail;
            if(decision==PillSettlement.Confirm)
            {
                r.Stage=PillStage.Committed; CultivationPolicy.ClearNoGain(npc);
                detail="已突破至 "+StageName(r.ToGrade)+"，消耗 "+ItemName(r.ItemId)+" 1颗";
            }
            else if(decision==PillSettlement.Keep)
            {
                r.Stage=PillStage.Released;
                npc.FailedPillGrade=dynamic; npc.FailedPillExp=unit.data.dynUnitData.exp.value;
                detail="境界未提升，"+ItemName(r.ItemId)+"已保留；暂停本阶段重复服药";
            }
            else
            {
                r.Stage=PillStage.Uncertain;
                detail="突破或药品数量出现非预期变化，暂停自动扣补药，等待核对";
            }
            npc.SupplyStatus=detail; npc.LastEvent=detail;
            ModMain.Log(npc.Id+" phase-pill-result item="+r.ItemId+" count="+r.BeforeCount+"->"+count+
                " grade="+r.FromGrade+"->"+unit.data.dynUnitData.gradeID.value+" settlement="+r.Stage+" token="+r.Token);
            ModMain.Instance.Persist();
            return ActivityAttempt.Result(decision==PillSettlement.Confirm ? AttemptKind.Complete : AttemptKind.Failed,detail);
        }
        private static Il2CppStructArray<int> CopyPillValues(int[] values)
        {
            var result=new Il2CppStructArray<int>(values.Length);
            for(int i=0;i<values.Length;i++) result[i]=values[i];
            return result;
        }
    }
}
