using System;
using System.Collections.Generic;
using System.Linq;

namespace NpcMonthActivity
{
    internal sealed partial class GameAdapter : ITransitWorld
    {
        private readonly List<TransitLink> transitLinks = new List<TransitLink>();
        private DateTime nextTransitScan;
        private readonly HashSet<string> transitDiagnostics = new HashSet<string>();

        private List<Point> FindRoute(string id, Point origin, Point target)
        {
            RefreshTransitLinks();
            return WorldPaths.Find(origin, target, EntryPolicy(id), transitLinks);
        }

        private void TransitTrace(string text)
        { if (transitDiagnostics.Count < 512 && transitDiagnostics.Add(text)) Trace(text); }

        private void RefreshTransitLinks()
        {
            if (DateTime.UtcNow < nextTransitScan) return;
            nextTransitScan = DateTime.UtcNow.AddSeconds(15);
            transitLinks.Clear();
            try
            {
                ReadNativePortalBuildings();
                var events = g.world.mapEvent.GetAllGridEvent();
                int nearbyLogged = 0;
                var actors = ModMain.Instance?.Engine?.State.Npcs.Keys.Take(4).Select(Unit).Where(u=>u!=null)
                    .Select(u=>Read(u.data.unitData.GetPoint())).ToList() ?? new List<Point>();
                for (int i = 0; i < events.Count; i++)
                {
                    var ev = events[i];
                    if (ev == null || ev.eventData == null || ev.eventBaseItem == null || ev.isHide) continue;
                    var conf = ev.eventBaseItem;
                    string label = ev.name;
                    if (!TransitPolicy.IsPortalName(label) && !TransitPolicy.IsPortalName(conf.name))
                    {
                        if (nearbyLogged < 24 && actors.Any(p=>Distance(p,Read(ev.GetPoint()))<=20))
                        {
                            nearbyLogged++;
                            TransitTrace("transit-nearby conf=" + conf.id + " name=" + label + " point=" + Read(ev.GetPoint()) +
                                " drama=" + conf.dramaID + " function=" + conf.function + " moveOn=" + conf.moveOnFunction);
                        }
                        continue;
                    }
                    string source = ev.eventData.soleID;
                    if (string.IsNullOrEmpty(source)) continue;
                    var commands = new List<string>();
                    commands.AddRange(TransitPolicy.Commands(conf.function));
                    commands.AddRange(TransitPolicy.Commands(conf.moveOnFunction));
                    bool complete = ReadTransitDialogues(conf.dramaID, commands);
                    var exits = new HashSet<Point>();
                    bool unresolved = !complete, foundCommand = false;
                    foreach (var command in commands.Distinct())
                    {
                        int argument;
                        if (TransitPolicy.CommandId(command, "setPoint", out argument))
                        {
                            foundCommand = true;
                            var points = ResolveTransitPosition(argument);
                            if (points.Count == 0) unresolved = true;
                            foreach (var p in points) exits.Add(p);
                        }
                        else if (TransitPolicy.CommandId(command, "setPointInDecorateClassName", out argument))
                        {
                            foundCommand = true;
                            var points = ResolveDecorateExit(argument);
                            if (points.Count == 0) unresolved = true;
                            foreach (var p in points) exits.Add(p);
                        }
                    }
                    TransitTrace("transit-event id=" + source + " conf=" + conf.id + " name=" + label +
                        " origin=" + Read(ev.GetPoint()) + " drama=" + conf.dramaID + " commands=" +
                        string.Join("|", commands.Distinct().Take(32)) + " exits=" + string.Join(",", exits) + " unresolved=" + unresolved);
                    // A portal with several possible scripted destinations needs its branch semantics verified first.
                    if (!foundCommand || unresolved || exits.Count != 1) continue;
                    Point exit = exits.Single();
                    var entrances = new HashSet<Point> { Read(ev.GetPoint()) };
                    var dramaPoints = ev.dramaPoints;
                    if (dramaPoints != null) for (int p = 0; p < dramaPoints.Count; p++) entrances.Add(Read(dramaPoints[p]));
                    foreach (var entrance in entrances.Take(16))
                    {
                        if (entrance.Equals(exit) || !ValidTransitTile(entrance) || !ValidTransitTile(exit)) continue;
                        transitLinks.Add(new TransitLink { Entrance = entrance, Exit = exit, SourceId = source, Label = string.IsNullOrEmpty(label) ? "阵法" : label });
                        TransitTrace("transit-link source=" + source + " entrance=" + entrance + " exit=" + exit + " fee=0 days=0");
                    }
                }
                TransitTrace("transit-scan events=" + events.Count + " links=" + transitLinks.Count);
            }
            catch (Exception ex)
            {
                transitLinks.Clear();
                TransitTrace("transit-scan-failed " + ex);
            }
        }

