using UnityEngine;

namespace EverRealm.Combat
{
    public enum HitResult
    {
        Ignored,     // Target already dead.
        Dodged,      // Target was invulnerable mid-dodge.
        Parried,     // Block raised just in time: no cost, attacker staggered.
        Blocked,     // Absorbed by the guard at a stamina cost.
        GuardBroken, // Guard ran out of stamina: absorbed, but the defender is staggered.
        Damaged,
    }

    /// <summary>Snapshot of the defender at the moment a swing connects.</summary>
    public struct DefenderState
    {
        public bool IsDead;
        public bool IsInvulnerable;
        public bool IsBlocking;
        /// <summary>Seconds since the block button was pressed.</summary>
        public float BlockHeldTime;
        public float Stamina;
        public Vector3 Forward;
    }

    /// <summary>Pure combat maths, kept free of MonoBehaviour state so it can be unit-tested.</summary>
    public static class CombatRules
    {
        /// <summary>True if <paramref name="toOther"/> lies within <paramref name="arcDegrees"/> centred on <paramref name="forward"/>, ignoring height.</summary>
        public static bool InArc(Vector3 forward, Vector3 toOther, float arcDegrees)
        {
            forward.y = 0f;
            toOther.y = 0f;
            if (toOther.sqrMagnitude < 0.0001f) return true; // Overlapping: count as in front.
            return Vector3.Angle(forward, toOther) <= arcDegrees * 0.5f;
        }

        /// <summary>Whether a swing from the attacker, facing its actual forward, reaches the target.</summary>
        public static bool SwingReaches(Vector3 attackerPosition, Vector3 attackerForward, Vector3 targetPosition,
                                        float targetRadius, float reach, float arcDegrees, float maxHeightDifference)
        {
            Vector3 toTarget = targetPosition - attackerPosition;
            if (Mathf.Abs(toTarget.y) > maxHeightDifference) return false;
            toTarget.y = 0f;
            if (toTarget.magnitude - targetRadius > reach) return false;
            return InArc(attackerForward, toTarget, arcDegrees);
        }

        public static HitResult Resolve(DefenderState defender, Vector3 toAttacker, float blockCost,
                                        float blockArc, float parryWindow)
        {
            if (defender.IsDead) return HitResult.Ignored;
            if (defender.IsInvulnerable) return HitResult.Dodged;
            if (defender.IsBlocking && InArc(defender.Forward, toAttacker, blockArc))
            {
                if (defender.BlockHeldTime <= parryWindow) return HitResult.Parried;
                return defender.Stamina >= blockCost ? HitResult.Blocked : HitResult.GuardBroken;
            }
            return HitResult.Damaged;
        }
    }
}
