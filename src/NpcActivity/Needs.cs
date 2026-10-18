using System;
using System.Collections.Generic;

namespace NpcMonthActivity
{
    public enum NeedKind { Routine, Recover, Rest, SchoolTask, TownTask, Adventure, Breakthrough, Purchase, Give, Ask, Rob, Fight, PhasePill, PhaseRepair }
    public sealed class NeedSnapshot
    {
        public double Hp, Health, Energy;
        public int Money, Grade;
        public bool HasSchool;
        public bool CultivationBlocked;
    }
    public static class NeedPolicy
    {
        public static NeedKind Select(NeedSnapshot s, NeedKind previous)
        {
            if (s.Hp < .70 || s.Health < .70 || previous == NeedKind.Recover && (s.Hp < .90 || s.Health < .90))
                return NeedKind.Recover;
            if (s.Energy < .30 || previous == NeedKind.Rest && s.Energy < .80) return NeedKind.Rest;
            return NeedKind.Routine;
        }
        public static bool Urgent(NeedSnapshot s, ActivityPlan plan)
        {
            if(plan.Kind==NeedKind.Routine && s.CultivationBlocked) return true;
            var need = Select(s, plan.Kind);
            return (need == NeedKind.Recover || need == NeedKind.Rest) && need != plan.Kind;
        }
    }
    public sealed class ActivityPlan
    {
        public NeedKind Kind;
        public Point Target;
        public string Label = "", Reason = "", SourceId = "", Token = "";
        public int NativeAttempts;
        public double RequiredDays = 1;
        public string TaskId = "";
        public string OtherId = "", ItemSoleId = "";
        public int ItemId, ItemCount = 1, GradeId;
        // Durable reservation: a saved native call is never blindly re-issued after loading.
        public bool Submitted;
    }
    public enum AttemptKind { Wait, Pending, Complete, Failed, Travel }
    public sealed class ActivityReservation
    {
        private readonly HashSet<long> keys = new HashSet<long>();
        private double allowance, paid;
        public double Spent { get; private set; }
        public ActivityReservation(double available, double startup)
        {
            if (!Valid(available) || !Valid(startup) || startup > available) throw new ArgumentOutOfRangeException();
            allowance = available; Spent = startup;
        }
        private static bool Valid(double value) { return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0; }
        public bool Contains(long key) { return keys.Contains(key); }
        public bool TryReserve(long key, double cost)
        {
            if (!Valid(cost)) return false;
            if (keys.Contains(key)) return true;
            if (cost > allowance - Spent + .000001) return false;
            keys.Add(key); Spent += cost; return true;
        }
        public void ReleaseKey(long key) { keys.Remove(key); }
        public double Collect() { double delta = Spent - paid; paid = Spent; return delta; }
        public void Refresh(double remaining)
        {
            if (!Valid(remaining)) throw new ArgumentOutOfRangeException(nameof(remaining));
            allowance = Spent + remaining;
        }
    }
    public sealed class ActivityAttempt
    {
        public AttemptKind Kind;
        public double Spent;
        public Point Destination;
        public string Detail = "";
        public static ActivityAttempt Result(AttemptKind kind, string detail, double spent = 0)
        { return new ActivityAttempt { Kind = kind, Detail = detail, Spent = spent }; }
    }
    public interface IPlannedWorld
    {
        NeedSnapshot Snapshot(string id);
        ActivityPlan Plan(NpcState npc, Point origin, out string reason);
        ActivityAttempt Execute(NpcState npc, ActivityPlan plan);
        void Cancel(string id);
        bool HasPending(string id);
    }
}
