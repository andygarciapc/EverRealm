using System;
using System.Collections.Generic;
using EverRealm.Characters;
using UnityEngine;

namespace EverRealm.Combat
{
    public enum CombatState { Idle, Blocking, Windup, Active, Recovery, Dodging, Staggered, Dead }

    /// <summary>
    /// Combat actions and health for any agent: a single telegraphed swing (feintable during wind-up),
    /// hold-to-block with parry timing, weight-class dodges, and permanent death. Brains issue commands
    /// (<see cref="TryAttack"/>, <see cref="SetBlocking"/>, <see cref="TryDodge"/>). Actions spend from the
    /// body's <see cref="Stamina"/> and restrict locomotion via <see cref="CharacterMotor.Restriction"/>.
    /// Timing runs on the character's <see cref="LocalTime"/> so hit-stop only freezes those involved.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor), typeof(LocalTime), typeof(Stamina))]
    [RequireComponent(typeof(Agent))]
    public sealed class Combatant : MonoBehaviour
    {
        [SerializeField] WeaponData weapon;
        public float maxHealth = 100f;

        [Header("Blocking")]
        [Tooltip("Width of the protected arc in front of the character, in degrees.")]
        public float blockArc = 140f;
        [Tooltip("A block raised within this many seconds before the hit is a parry.")]
        public float parryWindow = 0.15f;
        [Tooltip("Stamina spent per point of blocked damage.")]
        public float blockStaminaPerDamage = 1.2f;
        public float blockMoveMultiplier = 0.45f;
        [Tooltip("Fraction of knockback that still gets through a block.")]
        public float blockKnockbackMultiplier = 0.4f;
        [Tooltip("Degrees per second the character turns to keep its guard facing the aim.")]
        public float blockTurnSpeed = 540f;

        [Header("Staggers (seconds)")]
        public float hitStagger = 0.35f;
        public float guardBreakStagger = 0.9f;
        [Tooltip("Applied to the attacker when its swing is parried.")]
        public float parryStagger = 1f;
        [Tooltip("Hit-stop multiplier for parries, so they read as special.")]
        public float parryHitStopMultiplier = 2f;

        [Header("Movement while acting")]
        public float attackMoveMultiplier = 0.2f;

        [Header("Dodge")]
        public WeightClass weightClass = WeightClass.Light;
        [Tooltip("One profile per weight class: Light, Medium, Heavy.")]
        public DodgeProfile[] dodges = DodgeProfile.Defaults();
        [Tooltip("Degrees per second the character turns into a roll.")]
        public float rollTurnSpeed = 1440f;

        [Header("Hit detection")]
        public LayerMask hitMask = ~0;
        [Tooltip("Swings can't reach targets standing more than this far above or below, in metres.")]
        public float maxHitHeightDifference = 1.5f;

        static readonly Collider[] OverlapBuffer = new Collider[32];
        readonly HashSet<Combatant> hitThisSwing = new HashSet<Combatant>();

        float clock, stateDuration;
        bool blockHeld;
        float blockPressedClock = float.NegativeInfinity;
        Vector3 aimDirection, blockFacing;

        public CharacterMotor Motor { get; private set; }
        public LocalTime LocalTime { get; private set; }
        public Stamina Stamina { get; private set; }
        public Agent Agent { get; private set; }
        public WeaponData Weapon => weapon;
        public CombatState State { get; private set; }
        /// <summary>Local seconds spent in the current state.</summary>
        public float StateTime { get; private set; }
        /// <summary>0..1 through the current timed state (wind-up, swing, recovery, stagger, dodge).</summary>
        public float StateProgress => stateDuration > 0f ? Mathf.Clamp01(StateTime / stateDuration) : 0f;
        public float Health { get; private set; }
        public DodgeProfile CurrentDodge => dodges[Mathf.Clamp((int)weightClass, 0, dodges.Length - 1)];
        public Vector3 DodgeDirection { get; private set; }
        /// <summary>The facing the body turns to during the current dodge (see <see cref="FacingDuringDodge"/>).</summary>
        public Vector3 DodgeFacing { get; private set; }
        public bool IsInvulnerable => State == CombatState.Dodging && CurrentDodge.IsInvulnerableAt(StateTime);
        public bool CanAct => State == CombatState.Idle || State == CombatState.Blocking;
        /// <summary>The swing isn't committed until it begins, so the whole wind-up can be feinted into a dodge.</summary>
        public bool IsFeintable => State == CombatState.Windup;
        public bool IsDead => State == CombatState.Dead;
        /// <summary>Stamina a block of this weapon's hit would cost.</summary>
        public float BlockCostAgainst(WeaponData attackerWeapon) => attackerWeapon.damage * blockStaminaPerDamage;

