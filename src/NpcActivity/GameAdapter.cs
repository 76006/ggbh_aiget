using System;
using System.Collections.Generic;
using UnityEngine;

namespace NpcMonthActivity
{
    internal sealed partial class GameAdapter : IActivityWorld, IPlannedWorld, IActivityAvailability
    {
        internal static Point Read(Vector2Int p) { return new Point(p.x, p.y); }
        internal static Vector2Int Write(Point p) { return new Vector2Int(p.X, p.Y); }
        internal static WorldUnitBase Unit(string id) { return g.world.unit.GetUnit(id, false); }
        internal static bool OwnMove;
        private bool dirtyMap;
        private sealed class Route
        {
            public Point Origin, Target;
            public Queue<Point> Steps;
        }
        private readonly Dictionary<string, Route> routes = new Dictionary<string, Route>();

        private static LocalPaths Search(Point origin, int radius)
        {
            int area = g.data.grid.GetGridData(Write(origin)).areaBaseID;
            return LocalPaths.Search(origin, radius, p =>
            {
                var native = Write(p);
                if (!g.data.grid.IsSafe(native)) return false;
                var grid = g.data.grid.GetGridData(native);
                return grid != null && !grid.isBlock && grid.areaBaseID == area;
            });
        }

        public bool TryPosition(string id, out Point point, out string pauseReason)
        {
            point = default(Point);
            var unit = Unit(id);
            pauseReason = "人物不存在";
            if (unit == null) return false;
            pauseReason = "人物已死亡";
            if (unit.isDie) return false;
            pauseReason = "人物处于隐藏或特殊状态";
            if (unit.data.dynUnitData.isHide != 0) return false;
            pauseReason = "正在月结";
            if (g.world.run.isRunning) return false;
            pauseReason = "正在战斗或切换场景";
            if (g.world.battle.isBattle || g.scene.isLoadScene) return false;
            point = Read(unit.data.unitData.GetPoint());
            pauseReason = "人物不在有效地图格";
            if (!g.data.grid.IsSafe(Write(point))) return false;
            var grid = g.data.grid.GetGridData(Write(point));
            if (grid == null || grid.isBlock) return false;
            pauseReason = "";
            return true;
        }

        public bool TryTarget(string id, Point origin, int decision, out Point target)
        {
            target = origin;
            // Stable across process restarts; string.GetHashCode is intentionally not used.
            int seed = decision;
            foreach (char c in id) seed = unchecked(seed * 31 + c);
            var random = new System.Random(seed & int.MaxValue);
            var paths = Search(origin, 6);
            if (paths.Destinations.Count == 0) return false;
            target = paths.Destinations[random.Next(paths.Destinations.Count)];
            routes[id] = new Route { Origin = origin, Target = target, Steps = new Queue<Point>(paths.PathTo(target)) };
            return true;
        }

        public bool TryStep(string id, Point origin, Point target, out Point next, out double cost)
        {
            next = origin; cost = 0;
            Route route;
            if (!routes.TryGetValue(id, out route) || !route.Origin.Equals(origin) || !route.Target.Equals(target))
            {
                var path = FindRoute(id, origin, target);
                if (path.Count == 0) return false;
                route = new Route { Origin = origin, Target = target, Steps = new Queue<Point>(path) };
                routes[id] = route;
            }
            if (route.Steps.Count == 0) return false;
            next = route.Steps.Peek();
            if (Math.Abs(next.X - origin.X) + Math.Abs(next.Y - origin.Y) != 1) return false;
            var grid = g.data.grid.GetGridData(Write(next));
            if (grid == null || grid.isBlock || !CanEnter(id, next)) { routes.Remove(id); return false; }
            var unit = Unit(id);
            var action = new UnitActionMoveNPC(Write(next));
            action.Init(unit);
            // Prefer the game's initialized action cost; retain fractional terrain cost for zero-day steps.
            cost = action.day > 0 ? action.day : MapTool.CalMoveDay(Math.Max(1, unit.data.dynUnitData.footSpeed.value), Write(next));
            return cost > 0 && !double.IsInfinity(cost) && !double.IsNaN(cost);
        }

        public bool Move(string id, Point origin, Point next)
        {
            var unit = Unit(id);
            if (unit == null || unit.isDie || !Read(unit.data.unitData.GetPoint()).Equals(origin)) return false;
            try
            {
                OwnMove = true;
                int result = unit.CreateAction(new UnitActionMoveNPC(Write(next)), false);
                var actual = Read(unit.data.unitData.GetPoint());
                if (!actual.Equals(next))
                {
                    Trace(id + " move-rejected native-result=" + result + " actual=" + actual +
                        " residueDay=" + unit.data.unitData.residueDay + " energy=" + unit.data.dynUnitData.energy.value);
                    return false;
                }
                dirtyMap = true;
                Route route;
                if (routes.TryGetValue(id, out route) && route.Origin.Equals(origin) && route.Steps.Count > 0 && route.Steps.Peek().Equals(next))
                {
                    route.Steps.Dequeue();
                    route.Origin = next;
                }
                return true;
            }
            finally { OwnMove = false; }
        }

        internal void RefreshMap()
        {
            if (!dirtyMap) return;
            dirtyMap = false;
            var map = g.ui.GetUI<UIMapMain>(UIType.MapMain);
            if (map != null)
            {
                map.uiGridUnit.UpdateUI(g.world.playerUnit.data.unitData.GetPoint());
                map.UpdateOpgroupUnitList();
            }
            var scene = g.scene.GetScene<SceneMap>(SceneType.Map);
            if (scene != null) scene.world.UpdateGridUI();
        }
        public void Trace(string message) { ModMain.Log(message); }
    }
}
