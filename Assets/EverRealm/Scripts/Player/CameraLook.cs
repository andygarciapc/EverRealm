using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EverRealm.Player
{
    /// <summary>
    /// Feeds the "Player/Look" action into a Cinemachine orbital camera and manages cursor lock
    /// (click to capture, Escape to release).
    /// </summary>
    [RequireComponent(typeof(CinemachineOrbitalFollow))]
    public sealed class CameraLook : MonoBehaviour
    {
        [Tooltip("Degrees per pixel of mouse movement.")]
        public float mouseSensitivity = 0.12f;
        [Tooltip("Degrees per second at full stick deflection.")]
        public float stickSpeed = 180f;
        public bool invertY;

        CinemachineOrbitalFollow orbit;
        InputAction lookAction;

        void Awake()
        {
            orbit = GetComponent<CinemachineOrbitalFollow>();
            lookAction = InputSystem.actions.FindAction("Player/Look", true);
        }

        void OnEnable() => SetCursorLocked(true);
        void OnDisable() => SetCursorLocked(false);

        void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
            else if (mouse != null && mouse.leftButton.wasPressedThisFrame) SetCursorLocked(true);

            bool fromPointer = lookAction.activeControl?.device is Pointer;
            if (fromPointer && Cursor.lockState != CursorLockMode.Locked) return;

            Vector2 look = lookAction.ReadValue<Vector2>();
            look *= fromPointer ? mouseSensitivity : stickSpeed * Time.deltaTime;
            if (!invertY) look.y = -look.y;

            orbit.HorizontalAxis.Value = orbit.HorizontalAxis.ClampValue(orbit.HorizontalAxis.Value + look.x);
            orbit.VerticalAxis.Value = orbit.VerticalAxis.ClampValue(orbit.VerticalAxis.Value + look.y);
        }

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
