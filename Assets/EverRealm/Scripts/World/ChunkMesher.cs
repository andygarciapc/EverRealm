using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EverRealm.World
{
    /// <summary>
    /// Builds one mesh per chunk, emitting only block faces that touch air. Colour comes from a texture
    /// holding every colour (a flattened colour cube) sampled via UVs, so a stock URP Lit material can show
    /// any colour on any block.
    /// </summary>
    public static class ChunkMesher
    {
        public const int ShadeCount = 8;
        // 64 levels per channel: 8 x 8 tiles of 64 x 64 in one 512 x 512 texture.
        const int CubeLevels = 64, CubeTiles = 8, CubeWidth = CubeLevels * CubeTiles;
        // Rock layers are this many blocks thick.
        const int LayerBlocks = 3;

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

        public static Mesh Build(WorldData world, WorldSettings settings, int cx, int cz)
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
                Vector2 uv = default;
                bool coloured = false;
                for (int f = 0; f < FaceDirections.Length; f++)
                {
                    Vector3Int d = FaceDirections[f];
                    if (IsSolid(world, blocks, lx + d.x, y + d.y, lz + d.z, gx + d.x, gz + d.z)) continue;
                    if (!coloured)
                    {
                        uv = CubeUv(ColorOf(world, settings, (BlockType)block, gx, y, gz));
                        coloured = true;
                    }
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
            if (lx >= 0 && lx < size && lz >= 0 && lz < size && y >= 0)
            {
                int index = world.LocalIndex(lx, y, lz);
                return index < blocks.Length && blocks[index] != (byte)BlockType.Air;
            }
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

        /// <summary>What a block looks like where it stands, with its own small shift in brightness.</summary>
        static Color32 ColorOf(WorldData world, WorldSettings settings, BlockType block, int x, int y, int z)
        {
            ref ColumnPaint paint = ref world.Paint(x, z);
            int hash = Hash(x, y, z);
            Color color = block switch
            {
                BlockType.Grass => paint.Ground,
                BlockType.Leaves => paint.Foliage,
                BlockType.Stone => Hash(0, y / LayerBlocks, 0) % 3 == 0 ? paint.RockAlt : paint.Rock,
                BlockType.Dirt => Color.Lerp(settings.blockColors[(int)block], paint.Rock, 0.6f),
                _ => settings.blockColors[(int)block],
            };
            float shade = hash % ShadeCount / (float)(ShadeCount - 1) * 2f - 1f;
            color *= 1f + shade * settings.colorJitter;
            color.a = 1f;
            return color;
        }

        /// <summary>Stable non-negative hash, so a block keeps its look across rebuilds.</summary>
        public static int Hash(int x, int y, int z)
        {
            unchecked
            {
                int h = x * 73856093 ^ y * 19349663 ^ z * 83492791;
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return h & 0x7fffffff;
            }
        }

        static int Level(byte channel) => channel * CubeLevels / 256;

        static Vector2 CubeUv(Color32 color)
        {
            int blue = Level(color.b);
            return new Vector2((Level(color.r) + blue % CubeTiles * CubeLevels + 0.5f) / CubeWidth,
                               (Level(color.g) + blue / CubeTiles * CubeLevels + 0.5f) / CubeWidth);
        }

        /// <summary>The colour texture matching <see cref="CubeUv"/>.</summary>
        public static Texture2D CreatePalette()
        {
            var pixels = new Color32[CubeWidth * CubeWidth];
            for (int y = 0; y < CubeWidth; y++)
            for (int x = 0; x < CubeWidth; x++)
            {
                int blue = x / CubeLevels + y / CubeLevels * CubeTiles;
                pixels[x + y * CubeWidth] = new Color32(Channel(x % CubeLevels), Channel(y % CubeLevels), Channel(blue), 255);
            }
            var texture = new Texture2D(CubeWidth, CubeWidth, TextureFormat.RGBA32, false)
            {
                name = "Block Colours",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        static byte Channel(int level) => (byte)(level * 255 / (CubeLevels - 1));
    }
}
