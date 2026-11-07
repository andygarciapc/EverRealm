using System;
using System.Threading.Tasks;
using UnityEngine;

namespace EverRealm.World
{
    /// <summary>
    /// Deterministic island generator. The island is divided into regions, each one biome with its own
    /// landform and colours, and neighbours blend into each other. Heights are computed in metres from
    /// world-space noise and only quantised to blocks at the end, so block size changes resolution, not the
    /// landscape.
    /// </summary>
    public sealed class TerrainGenerator
    {
        // Mathf.PerlinNoise loses precision with large inputs, so seed offsets stay small.
        const float MaxOffset = 5000f;

        struct Region
        {
            public Vector2 Centre;
            public int Biome;
        }

        readonly WorldSettings settings;
        readonly Biome[] biomes;
        readonly Region[] regions;
        readonly Vector2[] hillOffsets, ridgeOffsets, plateauOffsets;
        readonly Vector2 coastOffset, warpOffset, patchOffset, breakOffset;

        public TerrainGenerator(WorldSettings settings)
        {
            this.settings = settings;
            biomes = settings.biomes;
            var rng = new System.Random(settings.seed);
            hillOffsets = RandomOffsets(rng, settings.octaves);
            ridgeOffsets = RandomOffsets(rng, settings.octaves);
            plateauOffsets = RandomOffsets(rng, settings.octaves);
            coastOffset = RandomOffset(rng);
            warpOffset = RandomOffset(rng);
            patchOffset = RandomOffset(rng);
            breakOffset = RandomOffset(rng);
            regions = PlaceRegions(rng);
        }

        static Vector2[] RandomOffsets(System.Random rng, int count)
        {
            var offsets = new Vector2[count];
            for (int i = 0; i < count; i++) offsets[i] = RandomOffset(rng);
            return offsets;
        }

        static Vector2 RandomOffset(System.Random rng) =>
            new Vector2((float)(rng.NextDouble() * 2 - 1) * MaxOffset, (float)(rng.NextDouble() * 2 - 1) * MaxOffset);

        /// <summary>
        /// A jittered grid of regions. Biomes are dealt out from the centre, whole shuffled decks at a time, so
        /// the land near the middle of the island gets one of everything before any repeat.
        /// </summary>
        Region[] PlaceRegions(System.Random rng)
        {
            int perSide = Mathf.Max(1, Mathf.RoundToInt(settings.islandSize / settings.regionSize));
            float cell = settings.islandSize / perSide;
            var placed = new Region[perSide * perSide];
            for (int z = 0; z < perSide; z++)
            for (int x = 0; x < perSide; x++)
                placed[x + z * perSide].Centre = new Vector2((x + 0.2f + (float)rng.NextDouble() * 0.6f) * cell,
                                                             (z + 0.2f + (float)rng.NextDouble() * 0.6f) * cell);

            var middle = Vector2.one * (settings.islandSize * 0.5f);
            Array.Sort(placed, (a, b) => (a.Centre - middle).sqrMagnitude.CompareTo((b.Centre - middle).sqrMagnitude));

            var deck = new int[biomes.Length];
            for (int i = 0; i < deck.Length; i++) deck[i] = i;
            for (int i = 0; i < placed.Length; i++)
            {
                int dealt = i % deck.Length;
                if (dealt == 0)
                    for (int k = deck.Length - 1; k > 0; k--)
                    {
                        int swap = rng.Next(k + 1);
                        (deck[k], deck[swap]) = (deck[swap], deck[k]);
                    }
                placed[i].Biome = deck[dealt];
            }
            return placed;
        }

        // ---- Land ----

        /// <summary>Terrain surface height in metres at world position (x, z).</summary>
        public float HeightAt(float x, float z)
        {
            Span<float> weights = stackalloc float[biomes.Length];
            BiomeWeights(x, z, weights);
            return HeightAt(x, z, weights);
        }

        /// <summary>The biome that mostly covers world position (x, z).</summary>
        public Biome BiomeAt(float x, float z)
        {
            Span<float> weights = stackalloc float[biomes.Length];
            BiomeWeights(x, z, weights);
            int best = 0;
            for (int i = 1; i < weights.Length; i++)
                if (weights[i] > weights[best]) best = i;
            return biomes[best];
        }

