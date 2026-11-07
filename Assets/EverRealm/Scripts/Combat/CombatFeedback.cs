using Unity.Cinemachine;
using UnityEngine;

namespace EverRealm.Combat
{
    /// <summary>
    /// Visual and camera feedback for one character: a white flash when hurt, a red tint that builds on
    /// the weapon during wind-up (the telegraph), and camera shake while the viewer's impulse source is
    /// assigned (only to the body the local player controls).
    /// </summary>
    [RequireComponent(typeof(Combatant))]
    public sealed class CombatFeedback : MonoBehaviour
    {
        [SerializeField] Renderer[] bodyRenderers;
        [SerializeField] Renderer[] weaponRenderers;

        /// <summary>Set by <c>PlayerSession</c> on the body the local player controls; null otherwise.</summary>
        public CinemachineImpulseSource ImpulseSource { get; set; }

        [Header("Flash")]
        public Color flashColor = Color.white;
        public float flashDuration = 0.1f;

        [Header("Telegraph")]
        public Color windupTint = new Color(1f, 0.15f, 0.1f);
        [Tooltip("The whole body glows through the wind-up too, not just the weapon. For enemies you need to read at a glance.")]
        public bool telegraphBody;
        [Tooltip("How far the body's colour moves towards the tint by the end of the wind-up.")]
        [Range(0f, 1f)] public float bodyTelegraphStrength = 0.85f;

        [Header("Camera shake (impulse force)")]
        public float shakeWhenHurt = 0.6f;
        public float shakeWhenBlocking = 0.3f;
        public float shakeWhenLandingHit = 0.25f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        Combatant combatant;
        MaterialPropertyBlock block;
        float flashUntil;

        void Awake()
        {
            combatant = GetComponent<Combatant>();
            block = new MaterialPropertyBlock();
        }

        void OnEnable()
        {
            combatant.HitReceived += OnHitReceived;
            combatant.HitLanded += OnHitLanded;
        }

        void OnDisable()
        {
            combatant.HitReceived -= OnHitReceived;
            combatant.HitLanded -= OnHitLanded;
        }

        void OnHitReceived(HitResult result, Combatant attacker)
        {
            switch (result)
            {
                case HitResult.Damaged:
                case HitResult.GuardBroken:
                    flashUntil = Time.time + flashDuration;
                    Shake(shakeWhenHurt);
                    break;
                case HitResult.Blocked:
                case HitResult.Parried:
                    Shake(shakeWhenBlocking);
                    break;
            }
        }

        void OnHitLanded(HitResult result, Combatant target)
        {
            if (result is HitResult.Damaged or HitResult.GuardBroken or HitResult.Blocked)
                Shake(shakeWhenLandingHit);
        }

        void Shake(float force)
        {
            if (ImpulseSource != null && force > 0f) ImpulseSource.GenerateImpulseWithForce(force);
        }

        void LateUpdate()
        {
            bool flashing = Time.time < flashUntil;
            bool winding = combatant.State == CombatState.Windup;
            foreach (Renderer r in bodyRenderers)
            {
                Color? color = null;
                if (flashing) color = flashColor;
                else if (winding && telegraphBody)
                    color = Color.Lerp(r.sharedMaterial.GetColor(BaseColor), windupTint, combatant.StateProgress * bodyTelegraphStrength);
                Apply(r, color);
            }

            foreach (Renderer r in weaponRenderers)
            {
                Color? color = null;
                if (flashing) color = flashColor;
                else if (winding) color = Color.Lerp(r.sharedMaterial.GetColor(BaseColor), windupTint, combatant.StateProgress);
                Apply(r, color);
            }
        }

        void Apply(Renderer target, Color? color)
        {
            if (color.HasValue)
            {
                block.SetColor(BaseColor, color.Value);
                target.SetPropertyBlock(block);
            }
            else if (target.HasPropertyBlock())
            {
                target.SetPropertyBlock(null);
            }
        }
    }
}
