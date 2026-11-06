using System.Collections.Generic;
using EverRealm.Characters;
using UnityEngine;

namespace EverRealm.Combat
{
    /// <summary>
    /// How an AI fights one target in melee, shared by every brain that fights: chases, keeps spacing at its
    /// weapon's reach and swings when it holds one of the target's attack slots (two attackers per target). Reacts to
    /// a target's wind-up by blocking or dodging, sometimes feints its own swings, and respects its stamina.
    /// The brain picks the target and calls <see cref="Tick"/> every frame; this only uses the commands any
    /// brain has.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor), typeof(Combatant))]
    public sealed class MeleeTactics : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Strafing speed as a fraction of the run gait.")]
        public float strafeFraction = 0.35f;
        public float faceTurnSpeed = 540f;
        [Tooltip("Sprints to close the gap beyond this distance, if stamina allows.")]
        public float sprintDistance = 7f;

        [Header("Fighting")]
        [Tooltip("Preferred distance as a fraction of weapon reach.")]
        public float spacingFactor = 0.8f;
        [Tooltip("Extra distance kept while waiting for another attacker to finish.")]
        public float waitingDistance = 1.5f;
        public float attackCooldownMin = 1f;
        public float attackCooldownMax = 2.2f;
        [Tooltip("Only swings when the target is within this many degrees of its facing.")]
        public float attackFacingAngle = 35f;

        [Header("Reactions to an enemy wind-up")]
        [Range(0f, 1f)] public float blockChance = 0.4f;
        [Range(0f, 1f)] public float dodgeChance = 0.3f;
        [Tooltip("Delay before raising a plain guard, in seconds.")]
        public float blockReactionMin = 0.05f;
        public float blockReactionMax = 0.15f;
        [Tooltip("Delay before dodging, in seconds.")]
        public float dodgeReactionMin = 0.15f;
        public float dodgeReactionMax = 0.35f;
        [Tooltip("Chance a block is instead a timed parry attempt (raised just before the hit; can be mistimed).")]
        [Range(0f, 1f)] public float parryAttemptChance = 0.25f;
        [Tooltip("A parry attempt raises the guard up to this many seconds before the swing begins.")]
        public float parryTimingSpread = 0.25f;
        [Tooltip("Below this stamina fraction, prefers dodging to blocking.")]
        [Range(0f, 1f)] public float lowStaminaFraction = 0.3f;

        [Header("Feints")]
        [Range(0f, 1f)] public float feintChance = 0.2f;

        enum Reaction { None, Block, Dodge }

        /// <summary>How many may swing at one target at a time: outnumbering is dangerous, group fights stay readable.</summary>
        public const int AttackersPerTarget = 2;

        static readonly Dictionary<Combatant, List<MeleeTactics>> AttackSlots = new Dictionary<Combatant, List<MeleeTactics>>();

        CharacterMotor motor;
        Combatant combatant;
        Steering steering;
        bool attacking, holdingBlock, reactedToWindup;
        Reaction plannedReaction;
        float reactAt, feintAt = float.PositiveInfinity;
        float nextAttackTime, nextStrafeFlip, strafeSign = 1f;

        public Combatant Target { get; private set; }

