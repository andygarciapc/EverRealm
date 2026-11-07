using System.Collections.Generic;
using UnityEngine;

namespace EverRealm.World
{
    /// <summary>
    /// Rock added to the land block by block, for the shapes a heightmap can't make. Fills only empty air, and
    /// <see cref="Finish"/> grasses the flat tops and records the new surface.
    /// </summary>
    public sealed class RockStamp
    {
        // Blocks of headroom rock must leave over a road or building.
        const int Headroom = 7;

        readonly WorldData world;
        readonly WorldSettings settings;
        readonly int[] tops;
        readonly Dictionary<Vector2Int, int> highest = new Dictionary<Vector2Int, int>();

        public RockStamp(WorldData world, WorldSettings settings, int[] tops)
        {
            this.world = world;
            this.settings = settings;
            this.tops = tops;
        }

        public void Add(int x, int y, int z)
        {
            int size = world.SizeInBlocks;
            if (x < 0 || z < 0 || x >= size || z >= size || y < 0 || y >= world.Height) return;
            if (world.GetBlock(x, y, z) != BlockType.Air) return;
            world.SetBlock(x, y, z, BlockType.Stone);
            var column = new Vector2Int(x, z);
            if (!highest.TryGetValue(column, out int top) || y > top) highest[column] = y;
        }

        /// <summary>Grass takes on the flats of the new rock, not its steep sides.</summary>
        public void Finish()
        {
            foreach (KeyValuePair<Vector2Int, int> column in highest)
            {
                int index = column.Key.x + column.Key.y * world.SizeInBlocks;
                if (column.Value <= tops[index]) continue;
                tops[index] = column.Value;
                if (!IsLevel(column.Key, column.Value)) continue;
                bool snowy = (column.Value + 1) * world.BlockSize >= settings.snowLine;
                world.SetBlock(column.Key.x, column.Value, column.Key.y, snowy ? BlockType.Snow : BlockType.Grass);
            }
            highest.Clear();
        }

        bool IsLevel(Vector2Int column, int top)
        {
            foreach (Vector2Int side in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                if (!highest.TryGetValue(column + side, out int next) || Mathf.Abs(next - top) > 1) return false;
            return true;
        }

        /// <summary>Whether rock may stand in this circle (in blocks): nowhere near a hamlet, and not on a road or building.</summary>
        public static bool IsClear(WorldData world, WorldSettings settings, int[] tops, IReadOnlyList<Settlement> settlements, Vector2 centre, float radius)
        {
            float blockSize = world.BlockSize;
            foreach (Settlement settlement in settlements)
            {
                var square = new Vector2(settlement.Square.x, settlement.Square.z) / blockSize;
                if (Vector2.Distance(square, centre) < (settlement.Radius + settings.hamletBlend + 6f) / blockSize + radius) return false;
            }

            int size = world.SizeInBlocks, reach = Mathf.CeilToInt(radius);
            int cx = Mathf.RoundToInt(centre.x), cz = Mathf.RoundToInt(centre.y);
            for (int dz = -reach; dz <= reach; dz += 2)
            for (int dx = -reach; dx <= reach; dx += 2)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= size || z >= size) return false;
                if (dx * dx + dz * dz <= radius * radius && IsBuilt(world, tops, x, z)) return false;
            }
            return true;
        }

        /// <summary>Whether block (x, y, z) would come too close over a road or building.</summary>
        public static bool CrowdsBuilding(WorldData world, int[] tops, int x, int y, int z) =>
            y - tops[x + z * world.SizeInBlocks] < Headroom && IsBuilt(world, tops, x, z);

        static bool IsBuilt(WorldData world, int[] tops, int x, int z) =>
            world.GetBlock(x, tops[x + z * world.SizeInBlocks], z) is BlockType.Path or BlockType.Planks or BlockType.Wood or BlockType.Thatch;

        /// <summary>Broken rock scattered on the ground around a formation, thinning out, so it rises from the land rather than sitting on it.</summary>
        public static void Scree(WorldData world, int[] tops, Vector2 centre, float radius, int seed)
        {
            int size = world.SizeInBlocks, reach = Mathf.CeilToInt(radius);
            int cx = Mathf.RoundToInt(centre.x), cz = Mathf.RoundToInt(centre.y);
            for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= size || z >= size) continue;
                float distance = Mathf.Sqrt(dx * dx + dz * dz) / radius;
                if (distance > 1f) continue;
                int top = tops[x + z * size];
                if (world.GetBlock(x, top, z) != BlockType.Grass) continue;
                if (ChunkMesher.Hash(x, seed, z) % 100 < (1f - distance) * (1f - distance) * 130f) world.SetBlock(x, top, z, BlockType.Stone);
            }
        }
    }
}
