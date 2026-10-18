using System;
using System.Collections.Generic;

namespace NpcMonthActivity
{
    // Entirely managed graph search: no native generic collections or callback types cross IL2CPP.
    public sealed class LocalPaths
    {
        private readonly Point origin;
        private readonly Dictionary<Point, Point> parents = new Dictionary<Point, Point>();
        public readonly List<Point> Destinations = new List<Point>();
        private LocalPaths(Point origin) { this.origin = origin; parents[origin] = origin; }

        public static LocalPaths Search(Point origin, int radius, Func<Point, bool> canEnter)
        {
            if (radius < 0 || radius > 12) throw new ArgumentOutOfRangeException(nameof(radius));
            var paths = new LocalPaths(origin);
            var queue = new Queue<Point>();
            queue.Enqueue(origin);
            var offsets = new[] { new Point(1, 0), new Point(0, 1), new Point(-1, 0), new Point(0, -1) };
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var delta in offsets)
                {
                    var next = new Point(p.X + delta.X, p.Y + delta.Y);
                    if (Math.Abs(next.X - origin.X) > radius || Math.Abs(next.Y - origin.Y) > radius ||
                        paths.parents.ContainsKey(next) || !canEnter(next)) continue;
                    paths.parents.Add(next, p);
                    paths.Destinations.Add(next);
                    queue.Enqueue(next);
                }
            }
            return paths;
        }

        public List<Point> PathTo(Point target)
        {
            var result = new List<Point>();
            if (!parents.ContainsKey(target)) return result;
            for (var p = target; !p.Equals(origin); p = parents[p]) result.Add(p);
            result.Reverse();
            return result;
        }
    }
}
