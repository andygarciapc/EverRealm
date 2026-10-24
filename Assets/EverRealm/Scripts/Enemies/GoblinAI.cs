using System.Collections.Generic;
using EverRealm.Characters;
using EverRealm.Combat;
using UnityEngine;

namespace EverRealm.Enemies
{
    /// <summary>
    /// Goblin fighter: wanders near its camp, chases a target that comes within aggro range, keeps
    /// spacing at its weapon's reach and swings when it holds the target's attack slot (one attacker
    /// per target at a time). Leashes back to camp if dragged too far.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor), typeof(Combatant))]
    public sealed class GoblinAI : MonoBehaviour
    {
        [Header("Awareness")]
        public float aggroRadius = 14f;
        [Tooltip("Gives up and walks home when this far from camp.")]
        public float leashRadius = 28f;

        [Header("Movement")]
        public float walkSpeed = 2.5f;
        public float chaseSpeed = 4.6f;
        public float strafeSpeed = 1.6f;
        public float wanderRadius = 4f;
        public float faceTurnSpeed = 540f;

        [Header("Fighting")]
        [Tooltip("Preferred distance as a fraction of weapon reach.")]
        public float spacingFactor = 0.8f;
        [Tooltip("Extra distance kept while waiting for another goblin to finish attacking.")]
        public float waitingDistance = 1.5f;
        public float attackCooldownMin = 1f;
        public float attackCooldownMax = 2.2f;
        [Tooltip("Only swings when the target is within this many degrees of its facing.")]
        public float attackFacingAngle = 35f;
        [Tooltip("Corpse lingers this long before being removed.")]
        public float corpseTime = 3f;

        enum Mode { Idle, Chase, Fight, Return }

        // One attacker per target keeps group fights readable.
        static readonly Dictionary<Combatant, GoblinAI> AttackSlots = new Dictionary<Combatant, GoblinAI>();

        CharacterMotor motor;
        Combatant combatant;
        Mode mode;
        bool attacking;
        float nextAttackTime, nextWanderTime, nextStrafeFlip, stuckTime, strafeSign = 1f;
        Vector3 wanderTarget;

        public Combatant Target { get; set; }
        public Vector3 Home { get; set; }
        public float SeaLevel { get; set; } = float.NegativeInfinity;

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            combatant = GetComponent<Combatant>();
            Home = transform.position;
            wanderTarget = Home;
        }

        void OnEnable() => combatant.Died += OnDied;
        void OnDisable()
        {
            combatant.Died -= OnDied;
            ReleaseSlot();
        }

        void OnDied()
        {
            ReleaseSlot();
            Destroy(gameObject, corpseTime);
        }

        void Update()
        {
            float dt = combatant.LocalTime.DeltaTime;
            if (dt <= 0f) return;
            if (combatant.IsDead)
            {
                motor.DesiredVelocity = Vector3.zero;
                return;
            }

            if (attacking && combatant.State != CombatState.Windup && combatant.State != CombatState.Active
                && combatant.State != CombatState.Recovery)
            {
                attacking = false;
                ReleaseSlot();
            }

            UpdateMode();
            switch (mode)
            {
                case Mode.Idle: Wander(); break;
                case Mode.Chase: MoveTowards(Target.transform.position, chaseSpeed); break;
                case Mode.Fight: Fight(); break;
                case Mode.Return: MoveTowards(Home, walkSpeed); break;
            }

            JumpIfStuck(dt);
            if (transform.position.y < SeaLevel - 1.5f) motor.Teleport(Home + Vector3.up);
        }

        void UpdateMode()
        {
            bool targetAlive = Target != null && !Target.IsDead;
            float toTarget = targetAlive ? FlatDistance(transform.position, Target.transform.position) : float.PositiveInfinity;
            float fromHome = FlatDistance(transform.position, Home);
            bool targetNearHome = targetAlive && FlatDistance(Target.transform.position, Home) < leashRadius;

            switch (mode)
            {
                case Mode.Idle:
                    if (toTarget < aggroRadius) mode = Mode.Chase;
                    break;
                case Mode.Chase:
                case Mode.Fight:
                    if (!targetAlive || fromHome > leashRadius) { mode = Mode.Return; ReleaseSlot(); }
                    else mode = toTarget <= combatant.Weapon.reach * 1.6f ? Mode.Fight : Mode.Chase;
                    break;
                case Mode.Return:
                    if (toTarget < aggroRadius && targetNearHome) mode = Mode.Chase;
                    else if (fromHome < 1.5f) mode = Mode.Idle;
                    break;
            }
        }

