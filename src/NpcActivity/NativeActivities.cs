using System;
using System.Collections.Generic;
using System.Linq;
using UnhollowerBaseLib;

namespace NpcMonthActivity
{
    internal sealed partial class GameAdapter
    {
        internal static string OwnActivityId;
        private int lastPlanningFrame = -1;
        public NeedSnapshot Snapshot(string id)
        {
            var u = Unit(id); var d = u.data.dynUnitData;
            return new NeedSnapshot {
                Hp = Ratio(d.hp.value, d.hpMax.value), Health = Ratio(d.health.value, d.healthMax.value),
                Energy = Ratio(d.energy.value, d.energyMax.value), Money = u.data.unitData.propData.GetPropsNum(10001),
                Grade = d.GetGrade(), HasSchool = !string.IsNullOrEmpty(u.data.unitData.schoolID),
                CultivationBlocked = GrowthBlocked(u)
            };
        }
        private static double Ratio(int value, int max)
        {
            if (max <= 0) throw new InvalidOperationException("人物属性上限无效");
            return Math.Max(0, Math.Min(1, (double)value / max));
        }
        internal static bool CanEnter(string id, Point p)
        {
            var native = Write(p);
            if (!g.data.grid.IsSafe(native)) return false;
            var grid = g.data.grid.GetGridData(native);
            if (grid == null || grid.isBlock) return false;
            var area = g.conf.worldAreaBase.GetItem(grid.areaBaseID);
            var unit = Unit(id);
            return area != null && unit.data.dynUnitData.GetGrade() >= area.gradeMin && TerrainAllows(unit, grid, p);
        }
        private static bool TerrainAllows(WorldUnitBase unit, DataGrid.GridData grid, Point point)
        {
            if (grid.decorateID != 0 && !g.conf.worldTerrainDecorate.IsCondition(grid, Write(point), unit, false)) return false;
            return grid.IsAbstructArea() == 0 || WorldUnitEffectSpecialDecorateInvalid.IsIngoreCondition(unit, grid.GetDecorateItem());
        }
        internal static Func<Point, bool> EntryPolicy(string id)
        {
            int grade = Unit(id).data.dynUnitData.GetGrade();
            var unit = Unit(id);
            var cache = new Dictionary<Point, bool>();
            var limits = new Dictionary<int, int>();
            return p =>
            {
                bool allowed;
                if (cache.TryGetValue(p, out allowed)) return allowed;
                var native = Write(p);
                if (!g.data.grid.IsSafe(native)) { cache[p] = false; return false; }
                var grid = g.data.grid.GetGridData(native);
                if (grid == null || grid.isBlock) { cache[p] = false; return false; }
                int min;
                if (!limits.TryGetValue(grid.areaBaseID, out min))
                {
                    var area = g.conf.worldAreaBase.GetItem(grid.areaBaseID);
                    min = area == null ? int.MaxValue : area.gradeMin;
                    limits[grid.areaBaseID] = min;
                }
                allowed = grade >= min && TerrainAllows(unit, grid, p); cache[p] = allowed; return allowed;
            };
        }

