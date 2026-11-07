using EverRealm.Characters;
using EverRealm.Combat;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EverRealm.Player
{
    /// <summary>
    /// The player's brain: turns the project-wide "Player" input actions into the same intentions an AI
    /// brain produces (move + gait, jump, attack, block, dodge). Movement and aim are camera-relative.
    /// Enabled by <see cref="PlayerSession"/> on whichever body the player currently controls.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor), typeof(Combatant))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] Transform cameraTarget;

        CharacterMotor motor;
        Combatant combatant;
        Transform cameraTransform;
        InputAction moveAction, jumpAction, sprintAction, attackAction, blockAction, dodgeAction;
        // Dev-only: switches dodge style until a real weight system exists.
        readonly InputAction cycleWeightAction = new InputAction("Cycle Weight Class", binding: "<Keyboard>/v");

        public Transform CameraTarget => cameraTarget;
        public Combatant Body => combatant;

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            combatant = GetComponent<Combatant>();
            moveAction = InputSystem.actions.FindAction("Player/Move", true);
            jumpAction = InputSystem.actions.FindAction("Player/Jump", true);
            sprintAction = InputSystem.actions.FindAction("Player/Sprint", true);
            attackAction = InputSystem.actions.FindAction("Player/Attack", true);
            blockAction = InputSystem.actions.FindAction("Player/Block", true);
            dodgeAction = InputSystem.actions.FindAction("Player/Dodge", true);
        }

        [Tooltip("Camera pitch that counts as aiming level. The camera rests above the body looking down, so this is negative.")]
        public float restingCameraPitch = -15f;

        /// <summary>Which camera defines "forward" for movement and aim.</summary>
        public void Bind(Transform viewCamera) => cameraTransform = viewCamera;

        // Discrete presses are event-driven so a press is never missed, whichever update processes the input.
        void OnEnable()
        {
            jumpAction.performed += OnJump;
            attackAction.performed += OnAttack;
            dodgeAction.performed += OnDodge;
            cycleWeightAction.performed += OnCycleWeightClass;
            cycleWeightAction.Enable();
        }

        void OnDisable()
        {
            jumpAction.performed -= OnJump;
            attackAction.performed -= OnAttack;
            dodgeAction.performed -= OnDodge;
            cycleWeightAction.performed -= OnCycleWeightClass;
            cycleWeightAction.Disable();
            // Leave the body idle when the player lets go of it.
            motor.MoveIntent = Vector3.zero;
            combatant.SetBlocking(false, transform.forward);
            combatant.SetAttackHeld(false);
            combatant.SetAimPitch(0f);
        }

        void Update()
        {
            if (cameraTransform == null) return;
            motor.MoveIntent = MoveDirection();
            motor.Gait = sprintAction.IsPressed() ? Gait.Sprint : Gait.Run;
            Vector3 aim = CameraForward();
            combatant.SetBlocking(blockAction.IsPressed(), aim);
            combatant.SetAim(aim);
            combatant.SetAimPitch(CombatRules.PitchOf(cameraTransform.forward) - restingCameraPitch);
            combatant.SetAttackHeld(attackAction.IsPressed());
        }

        Vector3 CameraForward() => Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;

        Vector3 MoveDirection()
        {
            Vector2 input = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
            Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
            return CameraForward() * input.y + right * input.x;
        }

        void OnJump(InputAction.CallbackContext _) => motor.RequestJump();

        void OnAttack(InputAction.CallbackContext _)
        {
            if (cameraTransform != null) combatant.QueueAttack(CameraForward());
        }

        void OnDodge(InputAction.CallbackContext _)
        {
            if (cameraTransform == null) return;
            Vector3 direction = MoveDirection();
            combatant.QueueDodge(direction.sqrMagnitude > 0.01f ? direction : -CameraForward());
        }

        void OnCycleWeightClass(InputAction.CallbackContext _) =>
            combatant.weightClass = (WeightClass)(((int)combatant.weightClass + 1) % 3);
    }
}
