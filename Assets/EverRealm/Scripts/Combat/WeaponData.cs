using UnityEngine;

namespace EverRealm.Combat
{
    /// <summary>Timing, reach and impact of one weapon's single swing.</summary>
    [CreateAssetMenu(menuName = "EverRealm/Weapon", fileName = "Weapon")]
    public sealed class WeaponData : ScriptableObject
    {
        [Header("Damage")]
        public float damage = 20f;
        [Tooltip("Knockback speed applied to the target (m/s).")]
        public float knockback = 6f;

        [Header("Reach")]
        [Tooltip("Distance from the wielder's centre to the tip of the swing, in metres.")]
        public float reach = 2.2f;
        [Tooltip("Width of the swing arc in front of the wielder, in degrees.")]
        public float arc = 100f;

        [Header("Timing (seconds)")]
        public float windup = 0.35f;
        public float active = 0.12f;
        public float recovery = 0.4f;
        [Tooltip("Degrees per second the wielder can turn toward the aim during wind-up and swing.")]
        public float attackTurnSpeed = 540f;

        [Header("Cost and impact")]
        public float staminaCost = 8f;
        [Tooltip("How long attacker and target freeze when the swing connects.")]
        public float hitStop = 0.08f;
    }
}