        public ActivityPlan Plan(NpcState npc, Point origin, out string reason)
        {
            reason = null;
            if (lastPlanningFrame == UnityEngine.Time.frameCount) return null;
            lastPlanningFrame = UnityEngine.Time.frameCount;
            var settlement=PhaseSettlementPlan(npc,origin);
            if(settlement!=null) return settlement;
            if(npc.PillUse?.Stage==PillStage.Uncertain)
            { reason="上次突破结算出现非预期变化，暂停本人物活动等待核对"; return null; }
            var s = Snapshot(npc.Id);
            var kind = NeedPolicy.Select(s, npc.LastNeed);
            npc.LastNeed = kind;
            reason = "没有适用活动";
            if (kind == NeedKind.Recover || kind == NeedKind.Rest)
            {
                var town = NearestTown(npc.Id, origin);
                if (town == null) { reason = "没有可达且适合境界的休整城镇"; return null; }
                return FacilityPlan(npc.Id, origin, town, kind,
                    kind == NeedKind.Recover ? "气血或健康不足，寻找原生休整服务" : "精力不足，前往客栈休整");
            }
            var obsoleteLocks=ObsoletePhaseLocks(Unit(npc.Id));
            if(obsoleteLocks.Count>0)
                return new ActivityPlan { Kind=NeedKind.PhaseRepair,Target=origin,ItemId=obsoleteLocks[0],
                    GradeId=Unit(npc.Id).data.dynUnitData.gradeID.value,Label="核对旧阶段限制",
                    Reason="已达到当前阶段，但仍受之前阶段的修为限制；修复后重新核对真实修为" };
            bool full=s.CultivationBlocked;
            string growthReason="";
            if(full)
            {
                var growth=PlanGrowth(npc,origin,out growthReason);
                if(growth!=null) return growth;
                reason=growthReason;
                return null;
            }
            if (npc.Commission != null && npc.Commission.Stage != CommissionStage.Paid)
            {
                var ongoing=PlanCommission(npc,origin,npc.Commission.School);
                if(ongoing!=null) return ongoing;
            }
            string preparationReason;
            var preparation=PlanPhasePreparation(npc,origin,out preparationReason);
            if(preparation!=null) return preparation;
            bool focusCultivation=CultivationPolicy.PreferCultivation(PhaseCandidates(Unit(npc.Id)).Any(id=>
                Unit(npc.Id).data.unitData.propData.GetPropsNum(id)>0),s.Money,full);
            var socialPlan=focusCultivation ? null : PlanSocial(npc,origin,0);
            if(socialPlan!=null) return socialPlan;
            int choice = focusCultivation ? 0 : npc.Decisions++ % 4;
            bool nativeAllowed = npc.SkipNativePlans == 0;
            if (npc.SkipNativePlans > 0) npc.SkipNativePlans--;
            if (nativeAllowed && s.HasSchool && choice == 1)
            {
                var plan=PlanCommission(npc,origin,true);
                if(plan!=null) return plan;
            }
            if (nativeAllowed && !focusCultivation && (s.Money < 500 || choice == 2))
            {
                var plan=PlanCommission(npc,origin,false);
                if(plan!=null) return plan;
            }
            if (nativeAllowed && choice == 3)
            {
                var adventure = NativeMissions.PrepareAdventure(npc.Id);
                if (adventure != null && CanEnter(npc.Id, adventure.Target))
                {
                    var path = FindRoute(npc.Id, origin, adventure.Target);
                    if (origin.Equals(adventure.Target) || path.Count > 0)
                    { StoreRoute(npc.Id, origin, adventure.Target, path); return adventure; }
                }
            }
            if(full) { reason=string.IsNullOrEmpty(growthReason) ? "修炼已暂停，重新核对突破条件" : growthReason; return null; }
            var paths = LocalPaths.Search(origin, 8, EntryPolicy(npc.Id));
            Point best = origin;
            int bestScore = int.MinValue;
            bool found=false;
            var candidates=paths.Destinations.Concat(new[] { origin }).Distinct().OrderByDescending(p=>g.data.grid.GetGridData(Write(p)).anima-Distance(origin,p)*3).Take(16);
            int grade=Unit(npc.Id).data.dynUnitData.GetGrade();
            foreach (var candidate in candidates)
            {
                int predicted=UnitActionGridGetExp.GetAddExp(Write(candidate),grade,false,0);
                if(predicted<=0) continue;
                var probe=new UnitActionGridGetExp(Write(candidate));
                probe.Init(Unit(npc.Id)); probe.day=0; probe.isInit=true;
                if(Unit(npc.Id).IsCreateAction(probe,false)!=0) continue;
                int score = g.data.grid.GetGridData(Write(candidate)).anima -
                    (Math.Abs(candidate.X - origin.X) + Math.Abs(candidate.Y - origin.Y)) * 3;
                if (score > bestScore) { best = candidate; bestScore = score; found=true; }
            }
            if (GrowthBlocked(Unit(npc.Id)))
            { reason = string.IsNullOrEmpty(growthReason) ? "突破筹备：暂未找到可执行的获取途径" : growthReason; return null; }
            if (!found)
            { reason = "附近灵气对当前境界预计没有修炼收益，暂不赶路"; return null; }
            StoreRoute(npc.Id, origin, best, paths.PathTo(best));
            return new ActivityPlan { Kind = NeedKind.Routine, Target = best, Label = "灵气地修炼", Reason =
                (focusCultivation ? "丹药与生活资金已备妥，优先修炼至当前瓶颈" : "状态允许，寻找灵气提高修为")+
                (string.IsNullOrEmpty(preparationReason) ? "" : "；"+preparationReason) };
        }

