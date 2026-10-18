using System;
using System.Collections.Generic;

namespace NpcMonthActivity
{
    public struct Point : IEquatable<Point>
    {
        public int X, Y;
        public Point(int x, int y) { X = x; Y = y; }
        public bool Equals(Point p) { return X == p.X && Y == p.Y; }
        public override bool Equals(object obj) { return obj is Point && Equals((Point)obj); }
        public override int GetHashCode() { return unchecked(X * 397 ^ Y); }
        public override string ToString() { return "(" + X + "," + Y + ")"; }
    }

    public sealed class NpcState
    {
        public string Id = "";
        public bool HasTarget;
        public Point Target;
        public Point LastPosition;
        public bool HasPosition;
        public double Budget;
        public double RestRemaining;
        public double NeededBudget;
        public int Decisions;
        public string Status = "待命";
        public ActivityPlan Plan;
        public double Cooldown;
        public int LastRoutine;
        public NeedKind LastNeed;
        public int SkipNativePlans;
        public CommissionState Commission;
        public int CommissionMonth = -1, CommissionCount;
        public string LastEvent = "";
        public int NoGainGradeId=-1, NoGainExp=-1;
        public string SupplyStatus="", LastSupplyTrace="";
        public PillUseRecord PillUse;
        public int FailedPillGrade=-1,FailedPillExp=-1;
    }

    public sealed class ActivityState
    {
        public int Version = 1;
        public int RuntimeRevision;
        public string PlayerId = "";
        public double SimulatedDays;
        public Dictionary<string, NpcState> Npcs = new Dictionary<string, NpcState>();
        public Dictionary<string, double> SocialReadyAt = new Dictionary<string, double>();
        public Dictionary<string, bool> MarketPurchases = new Dictionary<string, bool>();
        public Dictionary<string, string> RecentEvents = new Dictionary<string, string>();

        public void UpgradeRuntime()
        {
            UpgradeLegacyRuntime();
            if(RuntimeRevision>=10) return;
            foreach(var npc in Npcs.Values)
                if(npc.Plan!=null && !npc.Plan.Submitted && (npc.Plan.Kind==NeedKind.Routine ||
                    npc.Plan.Kind==NeedKind.Purchase || npc.Plan.Kind==NeedKind.PhasePill || npc.Plan.Kind==NeedKind.Breakthrough))
                { npc.Plan=null; npc.HasTarget=false; npc.NeededBudget=0; npc.SupplyStatus="重新核对真实突破材料及库存"; }
            RuntimeRevision=10;
        }
        private void UpgradeLegacyRuntime()
        {
            if(RuntimeRevision>=6) return;
            if(RuntimeRevision>=5)
            {
                foreach(var npc in Npcs.Values)
                    if(npc.Plan!=null && !npc.Plan.Submitted && npc.Plan.Kind==NeedKind.Routine)
                    { npc.Plan=null; npc.HasTarget=false; npc.NeededBudget=0; npc.Status="重新核对丹药储备和修炼条件"; }
                RuntimeRevision=6; return;
            }
            if(RuntimeRevision>=4)
            {
                foreach(var npc in Npcs.Values)
                {
                    if(npc.Plan!=null && (npc.Plan.Kind==NeedKind.Routine || npc.Plan.Kind==NeedKind.Breakthrough && !npc.Plan.Submitted))
                    { npc.Plan=null; npc.HasTarget=false; npc.NeededBudget=0; npc.Status="重新核对修炼与突破条件"; }
                    if(npc.LastEvent!=null && npc.LastEvent.StartsWith("原生突破已生效")) npc.LastEvent="";
                    string recent;
                    if(RecentEvents.TryGetValue(npc.Id,out recent) && recent!=null && recent.StartsWith("原生突破已生效")) RecentEvents.Remove(npc.Id);
                }
                RuntimeRevision=6; return;
            }
            // Earlier builds could accrue travel time while their scheduler was blocked.
            // Keep assignments, but never replay that backlog when the fix first loads.
            foreach (var npc in Npcs.Values)
            {
                npc.Budget = 0;
                npc.NeededBudget = 0;
                npc.RestRemaining = 0;
                npc.HasTarget = false;
                npc.HasPosition = false;
                npc.Status = "待命";
                npc.Plan = null;
                npc.Cooldown = 0;
            }
            RuntimeRevision = 6;
        }

