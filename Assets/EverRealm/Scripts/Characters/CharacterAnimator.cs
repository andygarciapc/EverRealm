using System;
using EverRealm.Combat;
using UnityEngine;

namespace EverRealm.Characters
{
    public enum DodgeClip { Roll, HopForward, HopBackward, HopSide, PowerStep }

    /// <summary>
    /// Plays a rigged body's animation clips from what its motor and combatant are doing. Presentation only:
    /// it reads gameplay state and never changes it. Actions (swing, dodges, death) are posed by gameplay
    /// time, not clip time: each phase of the action is mapped onto the matching stretch of its clip, so
    /// combat timings can change without re-authoring animation, and hit-stop freezes the pose for free.
    /// </summary>
    [DefaultExecutionOrder(100)] // After the combatant and motor have updated this frame.
    [RequireComponent(typeof(CharacterMotor), typeof(Combatant), typeof(LocalTime))]
    public sealed class CharacterAnimator : MonoBehaviour
    {
        /// <summary>Where an action's phases sit in its clip, as normalized clip time.</summary>
        [Serializable]
        public struct DodgeKeys
        {
            [Tooltip("The clip time at which the dodge's movement has finished.")]
            [Range(0f, 1f)] public float moveEnd;
            [Tooltip("The clip time at which the recovery has finished.")]
            [Range(0f, 1f)] public float end;

            public DodgeKeys(float moveEnd, float end)
            {
                this.moveEnd = moveEnd;
                this.end = end;
            }
        }

        [SerializeField] Animator animator;

        [Header("Swing clip key points (normalized clip time)")]
        [Range(0f, 1f)] public float attackWindupEnd = 0.29f;
        [Range(0f, 1f)] public float attackSwingEnd = 0.43f;
        [Range(0f, 1f)] public float attackSettled = 0.83f;

        [Header("Dodge clip key points")]
        public DodgeKeys roll = new DodgeKeys(0.75f, 1f);
        public DodgeKeys hopForward = new DodgeKeys(0.75f, 1f);
        public DodgeKeys hopBackward = new DodgeKeys(0.55f, 0.75f);
        public DodgeKeys hopSide = new DodgeKeys(0.55f, 1f);
        public DodgeKeys powerStep = new DodgeKeys(0.55f, 1f);

        [Header("Death")]
        [Tooltip("Where in the death clip a death starts (skips some of the stumble).")]
        [Range(0f, 1f)] public float deathStart = 0.2f;
        public float deathClipSeconds = 3.03f;

        [Header("Blending")]
        public float crossFadeSeconds = 0.08f;
        [Tooltip("Vertical speed that counts as having jumped (rather than walked off a ledge).")]
        public float jumpSpeed = 1f;
        [Tooltip("Horizontal speed above which a jump plays the running jump.")]
        public float runningJumpSpeed = 1.5f;
        public float speedDamping = 0.08f;

        static readonly int SpeedParam = Animator.StringToHash("Speed");
        static readonly int PhaseParam = Animator.StringToHash("Phase");
        static readonly int Locomotion = Animator.StringToHash("Locomotion");
        static readonly int StandingJump = Animator.StringToHash("StandingJump");
        static readonly int RunningJump = Animator.StringToHash("RunningJump");
        static readonly int Attack = Animator.StringToHash("Attack");
        static readonly int Death = Animator.StringToHash("Death");
        static readonly int[] DodgeStates =
        {
            Animator.StringToHash("Roll"), Animator.StringToHash("HopForward"), Animator.StringToHash("HopBackward"),
            Animator.StringToHash("HopSide"), Animator.StringToHash("PowerStep"),
        };
        // The one-sided clips' other sides: the same clips in states that mirror them.
        static readonly int HopSideMirror = Animator.StringToHash("HopSideMirror");
        static readonly int PowerStepMirror = Animator.StringToHash("PowerStepMirror");

        CharacterMotor motor;
        Combatant combatant;
        LocalTime localTime;
        int current;
        bool airborne;

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            combatant = GetComponent<Combatant>();
            localTime = GetComponent<LocalTime>();
            animator.applyRootMotion = false; // The motor moves the body; clips play in place.
        }

