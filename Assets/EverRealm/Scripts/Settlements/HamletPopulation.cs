using System.Collections.Generic;
using EverRealm.Characters;
using EverRealm.World;
using UnityEngine;

namespace EverRealm.Settlements
{
    public enum HamletState { Living, Fallen, Ruin }

    /// <summary>
    /// Fills the hamlet with villagers after every generation: a few per house, a seeded mix of militia and
    /// civilians, each with a home, a bed, a work spot and its own resolve. Also takes in newcomers (the
    /// bodies the player starts in) as residents, so a body the player lets go of carries on living there.
    /// <para>
    /// PROVISIONAL (M3 placeholder, to be replaced once parties travel a larger map): settlers appear on their
    /// own every so often while the hamlet lives and has free beds, and when everyone is dead a single roll
    /// decides whether a colonization party comes or the hamlet stays a ruin.
    /// </para>
    /// </summary>
    public sealed class HamletPopulation : MonoBehaviour
    {
        /// <summary>Sleeping spots per house (see <see cref="House.SleepingSpot"/>).</summary>
        public const int BedsPerHouse = 3;

        [SerializeField] WorldBuilder world;
        [SerializeField] WorldClock clock;
        [Tooltip("A human body with a (disabled) VillagerAI brain.")]
        [SerializeField] VillagerAI villagerPrefab;

        public int minPerHouse = 1;
        public int maxPerHouse = 2;
        [Range(0f, 1f)] public float militiaFraction = 0.4f;

        [Header("Resolve")]
        [Tooltip("Chance a villager never breaks and fights to the death.")]
        [Range(0f, 1f)] public float fightToDeathChance = 0.33f;
        [Tooltip("Everyone else breaks and flees somewhere in this health range (fractions of max health).")]
        [Range(0f, 1f)] public float minBreakHealth = 0.1f;
        [Range(0f, 1f)] public float maxBreakHealth = 0.4f;
        [Tooltip("Chance a civilian turns and fights when a hostile corners it.")]
        [Range(0f, 1f)] public float cornerFightChance = 0.25f;

        [Header("Settlers (provisional)")]
        [Tooltip("In-game hours between settler arrivals, while the hamlet lives and has a free bed.")]
        public float settlerIntervalHours = 36f;
        public float settlerIntervalJitter = 12f;
        public int minSettlers = 1;
        public int maxSettlers = 2;
        [Tooltip("Settlers and colonists appear this far from the square and walk in.")]
        public float arrivalDistance = 70f;
        [Tooltip("Newcomers avoid arriving with a hostile within this distance.")]
        public float arrivalClearance = 30f;

        [Header("Recolonization (provisional)")]
        [Tooltip("Rolled once when the last resident dies: colonists come, or the hamlet stays a ruin.")]
        [Range(0f, 1f)] public float recolonizeChance = 0.5f;
        public float minColonizeDays = 2f;
        public float maxColonizeDays = 4f;
        public int minColonists = 3;
        public int maxColonists = 5;

        readonly List<VillagerAI> spawned = new List<VillagerAI>();
        readonly List<VillagerAI> adopted = new List<VillagerAI>();
        readonly Dictionary<VillagerAI, (House house, int spot)> beds = new Dictionary<VillagerAI, (House, int)>();
        System.Random rng;
        double nextSettlersAt, colonistsAt;
        int arrivals;

        public HamletState State { get; private set; }
        public double HoursUntilSettlers => nextSettlersAt - clock.TotalHours;
        public double HoursUntilColonists => colonistsAt - clock.TotalHours;

        public int FreeBeds
        {
            get
            {
                Settlement settlement = world.Settlement;
                if (settlement == null) return 0;
                int taken = 0;
                foreach (KeyValuePair<VillagerAI, (House house, int spot)> bed in beds)
                    if (bed.Key != null && bed.Key.IsAlive) taken++;
                return settlement.Houses.Count * BedsPerHouse - taken;
            }
        }

        public int CountAlive(Temperament temperament)
        {
            int count = 0;
            foreach (VillagerAI villager in Residents())
                if (villager.IsAlive && villager.Temperament == temperament) count++;
            return count;
        }

        public int CountAlive() => CountAlive(Temperament.Militia) + CountAlive(Temperament.Civilian);

        IEnumerable<VillagerAI> Residents()
        {
            foreach (VillagerAI villager in spawned)
                if (villager != null) yield return villager;
            foreach (VillagerAI villager in adopted)
                if (villager != null) yield return villager;
        }

        // Newcomers can be adopted before Start populates the hamlet.
        void Awake() => rng = new System.Random(world.Settings.seed * 13 + 5);

        void OnEnable() => world.Generated += Populate;
        void OnDisable() => world.Generated -= Populate;

        // WorldBuilder generates in Awake, before this subscribes.
        void Start() => Populate();