        private void ReadNativePortalBuildings()
        {
            var native = g.world.build.GetBuilds();
            var records = new List<PortalRecord>();
            var sources = new Dictionary<string, MapBuildBase>();
            for (int i=0;i<native.Count;i++)
            {
                var build = native[i];
                if (build?.buildData == null || build.gridData == null || build.points == null || build.points.Length == 0) continue;
                var values = build.buildData.values;
                var data = new List<string>();
                if (values != null) for (int v=0;v<values.Length;v++) data.Add(values[v]);
                var common = build.TryCast<MapBuildCommonBase>();
                string ui = common?.buildingBaseItem?.openUI ?? "";
                var record = new PortalRecord { Id=build.buildData.id, OpenUi=ui, Values=data.ToArray(),
                    TerrainType=build.gridData.typeInt, FirstPoint=Read(build.points[0]) };
                records.Add(record);
                if (ui=="Build10004" && build.buildData.disableOpenBuild==0 && build.buildData.disableOpenBuildCount==0)
                    sources[record.Id] = build;
            }
            foreach (var source in records.Where(r=>sources.ContainsKey(r.Id)))
            {
                var build = sources[source.Id]; Point exit; string reason;
                bool resolved = TransitPolicy.BuildExit(source,records,out exit,out reason);
                TransitTrace("transit-building id=" + source.Id + " name=" + build.name + " entrance=" + source.FirstPoint +
                    " ui=" + source.OpenUi + " values=" + string.Join("|",source.Values.Take(8)) + " exit=" + exit + " resolved=" + resolved + " reason=" + reason);
                if (!resolved || !ValidTransitTile(exit)) continue;
                var entrances = new HashSet<Point>();
                var opens = build.GetOpenBuildPoints();
                if (opens != null) for (int i=0;i<opens.Length;i++) entrances.Add(Read(opens[i]));
                if (entrances.Count==0) entrances.Add(source.FirstPoint);
                foreach (var entrance in entrances.Take(16))
                {
                    if (entrance.Equals(exit) || !ValidTransitTile(entrance)) continue;
                    transitLinks.Add(new TransitLink { Entrance=entrance, Exit=exit, SourceId="build:"+source.Id,
                        Label=string.IsNullOrEmpty(build.name) ? "阵法" : build.name });
                    TransitTrace("transit-link source=build:" + source.Id + " entrance=" + entrance + " exit=" + exit + " fee=0 days=0");
                }
            }
        }

        // Only read dialogue/configuration. No player commands, callbacks, rewards or flags are run.
        private bool ReadTransitDialogues(string roots, List<string> commands)
        {
            var queue = new Queue<int>(TransitPolicy.Ids(roots));
            var seen = new HashSet<int>();
            while (queue.Count > 0 && seen.Count < 64)
            {
                int id = queue.Dequeue();
                if (!seen.Add(id)) continue;
                var dialogue = g.conf.dramaDialogue.GetItem(id);
                if (dialogue == null) return false;
                commands.AddRange(TransitPolicy.Commands(dialogue.openFunction));
                commands.AddRange(TransitPolicy.Commands(dialogue.function));
                foreach (int next in TransitPolicy.Ids(dialogue.nextDialogue)) queue.Enqueue(next);
                foreach (int optionId in TransitPolicy.Ids(dialogue.options))
                {
                    var option = g.conf.dramaOptions.GetItem(optionId);
                    if (option == null) return false;
                    commands.AddRange(TransitPolicy.Commands(option.function));
                    foreach (int next in TransitPolicy.Ids(option.nextDialogue)) queue.Enqueue(next);
                }
                // CallDrama is a pure reference here, never an instruction to execute.
                foreach (var command in commands.ToArray())
                { int next; if (TransitPolicy.CommandId(command, "callDrama", out next) && !seen.Contains(next)) queue.Enqueue(next); }
            }
            return !queue.Any(id => !seen.Contains(id));
        }

