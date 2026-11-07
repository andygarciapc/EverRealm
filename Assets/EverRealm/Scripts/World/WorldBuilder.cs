using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace EverRealm.World
{
    /// <summary>
    /// Generates the island, its hamlets and roads at startup and on demand (R = new seed),
    /// creating one GameObject per chunk plus the ocean plane and edge walls.
    /// </summary>
    [DefaultExecutionOrder(-100)] // Generate before anything subscribes; listeners place themselves in Start.
    public sealed class WorldBuilder : MonoBehaviour
    {
        [SerializeField] WorldSettings settings;
        [SerializeField] Material terrainMaterial;
        [SerializeField] Material waterMaterial;

        readonly List<GameObject> spawned = new List<GameObject>();
        readonly List<Mesh> chunkMeshes = new List<Mesh>();
        Material terrainMaterialInstance;
        Texture2D palette;

        public WorldSettings Settings { get; private set; }
        public WorldData World { get; private set; }
        /// <summary>The island's hamlets; there may be fewer than wanted, or none, if the land is rough.</summary>
        public IReadOnlyList<Settlement> Settlements { get; private set; } = Array.Empty<Settlement>();
        public RoadNetwork Roads { get; private set; }
        /// <summary>Where newcomers come ashore, if the coast has somewhere suitable.</summary>
        public Vector3? Landing { get; private set; }
        public float GenerationMs { get; private set; }
        public float MeshingMs { get; private set; }

        /// <summary>Raised after every (re)generation, once colliders exist.</summary>
        public event Action Generated;

        // The tallest ledge a jump gets a body up: two blocks.
        const float HighestHop = 1.45f;

        // Dev-only keys, kept out of the player's action map.
        readonly InputAction regenerateAction = new InputAction("Regenerate", binding: "<Keyboard>/r");

        void Awake()
        {
            // Work on a copy so runtime seed changes never dirty the asset.
            Settings = Instantiate(settings);
            Build();
        }

        void OnEnable()
        {
            regenerateAction.performed += OnRegenerate;
            regenerateAction.Enable();
        }

        void OnDisable()
        {
            regenerateAction.Disable();
            regenerateAction.performed -= OnRegenerate;
        }

        void OnRegenerate(InputAction.CallbackContext _)
        {
            Settings.seed = UnityEngine.Random.Range(0, int.MaxValue);
            Build();
        }

        public void Build()
        {
            Clear();

            var timer = Stopwatch.StartNew();
            var terrain = new TerrainGenerator(Settings);
            World = terrain.Generate();
            List<Settlement> settlements = SettlementBuilder.Build(World, Settings);
            Settlements = settlements;
            Roads = RoadBuilder.Build(World, Settings, settlements, out Vector3? landing);
            Landing = landing;
            int[] tops = SettlementBuilder.TopBlocks(World);
            int formations = FormationBuilder.Build(World, Settings, terrain, tops, settlements);
            List<CliffRim> rims = Cliffs.FindRims(World, Settings, tops);
            int arches = ArchBuilder.Build(World, Settings, terrain, tops, rims, settlements);
            int overhangs = OverhangBuilder.Build(World, Settings, terrain, tops, rims, settlements);
            int trees = TreeBuilder.Plant(World, Settings, terrain, settlements);
            GenerationMs = (float)timer.Elapsed.TotalMilliseconds;

            timer.Restart();
            palette = ChunkMesher.CreatePalette();
            terrainMaterialInstance = new Material(terrainMaterial) { mainTexture = palette };
            float chunkMeters = World.ChunkSize * World.BlockSize;
            for (int cz = 0; cz < World.ChunksPerSide; cz++)
            for (int cx = 0; cx < World.ChunksPerSide; cx++)
                SpawnChunk(cx, cz, new Vector3(cx * chunkMeters, 0f, cz * chunkMeters));
            MeshingMs = (float)timer.Elapsed.TotalMilliseconds;

            SpawnWater();
            SpawnEdgeWalls();

            UnityEngine.Debug.Log($"[EverRealm] Island seed {Settings.seed}, block {Settings.blockSize}m: " +
                                  $"generate {GenerationMs:F0} ms, mesh+collide {MeshingMs:F0} ms, " +
                                  $"{Settlements.Count} hamlets, {Roads.Roads.Count} roads, {formations} formations, {arches} arches, {overhangs} overhanging columns, {trees} trees, " +
                                  (Landing.HasValue ? "a landing" : "no landing"));
            Generated?.Invoke();
        }

        void SpawnChunk(int cx, int cz, Vector3 position)
        {
            Mesh mesh = ChunkMesher.Build(World, Settings, cx, cz);
            chunkMeshes.Add(mesh);
            var chunk = new GameObject(mesh.name);
            chunk.transform.SetParent(transform, false);
            chunk.transform.localPosition = position;
            chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
            chunk.AddComponent<MeshRenderer>().sharedMaterial = terrainMaterialInstance;
            chunk.AddComponent<MeshCollider>().sharedMesh = mesh;
            spawned.Add(chunk);
        }

        void SpawnWater()
        {
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Ocean";
            Destroy(water.GetComponent<Collider>());
            water.transform.SetParent(transform, false);
            float size = World.SizeInMeters;
            water.transform.localPosition = new Vector3(size * 0.5f, Settings.seaLevel, size * 0.5f);
            water.transform.localScale = Vector3.one * (size * 4f / 10f); // Plane primitive is 10m wide.
            var renderer = water.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = waterMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            spawned.Add(water);
        }

        void SpawnEdgeWalls()
        {
            var walls = new GameObject("Edge Walls");
            walls.transform.SetParent(transform, false);
            float size = World.SizeInMeters, height = Settings.maxHeight * 3f, thickness = 4f;
            AddWall(walls, new Vector3(size * 0.5f, height * 0.5f, -thickness * 0.5f), new Vector3(size, height, thickness));
            AddWall(walls, new Vector3(size * 0.5f, height * 0.5f, size + thickness * 0.5f), new Vector3(size, height, thickness));
            AddWall(walls, new Vector3(-thickness * 0.5f, height * 0.5f, size * 0.5f), new Vector3(thickness, height, size));
            AddWall(walls, new Vector3(size + thickness * 0.5f, height * 0.5f, size * 0.5f), new Vector3(thickness, height, size));
            spawned.Add(walls);
        }

        static void AddWall(GameObject parent, Vector3 center, Vector3 size)
        {
            var box = parent.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
        }

        void Clear()
        {
            foreach (GameObject go in spawned) Destroy(go);
            foreach (Mesh mesh in chunkMeshes) Destroy(mesh);
            spawned.Clear();
            chunkMeshes.Clear();
            if (terrainMaterialInstance != null) Destroy(terrainMaterialInstance);
            if (palette != null) Destroy(palette);
        }

        /// <summary>Where a new character starts: the first hamlet's square, or else the land column nearest the island centre.</summary>
        public Vector3 FindSpawnPoint()
        {
            if (Settlements.Count > 0) return Settlements[0].Square + Vector3.up * 0.5f;
            int center = World.SizeInBlocks / 2;
            float minHeight = Settings.seaLevel + Settings.beachHeight;
            for (int radius = 0; radius < center; radius++)
            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != radius) continue;
                int x = center + dx, z = center + dz;
                float top = SurfaceHeight(x, z);
                if (top > minHeight)
                    return new Vector3((x + 0.5f) * World.BlockSize, top + 0.5f, (z + 0.5f) * World.BlockSize);
            }
            return new Vector3(center * World.BlockSize, Settings.maxHeight, center * World.BlockSize);
        }

        /// <summary>Surface point at world position (x, z), if that column is land above the waterline.</summary>
        public bool TryGetLandSurface(float x, float z, out Vector3 surface)
        {
            surface = default;
            int bx = Mathf.FloorToInt(x / World.BlockSize), bz = Mathf.FloorToInt(z / World.BlockSize);
            if (bx < 0 || bz < 0 || bx >= World.SizeInBlocks || bz >= World.SizeInBlocks) return false;
            float top = SurfaceHeight(bx, bz);
            if (top <= Settings.seaLevel + 0.1f) return false;
            surface = new Vector3(x, top, z);
            return true;
        }

        /// <summary>
        /// Whether a body could walk the straight line between two points, which is how goblins march:
        /// dry land all the way and no ledge too high to hop. Dropping off one is fine.
        /// </summary>
        public bool CanMarch(Vector3 from, Vector3 to)
        {
            float distance = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z));
            int steps = Mathf.CeilToInt(distance / World.BlockSize);
            float previous = float.PositiveInfinity;
            for (int i = 0; i <= steps; i++)
            {
                Vector3 point = Vector3.Lerp(from, to, steps == 0 ? 0f : i / (float)steps);
                if (!TryGetLandSurface(point.x, point.z, out Vector3 surface) || surface.y - previous > HighestHop) return false;
                previous = surface.y;
            }
            return true;
        }

        /// <summary>Height in metres of the ground in block column (x, z), ignoring any tree on it.</summary>
        float SurfaceHeight(int x, int z)
        {
            for (int y = World.GetChunkTop(x / World.ChunkSize, z / World.ChunkSize) - 1; y >= 0; y--)
                if (World.GetBlock(x, y, z) is not (BlockType.Air or BlockType.Leaves or BlockType.Log))
                    return (y + 1) * World.BlockSize;
            return 0f;
        }
    }
}
