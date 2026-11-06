using UnityEngine;

namespace EverRealm.Characters
{
    /// <summary>
    /// Straight-line movement for AI brains: head for a point at a gait, and hop up ledges the
    /// CharacterController can't step onto. Each brain owns one. No pathfinding yet.
    /// </summary>
    public sealed class Steering
    {
        readonly CharacterMotor motor;
        float stuckTime;

        public Steering(CharacterMotor motor) => this.motor = motor;

        public void MoveTowards(Vector3 destination, Gait gait)
        {
            Vector3 direction = destination - motor.transform.position;
            direction.y = 0f;
            motor.Gait = gait;
            motor.MoveIntent = direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.zero;
        }

        public void Stop() => motor.MoveIntent = Vector3.zero;

        /// <summary>Call once per frame after setting the intent.</summary>
        public void JumpIfStuck(float dt)
        {
            bool wantsToMove = motor.MoveIntent.sqrMagnitude > 0.25f;
            Vector3 actual = motor.ActualVelocity;
            bool barelyMoving = new Vector2(actual.x, actual.z).sqrMagnitude < 0.2f;
            stuckTime = wantsToMove && barelyMoving && motor.IsGrounded ? stuckTime + dt : 0f;
            if (stuckTime > 0.25f)
            {
                motor.RequestJump();
                stuckTime = 0f;
            }
        }

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>Horizontal unit direction from <paramref name="from"/> to <paramref name="to"/>, or <paramref name="fallback"/> if they coincide.</summary>
        public static Vector3 FlatDirection(Vector3 from, Vector3 to, Vector3 fallback)
        {
            Vector3 direction = to - from;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : fallback;
        }
    }
}
