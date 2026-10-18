using System.Collections.Generic;
using System.Linq;
using UnhollowerBaseLib;

namespace NpcMonthActivity
{
    internal sealed partial class GameAdapter
    {
        private static readonly Dictionary<string,string> growthMaterialDiagnostics=new Dictionary<string,string>();
        private static Il2CppStringArray OwnedGrowthMaterials(WorldUnitBase unit,Il2CppStringArray slots,Dictionary<string,int> reserved)
        {
            var source=new List<string>();
            if(slots!=null) foreach(string sole in slots) source.Add(sole);
            var valid=GrowthMaterialPolicy.OwnedSelection(source,sole=>unit.data.unitData.propData.GetProps(sole)?.propsCount??0,reserved);
            var result=new Il2CppStringArray(valid.Count);
            for(int i=0;i<valid.Count;i++) result[i]=valid[i];
            return result;
        }
        private static string MaterialSlots(Il2CppStringArray slots)
        { return slots==null ? "null" : string.Join(",",slots.Select(s=>string.IsNullOrWhiteSpace(s) ? "<empty>" : s)); }
        private static List<int> GrowthRarityCandidates(ConfRoleGradeItem target)
        {
            var result=new List<int>();
            var map=g.conf.roleGrade.rarityPropsIDs;
            if(target==null || map==null || !map.ContainsKey(target.grade)) return result;
            var items=map[target.grade];
            if(items!=null) for(int i=0;i<items.Count;i++)
            {
                int id=items[i].id;
                if(g.conf.itemProps.GetItem(id)!=null && !result.Contains(id)) result.Add(id);
            }
            return result;
        }
        private static void LogGrowthMaterials(WorldUnitBase unit,ConfRoleGradeItem target,Il2CppStringArray rawRequired,
            Il2CppStringArray rawRarity,Il2CppStringArray required,Il2CppStringArray rarity,int allowed)
        {
            string id=unit.data.unitData.unitID;
            string signature=target.id+":"+MaterialSlots(rawRequired)+":"+MaterialSlots(rawRarity)+":"+
                MaterialSlots(required)+":"+MaterialSlots(rarity)+":"+allowed;
            string previous; if(growthMaterialDiagnostics.TryGetValue(id,out previous) && previous==signature) return;
            growthMaterialDiagnostics[id]=signature;
            ModMain.Log(id+" growth-material-selection name="+unit.data.unitData.propertyData.GetName()+" target="+target.id+
                " targetMajor="+target.grade+" rawRequired=["+MaterialSlots(rawRequired)+"] rawRarity=["+MaterialSlots(rawRarity)+"]"+
                " ownedRequired="+required.Length+"/"+target.growItemNum+" ownedRarity="+rarity.Length+"/"+target.materialsNum+" allowed="+allowed);
        }
    }
}
