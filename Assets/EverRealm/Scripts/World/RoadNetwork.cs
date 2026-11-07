using System.Collections.Generic;
using UnityEngine;

namespace EverRealm.World
{
    /// <summary>
    /// The island's roads as a graph of points, so anyone can ask the way from wherever they stand. Roads that
    /// share ground share points, which is how they join. Holds no terrain and knows nothing of who walks it.
    /// </summary>
    public sealed class RoadNetwork
    {
        readonly List<Vector3> points = new List<Vector3>();
        readonly List<List<int>> links = new List<List<int>>();
        readonly Dictionary<Vector2Int, int> pointsByCell = new Dictionary<Vector2Int, int>();
        readonly List<IReadOnlyList<Vector3>> roads = new List<IReadOnlyList<Vector3>>();

        /// <summary>Every road's centre line, end to end.</summary>
        public IReadOnlyList<IReadOnlyList<Vector3>> Roads => roads;

        /// <summary>Adds a road through <paramref name="road"/>; <paramref name="cells"/> identify each point, so crossings join up.</summary>
        public void Add(IReadOnlyList<Vector3> road, IReadOnlyList<Vector2Int> cells)
        {
            roads.Add(road);
            int previous = -1;
            for (int i = 0; i < road.Count; i++)
            {
                if (!pointsByCell.TryGetValue(cells[i], out int point))
                {
                    point = points.Count;
                    points.Add(road[i]);
                    links.Add(new List<int>());
                    pointsByCell[cells[i]] = point;
                }
                if (previous >= 0 && previous != point)
                {
                    links[previous].Add(point);
                    links[point].Add(previous);
                }
                previous = point;
            }
        }

        /// <summary>
        /// The shortest way by road between the road points nearest <paramref name="from"/> and
        /// <paramref name="to"/>. False if there are no roads or they don't connect.
        /// </summary>
        public bool TryFindRoute(Vector3 from, Vector3 to, out List<Vector3> route, out float length)
        {
            route = null;
            length = float.PositiveInfinity;
            if (points.Count == 0) return false;
            int start = Nearest(from), goal = Nearest(to);

            var distance = new float[points.Count];
            var previous = new int[points.Count];
            for (int i = 0; i < distance.Length; i++)
            {
                distance[i] = float.PositiveInfinity;
                previous[i] = -1;
            }
            distance[start] = 0f;
            var open = new SortedSet<(float distance, int point)> { (0f, start) };
            while (open.Count > 0)
            {
                (float _, int current) = open.Min;
                open.Remove(open.Min);
                if (current == goal) break;
                foreach (int next in links[current])
                {
                    float total = distance[current] + FlatDistance(points[current], points[next]);
                    if (total >= distance[next]) continue;
                    distance[next] = total;
                    previous[next] = current;
                    open.Add((total, next));
                }
            }
            if (float.IsPositiveInfinity(distance[goal])) return false;

            route = new List<Vector3>();
            for (int point = goal; point >= 0; point = previous[point]) route.Add(points[point]);
            route.Reverse();
            length = distance[goal];
            return true;
        }

        int Nearest(Vector3 position)
        {
            int best = 0;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                float distance = FlatDistance(points[i], position);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = i;
            }
            return best;
        }

        static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }
}
