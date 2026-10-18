using System;
using System.Linq;

namespace NpcMonthActivity
{
    public interface IActivityAvailability { bool BusyAsTarget(string id); }
    public interface IActivityWorld
    {
        bool TryPosition(string id, out Point position, out string pauseReason);
        bool TryTarget(string id, Point origin, int decision, out Point target);
        bool TryStep(string id, Point origin, Point target, out Point next, out double cost);
        bool Move(string id, Point origin, Point next);
        void Trace(string message);
    }

    public sealed class ActivityEngine
    {
        public ActivityState State { get; private set; }
        private int cursor;
        public ActivityEngine(ActivityState state) { State = state; }
        public bool Enabled(string id) { return State.Npcs.ContainsKey(id); }
        public void Toggle(string id)
        {
            if (!State.Npcs.Remove(id)) State.Npcs.Add(id, new NpcState { Id = id });
        }
        public void Advance(double days)
        {
            if (double.IsNaN(days) || double.IsInfinity(days) || days <= 0) return;
            State.SimulatedDays += days;
            foreach (var npc in State.Npcs.Values) npc.Budget += days;
        }

        public bool Pump(IActivityWorld world, int maxOperations = 32)
        {
            var ids = State.Npcs.Keys.ToArray();
            if (ids.Length == 0) return false;
            bool changed = false;
            // Round-robin guarantees a large enabled set cannot starve its last members.
            int count = Math.Min(maxOperations, ids.Length * 4);
            for (int i = 0; i < count; i++)
            {
                var npc = State.Npcs[ids[cursor++ % ids.Length]];
                if (cursor == int.MaxValue) cursor = 0;
                if ((world as IActivityAvailability)?.BusyAsTarget(npc.Id) ?? false) continue;
                if (npc.Budget <= 0.000001 && !((world as IPlannedWorld)?.HasPending(npc.Id) ?? false) &&
                    !(npc.Plan != null && npc.HasPosition && ((world as ITransitWorld)?.HasReadyTransfer(npc.Id, npc.LastPosition, npc.Plan.Target) ?? false))) continue;
                try { changed |= Tick(npc, world); }
                catch (Exception ex)
                {
                    (world as IPlannedWorld)?.Cancel(npc.Id);
                    npc.Plan = null;
                    npc.Status = "暂停：活动接口异常";
                    npc.Budget = 0;
                    npc.NeededBudget = 0;
                    world.Trace(npc.Id + " npc-activity-error " + ex);
                    changed = true;
                }
            }
            return changed;
        }

        private static bool Tick(NpcState npc, IActivityWorld world)
        {
            var pendingWorld=world as IPlannedWorld;
            // Native callbacks may report death or relocation; collect their result before position validation.
            if(npc.Plan!=null && (pendingWorld?.HasPending(npc.Id) ?? false))
                return HandleAttempt(npc,world,pendingWorld,pendingWorld.Execute(npc,npc.Plan));
            Point pos; string reason;
            if (!world.TryPosition(npc.Id, out pos, out reason))
            {
                (world as IPlannedWorld)?.Cancel(npc.Id);
                if (npc.Status != "暂停：" + reason) world.Trace(npc.Id + " pause " + reason);
                npc.Status = "暂停：" + reason;
                // Time spent dead, in battle or otherwise unavailable is not deferred travel.
                npc.Budget = 0;
                return true;
            }
            if (npc.HasPosition && !npc.LastPosition.Equals(pos))
            {
                npc.HasTarget = false;
                npc.RestRemaining = 0;
                npc.NeededBudget = 0;
                npc.Budget = 0;
                npc.LastPosition = pos;
                (world as IPlannedWorld)?.Cancel(npc.Id);
                npc.Plan = null;
                npc.Status = "暂停：位置被其他行动改变，重新计划";
                world.Trace(npc.Id + " external-position " + pos);
                return true;
            }
            npc.HasPosition = true;
            npc.LastPosition = pos;
            var planned = world as IPlannedWorld;
            if (planned != null) return TickPlanned(npc, world, planned, pos);
            if (npc.Budget + 0.000001 < npc.NeededBudget) return false;
            if (npc.RestRemaining > 0)
            {
                double used = Math.Min(npc.Budget, npc.RestRemaining);
                npc.Budget -= used;
                npc.RestRemaining -= used;
                npc.Status = "停留";
                return true;
            }
            if (!npc.HasTarget)
            {
                Point target;
                if (!world.TryTarget(npc.Id, pos, npc.Decisions++, out target))
                {
                    world.Trace(npc.Id + " no-reachable-target origin=" + pos);
                    npc.Status = "待命：附近暂无可达目的地";
                    npc.Budget = 0;
                    return true;
                }
                npc.Target = target;
                npc.NeededBudget = 0;
                npc.HasTarget = true;
                world.Trace(npc.Id + " target " + target);
            }
            if (pos.Equals(npc.Target))
            {
                world.Trace(npc.Id + " step-unavailable origin=" + pos + " target=" + npc.Target);
                npc.HasTarget = false;
                npc.RestRemaining = 1;
                npc.Status = "停留";
                return true;
            }
            Point next; double cost;
            if (!world.TryStep(npc.Id, pos, npc.Target, out next, out cost) ||
                next.Equals(pos) || double.IsNaN(cost) || double.IsInfinity(cost) || cost <= 0)
            {
                npc.HasTarget = false;
                npc.NeededBudget = 0;
                npc.Status = "暂停：路线或移动条件不可用";
                npc.Budget = 0;
                return true;
            }
            npc.Status = "前往 " + npc.Target;
            npc.NeededBudget = cost;
            if (npc.Budget + 0.000001 < cost) return false;
            if (!world.Move(npc.Id, pos, next))
            {
                npc.Status = "暂停：原生移动未完成";
                npc.Budget = 0;
                return true;
            }
            npc.Budget = Math.Max(0, npc.Budget - cost);
            npc.NeededBudget = 0;
            npc.LastPosition = next;
            world.Trace(npc.Id + " move " + pos + " -> " + next + " days=" + cost.ToString("F3"));
            if (next.Equals(npc.Target))
            {
                npc.HasTarget = false;
                npc.RestRemaining = 1;
                npc.Status = "停留";
            }
            return true;
        }

