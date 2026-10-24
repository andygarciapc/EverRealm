using System;
using System.Collections.Generic;
using EverRealm.Characters;
using UnityEngine;

namespace EverRealm.Combat
{
    public enum CombatState { Idle, Blocking, Windup, Active, Recovery, Dodging, Staggered, Dead }

    /// <summary>
    /// Combat state machine shared by the player and enemies: a single telegraphed swing, hold-to-block
    /// with parry timing, weight-class dodges, health and stamina. Controllers issue commands
    /// (<see cref="TryAttack"/>, <see cref="SetBlocking"/>, <see cref="TryDodge"/>); timing runs on the
    /// character's <see cref="LocalTime"/> so hit-stop only freezes the characters involved.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor), typeof(LocalTime))]
    public sealed class Combatant : MonoBehaviour
    {
        [SerializeField] WeaponData weapon;
        [Tooltip("Combatants on the same team can't hit each other.")]
        public int team;

        [Header("Health and stamina")]
        public float maxHealth = 100f;
        public float maxStamina = 100f;
        [Tooltip("Stamina regained per second once regeneration starts.")]
        public float staminaRegen = 35f;
        [Tooltip("Seconds after spending stamina before it regenerates.")]
        public float staminaRegenDelay = 0.8f;

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

        float clock, stateDuration, lastStaminaUse = float.NegativeInfinity;
        bool blockHeld;
        float blockPressedClock = float.NegativeInfinity;
        Vector3 aimDirection, blockFacing;

        public CharacterMotor Motor { get; private set; }
        public LocalTime LocalTime { get; private set; }
        public WeaponData Weapon => weapon;
        public CombatState State { get; private set; }
        /// <summary>Local seconds spent in the current state.</summary>
        public float StateTime { get; private set; }
        /// <summary>0..1 through the current timed state (wind-up, swing, recovery, stagger, dodge).</summary>
        public float StateProgress => stateDuration > 0f ? Mathf.Clamp01(StateTime / stateDuration) : 0f;
        public float Health { get; private set; }
        public float Stamina { get; private set; }
        public DodgeProfile CurrentDodge => dodges[Mathf.Clamp((int)weightClass, 0, dodges.Length - 1)];
        public Vector3 DodgeDirection { get; private set; }
        public bool IsInvulnerable => State == CombatState.Dodging && CurrentDodge.IsInvulnerableAt(StateTime);
        public bool CanAct => State == CombatState.Idle || State == CombatState.Blocking;
        public bool IsDead => State == CombatState.Dead;

        /// <summary>Raised on the defender after any hit is resolved: (result, attacker).</summary>
        public event Action<HitResult, Combatant> HitReceived;
        /// <summary>Raised on the attacker after its swing is resolved: (result, target).</summary>
        public event Action<HitResult, Combatant> HitLanded;
        public event Action Died;

        void Awake()
        {
            Motor = GetComponent<CharacterMotor>();
            LocalTime = GetComponent<LocalTime>();
            Health = maxHealth;
            Stamina = maxStamina;
        }

        // ---- Commands ----

        public bool TryAttack(Vector3 aim)
        {
            if (!CanAct || weapon == null || Stamina <= 0f) return false;
            aim.y = 0f;
            aimDirection = aim.sqrMagnitude > 0.0001f ? aim.normalized : transform.forward;
            SpendStamina(weapon.staminaCost);
            hitThisSwing.Clear();
            Enter(CombatState.Windup, weapon.windup);
            return true;
        }

        /// <summary>Call every frame with the block button state and the direction the guard should face.</summary>
        public void SetBlocking(bool held, Vector3 facing)
        {
            if (held && !blockHeld) blockPressedClock = clock;
            blockHeld = held;
            facing.y = 0f;
            blockFacing = facing.sqrMagnitude > 0.0001f ? facing.normalized : transform.forward;
        }

        public bool TryDodge(Vector3 direction)
        {
            if (!CanAct || Stamina <= 0f) return false;
            direction.y = 0f;
            DodgeDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : -transform.forward;
            SpendStamina(CurrentDodge.staminaCost);
            Enter(CombatState.Dodging, CurrentDodge.TotalTime);
            return true;
        }

        public void Revive()
        {
            Health = maxHealth;
            Stamina = maxStamina;
            Enter(CombatState.Idle);
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
                    if (!blockHeld) Enter(CombatState.Idle);
                    break;
                case CombatState.Windup:
                    Motor.FaceTowards(aimDirection, weapon.attackTurnSpeed);
                    if (StateTime >= stateDuration) Enter(CombatState.Active, weapon.active);
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

            ApplyMovementRules();
            if (State != CombatState.Blocking && clock - lastStaminaUse >= staminaRegenDelay)
                Stamina = Mathf.Min(maxStamina, Stamina + staminaRegen * dt);
        }

        void UpdateDodge()
        {
            DodgeProfile dodge = CurrentDodge;
            bool moving = StateTime < dodge.duration;
            Motor.ScriptedVelocity = moving ? DodgeDirection * (dodge.distance / dodge.duration) : Vector3.zero;
            if (dodge.style == DodgeStyle.Roll) Motor.FaceTowards(DodgeDirection, rollTurnSpeed);
            else HoldFacing(); // Steps keep the guard facing the enemy.
            if (StateTime >= stateDuration) Enter(CombatState.Idle);
        }

        void HoldFacing() => Motor.FaceTowards(transform.forward, 0f);

        void ApplyMovementRules()
        {
            if (State != CombatState.Dodging) Motor.ScriptedVelocity = null;
            Motor.JumpAllowed = CanAct;
            Motor.SpeedMultiplier = State switch
            {
                CombatState.Idle => 1f,
                CombatState.Blocking => blockMoveMultiplier,
                CombatState.Windup or CombatState.Active or CombatState.Recovery => attackMoveMultiplier,
                _ => 0f,
            };
        }

        void Enter(CombatState state, float duration = 0f)
        {
            State = state;
            StateTime = 0f;
            stateDuration = duration;
            if (state != CombatState.Dodging) Motor.ScriptedVelocity = null;
        }

        void SpendStamina(float amount)
        {
            Stamina = Mathf.Max(0f, Stamina - amount);
            lastStaminaUse = clock;
        }

        // ---- Hits ----

        void SwingHitTest()
        {
            Vector3 center = transform.position + Vector3.up;
            int count = Physics.OverlapSphereNonAlloc(center, weapon.reach + 1f, OverlapBuffer, hitMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var target = OverlapBuffer[i].GetComponentInParent<Combatant>();
                if (target == null || target == this || target.team == team || target.IsDead || hitThisSwing.Contains(target)) continue;
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
                Stamina = Stamina,
                Forward = transform.forward,
            };
            float blockCost = attackWeapon.damage * blockStaminaPerDamage;
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
                    SpendStamina(blockCost);
                    Motor.AddImpulse(knockback * blockKnockbackMultiplier);
                    break;
                case HitResult.GuardBroken:
                    SpendStamina(Stamina);
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

        void Die()
        {
            Enter(CombatState.Dead);
            Died?.Invoke();
        }
    }
}
