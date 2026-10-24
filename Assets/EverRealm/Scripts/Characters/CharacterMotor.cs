using UnityEngine;

namespace EverRealm.Characters
{
    /// <summary>
    /// CharacterController movement shared by the player and AI: smoothed horizontal velocity,
    /// gravity, buffered jumps, limited-rate turning, knockback impulses and scripted motion
    /// (dodges). Controllers write the desired state each frame; this runs after them.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(CharacterController), typeof(LocalTime))]
    public sealed class CharacterMotor : MonoBehaviour
    {
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
        Vector3 horizontalVelocity, impulseVelocity;
        float verticalVelocity;
        float clock, lastGroundedTime = float.NegativeInfinity, lastJumpRequestTime = float.NegativeInfinity;
        Vector3? facingRequest;
        float facingRequestSpeed;

        /// <summary>World-space horizontal velocity the controller wants this frame.</summary>
        public Vector3 DesiredVelocity { get; set; }
        /// <summary>Scales <see cref="DesiredVelocity"/>; combat states use it to slow movement.</summary>
        public float SpeedMultiplier { get; set; } = 1f;
        /// <summary>When set, replaces horizontal movement entirely (no acceleration), e.g. for dodges.</summary>
        public Vector3? ScriptedVelocity { get; set; }
        public bool JumpAllowed { get; set; } = true;

        public bool IsGrounded => controller.isGrounded;
        public Vector3 HorizontalVelocity => horizontalVelocity;
        /// <summary>Velocity the controller actually achieved last move, after collisions.</summary>
        public Vector3 ActualVelocity => controller.velocity;
        public float Radius => controller.radius;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            localTime = GetComponent<LocalTime>();
        }

        public void RequestJump() => lastJumpRequestTime = clock;

        /// <summary>Turn toward <paramref name="direction"/> this frame at <paramref name="degreesPerSecond"/>, overriding facing the movement.</summary>
        public void FaceTowards(Vector3 direction, float degreesPerSecond)
        {
            facingRequest = direction;
            facingRequestSpeed = degreesPerSecond;
        }

        public void AddImpulse(Vector3 velocity) => impulseVelocity += velocity;

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
            float dt = localTime.DeltaTime;
            if (dt <= 0f) return; // Frozen by hit-stop.
            clock += dt;
            if (controller.isGrounded) lastGroundedTime = clock;

            UpdateHorizontal(dt);
            UpdateFacing(dt);
            UpdateVertical(dt);
            impulseVelocity = Vector3.MoveTowards(impulseVelocity, Vector3.zero, impulseDrag * dt);

            controller.Move((horizontalVelocity + impulseVelocity + Vector3.up * verticalVelocity) * dt);
        }

        void UpdateHorizontal(float dt)
        {
            if (ScriptedVelocity.HasValue)
            {
                horizontalVelocity = ScriptedVelocity.Value;
                return;
            }
            float acceleration = controller.isGrounded ? groundAcceleration : airAcceleration;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, DesiredVelocity * SpeedMultiplier, acceleration * dt);
        }

        void UpdateFacing(float dt)
        {
            Vector3 direction;
            float speed;
            if (facingRequest.HasValue) { direction = facingRequest.Value; speed = facingRequestSpeed; }
            else { direction = DesiredVelocity; speed = turnSpeed; }
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

            bool canJump = JumpAllowed && clock - lastGroundedTime <= coyoteTime;
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
