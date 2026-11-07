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
        public float islandSize = 1024f;
        [Tooltip("Edge length of one block, in metres.")]
        public float blockSize = 0.7f;
        [Tooltip("Blocks per chunk side (horizontal).")]
        public int chunkSize = 32;

        [Header("Height")]
        public float maxHeight = 146f;
        public float seaLevel = 10f;
        [Tooltip("Height of the ocean floor at the island edge.")]
        public float seaFloor = 2f;

        [Header("Noise")]
        [Tooltip("Size of the largest hills, in metres.")]
        public float noiseScale = 190f;
        [Tooltip("Spacing of mountain ridges, in metres.")]
        public float ridgeScale = 170f;
        [Range(1, 8)] public int octaves = 4;
        [Range(0f, 1f)] public float persistence = 0.45f;
        public float lacunarity = 2f;

        [Header("Regions")]
        [Tooltip("Rough width of one biome region, in metres.")]
        public float regionSize = 260f;
        [Tooltip("Width of the blend between neighbouring regions, in metres.")]
        public float regionBlend = 70f;
        [Tooltip("How far region borders wander from straight lines, in metres.")]
        public float regionWarp = 70f;
        [Tooltip("Size of the patches colours drift across, in metres.")]
        public float colorPatchScale = 45f;

        [Header("Cliffs")]
        [Tooltip("Size of the flat-topped plateaus, in metres.")]
        public float plateauScale = 150f;
        [Tooltip("Noise level (0-1) above which land is plateau: higher means fewer.")]
        [Range(0f, 1f)] public float plateauLine = 0.6f;
        [Tooltip("Spacing of the gullies that break cliff bands, in metres. Gullies are the ways up.")]
        public float cliffBreakScale = 55f;
        [Tooltip("Noise level (0-1) below which cliff bands give way to gullies: lower means fewer ways up.")]
        [Range(0f, 1f)] public float gullyShare = 0.3f;

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
        [Tooltip("Height above which peaks are capped with snow, in metres.")]
        public float snowLine = 104f;
        [Tooltip("Ground dropping this many metres from one block to the next shows bare rock.")]
        public float cliffRise = 1.4f;

        [Header("Trees")]
        [Tooltip("Width of a planting cell, in metres: the closest trees usually get.")]
        public float treeSpacing = 6f;
        [Tooltip("Trees keep this far back from a hamlet's flattened ground, in metres.")]
        public float treeHamletMargin = 3f;

        [Header("Overhangs")]
        [Tooltip("A drop this deep beyond an edge makes it a cliff, in metres.")]
        public float minCliffDrop = 10f;
        [Tooltip("Only walls dropping at least this far overhang, in metres.")]
        public float overhangMinDrop = 14f;
        [Tooltip("How far the widest overhangs jut out from the rim, in metres.")]
        public float overhangReach = 8f;
        [Tooltip("Size of the stretches of cliff that overhang, in metres.")]
        public float overhangPatchScale = 90f;
        [Tooltip("Noise level (0-1) above which a rim overhangs: higher means fewer.")]
        [Range(0f, 1f)] public float overhangLine = 0.62f;

        [Header("Arches")]
        [Tooltip("How many natural arches the island tries to have.")]
        public int archCount = 4;
        [Tooltip("Arches stand at least this far apart, in metres.")]
        public float archSpacing = 220f;
        [Tooltip("How far an arch's fin runs out from the cliff, in metres.")]
        public float archMinLength = 18f;
        public float archMaxLength = 40f;

        [Header("Pillars and mesas")]
        [Tooltip("How many formations the island tries to have: a mesa or a cluster of pillars each.")]
        public int formationCount = 26;
        [Tooltip("Formations stand at least this far apart, in metres.")]
        public float formationSpacing = 60f;
        [Tooltip("Share of formations that are mesas.")]
        [Range(0f, 1f)] public float mesaShare = 0.25f;
        public float mesaMinRadius = 14f;
        public float mesaMaxRadius = 30f;
        public float pillarMinRadius = 2.5f;
        public float pillarMaxRadius = 7f;
        public float pillarMinHeight = 14f;
        public float pillarMaxHeight = 56f;

        [Header("Hamlet")]
        [Tooltip("How many hamlets the island tries to have.")]
        [Min(1)] public int hamletCount = 3;
        [Tooltip("Hamlets stand at least this far apart, in metres.")]
        public float minHamletSpacing = 380f;
        [Tooltip("Hamlets stand at most this far apart, in metres, if the land allows it.")]
        public float maxHamletSpacing = 560f;
        [Tooltip("Radius of the flattened ground the hamlet is built on, in metres.")]
        public float hamletRadius = 14f;
        [Tooltip("Width of the slope blending the flattened ground back into the terrain, in metres.")]
        public float hamletBlend = 6f;
        [Tooltip("Largest height difference across a site that may be flattened, in metres.")]
        public float hamletMaxRelief = 8f;
        [Tooltip("Hamlets are built below this height, in metres, so roads can reach them.")]
        public float hamletMaxHeight = 36f;
        public int minHuts = 3;
        public int maxHuts = 5;
        [Tooltip("Outer width of a hut, in blocks.")]
        [Min(4)] public int hutBlocks = 7;

        [Header("Biomes")]
        public Biome[] biomes = DefaultBiomes();

        [Header("Palette (indexed by BlockType)")]
        public Color[] blockColors =
        {
            Color.clear,                        // Air
            new Color(0.36f, 0.62f, 0.24f),     // Grass
            new Color(0.52f, 0.36f, 0.22f),     // Dirt
            new Color(0.50f, 0.50f, 0.52f),     // Stone
            new Color(0.86f, 0.79f, 0.55f),     // Sand
            new Color(0.40f, 0.27f, 0.16f),     // Wood
            new Color(0.66f, 0.50f, 0.32f),     // Planks
            new Color(0.78f, 0.66f, 0.36f),     // Thatch
            new Color(0.62f, 0.54f, 0.42f),     // Path
            new Color(0.20f, 0.50f, 0.22f),     // Leaves (unused: biomes colour them)
            new Color(0.34f, 0.24f, 0.17f),     // Log
            new Color(0.93f, 0.95f, 0.98f),     // Snow
        };
        [Tooltip("Per-block brightness variation, 0 = flat colours.")]
        [Range(0f, 0.3f)] public float colorJitter = 0.08f;
        static Biome[] DefaultBiomes() => new[]
        {
            new Biome
            {
                name = "Meadow", baseHeight = 3f, relief = 15f, plateauHeight = 15f,
                ground = new Color(0.40f, 0.72f, 0.25f), groundAlt = new Color(0.60f, 0.80f, 0.28f),
                foliage = new Color(0.22f, 0.58f, 0.22f), foliageAlt = new Color(0.40f, 0.70f, 0.24f),
                rock = new Color(0.56f, 0.55f, 0.53f), rockAlt = new Color(0.47f, 0.47f, 0.48f),
                tree = TreeShape.Round, treeDensity = 0.05f, overhangs = 0.6f, archChance = 0.6f, formationChance = 0.12f,
            },
            new Biome
            {
                name = "Deepwood", baseHeight = 6f, relief = 24f, plateauHeight = 22f,
                ground = new Color(0.17f, 0.48f, 0.30f), groundAlt = new Color(0.25f, 0.58f, 0.30f),
                foliage = new Color(0.08f, 0.34f, 0.25f), foliageAlt = new Color(0.14f, 0.46f, 0.25f),
                rock = new Color(0.40f, 0.46f, 0.42f), rockAlt = new Color(0.33f, 0.38f, 0.36f),
                tree = TreeShape.Giant, treeDensity = 0.42f, overhangs = 0.6f, archChance = 0.6f, formationChance = 0.15f,
            },
            new Biome
            {
                name = "Highlands", baseHeight = 14f, relief = 118f, ridged = 0.85f, terrace = 13f,
                ground = new Color(0.44f, 0.60f, 0.42f), groundAlt = new Color(0.62f, 0.63f, 0.42f),
                foliage = new Color(0.10f, 0.30f, 0.30f), foliageAlt = new Color(0.14f, 0.38f, 0.32f),
                rock = new Color(0.52f, 0.54f, 0.62f), rockAlt = new Color(0.40f, 0.42f, 0.52f),
                tree = TreeShape.Pine, treeDensity = 0.14f, archChance = 0.5f, formationChance = 0.3f,
            },
            new Biome
            {
                name = "Badlands", baseHeight = 5f, relief = 30f, terrace = 7f, plateauHeight = 34f,
                ground = new Color(0.86f, 0.62f, 0.34f), groundAlt = new Color(0.80f, 0.48f, 0.27f),
                foliage = new Color(0.36f, 0.56f, 0.30f), foliageAlt = new Color(0.46f, 0.60f, 0.30f),
                rock = new Color(0.76f, 0.38f, 0.21f), rockAlt = new Color(0.92f, 0.70f, 0.46f),
                tree = TreeShape.Cactus, treeDensity = 0.04f, overhangs = 1f, archChance = 1f, formationChance = 1f,
            },
            new Biome
            {
                name = "Amberwood", baseHeight = 4f, relief = 19f, plateauHeight = 18f,
                ground = new Color(0.70f, 0.66f, 0.24f), groundAlt = new Color(0.84f, 0.58f, 0.22f),
                foliage = new Color(0.92f, 0.46f, 0.12f), foliageAlt = new Color(0.82f, 0.22f, 0.13f),
                rock = new Color(0.58f, 0.50f, 0.42f), rockAlt = new Color(0.48f, 0.41f, 0.35f),
                tree = TreeShape.Round, treeDensity = 0.30f, overhangs = 0.6f, archChance = 0.6f, formationChance = 0.2f,
            },
        };
    }
}
