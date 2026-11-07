using System.Collections.Generic;
using UnityEngine;

namespace EverRealm.World
{
    /// <summary>
    /// Lays the island's roads: every hamlet to every other, and the coastal landing to the nearest hamlet.
    /// Each road is found on a coarse grid that avoids water, huts and steep ground, graded so no step is
    /// taller than a block, and stamped into the terrain. Deterministic from the world it's given.
    /// </summary>
    public static class RoadBuilder
    {
        const int CellBlocks = 4;
        /// <summary>Blocks either side of a road's centre line, and the blocks of slope blending it into the terrain.</summary>
        const int HalfWidth = 2, ShoulderBlocks = 4;
        /// <summary>Most blocks a road climbs per block travelled.</summary>
        public const float MaxGrade = 1f / 3f;
        const float HutMargin = 2.5f, HutWall = 0.7f, CrowdedPenalty = 4f, ClimbPenalty = 0.5f, SharedRoadDiscount = 0.4f;
        const int SmoothingCells = 4;
        // The landing: its plank pad's half-width in blocks, how far from any hamlet it must be, in metres.
        const int LandingPadBlocks = 3;
        const float MinLandingDistance = 120f, LandingSearchStep = 4f;

        /// <summary>A flat place a road must meet at its own height: a hamlet's pad, or the landing.</summary>
        readonly struct Anchor
        {
            public readonly Vector2 Centre;
            public readonly float Radius;
            public readonly int Top;

            public Anchor(Vector2 centre, float radius, int top)
            {
                Centre = centre;
                Radius = radius;
                Top = top;
            }
        }

        /// <summary>Builds the road network into <paramref name="world"/>; <paramref name="landing"/> is where newcomers come ashore.</summary>
        public static RoadNetwork Build(WorldData world, WorldSettings settings, IReadOnlyList<Settlement> settlements, out Vector3? landing)
        {
            var network = new RoadNetwork();
            landing = null;
            if (settlements.Count == 0) return network;

            int[] tops = SettlementBuilder.TopBlocks(world);
            float blockSize = world.BlockSize;
            var anchors = new List<Anchor>();
            foreach (Settlement settlement in settlements)
                anchors.Add(new Anchor(Flat(settlement.Square), settlement.Radius, Mathf.RoundToInt(settlement.Square.y / blockSize) - 1));

            if (TryFindLanding(world, settings, tops, settlements, out Vector2 shore))
            {
                int shoreTop = Mathf.Max(tops[Column(world, shore)], Mathf.CeilToInt(settings.seaLevel / blockSize));
                StampLanding(world, settings, tops, shore, shoreTop);
                landing = new Vector3(shore.x, (shoreTop + 1) * blockSize, shore.y);
                anchors.Add(new Anchor(shore, LandingPadBlocks * blockSize, shoreTop));
            }

            var grid = new RoadGrid(world, settings, tops, settlements);
            for (int a = 0; a < settlements.Count; a++)
            for (int b = a + 1; b < settlements.Count; b++)
                Lay(world, settings, tops, grid, network, settlements, anchors, Flat(settlements[a].Square), Flat(settlements[b].Square));

            if (landing.HasValue)
            {
                Settlement nearest = settlements[0];
                foreach (Settlement settlement in settlements)
                    if (Vector2.Distance(Flat(settlement.Square), shore) < Vector2.Distance(Flat(nearest.Square), shore))
                        nearest = settlement;
                Lay(world, settings, tops, grid, network, settlements, anchors, shore, Flat(nearest.Square));
            }
            return network;
        }

