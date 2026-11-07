using System;
using System.Collections.Generic;
using EverRealm.Characters;
using EverRealm.World;
using UnityEngine;

namespace EverRealm.Settlements
{
    /// <summary>
    /// The island's hamlets and the people in them. Fills each hamlet after every generation: a few villagers
    /// per house, a seeded mix of militia and civilians, each with a home, a bed, a work spot and its own
    /// resolve. Watches over each hamlet's fortunes: asks who is left when an attack ends and sends the
    /// survivors away at dawn if nobody is. Also takes in the bodies the player starts in as residents, so a
    /// body the player lets go of carries on living there. Runs the same whether or not anyone is watching.
    /// </summary>
    [DefaultExecutionOrder(-10)] // Fills the hamlets before anyone's Start asks about them.
    public sealed class HamletPopulation : MonoBehaviour
    {
        [SerializeField] WorldBuilder world;
        [SerializeField] WorldClock clock;
        [Tooltip("A human body with a (disabled) VillagerAI brain.")]
        [SerializeField] VillagerAI villagerPrefab;

        public int minPerHouse = 1;
        public int maxPerHouse = 2;
        [Range(0f, 1f)] public float militiaFraction = 0.4f;
        [Tooltip("Every hamlet starts with at least this many militia, so its first raid is a fight.")]
        public int minStartingMilitia = 2;

        [Header("Resolve")]
        [Tooltip("Chance a villager never breaks and fights to the death.")]
        [Range(0f, 1f)] public float fightToDeathChance = 0.33f;
        [Tooltip("Everyone else breaks and flees somewhere in this health range (fractions of max health).")]
        [Range(0f, 1f)] public float minBreakHealth = 0.1f;
        [Range(0f, 1f)] public float maxBreakHealth = 0.4f;
        [Tooltip("Chance a civilian turns and fights when a hostile corners it.")]
        [Range(0f, 1f)] public float cornerFightChance = 0.25f;

        [Header("Resettling")]
        [Tooltip("An emptied hamlet is due colonists this many days later, even if nobody survived to carry the news (provisional).")]
        public float minColonizeDays = 2f;
        public float maxColonizeDays = 4f;

        readonly List<Hamlet> hamlets = new List<Hamlet>();
        readonly List<VillagerAI> spawned = new List<VillagerAI>();
        readonly List<VillagerAI> adopted = new List<VillagerAI>();
        System.Random rng;
        int arrivals, lastDawn;

        public IReadOnlyList<Hamlet> Hamlets => hamlets;

        /// <summary>Raised after every (re)population, once every hamlet has its people.</summary>
        public event Action Populated;
        /// <summary>A hamlet's survivors have been told to go; they no longer live there.</summary>
        public event Action<Hamlet, List<VillagerAI>> Abandoned;

        void OnEnable() => world.Generated += Populate;
        void OnDisable() => world.Generated -= Populate;

        // WorldBuilder generates in Awake, before this subscribes.
        void Start() => Populate();

        void Populate()
        {
            foreach (VillagerAI villager in spawned)
                if (villager != null) Destroy(villager.gameObject);
            spawned.Clear();
            hamlets.Clear();
            arrivals = 0;
            rng = new System.Random(world.Settings.seed * 13 + 5);
            lastDawn = clock.Dawns;

            foreach (Settlement settlement in world.Settlements)
            {
                var hamlet = new Hamlet(settlement);
                hamlet.StateChanged += OnStateChanged;
                hamlets.Add(hamlet);
            }
            ReadoptNewcomers();
            foreach (Hamlet hamlet in hamlets)
            {
                foreach (House house in hamlet.Settlement.Houses)
                {
                    int count = rng.Next(minPerHouse, maxPerHouse + 1);
                    for (int i = 0; i < count && hamlet.FreeBeds > 0; i++)
                    {
                        Temperament temperament = hamlet.LivingFighters < minStartingMilitia ? Temperament.Militia : RollTemperament();
                        VillagerAI villager = SpawnPerson("Villager", temperament, house.Inside);
                        Settle(villager, hamlet);
                        villager.GetComponent<CharacterMotor>().Teleport(villager.Bed + Vector3.up * 0.1f);
                    }
                }
            }
            Populated?.Invoke();
        }

        void Update()
        {
            float radius = villagerPrefab.defendRadius;
            foreach (Hamlet hamlet in hamlets) hamlet.Review(radius);

            if (clock.Dawns == lastDawn) return;
            lastDawn = clock.Dawns;
            foreach (Hamlet hamlet in hamlets)
                if (hamlet.ReviewAtDawn(FightersPresent(hamlet))) SendAway(hamlet);
        }

        void OnStateChanged(Hamlet hamlet, HamletState previous)
        {
            if (hamlet.State == HamletState.Empty)
                hamlet.ColonistsDueAt = clock.TotalHours + 24.0 * (minColonizeDays + rng.NextDouble() * (maxColonizeDays - minColonizeDays));
            Debug.Log($"[EverRealm] {hamlet.Name}: {previous} -> {hamlet.State} ({hamlet.LivingResidents} residents, {hamlet.LivingFighters} fighters)");
        }

