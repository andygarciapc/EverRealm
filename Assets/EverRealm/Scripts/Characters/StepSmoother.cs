using UnityEngine;

namespace EverRealm.Characters
{
    /// <summary>
    /// The controller pops a body up a whole block in one frame. This lets what you see catch up over a
    /// moment instead, so a step reads as a step. Presentation only: the body itself doesn't change.
    /// </summary>
    [DefaultExecutionOrder(150)] // After the motor, before anything that draws the body in LateUpdate.
    [RequireComponent(typeof(CharacterMotor), typeof(LocalTime))]
    public sealed class StepSmoother : MonoBehaviour
    {
        [Tooltip("Optional. Follows more slowly than the model, so the view glides up slopes.")]
        [SerializeField] Transform cameraTarget;

        [Tooltip("Seconds for the model to close most of the gap after a step.")]
        public float visualCatchUp = 0.07f;
        [Tooltip("Seconds for the camera target to close most of the gap after a step.")]
        public float cameraCatchUp = 0.16f;
        [Tooltip("A rise bigger than this is a teleport, not a step.")]
        public float largestStep = 1.5f;

        CharacterMotor motor;
        LocalTime localTime;
        Vector3 cameraHome;
        float cameraLag;

        /// <summary>How far the model should sit below the body right now.</summary>
        public float VisualLag { get; private set; }

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            localTime = GetComponent<LocalTime>();
            if (cameraTarget != null) cameraHome = cameraTarget.localPosition;
        }

        void Update()
        {
            float dt = localTime.DeltaTime, rise = motor.StepRise;
            if (rise > largestStep) rise = VisualLag = cameraLag = 0f;
            VisualLag = CatchUp(VisualLag + rise, visualCatchUp, dt);
            cameraLag = CatchUp(cameraLag + rise, cameraCatchUp, dt);
            if (cameraTarget != null) cameraTarget.localPosition = cameraHome + Vector3.down * cameraLag;
        }

        static float CatchUp(float lag, float seconds, float dt) => lag * Mathf.Exp(-dt / seconds);
    }
}