        static void Lay(WorldData world, WorldSettings settings, int[] tops, RoadGrid grid, RoadNetwork network,
                        IReadOnlyList<Settlement> settlements, List<Anchor> anchors, Vector2 from, Vector2 to)
        {
            List<Vector2Int> cells = grid.FindPath(from, to);
            if (cells == null)
            {
                Debug.LogWarning($"[EverRealm] No road could be laid from {from} to {to}");
                return;
            }

            // One sample per cell, at the terrain's height, or the pad's inside a hamlet.
            float blockSize = world.BlockSize;
            var points = new Vector2[cells.Count];
            var heights = new float[cells.Count];
            var isFixed = new bool[cells.Count];
            var along = new float[cells.Count];
            for (int i = 0; i < cells.Count; i++)
            {
                points[i] = grid.Centre(cells[i]);
                heights[i] = tops[Column(world, points[i])];
                // Near an earlier road this one takes that road's height, so they meet level.
                if (TryRoadHeightNear(world, tops, points[i], out int laid))
                {
                    heights[i] = laid;
                    isFixed[i] = true;
                }
                if (i > 0) along[i] = along[i - 1] + Vector2.Distance(points[i - 1], points[i]) / blockSize;
                foreach (Anchor anchor in anchors)
                {
                    if (Vector2.Distance(points[i], anchor.Centre) > anchor.Radius) continue;
                    heights[i] = anchor.Top;
                    isFixed[i] = true;
                }
            }

            int lowest = Mathf.CeilToInt((settings.seaLevel + 0.4f) / blockSize);
            float[] profile = GradeProfile(Smooth(heights, isFixed, SmoothingCells), isFixed, along, MaxGrade, lowest);
            Stamp(world, settings, tops, settlements, points, profile);

            var road = new Vector3[points.Length];
            for (int i = 0; i < road.Length; i++)
                road[i] = new Vector3(points[i].x, (Mathf.RoundToInt(profile[i]) + 1) * blockSize, points[i].y);
            network.Add(road, cells);
            grid.MarkRoad(cells);
        }

        /// <summary>The height of the nearest road surface within a road's width of <paramref name="point"/>, if there is one.</summary>
        static bool TryRoadHeightNear(WorldData world, int[] tops, Vector2 point, out int height)
        {
            height = 0;
            int reach = HalfWidth + 2, size = world.SizeInBlocks, best = int.MaxValue;
            int cx = Mathf.FloorToInt(point.x / world.BlockSize), cz = Mathf.FloorToInt(point.y / world.BlockSize);
            for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                int bx = cx + dx, bz = cz + dz;
                if (bx < 0 || bz < 0 || bx >= size || bz >= size) continue;
                int top = tops[bx + bz * size], distance = dx * dx + dz * dz;
                if (distance >= best || world.GetBlock(bx, top, bz) != BlockType.Path) continue;
                best = distance;
                height = top;
            }
            return best != int.MaxValue;
        }

        /// <summary>Moving average of the unfixed heights, so a road rides over small bumps instead of following each one.</summary>
        static float[] Smooth(float[] heights, bool[] isFixed, int radius)
        {
            var smooth = new float[heights.Length];
            for (int i = 0; i < heights.Length; i++)
            {
                if (isFixed[i])
                {
                    smooth[i] = heights[i];
                    continue;
                }
                float sum = 0f;
                int count = 0;
                for (int j = Mathf.Max(0, i - radius); j <= Mathf.Min(heights.Length - 1, i + radius); j++)
                {
                    sum += heights[j];
                    count++;
                }
                smooth[i] = sum / count;
            }
            return smooth;
        }

        /// <summary>
        /// Limits a height profile to <paramref name="maxGrade"/> blocks per block of <paramref name="along"/>,
        /// keeping fixed samples exactly where they are and everything else at or above <paramref name="lowest"/>.
        /// </summary>
        public static float[] GradeProfile(float[] heights, bool[] isFixed, float[] along, float maxGrade, float lowest)
        {
            int count = heights.Length;
            // The band every sample must stay in to reach the fixed samples at the allowed grade.
            var high = new float[count];
            var low = new float[count];
            for (int i = 0; i < count; i++)
            {
                high[i] = isFixed[i] ? heights[i] : float.PositiveInfinity;
                low[i] = isFixed[i] ? heights[i] : float.NegativeInfinity;
            }
            for (int i = 1; i < count; i++) Widen(high, low, i, i - 1, (along[i] - along[i - 1]) * maxGrade);
            for (int i = count - 2; i >= 0; i--) Widen(high, low, i, i + 1, (along[i + 1] - along[i]) * maxGrade);

            var graded = new float[count];
            for (int i = 0; i < count; i++)
            {
                float height = isFixed[i] ? heights[i] : Mathf.Max(heights[i], lowest);
                height = Mathf.Clamp(height, low[i], high[i]);
                if (i > 0)
                {
                    float step = (along[i] - along[i - 1]) * maxGrade;
                    height = Mathf.Clamp(height, Mathf.Max(graded[i - 1] - step, low[i]), Mathf.Min(graded[i - 1] + step, high[i]));
                }
                graded[i] = height;
            }
            return graded;
        }

