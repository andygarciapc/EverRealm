using UnityEngine;

namespace EverRealm.Combat
{
    /// <summary>
    /// Points a rigged model's right arm along the weapon pivot that <see cref="CombatVisuals"/> poses (the
    /// weapon sits in that hand), and hangs the left arm at the side. On a body without animation clips it
    /// poses the arms all the time; on one with a <see cref="Characters.CharacterAnimator"/> it only blends in
    /// while blocking, since there is no guard clip yet. Works from the bind pose each frame, so it doesn't
    /// depend on how the bones' local axes were authored.
    /// </summary>
    [DefaultExecutionOrder(200)] // After CombatVisuals has posed the pivot and the Animator has posed the bones.
    public sealed class ArmPoser : MonoBehaviour
    {
        [SerializeField] Transform weaponPivot;
        [SerializeField] Transform rightUpperArm, rightHand;
        [SerializeField] Transform leftUpperArm, leftHand;
        [Tooltip("Direction the left arm hangs, in the character's local space.")]
        public Vector3 leftArmDirection = new Vector3(-0.15f, -1f, 0.05f);
        [Tooltip("How fast the guard pose blends in and out over animation, per second.")]
        public float guardBlendSpeed = 10f;

        Quaternion rightBind, leftBind;
        Vector3 rightAxis, leftAxis; // Shoulder-to-hand direction in each upper arm's local space.
        Combatant combatant;
        bool animated;
        float weight = 1f;

        void Awake()
        {
            rightBind = rightUpperArm.localRotation;
            leftBind = leftUpperArm.localRotation;
            rightAxis = rightUpperArm.InverseTransformDirection(rightHand.position - rightUpperArm.position).normalized;
            leftAxis = leftUpperArm.InverseTransformDirection(leftHand.position - leftUpperArm.position).normalized;
            combatant = GetComponentInParent<Combatant>();
            animated = GetComponentInParent<Characters.CharacterAnimator>() != null;
            if (animated) weight = 0f;
        }

        void LateUpdate()
        {
            if (animated)
            {
                float target = combatant != null && combatant.State == CombatState.Blocking ? 1f : 0f;
                weight = Mathf.MoveTowards(weight, target, guardBlendSpeed * combatant.LocalTime.DeltaTime);
                if (weight <= 0f) return;
            }
            Aim(rightUpperArm, rightBind, rightAxis, weaponPivot.forward, weight);
            Aim(leftUpperArm, leftBind, leftAxis, transform.TransformDirection(leftArmDirection.normalized), weight);
        }

        static void Aim(Transform bone, Quaternion bind, Vector3 localAxis, Vector3 worldDirection, float weight)
        {
            Quaternion posed = bone.localRotation;
            bone.localRotation = bind;
            Vector3 current = bone.TransformDirection(localAxis);
            Quaternion aimed = Quaternion.FromToRotation(current, worldDirection) * bone.rotation;
            bone.localRotation = posed;
            bone.rotation = Quaternion.Slerp(bone.rotation, aimed, weight);
        }
    }
}