        void Populate()
        {
            foreach (VillagerAI villager in spawned)
                if (villager != null) Destroy(villager.gameObject);
            spawned.Clear();
            beds.Clear();
            arrivals = 0;
            rng = new System.Random(world.Settings.seed * 13 + 5);
            State = HamletState.Living;
            ScheduleSettlers();

            Settlement settlement = world.Settlement;
            ReadoptNewcomers(settlement);
            if (settlement == null || settlement.Houses.Count == 0) return;

            for (int h = 0; h < settlement.Houses.Count; h++)
            {
                House house = settlement.Houses[h];
                int count = rng.Next(minPerHouse, maxPerHouse + 1);
                for (int i = 0; i < count; i++)
                {
                    if (!TryClaimBed(house, out int spot)) break;
                    SpawnResident("Villager", RollTemperament(), house, spot, house.SleepingSpot(spot));
                }
            }
        }

        void Update()
        {
            if (world.Settlement == null) return;
            bool anyoneAlive = CountAlive() > 0;
            switch (State)
            {
                case HamletState.Living:
                    if (!anyoneAlive)
                    {
                        Fall();
                        break;
                    }
                    if (clock.TotalHours < nextSettlersAt) break;
                    if (FreeBeds > 0) Arrive("Settler", rng.Next(minSettlers, maxSettlers + 1), forceMilitia: false);
                    ScheduleSettlers();
                    break;
                case HamletState.Fallen:
                    if (anyoneAlive) Revive();
                    else if (clock.TotalHours >= colonistsAt)
                    {
                        Arrive("Colonist", rng.Next(minColonists, maxColonists + 1), forceMilitia: true);
                        Revive();
                    }
                    break;
                case HamletState.Ruin:
                    if (anyoneAlive) Revive(); // Someone (a newcomer body) moved in after all.
                    break;
            }
        }

        // ---- Provisional: settlers and recolonization ----

        void ScheduleSettlers() =>
            nextSettlersAt = clock.TotalHours + settlerIntervalHours + (rng.NextDouble() * 2 - 1) * settlerIntervalJitter;

        /// <summary>The last resident died: one roll decides between colonists and ruin.</summary>
        void Fall()
        {
            if (rng.NextDouble() < recolonizeChance)
            {
                State = HamletState.Fallen;
                colonistsAt = clock.TotalHours + 24.0 * (minColonizeDays + rng.NextDouble() * (maxColonizeDays - minColonizeDays));
                Debug.Log($"[EverRealm] The hamlet has fallen. Colonists will come in {HoursUntilColonists:F0} h.");
            }
            else
            {
                State = HamletState.Ruin;
                Debug.Log("[EverRealm] The hamlet has fallen and will stay a ruin.");
            }
        }

        void Revive()
        {
            State = HamletState.Living;
            ScheduleSettlers();
        }

        /// <summary>Newcomers appear out on the land and walk in to their new homes.</summary>
        void Arrive(string kind, int count, bool forceMilitia)
        {
            Settlement settlement = world.Settlement;
            Vector3 origin = ArrivalPoint(settlement);
            int arrived = 0;
            for (int i = 0; i < count; i++)
            {
                if (!TryClaimAnyBed(settlement, out House house, out int spot)) break;
                Temperament temperament = forceMilitia && i == 0 ? Temperament.Militia : RollTemperament();
                VillagerAI villager = SpawnResident(kind, temperament, house, spot, house.SleepingSpot(spot));
                Vector2 offset = Random.insideUnitCircle * 2f;
                villager.GetComponent<CharacterMotor>().Teleport(origin + new Vector3(offset.x, 0.1f, offset.y));
                arrived++;
            }
            if (arrived > 0) Debug.Log($"[EverRealm] {arrived} {kind.ToLowerInvariant()}{(arrived == 1 ? "" : "s")} arriving at the hamlet");
        }

        /// <summary>On land <see cref="arrivalDistance"/> out, preferring a direction with no hostiles (goblin camps) nearby.</summary>
        Vector3 ArrivalPoint(Settlement settlement)
        {
            Agent newcomer = villagerPrefab.GetComponent<Agent>();
            double start = rng.NextDouble() * Mathf.PI * 2;
            Vector3? fallback = null;
            for (int step = 0; step < 12; step++)
            {
                double angle = start + step * Mathf.PI / 6;
                Vector3 point = settlement.Square + new Vector3((float)System.Math.Cos(angle), 0f, (float)System.Math.Sin(angle)) * arrivalDistance;
                if (!world.TryGetLandSurface(point.x, point.z, out Vector3 surface)) continue;
                if (AgentDirectory.FindNearest(surface, arrivalClearance, a => a.IsAlive && newcomer.IsHostileTo(a)) == null) return surface;
                fallback ??= surface;
            }
            return fallback ?? settlement.Square;
        }

        // ---- Residents ----