        /// <summary>Close enough to fight rather than chase.</summary>
        public bool InFightRange => Target != null &&
                                    Steering.FlatDistance(transform.position, Target.transform.position) <= combatant.Weapon.reach * 1.6f;

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            combatant = GetComponent<Combatant>();
            steering = new Steering(motor);
        }

        void OnEnable() => combatant.Died += Disengage;
        void OnDisable()
        {
            combatant.Died -= Disengage;
            Disengage();
        }

        public void Engage(Combatant target)
        {
            if (target == Target) return;
            ReleaseSlot();
            Target = target;
        }

        public void Disengage()
        {
            ReleaseSlot();
            Target = null;
            holdingBlock = false;
        }

        /// <summary>
        /// Call every frame. With <paramref name="fight"/> and a target, drives movement toward and around it and
        /// swings; either way keeps the guard, reactions to the target's swings and feints up to date.
        /// </summary>
        public void Tick(bool fight)
        {
            if (attacking && combatant.State is not (CombatState.Windup or CombatState.Active or CombatState.Recovery))
            {
                attacking = false;
                feintAt = float.PositiveInfinity;
                ReleaseSlot();
            }
            if (Target != null && Target.IsDead) Disengage();

            if (fight && Target != null)
            {
                if (InFightRange) Fight();
                else Chase();
            }
            UpdateDefence();
            UpdateFeint();
            combatant.SetBlocking(holdingBlock, Target != null ? FlatDirectionTo(Target.transform.position) : transform.forward);
        }

        // ---- Movement ----

        void Chase()
        {
            float distance = Steering.FlatDistance(transform.position, Target.transform.position);
            bool sprint = distance > sprintDistance && combatant.Stamina.Current > combatant.Stamina.max * 0.5f;
            steering.MoveTowards(Target.transform.position, sprint ? Gait.Sprint : Gait.Run);
        }

        void Fight()
        {
            Vector3 toTarget = Target.transform.position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            Vector3 direction = distance > 0.001f ? toTarget / distance : transform.forward;
            motor.Gait = Gait.Run;
            motor.FaceTowards(direction, faceTurnSpeed);

            bool hasSlot = HoldsSlot();
            float reach = combatant.Weapon.reach + Target.Motor.Radius;
            float preferred = reach * spacingFactor + (hasSlot || SlotFree() ? 0f : waitingDistance);

            // Close or open the gap toward the preferred spacing, and circle a little while waiting.
            if (Time.time >= nextStrafeFlip)
            {
                strafeSign = Random.value < 0.5f ? -1f : 1f;
                nextStrafeFlip = Time.time + Random.Range(1.5f, 3f);
            }
            Vector3 radial = direction * Mathf.Clamp(distance - preferred, -1f, 1f);
            Vector3 tangent = Vector3.Cross(Vector3.up, direction) * (strafeSign * strafeFraction);
            motor.MoveIntent = radial + (attacking || holdingBlock ? Vector3.zero : tangent);

            bool inReach = distance <= reach &&
                           Mathf.Abs(Target.transform.position.y - transform.position.y) <= combatant.maxHitHeightDifference;
            bool facing = Vector3.Angle(transform.forward, direction) <= attackFacingAngle;
            bool affordable = combatant.Stamina.Current >= combatant.Weapon.staminaCost;
            if (!attacking && !holdingBlock && inReach && facing && affordable && combatant.CanAct
                && Time.time >= nextAttackTime && TryTakeSlot())
            {
                if (combatant.TryAttack(direction))
                {
                    attacking = true;
                    nextAttackTime = Time.time + Random.Range(attackCooldownMin, attackCooldownMax);
                    if (Random.value < feintChance)
                        feintAt = Time.time + Random.Range(0.2f, 0.85f) * combatant.Weapon.windup;
                }
                else ReleaseSlot();
            }
        }

        // ---- Defence and feints ----

        void UpdateDefence()
        {
            bool threatSwinging = Target != null && Target.State is CombatState.Windup or CombatState.Active;
            if (!threatSwinging)
            {
                reactedToWindup = false;
                plannedReaction = Reaction.None;
                holdingBlock = false;
                return;
            }

            if (!reactedToWindup && Target.State == CombatState.Windup && IsThreatened())
            {
                reactedToWindup = true;
                plannedReaction = ChooseReaction();
                reactAt = Time.time + ReactionDelay(plannedReaction);
            }

            if (plannedReaction != Reaction.None && Time.time >= reactAt)
            {
                if (plannedReaction == Reaction.Block) holdingBlock = true;
                else if (combatant.CanAct) combatant.TryDodge(DodgeAwayFrom(Target.transform.position));
                plannedReaction = Reaction.None;
            }
        }

        /// <summary>The target is winding up, facing us, and close enough to connect.</summary>
        bool IsThreatened()
        {
            Vector3 fromTarget = transform.position - Target.transform.position;
            fromTarget.y = 0f;
            float reach = Target.Weapon.reach + motor.Radius + 0.5f;
            return fromTarget.magnitude <= reach && Vector3.Angle(Target.transform.forward, fromTarget) <= 60f;
        }

        Reaction ChooseReaction()
        {
            if (attacking) return Reaction.None; // Committed to its own swing (feints aside).
            Stamina stamina = combatant.Stamina;
            bool lowStamina = stamina.Current < stamina.max * lowStaminaFraction;
            bool canAffordBlock = stamina.Current >= combatant.BlockCostAgainst(Target.Weapon);

            float roll = Random.value;
            if (roll < blockChance) return canAffordBlock && !lowStamina ? Reaction.Block : Reaction.Dodge;
            if (roll < blockChance + dodgeChance) return Reaction.Dodge;
            return Reaction.None;
        }

        float ReactionDelay(Reaction reaction)
        {
            if (reaction == Reaction.Dodge) return Random.Range(dodgeReactionMin, dodgeReactionMax);
            if (reaction != Reaction.Block) return 0f;
            if (Random.value >= parryAttemptChance) return Random.Range(blockReactionMin, blockReactionMax);
            // Parry attempt: aim to raise the guard just before the swing begins. Too early is a plain block.
            float untilSwing = Mathf.Max(0f, Target.Weapon.windup - Target.StateTime);
            return Mathf.Max(0f, untilSwing - Random.Range(0f, parryTimingSpread));
        }

        void UpdateFeint()
        {
            if (combatant.State != CombatState.Windup || Time.time < feintAt) return;
            feintAt = float.PositiveInfinity;
            Vector3 from = Target != null ? Target.transform.position : transform.position + transform.forward;
            combatant.TryDodge(DodgeAwayFrom(from));
        }

        /// <summary>Sideways or backwards relative to a threat.</summary>
        Vector3 DodgeAwayFrom(Vector3 threatPosition)
        {
            Vector3 away = -FlatDirectionTo(threatPosition);
            Vector3 side = Vector3.Cross(Vector3.up, away) * (Random.value < 0.5f ? -1f : 1f);
            return Random.value < 0.6f ? side : away;
        }

        Vector3 FlatDirectionTo(Vector3 position) => Steering.FlatDirection(transform.position, position, transform.forward);

        // ---- Attack slots ----

        bool SlotFree()
        {
            if (Target == null) return false;
            if (!AttackSlots.TryGetValue(Target, out List<MeleeTactics> holders)) return true;
            holders.RemoveAll(h => h == null);
            return holders.Count < AttackersPerTarget;
        }

        bool HoldsSlot() => Target != null && AttackSlots.TryGetValue(Target, out List<MeleeTactics> holders) && holders.Contains(this);

        bool TryTakeSlot()
        {
            if (HoldsSlot()) return true;
            if (!SlotFree()) return false;
            if (!AttackSlots.TryGetValue(Target, out List<MeleeTactics> holders)) AttackSlots[Target] = holders = new List<MeleeTactics>();
            holders.Add(this);
            return true;
        }

        void ReleaseSlot()
        {
            if (Target == null || !AttackSlots.TryGetValue(Target, out List<MeleeTactics> holders)) return;
            holders.Remove(this);
            if (holders.Count == 0) AttackSlots.Remove(Target);
        }
    }
}