        public void Validate(string playerId)
        {
            if (Version != 1 || PlayerId != playerId || Npcs == null)
                throw new InvalidOperationException("活动数据版本或存档身份不匹配");
            foreach (var pair in Npcs)
                if (pair.Value == null || pair.Key != pair.Value.Id ||
                    !Valid(pair.Value.Budget) || !Valid(pair.Value.RestRemaining) || !Valid(pair.Value.NeededBudget) || !Valid(pair.Value.Cooldown))
                    throw new InvalidOperationException("活动数据损坏：" + pair.Key);
            foreach (var npc in Npcs.Values)
                if (npc.Plan != null && (!Enum.IsDefined(typeof(NeedKind), npc.Plan.Kind) ||
                    !Valid(npc.Plan.RequiredDays) || npc.Plan.NativeAttempts < 0 || npc.Plan.ItemCount<1 || npc.Plan.ItemId<0 || npc.Plan.GradeId<0))
                    throw new InvalidOperationException("人物计划数据损坏：" + npc.Id);
            if (!Valid(SimulatedDays)) throw new InvalidOperationException("活动时间损坏");
            if (SocialReadyAt == null || MarketPurchases == null || RecentEvents == null) throw new InvalidOperationException("生活活动记录损坏");
            foreach (var time in SocialReadyAt.Values) if (!Valid(time)) throw new InvalidOperationException("交互时间损坏");
            foreach (var npc in Npcs.Values)
            {
                var pill=npc.PillUse;
                if(pill!=null && (pill.ActorId!=npc.Id || string.IsNullOrEmpty(pill.Token) || string.IsNullOrEmpty(pill.SoleId) ||
                    pill.FromGrade<1 || pill.ToGrade<=pill.FromGrade || pill.ItemId<=0 || pill.BeforeCount<1 ||
                    pill.Values==null || pill.Values.Length==0 || !Enum.IsDefined(typeof(PillStage),pill.Stage)))
                    throw new InvalidOperationException("服药结算记录损坏："+npc.Id);
                if(npc.CommissionCount<0 || npc.CommissionCount>2 || npc.CommissionMonth < -1) throw new InvalidOperationException("委托次数损坏");
                if (npc.Commission != null && !npc.Commission.Valid()) throw new InvalidOperationException("委托数据损坏");
            }
        }
        private static bool Valid(double v) { return !double.IsNaN(v) && !double.IsInfinity(v) && v >= 0; }
    }

    // A synchronous native action may not deliver an exception-finalizer callback on IL2CPP.
    // Its scheduling guard is a frame-scoped lease, never a persistent lock.
    public sealed class ActionGuard
    {
        private int frame = -1;
        private int depth;
        public void Begin(int currentFrame)
        {
            if (frame != currentFrame) { frame = currentFrame; depth = 0; }
            depth++;
        }
        public void End(int startedFrame)
        {
            if (frame == startedFrame && depth > 0) depth--;
        }
        public bool IsActive(int currentFrame) { return frame == currentFrame && depth > 0; }
        public int Depth(int currentFrame) { return frame == currentFrame ? depth : 0; }
        public int ReleaseStale(int currentFrame)
        {
            if (frame == currentFrame) return 0;
            int released = depth;
            frame = currentFrame;
            depth = 0;
            return released;
        }
        public void Reset() { frame = -1; depth = 0; }
    }

    // The world clock has integer days; committed player movement can cost fractional days.
    public sealed class ActivityClock
    {
        private int lastDay;
        public void Reset(int worldDay) { lastDay = worldDay; }
        public double Observe(int worldDay)
        {
            double elapsed = Math.Max(0, (long)worldDay - lastDay);
            lastDay = worldDay;
            return elapsed;
        }
        public double CommitMove(int worldDay, double cost, bool moved)
        {
            double fallback = Math.Max(0, (long)worldDay - lastDay);
            lastDay = worldDay;
            if (!moved) return fallback;
            return !double.IsNaN(cost) && !double.IsInfinity(cost) && cost > 0 ? cost : fallback;
        }
    }
}
