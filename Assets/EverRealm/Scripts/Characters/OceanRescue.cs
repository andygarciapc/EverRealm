using EverRealm.World;
using UnityEngine;

namespace EverRealm.Characters
{
    /// <summary>
    /// TEMPORARY prototype behaviour, not a world rule: returns a living agent to its last dry footing when
    /// it sinks into the sea. Replace with real swimming/water once that exists.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor))]
    public sealed class OceanRescue : MonoBehaviour
    {
        [Tooltip("How far below sea level the feet may sink before the rescue.")]
        public float drownDepth = 1.2f;

        CharacterMotor motor;
        Agent agent;
        WorldBuilder world;
        Vector3 lastSafePosition;

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            agent = GetComponent<Agent>();
        }

        void Start()
        {
            world = FindAnyObjectByType<WorldBuilder>();
            lastSafePosition = transform.position;
        }

        void Update()
        {
            if (world == null || (agent != null && !agent.IsAlive)) return;
            float seaLevel = world.Settings.seaLevel;
            Vector3 position = transform.position;
            // isGrounded can be stale right after a teleport, so also require dry land under the feet.
            if (motor.IsGrounded && position.y > seaLevel + 0.1f && world.TryGetLandSurface(position.x, position.z, out _))
                lastSafePosition = position;
            else if (transform.position.y < seaLevel - drownDepth)
                motor.Teleport(lastSafePosition + Vector3.up * 0.5f);
        }

        /// <summary>Call after placing the agent somewhere new so it isn't rescued back to the old spot.</summary>
        public void ResetSafePosition() => lastSafePosition = transform.position;
    }
}
