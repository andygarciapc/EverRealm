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
        public float guardBreakStagger = 0.9f;
        [Tooltip("Applied to the attacker when its swing is parried.")]
        public float parryStagger = 1f;
        [Tooltip("Hit-stop multiplier for parries, so they read as special.")]
        public float parryHitStopMultiplier = 2f;

        [Header("Movement while acting")]
        public float attackMoveMultiplier = 0.2f;

        [Tooltip("Furthest a swing or guard can be aimed above or below level, in degrees.")]
        public float maxAimPitch = 50f;

        [Header("Input")]
        [Tooltip("How long a queued attack or dodge waits for the body to be free, in seconds.")]
        public float inputBuffer = 0.2f;

        [Header("Dodge")]
        public WeightClass weightClass = WeightClass.Light;
        [Tooltip("One profile per weight class: Light, Medium, Heavy.")]
        public DodgeProfile[] dodges = DodgeProfile.Defaults();
        [Tooltip("Degrees per second the character turns into a roll.")]
        public float rollTurnSpeed = 1440f;

        [Header("Hit detection")]
        public LayerMask hitMask = ~0;
        [Tooltip("Where a swing starts, as a fraction of body height.")]
        public float swingOriginHeight = 0.65f;

        static readonly Collider[] OverlapBuffer = new Collider[32];
        readonly HashSet<Combatant> hitThisSwing = new HashSet<Combatant>();

        float clock, stateDuration;
        bool blockHeld, attackHeld;
        float blockPressedClock = float.NegativeInfinity;
        Vector3 aimDirection, blockFacing;
        BufferedIntent queuedAttack, queuedDodge;
        AttackChain chain;

        public CharacterMotor Motor { get; private set; }
        public LocalTime LocalTime { get; private set; }
        public Stamina Stamina { get; private set; }
        public Agent Agent { get; private set; }
        public WeaponData Weapon => weapon;
        /// <summary>The attack being made, or the last one made.</summary>
        public WeaponAttack CurrentAttack { get; private set; }
        /// <summary>Reach of the current attack.</summary>
        float SwingReach => weapon.reach + CurrentAttack.reachBonus;
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

        /// <summary>Starts a swing if the body is free. Remembers nothing if it isn't.</summary>
        public bool TryAttack(Vector3 aim)
        {
            if (!CanAct || weapon == null || weapon.attacks.Length == 0 || Stamina.Current < weapon.staminaCost) return false;
            queuedAttack.Clear();
            aim.y = 0f;
            aimDirection = aim.sqrMagnitude > 0.0001f ? aim.normalized : transform.forward;
            hitThisSwing.Clear();
            CurrentAttack = weapon.attacks[chain.Next(weapon.attacks.Length, clock, weapon.comboResetSeconds)];
            Enter(CombatState.Windup, CurrentAttack.windup);
            return true;
        }

        /// <summary>For a single press: starts a swing, or else remembers it briefly, replacing any it already remembered.</summary>
        public void QueueAttack(Vector3 aim)
        {
            if (!TryAttack(aim) && !IsDead) queuedAttack.Store(aim, clock + inputBuffer);
        }

        /// <summary>
        /// Call every frame with whether attack is held: the body swings again the moment it's free, along the
        /// aim from <see cref="SetAim"/>. Nothing is remembered, so letting go stops after the current swing.
        /// </summary>
        public void SetAttackHeld(bool held) => attackHeld = held;

        /// <summary>Steers the current swing. Brains that don't call this commit to their aim when the swing starts.</summary>
        public void SetAim(Vector3 aim)
        {
            aim.y = 0f;
            if (aim.sqrMagnitude > 0.0001f) aimDirection = aim.normalized;
        }

        /// <summary>How high swings and the guard are aimed, in degrees above level. Swings land along this, and the pose bends to match.</summary>
        public float AimPitch { get; private set; }

        public void SetAimPitch(float degrees) => AimPitch = Mathf.Clamp(degrees, -maxAimPitch, maxAimPitch);

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
            if (!CombatRules.CanDodgeFrom(State) || Stamina.IsEmpty) return false;
            queuedAttack.Clear();
            queuedDodge.Clear();
            if (IsFeintable) Stamina.Spend(weapon.feintStaminaCost);
            direction.y = 0f;
            DodgeDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : -transform.forward;
            DodgeFacing = FacingDuringDodge(CurrentDodge.style, DodgeDirection, transform.forward);
            Stamina.Spend(CurrentDodge.staminaCost);
            Enter(CombatState.Dodging, CurrentDodge.TotalTime);
            return true;
        }

        /// <summary>For a single press: dodges, or else remembers it briefly, replacing any it already remembered.</summary>
        public void QueueDodge(Vector3 direction)
        {
            if (!TryDodge(direction) && !IsDead) queuedDodge.Store(direction, clock + inputBuffer);
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
                        chain.Commit(weapon.attacks.Length, clock);
                        Advance(CombatState.Active, CurrentAttack.active);
                    }
                    break;
                case CombatState.Active:
                    Motor.FaceTowards(aimDirection, weapon.attackTurnSpeed);
                    SwingHitTest();
                    if (State == CombatState.Active && StateTime >= stateDuration) Advance(CombatState.Recovery, CurrentAttack.recovery);
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

            if (CombatRules.CanDodgeFrom(State) && queuedDodge.TryTake(clock, out Vector3 dodge)) TryDodge(dodge);
            // Before anything reads the state this frame, so back-to-back swings have no idle gap between them.
            if (CanAct && queuedAttack.TryTake(clock, out Vector3 aim)) TryAttack(aim);
            if (CanAct && attackHeld) TryAttack(aimDirection);

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
            CombatState.Windup or CombatState.Active or CombatState.Recovery => new MovementRestriction { SpeedMultiplier = attackMoveMultiplier, AllowJump = true },
            _ => new MovementRestriction { SpeedMultiplier = 0f },
        };

        /// <summary>Moves to the next phase of a swing, keeping the time overrun, so its length doesn't depend on the frame rate.</summary>
        void Advance(CombatState state, float duration)
        {
            float overrun = StateTime - stateDuration;
            Enter(state, duration);
            StateTime = overrun;
        }

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
            int count = Physics.OverlapSphereNonAlloc(center, SwingReach + 1f, OverlapBuffer, hitMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var target = OverlapBuffer[i].GetComponentInParent<Combatant>();
                if (target == null || target == this || target.IsDead || hitThisSwing.Contains(target)) continue;
                if (!Agent.IsHostileTo(target.Agent)) continue;
                if (!Reaches(target, transform.forward, CurrentAttack)) continue;
                hitThisSwing.Add(target);
                target.ReceiveHit(this);
                if (State != CombatState.Active) return; // Parried mid-swing.
            }
        }

        /// <summary>Whether a swing along <paramref name="facing"/> at the current aim would land on <paramref name="target"/>.</summary>
        public bool CouldHit(Combatant target, Vector3 facing) => Reaches(target, facing, weapon.attacks[0]);

        bool Reaches(Combatant target, Vector3 facing, WeaponAttack attack) =>
            CombatRules.SwingReaches(transform.position + Vector3.up * (Motor.Height * swingOriginHeight), facing, AimPitch,
                                     target.transform.position, target.Motor.Height, target.Motor.Radius,
                                     weapon.reach + attack.reachBonus, attack.arc, attack.heightArc);

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
                    Health = Mathf.Max(0f, Health - attackWeapon.damage * attacker.CurrentAttack.damageMultiplier);
                    Motor.AddImpulse(knockback);
                    if (Health <= 0f) Die();
                    else if (attackWeapon.hitStagger > 0f) Stagger(attackWeapon.hitStagger);
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
            queuedAttack.Clear();
            queuedDodge.Clear();
            attackHeld = false;
            blockHeld = false;
            Motor.MoveIntent = Vector3.zero;
            Motor.SetCollidesWithAgents(false);
            Died?.Invoke();
        }
    }
}