        private void StoreRoute(string id, Point origin, Point target, List<Point> path)
        { routes[id] = new Route { Origin = origin, Target = target, Steps = new Queue<Point>(path) }; }

        private MapBuildTown NearestTown(string id, Point origin)
        {
            var towns = new List<MapBuildTown>();
            var builds = g.world.build.GetBuilds();
            for (int i = 0; i < builds.Count; i++)
            {
                var town = builds[i].TryCast<MapBuildTown>();
                if (town != null) towns.Add(town);
            }
            foreach (var town in towns.OrderBy(t => Distance(origin, Read(t.GetOrigiPoint()))).Take(4))
                if (FacilityTarget(id, origin, town, out var target, out var path)) return town;
            return null;
        }
        private static int Distance(Point a, Point b) { return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y); }
        private bool FacilityTarget(string id, Point origin, MapBuildBase build, out Point target, out List<Point> path)
        {
            target = Read(build.GetOrigiPoint()); path = new List<Point>();
            var opens = build.GetOpenBuildPoints();
            var points = new List<Point>();
            if (opens != null) for (int i = 0; i < opens.Length; i++) points.Add(Read(opens[i]));
            if (points.Count == 0) points.Add(target);
            foreach (var p in points.OrderBy(p => Distance(origin, p)).Take(4))
            {
                if (!CanEnter(id, p)) continue;
                if (p.Equals(origin)) { target = p; return true; }
                var found = FindRoute(id, origin, p);
                if (found.Count == 0) continue;
                target = p; path = found; return true;
            }
            return false;
        }
        private ActivityPlan FacilityPlan(string id, Point origin, MapBuildBase build, NeedKind kind, string reason)
        {
            Point target; List<Point> path;
            if (!FacilityTarget(id, origin, build, out target, out path)) return null;
            StoreRoute(id, origin, target, path);
            return new ActivityPlan {
                Kind = kind, Target = target, SourceId = build.buildData.id,
                Label = build.name + (kind == NeedKind.SchoolTask ? "宗门任务" : kind == NeedKind.TownTask ? "城镇任务" : "休整"), Reason = reason
            };
        }

