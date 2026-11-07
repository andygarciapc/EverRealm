using System;
using System.Collections.Generic;
using UnityEngine;

namespace EverRealm.World
{
    /// <summary>
    /// Raises freestanding eroded rock: clusters of pillars and lone mesas. Each is built layer by layer from a
    /// silhouette, with uneven strata, a lumpy outline, a lean and a skirt of fallen rock at its foot.
    /// </summary>
    public static class FormationBuilder
    {
        const int Attempts = 5000, LayerBlocks = 5, SkirtBlocks = 5;

        /// <summary>Radius at each height, as a share of the formation's radius. 0 is the foot, 1 the top.</summary>
        delegate float Silhouette(float height);

        // A pillar's possible outlines.
        static readonly Silhouette[] Pillars = { Hoodoo, Spire, Tower, Anvil };

        /// <summary>A narrow neck under a wide, rounded cap.</summary>
        static float Hoodoo(float h) =>
            h < 0.62f ? Mathf.Lerp(1f, 0.36f, h / 0.62f)
            : h < 0.8f ? Mathf.Lerp(0.36f, 1.2f, (h - 0.62f) / 0.18f)
            : 1.2f * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((h - 0.8f) / 0.2f, 2f)));

        /// <summary>Tapers to a point, with a pinch part way up.</summary>
        static float Spire(float h) => (1f - Mathf.Pow(h, 0.8f)) * (h > 0.4f && h < 0.55f ? 0.72f : 1f) + 0.05f;

        /// <summary>Straight-sided, waisted, broken off at the top.</summary>
        static float Tower(float h) => h < 0.5f ? Mathf.Lerp(1f, 0.68f, h / 0.5f) : Mathf.Lerp(0.68f, 0.9f, (h - 0.5f) / 0.5f);

        /// <summary>Thin stem, flat top far wider than its base.</summary>
        static float Anvil(float h) => h < 0.75f ? Mathf.Lerp(0.8f, 0.42f, h / 0.75f) : Mathf.Lerp(0.42f, 1.5f, (h - 0.75f) / 0.25f);

        /// <summary>Sheer walls, pinched under a flat caprock.</summary>
        static float Mesa(float h) => h < 0.8f ? 1f - 0.12f * h : h < 0.88f ? 0.8f : 0.98f;

        /// <summary>Raises the island's formations and returns how many it got.</summary>
        public static int Build(WorldData world, WorldSettings settings, TerrainGenerator terrain, int[] tops, IReadOnlyList<Settlement> settlements)
        {
            var rng = new System.Random(settings.seed * 41 + 9);
            var built = new List<Vector2>();
            float blockSize = world.BlockSize, shore = settings.seaLevel + settings.beachHeight + 1f;
            int size = world.SizeInBlocks;

            for (int attempt = 0; attempt < Attempts && built.Count < settings.formationCount; attempt++)
            {
                var centre = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble()) * size;
                if (rng.NextDouble() >= terrain.BiomeAt(centre.x * blockSize, centre.y * blockSize).formationChance) continue;
                if (built.Exists(other => Vector2.Distance(other, centre) * blockSize < settings.formationSpacing)) continue;
                if ((Top(world, tops, centre) + 1) * blockSize <= shore) continue;

                bool raised = rng.NextDouble() < settings.mesaShare
                    ? TryMesa(world, settings, rng, tops, settlements, centre)
                    : TryCluster(world, settings, rng, tops, settlements, centre);
                if (raised) built.Add(centre);
            }
            return built.Count;
        }

        static bool TryMesa(WorldData world, WorldSettings settings, System.Random rng, int[] tops, IReadOnlyList<Settlement> settlements, Vector2 centre)
        {
            float blockSize = world.BlockSize;
            float radius = Mathf.Lerp(settings.mesaMinRadius, settings.mesaMaxRadius, (float)rng.NextDouble()) / blockSize;
            if (!RockStamp.IsClear(world, settings, tops, settlements, centre, radius * 1.7f) || !IsLevelEnough(world, tops, centre, radius)) return false;

            int height = Mathf.RoundToInt(Mathf.Lerp(settings.pillarMinHeight * 1.4f, settings.pillarMaxHeight * 0.85f, (float)rng.NextDouble()) / blockSize);
            Raise(world, settings, rng, tops, Mesa, centre, height, radius, Vector2.zero, Vector2.zero, flatTop: true);
            RockStamp.Scree(world, tops, centre, radius * 2.2f, rng.Next());
            return true;
        }

        static bool TryCluster(WorldData world, WorldSettings settings, System.Random rng, int[] tops, IReadOnlyList<Settlement> settlements, Vector2 centre)
        {
            float blockSize = world.BlockSize, shore = settings.seaLevel + settings.beachHeight + 1f;
            int wanted = rng.Next(1, 6), raised = 0;
            float spread = 14f / blockSize;
            for (int i = 0; i < wanted; i++)
            {
                Vector2 spot = centre + (i == 0 ? Vector2.zero : RandomDirection(rng) * (spread * (0.5f + (float)rng.NextDouble())));
                float radius = Mathf.Lerp(settings.pillarMinRadius, settings.pillarMaxRadius, (float)rng.NextDouble()) / blockSize;
                if ((Top(world, tops, spot) + 1) * blockSize <= shore) continue;
                if (!RockStamp.IsClear(world, settings, tops, settlements, spot, radius * 2.4f) || !IsLevelEnough(world, tops, spot, radius)) continue;

                int height = Mathf.RoundToInt(Mathf.Lerp(settings.pillarMinHeight, settings.pillarMaxHeight, Mathf.Pow((float)rng.NextDouble(), 1.4f)) / blockSize);
                Silhouette outline = Pillars[rng.Next(Pillars.Length)];
                Vector2 lean = RandomDirection(rng) * ((float)rng.NextDouble() * 0.22f);
                // Some caps sit off to one side of their stem, like a balancing rock.
                Vector2 capShift = outline == Hoodoo || outline == Anvil ? RandomDirection(rng) * (radius * (float)rng.NextDouble() * 0.8f) : Vector2.zero;
                Raise(world, settings, rng, tops, outline, spot, height, radius, lean, capShift, flatTop: outline == Anvil);
                RockStamp.Scree(world, tops, spot, radius * 2.6f, rng.Next());
                raised++;
            }
            return raised > 0;
        }

        /// <summary>Formations are bare or grassy, so they stop short of the snow line.</summary>
        static int BelowSnow(WorldData world, WorldSettings settings, int[] tops, Vector2 centre, int height) =>
            Mathf.Min(height, Mathf.FloorToInt(settings.snowLine / world.BlockSize) - 3 - Top(world, tops, centre));

        /// <summary>Builds one formation layer by layer, from below the lowest ground under it up to its top.</summary>
        static void Raise(WorldData world, WorldSettings settings, System.Random rng, int[] tops, Silhouette outline, Vector2 centre,
                          int height, float radius, Vector2 lean, Vector2 capShift, bool flatTop)
        {
            height = BelowSnow(world, settings, tops, centre, height);
            if (height < 8) return;
            int ground = Top(world, tops, centre), bottom = ground;
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI / 6f;
                bottom = Mathf.Min(bottom, Top(world, tops, centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius));
            }

            var stamp = new RockStamp(world, settings, tops);
            float lobeX = (float)rng.NextDouble() * 200f, lobeZ = (float)rng.NextDouble() * 200f;
            int layerSeed = rng.Next();
            for (int y = bottom - 1; y <= ground + height; y++)
            {
                float share = Mathf.Clamp01((y - ground) / (float)height);
                float strata = Mathf.Lerp(0.86f, 1.12f, ChunkMesher.Hash(y / LayerBlocks, layerSeed, 3) % 100 / 99f);
                float skirt = 1f + 1.1f * Mathf.Exp(-Mathf.Max(0, y - ground) / (float)SkirtBlocks);
                float layer = radius * outline(share) * strata * skirt;
                // Necks stay thick enough to hold what's above them, and a tip that thins out just ends.
                if (share < 0.9f) layer = Mathf.Max(layer, 2.5f);
                else if (layer < 1.5f) break;

                Vector2 middle = centre + lean * (y - ground) + capShift * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.85f, share));
                int reach = Mathf.CeilToInt(layer * 1.35f);
                int mx = Mathf.RoundToInt(middle.x), mz = Mathf.RoundToInt(middle.y);
                for (int dz = -reach; dz <= reach; dz++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    float angle = Mathf.Atan2(dz, dx);
                    // Sampled on a circle, so the outline joins up with itself.
                    float lobes = Mathf.PerlinNoise(Mathf.Cos(angle) * 1.4f + lobeX + y * 0.015f, Mathf.Sin(angle) * 1.4f + lobeZ);
                    if (Mathf.Sqrt(dx * dx + dz * dz) <= layer * (0.65f + 0.7f * lobes)) stamp.Add(mx + dx, y, mz + dz);
                }
            }
            stamp.Finish();
        }

        static bool IsLevelEnough(WorldData world, int[] tops, Vector2 centre, float radius)
        {
            int ground = Top(world, tops, centre);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4f;
                if (Mathf.Abs(Top(world, tops, centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius) - ground) > radius * 0.8f) return false;
            }
            return true;
        }

        static int Top(WorldData world, int[] tops, Vector2 point)
        {
            int size = world.SizeInBlocks;
            return tops[Mathf.Clamp(Mathf.RoundToInt(point.x), 0, size - 1) + Mathf.Clamp(Mathf.RoundToInt(point.y), 0, size - 1) * size];
        }

        static Vector2 RandomDirection(System.Random rng)
        {
            double angle = rng.NextDouble() * Math.PI * 2;
            return new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
        }
    }
}
