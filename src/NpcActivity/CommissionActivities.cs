using System;
using System.Linq;

namespace NpcMonthActivity
{
    internal sealed partial class GameAdapter
    {
        internal ActivityPlan PlanCommission(NpcState npc, Point origin, bool school)
        {
            var saved = npc.Commission;
            if (saved != null)
            {
                if (saved.Stage == CommissionStage.Settling)
                { npc.LastEvent = "委托已提交结算，不自动重复发奖"; npc.Commission = null; return null; }
                if (saved.Stage == CommissionStage.Paid) npc.Commission = null;
                else return CommissionPlan(saved);
            }
            int month = g.world.run.roundMonth;
            if (!LifePolicy.CanCommission(month, npc.CommissionMonth, npc.CommissionCount)) return null;
            MapBuildBase build = school ? g.world.build.GetBuild(Unit(npc.Id).data.unitData.schoolID) : NearestTown(npc.Id, origin);
            if (build == null) return null;
            var p = FacilityPlan(npc.Id, origin, build, school ? NeedKind.SchoolTask : NeedKind.TownTask, "领取独立巡查委托，赚取突破及生活费用");
            if (p != null) p.Label = build.name + "巡查委托";
            return p;
        }
        private static ActivityPlan CommissionPlan(CommissionState job)
        {
            return new ActivityPlan { Kind=job.School ? NeedKind.SchoolTask : NeedKind.TownTask,
                Token=job.Token, SourceId=job.SourceId, Target=job.Stage == CommissionStage.Accepted ? job.Site : job.Home,
                Label=job.Stage == CommissionStage.Accepted ? "委托巡查地点" : "委托交付地点",
                Reason=job.Stage == CommissionStage.Accepted ? "巡查两天后返回领取报酬" : "巡查已完成，返回交付", RequiredDays=2 };
        }
        internal ActivityAttempt ExecuteCommission(NpcState npc, ActivityPlan plan)
        {
            var build = g.world.build.GetBuild(plan.SourceId);
            if (build == null) { npc.Commission=null; return ActivityAttempt.Result(AttemptKind.Failed, "委托来源已不存在"); }
            bool school = plan.Kind == NeedKind.SchoolTask;
            if (school && Unit(npc.Id).data.unitData.schoolID != plan.SourceId)
            { npc.Commission=null; return ActivityAttempt.Result(AttemptKind.Failed, "已离开该宗门，停止宗门委托"); }
            Point pos = Read(Unit(npc.Id).data.unitData.GetPoint());
            var job = npc.Commission;
            if (job == null)
            {
                if (npc.Budget < 1) return ActivityAttempt.Result(AttemptKind.Wait, "到达委托来源，等待一天办理领取");
                int month = g.world.run.roundMonth;
                if (!LifePolicy.CanCommission(month, npc.CommissionMonth, npc.CommissionCount))
                    return ActivityAttempt.Result(AttemptKind.Failed, "本月两次独立委托额度已用完");
                var paths = LocalPaths.Search(pos, 6, EntryPolicy(npc.Id));
                var sites = paths.Destinations.Where(p => Distance(pos,p)>=3 && Distance(pos,p)<=5).OrderBy(p=>Distance(pos,p)).ToList();
                if (sites.Count==0) return ActivityAttempt.Result(AttemptKind.Failed, "没有可达的巡查地点");
                if (npc.CommissionMonth != month) { npc.CommissionMonth=month; npc.CommissionCount=0; }
                npc.CommissionCount++;
                job = new CommissionState { Token=Guid.NewGuid().ToString("N"), SourceId=plan.SourceId,
                    School=school, Home=pos, Site=sites[(npc.CommissionCount-1)%sites.Count], Reward=LifePolicy.CommissionReward(Unit(npc.Id).data.dynUnitData.GetGrade()), AcceptedMonth=month };
                npc.Commission=job; plan.Token=job.Token;
                ModMain.Log(npc.Id+" commission-accepted token="+job.Token+" home="+job.Home+" site="+job.Site+" reward="+job.Reward);
                return new ActivityAttempt { Kind=AttemptKind.Travel, Destination=job.Site, Spent=1, Detail="已领取巡查委托，前往巡查地点" };
            }
            if (plan.Token != job.Token || job.SourceId != plan.SourceId)
                return ActivityAttempt.Result(AttemptKind.Failed, "委托身份不匹配，未结算");
            if (job.Stage == CommissionStage.Accepted)
            {
                if (!pos.Equals(job.Site)) return new ActivityAttempt { Kind=AttemptKind.Travel, Destination=job.Site, Detail="继续前往巡查地点" };
                if (npc.Budget < 2) return ActivityAttempt.Result(AttemptKind.Wait, "到达巡查地点，等待两天完成巡查");
                if (!job.Patrol(pos)) return ActivityAttempt.Result(AttemptKind.Failed, "巡查阶段已改变");
                ModMain.Log(npc.Id+" commission-patrolled token="+job.Token);
                return new ActivityAttempt { Kind=AttemptKind.Travel, Destination=job.Home, Spent=2, Detail="巡查完成，返回交付" };
            }
            if (job.Stage != CommissionStage.Patrolled)
                return ActivityAttempt.Result(AttemptKind.Failed, "委托已有结算记录，不重复领奖");
            if (!pos.Equals(job.Home)) return new ActivityAttempt { Kind=AttemptKind.Travel, Destination=job.Home, Detail="返回委托来源交付" };
            if (npc.Budget < 1) return ActivityAttempt.Result(AttemptKind.Wait, "已返回，等待一天交付委托");
            if (!job.BeginSettlement(pos)) return ActivityAttempt.Result(AttemptKind.Failed, "委托结算条件已改变");
            npc.Budget-=1;
            ModMain.Instance.Persist();
            var props=Unit(npc.Id).data.unitData.propData; int before=props.GetPropsNum(10001);
            props.AddProps(10001,job.Reward,"");
            int change=props.GetPropsNum(10001)-before;
            job.Stage=CommissionStage.Paid;
            npc.LastEvent="独立巡查委托已交付，灵石 +"+change;
            ModMain.Log(npc.Id+" commission-paid token="+job.Token+" money="+before+"->"+props.GetPropsNum(10001));
            return ActivityAttempt.Result(change==job.Reward ? AttemptKind.Complete : AttemptKind.Failed, npc.LastEvent);
        }
    }
}
