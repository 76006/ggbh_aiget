using System;
using System.Collections.Generic;
using System.Linq;
using UnhollowerBaseLib;

namespace NpcMonthActivity
{
    internal sealed partial class GameAdapter
    {
        private sealed class GrowthInfo
        {
            internal bool Bottleneck, Supported;
            internal ConfRoleGradeItem Next;
            internal int MissingItem;
            internal string Reason="";
            internal UnitActionRoleUpGrade Action;
            internal bool Minor;
            internal DataProps.PropsData Pill;
            internal List<int> Candidates=new List<int>();
        }
        private static readonly HashSet<int> loggedGrades = new HashSet<int>();
        private ActivityState LifeState { get { return ModMain.Instance.Engine.State; } }

        private static GrowthInfo ReadGrowth(WorldUnitBase unit)
        {
            var info = new GrowthInfo();
            var grade=g.conf.roleGrade;
            bool minor=SmallBottleneck(unit), major=MajorBottleneck(unit);
            info.Bottleneck=GrowthBlocked(unit);
            if (!info.Bottleneck) return info;
            LogGrowthState(unit);
            if (grade.IsMaxGrade(unit)) { info.Reason="已到原生境界上限"; return info; }
            if(minor) return ReadPhaseGrowth(unit);
            if(!major) { info.Reason="此前修炼未增加修为，已暂停；原生尚未确认突破条件，等待状态变化"; return info; }
            int nextMajor=unit.data.dynUnitData.GetGrade()+1;
            info.Next=grade.GetGradeItem(nextMajor,1,1);
            if (info.Next==null) { info.Reason="未找到下一阶段境界配置"; return info; }
            if(info.Next.grade!=nextMajor || info.Next.phase!=1) { info.Reason="下一大境界配置与预期不符，停止突破"; return info; }
            if (info.Next.grade>2) { info.Reason="筹备突破：本测试版尚未适配筑基以上的大境界突破"; return info; }
            info.Supported=true;
            var rawRequired=grade.GetFastRequire(info.Next,unit);
            var rawRarity=grade.GetFastRarity(info.Next,unit);
            var reserved=new Dictionary<string,int>();
            var required=OwnedGrowthMaterials(unit,rawRequired,reserved);
            var rarity=OwnedGrowthMaterials(unit,rawRarity,reserved);
            int allowed=-1;
            bool enough=LifePolicy.MaterialSelectionComplete(info.Next.growItemNum,required==null ? 0 : required.Length) &&
                LifePolicy.MaterialSelectionComplete(info.Next.materialsNum,rarity==null ? 0 : rarity.Length);
            var selected=new List<string>();
            if(required!=null) foreach(string sole in required) selected.Add(sole);
            if(rarity!=null) foreach(string sole in rarity) selected.Add(sole);
            foreach(var group in selected.GroupBy(s=>s))
            {
                var item=string.IsNullOrEmpty(group.Key) ? null : unit.data.unitData.propData.GetProps(group.Key);
                if(item==null || item.propsCount<group.Count()) enough=false;
            }
            if(enough)
            {
                info.Action=new UnitActionRoleUpGrade(info.Next.id,required,rarity);
                info.Action.Init(unit);
                info.Action.day=0; info.Action.isInit=true;
                allowed=unit.IsCreateAction(info.Action,false);
            }
            LogGrowthMaterials(unit,info.Next,rawRequired,rawRarity,required,rarity,allowed);
            if (loggedGrades.Add(info.Next.id))
                ModMain.Log("growth-config target="+info.Next.id+" grade="+info.Next.grade+" phase="+info.Next.phase+
                    " growItemNum="+info.Next.growItemNum+" materialsNum="+info.Next.materialsNum+
                    " growRequire="+(info.Next.growItemRequire==null ? "null" : string.Join(",",info.Next.growItemRequire))+
                    " selectedRequire="+(required==null ? 0 : required.Length)+" selectedRarity="+(rarity==null ? 0 : rarity.Length)+" allowed="+allowed);
            if (allowed==0 && enough) { info.Reason="材料与原生条件允许，尝试正常突破"; return info; }
            var candidates=new List<int>();
            if((required==null ? 0 : required.Length)<info.Next.growItemNum)
            {
                candidates.AddRange(CultivationPolicy.MaterialIds(info.Next.itemShowA));
                if (info.Next.growItemRequire!=null) foreach(int id in info.Next.growItemRequire) candidates.Add(id);
                var growCandidates=grade.GetGrowItemRequire(info.Next.grade,info.Next.quality);
                if(growCandidates!=null) foreach(int id in growCandidates) candidates.Add(id);
            }
            if((rarity==null ? 0 : rarity.Length)<info.Next.materialsNum)
                candidates.AddRange(CultivationPolicy.MaterialIds(info.Next.itemShowB));
            // Only concrete, configured prop IDs can become acquisition targets; unknown cost expressions are never guessed.
            foreach(int id in candidates.Distinct())
                if (g.conf.itemProps.GetItem(id)!=null && unit.data.unitData.propData.GetPropsNum(id)<1)
                { info.Candidates.Add(id); if(info.MissingItem==0) info.MissingItem=id; }
            if (info.MissingItem==0 && (rarity==null ? 0 : rarity.Length)<info.Next.materialsNum)
            {
                foreach(int id in GrowthRarityCandidates(info.Next))
                    if(unit.data.unitData.propData.GetPropsNum(id)<1)
                    { if(!info.Candidates.Contains(id)) info.Candidates.Add(id); if(info.MissingItem==0) info.MissingItem=id; }
            }
            string quantities="筹备突破：材料 "+required.Length+"/"+info.Next.growItemNum+
                "，天材地宝 "+rarity.Length+"/"+info.Next.materialsNum;
            info.Reason=quantities+(info.MissingItem>0 ? "；缺少 "+ItemName(info.MissingItem) :
                !enough ? "；真实材料不足，尚未解析出可获取的缺项" :
                allowed==4 ? "；选中的天材地宝被原生库存检查拒绝，等待核对" : "；原生条件代码 "+allowed);
            // The original action remains responsible for checking quantities, combinations, experience and other conditions.
            info.Action=null;
            return info;
        }
        internal static string ItemName(int id)
        {
            var item=g.conf.itemProps.GetItem(id);
            if(item==null) return "物品#"+id;
            string text=GameTool.LS(item.name);
            return string.IsNullOrEmpty(text) ? "物品#"+id : text;
        }
        internal ActivityPlan PlanGrowth(NpcState npc,Point origin,out string reason)
        {
            var info=ReadGrowth(Unit(npc.Id)); reason=info.Reason;
            if(!info.Bottleneck || !info.Supported) return null;
            if(info.Minor && info.Pill!=null)
            {
                npc.SupplyStatus="当前瓶颈所需："+ItemName(info.Pill.propsID)+"，已持有，准备服用";
                return new ActivityPlan { Kind=NeedKind.PhasePill,Target=origin,GradeId=Unit(npc.Id).data.dynUnitData.gradeID.value,
                    ItemId=info.Pill.propsID,ItemSoleId=info.Pill.soleID,Label="服用突破丹药",Reason=info.Reason };
            }
            if(info.Action!=null)
                return new ActivityPlan { Kind=NeedKind.Breakthrough,Target=origin,GradeId=info.Next.id,Label="境界突破",Reason=info.Reason };
            if(info.MissingItem>0)
            {
                var options=info.Candidates.Count>0 ? info.Candidates : new List<int> { info.MissingItem };
                foreach(int id in options)
                {
                    var purchase=PlanPurchase(npc,origin,id);
                    if(purchase!=null) return purchase;
                }
                foreach(int id in options)
                {
                    var social=PlanSocial(npc,origin,id);
                    if(social!=null) return social;
                }
            }
            var item=info.MissingItem>0 ? g.conf.itemProps.GetItem(info.MissingItem) : null;
            if(item!=null && Unit(npc.Id).data.unitData.propData.GetPropsNum(10001)<Math.Max(1,item.worth))
            {
                var job=PlanCommission(npc,origin,!string.IsNullOrEmpty(Unit(npc.Id).data.unitData.schoolID));
                if(job!=null) { job.Reason=info.Reason+"；资金不足，先做巡查委托"; return job; }
            }
            if(info.MissingItem>0) reason=info.Reason+"；暂未找到可购买现货或可索取对象";
            return null;
        }
        internal ActivityAttempt ExecuteGrowth(NpcState npc,ActivityPlan plan)
        {
            if(NativeActions.HasPending(npc.Id)) return NativeActions.Execute(npc,plan,null,null);
            if(plan.Submitted) return ActivityAttempt.Result(AttemptKind.Failed,"突破请求已提交，重新核对境界和材料");
            var info=ReadGrowth(Unit(npc.Id));
            if(info.Action==null || info.Next.id!=plan.GradeId)
                return ActivityAttempt.Result(AttemptKind.Failed,info.Reason);
            int before=Unit(npc.Id).data.dynUnitData.gradeID.value;
            var beforeMaterials=new Dictionary<int,int>();
            var requiredCounts=new Dictionary<int,int>();
            var selected=new List<string>();
            if(info.Action.requirePropsSoleID!=null) foreach(string sole in info.Action.requirePropsSoleID) selected.Add(sole);
            if(info.Action.rarityPropsSoleID!=null) foreach(string sole in info.Action.rarityPropsSoleID) selected.Add(sole);
            foreach(string sole in selected)
            {
                var item=Unit(npc.Id).data.unitData.propData.GetProps(sole);
                if(item==null) return ActivityAttempt.Result(AttemptKind.Failed,"所选突破材料已变化");
                int id=item.propsID;
                beforeMaterials[id]=Unit(npc.Id).data.unitData.propData.GetPropsNum(id);
                if(!requiredCounts.ContainsKey(id)) requiredCounts[id]=0;
                requiredCounts[id]++;
            }
            return NativeActions.Execute(npc,plan,()=>info.Action,a=> {
                int after=Unit(npc.Id).data.dynUnitData.gradeID.value;
                bool consumed=true;
                foreach(var pair in beforeMaterials)
                {
                    int remaining=Unit(npc.Id).data.unitData.propData.GetPropsNum(pair.Key);
                    consumed &= pair.Value-remaining>=requiredCounts[pair.Key];
                    ModMain.Log(npc.Id+" growth-material item="+pair.Key+" count="+pair.Value+"->"+remaining+" selected="+requiredCounts[pair.Key]);
                }
                ModMain.Log(npc.Id+" growth-grade "+before+"->"+after);
                if(after!=before && !consumed)
                    return ActivityAttempt.Result(AttemptKind.Failed,"境界已变化，但材料消耗未确认，请查看日志");
                return ActivityAttempt.Result(after!=before ? AttemptKind.Complete : AttemptKind.Failed,
                    after!=before ? "原生突破已生效："+StageName(after) : "本次未突破，保留原生结果并重新筹备");
            });
        }
        private ActivityPlan PlanPurchase(NpcState npc,Point origin,int itemId)
        {
            int money=Unit(npc.Id).data.unitData.propData.GetPropsNum(10001);
            var config=g.conf.itemProps.GetItem(itemId);
            if(config==null || config.worth<=0) { SupplyTrace(npc,itemId,"物品未提供有效购买价格"); return null; }
            if(money<config.worth) { SupplyTrace(npc,itemId,"资金不足：现有 "+money+"，需要 "+config.worth); return null; }
            var builds=g.world.build.GetBuilds();
            var towns=new List<MapBuildTown>();
            for(int i=0;i<builds.Count;i++) { var t=builds[i].TryCast<MapBuildTown>(); if(t!=null) towns.Add(t); }
            var catalog=new List<string>(); int matches=0, quotaBlocked=0, routeBlocked=0;
            foreach(var town in towns.OrderBy(t=>Distance(origin,Read(t.GetOrigiPoint()))).Take(8))
            {
                var market=town.GetBuildSub(MapBuildSubType.TownMarketPill)?.TryCast<MapBuildTownMarket>();
                var data=market?.data;
                var sample=new List<int>();
                if(data?.props!=null) for(int i=0;i<data.props.Count && i<12;i++) sample.Add(data.props[i]);
                catalog.Add(town.name+"["+string.Join(",",sample)+"]");
                string key=LifePolicy.MarketKey(g.world.run.roundMonth,town.buildData.id,itemId);
                if(data?.props==null || !data.props.Contains(itemId)) continue;
                matches++;
                if(LifeState.MarketPurchases.ContainsKey(key)) { quotaBlocked++; catalog.Add(town.name+":quota-used"); continue; }
                var plan=FacilityPlan(npc.Id,origin,town,NeedKind.Purchase,"购买突破所缺的 "+ItemName(itemId)+"，预计 "+config.worth+" 灵石");
                if(plan==null) { routeBlocked++; catalog.Add(town.name+":route-unreachable"); continue; }
                plan.ItemId=itemId; plan.Label=town.name+"闹市采购";
                SupplyTrace(npc,itemId,"前往 "+town.name+" 购买，现有 "+money+" 灵石，价格 "+config.worth);
                return plan;
            }
            SupplyTrace(npc,itemId,LifePolicy.PurchaseFailure(matches,quotaBlocked,routeBlocked),
                "stockMatches="+matches+" quotaBlocked="+quotaBlocked+" routeBlocked="+routeBlocked+" transitLinks="+transitLinks.Count+" catalog="+string.Join(";",catalog));
            return null;
        }
        private static void SupplyTrace(NpcState npc,int itemId,string reason,string detail="")
        {
            npc.SupplyStatus=ItemName(itemId)+"："+reason;
            string key=itemId+":"+reason+":"+detail;
            if(npc.LastSupplyTrace==key) return;
            npc.LastSupplyTrace=key;
            ModMain.Log(npc.Id+" pill-supply name="+Unit(npc.Id).data.unitData.propertyData.GetName()+" item="+itemId+" "+reason+" "+detail);
        }
        internal ActivityAttempt ExecutePurchase(NpcState npc,ActivityPlan plan)
        {
            if(plan.Submitted) return ActivityAttempt.Result(AttemptKind.Failed,"采购已有提交记录，不自动重扣或补发");
            var unit=Unit(npc.Id); var props=unit.data.unitData.propData;
            if(props.GetPropsNum(plan.ItemId)>0) return ActivityAttempt.Result(AttemptKind.Complete,"所需材料已有，重新检查突破条件");
            if(npc.Budget<1) return ActivityAttempt.Result(AttemptKind.Wait,"到达商店，等待一天采购时间");
            var town=g.world.build.GetBuild(plan.SourceId)?.TryCast<MapBuildTown>();
            var market=town?.GetBuildSub(MapBuildSubType.TownMarketPill)?.TryCast<MapBuildTownMarket>();
            var config=g.conf.itemProps.GetItem(plan.ItemId);
            string key=LifePolicy.MarketKey(g.world.run.roundMonth,plan.SourceId,plan.ItemId);
            if(market?.data?.props==null || !market.data.props.Contains(plan.ItemId) || config==null || config.worth<=0 || LifeState.MarketPurchases.ContainsKey(key))
                return ActivityAttempt.Result(AttemptKind.Failed,"商店无此现货或本月 NPC 采购额度已用完");
            int price=config.worth, before=props.GetPropsNum(10001);
            if(before<price) return ActivityAttempt.Result(AttemptKind.Failed,"采购资金不足，重新筹备");
            if(!LifePolicy.ReserveSubmission(npc,plan,1)) return ActivityAttempt.Result(AttemptKind.Failed,"采购时间或提交状态已变化");
            LifeState.MarketPurchases[key]=true;
            ModMain.Instance.Persist();
            props.DelProps(10001,price);
            if(props.GetPropsNum(10001)!=before-price) return ActivityAttempt.Result(AttemptKind.Failed,"采购扣费未确认，停止自动结算");
            props.AddProps(plan.ItemId,1,"");
            npc.LastEvent="采购 "+ItemName(plan.ItemId)+"，支付 "+price+" 灵石";
            ModMain.Log(npc.Id+" life-purchase item="+plan.ItemId+" money="+before+"->"+props.GetPropsNum(10001)+" count="+props.GetPropsNum(plan.ItemId));
            return ActivityAttempt.Result(props.GetPropsNum(plan.ItemId)>0 ? AttemptKind.Complete : AttemptKind.Failed,
                props.GetPropsNum(plan.ItemId)>0 ? npc.LastEvent : "已扣费但入库未确认，请查看日志");
        }
    }
}
