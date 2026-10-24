using System;
using UnityEngine;

namespace EverRealm.Combat
{
    /// <summary>Weight class decides which dodge a character performs. No real weight system yet.</summary>
    public enum WeightClass { Light, Medium, Heavy }

    public enum DodgeStyle { Roll, Hopstep, PowerStep }

    [Serializable]
    public sealed class DodgeProfile
    {
        public DodgeStyle style;
        [Tooltip("Ground covered, in metres.")]
        public float distance;
        [Tooltip("Time spent moving, in seconds.")]
        public float duration;
        [Tooltip("Invulnerability window, in seconds from the start of the dodge.")]
        public float invulnerableFrom, invulnerableTo;
        [Tooltip("Vulnerable, mostly immobile time after the movement ends.")]
        public float recovery;
        public float staminaCost;
        [Tooltip("Visual hop height for the hopstep, in metres.")]
        public float hopHeight;

        public float TotalTime => duration + recovery;
        public bool IsInvulnerableAt(float time) => time >= invulnerableFrom && time <= invulnerableTo;

        public static DodgeProfile[] Defaults() => new[]
        {
            new DodgeProfile { style = DodgeStyle.Roll, distance = 4.5f, duration = 0.45f, invulnerableFrom = 0.05f, invulnerableTo = 0.35f, recovery = 0.15f, staminaCost = 20f },
            new DodgeProfile { style = DodgeStyle.Hopstep, distance = 3f, duration = 0.3f, invulnerableFrom = 0.03f, invulnerableTo = 0.2f, recovery = 0.1f, staminaCost = 15f, hopHeight = 0.35f },
            new DodgeProfile { style = DodgeStyle.PowerStep, distance = 1.8f, duration = 0.28f, invulnerableFrom = 0f, invulnerableTo = 0.15f, recovery = 0.25f, staminaCost = 12f },
        };
    }
}
