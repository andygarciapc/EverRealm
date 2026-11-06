using System.Collections.Generic;
using EverRealm.Characters;
using EverRealm.Combat;
using EverRealm.Settlements;
using EverRealm.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EverRealm.Enemies
{
    /// <summary>
    /// Sends goblin war parties against the hamlet on a world-time schedule, whether or not anyone is
    /// watching. A party outnumbers the hamlet's fighters, drawn from the camps nearest the hamlet; it gathers
    /// at a muster point out of the villagers' sight and attacks together. If the camps can't muster a full
    /// party yet, the raid waits until they can. Y raids now (debug), with whatever goblins are free.
    /// </summary>
    public sealed class RaidDirector : MonoBehaviour
    {
        [SerializeField] WorldBuilder world;
        [SerializeField] WorldClock clock;
        [SerializeField] EncounterSpawner encounters;
        [SerializeField] HamletPopulation population;

        [Tooltip("In-game hours from the start (or a new island) to the first raid.")]
        public float firstRaidHours = 10f;
        [Tooltip("In-game hours between raids.")]
        public float raidIntervalHours = 30f;
        [Tooltip("Each interval varies by up to this many hours either way.")]
        public float raidIntervalJitter = 6f;

        [Header("War party")]
        [Tooltip("Goblins sent per living hamlet fighter (militia, including armed newcomers), rounded up.")]
        public float goblinsPerFighter = 1.5f;
        public int minPartySize = 2;
        [Tooltip("If the camps can't muster a full party, the raid is tried again this many in-game hours later.")]
        public float postponeHours = 4f;
        [Tooltip("The party gathers this far from the square, beyond the militia's defence radius.")]
        public float musterDistance = 36f;
        [Tooltip("Seconds the gathered party waits for stragglers before attacking anyway.")]
        public float musterTimeoutSeconds = 90f;

        // Dev-only key, kept out of the player's action map.
        readonly InputAction raidAction = new InputAction("Raid Now", binding: "<Keyboard>/y");
        readonly List<GoblinAI> mustering = new List<GoblinAI>();
        double nextRaidAt;
        float musterStartTime;

        public double HoursUntilNextRaid => nextRaidAt - clock.TotalHours;
        public string LastRaid { get; private set; } = "none yet";
        /// <summary>A war party is still gathering before it attacks.</summary>
        public bool IsMustering => mustering.Count > 0;

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

        void OnEnable()
        {
            world.Generated += ResetSchedule;
            raidAction.performed += OnRaidPressed;
            raidAction.Enable();
        }

        void OnDisable()
        {
            world.Generated -= ResetSchedule;
            raidAction.performed -= OnRaidPressed;
            raidAction.Disable();
        }

        void Start() => ResetSchedule();

        void ResetSchedule()
        {
            nextRaidAt = clock.TotalHours + firstRaidHours;
            LastRaid = "none yet";
            mustering.Clear();
        }

        void OnRaidPressed(InputAction.CallbackContext _) => LaunchRaid(fullPartyOnly: false);

        void Update()
        {
            UpdateMuster();
            if (clock.TotalHours < nextRaidAt) return;
            bool launched = LaunchRaid(fullPartyOnly: true);
            nextRaidAt = clock.TotalHours + (launched
                ? raidIntervalHours + Random.Range(-raidIntervalJitter, raidIntervalJitter)
                : postponeHours);
        }

        /// <summary>Once everyone still raiding has gathered (or waited long enough), the party attacks.</summary>
        void UpdateMuster()
        {
            mustering.RemoveAll(g => g == null || !g.IsRaiding || !g.IsMustering || g.GetComponent<Combatant>().IsDead);
            if (mustering.Count == 0) return;
            // Everyone has gathered, the wait is over, or the party has been spotted and is already fighting.
            if (!mustering.TrueForAll(g => g.IsMustered) && !mustering.Exists(g => g.IsFighting) &&
                Time.time - musterStartTime < musterTimeoutSeconds) return;
            for (int i = 0; i < mustering.Count; i++) mustering[i].Charge(warCry: i == 0);
            Debug.Log($"[EverRealm] War party of {mustering.Count} attacks the hamlet");
            mustering.Clear();
        }

        /// <summary>
        /// Sends a war party from the camps nearest the hamlet: 1.5 goblins per fighter by default. With
        /// <paramref name="fullPartyOnly"/>, sends nobody unless the full party can be mustered. Returns false
        /// if nobody was sent.
        /// </summary>
        public bool LaunchRaid(bool fullPartyOnly)
        {
            Settlement settlement = world.Settlement;
            string when = $"Day {clock.Day} {Mathf.FloorToInt(clock.Hour):00}:00";
            if (settlement == null)
            {
                LastRaid = $"{when}: no hamlet to raid";
                return false;
            }
            if (population.CountAlive() == 0)
            {
                LastRaid = $"{when}: the hamlet is empty, nobody to raid";
                return false;
            }

            int fighters = population.CountAlive(Temperament.Militia);
            int wanted = Mathf.Max(minPartySize, Mathf.CeilToInt(fighters * goblinsPerFighter));
            var free = new List<GoblinAI>();
            foreach (GoblinAI goblin in encounters.Goblins)
                if (goblin != null && !goblin.IsRaiding && !goblin.GetComponent<Combatant>().IsDead) free.Add(goblin);
            free.Sort((a, b) => Steering.FlatDistance(a.Home, settlement.Square).CompareTo(Steering.FlatDistance(b.Home, settlement.Square)));

            if (free.Count == 0 || (fullPartyOnly && free.Count < wanted))
            {
                LastRaid = $"{when}: camps could muster {free.Count} of {wanted} goblins, raid postponed";
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
            if (mustering.Count == 0) musterStartTime = Time.time;
            mustering.AddRange(party);

            LastRaid = $"{when}: {party.Count} goblins from {camps.Count} camp{(camps.Count == 1 ? "" : "s")} vs {fighters} fighters";
            Debug.Log($"[EverRealm] Raid on the hamlet — {LastRaid}");
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