        /// <summary>Raised on the defender after any hit is resolved: (result, attacker).</summary>
        public event Action<HitResult, Combatant> HitReceived;
        /// <summary>Raised on the attacker after its swing is resolved: (result, target).</summary>
        public event Action<HitResult, Combatant> HitLanded;
        public event Action Died;

        void Awake()
        {
            Motor = GetComponent<CharacterMotor>();
            LocalTime = GetComponent<LocalTime>();
            Stamina = GetComponent<Stamina>();
            Agent = GetComponent<Agent>();
            Health = maxHealth;
        }

        // ---- Commands ----

        public bool TryAttack(Vector3 aim)
        {
            if (!CanAct || weapon == null || Stamina.IsEmpty) return false;
            aim.y = 0f;
            aimDirection = aim.sqrMagnitude > 0.0001f ? aim.normalized : transform.forward;
            hitThisSwing.Clear();
            Enter(CombatState.Windup, weapon.windup);
            return true;
        }

        /// <summary>Call every frame with the block intent and the direction the guard should face.</summary>
        public void SetBlocking(bool held, Vector3 facing)
        {
            if (held && !blockHeld) blockPressedClock = clock;
            blockHeld = held;
            facing.y = 0f;
            blockFacing = facing.sqrMagnitude > 0.0001f ? facing.normalized : transform.forward;
        }

        public bool TryDodge(Vector3 direction)
        {
            if (!(CanAct || IsFeintable) || Stamina.IsEmpty) return false;
            if (IsFeintable) Stamina.Spend(weapon.feintStaminaCost);
            direction.y = 0f;
            DodgeDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : -transform.forward;
            DodgeFacing = FacingDuringDodge(CurrentDodge.style, DodgeDirection, transform.forward);
            Stamina.Spend(CurrentDodge.staminaCost);
            Enter(CombatState.Dodging, CurrentDodge.TotalTime);
            return true;
        }

        // ---- Update ----

        void Update()
        {
            float dt = LocalTime.DeltaTime;
            if (dt <= 0f) return; // Frozen by hit-stop.
            clock += dt;
            StateTime += dt;

            switch (State)
            {
                case CombatState.Idle:
                    if (blockHeld) Enter(CombatState.Blocking);
                    break;
                case CombatState.Blocking:
                    Motor.FaceTowards(blockFacing, blockTurnSpeed);
                    Stamina.DelayRegen(); // No recovery with the guard up.
                    if (!blockHeld) Enter(CombatState.Idle);
                    break;
                case CombatState.Windup:
                    Motor.FaceTowards(aimDirection, weapon.attackTurnSpeed);
                    if (StateTime >= stateDuration)
                    {
                        Stamina.Spend(weapon.staminaCost); // The swing is now committed.
                        Enter(CombatState.Active, weapon.active);
                    }
                    break;
                case CombatState.Active:
                    Motor.FaceTowards(aimDirection, weapon.attackTurnSpeed);
                    SwingHitTest();
                    if (State == CombatState.Active && StateTime >= stateDuration) Enter(CombatState.Recovery, weapon.recovery);
                    break;
                case CombatState.Recovery:
                    Motor.FaceTowards(aimDirection, weapon.attackTurnSpeed);
                    if (StateTime >= stateDuration) Enter(CombatState.Idle);
                    break;
                case CombatState.Dodging:
                    UpdateDodge();
                    break;
                case CombatState.Staggered:
                    HoldFacing();
                    if (StateTime >= stateDuration) Enter(CombatState.Idle);
                    break;
                case CombatState.Dead:
                    HoldFacing();
                    break;
            }

            Motor.Restriction = CurrentRestriction();
        }

        void UpdateDodge()
        {
            DodgeProfile dodge = CurrentDodge;
            bool moving = StateTime < dodge.duration;
            Motor.ScriptedVelocity = moving ? DodgeDirection * (dodge.distance / dodge.duration) : Vector3.zero;
            if (dodge.style == DodgeStyle.Hopstep) HoldFacing(); // Hopsteps keep the guard facing the enemy.
            else Motor.FaceTowards(DodgeFacing, rollTurnSpeed);
            if (StateTime >= stateDuration) Enter(CombatState.Idle);
        }

