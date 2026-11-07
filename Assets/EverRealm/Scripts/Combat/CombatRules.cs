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

    /// <summary>
    /// One remembered command, waiting for the body to be free. A single slot: storing again replaces it, so
    /// commands can never stack up.
    /// </summary>
    public struct BufferedIntent
    {
        Vector3 direction;
        float expiresAt;
        bool pending;

        public void Store(Vector3 newDirection, float expiry)
        {
            direction = newDirection;
            expiresAt = expiry;
            pending = true;
        }

        public void Clear() => pending = false;

        /// <summary>Hands over the command if it's still fresh. Either way the slot ends up empty.</summary>
        public bool TryTake(float now, out Vector3 stored)
        {
            stored = direction;
            bool fresh = pending && now <= expiresAt;
            pending = false;
            return fresh;
        }
    }

    /// <summary>
    /// Which attack comes next in a weapon's chain. It only moves on when a swing is committed, so a wind-up
    /// cancelled into a dodge repeats the same attack rather than skipping it.
    /// </summary>
    public struct AttackChain
    {
        int next;
        float lastStrike;
        bool started;

        /// <summary>The attack to play now, going back to the first if the chain has lapsed.</summary>
        public int Next(int attacks, float now, float resetSeconds)
        {
            if (!started || now - lastStrike > resetSeconds) next = 0;
            return attacks > 0 ? next % attacks : 0;
        }

        /// <summary>A swing has struck: the next attack follows it.</summary>
        public void Commit(int attacks, float now)
        {
            next = attacks > 0 ? (next + 1) % attacks : 0;
            lastStrike = now;
            started = true;
        }
    }

    /// <summary>Pure combat maths, kept free of MonoBehaviour state so it can be unit-tested.</summary>
    public static class CombatRules
    {
        /// <summary>A dodge can cut a swing's wind-up or recovery short, but once the swing itself is under way it's committed.</summary>
        public static bool CanDodgeFrom(CombatState state) =>
            state is CombatState.Idle or CombatState.Blocking or CombatState.Windup or CombatState.Recovery;

        /// <summary>True if <paramref name="toOther"/> lies within <paramref name="arcDegrees"/> centred on <paramref name="forward"/>, ignoring height.</summary>
        public static bool InArc(Vector3 forward, Vector3 toOther, float arcDegrees)
        {
            forward.y = 0f;
            toOther.y = 0f;
            if (toOther.sqrMagnitude < 0.0001f) return true; // Overlapping: count as in front.
            return Vector3.Angle(forward, toOther) <= arcDegrees * 0.5f;
        }

        /// <summary>Whether a swing from the attacker, facing its actual forward, reaches the target.</summary>
        /// <summary>How far a direction points above the horizon, in degrees; below it is negative.</summary>
        public static float PitchOf(Vector3 direction) =>
            Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;

        /// <summary>
        /// Whether a swing from <paramref name="origin"/> lands: the target's body has to be within the arc and within
        /// reach, and the aim has to pass through that body, to within half of <paramref name="heightArc"/>. A target
        /// closer than half the reach counts as being there, because a blade sweeps the space right around the body.
        /// </summary>
        public static bool SwingReaches(Vector3 origin, Vector3 forward, float aimPitch, Vector3 targetFeet, float targetHeight,
                                        float targetRadius, float reach, float arc, float heightArc)
        {
            Vector3 toTarget = targetFeet - origin;
            if (!InArc(forward, toTarget, arc)) return false;

            float flat = new Vector2(toTarget.x, toTarget.z).magnitude;
            float along = Mathf.Max(flat, reach * 0.5f);
            float aimedHeight = origin.y + Mathf.Tan(aimPitch * Mathf.Deg2Rad) * along;
            float struck = Mathf.Clamp(aimedHeight, targetFeet.y, targetFeet.y + targetHeight) - origin.y;

            if (Mathf.Sqrt(flat * flat + struck * struck) - targetRadius > reach) return false;
            return Mathf.Abs(Mathf.Atan2(struck, along) * Mathf.Rad2Deg - aimPitch) <= heightArc * 0.5f;
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
