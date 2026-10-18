using System.Collections.Generic;

namespace NpcMonthActivity
{
    public static class CultivationPolicy
    {
        public static bool NoGainAtCurrentState(NpcState npc,int gradeId,int exp)
        { return npc.NoGainGradeId==gradeId && npc.NoGainExp==exp; }
        public static void RecordNoGain(NpcState npc,int gradeId,int exp)
        { npc.NoGainGradeId=gradeId; npc.NoGainExp=exp; }
        public static void ClearNoGain(NpcState npc) { npc.NoGainGradeId=-1; npc.NoGainExp=-1; }
        public static bool CanCultivate(bool phaseStop,bool majorStop,bool sameStateNoGain)
        { return !phaseStop && !majorStop && !sameStateNoGain; }
        public static bool AtConfiguredCap(int currentThreshold,int nextThreshold,int rawExp,int dynamicExp)
        { return currentThreshold>=0 && nextThreshold>currentThreshold && rawExp>=nextThreshold && dynamicExp>=nextThreshold; }
        public static bool ShouldPreparePill(int phase,int candidateCount,bool ownsAnyCandidate)
        { return phase>=1 && phase<3 && candidateCount>0 && !ownsAnyCandidate; }
        public static bool PreferCultivation(bool ownsPhasePill,int money,bool blocked)
        { return ownsPhasePill && money>=500 && !blocked; }
        public static int ActivityDays(bool npcCultivation,int nativeDays)
        { return npcCultivation ? 1 : System.Math.Max(1,nativeDays); }
        public static bool ObsoletePhaseLimit(int currentMajor,int currentPhase,int entryMajor,int entryPhase,
            int entryExp,int rawExp,int dynamicExp,int limitMajor,int limitPhase)
        {
            return currentMajor>0 && currentMajor==entryMajor && currentPhase>=entryPhase && currentPhase<=3 &&
                entryPhase>=2 && entryPhase<=3 && entryExp>0 && rawExp>=entryExp && dynamicExp>=entryExp &&
                limitMajor==entryMajor && limitPhase==entryPhase-1;
        }
        public static bool PhasePillSucceeded(int beforeCount,int afterCount,int expectedGrade,int actualGrade)
        { return beforeCount>0 && afterCount==beforeCount-1 && expectedGrade>0 && actualGrade==expectedGrade; }
        public static List<int> MaterialIds(string list)
        {
            var result=new List<int>();
            if(string.IsNullOrWhiteSpace(list)) return result;
            foreach(string token in list.Split('_'))
            {
                int id;
                if(int.TryParse(token,out id) && id>0 && !result.Contains(id)) result.Add(id);
            }
            return result;
        }
    }
}
