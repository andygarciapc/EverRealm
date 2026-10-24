using UnityEngine;

namespace EverRealm.World
{
    /// <summary>
    /// Tunable parameters for island generation. All distances are in metres so the same seed
    /// produces the same landscape at any block size.
    /// </summary>
    [CreateAssetMenu(menuName = "EverRealm/World Settings", fileName = "WorldSettings")]
    public sealed class WorldSettings : ScriptableObject
    {
        [Header("World")]
        public int seed = 12345;
        [Tooltip("Width and depth of the generated area, in metres.")]
        public float islandSize = 256f;
        [Tooltip("Edge length of one block, in metres.")]
        public float blockSize = 0.7f;
        [Tooltip("Blocks per chunk side (horizontal).")]
        public int chunkSize = 32;

        [Header("Height")]
        public float maxHeight = 48f;
        public float seaLevel = 10f;
        [Tooltip("Height of the ocean floor at the island edge.")]
        public float seaFloor = 2f;
        [Tooltip(">1 flattens lowlands and sharpens peaks.")]
        public float heightExponent = 1.8f;

        [Header("Noise")]
        [Tooltip("Size of the largest landforms, in metres.")]
        public float noiseScale = 110f;
        [Range(1, 8)] public int octaves = 5;
        [Range(0f, 1f)] public float persistence = 0.5f;
        public float lacunarity = 2f;

        [Header("Coastline")]
        [Tooltip("Normalised distance from centre where land starts sinking into the sea.")]
        [Range(0f, 1f)] public float falloffStart = 0.55f;
        [Tooltip("How much noise distorts the coastline away from a circle.")]
        [Range(0f, 0.5f)] public float coastWarp = 0.25f;

        [Header("Materials")]
        [Tooltip("Top layers within this height above sea level become sand.")]
        public float beachHeight = 1.5f;
        [Tooltip("Depth of dirt under the grass, in metres.")]
        public float dirtDepth = 3f;
        [Tooltip("Normalised height (0-1 of maxHeight) above which bare stone is exposed.")]
        [Range(0f, 1f)] public float rockLine = 0.7f;

        [Header("Palette (indexed by BlockType)")]
        public Color[] blockColors =
        {
            Color.clear,                        // Air
            new Color(0.36f, 0.62f, 0.24f),     // Grass
            new Color(0.52f, 0.36f, 0.22f),     // Dirt
            new Color(0.50f, 0.50f, 0.52f),     // Stone
            new Color(0.86f, 0.79f, 0.55f),     // Sand
        };
        [Tooltip("Per-block brightness variation, 0 = flat colours.")]
        [Range(0f, 0.3f)] public float colorJitter = 0.08f;
    }
}
