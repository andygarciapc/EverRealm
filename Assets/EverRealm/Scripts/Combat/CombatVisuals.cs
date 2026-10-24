using UnityEngine;

namespace EverRealm.Combat
{
    /// <summary>
    /// Placeholder procedural animation driven by <see cref="Combatant"/> state: the weapon pivot moves
    /// through rest, wind-up, swing and guard poses, and the visual root rolls, hops, lunges, staggers
    /// and falls. Runs on the character's local time so hit-stop freezes it too.
    /// </summary>
    [RequireComponent(typeof(Combatant))]
    public sealed class CombatVisuals : MonoBehaviour
    {
        [SerializeField] Transform visualRoot;
        [SerializeField] Transform weaponPivot;

        [Header("Weapon poses (pivot local euler angles)")]
        public Vector3 restPose = new Vector3(60f, 10f, 0f);
        public Vector3 windupPose = new Vector3(-50f, 75f, 0f);
        public Vector3 swingEndPose = new Vector3(20f, -85f, 0f);
        public Vector3 guardPose = new Vector3(0f, -80f, 0f);
        [Tooltip("Pivot offset while guarding, so the weapon sits across the chest.")]
        public Vector3 guardOffset = new Vector3(-0.1f, 0f, 0.35f);
        public float poseSharpness = 18f;

        [Header("Body")]
        [Tooltip("Height of the body's centre, used as the roll pivot.")]
        public float bodyCenterHeight = 0.9f;
        public float powerStepLean = 18f;
        public float staggerLean = 14f;
        public float deathFallTime = 0.4f;

        Combatant combatant;
        Vector3 pivotHome;

        void Awake()
        {
            combatant = GetComponent<Combatant>();
            pivotHome = weaponPivot.localPosition;
        }

        void LateUpdate()
        {
            float dt = combatant.LocalTime.DeltaTime;
            if (dt <= 0f) return; // Hold the pose during hit-stop.
            UpdateWeapon(dt);
            UpdateBody();
        }

        void UpdateWeapon(float dt)
        {
            Quaternion rest = Quaternion.Euler(restPose), windup = Quaternion.Euler(windupPose), swingEnd = Quaternion.Euler(swingEndPose);
            float p = combatant.StateProgress;
            Vector3 position = pivotHome;
            Quaternion target;
            bool exact = false;

            switch (combatant.State)
            {
                case CombatState.Windup:
                    target = windup;
                    break;
                case CombatState.Active:
                    target = Quaternion.Slerp(windup, swingEnd, p);
                    exact = true;
                    break;
                case CombatState.Recovery:
                    target = Quaternion.Slerp(swingEnd, rest, Mathf.SmoothStep(0f, 1f, p));
                    exact = true;
                    break;
                case CombatState.Blocking:
                    target = Quaternion.Euler(guardPose);
                    position += guardOffset;
                    break;
                default:
                    target = rest;
                    break;
            }

            float blend = 1f - Mathf.Exp(-poseSharpness * dt);
            weaponPivot.localRotation = exact ? target : Quaternion.Slerp(weaponPivot.localRotation, target, blend);
            weaponPivot.localPosition = Vector3.Lerp(weaponPivot.localPosition, position, blend);
        }

        void UpdateBody()
        {
            Quaternion rotation = Quaternion.identity;
            Vector3 offset = Vector3.zero;
            bool aroundCenter = false;

            switch (combatant.State)
            {
                case CombatState.Dodging:
                {
                    DodgeProfile dodge = combatant.CurrentDodge;
                    float t = Mathf.Clamp01(combatant.StateTime / dodge.duration);
                    switch (dodge.style)
                    {
                        case DodgeStyle.Roll:
                            rotation = Quaternion.AngleAxis(360f * Mathf.SmoothStep(0f, 1f, t), Vector3.right);
                            aroundCenter = true;
                            break;
                        case DodgeStyle.Hopstep:
                            offset = Vector3.up * (dodge.hopHeight * 4f * t * (1f - t));
                            break;
                        case DodgeStyle.PowerStep:
                            Vector3 localDir = transform.InverseTransformDirection(combatant.DodgeDirection);
                            Vector3 axis = Vector3.Cross(Vector3.up, localDir);
                            if (axis.sqrMagnitude > 0.0001f)
                                rotation = Quaternion.AngleAxis(powerStepLean * Mathf.Sin(Mathf.PI * t), axis.normalized);
                            break;
                    }
                    break;
                }
                case CombatState.Staggered:
                    rotation = Quaternion.AngleAxis(-staggerLean * Mathf.Sin(Mathf.PI * combatant.StateProgress), Vector3.right);
                    break;
                case CombatState.Dead:
                    rotation = Quaternion.AngleAxis(-90f * Mathf.SmoothStep(0f, 1f, combatant.StateTime / deathFallTime), Vector3.right);
                    break;
            }

            if (aroundCenter)
            {
                Vector3 center = Vector3.up * bodyCenterHeight;
                offset += center - rotation * center;
            }
            visualRoot.localRotation = rotation;
            visualRoot.localPosition = offset;
        }
    }
}
