using System.Collections.Generic;
using EverRealm.Characters;
using EverRealm.World;
using UnityEngine;

namespace EverRealm.Settlements
{
    /// <summary>
    /// Everyone on the roads. Sends refugees out of abandoned hamlets, colonists to empty ones and newcomers up
    /// from the landing, walks them as parties and decides what happens on arrival: nobody appears from
    /// nowhere, so every person in a hamlet walked there. Keeps the record of who went where, for the ledger.
    /// Runs the same whether or not anyone is watching.
    /// </summary>
    public sealed class JourneyDirector : MonoBehaviour
    {
        [SerializeField] WorldBuilder world;
        [SerializeField] WorldClock clock;
        [SerializeField] HamletPopulation population;

        [Header("Newcomers (provisional)")]
        [Tooltip("In-game hours between boats reaching the landing.")]
        public float newcomerIntervalHours = 18f;
        public float newcomerIntervalJitter = 6f;
        public int minNewcomers = 1;
        public int maxNewcomers = 2;

        [Header("Colonists")]
        public int minColonists = 3;
        public int maxColonists = 5;
        [Tooltip("A hamlet sends colonists only if this many residents stay behind.")]
        public int residentsKeptBehind = 3;

        [Header("On the road")]
        [Tooltip("A party with nobody making progress for this long, while not fighting or fleeing, is stuck and becomes stranded.")]
        public float stuckSeconds = 60f;
        [Tooltip("Members this many road points behind the leader run to catch up.")]
        public int catchUpPoints = 5;

        const int RememberedEvents = 14;

        readonly List<Journey> journeys = new List<Journey>();
        readonly List<string> events = new List<string>();
        System.Random rng;
        double nextBoatAt;
        int lastDawn;

        /// <summary>Parties on the road or stranded.</summary>
        public IReadOnlyList<Journey> Journeys => journeys;
        /// <summary>The most recent things to happen on the roads, newest last.</summary>
        public IReadOnlyList<string> Events => events;
        public int Landed { get; private set; }
        public int ArrivedJourneys { get; private set; }
        public int DeadJourneys { get; private set; }

        void OnEnable()
        {
            population.Populated += ResetRoads;
            population.Abandoned += OnAbandoned;
        }

        void OnDisable()
        {
            population.Populated -= ResetRoads;
            population.Abandoned -= OnAbandoned;
        }

        // The hamlets fill in their own Start, before this subscribes or just after, depending on script order.
        void Start() => ResetRoads();

        void ResetRoads()
        {
            journeys.Clear();
            events.Clear();
            Landed = ArrivedJourneys = DeadJourneys = 0;
            rng = new System.Random(world.Settings.seed * 19 + 11);
            lastDawn = clock.Dawns;
            ScheduleBoat();
        }

        void ScheduleBoat() =>
            nextBoatAt = clock.TotalHours + newcomerIntervalHours + (rng.NextDouble() * 2 - 1) * newcomerIntervalJitter;

        void Update()
        {
            foreach (Journey journey in journeys.ToArray()) Tick(journey);

            if (clock.Dawns != lastDawn)
            {
                lastDawn = clock.Dawns;
                foreach (Journey journey in journeys.ToArray())
                    if (journey.State == JourneyState.Stranded) ReconsiderStranded(journey);
                SendColonists();
            }
            if (clock.TotalHours >= nextBoatAt)
            {
                ScheduleBoat();
                LandNewcomers();
            }
        }

        // ---- Walking ----

