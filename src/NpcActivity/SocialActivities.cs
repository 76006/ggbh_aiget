using System;
using System.Linq;

namespace NpcMonthActivity
{
    internal sealed partial class GameAdapter
    {
        private bool SocialReady(string id)
        {
            double until;
            return !LifeState.SocialReadyAt.TryGetValue(id,out until) || LifePolicy.Ready(LifeState.SimulatedDays,until);
        }
        private static double EstimatedStrength(WorldUnitBase unit)
        {
            var d=unit.data.dynUnitData;
            return Math.Max(1,d.attack.value)*Math.Sqrt(Math.Max(1,d.defense.value))*Math.Sqrt(Math.Max(1,d.hp.value));
        }
        private bool SocialEligible(NpcState npc,WorldUnitBase other)
        {
            if(other==null) return false;
            string target=other.data.unitData.unitID;
            return LifePolicy.CanInteract(npc.Id,target,LifeState.PlayerId,LifeState.Npcs.ContainsKey(npc.Id),
                !other.isDie && other.data.dynUnitData.isHide==0,
                Distance(Read(Unit(npc.Id).data.unitData.GetPoint()),Read(other.data.unitData.GetPoint()))) &&
                SocialReady(npc.Id) && SocialReady(target) && !NativeActions.Busy(npc.Id) && !NativeActions.Busy(target);
        }
        internal ActivityPlan PlanSocial(NpcState npc,Point origin,int missingItem)
        {
            if(!SocialReady(npc.Id) || NativeActions.Busy(npc.Id)) return null;
            var actor=Unit(npc.Id);
            var nearby=g.world.unit.GetUnit(Write(origin),1,false);
            // Hard cap work; native area query supplies ordinary NPCs regardless of AI assignment.
            int inspected=0;
            for(int i=0;i<nearby.Count && inspected<16;i++)
            {
                var other=nearby[i];
                if(!SocialEligible(npc,other)) continue;
                inspected++;
                string otherId=other.data.unitData.unitID;
                int affinity=actor.data.unitData.relationData.GetIntim(otherId);
                bool hostile=actor.data.GetRelationType(other)==UnitBothRelationType.Hater;
                bool advantage=actor.data.dynUnitData.GetGrade()>=other.data.dynUnitData.GetGrade() && EstimatedStrength(actor)>EstimatedStrength(other)*1.4;
                if(missingItem>0)
                {
                    var list=other.data.unitData.propData.GetProps(missingItem);
                    if(list!=null && list.Count>0)
                    {
                        if(hostile && advantage)
                            return SocialPlan(npc,other,NeedKind.Rob,list[0],"为获取缺少的 "+ItemName(missingItem)+"，向敌对且较弱者打劫");
                        if(!hostile)
                            return SocialPlan(npc,other,NeedKind.Ask,list[0],"筹备突破，向附近人物索取 "+ItemName(missingItem));
                    }
                    continue;
                }
                if(hostile && advantage)
                    return SocialPlan(npc,other,NeedKind.Fight,null,"与敌对人物相遇，按原生 NPC 规则处理冲突");
                bool friendly=affinity>=100 || actor.data.unitData.relationData.IsFriend(other) ||
                    actor.data.unitData.relationData.GetRelation(other)!=UnitRelationType.None;
                if(hostile || !friendly) continue;
                var needs=ReadGrowth(other);
                if(needs.MissingItem>0)
                {
                    var ownNeeds=ReadGrowth(actor);
                    var items=actor.data.unitData.propData.GetProps(needs.MissingItem);
                    int count=actor.data.unitData.propData.GetPropsNum(needs.MissingItem);
                    if(items!=null && items.Count>0 && LifePolicy.CanGive(count,ownNeeds.MissingItem==needs.MissingItem || RequiredForGrowth(actor,needs.MissingItem)))
                        return SocialPlan(npc,other,NeedKind.Give,items[0],"帮助亲近人物筹备突破，赠予多余的 "+ItemName(needs.MissingItem));
                }
                if(actor.data.unitData.propData.GetPropsNum(10001)<200 && other.data.unitData.propData.GetPropsNum(10001)>=1000)
                {
                    var coins=other.data.unitData.propData.GetProps(10001);
                    if(coins!=null && coins.Count>0)
                    {
                        var plan=SocialPlan(npc,other,NeedKind.Ask,coins[0],"生活费用不足，向亲近人物请求 100 灵石援助");
                        plan.ItemCount=100; return plan;
                    }
                }
            }
            return null;
        }
        private static bool RequiredForGrowth(WorldUnitBase unit,int itemId)
        {
            var current=g.conf.roleGrade.GetItem(unit.data.dynUnitData.gradeID.value);
            var next=current==null ? null : g.conf.roleGrade.GetNextGradeItem(current.id);
            if(next?.growItemRequire!=null && next.growItemRequire.Contains(itemId)) return true;
            if(next!=null)
            {
                var req=g.conf.roleGrade.GetFastRequire(next,unit);
                var rarity=g.conf.roleGrade.GetFastRarity(next,unit);
                if(req!=null) foreach(var sole in req) if(unit.data.unitData.propData.GetProps(sole)?.propsID==itemId) return true;
                if(rarity!=null) foreach(var sole in rarity) if(unit.data.unitData.propData.GetProps(sole)?.propsID==itemId) return true;
            }
            return false;
        }
        private static ActivityPlan SocialPlan(NpcState npc,WorldUnitBase other,NeedKind kind,DataProps.PropsData item,string reason)
        {
            string name=other.data.unitData.propertyData.GetName();
            return new ActivityPlan { Kind=kind,Target=Read(Unit(npc.Id).data.unitData.GetPoint()),
                OtherId=other.data.unitData.unitID,ItemId=item==null ? 0 : item.propsID,ItemSoleId=item?.soleID??"",
                Label=(kind==NeedKind.Give ? "赠予 " : kind==NeedKind.Ask ? "索取 " : kind==NeedKind.Rob ? "打劫 " : "斗殴 ")+name,
                Reason=reason };
        }
        internal ActivityAttempt ExecuteSocial(NpcState npc,ActivityPlan plan)
        {
            if(NativeActions.HasPending(npc.Id)) return NativeActions.Execute(npc,plan,null,null);
            if(plan.Submitted) return ActivityAttempt.Result(AttemptKind.Failed,"交互已经提交，重新核对结果");
            var other=Unit(plan.OtherId); var actor=Unit(npc.Id);
            if(!SocialEligible(npc,other)) return ActivityAttempt.Result(AttemptKind.Failed,"对方已离开、不可交互或双方仍在冷却");
            bool gift=plan.Kind==NeedKind.Give;
            var source=gift ? actor : other;
            var targetItem=plan.ItemId>0 ? source.data.unitData.propData.GetProps(plan.ItemSoleId) : null;
            if(plan.Kind!=NeedKind.Fight && (targetItem==null || targetItem.propsID!=plan.ItemId || targetItem.propsCount<plan.ItemCount))
                return ActivityAttempt.Result(AttemptKind.Failed,"原定物品已不存在或数量不足");
            if(gift && (!LifePolicy.CanGive(source.data.unitData.propData.GetPropsNum(plan.ItemId),RequiredForGrowth(actor,plan.ItemId))))
                return ActivityAttempt.Result(AttemptKind.Failed,"物品需要自用或不足以保留储备，取消赠予");
            int ownBefore=actor.data.unitData.propData.GetPropsNum(plan.ItemId);
            int otherBefore=other.data.unitData.propData.GetPropsNum(plan.ItemId);
            Func<UnitActionBase> create;
            if(gift)
            {
                var copy=targetItem.Clone(); copy.propsCount=plan.ItemCount;
                var list=new Il2CppSystem.Collections.Generic.List<DataProps.PropsData>(); list.Add(copy);
                create=()=>new UnitActionRoleGive(other,list);
            }
            else if(plan.Kind==NeedKind.Ask) create=()=>new UnitActionRoleAskfor(other,plan.ItemSoleId,plan.ItemCount);
            else create=()=>new UnitActionRoleAttack(other);
            return NativeActions.Execute(npc,plan,create,a=> {
                string name=other.data.unitData.propertyData.GetName();
                if(plan.Kind==NeedKind.Give || plan.Kind==NeedKind.Ask)
                {
                    int ownAfter=actor.data.unitData.propData.GetPropsNum(plan.ItemId);
                    int otherAfter=other.data.unitData.propData.GetPropsNum(plan.ItemId);
                    int moved=gift ? otherAfter-otherBefore : ownAfter-ownBefore;
                    bool conserved=(long)ownBefore+otherBefore==(long)ownAfter+otherAfter;
                    ModMain.Log(npc.Id+" social-items item="+plan.ItemId+" actor="+ownBefore+"->"+ownAfter+" target="+otherBefore+"->"+otherAfter);
                    if(!conserved) return ActivityAttempt.Result(AttemptKind.Failed,"原生交互物品变化需核对，请查看日志");
                    return ActivityAttempt.Result(AttemptKind.Complete,moved>0 ?
                        (gift ? name+"接受赠予：" : name+"同意索取：")+ItemName(plan.ItemId)+" ×"+moved : name+"未接受本次"+(gift ? "赠予" : "索取"));
                }
                var attack=a.TryCast<UnitActionRoleAttack>(); var battle=attack?.roleBattle;
                var winner=battle?.winUnit; var loser=battle?.failUnit;
                if(actor.isDie || other.isDie)
                    return ActivityAttempt.Result(AttemptKind.Complete,(actor.isDie ? "发起者" : name)+"在原生冲突中死亡；掉落和关系由游戏结算");
                if(winner==null || loser==null)
                    return ActivityAttempt.Result(AttemptKind.Complete,"原生冲突已结束：未确认击败，可能逃脱、拒绝或被阻止");
                bool won=winner.data.unitData.unitID==npc.Id && loser.data.unitData.unitID==plan.OtherId;
                string result=won ? "击败了 "+name : "冲突落败，原生伤势及关系已由游戏处理";
                if(won && plan.Kind==NeedKind.Rob)
                {
                    if(Distance(Read(actor.data.unitData.GetPoint()),Read(other.data.unitData.GetPoint()))>1)
                        return ActivityAttempt.Result(AttemptKind.Complete,result+"，对方已离开，未远程夺取物品");
                    // Native attack decides fighting/death. Robbery adds only the selected, still existing property, once.
                    var actual=other.data.unitData.propData.GetProps(plan.ItemSoleId);
                    if(actual!=null && actual.propsID==plan.ItemId && actual.propsCount>=1)
                    {
                        var copy=actual.Clone(); copy.propsCount=1; copy.soleID=Guid.NewGuid().ToString("N");
                        int before=other.data.unitData.propData.GetPropsNum(plan.ItemId);
                        other.data.unitData.propData.DelProps(plan.ItemSoleId,1);
                        if(other.data.unitData.propData.GetPropsNum(plan.ItemId)==before-1)
                        {
                            int receivedBefore=actor.data.unitData.propData.GetPropsNum(plan.ItemId);
                            actor.data.unitData.propData.AddProps(copy);
                            result+=actor.data.unitData.propData.GetPropsNum(plan.ItemId)==receivedBefore+1 ?
                                "，夺取 "+ItemName(plan.ItemId)+" ×1" : "，对方已扣除物品但入库未确认，请查看日志";
                        }
                        else result+="，物品扣除未确认，停止转移";
                    }
                    else result+="，原定物品已不在对方背包，未额外发放";
                }
                return ActivityAttempt.Result(AttemptKind.Complete,result);
            });
        }
    }
}