        void Wander()
        {
            if (Time.time >= nextWanderTime)
            {
                Vector2 offset = Random.insideUnitCircle * wanderRadius;
                wanderTarget = Home + new Vector3(offset.x, 0f, offset.y);
                nextWanderTime = Time.time + Random.Range(2f, 5f);
            }
            if (FlatDistance(transform.position, wanderTarget) > 0.5f) MoveTowards(wanderTarget, walkSpeed);
            else motor.DesiredVelocity = Vector3.zero;
        }

        void Fight()
        {
            Vector3 toTarget = Target.transform.position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            Vector3 direction = distance > 0.001f ? toTarget / distance : transform.forward;
            motor.FaceTowards(direction, faceTurnSpeed);

            bool hasSlot = HoldsSlot();
            float reach = combatant.Weapon.reach + Target.Motor.Radius;
            float preferred = reach * spacingFactor + (hasSlot || SlotFree() ? 0f : waitingDistance);

            // Close or open the gap toward the preferred spacing, and circle a little while waiting.
            Vector3 radial = direction * Mathf.Clamp(distance - preferred, -1f, 1f) * chaseSpeed;
            Vector3 tangent = Vector3.Cross(Vector3.up, direction) * (strafeSign * strafeSpeed);
            if (Time.time >= nextStrafeFlip)
            {
                strafeSign = Random.value < 0.5f ? -1f : 1f;
                nextStrafeFlip = Time.time + Random.Range(1.5f, 3f);
            }
            motor.DesiredVelocity = radial + (attacking ? Vector3.zero : tangent);

            bool inReach = distance <= reach &&
                           Mathf.Abs(Target.transform.position.y - transform.position.y) <= combatant.maxHitHeightDifference;
            bool facing = Vector3.Angle(transform.forward, direction) <= attackFacingAngle;
            if (!attacking && inReach && facing && combatant.CanAct && Time.time >= nextAttackTime && TryTakeSlot())
            {
                if (combatant.TryAttack(direction))
                {
                    attacking = true;
                    nextAttackTime = Time.time + Random.Range(attackCooldownMin, attackCooldownMax);
                }
                else ReleaseSlot();
            }
        }

        void MoveTowards(Vector3 destination, float speed)
        {
            Vector3 direction = destination - transform.position;
            direction.y = 0f;
            motor.DesiredVelocity = direction.sqrMagnitude > 0.01f ? direction.normalized * speed : Vector3.zero;
        }

        /// <summary>Hop up ledges the CharacterController can't step onto.</summary>
        void JumpIfStuck(float dt)
        {
            bool wantsToMove = motor.DesiredVelocity.sqrMagnitude > 1f;
            Vector3 actual = motor.ActualVelocity;
            bool barelyMoving = new Vector2(actual.x, actual.z).sqrMagnitude < 0.2f;
            stuckTime = wantsToMove && barelyMoving && motor.IsGrounded ? stuckTime + dt : 0f;
            if (stuckTime > 0.25f)
            {
                motor.RequestJump();
                stuckTime = 0f;
            }
        }

        bool SlotFree() => Target != null && (!AttackSlots.TryGetValue(Target, out GoblinAI holder) || holder == null);
        bool HoldsSlot() => Target != null && AttackSlots.TryGetValue(Target, out GoblinAI holder) && holder == this;

        bool TryTakeSlot()
        {
            if (HoldsSlot()) return true;
            if (!SlotFree()) return false;
            AttackSlots[Target] = this;
            return true;
        }

        void ReleaseSlot()
        {
            if (HoldsSlot()) AttackSlots.Remove(Target);
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
