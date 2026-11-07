using System.Collections.Generic;
using UnityEngine;

namespace EverRealm.World
{
    /// <summary>
    /// Grows each biome's trees, after hamlets and roads have claimed their ground. Canopies start above head
    /// height, so only a narrow trunk stands in anyone's way.
    /// </summary>
    public static class TreeBuilder
    {
        // Blocks to keep clear of a road or building.
        const int Clearance = 3;

        /// <summary>Plants the island and returns how many trees it grew.</summary>
        public static int Plant(WorldData world, WorldSettings settings, TerrainGenerator terrain, IReadOnlyList<Settlement> settlements)
        {
            int[] tops = SettlementBuilder.TopBlocks(world);
            int cells = Mathf.CeilToInt(world.SizeInMeters / settings.treeSpacing), planted = 0;
            for (int cz = 0; cz < cells; cz++)
            for (int cx = 0; cx < cells; cx++)
            {
                var rng = new System.Random(ChunkMesher.Hash(cx, settings.seed, cz));
                float x = (cx + (float)rng.NextDouble()) * settings.treeSpacing;
                float z = (cz + (float)rng.NextDouble()) * settings.treeSpacing;
                Biome biome = terrain.BiomeAt(x, z);
                if (biome.tree == TreeShape.None || rng.NextDouble() >= biome.treeDensity) continue;

                int bx = Mathf.FloorToInt(x / world.BlockSize), bz = Mathf.FloorToInt(z / world.BlockSize);
                if (!CanGrow(world, settings, tops, settlements, bx, bz)) continue;
                Grow(world, rng, biome.tree, bx, tops[bx + bz * world.SizeInBlocks], bz);
                planted++;
            }
            return planted;
        }

        static bool CanGrow(WorldData world, WorldSettings settings, int[] tops, IReadOnlyList<Settlement> settlements, int bx, int bz)
        {
            int size = world.SizeInBlocks;
            if (bx < Clearance || bz < Clearance || bx >= size - Clearance || bz >= size - Clearance) return false;
            int top = tops[bx + bz * size];
            if (world.GetBlock(bx, top, bz) != BlockType.Grass) return false;
            if (world.GetBlock(bx, top + 1, bz) != BlockType.Air) return false; // Under a neighbour's canopy.

            var position = new Vector3((bx + 0.5f) * world.BlockSize, 0f, (bz + 0.5f) * world.BlockSize);
            foreach (Settlement settlement in settlements)
            {
                Vector3 square = settlement.Square;
                square.y = 0f;
                if (Vector3.Distance(position, square) < settlement.Radius + settings.hamletBlend + settings.treeHamletMargin) return false;
            }

            for (int dz = -Clearance; dz <= Clearance; dz++)
            for (int dx = -Clearance; dx <= Clearance; dx++)
            {
                BlockType block = world.GetBlock(bx + dx, tops[bx + dx + (bz + dz) * size], bz + dz);
                if (block is BlockType.Path or BlockType.Planks or BlockType.Wood or BlockType.Thatch) return false;
            }
            return true;
        }

        static void Grow(WorldData world, System.Random rng, TreeShape shape, int x, int ground, int z)
        {
            switch (shape)
            {
                case TreeShape.Round:
                    Broadleaf(world, rng, x, ground, z, trunk: rng.Next(7, 11), width: rng.Next(3, 6), thick: false);
                    break;
                case TreeShape.Giant:
                    bool giant = rng.Next(7) == 0;
                    Broadleaf(world, rng, x, ground, z, trunk: giant ? rng.Next(16, 23) : rng.Next(9, 14), width: giant ? rng.Next(8, 12) : rng.Next(4, 6), thick: giant);
                    break;
                case TreeShape.Pine:
                    Pine(world, rng, x, ground, z);
                    break;
                case TreeShape.Cactus:
                    Cactus(world, rng, x, ground, z);
                    break;
            }
        }

        static void Broadleaf(WorldData world, System.Random rng, int x, int ground, int z, int trunk, int width, bool thick)
        {
            int crown = ground + trunk;
            Trunk(world, x, ground, z, trunk, thick);
            float height = width * 0.75f;
            Blob(world, rng, x, crown, z, width, height);
            // A second, offset lobe keeps big crowns from reading as balls on sticks.
            if (width >= 5)
                Blob(world, rng, x + rng.Next(-width / 2, width / 2 + 1), crown + Mathf.RoundToInt(height * 0.6f), z + rng.Next(-width / 2, width / 2 + 1), width * 0.65f, height * 0.7f);
        }

        static void Pine(WorldData world, System.Random rng, int x, int ground, int z)
        {
            int height = rng.Next(13, 21), lowest = 5;
            Trunk(world, x, ground, z, height - 1, false);
            for (int y = lowest; y <= height; y++)
            {
                // Tiers that narrow towards the tip, each flaring out at its base.
                float taper = 1f - (y - lowest) / (float)(height - lowest);
                float radius = 0.6f + taper * 4.2f * (0.55f + 0.45f * ((height - y) % 3 / 2f));
                Disc(world, x, ground + y, z, radius);
            }
        }

        static void Cactus(WorldData world, System.Random rng, int x, int ground, int z)
        {
            int height = rng.Next(4, 8);
            Leaf(world, x, ground + 1, z, height);
            if (height < 5) return;
            int side = rng.Next(2) == 0 ? 1 : -1, arm = ground + rng.Next(2, height - 1);
            Leaf(world, x + side, arm, z, 1);
            Leaf(world, x + side * 2, arm, z, 3);
        }

        static void Trunk(WorldData world, int x, int ground, int z, int height, bool thick)
        {
            for (int y = ground + 1; y <= ground + height; y++)
            {
                Place(world, x, y, z, BlockType.Log);
                if (!thick) continue;
                Place(world, x + 1, y, z, BlockType.Log);
                Place(world, x, y, z + 1, BlockType.Log);
                Place(world, x + 1, y, z + 1, BlockType.Log);
            }
        }

        /// <summary>A lumpy ellipsoid of leaves: its outer shell loses blocks at random.</summary>
        static void Blob(WorldData world, System.Random rng, int cx, int cy, int cz, float width, float height)
        {
            int reach = Mathf.CeilToInt(width), rise = Mathf.CeilToInt(height);
            for (int dy = -rise; dy <= rise; dy++)
            for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                float distance = (dx * dx + dz * dz) / (width * width) + dy * dy / (height * height);
                if (distance > 1f || (distance > 0.72f && rng.Next(4) == 0)) continue;
                Place(world, cx + dx, cy + dy, cz + dz, BlockType.Leaves);
            }
        }

        static void Disc(WorldData world, int cx, int y, int cz, float radius)
        {
            int reach = Mathf.CeilToInt(radius);
            for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
                if (dx * dx + dz * dz <= radius * radius) Place(world, cx + dx, y, cz + dz, BlockType.Leaves);
        }

        static void Leaf(WorldData world, int x, int y, int z, int height)
        {
            for (int i = 0; i < height; i++) Place(world, x, y + i, z, BlockType.Leaves);
        }

        /// <summary>Trees only fill empty air; a trunk still replaces leaves it grows through.</summary>
        static void Place(WorldData world, int x, int y, int z, BlockType block)
        {
            BlockType current = world.GetBlock(x, y, z);
            if (current == BlockType.Air || (current == BlockType.Leaves && block == BlockType.Log)) world.SetBlock(x, y, z, block);
        }
    }
}
