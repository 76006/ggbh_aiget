using System;
using System.Collections.Generic;

namespace NpcMonthActivity
{
    public static class WorldPaths
    {
        // A* with a binary heap, a deterministic work limit, and managed values only.
        public static List<Point> Find(Point start, Point target, Func<Point, bool> canEnter, int maxNodes = 12000)
        { return Find(start, target, canEnter, new TransitLink[0], maxNodes); }

        public static List<Point> Find(Point start, Point target, Func<Point, bool> canEnter, IEnumerable<TransitLink> transfers, int maxNodes = 12000)
        {
            var empty = new List<Point>();
            if (start.Equals(target)) return empty;
            if (!canEnter(target)) return empty;
            var open = new Heap();
            var parents = new Dictionary<Point, Point>();
            var costs = new Dictionary<Point, int> { [start] = 0 };
            var closed = new HashSet<Point>();
            var offsets = new[] { new Point(1,0), new Point(0,1), new Point(-1,0), new Point(0,-1) };
            var links = new Dictionary<Point, List<Point>>();
            foreach (var link in transfers)
            {
                if (link.Entrance.Equals(link.Exit)) continue;
                List<Point> exits;
                if (!links.TryGetValue(link.Entrance, out exits)) links[link.Entrance] = exits = new List<Point>();
                if (!exits.Contains(link.Exit)) exits.Add(link.Exit);
            }
            // Manhattan is not admissible across a portal. Dijkstra preserves correct route selection.
            bool hasTransfers = links.Count > 0;
            open.Add(start, hasTransfers ? 0 : Distance(start, target));
            while (open.Count > 0 && closed.Count < maxNodes)
            {
                var current = open.Pop();
                if (!closed.Add(current)) continue;
                if (current.Equals(target))
                {
                    var path = new List<Point>();
                    for (var p = target; !p.Equals(start); p = parents[p]) path.Add(p);
                    path.Reverse(); return path;
                }
                var neighbors = new List<Point>();
                foreach (var d in offsets) neighbors.Add(new Point(current.X + d.X, current.Y + d.Y));
                List<Point> portals;
                if (links.TryGetValue(current, out portals)) neighbors.AddRange(portals);
                foreach (var next in neighbors)
                {
                    if (closed.Contains(next) || !canEnter(next)) continue;
                    int score = costs[current] + (portals != null && portals.Contains(next) ? 0 : 1), old;
                    if (costs.TryGetValue(next, out old) && old <= score) continue;
                    costs[next] = score; parents[next] = current;
                    open.Add(next, score + (hasTransfers ? 0 : Distance(next, target)));
                }
            }
            return empty;
        }
        private static int Distance(Point a, Point b) { return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y); }
        private sealed class Heap
        {
            private struct Node { public Point Point; public int Score; }
            private readonly List<Node> items = new List<Node>();
            public int Count { get { return items.Count; } }
            public void Add(Point p, int score)
            {
                int i = items.Count;
                items.Add(new Node { Point = p, Score = score });
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (items[parent].Score <= items[i].Score) break;
                    var temp = items[i]; items[i] = items[parent]; items[parent] = temp; i = parent;
                }
            }
            public Point Pop()
            {
                var result = items[0].Point;
                int last = items.Count - 1;
                items[0] = items[last]; items.RemoveAt(last);
                for (int i = 0; i < items.Count;)
                {
                    int child = i * 2 + 1;
                    if (child >= items.Count) break;
                    if (child + 1 < items.Count && items[child + 1].Score < items[child].Score) child++;
                    if (items[i].Score <= items[child].Score) break;
                    var temp = items[i]; items[i] = items[child]; items[child] = temp; i = child;
                }
                return result;
            }
        }
    }
}
