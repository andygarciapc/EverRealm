using UnityEngine;

namespace EverRealm.Characters
{
    /// <summary>
    /// Per-character clock. Gameplay effects like hit-stop freeze one character's time instead of
    /// touching the global Time.timeScale, so they stay local in multiplayer.
    /// </summary>
    public sealed class LocalTime : MonoBehaviour
    {
        float frozenUntil;

        public bool IsFrozen => Time.time < frozenUntil;
        public float DeltaTime => IsFrozen ? 0f : Time.deltaTime;

        /// <summary>Freeze this character for <paramref name="duration"/> seconds of game time.</summary>
        public void Freeze(float duration) => frozenUntil = Mathf.Max(frozenUntil, Time.time + duration);
    }
}
