using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class WorldManager : MonoBehaviour
{
    [Header("Chunk Settings")]
    [SerializeField] private GameObject chunkPrefab;
    [SerializeField] private int renderDistance = 8;

    [Header("Dependencies")]
    [SerializeField] private NoiseGenerator noiseGenerator;
    [SerializeField] private MeshGenerator meshGenerator;

    private Dictionary<Vector3Int, ChunkData> activeChunks = new Dictionary<Vector3Int, ChunkData>();
    private Transform playerTransform;
    private Vector3Int lastPlayerChunkPos;

    void Awake()
    {
        playerTransform = Camera.main.transform;
        lastPlayerChunkPos = GetCurrentChunkCoord();
    }

    void Start()
    {
        InitializeWorld();
    }

    void Update()
    {
        Vector3Int currentChunkPos = GetCurrentChunkCoord();
        if (currentChunkPos != lastPlayerChunkPos)
        {
            StartCoroutine(LoadChunksCoroutine(currentChunkPos));
            lastPlayerChunkPos = currentChunkPos;
        }
    }

    private void InitializeWorld()
    {
        Vector3Int startChunk = GetCurrentChunkCoord();
        StartCoroutine(LoadChunksCoroutine(startChunk));
    }

    private IEnumerator LoadChunksCoroutine(Vector3Int centerChunk)
    {
        HashSet<Vector3Int> chunksToKeep = new HashSet<Vector3Int>();
        int loadRadius = renderDistance;

        // Determine needed chunks
        for (int x = -loadRadius; x <= loadRadius; x++)
        {
            for (int z = -loadRadius; z <= loadRadius; z++)
            {
                Vector3Int chunkPos = new Vector3Int(
                    centerChunk.x + x,
                    0,
                    centerChunk.z + z
                );

                chunksToKeep.Add(chunkPos);

                if (!activeChunks.ContainsKey(chunkPos))
                {
                    CreateNewChunk(chunkPos);
                    yield return null; // Spread loading over frames
                }
            }
        }

        // Unload distant chunks
        List<Vector3Int> chunksToRemove = new List<Vector3Int>();
        foreach (var chunkPos in activeChunks.Keys)
        {
            if (!chunksToKeep.Contains(chunkPos))
            {
                chunksToRemove.Add(chunkPos);
            }
        }

        foreach (var chunkPos in chunksToRemove)
        {
            Destroy(activeChunks[chunkPos]);
            activeChunks.Remove(chunkPos);
        }
    }

    private void CreateNewChunk(Vector3Int chunkCoord)
    {
        // Create chunk data
        //ChunkData chunkData = new ChunkData(chunkCoord);

        // Instantiate chunk object
        Vector3 worldPos = new Vector3(
            chunkCoord.x * ChunkData.chunkWidth,
            0,
            chunkCoord.z * ChunkData.chunkLength
        );

        GameObject chunkObj = Instantiate(
            chunkPrefab,
            worldPos,
            Quaternion.identity,
            transform
        );

        ChunkData chunkData = chunkObj.AddComponent<ChunkData>();
        chunkData.InitializePosition(chunkCoord);
        PopulateChunk(chunkData);

        chunkObj.transform.position = worldPos;

        // Initialize chunk components
        ChunkManager chunkManager = chunkObj.GetComponent<ChunkManager>();
        chunkManager.Initialize(chunkData, meshGenerator);

        activeChunks.Add(chunkCoord, chunkData);
    }

    private void PopulateChunk(ChunkData chunkData)
    {
        for (int x = 0; x < ChunkData.chunkWidth; x++)
        {
            for (int z = 0; z < ChunkData.chunkLength; z++)
            {
                for (int y = 0; y < ChunkData.chunkHeight; y++)
                {
                    Vector3Int worldPos = chunkData.LocalToWorldPosition(x, y, z);
                    byte blockID = noiseGenerator.GetBlockType(worldPos);
                    chunkData.SetBlock(x, y, z, blockID);
                }
            }
        }
    }

    private Vector3Int GetCurrentChunkCoord()
    {
        Vector3 playerPos = playerTransform.position;
        return new Vector3Int(
            Mathf.FloorToInt(playerPos.x / ChunkData.chunkWidth),
            0,
            Mathf.FloorToInt(playerPos.z / ChunkData.chunkLength)
        );
    }

    public byte GetBlockAt(Vector3Int worldPosition)
    {
        Vector3Int chunkCoord = new Vector3Int(
            Mathf.FloorToInt(worldPosition.x / (float)ChunkData.chunkWidth),
            0,
            Mathf.FloorToInt(worldPosition.z / (float)ChunkData.chunkLength)
        );

        if (activeChunks.TryGetValue(chunkCoord, out ChunkData chunk))
        {
            Vector3Int localPos = new Vector3Int(
                worldPosition.x - chunkCoord.x * ChunkData.chunkWidth,
                worldPosition.y,
                worldPosition.z - chunkCoord.z * ChunkData.chunkLength
            );

            if (chunk.IsValidLocalPosition(localPos.x, localPos.y, localPos.z))
                return chunk.GetBlock(localPos.x, localPos.y, localPos.z);
        }
        return 0; // Air
    }
}