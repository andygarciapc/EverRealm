using System.Collections.Generic;
using EverRealm.Combat;
using EverRealm.Player;
using EverRealm.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EverRealm.Enemies
{
    /// <summary>
    /// Places seeded goblin camps on the island (away from the hamlet) after every generation, each goblin
    /// with a varied dodge style. Camps slowly regrow over world time, up to a cap, even when wiped out.
    /// Spawns a goblin in front of the controlled character with the G debug key.
    /// </summary>
    public sealed class EncounterSpawner : MonoBehaviour
    {
        [SerializeField] WorldBuilder world;
        [SerializeField] WorldClock clock;
        [SerializeField] GoblinAI goblinPrefab;
        [Tooltip("Only used to place debug spawns in front of the controlled character.")]
        [SerializeField] PlayerSession session;

        [Header("Camps")]
        public int campCount = 6;
        public int minPerCamp = 1;
        public int maxPerCamp = 3;
        [Tooltip("Goblins scatter this far around their camp centre.")]
        public float campRadius = 3f;
        public float minDistanceFromSpawn = 35f;
        [Tooltip("Keeps camps far enough from the hamlet that only raids, not wandering, bring goblins there.")]
        public float minDistanceFromHamlet = 50f;
        public float minCampSpacing = 40f;
        [Tooltip("Camps placed around each hamlet first, so every hamlet has goblins in raiding reach. They count toward the camp total.")]
        public int campsPerHamlet = 4;
        [Tooltip("Those camps lie no further than this from the hamlet's square.")]
        public float hamletCampRange = 180f;
        [Tooltip("A camp regrows one goblin this often (in-game hours) while it has fewer than its capacity.")]
        public float regrowHours = 12f;
        public int campCapacity = 4;

        [Header("Debug spawn")]
        public float debugSpawnDistance = 7f;

        // Dev-only key, kept out of the player's action map.
        readonly InputAction spawnAction = new InputAction("Spawn Goblin", binding: "<Keyboard>/g");
        readonly List<GoblinAI> goblins = new List<GoblinAI>();
        readonly List<Vector3> camps = new List<Vector3>();
        readonly List<double> nextRegrowAt = new List<double>();

        /// <summary>Every goblin placed so far, dead or alive (destroyed ones may be null).</summary>
        public IReadOnlyList<GoblinAI> Goblins => goblins;

        public int AliveCount
        {
            get
            {
                goblins.RemoveAll(g => g == null);
                return goblins.FindAll(g => !g.GetComponent<Combatant>().IsDead).Count;
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
            camps.Clear();
            nextRegrowAt.Clear();

            var rng = new System.Random(world.Settings.seed * 31 + 7);
            float size = world.World.SizeInMeters;

            // Every hamlet gets camps within raiding reach first; the rest fall anywhere.
            foreach (Settlement settlement in world.Settlements)
            {
                int placed = 0;
                for (int attempt = 0; attempt < 100 && placed < campsPerHamlet; attempt++)
                {
                    double angle = rng.NextDouble() * Mathf.PI * 2;
                    double range = minDistanceFromHamlet + rng.NextDouble() * (hamletCampRange - minDistanceFromHamlet);
                    float x = settlement.Square.x + (float)(System.Math.Cos(angle) * range);
                    float z = settlement.Square.z + (float)(System.Math.Sin(angle) * range);
                    if (TryPlaceCamp(x, z, rng, settlement.Square)) placed++;
                }
            }
            for (int attempt = 0; attempt < 300 && camps.Count < campCount; attempt++)
                TryPlaceCamp((float)rng.NextDouble() * size, (float)rng.NextDouble() * size, rng);
        }

        bool TryPlaceCamp(float x, float z, System.Random rng, Vector3? marchesOn = null)
        {
            if (!world.TryGetLandSurface(x, z, out Vector3 camp)) return false;
            if (FlatDistance(camp, world.FindSpawnPoint()) < minDistanceFromSpawn) return false;
            if (IsNearHamlet(camp)) return false;
            if (camps.Exists(c => FlatDistance(c, camp) < minCampSpacing)) return false;
            if (marchesOn.HasValue && !world.CanMarch(camp, marchesOn.Value)) return false;
            camps.Add(camp);
            nextRegrowAt.Add(clock.TotalHours + regrowHours);

            int count = rng.Next(minPerCamp, maxPerCamp + 1);
            for (int i = 0; i < count; i++)
            {
                double angle = rng.NextDouble() * Mathf.PI * 2, radius = rng.NextDouble() * campRadius;
                float gx = camp.x + (float)(System.Math.Cos(angle) * radius);
                float gz = camp.z + (float)(System.Math.Sin(angle) * radius);
                if (world.TryGetLandSurface(gx, gz, out Vector3 position))
                    Spawn(position, camp, (float)(rng.NextDouble() * 360), (WeightClass)rng.Next(3));
            }
            return true;
        }

        bool IsNearHamlet(Vector3 point)
        {
            foreach (Settlement settlement in world.Settlements)
                if (FlatDistance(point, settlement.Square) < minDistanceFromHamlet) return true;
            return false;
        }

        void Update()
        {
            for (int i = 0; i < camps.Count; i++)
            {
                if (clock.TotalHours < nextRegrowAt[i]) continue;
                nextRegrowAt[i] = clock.TotalHours + regrowHours;
                if (Strength(camps[i]) >= campCapacity) continue;
                Vector2 offset = Random.insideUnitCircle * campRadius;
                if (world.TryGetLandSurface(camps[i].x + offset.x, camps[i].z + offset.y, out Vector3 position))
                    Spawn(position, camps[i], Random.Range(0f, 360f), (WeightClass)Random.Range(0, 3));
            }
        }

        /// <summary>Living goblins that call <paramref name="camp"/> home, including any out raiding.</summary>
        int Strength(Vector3 camp)
        {
            int count = 0;
            foreach (GoblinAI goblin in goblins)
                if (goblin != null && goblin.Home == camp && !goblin.GetComponent<Combatant>().IsDead) count++;
            return count;
        }

        void OnSpawnPressed(InputAction.CallbackContext _)
        {
            Combatant body = session.CurrentBody;
            if (body == null) return;
            Vector3 forward = body.transform.forward;
            Vector3 point = body.transform.position + forward * debugSpawnDistance;
            if (world.TryGetLandSurface(point.x, point.z, out Vector3 position))
                Spawn(position, position, Quaternion.LookRotation(-forward).eulerAngles.y, (WeightClass)Random.Range(0, 3));
        }

        GoblinAI Spawn(Vector3 position, Vector3 home, float yaw, WeightClass weightClass)
        {
            GoblinAI goblin = Instantiate(goblinPrefab, position + Vector3.up * 0.1f, Quaternion.Euler(0f, yaw, 0f), transform);
            goblin.Home = home;
            goblin.GetComponent<Combatant>().weightClass = weightClass;
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
