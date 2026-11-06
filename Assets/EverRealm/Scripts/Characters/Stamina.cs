using UnityEngine;

namespace EverRealm.Characters
{
    /// <summary>
    /// A body's stamina pool, shared by locomotion (sprinting) and actions (attacks, blocks, dodges).
    /// Regenerates after a short delay on the character's local time.
    /// </summary>
    [RequireComponent(typeof(LocalTime))]
    public sealed class Stamina : MonoBehaviour
    {
        public float max = 100f;
        [Tooltip("Regained per second once regeneration starts.")]
        public float regenPerSecond = 35f;
        [Tooltip("Seconds after spending before regeneration starts.")]
        public float regenDelay = 0.8f;

        LocalTime localTime;
        float clock, lastSpendTime = float.NegativeInfinity;

        public float Current { get; private set; }
        public bool IsEmpty => Current <= 0f;

        void Awake()
        {
            localTime = GetComponent<LocalTime>();
            Current = max;
        }

        public void Spend(float amount)
        {
            Current = Mathf.Max(0f, Current - amount);
            DelayRegen();
        }

        /// <summary>Hold off regeneration this frame, e.g. while a guard is raised.</summary>
        public void DelayRegen() => lastSpendTime = clock;

        public void Refill() => Current = max;

        void Update()
        {
            float dt = localTime.DeltaTime;
            if (dt <= 0f) return;
            clock += dt;
            if (clock - lastSpendTime >= regenDelay)
                Current = Mathf.Min(max, Current + regenPerSecond * dt);
        }
    }
}
