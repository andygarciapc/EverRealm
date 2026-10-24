using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EverRealm.World
{
    /// <summary>
    /// Builds one mesh per chunk, emitting only block faces that touch air. Colour comes from a
    /// palette texture (rows = block type, columns = brightness shade) sampled via UVs, so a stock
    /// URP Lit material renders it.
    /// </summary>
    public static class ChunkMesher
    {
        public const int ShadeCount = 8;
        static readonly int BlockTypeCount = System.Enum.GetValues(typeof(BlockType)).Length;

        static readonly Vector3Int[] FaceDirections =
        {
            Vector3Int.up, Vector3Int.down, Vector3Int.right, Vector3Int.left,
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
        };

        // Corners per face, ordered clockwise as seen from outside the block (Unity front faces).
        static readonly Vector3[][] FaceCorners =
        {
            new[] { new Vector3(0, 1, 0), new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0) }, // +Y
            new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1) }, // -Y
            new[] { new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1) }, // +X
            new[] { new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0) }, // -X
            new[] { new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1), new Vector3(0, 0, 1) }, // +Z
            new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) }, // -Z
        };

        // Reused across calls; meshing runs on the main thread only.
        static readonly List<Vector3> Vertices = new List<Vector3>();
        static readonly List<Vector3> Normals = new List<Vector3>();
        static readonly List<Vector2> Uvs = new List<Vector2>();
        static readonly List<int> Triangles = new List<int>();

        public static Mesh Build(WorldData world, int cx, int cz)
        {
            Vertices.Clear(); Normals.Clear(); Uvs.Clear(); Triangles.Clear();

            int size = world.ChunkSize;
            float blockSize = world.BlockSize;
            byte[] blocks = world.GetChunk(cx, cz);
            int top = world.GetChunkTop(cx, cz);
            int originX = cx * size, originZ = cz * size;

            for (int y = 0; y < top; y++)
            for (int lz = 0; lz < size; lz++)
            for (int lx = 0; lx < size; lx++)
            {
                byte block = blocks[world.LocalIndex(lx, y, lz)];
                if (block == (byte)BlockType.Air) continue;

                int gx = originX + lx, gz = originZ + lz;
                Vector2 uv = PaletteUv(block, Shade(gx, y, gz));
                for (int f = 0; f < FaceDirections.Length; f++)
                {
                    Vector3Int d = FaceDirections[f];
                    if (IsSolid(world, blocks, lx + d.x, y + d.y, lz + d.z, gx + d.x, gz + d.z)) continue;
                    AddFace(f, new Vector3(lx, y, lz), blockSize, uv);
                }
            }

            var mesh = new Mesh { name = $"Chunk {cx},{cz}" };
            if (Vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(Vertices);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static bool IsSolid(WorldData world, byte[] blocks, int lx, int y, int lz, int gx, int gz)
        {
            int size = world.ChunkSize;
            if (lx >= 0 && lx < size && lz >= 0 && lz < size && y >= 0 && y < world.Height)
                return blocks[world.LocalIndex(lx, y, lz)] != (byte)BlockType.Air;
            return world.GetBlock(gx, y, gz) != BlockType.Air;
        }

        static void AddFace(int face, Vector3 blockPos, float blockSize, Vector2 uv)
        {
            int start = Vertices.Count;
            Vector3 normal = FaceDirections[face];
            foreach (Vector3 corner in FaceCorners[face])
            {
                Vertices.Add((blockPos + corner) * blockSize);
                Normals.Add(normal);
                Uvs.Add(uv);
            }
            Triangles.Add(start); Triangles.Add(start + 1); Triangles.Add(start + 2);
            Triangles.Add(start); Triangles.Add(start + 2); Triangles.Add(start + 3);
        }

        /// <summary>Stable per-block hash so each block keeps its shade across rebuilds.</summary>
        static int Shade(int x, int y, int z)
        {
            unchecked
            {
                int h = x * 73856093 ^ y * 19349663 ^ z * 83492791;
                h ^= h >> 13;
                h *= 0x5bd1e995;
                return (h & 0x7fffffff) % ShadeCount;
            }
        }

        static Vector2 PaletteUv(byte block, int shade) =>
            new Vector2((shade + 0.5f) / ShadeCount, (block + 0.5f) / BlockTypeCount);

        /// <summary>Palette texture matching <see cref="PaletteUv"/>: one row per block type, one column per shade.</summary>
        public static Texture2D CreatePalette(WorldSettings settings)
        {
            var texture = new Texture2D(ShadeCount, BlockTypeCount, TextureFormat.RGBA32, false)
            {
                name = "Block Palette",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            for (int block = 0; block < BlockTypeCount; block++)
            {
                Color baseColor = block < settings.blockColors.Length ? settings.blockColors[block] : Color.magenta;
                for (int shade = 0; shade < ShadeCount; shade++)
                {
                    float t = shade / (float)(ShadeCount - 1) * 2f - 1f;
                    Color c = baseColor * (1f + t * settings.colorJitter);
                    c.a = 1f;
                    texture.SetPixel(shade, block, c);
                }
            }
            texture.Apply();
            return texture;
        }
    }
}
