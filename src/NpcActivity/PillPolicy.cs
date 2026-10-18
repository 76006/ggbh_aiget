namespace NpcMonthActivity
{
    public enum PillStage { Prepared, Deferred, CommitIntent, Committed, RefundIntent, Released, Uncertain }
    public enum PillSettlement { Commit, Confirm, Keep, Restore, Hold, Closed }
    public sealed class PillUseRecord
    {
        public string ActorId="",Token="",SoleId="",ValuesStr="";
        public int ItemId,FromGrade,ToGrade,BeforeCount,StartExp,PropsType;
        public int[] Values;
        public bool Submitted;
        public PillStage Stage;
        public bool NeedsSettlement { get { return Submitted && Stage!=PillStage.Committed && Stage!=PillStage.Released && Stage!=PillStage.Uncertain; } }
    }
    public static class PillPolicy
    {
        public static bool NeedsInit(bool initialized,bool hasActor,bool sameActor)
        { return !initialized || !hasActor || !sameActor; }
        public static bool Matches(PillUseRecord r,string actor,string token,string sole,int count)
        { return r!=null && r.Submitted && r.NeedsSettlement && r.ActorId==actor && r.Token==token && r.SoleId==sole && count==1; }
        public static PillSettlement Decide(PillUseRecord r,int rawGrade,int dynamicGrade,int count,bool ownsReservedSole)
        {
            if(r==null || !r.Submitted || r.BeforeCount<1 || r.FromGrade<1 || r.ToGrade<=r.FromGrade) return PillSettlement.Hold;
            if(r.Stage==PillStage.Committed || r.Stage==PillStage.Released) return PillSettlement.Closed;
            if(r.Stage==PillStage.Uncertain || rawGrade!=dynamicGrade) return PillSettlement.Hold;
            if(rawGrade==r.ToGrade)
            {
                if(r.Stage==PillStage.RefundIntent) return PillSettlement.Hold;
                if(count==r.BeforeCount-1) return PillSettlement.Confirm;
                if(count==r.BeforeCount && ownsReservedSole) return PillSettlement.Commit;
            }
            if(rawGrade==r.FromGrade)
            {
                if(count==r.BeforeCount) return PillSettlement.Keep;
                if(count==r.BeforeCount-1 && r.Values!=null && r.Values.Length>0) return PillSettlement.Restore;
            }
            return PillSettlement.Hold;
        }
    }
}
