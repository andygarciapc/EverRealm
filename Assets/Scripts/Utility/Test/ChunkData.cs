using UnityEngine;
using UnityEngine.UIElements;

public class ChunkData : MonoBehaviour
{
    // Chunk dimensions (standard Minecraft size: 16x256x16)
    public static readonly int chunkWidth = 16;
    public static readonly int chunkHeight = 256;
    public static readonly int chunkLength = 16;

    // Block data storage (0 = air)
    private readonly byte[,,] blocks;

    // World position of the chunk (in chunk coordinates, not world space)
    public Vector3Int chunkPosition { get; private set; }

    // Modification tracking
    private bool isModified;
    public bool IsModified => isModified;

    public ChunkData()
    {
        blocks = new byte[chunkWidth, chunkHeight, chunkLength];
        isModified = true; // New chunks need initial mesh generation
    }

    public void InitializePosition(Vector3Int position)
    {
        chunkPosition = position;
    }
    // Get block at local position within chunk
    public byte GetBlock(int x, int y, int z)
    {
        if (IsValidLocalPosition(x, y, z))
        {
            return blocks[x, y, z];
        }
        throw new System.ArgumentOutOfRangeException("Coordinates out of chunk bounds");
    }

    // Set block at local position and mark modified
    public void SetBlock(int x, int y, int z, byte blockID)
    {
        if (IsValidLocalPosition(x, y, z))
        {
            if (blocks[x, y, z] != blockID)
            {
                blocks[x, y, z] = blockID;
                isModified = true;
            }
        }
        else
        {
            throw new System.ArgumentOutOfRangeException("Coordinates out of chunk bounds");
        }
    }

    // Check if position is within chunk bounds
    public bool IsValidLocalPosition(int x, int y, int z)
    {
        return x >= 0 && x < chunkWidth &&
               y >= 0 && y < chunkHeight &&
               z >= 0 && z < chunkLength;
    }

    // Convert local position to world position
    public Vector3Int LocalToWorldPosition(int x, int y, int z)
    {
        return new Vector3Int(
            chunkPosition.x * chunkWidth + x,
            y,
            chunkPosition.z * chunkLength + z
        );
    }

    // Reset modified flag after changes are processed
    public void MarkAsProcessed()
    {
        isModified = false;
    }
}