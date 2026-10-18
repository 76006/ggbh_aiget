using System;
using System.Collections.Generic;

namespace NpcMonthActivity
{
    public static class GrowthMaterialPolicy
    {
        public static List<string> OwnedSelection(IEnumerable<string> slots,Func<string,int> ownedCount,IDictionary<string,int> reserved)
        {
            var result=new List<string>();
            if(slots==null) return result;
            foreach(string sole in slots)
            {
                if(string.IsNullOrWhiteSpace(sole)) continue;
                int used; if(!reserved.TryGetValue(sole,out used)) used=0;
                int count=ownedCount(sole);
                if(count<=0 || used>=count) continue;
                result.Add(sole); reserved[sole]=used+1;
            }
            return result;
        }
    }
}
