namespace EverRealm.World
{
    /// <summary>
    /// Block storage for a bounded world, split into square chunks that span the full height.
    /// Block coordinates are global; chunk (0,0) starts at world origin.
    /// </summary>
    public sealed class WorldData
    {
        public readonly int ChunkSize;
        public readonly int ChunksPerSide;
        public readonly int Height;
        public readonly float BlockSize;

        readonly byte[][] chunks;
        readonly int[] chunkTopY;

        public WorldData(int chunkSize, int chunksPerSide, int height, float blockSize)
        {
            ChunkSize = chunkSize;
            ChunksPerSide = chunksPerSide;
            Height = height;
            BlockSize = blockSize;
            chunks = new byte[chunksPerSide * chunksPerSide][];
            chunkTopY = new int[chunksPerSide * chunksPerSide];
            for (int i = 0; i < chunks.Length; i++)
                chunks[i] = new byte[chunkSize * chunkSize * height];
        }

        public int SizeInBlocks => ChunkSize * ChunksPerSide;
        public float SizeInMeters => SizeInBlocks * BlockSize;

        /// <summary>Index into a chunk array; x is innermost so x-runs are contiguous.</summary>
        public int LocalIndex(int x, int y, int z) => x + ChunkSize * (z + ChunkSize * y);

        public byte[] GetChunk(int cx, int cz) => chunks[cx + cz * ChunksPerSide];

        /// <summary>One above the highest non-air block in the chunk, so meshing can skip empty sky.</summary>
        public int GetChunkTop(int cx, int cz) => chunkTopY[cx + cz * ChunksPerSide];
        public void SetChunkTop(int cx, int cz, int top) => chunkTopY[cx + cz * ChunksPerSide] = top;

        /// <summary>Block at global coordinates. Below the world is solid; outside it is air.</summary>
        public BlockType GetBlock(int x, int y, int z)
        {
            if (y < 0) return BlockType.Stone;
            if (y >= Height || x < 0 || z < 0) return BlockType.Air;
            int cx = x / ChunkSize, cz = z / ChunkSize;
            if (cx >= ChunksPerSide || cz >= ChunksPerSide) return BlockType.Air;
            return (BlockType)GetChunk(cx, cz)[LocalIndex(x - cx * ChunkSize, y, z - cz * ChunkSize)];
        }
    }
}
