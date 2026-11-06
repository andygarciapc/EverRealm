using System.Collections.Generic;
using UnityEngine;

namespace EverRealm.World
{
    /// <summary>
    /// Finds a fairly flat grass site near the island centre, flattens it and stamps a ring of huts around
    /// an open square, doors facing in. Deterministic from the seed. Huts are measured in whole blocks, the
    /// world's building unit; the site and the layout of the huts on it are in metres.
    /// </summary>
    public static class SettlementBuilder
    {
        const float SearchRadius = 60f, SearchStep = 4f;
        // A 2 x 4 block doorway (1.4m x 2.8m at 0.7m blocks) fits the tallest planned humans (2.1m) without
        // catching the lintel. Walls stand a block above the doorway.
        const int DoorWidth = 2, DoorHeight = 4, WallHeight = DoorHeight + 1;
        // How far the door, inside and bed points sit from the walls.
        const float DoorStandOff = 0.9f, WallStandOff = 0.6f;

        /// <summary>Builds the hamlet into <paramref name="world"/>, or returns null if no site is flat enough.</summary>
        public static Settlement Build(WorldData world, WorldSettings settings)
        {
            int[] tops = TopBlocks(world);
            if (!TryFindSite(world, settings, tops, out Vector2 centre, out int padTop)) return null;
            Flatten(world, settings, tops, centre, padTop);

            float blockSize = world.BlockSize;
            float floorHeight = (padTop + 1) * blockSize;
            var rng = new System.Random(settings.seed * 17 + 3);
            int hutCount = rng.Next(settings.minHuts, settings.maxHuts + 1);
            int hutBlocks = Mathf.Max(DoorWidth + 2, settings.hutBlocks);
            float hutMeters = hutBlocks * blockSize;
            // Huts sit on a ring that keeps their far corners on the flattened ground.
            float ring = settings.hamletRadius - hutMeters * 0.71f - 1f;
            double startAngle = rng.NextDouble() * Mathf.PI * 2;

            var houses = new List<House>();
            var footprints = new List<RectInt>();
            var workSpots = new List<Vector3>();
            for (int i = 0; i < hutCount; i++)
            {
                double angle = startAngle + i * Mathf.PI * 2 / hutCount + (rng.NextDouble() - 0.5) * 0.3;
                Vector2 hutCentre = centre + new Vector2((float)System.Math.Cos(angle), (float)System.Math.Sin(angle)) * ring;
                int x0 = Mathf.FloorToInt(hutCentre.x / blockSize) - hutBlocks / 2;
                int z0 = Mathf.FloorToInt(hutCentre.y / blockSize) - hutBlocks / 2;
                var footprint = new RectInt(x0, z0, hutBlocks, hutBlocks);
                if (footprints.Exists(f => Overlaps(f, footprint, 2))) continue;
                footprints.Add(footprint);

                Vector2 toCentre = centre - hutCentre;
                Vector2Int doorSide = Mathf.Abs(toCentre.x) >= Mathf.Abs(toCentre.y)
                    ? new Vector2Int(toCentre.x > 0 ? 1 : -1, 0)
                    : new Vector2Int(0, toCentre.y > 0 ? 1 : -1);
                houses.Add(StampHut(world, footprint, padTop, doorSide));

                // Work spots sit in the gaps between huts, so the square reaches them in a straight line.
                double gapAngle = startAngle + (i + 0.5) * Mathf.PI * 2 / hutCount;
                Vector2 spot = centre + new Vector2((float)System.Math.Cos(gapAngle), (float)System.Math.Sin(gapAngle)) * ring;
                workSpots.Add(new Vector3(spot.x, floorHeight, spot.y));
            }

            return new Settlement(new Vector3(centre.x, floorHeight, centre.y), settings.hamletRadius, houses, workSpots);
        }

        /// <summary>Index of the highest solid block in each column (x + z * size), or -1 for an empty column.</summary>
        static int[] TopBlocks(WorldData world)
        {
            int size = world.SizeInBlocks;
            var tops = new int[size * size];
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                int y = world.GetChunkTop(x / world.ChunkSize, z / world.ChunkSize) - 1;
                while (y >= 0 && world.GetBlock(x, y, z) == BlockType.Air) y--;
                tops[x + z * size] = y;
            }
            return tops;
        }

