using System;
using System.Collections.Generic;
using System.Linq;

namespace NpcMonthActivity
{
    public sealed class TransitLink
    {
        public Point Entrance, Exit;
        public string SourceId = "", Label = "阵法";
    }

    public sealed class PortalRecord
    {
        public string Id = "", OpenUi = "";
        public int TerrainType;
        public Point FirstPoint;
        public string[] Values = new string[0];
    }

    public interface ITransitWorld
    {
        bool HasReadyTransfer(string id, Point origin, Point target);
        bool Transfer(string id, Point origin, Point target, out Point exit, out string detail);
    }

    public static class TransitPolicy
    {
        public static bool BuildExit(PortalRecord source, IEnumerable<PortalRecord> buildings, out Point exit, out string reason)
        {
            exit = default(Point); reason = "原生阵法出口数据无效";
            int x, y;
            if (source == null || source.OpenUi != "Build10004" || string.IsNullOrEmpty(source.Id) || source.Values == null ||
                source.Values.Length < 2 || !int.TryParse(source.Values[0], out x) || !int.TryParse(source.Values[1], out y)) return false;
            exit = new Point(x, y);
            // Native UIBuild10004 uses a same-terrain, different-building peer with the same fourth value.
            if (source.Values.Length > 3)
            {
                var peers = buildings.Where(b=>b.Id != source.Id && b.TerrainType == source.TerrainType &&
                    b.Values != null && b.Values.Length > 3 && b.Values[3] == source.Values[3]).ToList();
                if (peers.Count > 1) { reason = "阵法配对出口不唯一"; return false; }
                if (peers.Count == 1) exit = peers[0].FirstPoint;
            }
            if (exit.Equals(source.FirstPoint)) { reason = "阵法出口与入口重合"; return false; }
            reason = ""; return true;
        }
        // Functions use '_' arguments and '|' command separators. Never execute arbitrary scripts.
        public static List<string> Commands(string functions)
        {
            return (functions ?? "").Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }
        public static bool CommandId(string command, string name, out int id)
        {
            id = 0;
            var parts = command.Split('_');
            return parts.Length == 2 && string.Equals(parts[0], name, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(parts[1], out id) && id > 0;
        }
        public static List<int> Ids(string text)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (string part in text.Split('|'))
            {
                int id;
                if (!int.TryParse(part, out id) || id <= 0) return new List<int>();
                if (!result.Contains(id)) result.Add(id);
            }
            return result;
        }
        public static bool FixedPoint(string text, out Point point)
        {
            point = default(Point);
            var parts = (text ?? "").Split('_'); int x, y;
            if (parts.Length != 2 || !int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y)) return false;
            point = new Point(x, y); return true;
        }
        public static bool IsPortalName(string name)
        {
            return !string.IsNullOrEmpty(name) && (name.Contains("传送") || name.Contains("傳送") || name.Contains("阵法") || name.Contains("法阵"));
        }
        public static TransitLink Match(IEnumerable<TransitLink> links, Point entrance, Point exit)
        { return links.FirstOrDefault(l => l.Entrance.Equals(entrance) && l.Exit.Equals(exit)); }
    }
}
