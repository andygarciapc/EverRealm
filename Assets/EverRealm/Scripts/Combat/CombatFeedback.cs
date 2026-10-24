using Unity.Cinemachine;
using UnityEngine;

namespace EverRealm.Combat
{
    /// <summary>
    /// Visual and camera feedback for one character: a white flash when hurt, a red tint that builds on
    /// the weapon during wind-up (the telegraph), and camera shake when an impulse source is assigned
    /// (only the local player has one).
    /// </summary>
    [RequireComponent(typeof(Combatant))]
    public sealed class CombatFeedback : MonoBehaviour
    {
        [SerializeField] Renderer[] bodyRenderers;
        [SerializeField] Renderer[] weaponRenderers;
        [Tooltip("Optional. Only the local player shakes the camera.")]
        [SerializeField] CinemachineImpulseSource impulseSource;

        [Header("Flash")]
        public Color flashColor = Color.white;
        public float flashDuration = 0.1f;

        [Header("Telegraph")]
        public Color windupTint = new Color(1f, 0.15f, 0.1f);

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
            if (impulseSource != null && force > 0f) impulseSource.GenerateImpulseWithForce(force);
        }

        void LateUpdate()
        {
            bool flashing = Time.time < flashUntil;
            foreach (Renderer r in bodyRenderers)
                Apply(r, flashing ? flashColor : (Color?)null);

            bool winding = combatant.State == CombatState.Windup;
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
