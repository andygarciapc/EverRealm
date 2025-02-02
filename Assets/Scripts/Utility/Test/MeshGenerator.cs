using UnityEngine;
using System.Collections.Generic;

public class MeshGenerator : MonoBehaviour
{
    public struct VoxelMeshData
    {
        public List<Vector3> vertices;
        public List<int> triangles;
        public List<Vector2> uvs;

        public VoxelMeshData(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs)
        {
            this.vertices = vertices;
            this.triangles = triangles;
            this.uvs = uvs;
        }
    }

    private enum Direction { Up, Down, North, South, East, West }

    [Header("UV Settings")]
    [SerializeField] private Vector2 textureTileSize = new Vector2(16, 16);
    [SerializeField] private Vector2 atlasSize = new Vector2(256, 256);

    public VoxelMeshData GenerateMesh(ChunkData chunkData)
    {
        VoxelMeshData meshData = new VoxelMeshData(
            new List<Vector3>(),
            new List<int>(),
            new List<Vector2>()
        );

        for(int x = 0; x < ChunkData.chunkWidth; x++)
        {
            for(int y = 0; y < ChunkData.chunkHeight; y++)
            {
                for(int z = 0; z < ChunkData.chunkLength; z++)
                {
                    byte blockID = chunkData.GetBlock(x, y, z);
                    if(blockID == 0) continue;

                    GenerateBlockFaces(chunkData, x, y, z, blockID, ref meshData);
                }
            }
        }

        return meshData;
    }

    private void GenerateBlockFaces(ChunkData chunkData, int x, int y, int z, byte blockID, ref VoxelMeshData meshData)
    {
        CheckFace(chunkData, x, y, z, Direction.Up, blockID, ref meshData);
        CheckFace(chunkData, x, y, z, Direction.Down, blockID, ref meshData);
        CheckFace(chunkData, x, y, z, Direction.North, blockID, ref meshData);
        CheckFace(chunkData, x, y, z, Direction.South, blockID, ref meshData);
        CheckFace(chunkData, x, y, z, Direction.East, blockID, ref meshData);
        CheckFace(chunkData, x, y, z, Direction.West, blockID, ref meshData);
    }

    private void CheckFace(ChunkData chunkData, int x, int y, int z, Direction direction, byte blockID, ref VoxelMeshData meshData)
    {
        if(IsFaceVisible(chunkData, x, y, z, direction))
        {
            AddFaceVertices(x, y, z, direction, ref meshData);
            AddFaceUVs(blockID, direction, ref meshData);
            AddFaceTriangles(ref meshData);
        }
    }

    private bool IsFaceVisible(ChunkData chunkData, int x, int y, int z, Direction direction)
    {
        Vector3Int offset = GetDirectionOffset(direction);
        int adjX = x + offset.x;
        int adjY = y + offset.y;
        int adjZ = z + offset.z;

        if(chunkData.IsValidLocalPosition(adjX, adjY, adjZ))
        {
            byte adjacentBlock = chunkData.GetBlock(adjX, adjY, adjZ);
            return adjacentBlock == 0;
        }
        return true; // Assume chunks at world edges have air
    }

    private void AddFaceVertices(int x, int y, int z, Direction direction, ref VoxelMeshData meshData)
    {
        Vector3[] vertices = new Vector3[4];
        Vector3 basePos = new Vector3(x, y, z);

        switch(direction)
        {
            case Direction.Up:
                vertices[0] = basePos + new Vector3(0, 1, 0);
                vertices[1] = basePos + new Vector3(1, 1, 0);
                vertices[2] = basePos + new Vector3(1, 1, 1);
                vertices[3] = basePos + new Vector3(0, 1, 1);
                break;
            case Direction.Down:
                vertices[0] = basePos + new Vector3(0, 0, 0);
                vertices[1] = basePos + new Vector3(0, 0, 1);
                vertices[2] = basePos + new Vector3(1, 0, 1);
                vertices[3] = basePos + new Vector3(1, 0, 0);
                break;
            case Direction.North:
                vertices[0] = basePos + new Vector3(0, 0, 1);
                vertices[1] = basePos + new Vector3(1, 0, 1);
                vertices[2] = basePos + new Vector3(1, 1, 1);
                vertices[3] = basePos + new Vector3(0, 1, 1);
                break;
            case Direction.South:
                vertices[0] = basePos + new Vector3(0, 0, 0);
                vertices[1] = basePos + new Vector3(0, 1, 0);
                vertices[2] = basePos + new Vector3(1, 1, 0);
                vertices[3] = basePos + new Vector3(1, 0, 0);
                break;
            case Direction.East:
                vertices[0] = basePos + new Vector3(1, 0, 0);
                vertices[1] = basePos + new Vector3(1, 0, 1);
                vertices[2] = basePos + new Vector3(1, 1, 1);
                vertices[3] = basePos + new Vector3(1, 1, 0);
                break;
            case Direction.West:
                vertices[0] = basePos + new Vector3(0, 0, 0);
                vertices[1] = basePos + new Vector3(0, 0, 1);
                vertices[2] = basePos + new Vector3(0, 1, 1);
                vertices[3] = basePos + new Vector3(0, 1, 0);
                break;
        }

        meshData.vertices.AddRange(vertices);
    }

    private void AddFaceUVs(byte blockID, Direction direction, ref VoxelMeshData meshData)
    {
        Vector2 textureCoord = GetTextureCoord(blockID, direction);
        float normalizedX = textureCoord.x / atlasSize.x;
        float normalizedY = textureCoord.y / atlasSize.y;
        float tileX = textureTileSize.x / atlasSize.x;
        float tileY = textureTileSize.y / atlasSize.y;

        meshData.uvs.Add(new Vector2(normalizedX, normalizedY));
        meshData.uvs.Add(new Vector2(normalizedX + tileX, normalizedY));
        meshData.uvs.Add(new Vector2(normalizedX + tileX, normalizedY + tileY));
        meshData.uvs.Add(new Vector2(normalizedX, normalizedY + tileY));
    }

    private Vector2 GetTextureCoord(byte blockID, Direction direction)
    {
        // Temporary texture coordinates - replace with your atlas layout
        return blockID switch
        {
            1 => new Vector2(0, 0),    // Stone
            2 => new Vector2(16, 0),   // Grass top
            3 => new Vector2(0, 16),   // Dirt
            4 => new Vector2(16, 16),  // Bedrock
            _ => Vector2.zero
        };
    }

    private void AddFaceTriangles(ref VoxelMeshData meshData)
    {
        int vertCount = meshData.vertices.Count;
        meshData.triangles.Add(vertCount - 4);
        meshData.triangles.Add(vertCount - 3);
        meshData.triangles.Add(vertCount - 2);
        meshData.triangles.Add(vertCount - 4);
        meshData.triangles.Add(vertCount - 2);
        meshData.triangles.Add(vertCount - 1);
    }

    private Vector3Int GetDirectionOffset(Direction direction)
    {
        return direction switch
        {
            Direction.Up => new Vector3Int(0, 1, 0),
            Direction.Down => new Vector3Int(0, -1, 0),
            Direction.North => new Vector3Int(0, 0, 1),
            Direction.South => new Vector3Int(0, 0, -1),
            Direction.East => new Vector3Int(1, 0, 0),
            Direction.West => new Vector3Int(-1, 0, 0),
            _ => Vector3Int.zero,
        };
    }
}