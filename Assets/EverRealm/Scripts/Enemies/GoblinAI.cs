using EverRealm.Characters;
using EverRealm.Combat;
using UnityEngine;

namespace EverRealm.Enemies
{
    /// <summary>
    /// Goblin brain. Perceives the nearest hostile agent through <see cref="AgentDirectory"/>, wanders near
    /// its camp, and fights through <see cref="MeleeTactics"/>. Sent on a raid, it runs to the war party's
    /// muster point, waits for the order to attack, then marches on the target, fights whoever it finds there
    /// and goes home once nobody is left nearby. Uses only the commands any brain has.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor), typeof(Combatant), typeof(Agent))]
    [RequireComponent(typeof(MeleeTactics))]
    public sealed class GoblinAI : MonoBehaviour
    {
        [Header("Awareness")]
        public float aggroRadius = 14f;
        [Tooltip("Gives up and walks home when this far from camp (or from the muster point or raid target while raiding).")]
        public float leashRadius = 28f;

        [Header("Movement")]
        public float wanderRadius = 4f;

        [Header("Raids")]
        [Tooltip("Seconds spent at the raid target without finding anyone before marching home.")]
        public float raidLingerSeconds = 20f;

        enum Mode { Idle, Fight, Return, Raid }

        Combatant combatant;
        MeleeTactics tactics;
        Steering steering;
        Agent self;
        Mode mode;
        float nextWanderTime, raidIdleTime, raidLeash;
        Vector3 wanderTarget, raidTarget, musterPoint;

        public Vector3 Home { get; set; }
        public bool IsRaiding { get; private set; }
        /// <summary>On a raid, still gathering with the rest of the war party.</summary>
        public bool IsMustering { get; private set; }
        /// <summary>Waiting at the muster point for the order to attack.</summary>
        public bool IsMustered => IsMustering && Steering.FlatDistance(transform.position, musterPoint) < 3f;

        public bool IsFighting => mode == Mode.Fight;

        /// <summary>Where the leash is anchored: the raid target while raiding, otherwise home.</summary>
        Vector3 Anchor => IsRaiding ? raidTarget : Home;
        /// <summary>While raiding the leash reaches past the muster point, so a fight there doesn't end the raid.</summary>
        float Leash => IsRaiding ? raidLeash : leashRadius;
        Vector3 MarchTarget => IsMustering ? musterPoint : raidTarget;

        void Awake()
        {
            combatant = GetComponent<Combatant>();
            tactics = GetComponent<MeleeTactics>();
            steering = new Steering(GetComponent<CharacterMotor>());
            self = GetComponent<Agent>();
            Home = transform.position;
            wanderTarget = Home;
        }

        void OnDisable() => tactics.Disengage();

        /// <summary>Gather at <paramref name="muster"/>, then (on <see cref="Charge"/>) attack whoever is at <paramref name="target"/>.</summary>
        public void Raid(Vector3 target, Vector3 muster)
        {
            raidTarget = target;
            musterPoint = muster;
            raidLeash = Steering.FlatDistance(muster, target) + leashRadius;
            IsMustering = true;
            raidIdleTime = 0f;
            IsRaiding = true;
            if (mode != Mode.Fight) mode = Mode.Raid;
        }

        /// <summary>The war party has gathered: march on the raid target, with a war cry if <paramref name="warCry"/>.</summary>
        public void Charge(bool warCry)
        {
            IsMustering = false;
            if (warCry && !combatant.IsDead && TryGetComponent(out Speech speech)) speech.Say("Raaah!", Volume.Shout);
        }

        /// <summary>The raid is over, for whatever reason: head home, leaving any fight behind.</summary>
        public void CallOff()
        {
            if (!IsRaiding && mode != Mode.Fight) return;
            tactics.Disengage();
            EndRaid();
            mode = Mode.Return;
        }

        void Update()
        {
            if (combatant.IsDead || combatant.LocalTime.DeltaTime <= 0f) return;
            float dt = combatant.LocalTime.DeltaTime;

            Perceive();
            UpdateMode(dt);
            switch (mode)
            {
                case Mode.Idle: Wander(); break;
                case Mode.Return: steering.MoveTowards(Home, Gait.Walk); break;
                case Mode.Raid: MarchOnRaidTarget(); break;
            }
            tactics.Tick(mode == Mode.Fight); // Reacts to the target's swings even while walking home.
            steering.JumpIfStuck(dt);
        }

        // ---- Perception and modes ----

        void Perceive()
        {
            Combatant target = tactics.Target;
            if (target != null && !target.IsDead && Steering.FlatDistance(transform.position, target.transform.position) <= leashRadius) return;
            Agent nearest = AgentDirectory.FindNearest(transform.position, aggroRadius,
                a => a != self && a.IsAlive && self.IsHostileTo(a) && a.Combatant != null);
            if (nearest != null) tactics.Engage(nearest.Combatant);
            else tactics.Disengage();
        }

        void UpdateMode(float dt)
        {
            Combatant target = tactics.Target;
            bool hasTarget = target != null;
            float fromAnchor = Steering.FlatDistance(transform.position, Anchor);

            switch (mode)
            {
                case Mode.Idle:
                case Mode.Raid:
                    if (hasTarget)
                    {
                        mode = Mode.Fight;
                        raidIdleTime = 0f;
                    }
                    break;
                case Mode.Fight:
                    if (hasTarget && fromAnchor <= Leash) break;
                    tactics.Disengage();
                    mode = IsRaiding && fromAnchor <= Leash ? Mode.Raid : Mode.Return;
                    if (mode == Mode.Return) EndRaid();
                    break;
                case Mode.Return:
                    if (hasTarget && Steering.FlatDistance(target.transform.position, Home) < leashRadius) mode = Mode.Fight;
                    else if (Steering.FlatDistance(transform.position, Home) < 1.5f) mode = Mode.Idle;
                    break;
            }

            if (mode == Mode.Raid && !IsMustering && Steering.FlatDistance(transform.position, raidTarget) < 4f)
            {
                raidIdleTime += dt;
                if (raidIdleTime >= raidLingerSeconds)
                {
                    EndRaid();
                    mode = Mode.Return;
                }
            }
        }

        void EndRaid()
        {
            IsRaiding = false;
            IsMustering = false;
        }

        void Wander()
        {
            if (Time.time >= nextWanderTime)
            {
                Vector2 offset = Random.insideUnitCircle * wanderRadius;
                wanderTarget = Home + new Vector3(offset.x, 0f, offset.y);
                nextWanderTime = Time.time + Random.Range(2f, 5f);
            }
            if (Steering.FlatDistance(transform.position, wanderTarget) > 0.5f) steering.MoveTowards(wanderTarget, Gait.Walk);
            else steering.Stop();
        }

        void MarchOnRaidTarget()
        {
            if (Steering.FlatDistance(transform.position, MarchTarget) > 2f) steering.MoveTowards(MarchTarget, Gait.Run);
            else steering.Stop();
        }
    }
}