        private List<Point> ResolveTransitPosition(int id)
        {
            var points = new List<Point>();
            // 101 is a special runtime/player-relative position in the native resolver, not a fixed exit.
            if (id == 101) return points;
            var position = g.conf.mapPosition.GetItem(id);
            if (position == null) return points;
            TransitTrace("transit-position id=" + id + " type=" + position.posiType + " pos=" + position.posiID +
                " area=" + position.areaID + " condition=" + position.condition + " index=" + position.buildingIndex);
            if (!string.IsNullOrEmpty(position.condition) && position.condition != "0") return points;
            Point fixedPoint;
            // Native MovePositionTool.GetPoint case 3 reads exactly x_y.
            if (position.posiType == 3 && TransitPolicy.FixedPoint(position.posiID, out fixedPoint)) points.Add(fixedPoint);
            // Case 2 selects an existing event, case 1 an existing building. Only unique matches are accepted.
            else if (position.posiType == 2)
            {
                foreach (int eventId in TransitPolicy.Ids(position.posiID))
                {
                    var events = g.world.mapEvent.GetGridEvent(eventId);
                    if (events != null) for (int i = 0; i < events.Count; i++)
                    {
                        var p = Read(events[i].GetPoint());
                        if (TransitAreaMatches(position.areaID, p)) points.Add(p);
                    }
                }
            }
            else if (position.posiType == 1)
            {
                int buildId;
                if (int.TryParse(position.posiID, out buildId))
                {
                    var builds = g.world.build.GetBuilds(buildId);
                    var matches = new List<MapBuildBase>();
                    if (builds != null) for (int i = 0; i < builds.Count; i++)
                        if (TransitAreaMatches(position.areaID, Read(builds[i].GetOrigiPoint()))) matches.Add(builds[i]);
                    if (matches.Count == 1)
                    {
                        var open = matches[0].GetOpenBuildPoints();
                        if (open != null && open.Length > 0) points.Add(Read(open[0]));
                    }
                }
            }
            return points.Distinct().ToList();
        }

        private static bool TransitAreaMatches(string areas, Point p)
        {
            if (!ValidTransitTile(p)) return false;
            if (string.IsNullOrEmpty(areas) || areas == "0") return true;
            return TransitPolicy.Ids(areas).Contains(g.data.grid.GetGridData(Write(p)).areaBaseID);
        }

        private List<Point> ResolveDecorateExit(int className)
        {
            var points = new List<Point>();
            // Same real grid/decorate relation used by native SetPointInDecorateClassName; do not create coordinates.
            foreach (var pair in g.data.grid.allGrid)
            {
                var decorate = pair.Value.GetDecorateItem();
                if (decorate == null || decorate.className == null) continue;
                for (int i = 0; i < decorate.className.Length; i++)
                    if (decorate.className[i] == className)
                    { points.Add(Read(GameTool.IntToPoint(pair.Key))); break; }
                if (points.Count > 1) break; // Multiple native random destinations remain unresolved in this build.
            }
            return points;
        }

        private static bool ValidTransitTile(Point p)
        {
            if (!g.data.grid.IsSafe(Write(p))) return false;
            var grid = g.data.grid.GetGridData(Write(p));
            return grid != null && !grid.isBlock;
        }

        public bool HasReadyTransfer(string id, Point origin, Point target)
        {
            Route route;
            return routes.TryGetValue(id, out route) && route.Origin.Equals(origin) && route.Target.Equals(target)
                && route.Steps.Count > 0 && TransitPolicy.Match(transitLinks, origin, route.Steps.Peek()) != null;
        }

        public bool Transfer(string id, Point origin, Point target, out Point exit, out string detail)
        {
            exit = origin; detail = "阵法或出口已失效，重新寻路";
            if (!HasReadyTransfer(id, origin, target)) return false;
            var expected = routes[id].Steps.Peek();
            // Re-read the real event/configuration immediately before relocation.
            nextTransitScan = DateTime.MinValue;
            RefreshTransitLinks();
            var link = TransitPolicy.Match(transitLinks, origin, expected);
            if (link == null) return false;
            var unit = Unit(id);
            if (ModMain.Instance?.Engine == null || !ModMain.Instance.Engine.Enabled(id) ||
                unit == null || unit.isDie || unit.Pointer == g.world.playerUnit.Pointer ||
                !Read(unit.data.unitData.GetPoint()).Equals(origin) || !CanEnter(id, expected) ||
                NativeMissions.HasPending(id) || NativeActions.HasPending(id)) return false;
            try
            {
                OwnMove = true;
                unit.data.unitData.SetPoint(Write(expected));
                exit = Read(unit.data.unitData.GetPoint());
                if (!exit.Equals(expected)) return false;
                var route = routes[id]; route.Steps.Dequeue(); route.Origin = exit;
                dirtyMap = true;
                detail = "通过" + link.Label + "抵达出口，继续前往目的地";
                Trace(id + " transit-complete source=" + link.SourceId + " entrance=" + origin + " exit=" + exit + " goal=" + target);
                return true;
            }
            finally { OwnMove = false; }
        }
    }
}