        /// <summary>
        /// Which way the body faces during a dodge: a roll turns into its direction, a power step turns side-on
        /// to it (whichever side is the smaller turn), a hopstep keeps facing <paramref name="forward"/>.
        /// </summary>
        public static Vector3 FacingDuringDodge(DodgeStyle style, Vector3 direction, Vector3 forward)
        {
            switch (style)
            {
                case DodgeStyle.Roll:
                    return direction;
                case DodgeStyle.PowerStep:
                    Vector3 side = Vector3.Cross(Vector3.up, direction); // Facing this, the step goes to the left.
                    return Vector3.Dot(side, forward) >= 0f ? side : -side;
                default:
                    return forward;
            }
        }

        void HoldFacing() => Motor.FaceTowards(transform.forward, 0f);

        /// <summary>How the current action limits locomotion. Free movement when not acting.</summary>
        MovementRestriction CurrentRestriction() => State switch
        {
            CombatState.Idle => MovementRestriction.None,
            CombatState.Blocking => new MovementRestriction { SpeedMultiplier = blockMoveMultiplier },
            CombatState.Windup or CombatState.Active or CombatState.Recovery => new MovementRestriction { SpeedMultiplier = attackMoveMultiplier },
            _ => new MovementRestriction { SpeedMultiplier = 0f },
        };

        void Enter(CombatState state, float duration = 0f)
        {
            State = state;
            StateTime = 0f;
            stateDuration = duration;
            if (state != CombatState.Dodging) Motor.ScriptedVelocity = null;
        }

        // ---- Hits ----

        void SwingHitTest()
        {
            Vector3 center = transform.position + Vector3.up;
            int count = Physics.OverlapSphereNonAlloc(center, weapon.reach + 1f, OverlapBuffer, hitMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var target = OverlapBuffer[i].GetComponentInParent<Combatant>();
                if (target == null || target == this || target.IsDead || hitThisSwing.Contains(target)) continue;
                if (!Agent.IsHostileTo(target.Agent)) continue;
                if (!CombatRules.SwingReaches(transform.position, transform.forward, target.transform.position,
                                              target.Motor.Radius, weapon.reach, weapon.arc, maxHitHeightDifference)) continue;
                hitThisSwing.Add(target);
                target.ReceiveHit(this);
                if (State != CombatState.Active) return; // Parried mid-swing.
            }
        }

        void ReceiveHit(Combatant attacker)
        {
            WeaponData attackWeapon = attacker.weapon;
            var defender = new DefenderState
            {
                IsDead = IsDead,
                IsInvulnerable = IsInvulnerable,
                IsBlocking = State == CombatState.Blocking,
                BlockHeldTime = clock - blockPressedClock,
                Stamina = Stamina.Current,
                Forward = transform.forward,
            };
            float blockCost = BlockCostAgainst(attackWeapon);
            HitResult result = CombatRules.Resolve(defender, attacker.transform.position - transform.position, blockCost, blockArc, parryWindow);

            Vector3 away = transform.position - attacker.transform.position;
            away.y = 0f;
            Vector3 knockback = away.normalized * attackWeapon.knockback;
            float hitStop = attackWeapon.hitStop;

            switch (result)
            {
                case HitResult.Parried:
                    attacker.Stagger(parryStagger);
                    hitStop *= parryHitStopMultiplier;
                    break;
                case HitResult.Blocked:
                    Stamina.Spend(blockCost);
                    Motor.AddImpulse(knockback * blockKnockbackMultiplier);
                    break;
                case HitResult.GuardBroken:
                    Stamina.Spend(Stamina.Current);
                    Motor.AddImpulse(knockback);
                    Stagger(guardBreakStagger);
                    break;
                case HitResult.Damaged:
                    Health = Mathf.Max(0f, Health - attackWeapon.damage);
                    Motor.AddImpulse(knockback);
                    if (Health <= 0f) Die();
                    else Stagger(hitStagger);
                    break;
                default:
                    hitStop = 0f;
                    break;
            }

            if (hitStop > 0f)
            {
                LocalTime.Freeze(hitStop);
                attacker.LocalTime.Freeze(hitStop);
            }
            HitReceived?.Invoke(result, attacker);
            attacker.HitLanded?.Invoke(result, this);
        }

        public void Stagger(float duration)
        {
            if (IsDead) return;
            Enter(CombatState.Staggered, duration);
        }

        /// <summary>Death is permanent. The body stays where it fell and stops blocking other agents.</summary>
        void Die()
        {
            Enter(CombatState.Dead);
            blockHeld = false;
            Motor.MoveIntent = Vector3.zero;
            Motor.SetCollidesWithAgents(false);
            Died?.Invoke();
        }
    }
}
