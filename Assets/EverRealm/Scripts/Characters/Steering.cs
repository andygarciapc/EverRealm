using UnityEngine;

namespace EverRealm.Characters
{
    /// <summary>
    /// Straight-line movement for AI brains: head for a point at a gait, hop up ledges the CharacterController
    /// can't step onto, and work along anything a hop won't clear. Each brain owns one. No pathfinding yet.
    /// </summary>
    public sealed class Steering
    {
        const float StuckBeforeJump = 0.25f;
        const int JumpsBeforeSidestep = 3;
        const float SidestepSeconds = 1.5f;
        const float ClearSeconds = 2f;

        readonly CharacterMotor motor;
        float stuckTime, sidestepTime, clearTime, sidestepSide = 1f;
        int failedJumps, sidesteps;

        public Steering(CharacterMotor motor) => this.motor = motor;

        public void MoveTowards(Vector3 destination, Gait gait)
        {
            Vector3 direction = destination - motor.transform.position;
            direction.y = 0f;
            motor.Gait = gait;
            if (direction.sqrMagnitude <= 0.01f)
            {
                motor.MoveIntent = Vector3.zero;
                return;
            }
            direction.Normalize();
            motor.MoveIntent = sidestepTime > 0f ? Quaternion.Euler(0f, 90f * sidestepSide, 0f) * direction : direction;
        }

        public void Stop() => motor.MoveIntent = Vector3.zero;

        /// <summary>Call once per frame after setting the intent.</summary>
        public void JumpIfStuck(float dt)
        {
            sidestepTime = Mathf.Max(0f, sidestepTime - dt);
            bool wantsToMove = motor.MoveIntent.sqrMagnitude > 0.25f;
            Vector3 actual = motor.ActualVelocity;
            bool barelyMoving = new Vector2(actual.x, actual.z).sqrMagnitude < 0.2f;

            // Only a good stretch of free movement means the obstacle is behind us.
            clearTime = barelyMoving ? 0f : clearTime + dt;
            if (clearTime >= ClearSeconds && sidestepTime <= 0f) failedJumps = sidesteps = 0;

            stuckTime = wantsToMove && barelyMoving && motor.IsGrounded ? stuckTime + dt : 0f;
            if (stuckTime <= StuckBeforeJump) return;
            stuckTime = 0f;
            motor.RequestJump();
            if (++failedJumps < JumpsBeforeSidestep) return;

            // Each try goes further, to the other side, so a body in a corner sweeps along the whole obstacle.
            failedJumps = 0;
            sidesteps++;
            sidestepSide = -sidestepSide;
            sidestepTime = SidestepSeconds * sidesteps;
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
