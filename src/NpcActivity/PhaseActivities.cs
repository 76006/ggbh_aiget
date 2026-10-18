using System;
using System.Collections.Generic;
using System.Linq;

namespace NpcMonthActivity
{
    internal sealed partial class GameAdapter
    {
        private static readonly Dictionary<string,string> growthDiagnostics=new Dictionary<string,string>();
        private static bool GrowthBlocked(WorldUnitBase unit)
        {
            LogGrowthState(unit);
            NpcState npc=null;
            ModMain.Instance?.Engine?.State.Npcs.TryGetValue(unit.data.unitData.unitID,out npc);
            bool noGain=npc!=null && CultivationPolicy.NoGainAtCurrentState(npc,unit.data.dynUnitData.gradeID.value,unit.data.dynUnitData.exp.value);
            return !CultivationPolicy.CanCultivate(SmallBottleneck(unit),MajorBottleneck(unit),noGain);
        }
        private static bool ConfiguredCap(WorldUnitBase unit)
        {
            var current=g.conf.roleGrade.GetItem(unit.data.dynUnitData.gradeID.value);
            var next=current==null ? null : g.conf.roleGrade.GetNextGradeItem(current.id);
            return current!=null && next!=null && CultivationPolicy.AtConfiguredCap(current.exp,next.exp,
                unit.data.unitData.propertyData.exp,unit.data.dynUnitData.exp.value);
        }
        internal static bool SmallBottleneck(WorldUnitBase unit)
        {
            if(g.conf.roleGrade.IsPhaseStop(unit)) return true;
            var current=g.conf.roleGrade.GetItem(unit.data.dynUnitData.gradeID.value);
            return current!=null && current.phase<3 && !g.conf.roleGrade.IsUpGrade(unit) && ConfiguredCap(unit);
        }
        private static ConfRoleGradeItem ActivePhaseTarget(WorldUnitBase unit)
        {
            var grade=g.conf.roleGrade;
            var current=grade.GetItem(unit.data.dynUnitData.gradeID.value);
            if(current==null) return null;
            var next=grade.GetNextGradeItem(current.id);
            return next!=null && next.grade==current.grade && next.phase==current.phase+1 ? next : null;
        }
        private static bool MajorBottleneck(WorldUnitBase unit)
        {
            if(g.conf.roleGrade.IsUpGrade(unit)) return true;
            var current=g.conf.roleGrade.GetItem(unit.data.dynUnitData.gradeID.value);
            return current!=null && current.phase>=3 && !g.conf.roleGrade.IsPhaseStop(unit) && ConfiguredCap(unit);
        }
        internal static string StageName(int configId)
        {
            var item=g.conf.roleGrade.GetItem(configId);
            return item==null ? "境界配置#"+configId : g.conf.roleGrade.GetGradeName(item.grade,item.phase);
        }
        private static List<int> PhaseCandidates(WorldUnitBase unit)
        {
            var result=new List<int>();
            if(ActivePhaseTarget(unit)==null) return result;
            var native=g.conf.roleGrade.GetPhaseProps(unit);
            if(native!=null) for(int i=0;i<native.Count;i++)
                if(g.conf.itemProps.GetItem(native[i])!=null && !result.Contains(native[i])) result.Add(native[i]);
            return result;
        }
        private static GrowthInfo ReadPhaseGrowth(WorldUnitBase unit)
        {
            var info=new GrowthInfo { Bottleneck=true,Supported=true,Minor=true,Next=ActivePhaseTarget(unit) };
            if(info.Next==null) { info.Supported=false; info.Reason="小阶段目标配置不一致，等待重新核对"; return info; }
            NpcState npc;
            if(ModMain.Instance.Engine.State.Npcs.TryGetValue(unit.data.unitData.unitID,out npc) &&
                (npc.PillUse?.Stage==PillStage.Uncertain ||
                npc.FailedPillGrade==unit.data.dynUnitData.gradeID.value && npc.FailedPillExp==unit.data.dynUnitData.exp.value))
            { info.Supported=false; info.Reason="上次突破未完成，丹药已保留；暂停相同状态下重复服药，等待核对"; return info; }
            var candidates=PhaseCandidates(unit);
            int rejected=0; bool owned=false;
            foreach(int id in candidates)
            {
                var items=unit.data.unitData.propData.GetProps(id);
                if(items==null || items.Count==0) continue;
                owned=true;
                for(int i=0;i<items.Count;i++)
                {
                    var action=new UnitActionPropUse(items[i].soleID);
                    action.Init(unit); action.day=0; action.isInit=true;
                    rejected=unit.IsCreateAction(action,false);
                    if(rejected==0)
                    {
                        info.Pill=items[i]; info.Reason="小阶段瓶颈：服用背包中的 "+ItemName(id); return info;
                    }
                }
            }
            if(owned)
            { info.Reason="小阶段瓶颈：已有正确丹药；修为 "+unit.data.dynUnitData.exp.value+"/"+info.Next.exp+"，原生服药条件代码 "+rejected; return info; }
            if(candidates.Count==0)
            { info.Reason="小阶段瓶颈：原生材料列表未返回可识别丹药，停止修炼等待核实"; return info; }
            info.Candidates=candidates;
            info.MissingItem=candidates.OrderBy(id=>Math.Max(1,g.conf.itemProps.GetItem(id).worth)).First();
            info.Reason="小阶段瓶颈：缺少可用突破丹药（"+string.Join("、",candidates.Take(3).Select(ItemName))+"）";
            return info;
        }
        private ActivityPlan PlanPhasePreparation(NpcState npc,Point origin,out string reason)
        {
            reason="";
            var unit=Unit(npc.Id);
            var current=g.conf.roleGrade.GetItem(unit.data.dynUnitData.gradeID.value);
            if(current==null || current.phase>=3) return null;
            var candidates=PhaseCandidates(unit);
            bool owned=candidates.Any(id=>unit.data.unitData.propData.GetPropsNum(id)>0);
            if(owned)
            { npc.SupplyStatus="已储备 "+ItemName(candidates.First(id=>unit.data.unitData.propData.GetPropsNum(id)>0))+
                "；修为 "+unit.data.dynUnitData.exp.value+"/"+(ActivePhaseTarget(unit)?.exp??0)+
                (ObsoletePhaseLocks(unit).Count>0 ? "，修炼前修复旧阶段限制" : "，达到条件后服用"); return null; }
            if(!CultivationPolicy.ShouldPreparePill(current.phase,candidates.Count,owned))
            {
                if(candidates.Count==0) { reason="原生阶段材料列表暂未返回可识别丹药"; npc.SupplyStatus=reason; }
                return null;
            }
            foreach(int id in candidates.OrderBy(id=>Math.Max(1,g.conf.itemProps.GetItem(id).worth)))
            {
                var plan=PlanPurchase(npc,origin,id);
                if(plan==null) continue;
                plan.Reason="提前储备下一小阶段所需的 "+ItemName(id)+"；达到条件后才服用";
                return plan;
            }
            reason=npc.SupplyStatus;
            return null;
        }
        internal ActivityAttempt ExecutePhasePill(NpcState npc,ActivityPlan plan)
        {
            if(NativeActions.HasPending(npc.Id)) return NativeActions.Execute(npc,plan,null,null);
            if(plan.Submitted) return SettlePhaseUse(npc,plan);
            var unit=Unit(npc.Id);
            if(!SmallBottleneck(unit)) return ActivityAttempt.Result(AttemptKind.Complete,"小阶段瓶颈状态已变化，重新规划");
            var item=unit.data.unitData.propData.GetProps(plan.ItemSoleId);
            if(item==null || item.propsID!=plan.ItemId || !PhaseCandidates(unit).Contains(plan.ItemId))
                return ActivityAttempt.Result(AttemptKind.Failed,"所选突破丹药已变化，重新查询原生列表");
            return NativeActions.Execute(npc,plan,()=>new UnitActionPropUse(plan.ItemSoleId),a=>SettlePhaseUse(npc,plan));
        }
        private static void LogGrowthState(WorldUnitBase unit)
        {
            string id=unit.data.unitData.unitID;
            var d=unit.data.dynUnitData;
            bool phase=g.conf.roleGrade.IsPhaseStop(unit),major=g.conf.roleGrade.IsUpGrade(unit);
            int rawExp=unit.data.unitData.propertyData.exp;
            int money=unit.data.unitData.propData.GetPropsNum(10001);
            var activeTarget=ActivePhaseTarget(unit);
            string signature=d.gradeID.value+":"+d.exp.value+":"+rawExp+":"+phase+":"+major+":"+money+":"+(activeTarget?.id??0);
            string last;
            if(growthDiagnostics.TryGetValue(id,out last) && last==signature) return;
            growthDiagnostics[id]=signature;
            var current=g.conf.roleGrade.GetItem(d.gradeID.value);
            var next=g.conf.roleGrade.GetNextGradeItem(d.gradeID.value);
            var pills=PhaseCandidates(unit);
            ModMain.Log(id+" growth-state name="+unit.data.unitData.propertyData.GetName()+" stage="+StageName(d.gradeID.value)+
                " gradeID="+d.gradeID.value+" rawGradeID="+unit.data.unitData.propertyData.gradeID+" major="+d.GetGrade()+" phase="+(current?.phase??-1)+" exp="+d.exp.value+
                " rawExp="+rawExp+" money="+money+" configuredCap="+ConfiguredCap(unit)+" expBar="+g.conf.roleGrade.ExpToBar(unit)+
                " expMax="+d.exp.maxValue+" currentConfExp="+(current?.exp??-1)+" nextConfExp="+(next?.exp??-1)+
                " currentLock="+(current?.lockFeature??0)+":"+(current!=null && current.lockFeature>0 && unit.GetLuck(current.lockFeature)!=null)+
                " nextLock="+(next?.lockFeature??0)+":"+(next!=null && next.lockFeature>0 && unit.GetLuck(next.lockFeature)!=null)+
                " activePhaseTarget="+(activeTarget?.id??0)+" obsoleteLocks="+string.Join(",",ObsoletePhaseLocks(unit))+
                " phaseStop="+phase+" majorStop="+major+" phasePills="+string.Join(",",pills.Select(p=>p+":"+ItemName(p)+":"+unit.data.unitData.propData.GetPropsNum(p))));
        }
    }
}
