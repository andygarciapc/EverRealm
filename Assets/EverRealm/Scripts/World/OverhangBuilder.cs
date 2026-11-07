using System;
using System.Collections.Generic;
using UnityEngine;

namespace EverRealm.World
{
    /// <summary>
    /// Pushes the odd stretch of a tall sheer wall out over the drop as a wedge of rock: level with the land
    /// behind it, thick at the wall and thin at the lip. Rare by design: most cliffs just stay cliffs.
    /// </summary>
    public static class OverhangBuilder
    {
        // Rim points are scanned on a grid, so each must reach its neighbours' lips.
        const int ThinnestLip = 3;
        const float ThickestShare = 0.55f;

        /// <summary>Adds the overhangs and returns how many columns of rock jut out.</summary>
        public static int Build(WorldData world, WorldSettings settings, TerrainGenerator terrain, int[] tops, IReadOnlyList<CliffRim> rims, IReadOnlyList<Settlement> settlements)
        {
            int size = world.SizeInBlocks;
            var lipTop = new int[size * size];
            var lipBottom = new int[size * size];
            Array.Fill(lipTop, -1);
            Array.Fill(lipBottom, int.MaxValue);

            var rng = new System.Random(settings.seed * 13 + 5);
            float offsetX = (float)rng.NextDouble() * 1000f, offsetZ = (float)rng.NextDouble() * 1000f;
            float blockSize = world.BlockSize, widest = settings.overhangReach / blockSize;

            foreach (CliffRim rim in rims)
            {
                if (rim.Drop * blockSize < settings.overhangMinDrop) continue;
                float x = rim.X * blockSize / settings.overhangPatchScale + offsetX, z = rim.Z * blockSize / settings.overhangPatchScale + offsetZ;
                float reach = widest * Mathf.InverseLerp(settings.overhangLine, settings.overhangLine + 0.15f, Mathf.PerlinNoise(x, z));
                reach *= terrain.BiomeAt(rim.X * blockSize, rim.Z * blockSize).overhangs;
                if (reach >= ThinnestLip) Reach(world, tops, lipTop, lipBottom, rim, reach);
            }

            int columns = 0;
            var stamp = new RockStamp(world, settings, tops);
            for (int index = 0; index < lipTop.Length; index++)
            {
                if (lipTop[index] < 0) continue;
                int x = index % size, z = index / size;
                if (IsNearHamlet(world, settings, settlements, x, z) || RockStamp.CrowdsBuilding(world, tops, x, lipBottom[index], z)) continue;
                for (int y = Mathf.Max(lipBottom[index], tops[index] + 1); y <= lipTop[index]; y++) stamp.Add(x, y, z);
                columns++;
            }
            stamp.Finish();
            return columns;
        }

        /// <summary>Marks the half-disc beyond one rim point. A column takes one lip, the highest: joining lips from two tiers of a cliff would make a solid wall between them.</summary>
        static void Reach(WorldData world, int[] tops, int[] lipTop, int[] lipBottom, in CliffRim rim, float reach)
        {
            int size = world.SizeInBlocks, span = Mathf.CeilToInt(reach);
            float thickest = Mathf.Min(reach * 1.1f, rim.Drop * ThickestShare);
            for (int dz = -span; dz <= span; dz++)
            for (int dx = -span; dx <= span; dx++)
            {
                int x = rim.X + dx, z = rim.Z + dz;
                if (x < 0 || z < 0 || x >= size || z >= size) continue;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                if (distance > reach || dx * rim.Out.x + dz * rim.Out.y <= 0f) continue;

                int index = x + z * size;
                if (tops[index] >= rim.Top - 2) continue;
                float thickness = Mathf.Lerp(thickest, 1.5f, distance / reach) + ChunkMesher.Hash(x, 7, z) % 3 * 0.5f;
                if (rim.Top < lipTop[index]) continue;
                int bottom = rim.Top - Mathf.RoundToInt(thickness);
                lipBottom[index] = rim.Top > lipTop[index] ? bottom : Mathf.Min(lipBottom[index], bottom);
                lipTop[index] = rim.Top;
            }
        }

        static bool IsNearHamlet(WorldData world, WorldSettings settings, IReadOnlyList<Settlement> settlements, int x, int z)
        {
            var point = new Vector2(x, z) * world.BlockSize;
            foreach (Settlement settlement in settlements)
                if (Vector2.Distance(point, new Vector2(settlement.Square.x, settlement.Square.z)) < settlement.Radius + settings.hamletBlend + 6f) return true;
            return false;
        }
    }
}