        void Tick(Journey journey)
        {
            journey.Review();
            if (journey.IsOver)
            {
                Close(journey);
                return;
            }
            if (journey.State != JourneyState.Walking) return;

            List<IPerson> walkers = journey.Living();
            int leader = 0;
            bool stuck = true;
            foreach (IPerson walker in walkers)
            {
                var villager = (VillagerAI)walker;
                leader = Mathf.Max(leader, villager.RouteIndex);
                if (villager.NoProgressSeconds < stuckSeconds) stuck = false;
            }
            foreach (IPerson walker in walkers)
            {
                var villager = (VillagerAI)walker;
                villager.TravelPace = leader - villager.RouteIndex > catchUpPoints ? Gait.Run : Gait.Walk;
                if (villager.HasArrived) ReachDestination(journey, villager);
                if (journey.State != JourneyState.Walking) return;
            }
            if (stuck)
            {
                Note($"{Describe(journey)} stuck on the road at {((VillagerAI)walkers[0]).Position:F0}");
                Strand(journey, ((VillagerAI)walkers[0]).Position);
            }
        }

        void ReachDestination(Journey journey, VillagerAI villager)
        {
            Hamlet here = journey.Destination;
            journey.Witness(here);
            TellNews(journey, villager, here);

            if (journey.TryMoveIn(villager, out House house, out int spot))
            {
                population.GiveHome(villager, here, house, spot);
                Note($"{villager.name} of the {journey.Kind.ToString().ToLowerInvariant()} from {journey.Origin} settles in {here.Name}");
                return;
            }
            Hamlet next = Journey.NearestViable(population.Hamlets, journey.Kind, here, RoadDistanceFrom(villager.Position));
            Note($"{Describe(journey)}: {here.Name} won't do ({here.State}, {here.FreeBeds} beds), {(next != null ? $"going on to {next.Name}" : "nowhere left to go")}");
            if (!TrySend(journey, next, villager.Position)) Strand(journey, villager.Position);
        }

        /// <summary>News is told once in each hamlet with people left to hear it.</summary>
        void TellNews(Journey journey, VillagerAI villager, Hamlet here)
        {
            if (journey.ToldNewsIn == here || here.LivingResidents == 0) return;
            journey.ToldNewsIn = here;
            string line = journey.NewsLine(here);
            villager.GetComponent<Speech>().Say(line, Volume.Shout);
            Note($"News reaches {here.Name}: \"{line}\"");
        }

        void Close(Journey journey)
        {
            journeys.Remove(journey);
            if (journey.State == JourneyState.Arrived)
            {
                ArrivedJourneys++;
                Note($"{journey.Kind} from {journey.Origin} arrived: {journey.Settled} of {journey.SetOutWith} found homes");
            }
            else
            {
                DeadJourneys++;
                Note($"{journey.Kind} from {journey.Origin} all died on the road, and their news with them");
            }
        }

        // ---- Setting out ----

        void OnAbandoned(Hamlet from, List<VillagerAI> leaving)
        {
            if (leaving.Count == 0) return;
            var journey = new Journey(JourneyKind.Refugees, from.Name, leaving);
            journey.Witness(from);
            journeys.Add(journey);
            Vector3 start = from.Settlement.Square;
            Hamlet next = Journey.NearestOther(population.Hamlets, from, RoadDistanceFrom(start));
            Note($"{leaving.Count} refugees leave {from.Name}{(next != null ? $" for {next.Name}" : "")}");
            if (!TrySend(journey, next, start)) Strand(journey, start);
        }

        /// <summary>Puts everyone on the road to <paramref name="destination"/>. False if it's null or no road leads there.</summary>
        bool TrySend(Journey journey, Hamlet destination, Vector3 from)
        {
            if (destination == null || world.Roads == null ||
                !world.Roads.TryFindRoute(from, destination.Settlement.Square, out List<Vector3> route, out _)) return false;
            journey.SetCourse(destination, route);
            foreach (IPerson member in journey.Living()) ((VillagerAI)member).SetOff(route, world.Settlements);
            return true;
        }

        void Strand(Journey journey, Vector3 camp)
        {
            journey.Strand(camp);
            foreach (IPerson member in journey.Living()) ((VillagerAI)member).MakeCamp(camp);
            Note($"{Describe(journey)} stranded");
        }