        // ---- Attacks and abandonment ----

        /// <summary>Armed, unbroken allies standing in <paramref name="hamlet"/> right now, resident or not.</summary>
        public int FightersPresent(Hamlet hamlet) => hamlet.CountFightersPresent(AlliesOnTheIsland(), villagerPrefab.defendRadius);

        /// <summary>Everyone alive, counted from the bodies themselves rather than from any record of them.</summary>
        public int CountAlive()
        {
            int count = 0;
            foreach (IPerson person in AlliesOnTheIsland())
                if (person.IsAlive) count++;
            return count;
        }

        IEnumerable<IPerson> AlliesOnTheIsland()
        {
            Agent ally = villagerPrefab.GetComponent<Agent>();
            foreach (Agent agent in AgentDirectory.All)
                if (ally.IsAlliedWith(agent) && agent.TryGetComponent(out VillagerAI villager)) yield return villager;
        }

        /// <summary>Leaving is an order to the residents, so a body the player controls stays put and keeps its home.</summary>
        void SendAway(Hamlet hamlet)
        {
            var leaving = new List<VillagerAI>();
            foreach (IPerson resident in hamlet.Residents())
            {
                var villager = (VillagerAI)resident;
                if (!villager.enabled) continue;
                hamlet.MoveOut(villager);
                leaving.Add(villager);
            }
            Debug.Log($"[EverRealm] {leaving.Count} survivors leave {hamlet.Name}");
            Abandoned?.Invoke(hamlet, leaving);
        }

        // ---- People ----

        /// <summary>Makes a new body with no home yet, standing at <paramref name="position"/>.</summary>
        public VillagerAI SpawnPerson(string kind, Temperament temperament, Vector3 position)
        {
            VillagerAI villager = Instantiate(villagerPrefab, position + Vector3.up * 0.1f, Quaternion.identity, transform);
            villager.name = $"{kind} {++arrivals} ({temperament})";
            (float breakAt, bool cornerFight) = RollResolve(temperament);
            villager.Assign(clock, null, null, position, position, temperament, breakAt, cornerFight);
            villager.enabled = true;
            spawned.Add(villager);
            return villager;
        }

        /// <summary>Gives <paramref name="villager"/> a free bed in <paramref name="hamlet"/>. False if there is none.</summary>
        public bool Settle(VillagerAI villager, Hamlet hamlet)
        {
            if (!hamlet.TryTakeBed(villager, out House house, out int spot)) return false;
            GiveHome(villager, hamlet, house, spot);
            return true;
        }

        /// <summary>Sets <paramref name="villager"/> up in a bed it has already taken.</summary>
        public void GiveHome(VillagerAI villager, Hamlet hamlet, House house, int spot)
        {
            Settlement settlement = hamlet.Settlement;
            villager.MoveInto(settlement, house, house.SleepingSpot(spot), WorkSpot(settlement, HouseIndex(settlement, house) + spot));
        }

        public Temperament RollTemperament() => rng.NextDouble() < militiaFraction ? Temperament.Militia : Temperament.Civilian;

        (float breakHealthFraction, bool fightsWhenCornered) RollResolve(Temperament temperament)
        {
            float breakAt = rng.NextDouble() < fightToDeathChance ? 0f
                          : Mathf.Lerp(minBreakHealth, maxBreakHealth, (float)rng.NextDouble());
            bool cornerFight = temperament == Temperament.Civilian && rng.NextDouble() < cornerFightChance;
            return (breakAt, cornerFight);
        }

        /// <summary>
        /// Makes <paramref name="body"/> a resident of the first hamlet (militia, since newcomers carry a sword)
        /// without switching its brain on; whoever controls the body decides that.
        /// </summary>
        public void Adopt(VillagerAI body)
        {
            if (!adopted.Contains(body)) adopted.Add(body);
            AssignNewcomer(body);
        }

        /// <summary>After a regeneration, newcomers get homes in the new hamlet; those living on their own move in.</summary>
        void ReadoptNewcomers()
        {
            adopted.RemoveAll(v => v == null || !v.IsAlive);
            foreach (VillagerAI body in adopted)
            {
                AssignNewcomer(body);
                if (!body.enabled || body.Home == null) continue;
                body.GetComponent<CharacterMotor>().Teleport(body.Home.Inside + Vector3.up * 0.1f);
                if (body.TryGetComponent(out OceanRescue rescue)) rescue.ResetSafePosition();
            }
        }

        void AssignNewcomer(VillagerAI body)
        {
            foreach (Hamlet other in hamlets) other.MoveOut(body);
            (float breakAt, bool cornerFight) = RollResolve(Temperament.Militia);
            body.Assign(clock, null, null, body.transform.position, body.transform.position, Temperament.Militia, breakAt, cornerFight);
            if (hamlets.Count > 0 && hamlets[0].TryTakeShelter(body, out House house, out int spot))
                GiveHome(body, hamlets[0], house, spot);
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