        private static bool TickPlanned(NpcState npc, IActivityWorld world, IPlannedWorld planned, Point pos)
        {
            if (npc.Cooldown > 0)
            {
                double time = Math.Min(npc.Budget, npc.Cooldown);
                npc.Budget -= time; npc.Cooldown -= time;
                return true;
            }
            var needs=npc.Plan==null ? null : planned.Snapshot(npc.Id);
            if (npc.Plan != null && NeedPolicy.Urgent(needs, npc.Plan))
            {
                world.Trace(npc.Id+" plan-interrupted kind="+npc.Plan.Kind+" hp="+needs.Hp+" health="+needs.Health+
                    " energy="+needs.Energy+" growthBlocked="+needs.CultivationBlocked+" budget="+npc.Budget);
                planned.Cancel(npc.Id); npc.Plan = null; npc.NeededBudget = 0;
            }
            if (npc.Plan == null)
            {
                string reason;
                npc.Plan = planned.Plan(npc, pos, out reason);
                if (npc.Plan == null)
                {
                    if (reason == null) return false; // Planning work is deferred to another frame without costing game time.
                    npc.Status = "待命：" + reason;
                    npc.Cooldown = 1; npc.Budget = 0;
                    return true;
                }
                npc.Target = npc.Plan.Target; npc.HasTarget = true; npc.NeededBudget = 0;
                world.Trace(npc.Id + " plan kind=" + npc.Plan.Kind + " target=" + npc.Target + " reason=" + npc.Plan.Reason);
            }
            if (!pos.Equals(npc.Plan.Target))
            {
                var transit = world as ITransitWorld;
                if (transit != null && transit.HasReadyTransfer(npc.Id, pos, npc.Plan.Target))
                {
                    Point exit; string detail;
                    if (!transit.Transfer(npc.Id, pos, npc.Plan.Target, out exit, out detail))
                        return FailPlan(npc, planned, detail);
                    npc.LastPosition = exit; npc.HasPosition = true; npc.NeededBudget = 0; npc.Status = detail;
                    world.Trace(npc.Id + " transfer " + pos + " -> " + exit + " days=0 cost=0 goal=" + npc.Plan.Target);
                    return true;
                }
                if (npc.Budget + .000001 < npc.NeededBudget) return false;
                Point next; double cost;
                if (!world.TryStep(npc.Id, pos, npc.Plan.Target, out next, out cost) || cost <= 0 || double.IsNaN(cost) || double.IsInfinity(cost))
                    return FailPlan(npc, planned, "路线或区域通行条件不满足");
                npc.NeededBudget = cost;
                npc.Status = "前往" + npc.Plan.Label + " " + npc.Plan.Target;
                if (npc.Budget + .000001 < cost) return false;
                if (!world.Move(npc.Id, pos, next)) return FailPlan(npc, planned, "游戏拒绝移动");
                npc.Budget = Math.Max(0, npc.Budget - cost); npc.NeededBudget = 0; npc.LastPosition = next;
                world.Trace(npc.Id + " move " + pos + " -> " + next + " days=" + cost.ToString("F3"));
                return true;
            }
            return HandleAttempt(npc,world,planned,planned.Execute(npc,npc.Plan));
        }
        private static bool HandleAttempt(NpcState npc,IActivityWorld world,IPlannedWorld planned,ActivityAttempt attempt)
        {
            if (double.IsNaN(attempt.Spent) || double.IsInfinity(attempt.Spent) || attempt.Spent < 0 || attempt.Spent > npc.Budget + .000001)
                throw new InvalidOperationException("原生活动时间结算无效");
            npc.Budget = Math.Max(0, npc.Budget - attempt.Spent);
            npc.Status = attempt.Detail;
            if (attempt.Kind == AttemptKind.Travel)
            {
                npc.Plan.Target = attempt.Destination; npc.Target = attempt.Destination; npc.NeededBudget = 0;
                return true;
            }
            if (attempt.Kind == AttemptKind.Failed) return FailPlan(npc, planned, attempt.Detail);
            if (attempt.Kind == AttemptKind.Complete)
            {
                world.Trace(npc.Id + " activity-complete " + attempt.Detail);
                bool cultivation=npc.Plan?.Kind==NeedKind.Routine;
                planned.Cancel(npc.Id); npc.Plan = null; npc.HasTarget = false; npc.Cooldown = cultivation ? 0 : 1;
                return true;
            }
            return attempt.Spent > 0;
        }
        private static bool FailPlan(NpcState npc, IPlannedWorld world, string reason)
        {
            (world as IActivityWorld)?.Trace(npc.Id+" plan-failed kind="+npc.Plan?.Kind+" reason="+reason+" budget="+npc.Budget);
            if (npc.Plan != null && (npc.Plan.Kind == NeedKind.SchoolTask || npc.Plan.Kind == NeedKind.TownTask || npc.Plan.Kind == NeedKind.Adventure))
                npc.SkipNativePlans = 2;
            world.Cancel(npc.Id);
            npc.Plan = null; npc.HasTarget = false; npc.NeededBudget = 0; npc.Cooldown = 2;
            npc.Status = "暂停：" + reason;
            return true;
        }
    }
}
