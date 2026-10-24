using System.Collections.Generic;
using EverRealm.Combat;
using EverRealm.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EverRealm.Enemies
{
    /// <summary>
    /// Places seeded goblin camps on the island after every generation, and spawns a goblin in front
    /// of the player with the G debug key.
    /// </summary>
    public sealed class EncounterSpawner : MonoBehaviour
    {
        [SerializeField] WorldBuilder world;
        [SerializeField] GoblinAI goblinPrefab;
        [SerializeField] Combatant player;

        [Header("Camps")]
        public int campCount = 6;
        public int minPerCamp = 1;
        public int maxPerCamp = 3;
        [Tooltip("Goblins scatter this far around their camp centre.")]
        public float campRadius = 3f;
        public float minDistanceFromSpawn = 35f;
        public float minCampSpacing = 40f;

        [Header("Debug spawn")]
        public float debugSpawnDistance = 7f;

        // Dev-only key, kept out of the player's action map.
        readonly InputAction spawnAction = new InputAction("Spawn Goblin", binding: "<Keyboard>/g");
        readonly List<GoblinAI> goblins = new List<GoblinAI>();

        public int AliveCount
        {
            get
            {
                goblins.RemoveAll(g => g == null);
                return goblins.Count;
            }
        }

        void OnEnable()
        {
            world.Generated += Populate;
            spawnAction.performed += OnSpawnPressed;
            spawnAction.Enable();
        }

        void OnDisable()
        {
            world.Generated -= Populate;
            spawnAction.performed -= OnSpawnPressed;
            spawnAction.Disable();
        }

        // WorldBuilder generates in Awake, before this subscribes.
        void Start() => Populate();

        void Populate()
        {
            foreach (GoblinAI goblin in goblins)
                if (goblin != null) Destroy(goblin.gameObject);
            goblins.Clear();

            var rng = new System.Random(world.Settings.seed * 31 + 7);
            float size = world.World.SizeInMeters;
            Vector3 spawn = world.FindSpawnPoint();
            var camps = new List<Vector3>();

            for (int attempt = 0; attempt < 300 && camps.Count < campCount; attempt++)
            {
                float x = (float)rng.NextDouble() * size, z = (float)rng.NextDouble() * size;
                if (!world.TryGetLandSurface(x, z, out Vector3 camp)) continue;
                if (FlatDistance(camp, spawn) < minDistanceFromSpawn) continue;
                if (camps.Exists(c => FlatDistance(c, camp) < minCampSpacing)) continue;
                camps.Add(camp);

                int count = rng.Next(minPerCamp, maxPerCamp + 1);
                for (int i = 0; i < count; i++)
                {
                    double angle = rng.NextDouble() * Mathf.PI * 2, radius = rng.NextDouble() * campRadius;
                    float gx = camp.x + (float)(System.Math.Cos(angle) * radius);
                    float gz = camp.z + (float)(System.Math.Sin(angle) * radius);
                    if (world.TryGetLandSurface(gx, gz, out Vector3 position))
                        Spawn(position, camp, (float)(rng.NextDouble() * 360));
                }
            }
        }

        void OnSpawnPressed(InputAction.CallbackContext _)
        {
            Vector3 forward = player.transform.forward;
            Vector3 point = player.transform.position + forward * debugSpawnDistance;
            if (world.TryGetLandSurface(point.x, point.z, out Vector3 position))
                Spawn(position, position, Quaternion.LookRotation(-forward).eulerAngles.y);
        }

        GoblinAI Spawn(Vector3 position, Vector3 home, float yaw)
        {
            GoblinAI goblin = Instantiate(goblinPrefab, position + Vector3.up * 0.1f, Quaternion.Euler(0f, yaw, 0f), transform);
            goblin.Target = player;
            goblin.Home = home;
            goblin.SeaLevel = world.Settings.seaLevel;
            goblins.Add(goblin);
            return goblin;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
