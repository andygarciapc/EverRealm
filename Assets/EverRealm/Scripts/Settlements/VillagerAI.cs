using EverRealm.Characters;
using EverRealm.Combat;
using EverRealm.World;
using UnityEngine;

namespace EverRealm.Settlements
{
    public enum Temperament { Civilian, Militia }

    /// <summary>
    /// Villager brain. Follows the world clock: works at its work spot by day, gathers on the square in the
    /// evening and sleeps at home at night, using doorways and walking round houses in the way. When danger
    /// comes close (a hostile in sight, the sound of fighting, or an ally's alarm) militia fight through
    /// <see cref="MeleeTactics"/> until they break (some never do); civilians run home, or away if the danger
    /// is between them and home, though some turn and fight when cornered. Whoever sees the danger first
    /// raises the alarm. Disabled while the player controls the body. Uses only the commands any brain has.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor), typeof(Combatant), typeof(Agent))]
    [RequireComponent(typeof(MeleeTactics), typeof(Speech))]
    public sealed class VillagerAI : MonoBehaviour
    {
        [Header("Awareness")]
        public float sightRadius = 14f;
        [Tooltip("Hears fighting (a hostile mid-swing) this far away.")]
        public float hearingRadius = 22f;
        [Tooltip("Militia defend anywhere within this distance of the square, and anywhere when attacked up close.")]
        public float defendRadius = 30f;
        public float selfDefenceRadius = 4f;
        [Tooltip("Seconds without a threat before going back to the day's routine.")]
        public float calmDownSeconds = 8f;
        [Tooltip("Civilians stay hidden this long after the last sign of danger before going back to the day's routine.")]
        public float hideSeconds = 30f;
        [Tooltip("Shortest time between two alarm shouts from the same villager.")]
        public float shoutCooldownSeconds = 10f;

        [Header("Fighting")]
        [Tooltip("A cornered civilian keeps fighting until its foe dies or gets this far away (hysteresis over the self-defence radius).")]
        public float cornerReleaseRadius = 8f;

        [Header("Routine")]
        public float workLoiterRadius = 2f;
        public float gatherRadius = 4f;
        public float arriveDistance = 0.6f;

        // Body radius plus a little, so paths don't graze walls.
        const float WallMargin = 0.45f;

        Combatant combatant;
        MeleeTactics tactics;
        Steering steering;
        Agent self;
        Speech speech;
        CombatVisuals visuals;
        WorldClock clock;
        Settlement settlement;
        float lastThreatTime = float.NegativeInfinity, lastShoutTime = float.NegativeInfinity, nextIdleTime;
        Vector3 threatPosition, idleOffset;
        bool leavingThroughDoor, enteringThroughDoor, cornered, broken;

        public Temperament Temperament { get; private set; }
        /// <summary>Breaks off and flees below this fraction of health; 0 fights to the death.</summary>
        public float BreakHealthFraction { get; private set; }
        /// <summary>A civilian that turns and fights when a hostile corners it.</summary>
        public bool FightsWhenCornered { get; private set; }
        public House Home { get; private set; }
        /// <summary>Its own sleeping spot inside <see cref="Home"/>.</summary>
        public Vector3 Bed { get; private set; }
        public Vector3 WorkSpot { get; private set; }
        public bool IsAssigned => clock != null;
        public bool IsAlive => !combatant.IsDead;
        /// <summary>What it's doing right now, for debug readouts.</summary>
        public string Activity { get; private set; } = "Idle";

        void Awake()
        {
            combatant = GetComponent<Combatant>();
            tactics = GetComponent<MeleeTactics>();
            steering = new Steering(GetComponent<CharacterMotor>());
            self = GetComponent<Agent>();
            speech = GetComponent<Speech>();
            visuals = GetComponent<CombatVisuals>();
        }

        void OnEnable() => Speech.Spoken += OnHeard;

        void OnDisable()
        {
            Speech.Spoken -= OnHeard;
            tactics.Disengage();
            steering.Stop();
        }

        /// <summary>Makes this body a resident: where it lives and works, and how it meets danger.</summary>
        public void Assign(WorldClock worldClock, Settlement home, House house, Vector3 bed, Vector3 workSpot,
                           Temperament temperament, float breakHealthFraction, bool fightsWhenCornered)
        {
            clock = worldClock;
            settlement = home;
            Home = house;
            Bed = bed;
            WorkSpot = workSpot;
            Temperament = temperament;
            BreakHealthFraction = breakHealthFraction;
            FightsWhenCornered = fightsWhenCornered;
            cornered = broken = false;
            ShowWeapon(temperament == Temperament.Militia);
        }

