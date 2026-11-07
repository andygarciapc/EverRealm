using UnityEngine;

namespace EverRealm.Characters
{
    public enum Gait { Walk, Run, Sprint }

    /// <summary>Limits an action places on locomotion (e.g. slow while blocking). Locomotion stays in charge otherwise.</summary>
    public struct MovementRestriction
    {
        public float SpeedMultiplier;
        public bool AllowSprint;
        public bool AllowJump;

        public static MovementRestriction None => new MovementRestriction { SpeedMultiplier = 1f, AllowSprint = true, AllowJump = true };
    }

    /// <summary>
    /// CharacterController locomotion shared by every agent: gaits (walk/run/sprint, with sprint costing
    /// stamina), gravity, buffered jumps, limited-rate turning, knockback impulses and scripted motion
    /// (dodges). Brains write the intent each frame; actions may restrict it. Runs after both.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(CharacterController), typeof(LocalTime))]
    public sealed class CharacterMotor : MonoBehaviour
    {
        [Header("Gaits (m/s)")]
        public float walkSpeed = 2.5f;
        public float runSpeed = 6f;
        public float sprintSpeed = 10f;
        [Tooltip("Stamina drained per second while sprinting.")]
        public float sprintStaminaPerSecond = 15f;

        [Header("Movement")]
        [Tooltip("How quickly speed changes on the ground (m/s²).")]
        public float groundAcceleration = 60f;
        [Tooltip("How quickly speed changes in the air (m/s²).")]
        public float airAcceleration = 20f;
        [Tooltip("Degrees per second the character turns to face its movement.")]
        public float turnSpeed = 720f;
        [Tooltip("How quickly knockback fades (m/s²).")]
        public float impulseDrag = 30f;

        [Header("Jumping")]
        public float jumpHeight = 1.3f;
        public float gravity = -25f;
        [Tooltip("Grace period after walking off a ledge during which a jump still works.")]
        public float coyoteTime = 0.12f;
        [Tooltip("A jump pressed this long before landing still fires on landing.")]
        public float jumpBuffer = 0.12f;

        CharacterController controller;
        LocalTime localTime;
        Stamina stamina;
        Vector3 horizontalVelocity, impulseVelocity;
        float verticalVelocity;
        float clock, lastGroundedTime = float.NegativeInfinity, lastJumpRequestTime = float.NegativeInfinity;
        Vector3? facingRequest;
        float facingRequestSpeed;

        /// <summary>World-space direction the brain wants to move in; magnitude 0..1 scales the gait speed.</summary>
        public Vector3 MoveIntent { get; set; }
        public Gait Gait { get; set; } = Gait.Run;
        /// <summary>Set by the current action each frame; <see cref="MovementRestriction.None"/> when free.</summary>
        public MovementRestriction Restriction { get; set; } = MovementRestriction.None;
        /// <summary>When set, replaces horizontal movement entirely (no acceleration), e.g. for dodges.</summary>
        public Vector3? ScriptedVelocity { get; set; }

        /// <summary>The gait actually used last frame, after restrictions and stamina.</summary>
        public Gait ResolvedGait { get; private set; }
        public bool IsGrounded => controller.isGrounded;
        public Vector3 HorizontalVelocity => horizontalVelocity;
        /// <summary>Velocity the controller actually achieved last move, after collisions.</summary>
        public Vector3 ActualVelocity => controller.velocity;
        public float Radius => controller.radius;
        public float Height => controller.height;
        /// <summary>How far the controller popped up onto a step this frame, beyond any jump or fall.</summary>
        public float StepRise { get; private set; }

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            localTime = GetComponent<LocalTime>();
            stamina = GetComponent<Stamina>();
        }

        public void RequestJump() => lastJumpRequestTime = clock;

        /// <summary>Turn toward <paramref name="direction"/> this frame at <paramref name="degreesPerSecond"/>, overriding facing the movement.</summary>
        public void FaceTowards(Vector3 direction, float degreesPerSecond)
        {
            facingRequest = direction;
            facingRequestSpeed = degreesPerSecond;
        }

        public void AddImpulse(Vector3 velocity) => impulseVelocity += velocity;

        /// <summary>Whether other agents bump into this body (off for corpses).</summary>
        public void SetCollidesWithAgents(bool collides) => controller.detectCollisions = collides;

        public void Teleport(Vector3 position)
        {
            // CharacterController overrides transform changes while enabled.
            controller.enabled = false;
            transform.position = position;
            controller.enabled = true;
            horizontalVelocity = impulseVelocity = Vector3.zero;
            verticalVelocity = 0f;
        }

        void Update()
        {
            StepRise = 0f;
            float dt = localTime.DeltaTime;
            if (dt <= 0f) return; // Frozen by hit-stop.
            clock += dt;
            if (controller.isGrounded) lastGroundedTime = clock;

            Vector3 desired = DesiredVelocity(dt);
            UpdateHorizontal(desired, dt);
            UpdateFacing(desired, dt);
            UpdateVertical(dt);
            impulseVelocity = Vector3.MoveTowards(impulseVelocity, Vector3.zero, impulseDrag * dt);

            float heightBefore = transform.position.y;
            controller.Move((horizontalVelocity + impulseVelocity + Vector3.up * verticalVelocity) * dt);
            StepRise = Mathf.Max(0f, transform.position.y - heightBefore - Mathf.Max(0f, verticalVelocity * dt));
        }

        Vector3 DesiredVelocity(float dt)
        {
            Vector3 intent = Vector3.ClampMagnitude(MoveIntent, 1f);
            intent.y = 0f;
            bool moving = intent.sqrMagnitude > 0.01f;

            Gait gait = Gait;
            if (gait == Gait.Sprint && (!Restriction.AllowSprint || (stamina != null && stamina.IsEmpty) || !moving))
                gait = Gait.Run;
            if (gait == Gait.Sprint && stamina != null) stamina.Spend(sprintStaminaPerSecond * dt);
            ResolvedGait = gait;

            float speed = gait switch { Gait.Walk => walkSpeed, Gait.Sprint => sprintSpeed, _ => runSpeed };
            return intent * (speed * Restriction.SpeedMultiplier);
        }

        void UpdateHorizontal(Vector3 desired, float dt)
        {
            if (ScriptedVelocity.HasValue)
            {
                horizontalVelocity = ScriptedVelocity.Value;
                return;
            }
            float acceleration = controller.isGrounded ? groundAcceleration : airAcceleration;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, desired, acceleration * dt);
        }

        void UpdateFacing(Vector3 desired, float dt)
        {
            Vector3 direction;
            float speed;
            if (facingRequest.HasValue) { direction = facingRequest.Value; speed = facingRequestSpeed; }
            else { direction = desired; speed = turnSpeed; }
            facingRequest = null;

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f || speed <= 0f) return;
            Quaternion target = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, speed * dt);
        }

        void UpdateVertical(float dt)
        {
            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f; // Keep a small downward push so isGrounded stays reliable on steps.

            bool canJump = Restriction.AllowJump && clock - lastGroundedTime <= coyoteTime;
            bool wantsJump = clock - lastJumpRequestTime <= jumpBuffer;
            if (canJump && wantsJump)
            {
                verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
                lastGroundedTime = lastJumpRequestTime = float.NegativeInfinity;
            }

            verticalVelocity += gravity * dt;
        }
    }
}