        /// <summary>Flattest all-grass site near the centre; ties go to the one closer to the centre.</summary>
        static bool TryFindSite(WorldData world, WorldSettings settings, int[] tops, out Vector2 best, out int bestPadTop)
        {
            best = default;
            bestPadTop = 0;
            float half = world.SizeInMeters * 0.5f, bestScore = float.PositiveInfinity;
            for (float oz = -SearchRadius; oz <= SearchRadius; oz += SearchStep)
            for (float ox = -SearchRadius; ox <= SearchRadius; ox += SearchStep)
            {
                float distance = Mathf.Sqrt(ox * ox + oz * oz);
                if (distance > SearchRadius) continue;
                var candidate = new Vector2(half + ox, half + oz);
                if (!TrySite(world, settings, tops, candidate, out float relief, out int padTop)) continue;
                float score = relief + distance * 0.03f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                    bestPadTop = padTop;
                }
            }
            return bestScore < float.PositiveInfinity;
        }

        static bool TrySite(WorldData world, WorldSettings settings, int[] tops, Vector2 centre, out float relief, out int padTop)
        {
            relief = 0f;
            padTop = 0;
            float blockSize = world.BlockSize, radius = settings.hamletRadius, step = Mathf.Max(1f, blockSize);
            float minLand = settings.seaLevel + settings.beachHeight + 0.5f;
            float rockHeight = settings.seaLevel + settings.rockLine * (settings.maxHeight - settings.seaLevel);
            int size = world.SizeInBlocks, lowest = int.MaxValue, highest = int.MinValue;
            long sum = 0;
            int count = 0;

            for (float dz = -radius; dz <= radius; dz += step)
            for (float dx = -radius; dx <= radius; dx += step)
            {
                if (dx * dx + dz * dz > radius * radius) continue;
                int bx = Mathf.FloorToInt((centre.x + dx) / blockSize), bz = Mathf.FloorToInt((centre.y + dz) / blockSize);
                if (bx < 0 || bz < 0 || bx >= size || bz >= size) return false;
                int top = tops[bx + bz * size];
                float topMeters = (top + 1) * blockSize;
                if (top < 0 || topMeters <= minLand || topMeters >= rockHeight) return false;
                lowest = Mathf.Min(lowest, top);
                highest = Mathf.Max(highest, top);
                sum += top;
                count++;
            }

            relief = (highest - lowest) * blockSize;
            if (count == 0 || relief > settings.hamletMaxRelief) return false;
            padTop = Mathf.RoundToInt(sum / (float)count);
            return true;
        }

        /// <summary>Levels the site to <paramref name="padTop"/> and blends a slope back into the terrain around it.</summary>
        static void Flatten(WorldData world, WorldSettings settings, int[] tops, Vector2 centre, int padTop)
        {
            float blockSize = world.BlockSize, radius = settings.hamletRadius, outer = radius + settings.hamletBlend;
            int dirtBlocks = Mathf.Max(1, Mathf.RoundToInt(settings.dirtDepth / blockSize));
            int size = world.SizeInBlocks;
            int minX = Mathf.Max(0, Mathf.FloorToInt((centre.x - outer) / blockSize));
            int maxX = Mathf.Min(size - 1, Mathf.CeilToInt((centre.x + outer) / blockSize));
            int minZ = Mathf.Max(0, Mathf.FloorToInt((centre.y - outer) / blockSize));
            int maxZ = Mathf.Min(size - 1, Mathf.CeilToInt((centre.y + outer) / blockSize));

            for (int bz = minZ; bz <= maxZ; bz++)
            for (int bx = minX; bx <= maxX; bx++)
            {
                var column = new Vector2((bx + 0.5f) * blockSize, (bz + 0.5f) * blockSize);
                float distance = Vector2.Distance(column, centre);
                int index = bx + bz * size, old = tops[index];
                if (distance > outer || old < 0) continue;

                int target = distance <= radius
                    ? padTop
                    : Mathf.RoundToInt(Mathf.Lerp(padTop, old, Mathf.SmoothStep(0f, 1f, (distance - radius) / settings.hamletBlend)));
                if (target == old) continue;

                // Reshape the column but keep its surface material (grass on the site, maybe sand at the edge).
                BlockType surface = world.GetBlock(bx, old, bz);
                BlockType subsurface = surface == BlockType.Grass ? BlockType.Dirt : surface;
                for (int y = Mathf.Max(0, Mathf.Min(old, target) - dirtBlocks); y <= Mathf.Max(old, target); y++)
                {
                    BlockType block = y > target ? BlockType.Air
                                    : y == target ? surface
                                    : y >= target - dirtBlocks ? subsurface
                                    : BlockType.Stone;
                    world.SetBlock(bx, y, bz, block);
                }
                tops[index] = target;
            }
        }

        /// <summary>Plank floor, wooden walls with a doorway on <paramref name="doorSide"/>, stepped thatch roof.</summary>
        static House StampHut(WorldData world, RectInt footprint, int floorY, Vector2Int doorSide)
        {
            float blockSize = world.BlockSize;
            int width = footprint.width;

            for (int z = footprint.yMin; z < footprint.yMax; z++)
            for (int x = footprint.xMin; x < footprint.xMax; x++)
            {
                world.SetBlock(x, floorY, z, BlockType.Planks);
                bool wall = x == footprint.xMin || x == footprint.xMax - 1 || z == footprint.yMin || z == footprint.yMax - 1;
                for (int y = floorY + 1; y <= floorY + WallHeight; y++)
                    world.SetBlock(x, y, z, wall ? BlockType.Wood : BlockType.Air);
            }

            int doorStart = (width - DoorWidth) / 2;
            for (int k = 0; k < DoorWidth; k++)
            for (int y = floorY + 1; y <= floorY + DoorHeight; y++)
            {
                if (doorSide.x != 0)
                    world.SetBlock(doorSide.x > 0 ? footprint.xMax - 1 : footprint.xMin, y, footprint.yMin + doorStart + k, BlockType.Air);
                else
                    world.SetBlock(footprint.xMin + doorStart + k, y, doorSide.y > 0 ? footprint.yMax - 1 : footprint.yMin, BlockType.Air);
            }

            // One block of overhang, then each layer steps in by a block.
            for (int layer = 0; ; layer++)
            {
                int inset = layer - 1;
                int xMin = footprint.xMin + inset, xMax = footprint.xMax - 1 - inset;
                int zMin = footprint.yMin + inset, zMax = footprint.yMax - 1 - inset;
                if (xMin > xMax || zMin > zMax) break;
                int y = floorY + WallHeight + 1 + layer;
                for (int z = zMin; z <= zMax; z++)
                for (int x = xMin; x <= xMax; x++)
                    world.SetBlock(x, y, z, BlockType.Thatch);
            }

            return HousePoints(footprint, floorY, doorSide, doorStart, DoorWidth, blockSize);
        }

        static House HousePoints(RectInt footprint, int floorY, Vector2Int doorSide, int doorStart, int doorWidth, float blockSize)
        {
            float floor = (floorY + 1) * blockSize;
            bool alongX = doorSide.x != 0;
            float sign = alongX ? doorSide.x : doorSide.y;
            // "Normal" runs through the doorway; "tangent" runs along the door wall.
            float min = (alongX ? footprint.xMin : footprint.yMin) * blockSize;
            float max = (alongX ? footprint.xMax : footprint.yMax) * blockSize;
            float tangentMin = (alongX ? footprint.yMin : footprint.xMin) * blockSize;
            float doorTangent = tangentMin + (doorStart + doorWidth * 0.5f) * blockSize;

            float outerFace = sign > 0 ? max : min;
            float innerFace = outerFace - sign * blockSize;
            float farInnerFace = (sign > 0 ? min : max) + sign * blockSize;

            Vector3 Point(float normal, float tangent) =>
                alongX ? new Vector3(normal, floor, tangent) : new Vector3(tangent, floor, normal);

            return new House(
                door: Point(outerFace + sign * DoorStandOff, doorTangent),
                inside: Point(innerFace - sign * WallStandOff, doorTangent),
                bed: Point(farInnerFace + sign * WallStandOff, tangentMin + blockSize + WallStandOff),
                footprint: new Rect(footprint.xMin * blockSize, footprint.yMin * blockSize,
                                    footprint.width * blockSize, footprint.height * blockSize));
        }

        static bool Overlaps(RectInt a, RectInt b, int margin) =>
            a.xMin - margin < b.xMax && b.xMin - margin < a.xMax && a.yMin - margin < b.yMax && b.yMin - margin < a.yMax;
    }
}