        VillagerAI SpawnResident(string kind, Temperament temperament, House house, int spot, Vector3 bed)
        {
            Settlement settlement = world.Settlement;
            VillagerAI villager = Instantiate(villagerPrefab, bed + Vector3.up * 0.1f, Quaternion.identity, transform);
            villager.name = $"{kind} {++arrivals} ({temperament})";
            (float breakAt, bool cornerFight) = RollResolve(temperament);
            villager.Assign(clock, settlement, house, bed, WorkSpot(settlement, HouseIndex(settlement, house) + spot),
                            temperament, breakAt, cornerFight);
            beds[villager] = (house, spot);
            villager.enabled = true;
            spawned.Add(villager);
            return villager;
        }

        Temperament RollTemperament() => rng.NextDouble() < militiaFraction ? Temperament.Militia : Temperament.Civilian;

        (float breakHealthFraction, bool fightsWhenCornered) RollResolve(Temperament temperament)
        {
            float breakAt = rng.NextDouble() < fightToDeathChance ? 0f
                          : Mathf.Lerp(minBreakHealth, maxBreakHealth, (float)rng.NextDouble());
            bool cornerFight = temperament == Temperament.Civilian && rng.NextDouble() < cornerFightChance;
            return (breakAt, cornerFight);
        }

        /// <summary>
        /// Makes <paramref name="body"/> a resident (militia, since newcomers carry a sword) without
        /// switching its brain on; whoever controls the body decides that.
        /// </summary>
        public void Adopt(VillagerAI body)
        {
            if (!adopted.Contains(body)) adopted.Add(body);
            AssignNewcomer(body, world.Settlement);
        }

        /// <summary>After a regeneration, newcomers get homes in the new hamlet; those living on their own move in.</summary>
        void ReadoptNewcomers(Settlement settlement)
        {
            adopted.RemoveAll(v => v == null || !v.IsAlive);
            foreach (VillagerAI body in adopted)
            {
                AssignNewcomer(body, settlement);
                if (!body.enabled || body.Home == null) continue;
                body.GetComponent<CharacterMotor>().Teleport(body.Home.Inside + Vector3.up * 0.1f);
                if (body.TryGetComponent(out OceanRescue rescue)) rescue.ResetSafePosition();
            }
        }

        void AssignNewcomer(VillagerAI body, Settlement settlement)
        {
            beds.Remove(body);
            (float breakAt, bool cornerFight) = RollResolve(Temperament.Militia);
            if (settlement == null || settlement.Houses.Count == 0)
            {
                body.Assign(clock, null, null, body.transform.position, body.transform.position, Temperament.Militia, breakAt, cornerFight);
                return;
            }
            // A full hamlet still takes them in: they share the least crowded house's first spot.
            if (!TryClaimAnyBed(settlement, out House house, out int spot))
            {
                house = LeastCrowded(settlement);
                spot = 0;
            }
            body.Assign(clock, settlement, house, house.SleepingSpot(spot), WorkSpot(settlement, HouseIndex(settlement, house) + spot),
                        Temperament.Militia, breakAt, cornerFight);
            beds[body] = (house, spot);
        }

        // ---- Beds ----

        bool TryClaimAnyBed(Settlement settlement, out House house, out int spot)
        {
            house = LeastCrowded(settlement);
            spot = -1;
            return house != null && TryClaimBed(house, out spot);
        }

        /// <summary>The lowest sleeping spot in <paramref name="house"/> no living resident uses.</summary>
        bool TryClaimBed(House house, out int spot)
        {
            for (spot = 0; spot < BedsPerHouse; spot++)
                if (!IsTaken(house, spot)) return true;
            return false;
        }

        bool IsTaken(House house, int spot)
        {
            foreach (KeyValuePair<VillagerAI, (House house, int spot)> bed in beds)
                if (bed.Key != null && bed.Key.IsAlive && bed.Value.house == house && bed.Value.spot == spot) return true;
            return false;
        }

        House LeastCrowded(Settlement settlement)
        {
            House best = null;
            int fewest = int.MaxValue;
            foreach (House house in settlement.Houses)
            {
                int living = 0;
                foreach (KeyValuePair<VillagerAI, (House house, int spot)> bed in beds)
                    if (bed.Key != null && bed.Key.IsAlive && bed.Value.house == house) living++;
                if (living < fewest)
                {
                    fewest = living;
                    best = house;
                }
            }
            return best;
        }

        static int HouseIndex(Settlement settlement, House house)
        {
            for (int i = 0; i < settlement.Houses.Count; i++)
                if (settlement.Houses[i] == house) return i;
            return 0;
        }

        static Vector3 WorkSpot(Settlement settlement, int index) =>
            settlement.WorkSpots.Count > 0 ? settlement.WorkSpots[index % settlement.WorkSpots.Count] : settlement.Square;
    }
}
