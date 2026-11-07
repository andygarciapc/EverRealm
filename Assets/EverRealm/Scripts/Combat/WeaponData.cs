using System;
using UnityEngine;

namespace EverRealm.Combat
{
    /// <summary>One attack in a weapon's chain: its timing, shape and animation.</summary>
    [Serializable]
    public sealed class WeaponAttack
    {
        public string name = "Swing";
        [Tooltip("State on the Animator's UpperBody layer. Leave empty on bodies without clips.")]
        public string animation;

        [Header("Timing (seconds)")]
        public float windup = 0.3f;
        public float active = 0.1f;
        public float recovery = 0.2f;

        [Header("Shape")]
        [Tooltip("Width of the swing arc in front of the wielder, in degrees.")]
        public float arc = 120f;
        [Tooltip("Height of the swing around its aim, in degrees. Narrow attacks have to be aimed more exactly.")]
        public float heightArc = 60f;
        [Tooltip("Added to the weapon's reach, in metres.")]
        public float reachBonus;
        public float damageMultiplier = 1f;

        [Header("Where the phases sit in the clip (normalized clip time)")]
        [Range(0f, 1f)] public float clipWindupEnd = 0.5f;
        [Range(0f, 1f)] public float clipSwingEnd = 0.667f;
        [Range(0f, 1f)] public float clipSettled = 1f;
    }

    /// <summary>What a weapon does: its attacks, in the order they chain, and how hard they land.</summary>
    [CreateAssetMenu(menuName = "EverRealm/Weapon", fileName = "Weapon")]
    public sealed class WeaponData : ScriptableObject
    {
        [Tooltip("Played in order, then back to the first. One entry is a single repeated swing.")]
        public WeaponAttack[] attacks = { new WeaponAttack() };
        [Tooltip("The chain starts over if no attack lands this many seconds after the last one struck.")]
        public float comboResetSeconds = 1.2f;
        [Tooltip("State on the Animator's UpperBody layer held while blocking. Leave empty on bodies without clips.")]
        public string guardAnimation;

        [Header("Damage")]
        public float damage = 20f;
        [Tooltip("Knockback speed applied to the target (m/s).")]
        public float knockback = 6f;

        [Header("Reach")]
        [Tooltip("Distance from the wielder's centre to the tip of the swing, in metres.")]
        public float reach = 2.2f;
        [Tooltip("Degrees per second the wielder can turn toward the aim during an attack.")]
        public float attackTurnSpeed = 540f;

        [Header("Cost and impact")]
        [Tooltip("Spent when the swing begins (end of wind-up).")]
        public float staminaCost = 8f;
        [Tooltip("Spent when the wind-up is cancelled into a dodge (a feint), on top of the dodge's own cost.")]
        public float feintStaminaCost = 4f;
        [Tooltip("How long attacker and target freeze when the swing connects.")]
        public float hitStop = 0.08f;
        [Tooltip("How long a landed hit staggers the target, in seconds. 0 means it doesn't interrupt them.")]
        public float hitStagger = 0.35f;
    }
}