        float HeightAt(float x, float z, ReadOnlySpan<float> weights)
        {
            float hills = Fbm(hillOffsets, settings.noiseScale, x, z);
            float ridges = Ridges(x, z);
            float plateau = Plateau(x, z);
            // Cliff bands give way in places: those gullies are the ways up.
            float cliffs = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(settings.gullyShare - 0.05f, settings.gullyShare + 0.05f,
                Mathf.PerlinNoise(x / settings.cliffBreakScale + breakOffset.x, z / settings.cliffBreakScale + breakOffset.y)));
            float land = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0f) continue;
                Biome biome = biomes[i];
                float height = biome.baseHeight + biome.relief * Mathf.Lerp(hills, ridges, biome.ridged);
                if (biome.terrace > 0f) height = Mathf.Lerp(height, Terrace(height, biome.terrace), cliffs);
                land += weights[i] * (height + biome.plateauHeight * plateau);
            }
            return Mathf.Lerp(settings.seaFloor, settings.seaLevel + 1f + land, LandShare(x, z));
        }

        /// <summary>How much of (x, z) is island rather than sea, 0..1.</summary>
        float LandShare(float x, float z)
        {
            float half = settings.islandSize * 0.5f;
            float dx = (x - half) / half, dz = (z - half) / half;

            // Warp the distance with low-frequency noise so the coast isn't a circle.
            float warp = Mathf.PerlinNoise(x / settings.noiseScale * 0.6f + coastOffset.x,
                                           z / settings.noiseScale * 0.6f + coastOffset.y) - 0.5f;
            float distance = Mathf.Sqrt(dx * dx + dz * dz) + warp * settings.coastWarp * 2f;
            float land01 = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(settings.falloffStart, 1f, distance));
            // Guarantee open sea along the square border, whatever the warp does.
            return land01 * Mathf.InverseLerp(1f, 0.9f, Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)));
        }

        /// <summary>Each biome's share of (x, z), summing to 1. Region borders are warped so they don't read as lines.</summary>
        void BiomeWeights(float x, float z, Span<float> weights)
        {
            float scale = settings.regionWarp * 2f;
            float wx = x + (Mathf.PerlinNoise(x / scale + warpOffset.x, z / scale + warpOffset.y) - 0.5f) * settings.regionWarp * 2f;
            float wz = z + (Mathf.PerlinNoise(x / scale + warpOffset.y, z / scale + warpOffset.x) - 0.5f) * settings.regionWarp * 2f;

            float nearest = float.PositiveInfinity;
            foreach (Region region in regions)
                nearest = Mathf.Min(nearest, Distance(region, wx, wz));

            weights.Clear();
            float total = 0f;
            foreach (Region region in regions)
            {
                float share = 1f - (Distance(region, wx, wz) - nearest) / settings.regionBlend;
                if (share <= 0f) continue;
                share *= share;
                weights[region.Biome] += share;
                total += share;
            }
            for (int i = 0; i < weights.Length; i++) weights[i] /= total;
        }

        static float Distance(in Region region, float x, float z)
        {
            float dx = region.Centre.x - x, dz = region.Centre.y - z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Fractal noise remapped to roughly 0..1.</summary>
        float Fbm(Vector2[] offsets, float scale, float x, float z)
        {
            float sum = 0f, amplitude = 1f, norm = 0f, frequency = 1f / scale;
            foreach (Vector2 offset in offsets)
            {
                sum += amplitude * Mathf.PerlinNoise(x * frequency + offset.x, z * frequency + offset.y);
                norm += amplitude;
                amplitude *= settings.persistence;
                frequency *= settings.lacunarity;
            }
            // Summed octaves cluster around 0.5; stretch so the full height range gets used.
            return Mathf.Clamp01(Mathf.InverseLerp(0.28f, 0.72f, sum / norm));
        }

        /// <summary>Noise folded along its midline into sharp crests, 0..1.</summary>
        float Ridges(float x, float z)
        {
            float crest = 1f - Mathf.Abs(Fbm(ridgeOffsets, settings.ridgeScale, x, z) * 2f - 1f);
            return crest * Mathf.Sqrt(crest);
        }

        /// <summary>0 on open land, rising through two sheer steps to 1 on the highest tier of a plateau.</summary>
        float Plateau(float x, float z)
        {
            float field = Fbm(plateauOffsets, settings.plateauScale, x, z), line = settings.plateauLine;
            return Sheer(field, line) * 0.6f + Sheer(field, line + 0.1f) * 0.4f;
        }

        static float Sheer(float field, float line) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(line, line + 0.015f, field));

        /// <summary>Wide flat treads with short steep risers between them.</summary>
        static float Terrace(float height, float step)
        {
            float steps = height / step, floor = Mathf.Floor(steps);
            return (floor + Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.82f, 1f, steps - floor))) * step;
        }

        // ---- Colour ----

        ColumnPaint PaintAt(float x, float z, ReadOnlySpan<float> weights)
        {
            float scale = settings.colorPatchScale;
            float patch = Mathf.PerlinNoise(x / scale + patchOffset.x, z / scale + patchOffset.y) * 0.7f +
                          Mathf.PerlinNoise(x / scale * 3.1f + patchOffset.y, z / scale * 3.1f + patchOffset.x) * 0.3f;
            patch = Mathf.Clamp01(Mathf.InverseLerp(0.3f, 0.7f, patch));

            Color ground = Color.clear, foliage = Color.clear, rock = Color.clear, rockAlt = Color.clear;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0f) continue;
                Biome biome = biomes[i];
                ground += Color.Lerp(biome.ground, biome.groundAlt, patch) * weights[i];
                foliage += Color.Lerp(biome.foliage, biome.foliageAlt, patch) * weights[i];
                rock += biome.rock * weights[i];
                rockAlt += biome.rockAlt * weights[i];
            }
            return new ColumnPaint { Ground = ground, Foliage = foliage, Rock = rock, RockAlt = rockAlt };
        }

        // ---- Blocks ----

        public WorldData Generate()
        {
            float blockSize = settings.blockSize;
            int chunksPerSide = Mathf.CeilToInt(settings.islandSize / (settings.chunkSize * blockSize));
            int height = Mathf.CeilToInt(settings.maxHeight / blockSize) + 1;
            var world = new WorldData(settings.chunkSize, chunksPerSide, height, blockSize);

            // Cliffs are found from neighbouring heights, so every column's height comes first.
            int size = world.SizeInBlocks;
            var heights = new float[size * size];
            Parallel.For(0, size, bz =>
            {
                Span<float> weights = stackalloc float[biomes.Length];
                for (int bx = 0; bx < size; bx++)
                {
                    float x = (bx + 0.5f) * blockSize, z = (bz + 0.5f) * blockSize;
                    BiomeWeights(x, z, weights);
                    heights[bx + bz * size] = HeightAt(x, z, weights);
                    world.Paint(bx, bz) = PaintAt(x, z, weights);
                }
            });

            Parallel.For(0, chunksPerSide * chunksPerSide, chunk =>
                FillChunk(world, heights, chunk % chunksPerSide, chunk / chunksPerSide));
            return world;
        }

        void FillChunk(WorldData world, float[] heights, int cx, int cz)
        {
            int chunkSize = world.ChunkSize, size = world.SizeInBlocks;
            float blockSize = world.BlockSize;
            float tallest = 0f;
            for (int lz = 0; lz < chunkSize; lz++)
            for (int lx = 0; lx < chunkSize; lx++)
                tallest = Mathf.Max(tallest, heights[cx * chunkSize + lx + (cz * chunkSize + lz) * size]);
            world.Reserve(cx, cz, Mathf.FloorToInt(tallest / blockSize) + 1);

            byte[] blocks = world.GetChunk(cx, cz);
            int dirtBlocks = Mathf.Max(1, Mathf.RoundToInt(settings.dirtDepth / blockSize));
            float beachTop = settings.seaLevel + settings.beachHeight;
            int chunkTop = 0;

            for (int lz = 0; lz < chunkSize; lz++)
            for (int lx = 0; lx < chunkSize; lx++)
            {
                int bx = cx * chunkSize + lx, bz = cz * chunkSize + lz;
                float surface = heights[bx + bz * size];
                int top = Mathf.Clamp(Mathf.FloorToInt(surface / blockSize), 0, world.Height - 1);
                float topMeters = (top + 1) * blockSize;

                // A cliff face is bare rock. Its rim keeps its grass, with rock showing below.
                Neighbours(heights, size, bx, bz, out float lowest, out float highest);
                bool dropsAway = surface - lowest >= settings.cliffRise;
                bool cliffFace = dropsAway && highest - surface >= blockSize;

                BlockType surfaceBlock, subsurfaceBlock;
                if (topMeters <= beachTop) surfaceBlock = subsurfaceBlock = BlockType.Sand;
                else if (cliffFace) surfaceBlock = subsurfaceBlock = BlockType.Stone;
                else if (topMeters >= settings.snowLine) { surfaceBlock = BlockType.Snow; subsurfaceBlock = BlockType.Stone; }
                else { surfaceBlock = BlockType.Grass; subsurfaceBlock = dropsAway ? BlockType.Stone : BlockType.Dirt; }

                for (int y = 0; y <= top; y++)
                {
                    BlockType block = y == top ? surfaceBlock
                                    : y >= top - dirtBlocks ? subsurfaceBlock
                                    : BlockType.Stone;
                    blocks[world.LocalIndex(lx, y, lz)] = (byte)block;
                }
                if (top + 1 > chunkTop) chunkTop = top + 1;
            }
            world.SetChunkTop(cx, cz, chunkTop);
        }

        /// <summary>The lowest and highest ground around column (x, z), counting the column itself.</summary>
        static void Neighbours(float[] heights, int size, int x, int z, out float lowest, out float highest)
        {
            lowest = highest = heights[x + z * size];
            if (x > 0) Include(heights[x - 1 + z * size], ref lowest, ref highest);
            if (x < size - 1) Include(heights[x + 1 + z * size], ref lowest, ref highest);
            if (z > 0) Include(heights[x + (z - 1) * size], ref lowest, ref highest);
            if (z < size - 1) Include(heights[x + (z + 1) * size], ref lowest, ref highest);
        }

        static void Include(float height, ref float lowest, ref float highest)
        {
            lowest = Mathf.Min(lowest, height);
            highest = Mathf.Max(highest, height);
        }
    }
}
