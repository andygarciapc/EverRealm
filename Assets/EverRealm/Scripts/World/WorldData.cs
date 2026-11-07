using UnityEngine;

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
        // Extra layers taken when a chunk grows, so a tree doesn't resize it block by block.
        const int SpareLayers = 12;

        readonly byte[][] chunks;
        readonly int[] chunkTopY;
        readonly ColumnPaint[] paint;

        public WorldData(int chunkSize, int chunksPerSide, int height, float blockSize)
        {
            ChunkSize = chunkSize;
            ChunksPerSide = chunksPerSide;
            Height = height;
            BlockSize = blockSize;
            chunks = new byte[chunksPerSide * chunksPerSide][];
            chunkTopY = new int[chunksPerSide * chunksPerSide];
            paint = new ColumnPaint[chunkSize * chunksPerSide * chunkSize * chunksPerSide];
            for (int i = 0; i < chunks.Length; i++)
                chunks[i] = System.Array.Empty<byte>();
        }

        public int SizeInBlocks => ChunkSize * ChunksPerSide;
        public float SizeInMeters => SizeInBlocks * BlockSize;

        /// <summary>Makes room for <paramref name="layers"/> layers in a chunk. Chunks hold only the layers in use, not the empty sky.</summary>
        public void Reserve(int cx, int cz, int layers)
        {
            ref byte[] chunk = ref chunks[cx + cz * ChunksPerSide];
            int needed = ChunkSize * ChunkSize * Mathf.Min(layers, Height);
            if (chunk.Length < needed) System.Array.Resize(ref chunk, needed);
        }

        /// <summary>Index into a chunk array; x is innermost so x-runs are contiguous.</summary>
        public int LocalIndex(int x, int y, int z) => x + ChunkSize * (z + ChunkSize * y);

        public byte[] GetChunk(int cx, int cz) => chunks[cx + cz * ChunksPerSide];

        /// <summary>One above the highest non-air block in the chunk, so meshing can skip empty sky.</summary>
        public int GetChunkTop(int cx, int cz) => chunkTopY[cx + cz * ChunksPerSide];
        public void SetChunkTop(int cx, int cz, int top) => chunkTopY[cx + cz * ChunksPerSide] = top;

        /// <summary>Colours of block column (x, z), set by the terrain generator.</summary>
        public ref ColumnPaint Paint(int x, int z) => ref paint[x + z * SizeInBlocks];

        /// <summary>Block at global coordinates. Below the world is solid; outside it is air.</summary>
        public BlockType GetBlock(int x, int y, int z)
        {
            if (y < 0) return BlockType.Stone;
            if (y >= Height || x < 0 || z < 0) return BlockType.Air;
            int cx = x / ChunkSize, cz = z / ChunkSize;
            if (cx >= ChunksPerSide || cz >= ChunksPerSide) return BlockType.Air;
            byte[] chunk = GetChunk(cx, cz);
            int index = LocalIndex(x - cx * ChunkSize, y, z - cz * ChunkSize);
            return index < chunk.Length ? (BlockType)chunk[index] : BlockType.Air;
        }

        /// <summary>Sets a block at global coordinates (ignored outside the world), raising the chunk top if needed.</summary>
        public void SetBlock(int x, int y, int z, BlockType block)
        {
            if (y < 0 || y >= Height || x < 0 || z < 0 || x >= SizeInBlocks || z >= SizeInBlocks) return;
            int cx = x / ChunkSize, cz = z / ChunkSize;
            if (block != BlockType.Air) Reserve(cx, cz, y + 1 + SpareLayers);
            int index = LocalIndex(x - cx * ChunkSize, y, z - cz * ChunkSize);
            byte[] chunk = GetChunk(cx, cz);
            if (index < chunk.Length) chunk[index] = (byte)block;
            if (block != BlockType.Air && y + 1 > GetChunkTop(cx, cz)) SetChunkTop(cx, cz, y + 1);
        }
    }
}
