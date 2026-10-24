using System.Collections;
using EverRealm.Characters;
using EverRealm.Combat;
using EverRealm.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EverRealm.Player
{
    /// <summary>
    /// Turns the project-wide "Player" input actions into motor movement and combat commands.
    /// Movement and aim are camera-relative. Also handles respawning (ocean, death, new island).
    /// </summary>
    [RequireComponent(typeof(CharacterMotor), typeof(Combatant))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] WorldBuilder world;
        [SerializeField] Transform cameraTransform;

        [Header("Movement")]
        public float runSpeed = 6f;
        public float sprintSpeed = 10f;

        [Header("Respawn")]
        [Tooltip("How far below sea level the feet may sink before respawning on land.")]
        public float drownDepth = 1.2f;
        [Tooltip("Seconds between dying and respawning at the island spawn point.")]
        public float respawnDelay = 2f;

        CharacterMotor motor;
        Combatant combatant;
        InputAction moveAction, jumpAction, sprintAction, attackAction, blockAction, dodgeAction;
        // Dev-only: switches dodge style until a real weight system exists.
        readonly InputAction cycleWeightAction = new InputAction("Cycle Weight Class", binding: "<Keyboard>/v");
        Vector3 lastSafePosition;

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

        // Discrete presses are event-driven so a press is never missed, whichever update processes the input.
        void OnEnable()
        {
            world.Generated += SpawnOnIsland;
            combatant.Died += OnDied;
            jumpAction.performed += OnJump;
            attackAction.performed += OnAttack;
            dodgeAction.performed += OnDodge;
            cycleWeightAction.performed += OnCycleWeightClass;
            cycleWeightAction.Enable();
        }

        void OnDisable()
        {
            world.Generated -= SpawnOnIsland;
            combatant.Died -= OnDied;
            jumpAction.performed -= OnJump;
            attackAction.performed -= OnAttack;
            dodgeAction.performed -= OnDodge;
            cycleWeightAction.performed -= OnCycleWeightClass;
            cycleWeightAction.Disable();
        }

        // WorldBuilder generates in Awake, before this subscribes, so place the player once here too.
        void Start() => SpawnOnIsland();

        void Update()
        {
            bool sprinting = sprintAction.IsPressed() && combatant.State == CombatState.Idle;
            motor.DesiredVelocity = MoveDirection() * (sprinting ? sprintSpeed : runSpeed);
            combatant.SetBlocking(blockAction.IsPressed(), CameraForward());
            TrackSafePositionAndRespawn();
        }

        Vector3 CameraForward() => Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;

        Vector3 MoveDirection()
        {
            Vector2 input = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
            Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
            return CameraForward() * input.y + right * input.x;
        }

        void OnJump(InputAction.CallbackContext _) => motor.RequestJump();
        void OnAttack(InputAction.CallbackContext _) => combatant.TryAttack(CameraForward());

        void OnDodge(InputAction.CallbackContext _)
        {
            Vector3 direction = MoveDirection();
            combatant.TryDodge(direction.sqrMagnitude > 0.01f ? direction : -transform.forward);
        }

        void OnCycleWeightClass(InputAction.CallbackContext _) =>
            combatant.weightClass = (WeightClass)(((int)combatant.weightClass + 1) % 3);

        void OnDied() => StartCoroutine(RespawnAfterDelay());

        IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(respawnDelay);
            SpawnOnIsland();
        }

        void SpawnOnIsland()
        {
            motor.Teleport(world.FindSpawnPoint());
            lastSafePosition = transform.position;
            combatant.Revive();
        }

        void TrackSafePositionAndRespawn()
        {
            float seaLevel = world.Settings.seaLevel;
            if (motor.IsGrounded && transform.position.y > seaLevel + 0.1f)
                lastSafePosition = transform.position;
            else if (transform.position.y < seaLevel - drownDepth)
                motor.Teleport(lastSafePosition + Vector3.up * 0.5f);
        }
    }
}
