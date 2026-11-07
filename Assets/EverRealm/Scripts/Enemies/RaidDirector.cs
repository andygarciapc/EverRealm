using System.Collections.Generic;
using EverRealm.Characters;
using EverRealm.Combat;
using EverRealm.Settlements;
using EverRealm.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EverRealm.Enemies
{
    /// <summary>One hamlet's raid schedule and the war party, if any, now sent against it.</summary>
    public sealed class RaidFront
    {
        public Hamlet Hamlet { get; }
        public double NextRaidAt { get; set; }
        public string LastRaid { get; set; } = "none yet";
        /// <summary>The war party has charged and the attack isn't over.</summary>
        public bool IsAttacking { get; set; }
        public double AttackStartedAt { get; set; }
        public float MusterStartTime { get; set; }
        public List<GoblinAI> Party { get; } = new List<GoblinAI>();
        /// <summary>A war party is still gathering before it attacks.</summary>
        public bool IsMustering => Party.Count > 0 && !IsAttacking;

        public RaidFront(Hamlet hamlet) => Hamlet = hamlet;
    }

    /// <summary>
    /// Sends goblin war parties against each hamlet on that hamlet's own world-time schedule, whether or not
    /// anyone is watching. A party outnumbers the hamlet's fighters, drawn only from camps within reach of it;
    /// it gathers at a muster point out of the villagers' sight and attacks together. If the camps can't muster
    /// a full party yet, the raid waits until they can. An attack lasts from the charge until the party is dead
    /// or gone, or until it has dragged on too long; the hamlet then counts who is left to hold it.
    /// Y raids the hamlet nearest the camera now (debug), with whatever goblins are free.
    /// </summary>
    public sealed class RaidDirector : MonoBehaviour
    {
        [SerializeField] WorldBuilder world;
        [SerializeField] WorldClock clock;
        [SerializeField] EncounterSpawner encounters;
        [SerializeField] HamletPopulation population;

        [Tooltip("In-game hours from the start (or a new island) to the first raid anywhere.")]
        public float firstRaidHours = 10f;
        [Tooltip("Hamlets are first raided this many in-game hours apart, the first hamlet last, so trouble starts elsewhere.")]
        public float firstRaidStaggerHours = 10f;
        [Tooltip("In-game hours between raids on the same hamlet.")]
        public float raidIntervalHours = 20f;
        [Tooltip("Each interval varies by up to this many hours either way.")]
        public float raidIntervalJitter = 4f;

        [Header("War party")]
        [Tooltip("Goblins sent per living hamlet fighter (militia, including armed newcomers), rounded up.")]
        public float goblinsPerFighter = 2f;
        public int minPartySize = 2;
        [Tooltip("Only goblins whose camp is this close to the square are sent: they march in straight lines.")]
        public float raidReach = 250f;
        [Tooltip("If the camps can't muster a full party, the raid is tried again this many in-game hours later.")]
        public float postponeHours = 4f;
        [Tooltip("The party gathers this far from the square, beyond the militia's defence radius.")]
        public float musterDistance = 36f;
        [Tooltip("Seconds the gathered party waits for stragglers before attacking anyway.")]
        public float musterTimeoutSeconds = 90f;

        [Header("Attack")]
        [Tooltip("Raiders further than this from the square aren't attacking it. Must reach past the muster point.")]
        public float attackRadius = 40f;
        [Tooltip("For this many in-game hours after the charge, raiders still marching in keep the attack going, so a party that arrives in pieces still counts as one attack.")]
        public float stragglerHours = 2f;
        [Tooltip("An attack still going after this many in-game hours is called off, so a stuck raider can't hold it open.")]
        public float attackTimeoutHours = 4f;

        // Dev-only key, kept out of the player's action map.
        readonly InputAction raidAction = new InputAction("Raid Now", binding: "<Keyboard>/y");
        readonly List<RaidFront> fronts = new List<RaidFront>();
        System.Random rng;

        public IReadOnlyList<RaidFront> Fronts => fronts;

        public int ActiveRaiders
        {
            get
            {
                int count = 0;
                foreach (GoblinAI goblin in encounters.Goblins)
                    if (goblin != null && goblin.IsRaiding && !goblin.GetComponent<Combatant>().IsDead) count++;
                return count;
            }
        }

        public RaidFront FrontOf(Hamlet hamlet) => fronts.Find(f => f.Hamlet == hamlet);

        void OnEnable()
        {
            population.Populated += ResetSchedule;
            raidAction.performed += OnRaidPressed;
            raidAction.Enable();
        }

        void OnDisable()
        {
            population.Populated -= ResetSchedule;
            raidAction.performed -= OnRaidPressed;
            raidAction.Disable();
        }

        // The hamlets fill in their own Start, before this subscribes or just after, depending on script order.
        void Start() => ResetSchedule();

        void ResetSchedule()
        {
            rng = new System.Random(world.Settings.seed * 17 + 3);
            fronts.Clear();
            for (int i = 0; i < population.Hamlets.Count; i++)
                fronts.Add(new RaidFront(population.Hamlets[i]) { NextRaidAt = clock.TotalHours + firstRaidHours + (population.Hamlets.Count - 1 - i) * firstRaidStaggerHours });
        }

        void OnRaidPressed(InputAction.CallbackContext _)
        {
            RaidFront nearest = null;
            float best = float.PositiveInfinity;
            Vector3 viewer = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            foreach (RaidFront front in fronts)
            {
                float distance = Steering.FlatDistance(viewer, front.Hamlet.Settlement.Square);
                if (distance >= best) continue;
                best = distance;
                nearest = front;
            }
            if (nearest != null) LaunchRaid(nearest, fullPartyOnly: false);
        }

        void Update()
        {
            foreach (RaidFront front in fronts)
            {
                UpdateMuster(front);
                UpdateAttack(front);
                if (clock.TotalHours < front.NextRaidAt) continue;
                bool launched = LaunchRaid(front, fullPartyOnly: true);
                front.NextRaidAt = clock.TotalHours + (launched
                    ? raidIntervalHours + (rng.NextDouble() * 2 - 1) * raidIntervalJitter
                    : postponeHours);
            }
        }

        /// <summary>Once everyone still raiding has gathered (or waited long enough), the party attacks.</summary>
        void UpdateMuster(RaidFront front)
        {
            if (front.IsAttacking) return;
            List<GoblinAI> party = front.Party;
            party.RemoveAll(g => g == null || !g.IsRaiding || !g.IsMustering || g.GetComponent<Combatant>().IsDead);
            if (party.Count == 0) return;

            // The hamlet fell or emptied while they gathered: nothing left to attack.
            if (front.Hamlet.State != HamletState.Living)
            {
                CallOff(front);
                return;
            }
            // Everyone has gathered, the wait is over, or the party has been spotted and is already fighting.
            if (!party.TrueForAll(g => g.IsMustered) && !party.Exists(g => g.IsFighting) &&
                Time.time - front.MusterStartTime < musterTimeoutSeconds) return;

            for (int i = 0; i < party.Count; i++) party[i].Charge(warCry: i == 0);
            front.IsAttacking = true;
            front.AttackStartedAt = clock.TotalHours;
            front.Hamlet.BeginAttack();
            Debug.Log($"[EverRealm] War party of {party.Count} attacks {front.Hamlet.Name}");
        }

        /// <summary>
        /// The attack ends once none of the party is alive and near (allowing a little time for stragglers to
        /// come up), or when it has gone on too long. Any still
        /// out on the land are called home with the rest.
        /// </summary>
        void UpdateAttack(RaidFront front)
        {
            if (!front.IsAttacking) return;
            front.Party.RemoveAll(g => g == null || g.GetComponent<Combatant>().IsDead);
            Vector3 square = front.Hamlet.Settlement.Square;
            bool stragglersDue = clock.TotalHours - front.AttackStartedAt < stragglerHours;
            bool ongoing = front.Party.Exists(g => (stragglersDue && g.IsRaiding) || Steering.FlatDistance(g.transform.position, square) <= attackRadius);
            bool timedOut = clock.TotalHours - front.AttackStartedAt >= attackTimeoutHours;
            if (ongoing && !timedOut) return;

            int fighters = population.FightersPresent(front.Hamlet);
            Debug.Log($"[EverRealm] Attack on {front.Hamlet.Name} over after {clock.TotalHours - front.AttackStartedAt:F1} h" +
                      $"{(ongoing ? " (timed out)" : "")}: {front.Party.Count} raiders left, {fighters} fighters present");
            CallOff(front);
            front.IsAttacking = false;
            front.Hamlet.EndAttack(fighters);
        }

        void CallOff(RaidFront front)
        {
            foreach (GoblinAI goblin in front.Party) goblin.CallOff();
            front.Party.Clear();
        }

        /// <summary>
        /// Sends a war party from the camps nearest the hamlet: 2 goblins per fighter by default. With
        /// <paramref name="fullPartyOnly"/>, sends nobody unless the full party can be mustered. Returns false
        /// if nobody was sent.
        /// </summary>
        public bool LaunchRaid(RaidFront front, bool fullPartyOnly)
        {
            Hamlet hamlet = front.Hamlet;
            Settlement settlement = hamlet.Settlement;
            string when = $"Day {clock.Day} {Mathf.FloorToInt(clock.Hour):00}:00";
            if (front.Party.Count > 0) return false;
            if (hamlet.State != HamletState.Living)
            {
                front.LastRaid = $"{when}: nothing to raid, the hamlet is {hamlet.State}";
                return false;
            }

            int fighters = hamlet.LivingFighters;
            int wanted = Mathf.Max(minPartySize, Mathf.CeilToInt(fighters * goblinsPerFighter));
            // Goblins march in straight lines, so a camp behind a cliff has no way to this hamlet.
            var canMarch = new Dictionary<Vector3, bool>();
            var free = new List<GoblinAI>();
            foreach (GoblinAI goblin in encounters.Goblins)
            {
                if (goblin == null || goblin.IsRaiding || goblin.GetComponent<Combatant>().IsDead ||
                    Steering.FlatDistance(goblin.Home, settlement.Square) > raidReach) continue;
                if (!canMarch.TryGetValue(goblin.Home, out bool clear))
                    canMarch[goblin.Home] = clear = world.CanMarch(goblin.Home, settlement.Square);
                if (clear) free.Add(goblin);
            }
            free.Sort((a, b) => Steering.FlatDistance(a.Home, settlement.Square).CompareTo(Steering.FlatDistance(b.Home, settlement.Square)));

            if (free.Count == 0 || (fullPartyOnly && free.Count < wanted))
            {
                front.LastRaid = $"{when}: camps could muster {free.Count} of {wanted} goblins, raid postponed";
                return false;
            }

            List<GoblinAI> party = free.GetRange(0, Mathf.Min(wanted, free.Count));
            Vector3 muster = MusterPoint(settlement, party);
            var camps = new HashSet<Vector3>();
            foreach (GoblinAI goblin in party)
            {
                goblin.Raid(settlement.Square, muster);
                camps.Add(goblin.Home);
            }
            front.MusterStartTime = Time.time;
            front.Party.AddRange(party);

            front.LastRaid = $"{when}: {party.Count} goblins from {camps.Count} camp{(camps.Count == 1 ? "" : "s")} vs {fighters} fighters";
            Debug.Log($"[EverRealm] Raid on {hamlet.Name}: {front.LastRaid}");
            return true;
        }

        /// <summary>On land <see cref="musterDistance"/> from the square, on the side the party comes from.</summary>
        Vector3 MusterPoint(Settlement settlement, List<GoblinAI> party)
        {
            Vector3 toCamps = Vector3.zero;
            foreach (GoblinAI goblin in party) toCamps += Steering.FlatDirection(settlement.Square, goblin.Home, Vector3.forward);
            float baseAngle = Mathf.Atan2(toCamps.z, toCamps.x) * Mathf.Rad2Deg;

            // Swing round from the camps' side until the point is on land.
            for (int step = 0; step < 12; step++)
            {
                float angle = (baseAngle + (step % 2 == 0 ? 1 : -1) * (step + 1) / 2 * 30f) * Mathf.Deg2Rad;
                Vector3 point = settlement.Square + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * musterDistance;
                if (world.TryGetLandSurface(point.x, point.z, out Vector3 surface)) return surface;
            }
            return settlement.Square;
        }
    }
}
