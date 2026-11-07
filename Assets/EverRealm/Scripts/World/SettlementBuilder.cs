using System.Collections.Generic;
using UnityEngine;

namespace EverRealm.World
{
    /// <summary>
    /// Finds fairly flat grass sites a good walk apart, flattens each and stamps a ring of huts around an
    /// open square, doors facing in, and names it. Deterministic from the seed. Huts are measured in whole
    /// blocks, the world's building unit; the sites and the layout of the huts on them are in metres.
    /// </summary>
    public static class SettlementBuilder
    {
        const float SearchStep = 16f;
        // Of equally flat sites, those nearer the centre are preferred, staying clear of the coast.
        const float CentrePreference = 0.01f;
        const int FirstSiteTries = 64;

        // Provisional names, chosen by seed. The creative director may want to set the tone.
        static readonly string[] Names =
        {
            "Hollowmere", "Ashford", "Thornwick", "Bracken Hollow", "Elderbrook", "Stonewell",
            "Greywater", "Oakenfield", "Marrowdale", "Fernhaven", "Wrenbury", "Dunmarrow",
        };

        // A 2 x 4 block doorway (1.4m x 2.8m at 0.7m blocks) fits the tallest planned humans (2.1m) without
        // catching the lintel. Walls stand a block above the doorway.
        const int DoorWidth = 2, DoorHeight = 4, WallHeight = DoorHeight + 1;
        // How far the door, inside and bed points sit from the walls.
        const float DoorStandOff = 0.9f, WallStandOff = 0.6f;

        readonly struct Site
        {
            public readonly Vector2 Centre;
            public readonly int PadTop;
            public readonly float Score;

            public Site(Vector2 centre, int padTop, float score)
            {
                Centre = centre;
                PadTop = padTop;
                Score = score;
            }
        }

        /// <summary>Builds the island's hamlets into <paramref name="world"/>: as many as the land has room for, up to the wanted number.</summary>
        public static List<Settlement> Build(WorldData world, WorldSettings settings)
        {
            int[] tops = TopBlocks(world);
            var rng = new System.Random(settings.seed * 17 + 3);
            var names = new List<string>(Names);
            var settlements = new List<Settlement>();
            foreach (Site site in ChooseSites(FindSites(world, settings, tops), settings))
            {
                int name = rng.Next(names.Count);
                Flatten(world, settings, tops, site.Centre, site.PadTop);
                settlements.Add(BuildHamlet(world, settings, rng, names[name], site));
                names.RemoveAt(name);
            }
            return settlements;
        }

        static Settlement BuildHamlet(WorldData world, WorldSettings settings, System.Random rng, string name, Site site)
        {
            Vector2 centre = site.Centre;
            int padTop = site.PadTop;
            float blockSize = world.BlockSize;
            float floorHeight = (padTop + 1) * blockSize;
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

            return new Settlement(name, new Vector3(centre.x, floorHeight, centre.y), settings.hamletRadius, houses, workSpots);
        }

        /// <summary>Index of the highest solid block in each column (x + z * size), or -1 for an empty column.</summary>
        public static int[] TopBlocks(WorldData world)
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

        /// <summary>Every all-grass site on the island that's flat enough, best first: flattest, then nearest the centre.</summary>
        static List<Site> FindSites(WorldData world, WorldSettings settings, int[] tops)
        {
            var sites = new List<Site>();
            float size = world.SizeInMeters, half = size * 0.5f;
            for (float z = SearchStep; z < size; z += SearchStep)
            for (float x = SearchStep; x < size; x += SearchStep)
            {
                var candidate = new Vector2(x, z);
                if (!TrySite(world, settings, tops, candidate, out float relief, out int padTop)) continue;
                sites.Add(new Site(candidate, padTop, relief + Vector2.Distance(candidate, new Vector2(half, half)) * CentrePreference));
            }
            sites.Sort((a, b) => a.Score.CompareTo(b.Score));
            return sites;
        }

        /// <summary>
        /// The best set of sites at the wanted spacing. If the land can't give the wanted number, the spacing
        /// relaxes before the number does: first the upper limit, then the lower.
        /// </summary>
        static List<Site> ChooseSites(List<Site> sites, WorldSettings settings)
        {
            var best = new List<Site>();
            foreach ((float min, float max) in new[]
                     {
                         (settings.minHamletSpacing, settings.maxHamletSpacing),
                         (settings.minHamletSpacing, float.PositiveInfinity),
                         (settings.minHamletSpacing * 0.5f, float.PositiveInfinity),
                     })
            {
                for (int first = 0; first < sites.Count && first < FirstSiteTries; first++)
                {
                    List<Site> chosen = SpacedFrom(sites, first, settings.hamletCount, min, max);
                    if (chosen.Count > best.Count) best = chosen;
                    if (best.Count == settings.hamletCount) return best;
                }
            }
            return best;
        }

        static List<Site> SpacedFrom(List<Site> sites, int first, int wanted, float min, float max)
        {
            var chosen = new List<Site> { sites[first] };
            foreach (Site site in sites)
            {
                if (chosen.Count == wanted) break;
                if (chosen.TrueForAll(c =>
                    {
                        float distance = Vector2.Distance(c.Centre, site.Centre);
                        return distance >= min && distance <= max;
                    })) chosen.Add(site);
            }
            return chosen;
        }

        static bool TrySite(WorldData world, WorldSettings settings, int[] tops, Vector2 centre, out float relief, out int padTop)
        {
            relief = 0f;
            padTop = 0;
            float blockSize = world.BlockSize, radius = settings.hamletRadius, step = Mathf.Max(1f, blockSize);
            float minLand = settings.seaLevel + settings.beachHeight + 0.5f;
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
                if (top < 0 || topMeters <= minLand || topMeters >= settings.hamletMaxHeight) return false;
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
                ReshapeColumn(world, settings, bx, bz, old, target, world.GetBlock(bx, old, bz));
                tops[index] = target;
            }
        }

        /// <summary>Cuts or fills a column from height <paramref name="old"/> to <paramref name="target"/>, topped with <paramref name="surface"/>.</summary>
        public static void ReshapeColumn(WorldData world, WorldSettings settings, int bx, int bz, int old, int target, BlockType surface)
        {
            int dirtBlocks = Mathf.Max(1, Mathf.RoundToInt(settings.dirtDepth / world.BlockSize));
            BlockType subsurface = surface == BlockType.Sand || surface == BlockType.Stone ? surface : BlockType.Dirt;
            for (int y = Mathf.Max(0, Mathf.Min(old, target) - dirtBlocks); y <= Mathf.Max(old, target); y++)
            {
                BlockType block = y > target ? BlockType.Air
                                : y == target ? surface
                                : y >= target - dirtBlocks ? subsurface
                                : BlockType.Stone;
                world.SetBlock(bx, y, bz, block);
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
