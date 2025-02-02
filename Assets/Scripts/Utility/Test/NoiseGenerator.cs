using UnityEngine;

public class NoiseGenerator : MonoBehaviour
{
    [Header("Height Settings")]
    [SerializeField] private float noiseScale = 50f;
    [SerializeField] private int octaves = 4;
    [SerializeField] private float persistence = 0.5f;
    [SerializeField] private float lacunarity = 2f;
    [SerializeField] private float heightMultiplier = 64f;
    [SerializeField] private int seed;

    [Header("Terrain Layers")]
    [SerializeField] private int groundHeight = 4;
    [SerializeField] private int stoneHeight = 8;

    void Awake()
    {
        InitializeNoise();
    }

    public void InitializeNoise()
    {
        if (seed == 0) seed = Random.Range(int.MinValue, int.MaxValue);
    }

    public byte GetBlockType(Vector3Int worldPosition)
    {
        int surfaceHeight = GetSurfaceHeight(worldPosition.x, worldPosition.z);

        if (worldPosition.y > surfaceHeight) return 0; // Air
        if (worldPosition.y == surfaceHeight) return 2; // Grass
        if (worldPosition.y > surfaceHeight - groundHeight) return 3; // Dirt
        if (worldPosition.y > surfaceHeight - groundHeight - stoneHeight) return 1; // Stone
        return 4; // Bedrock
    }

    private int GetSurfaceHeight(int worldX, int worldZ)
    {
        float amplitude = 1f;
        float frequency = 1f;
        float noiseHeight = 0f;
        float maxHeight = 0f;

        for (int i = 0; i < octaves; i++)
        {
            float sampleX = (worldX + seed) / noiseScale * frequency;
            float sampleZ = (worldZ + seed) / noiseScale * frequency;

            float perlinValue = Mathf.PerlinNoise(sampleX, sampleZ) * 2 - 1;
            noiseHeight += perlinValue * amplitude;

            maxHeight += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        noiseHeight = Mathf.Clamp01(noiseHeight / maxHeight);
        return Mathf.FloorToInt(noiseHeight * heightMultiplier);
    }
}