        static void Widen(float[] high, float[] low, int index, int neighbour, float step)
        {
            high[index] = Mathf.Min(high[index], high[neighbour] + step);
            low[index] = Mathf.Max(low[index], low[neighbour] - step);
        }

        /// <summary>Cuts and fills the columns along the road to its profile, with a shoulder blending back into the terrain.</summary>
        static void Stamp(WorldData world, WorldSettings settings, int[] tops, IReadOnlyList<Settlement> settlements, Vector2[] points, float[] profile)
        {
            float blockSize = world.BlockSize;
            int size = world.SizeInBlocks, reach = HalfWidth + ShoulderBlocks;
            // Each column takes the height of the nearest point on the road's centre line.
            var nearest = new Dictionary<int, (float distance, float height)>();
            for (int i = 0; i + 1 < points.Length; i++)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(points[i], points[i + 1]) / blockSize));
                for (int s = 0; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    Vector2 point = Vector2.Lerp(points[i], points[i + 1], t);
                    float height = Mathf.Lerp(profile[i], profile[i + 1], t);
                    int cx = Mathf.FloorToInt(point.x / blockSize), cz = Mathf.FloorToInt(point.y / blockSize);
                    for (int dz = -reach; dz <= reach; dz++)
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        int bx = cx + dx, bz = cz + dz;
                        if (bx < 0 || bz < 0 || bx >= size || bz >= size) continue;
                        float distance = Vector2.Distance(new Vector2((bx + 0.5f) * blockSize, (bz + 0.5f) * blockSize), point) / blockSize;
                        if (distance > reach) continue;
                        int column = bx + bz * size;
                        if (!nearest.TryGetValue(column, out (float distance, float height) best) || distance < best.distance)
                            nearest[column] = (distance, height);
                    }
                }
            }

            foreach (KeyValuePair<int, (float distance, float height)> entry in nearest)
            {
                int bx = entry.Key % size, bz = entry.Key / size;
                var position = new Vector3((bx + 0.5f) * blockSize, 0f, (bz + 0.5f) * blockSize);
                if (InsideSettlement(settlements, position, out bool onHut))
                {
                    // Hamlets are already level: the road only colours the ground, and leaves the huts alone.
                    if (!onHut && entry.Value.distance <= HalfWidth) world.SetBlock(bx, tops[entry.Key], bz, BlockType.Path);
                    continue;
                }

                int old = tops[entry.Key];
                if (old < 0) continue;
                bool isRoad = entry.Value.distance <= HalfWidth;
                if (world.GetBlock(bx, old, bz) == BlockType.Path) continue; // A road already laid keeps its height.
                float blend = Mathf.SmoothStep(0f, 1f, (entry.Value.distance - HalfWidth) / ShoulderBlocks);
                int target = Mathf.RoundToInt(isRoad ? entry.Value.height : Mathf.Lerp(entry.Value.height, old, blend));
                BlockType surface = world.GetBlock(bx, old, bz);
                if (surface == BlockType.Stone) surface = BlockType.Dirt; // A cutting's sides stay earthy.
                SettlementBuilder.ReshapeColumn(world, settings, bx, bz, old, target, isRoad ? BlockType.Path : surface);
                tops[entry.Key] = target;
            }
        }

        static bool InsideSettlement(IReadOnlyList<Settlement> settlements, Vector3 position, out bool onHut)
        {
            onHut = false;
            foreach (Settlement settlement in settlements)
            {
                if (Vector2.Distance(Flat(position), Flat(settlement.Square)) > settlement.Radius) continue;
                foreach (House house in settlement.Houses)
                {
                    Rect f = house.Footprint;
                    if (position.x >= f.xMin - 1f && position.x <= f.xMax + 1f && position.z >= f.yMin - 1f && position.z <= f.yMax + 1f)
                        onHut = true;
                }
                return true;
            }
            return false;
        }

        // ---- The landing ----

        /// <summary>
        /// The dry shore nearest a hamlet that's still a good walk from all of them, looking outward from the
        /// centre of the hamlets in every direction.
        /// </summary>
        static bool TryFindLanding(WorldData world, WorldSettings settings, int[] tops, IReadOnlyList<Settlement> settlements, out Vector2 best)
        {
            best = default;
            float bestDistance = float.PositiveInfinity, blockSize = world.BlockSize, shoreHeight = settings.seaLevel + 0.1f;
            Vector2 centre = Vector2.zero;
            foreach (Settlement settlement in settlements) centre += Flat(settlement.Square);
            centre /= settlements.Count;

            for (int direction = 0; direction < 36; direction++)
            {
                float angle = direction * Mathf.PI * 2f / 36f;
                var ray = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 point = centre, lastDry = centre;
                bool reachedSea = false;
                for (float distance = 0f; distance < world.SizeInMeters; distance += LandingSearchStep)
                {
                    point = centre + ray * distance;
                    if (!InWorld(world, point)) break;
                    if ((tops[Column(world, point)] + 1) * blockSize <= shoreHeight)
                    {
                        reachedSea = true;
                        break;
                    }
                    lastDry = point;
                }
                if (!reachedSea) continue;

                float nearest = float.PositiveInfinity;
                foreach (Settlement settlement in settlements)
                    nearest = Mathf.Min(nearest, Vector2.Distance(Flat(settlement.Square), lastDry));
                if (nearest >= MinLandingDistance && nearest < bestDistance)
                {
                    bestDistance = nearest;
                    best = lastDry;
                }
            }
            return bestDistance < float.PositiveInfinity;
        }

        static void StampLanding(WorldData world, WorldSettings settings, int[] tops, Vector2 shore, int top)
        {
            float blockSize = world.BlockSize;
            int cx = Mathf.FloorToInt(shore.x / blockSize), cz = Mathf.FloorToInt(shore.y / blockSize), size = world.SizeInBlocks;
            for (int dz = -LandingPadBlocks; dz <= LandingPadBlocks; dz++)
            for (int dx = -LandingPadBlocks; dx <= LandingPadBlocks; dx++)
            {
                int bx = cx + dx, bz = cz + dz;
                if (bx < 0 || bz < 0 || bx >= size || bz >= size) continue;
                int column = bx + bz * size;
                SettlementBuilder.ReshapeColumn(world, settings, bx, bz, tops[column], top, BlockType.Planks);
                tops[column] = top;
            }
        }

        // ---- Helpers ----

        static Vector2 Flat(Vector3 position) => new Vector2(position.x, position.z);

        static bool InWorld(WorldData world, Vector2 point) =>
            point.x >= 0f && point.y >= 0f && point.x < world.SizeInMeters && point.y < world.SizeInMeters;

        static int Column(WorldData world, Vector2 point)
        {
            int bx = Mathf.Clamp(Mathf.FloorToInt(point.x / world.BlockSize), 0, world.SizeInBlocks - 1);
            int bz = Mathf.Clamp(Mathf.FloorToInt(point.y / world.BlockSize), 0, world.SizeInBlocks - 1);
            return bx + bz * world.SizeInBlocks;
        }


        /// <summary>The coarse grid roads are searched on.</summary>
        sealed class RoadGrid
        {
            readonly int side;
            readonly float cellMeters;
            readonly int[] heights;
            readonly bool[] blocked, crowded, onRoad;

            public RoadGrid(WorldData world, WorldSettings settings, int[] tops, IReadOnlyList<Settlement> settlements)
            {
                float blockSize = world.BlockSize;
                cellMeters = CellBlocks * blockSize;
                side = world.SizeInBlocks / CellBlocks;
                heights = new int[side * side];
                blocked = new bool[side * side];
                crowded = new bool[side * side];
                onRoad = new bool[side * side];
                float dry = settings.seaLevel + 0.4f;

                for (int z = 0; z < side; z++)
                for (int x = 0; x < side; x++)
                {
                    int index = x + z * side;
                    Vector2 centre = Centre(new Vector2Int(x, z));
                    int top = tops[Column(world, centre)];
                    heights[index] = top;
                    blocked[index] = (top + 1) * blockSize <= dry || NearHut(settlements, centre, HutWall);
                    crowded[index] = NearHut(settlements, centre, HutMargin);
                }
            }

            static bool NearHut(IReadOnlyList<Settlement> settlements, Vector2 point, float margin)
            {
                foreach (Settlement settlement in settlements)
                foreach (House house in settlement.Houses)
                {
                    Rect f = house.Footprint;
                    if (point.x >= f.xMin - margin && point.x <= f.xMax + margin &&
                        point.y >= f.yMin - margin && point.y <= f.yMax + margin) return true;
                }
                return false;
            }

            public Vector2 Centre(Vector2Int cell) => new Vector2((cell.x + 0.5f) * cellMeters, (cell.y + 0.5f) * cellMeters);

            Vector2Int CellAt(Vector2 point) => new Vector2Int(
                Mathf.Clamp(Mathf.FloorToInt(point.x / cellMeters), 0, side - 1),
                Mathf.Clamp(Mathf.FloorToInt(point.y / cellMeters), 0, side - 1));

            public void MarkRoad(List<Vector2Int> cells)
            {
                foreach (Vector2Int cell in cells) onRoad[cell.x + cell.y * side] = true;
            }

            /// <summary>The cheapest cells from one point to another, preferring level ground and existing road. Null if there's no way.</summary>
            public List<Vector2Int> FindPath(Vector2 from, Vector2 to)
            {
                Vector2Int start = CellAt(from), goal = CellAt(to);
                int startIndex = start.x + start.y * side, goalIndex = goal.x + goal.y * side;
                var cost = new float[side * side];
                var previous = new int[side * side];
                for (int i = 0; i < cost.Length; i++)
                {
                    cost[i] = float.PositiveInfinity;
                    previous[i] = -1;
                }
                cost[startIndex] = 0f;
                var open = new SortedSet<(float priority, int index)> { (0f, startIndex) };

                while (open.Count > 0)
                {
                    (float _, int current) = open.Min;
                    open.Remove(open.Min);
                    if (current == goalIndex) break;
                    int cx = current % side, cz = current / side;

                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= side || nz >= side) continue;
                        int next = nx + nz * side;
                        // The endpoints may sit on awkward cells; everything between must be clear.
                        if (blocked[next] && next != goalIndex) continue;
                        // A diagonal can't cut the corner of something solid.
                        if (dx != 0 && dz != 0 && (blocked[cx + dx + cz * side] || blocked[cx + (cz + dz) * side])) continue;

                        float distance = dx != 0 && dz != 0 ? 1.414f : 1f;
                        float climb = Mathf.Abs(heights[next] - heights[current]) / distance;
                        float step = distance * (1f + ClimbPenalty * climb * climb);
                        // Roads keep their distance from huts, but can squeeze between them when a square is hemmed in.
                        if (crowded[next]) step *= CrowdedPenalty;
                        if (onRoad[next]) step *= SharedRoadDiscount;
                        float total = cost[current] + step;
                        if (total >= cost[next]) continue;

                        cost[next] = total;
                        previous[next] = current;
                        float remaining = SharedRoadDiscount * Vector2Int.Distance(new Vector2Int(nx, nz), goal);
                        open.Add((total + remaining, next));
                    }
                }

                if (previous[goalIndex] < 0 && goalIndex != startIndex) return null;
                var path = new List<Vector2Int>();
                for (int index = goalIndex; index >= 0; index = previous[index])
                    path.Add(new Vector2Int(index % side, index / side));
                path.Reverse();
                return path;
            }
        }
    }
}