        void Update()
        {
            float dt = combatant.LocalTime.DeltaTime;
            if (!IsAssigned || combatant.IsDead || dt <= 0f) return;

            Agent threat = Perceive();
            if (threat != null)
            {
                // Seeing danger nobody has warned us about raises the alarm; hearing one doesn't, so alarms don't echo.
                if (Time.time - lastThreatTime >= calmDownSeconds && Time.time - lastShoutTime >= shoutCooldownSeconds)
                    RaiseAlarm();
                lastThreatTime = Time.time;
                threatPosition = threat.transform.position;
            }
            float calmAfter = Temperament == Temperament.Civilian ? hideSeconds : calmDownSeconds;
            bool alarmed = Time.time - lastThreatTime < calmAfter;
            bool canFight = UpdateCourage(threat);

            bool closeIn = false;
            if (threat != null && canFight && (cornered || WillDefendAgainst(threat)))
            {
                tactics.Engage(threat.Combatant);
                Activity = cornered ? "Cornered" : "Fighting";
                // Foe on the other side of a wall (one of us indoors): go round through the doorways, not into the wall.
                Vector3 foe = threat.transform.position;
                House mine = settlement?.HouseContaining(transform.position);
                House theirs = settlement?.HouseContaining(foe);
                if (mine != theirs) NavigateTo(foe, theirs, Gait.Run);
                else closeIn = true;
            }
            else
            {
                tactics.Disengage();
                if (alarmed && canFight) Rally();
                else if (alarmed) Flee();
                else FollowRoutine();
            }
            tactics.Tick(closeIn);
            steering.JumpIfStuck(dt);
        }

        // ---- Courage ----

        /// <summary>
        /// Whether it will fight right now. Militia fight until they break (once broken they stay broken).
        /// Civilians only fight once cornered, and a cornered civilian keeps fighting that foe until it dies,
        /// gets beyond <see cref="cornerReleaseRadius"/> or the civilian breaks, so stepping back out of the
        /// self-defence radius mid-fight doesn't flip it back to fleeing.
        /// </summary>
        bool UpdateCourage(Agent threat)
        {
            if (!broken && combatant.Health <= combatant.maxHealth * BreakHealthFraction)
            {
                broken = true;
                speech.Say("Fall back!", Volume.Talk);
            }
            if (Temperament == Temperament.Militia) return !broken;

            float distance = threat != null ? Distance(threat.transform.position) : float.PositiveInfinity;
            if (cornered && (broken || distance > cornerReleaseRadius))
            {
                cornered = false;
                ShowWeapon(false);
            }
            else if (!cornered && !broken && FightsWhenCornered && distance <= selfDefenceRadius)
            {
                cornered = true;
                ShowWeapon(true);
                speech.Say("Get back!", Volume.Talk);
            }
            return cornered;
        }

        void ShowWeapon(bool visible)
        {
            if (visuals != null) visuals.SetWeaponVisible(visible);
        }

        // ---- Danger ----

        void RaiseAlarm()
        {
            lastShoutTime = Time.time;
            speech.Say(Temperament == Temperament.Militia ? "Goblins!" : "Help!", Volume.Shout, SpeechIntent.Alarm);
        }

        /// <summary>An ally's alarm in earshot puts us on guard as if we'd seen the danger near them.</summary>
        void OnHeard(Utterance line)
        {
            if (!IsAssigned || combatant.IsDead || line.Intent != SpeechIntent.Alarm) return;
            if (line.Speaker == speech || line.Speaker == null || !self.IsAlliedWith(line.Speaker.Agent)) return;
            if (!line.CanBeHeardAt(speech.MouthPosition)) return;
            lastThreatTime = Time.time;
            threatPosition = line.Position;
        }

        /// <summary>The current foe if still close, else the nearest hostile in sight or heard fighting.</summary>
        Agent Perceive()
        {
            Combatant foe = tactics.Target;
            if (foe != null && !foe.IsDead && Distance(foe.transform.position) <= hearingRadius) return foe.Agent;
            return AgentDirectory.FindNearest(transform.position, hearingRadius,
                a => a != self && a.IsAlive && self.IsHostileTo(a) && a.Combatant != null &&
                     (Distance(a.transform.position) <= sightRadius ||
                      a.Combatant.State is CombatState.Windup or CombatState.Active or CombatState.Recovery));
        }

        bool WillDefendAgainst(Agent threat) =>
            settlement == null ||
            Steering.FlatDistance(threat.transform.position, settlement.Square) <= defendRadius ||
            Distance(threat.transform.position) <= selfDefenceRadius;