        public ActivityAttempt Execute(NpcState npc, ActivityPlan plan)
        {
            if(plan.Kind==NeedKind.PhaseRepair) return RepairObsoletePhaseLocks(npc,plan);
            if(plan.Kind==NeedKind.Breakthrough) return ExecuteGrowth(npc,plan);
            if(plan.Kind==NeedKind.PhasePill) return ExecutePhasePill(npc,plan);
            if(plan.Kind==NeedKind.Purchase) return ExecutePurchase(npc,plan);
            if(plan.Kind==NeedKind.Give || plan.Kind==NeedKind.Ask || plan.Kind==NeedKind.Rob || plan.Kind==NeedKind.Fight)
                return ExecuteSocial(npc,plan);
            if(plan.Kind==NeedKind.SchoolTask || plan.Kind==NeedKind.TownTask) return ExecuteCommission(npc,plan);
            if (plan.Kind == NeedKind.Adventure)
            {
                if(NativeActions.AnyPending) return ActivityAttempt.Result(AttemptKind.Wait,"等待 NPC 交互或突破结束");
                return NativeMissions.Execute(npc, plan);
            }
            var unit = Unit(npc.Id);
            if(plan.Kind==NeedKind.Routine && GrowthBlocked(unit))
                return ActivityAttempt.Result(AttemptKind.Failed,"修炼条件已改变，停止修炼并筹备突破");
            UnitActionBase action;
            if (plan.Kind == NeedKind.Routine) action = new UnitActionGridGetExp(Write(plan.Target));
            else
            {
                var town = g.world.build.GetBuild(plan.SourceId)?.TryCast<MapBuildTown>();
                if (town == null) return ActivityAttempt.Result(AttemptKind.Failed, "休整城镇已失效");
                action = new UnitActionTownHotel(town);
            }
            action.Init(unit);
            int predicted=plan.Kind==NeedKind.Routine ? UnitActionGridGetExp.GetAddExp(Write(plan.Target),unit.data.dynUnitData.GetGrade(),false,0) : -1;
            if(plan.Kind==NeedKind.Routine && predicted<=0)
                return ActivityAttempt.Result(AttemptKind.Failed,"所在地灵气对当前境界预计没有修炼收益，未消耗修炼时间");
            double cost = CultivationPolicy.ActivityDays(plan.Kind==NeedKind.Routine,action.day);
            plan.RequiredDays=cost;
            var before = Snapshot(npc.Id); int exp = unit.data.dynUnitData.exp.value;
            // Our elapsed-time budget pays for this action; retain all other native checks and resource costs.
            action.day = 0; action.isInit = true;
            int allowed = unit.IsCreateAction(action, false);
            if (allowed != 0)
            {
                Trace(npc.Id+" activity-preflight-refused kind="+plan.Kind+" point="+plan.Target+" code="+allowed+" predicted="+predicted+" nativeDays="+cost);
                return ActivityAttempt.Result(AttemptKind.Failed, "原生活动条件不满足，代码 " + allowed);
            }
            if (npc.Budget + .000001 < cost)
                return ActivityAttempt.Result(AttemptKind.Wait, plan.Kind==NeedKind.Routine ?
                    "正在修炼：已积累 "+npc.Budget.ToString("F1")+"/"+cost+" 天，完成后结算修为" : "已到达，等待 " + cost + " 天活动时间");
            int result;
            try
            {
                OwnActivityId = npc.Id;
                action.day = 0; action.isInit = true;
                result = unit.CreateAction(action, false);
            }
            finally { OwnActivityId = null; }
            var after = Snapshot(npc.Id); int afterExp = unit.data.dynUnitData.exp.value;
            Trace(npc.Id + " native-activity " + plan.Kind + " result=" + result + " exp=" + exp + "->" + afterExp +
                " name="+unit.data.unitData.propertyData.GetName()+" stage="+StageName(unit.data.dynUnitData.gradeID.value)+
                " predicted="+predicted+" nativeDays="+cost+" nativeAddExp="+(action.TryCast<UnitActionGridGetExp>()?.addExp??-1)+
                " hp=" + before.Hp + "->" + after.Hp + " health=" + before.Health + "->" + after.Health +
                " energy=" + before.Energy + "->" + after.Energy + " money=" + before.Money + "->" + after.Money);
            if (result != 0) return ActivityAttempt.Result(AttemptKind.Failed, "游戏拒绝活动，代码 " + result);
            if(plan.Kind==NeedKind.Routine && afterExp<=exp)
            {
                CultivationPolicy.RecordNoGain(npc,unit.data.dynUnitData.gradeID.value,afterExp);
                LogGrowthState(unit);
                npc.LastEvent="修炼没有增加修为，已停止重复寻找灵气，重新核对瓶颈或收益条件";
                return ActivityAttempt.Result(AttemptKind.Failed,npc.LastEvent,cost);
            }
            if(plan.Kind==NeedKind.Routine) CultivationPolicy.ClearNoGain(npc);
            bool effective = plan.Kind == NeedKind.Routine ? afterExp > exp :
                after.Hp > before.Hp || after.Health > before.Health || after.Energy > before.Energy;
            return ActivityAttempt.Result(effective ? AttemptKind.Complete : AttemptKind.Failed,
                effective ? plan.Kind == NeedKind.Routine ? "修炼生效，修为 +" + (afterExp - exp) : "客栈休整已生效" : "原生活动未产生可确认效果", cost);
        }
        public void Cancel(string id) { routes.Remove(id); NativeMissions.Cancel(id); NativeActions.Cancel(id); }
        public bool HasPending(string id)
        {
            NpcState npc;
            return NativeMissions.HasPending(id) || NativeActions.HasPending(id) ||
                ModMain.Instance.Engine.State.Npcs.TryGetValue(id,out npc) && npc.PillUse?.NeedsSettlement==true;
        }
        public bool BusyAsTarget(string id) { return NativeActions.BusyOther(id); }
        internal void Reset() { routes.Clear(); transitLinks.Clear(); transitDiagnostics.Clear(); nextTransitScan=DateTime.MinValue; lastPlanningFrame = -1; NativeMissions.Reset(); NativeActions.Reset(); loggedGrades.Clear(); growthDiagnostics.Clear(); growthMaterialDiagnostics.Clear(); }
    }
}
