using UnityEngine;

namespace EverRealm.World
{
    /// <summary>
    /// Deterministic island generator. Heights are computed in metres from world-space noise and
    /// only quantised to blocks at the end, so block size changes resolution, not the landscape.
    /// </summary>
    public sealed class TerrainGenerator
    {
        // Mathf.PerlinNoise loses precision with large inputs, so seed offsets stay small.
        const float MaxOffset = 5000f;

        readonly WorldSettings settings;
        readonly Vector2[] octaveOffsets;
        readonly Vector2 coastOffset;

        public TerrainGenerator(WorldSettings settings)
        {
            this.settings = settings;
            var rng = new System.Random(settings.seed);
            octaveOffsets = new Vector2[settings.octaves];
            for (int i = 0; i < octaveOffsets.Length; i++)
                octaveOffsets[i] = RandomOffset(rng);
            coastOffset = RandomOffset(rng);
        }

        static Vector2 RandomOffset(System.Random rng) =>
            new Vector2((float)(rng.NextDouble() * 2 - 1) * MaxOffset, (float)(rng.NextDouble() * 2 - 1) * MaxOffset);

        /// <summary>Terrain surface height in metres at world position (x, z).</summary>
        public float HeightAt(float x, float z)
        {
            float half = settings.islandSize * 0.5f;
            float dx = (x - half) / half, dz = (z - half) / half;

            // Warp the distance with low-frequency noise so the coast isn't a circle.
            float warp = Mathf.PerlinNoise(x / settings.noiseScale * 0.5f + coastOffset.x,
                                           z / settings.noiseScale * 0.5f + coastOffset.y) - 0.5f;
            float distance = Mathf.Sqrt(dx * dx + dz * dz) + warp * settings.coastWarp * 2f;
            float land01 = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(settings.falloffStart, 1f, distance));
            // Guarantee open sea along the square border, whatever the warp does.
            land01 *= Mathf.InverseLerp(1f, 0.9f, Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)));

            float elevation = Mathf.Pow(Fbm(x, z), settings.heightExponent);
            float landHeight = settings.seaLevel + 1f + elevation * (settings.maxHeight - settings.seaLevel - 1f);
            return Mathf.Lerp(settings.seaFloor, landHeight, land01);
        }

        /// <summary>Fractal noise remapped to roughly 0..1.</summary>
        float Fbm(float x, float z)
        {
            float sum = 0f, amplitude = 1f, norm = 0f, frequency = 1f / settings.noiseScale;
            for (int i = 0; i < octaveOffsets.Length; i++)
            {
                sum += amplitude * Mathf.PerlinNoise(x * frequency + octaveOffsets[i].x, z * frequency + octaveOffsets[i].y);
                norm += amplitude;
                amplitude *= settings.persistence;
                frequency *= settings.lacunarity;
            }
            // Summed octaves cluster around 0.5; stretch so the full height range gets used.
            return Mathf.Clamp01(Mathf.InverseLerp(0.3f, 0.7f, sum / norm));
        }

        public WorldData Generate()
        {
            float blockSize = settings.blockSize;
            int chunksPerSide = Mathf.CeilToInt(settings.islandSize / (settings.chunkSize * blockSize));
            int height = Mathf.CeilToInt(settings.maxHeight / blockSize) + 1;
            var world = new WorldData(settings.chunkSize, chunksPerSide, height, blockSize);

            for (int cz = 0; cz < chunksPerSide; cz++)
            for (int cx = 0; cx < chunksPerSide; cx++)
                GenerateChunk(world, cx, cz);
            return world;
        }

        void GenerateChunk(WorldData world, int cx, int cz)
        {
            int size = world.ChunkSize;
            float blockSize = world.BlockSize;
            byte[] blocks = world.GetChunk(cx, cz);
            int dirtBlocks = Mathf.Max(1, Mathf.RoundToInt(settings.dirtDepth / blockSize));
            float beachTop = settings.seaLevel + settings.beachHeight;
            float rockHeight = settings.seaLevel + settings.rockLine * (settings.maxHeight - settings.seaLevel);
            int chunkTop = 0;

            for (int lz = 0; lz < size; lz++)
            for (int lx = 0; lx < size; lx++)
            {
                // Sample at the block column's centre, in metres.
                float wx = ((cx * size + lx) + 0.5f) * blockSize;
                float wz = ((cz * size + lz) + 0.5f) * blockSize;
                float surface = HeightAt(wx, wz);
                int top = Mathf.Clamp(Mathf.FloorToInt(surface / blockSize), 0, world.Height - 1);
                float topMeters = (top + 1) * blockSize;

                BlockType surfaceBlock, subsurfaceBlock;
                if (topMeters <= beachTop) { surfaceBlock = BlockType.Sand; subsurfaceBlock = BlockType.Sand; }
                else if (topMeters >= rockHeight) { surfaceBlock = BlockType.Stone; subsurfaceBlock = BlockType.Stone; }
                else { surfaceBlock = BlockType.Grass; subsurfaceBlock = BlockType.Dirt; }

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
    }
}
