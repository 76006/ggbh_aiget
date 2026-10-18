using System;

namespace NpcMonthActivity
{
    public enum CommissionStage { Accepted, Patrolled, Settling, Paid }
    public sealed class CommissionState
    {
        public string Token = "", SourceId = "";
        public bool School;
        public Point Home, Site;
        public CommissionStage Stage;
        public int Reward, AcceptedMonth;
        public bool Valid() { return !string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(SourceId) && Reward > 0 &&
            AcceptedMonth >= 0 && Enum.IsDefined(typeof(CommissionStage), Stage) && !Home.Equals(Site); }
        public bool Patrol(Point position) {
            if (Stage != CommissionStage.Accepted || !position.Equals(Site)) return false;
            Stage = CommissionStage.Patrolled; return true;
        }
        public bool BeginSettlement(Point position) {
            if (Stage != CommissionStage.Patrolled || !position.Equals(Home)) return false;
            Stage = CommissionStage.Settling; return true;
        }
    }
    public static class LifePolicy
    {
        public static bool CanInteract(string actor, string target, string player, bool assigned, bool alive, int distance)
        { return assigned && alive && !string.IsNullOrEmpty(actor) && !string.IsNullOrEmpty(target) &&
            actor != target && actor != player && target != player && distance >= 0 && distance <= 1; }
        public static bool Ready(double now, double until) { return now >= until && !double.IsNaN(now) && !double.IsNaN(until); }
        public static bool CanGive(int count, bool selfNeeds) { return !selfNeeds && count >= 2; }
        public static bool CanCommission(int month, int savedMonth, int count) { return month >= 0 && (month != savedMonth || count < 2); }
        public static string MarketKey(int month, string source, int item) { return month + ":" + source + ":" + item; }
        public static string PurchaseFailure(int stockMatches, int quotaBlocked, int routeBlocked)
        {
            if (stockMatches==0) return "有足够资金，但查询的闹市未列出此丹药现货";
            if (routeBlocked==0 && quotaBlocked>0) return "有现货，但有货店铺的本月 NPC 采购配额已用完";
            if (quotaBlocked>0) return "有现货，但当前没有可达路线；部分店铺本月 NPC 采购配额已用完";
            return "有现货，但当前没有可达路线，请核对阵法出口或区域通行条件";
        }
        public static int CommissionReward(int grade) { return 100 * Math.Max(1, Math.Min(10, grade)); }
        public static bool MaterialSelectionComplete(int required, int selected)
        { return required >= 0 && selected >= required; }
        public static bool ReserveSubmission(NpcState npc, ActivityPlan plan, double cost)
        {
            if(plan.Submitted || double.IsNaN(cost) || double.IsInfinity(cost) || cost<=0 || npc.Budget+.000001<cost) return false;
            npc.Budget=Math.Max(0,npc.Budget-cost); plan.Submitted=true; return true;
        }
    }
}