        /// <summary>Militia with nobody in reach hold the square.</summary>
        void Rally()
        {
            Activity = "Rallying";
            if (settlement != null) NavigateTo(settlement.Square + idleOffset * 0.5f, null, Gait.Run);
            else steering.Stop();
        }

        void Flee()
        {
            Activity = "Fleeing";
            Gait gait = combatant.Stamina.Current > combatant.Stamina.max * 0.25f ? Gait.Sprint : Gait.Run;
            if (Home != null && (Home.Contains(transform.position) || HomeIsSafe()))
            {
                if (NavigateTo(Bed, Home, gait)) Activity = "Hiding";
                return;
            }
            Vector3 away = Steering.FlatDirection(threatPosition, transform.position, transform.forward);
            steering.MoveTowards(transform.position + away * 5f, gait);
        }

        /// <summary>Home is safe to run to if we'd reach its door before the threat does.</summary>
        bool HomeIsSafe() =>
            Steering.FlatDistance(threatPosition, Home.Door) > Distance(Home.Door) + 1f;

        // ---- Routine ----

        void FollowRoutine()
        {
            switch (clock.Phase)
            {
                case DayPhase.Day:
                    Activity = "Working";
                    Loiter(WorkSpot, workLoiterRadius);
                    break;
                case DayPhase.Evening:
                    Activity = "Gathering";
                    Loiter(settlement != null ? settlement.Square : WorkSpot, gatherRadius);
                    break;
                default:
                    if (Home == null)
                    {
                        Activity = "Resting";
                        steering.Stop();
                    }
                    else Activity = NavigateTo(Bed, Home, Gait.Walk) ? "Sleeping" : "Going home";
                    break;
            }
        }

        void Loiter(Vector3 centre, float radius)
        {
            if (Time.time >= nextIdleTime)
            {
                Vector2 offset = Random.insideUnitCircle * radius;
                idleOffset = new Vector3(offset.x, 0f, offset.y);
                nextIdleTime = Time.time + Random.Range(3f, 7f);
            }
            NavigateTo(centre + idleOffset, null, Gait.Walk);
        }

        // ---- Getting around ----

        /// <summary>
        /// Walks toward <paramref name="destination"/> (inside <paramref name="destinationHouse"/>, if given),
        /// leaving and entering houses through their doorways and walking round houses in the way.
        /// Returns true once there.
        /// </summary>
        bool NavigateTo(Vector3 destination, House destinationHouse, Gait gait)
        {
            Vector3 position = transform.position;
            House current = settlement != null ? settlement.HouseContaining(position) : null;

            if (current != null && current != destinationHouse)
            {
                // Out through the doorway: to the point just inside it, then straight out.
                if (!leavingThroughDoor && Distance(current.Inside) > arriveDistance)
                {
                    steering.MoveTowards(current.Inside, gait);
                    return false;
                }
                leavingThroughDoor = true;
                steering.MoveTowards(current.Door, gait);
                return false;
            }
            leavingThroughDoor = false;

            if (destinationHouse != null && current != destinationHouse)
            {
                // In through the doorway: to the point just outside it, then straight in.
                if (Distance(destinationHouse.Door) > 2.5f) enteringThroughDoor = false;
                if (!enteringThroughDoor && Distance(destinationHouse.Door) > arriveDistance)
                {
                    MoveAroundHouses(destinationHouse.Door, gait);
                    return false;
                }
                enteringThroughDoor = true;
                steering.MoveTowards(destinationHouse.Inside, gait);
                return false;
            }
            enteringThroughDoor = false;

            if (Distance(destination) <= arriveDistance)
            {
                steering.Stop();
                return true;
            }
            if (current != null) steering.MoveTowards(destination, gait); // Inside one room: always a straight line.
            else MoveAroundHouses(destination, gait);
            return false;
        }

        /// <summary>Straight there if no house is in the way, otherwise round the corner of the house that is.</summary>
        void MoveAroundHouses(Vector3 destination, Gait gait)
        {
            Vector3 position = transform.position;
            if (settlement == null || settlement.IsPathClear(position, destination, WallMargin))
                steering.MoveTowards(destination, gait);
            else if (settlement.TryDetour(position, destination, WallMargin, out Vector3 corner))
                steering.MoveTowards(corner, gait);
            else
                steering.MoveTowards(settlement.Square, gait);
        }

        float Distance(Vector3 point) => Steering.FlatDistance(transform.position, point);
    }
}