        void ReconsiderStranded(Journey journey)
        {
            Hamlet next = journey.ChooseAtDawn(population.Hamlets, RoadDistanceFrom(journey.Camp));
            if (next == null) return;
            if (TrySend(journey, next, journey.Camp))
                Note($"Stranded {journey.Kind.ToString().ToLowerInvariant()} from {journey.Origin} set out for {next.Name}");
        }

        /// <summary>Each due empty hamlet gets colonists from the nearest living hamlet that can spare them. Stranded parties have already had first refusal.</summary>
        void SendColonists()
        {
            foreach (Hamlet empty in population.Hamlets)
            {
                if (empty.State != HamletState.Empty || clock.TotalHours < empty.ColonistsDueAt) continue;
                if (journeys.Exists(j => j.Destination == empty && j.Kind == JourneyKind.Colonists && j.State == JourneyState.Walking)) continue;

                System.Func<Hamlet, float> distance = RoadDistanceFrom(empty.Settlement.Square);
                var donors = new List<Hamlet>();
                foreach (Hamlet hamlet in population.Hamlets)
                    if (hamlet != empty && hamlet.State == HamletState.Living) donors.Add(hamlet);
                donors.Sort((a, b) => distance(a).CompareTo(distance(b)));

                foreach (Hamlet donor in donors)
                {
                    var free = donor.Residents().FindAll(r => ((VillagerAI)r).enabled);
                    List<IPerson> colonists = Journey.ChooseColonists(free, rng.Next(minColonists, maxColonists + 1), residentsKeptBehind);
                    if (colonists.Count == 0) continue;

                    var journey = new Journey(JourneyKind.Colonists, donor.Name, colonists);
                    foreach (IPerson colonist in colonists) donor.MoveOut(colonist);
                    journeys.Add(journey);
                    Note($"{colonists.Count} colonists leave {donor.Name} for {empty.Name}");
                    if (!TrySend(journey, empty, donor.Settlement.Square)) Strand(journey, donor.Settlement.Square);
                    break;
                }
            }
        }

        void LandNewcomers()
        {
            if (!world.Landing.HasValue) return;
            Vector3 landing = world.Landing.Value;
            Hamlet destination = Journey.ChooseForNewcomers(population.Hamlets, rng.Next, RoadDistanceFrom(landing), out JourneyKind kind);
            if (destination == null) return;

            int count = kind == JourneyKind.Colonists ? rng.Next(minColonists, maxColonists + 1) : rng.Next(minNewcomers, maxNewcomers + 1);
            var people = new List<VillagerAI>();
            for (int i = 0; i < count; i++)
            {
                double angle = rng.NextDouble() * Mathf.PI * 2;
                Vector3 spot = landing + new Vector3((float)System.Math.Cos(angle), 0f, (float)System.Math.Sin(angle)) * 1.5f;
                people.Add(population.SpawnPerson("Newcomer", population.RollTemperament(), spot));
            }
            Landed += count;
            var journey = new Journey(kind, "the landing", people);
            journeys.Add(journey);
            Note($"{count} {kind.ToString().ToLowerInvariant()} land, bound for {destination.Name}");
            if (!TrySend(journey, destination, landing)) Strand(journey, landing);
        }

        // ---- Record ----

        System.Func<Hamlet, float> RoadDistanceFrom(Vector3 from) => hamlet =>
            world.Roads != null && world.Roads.TryFindRoute(from, hamlet.Settlement.Square, out _, out float length) ? length : float.PositiveInfinity;

        static string Describe(Journey journey) => $"{journey.LivingMembers} {journey.Kind.ToString().ToLowerInvariant()} from {journey.Origin}";

        void Note(string text)
        {
            string line = $"Day {clock.Day} {Mathf.FloorToInt(clock.Hour):00}:{Mathf.FloorToInt(clock.Hour % 1f * 60f):00}  {text}";
            Debug.Log($"[EverRealm] {line}");
            events.Add(line);
            if (events.Count > RememberedEvents) events.RemoveAt(0);
        }
    }
}