        void Update()
        {
            animator.speed = localTime.IsFrozen ? 0f : 1f;
            float dt = localTime.DeltaTime;

            switch (combatant.State)
            {
                case CombatState.Windup:
                case CombatState.Active:
                case CombatState.Recovery:
                    Play(Attack);
                    animator.SetFloat(PhaseParam, AttackPhase(combatant.State, combatant.StateProgress,
                                                              attackWindupEnd, attackSwingEnd, attackSettled));
                    break;
                case CombatState.Dodging:
                {
                    DodgeProfile dodge = combatant.CurrentDodge;
                    Vector3 local = Quaternion.Inverse(Quaternion.LookRotation(combatant.DodgeFacing, Vector3.up)) * combatant.DodgeDirection;
                    (DodgeClip clip, bool mirror) = ChooseDodge(dodge.style, local);
                    DodgeKeys keys = KeysFor(clip);
                    Play(!mirror ? DodgeStates[(int)clip] : clip == DodgeClip.PowerStep ? PowerStepMirror : HopSideMirror);
                    animator.SetFloat(PhaseParam, DodgePhase(combatant.StateTime, dodge.duration, dodge.recovery, keys.moveEnd, keys.end));
                    break;
                }
                case CombatState.Dead:
                    Play(Death);
                    animator.SetFloat(PhaseParam, Mathf.Min(1f, deathStart + combatant.StateTime / deathClipSeconds));
                    break;
                default:
                    UpdateLocomotion(dt);
                    break;
            }
        }

        void UpdateLocomotion(float dt)
        {
            Vector3 velocity = motor.ActualVelocity;
            float horizontal = new Vector2(velocity.x, velocity.z).magnitude;
            if (dt > 0f) animator.SetFloat(SpeedParam, horizontal, speedDamping, dt);

            if (!motor.IsGrounded && velocity.y > jumpSpeed && !airborne)
            {
                airborne = true;
                Play(horizontal > runningJumpSpeed ? RunningJump : StandingJump);
                return;
            }
            if (airborne && !motor.IsGrounded) return; // Let the jump play out until landing.
            airborne = false;
            Play(Locomotion);
        }

        void Play(int state)
        {
            if (state == current) return;
            current = state;
            animator.CrossFadeInFixedTime(state, crossFadeSeconds);
            if (state != Locomotion && state != StandingJump && state != RunningJump) airborne = false;
        }

        DodgeKeys KeysFor(DodgeClip clip) => clip switch
        {
            DodgeClip.Roll => roll,
            DodgeClip.HopForward => hopForward,
            DodgeClip.HopBackward => hopBackward,
            DodgeClip.HopSide => hopSide,
            _ => powerStep,
        };

        // ---- Pure mapping, unit-tested ----

        /// <summary>Clip time for a swing: wind-up, active and recovery each fill their stretch of the clip.</summary>
        public static float AttackPhase(CombatState state, float progress, float windupEnd, float swingEnd, float settled)
        {
            progress = Mathf.Clamp01(progress);
            return state switch
            {
                CombatState.Windup => Mathf.Lerp(0f, windupEnd, progress),
                CombatState.Active => Mathf.Lerp(windupEnd, swingEnd, progress),
                _ => Mathf.Lerp(swingEnd, settled, progress),
            };
        }

        /// <summary>Clip time for a dodge: the movement fills [0, moveEnd], the recovery [moveEnd, end].</summary>
        public static float DodgePhase(float time, float duration, float recovery, float moveEnd, float end)
        {
            if (time < duration) return Mathf.Lerp(0f, moveEnd, duration > 0f ? time / duration : 1f);
            return Mathf.Lerp(moveEnd, end, recovery > 0f ? (time - duration) / recovery : 1f);
        }

        /// <summary>
        /// Which clip a dodge plays, and whether mirrored, from its direction in the dodging body's own frame
        /// (+z ahead, +x right). The side hop state steps right and the power step state steps left, so the
        /// other side is their mirror.
        /// </summary>
        public static (DodgeClip clip, bool mirror) ChooseDodge(DodgeStyle style, Vector3 localDirection)
        {
            switch (style)
            {
                case DodgeStyle.Roll:
                    return (DodgeClip.Roll, false);
                case DodgeStyle.PowerStep:
                    return (DodgeClip.PowerStep, localDirection.x > 0f);
                default:
                    if (Mathf.Abs(localDirection.z) >= Mathf.Abs(localDirection.x))
                        return (localDirection.z >= 0f ? DodgeClip.HopForward : DodgeClip.HopBackward, false);
                    return (DodgeClip.HopSide, localDirection.x < 0f);
            }
        }
    }
}
