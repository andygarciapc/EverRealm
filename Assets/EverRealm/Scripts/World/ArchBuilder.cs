using System.Collections.Generic;
using UnityEngine;

namespace EverRealm.World
{
    /// <summary>
    /// Natural arches, formed the way real ones are: a fin of rock runs out from a cliff, and a window has been
    /// eroded through it. The land behind the cliff carries straight on over the bridge, so an arch always
    /// belongs to the rock around it. Rare by design.
    /// </summary>
    public static class ArchBuilder
    {
        const int Attempts = 4000;
        // In blocks: the least room to walk under, and the slope of a fin's rounded back.
        const int LeastOpening = 9, BackDrop = 3;
        // Share of the fin taken up by its rounded far end.
        const float EndShare = 0.15f;

        /// <summary>Raises the island's arches from its cliffs and returns how many it got.</summary>
        public static int Build(WorldData world, WorldSettings settings, TerrainGenerator terrain, int[] tops, IReadOnlyList<CliffRim> rims, IReadOnlyList<Settlement> settlements)
        {
            var built = new List<Vector2>();
            if (rims.Count == 0) return 0;
            var rng = new System.Random(settings.seed * 31 + 7);
            float blockSize = world.BlockSize;
            for (int attempt = 0; attempt < Attempts && built.Count < settings.archCount; attempt++)
            {
                CliffRim rim = rims[rng.Next(rims.Count)];
                var place = new Vector2(rim.X, rim.Z);
                if (rng.NextDouble() >= terrain.BiomeAt(rim.X * blockSize, rim.Z * blockSize).archChance) continue;
                if (built.Exists(other => Vector2.Distance(other, place) * blockSize < settings.archSpacing)) continue;
                if (TryRaise(world, settings, rng, tops, settlements, rim)) built.Add(place);
            }
            return built.Count;
        }

        static bool TryRaise(WorldData world, WorldSettings settings, System.Random rng, int[] tops, IReadOnlyList<Settlement> settlements, in CliffRim rim)
        {
            float blockSize = world.BlockSize;
            float length = Mathf.Lerp(settings.archMinLength, settings.archMaxLength, (float)rng.NextDouble()) / blockSize;
            float halfWidth = Mathf.Lerp(4f, 7.5f, (float)rng.NextDouble());
            int bridge = rng.Next(4, 8);
            var start = new Vector2(rim.X, rim.Z);
            var across = new Vector2(-rim.Out.y, rim.Out.x);
            int size = world.SizeInBlocks;

            // The opening needs clear low ground beyond the cliff, all the way out.
            int floor = int.MinValue;
            for (float s = length * 0.2f; s <= length; s += 2f)
            {
                Vector2 point = start + rim.Out * s;
                int x = Mathf.RoundToInt(point.x), z = Mathf.RoundToInt(point.y);
                if (x < 0 || z < 0 || x >= size || z >= size) return false;
                int top = tops[x + z * size];
                if ((top + 1) * blockSize <= settings.seaLevel + settings.beachHeight) return false;
                floor = Mathf.Max(floor, top);
            }
            int roof = rim.Top - bridge;
            if (roof - floor < LeastOpening) return false;
            if (!RockStamp.IsClear(world, settings, tops, settlements, start + rim.Out * (length * 0.5f), length * 0.5f + halfWidth * 2f)) return false;

            float sag = (roof - floor - LeastOpening) * 0.35f * (float)rng.NextDouble();
            float bend = (float)(rng.NextDouble() * 2 - 1) * length * 0.12f;
            float windowCentre = length * Mathf.Lerp(0.45f, 0.55f, (float)rng.NextDouble()), windowHalf = length * Mathf.Lerp(0.26f, 0.36f, (float)rng.NextDouble());
            float seed = (float)rng.NextDouble() * 100f;

            var stamp = new RockStamp(world, settings, tops);
            for (float s = -4f; s <= length; s += 0.5f)
            {
                float along = Mathf.Max(0f, s) / length;
                Vector2 spine = start + rim.Out * s + across * (Mathf.Sin(along * Mathf.PI) * bend);
                float width = halfWidth * Mathf.Lerp(0.8f, 1.3f, Mathf.PerlinNoise(s * 0.07f, seed));
                float back = rim.Top - sag * along * along;

                // The far end rounds down into the buttress the bridge stands on.
                float end = Mathf.InverseLerp(1f - EndShare, 1f, along);
                back -= end * end * (rim.Top - floor) * 0.5f;
                width *= 1f - 0.4f * end * end;

                for (float a = -width * 2f; a <= width * 2f; a += 0.5f)
                {
                    Vector2 point = spine + across * a;
                    int x = Mathf.RoundToInt(point.x), z = Mathf.RoundToInt(point.y);
                    if (x < 0 || z < 0 || x >= size || z >= size) continue;
                    float side = a / width;
                    int top = Mathf.FloorToInt(back - side * side * BackDrop);
                    for (int y = tops[x + z * size] - 1; y <= top; y++)
                    {
                        // Flares out towards the ground, so the rock rises from the land.
                        float flare = 1f + 1f * Mathf.Exp(-Mathf.Max(0, y - floor) / 5f);
                        if (Mathf.Abs(side) > flare) continue;
                        if (InWindow(s, y, windowCentre, windowHalf, floor, roof - sag * 0.5f, seed)) continue;
                        stamp.Add(x, y, z);
                    }
                }
            }
            stamp.Finish();
            RockStamp.Scree(world, tops, start + rim.Out * length, halfWidth * 3.5f, rng.Next());
            return true;
        }

        /// <summary>A rough half-ellipse standing on the ground, as if worn out from below.</summary>
        static bool InWindow(float along, int y, float centre, float half, int floor, float roof, float seed)
        {
            float across = (along - centre) / half, up = (y - floor) / (roof - floor);
            float rough = Mathf.Lerp(0.85f, 1.1f, Mathf.PerlinNoise(along * 0.15f + seed, y * 0.15f));
            return across * across + up * up < rough * rough;
        }
    }
}
