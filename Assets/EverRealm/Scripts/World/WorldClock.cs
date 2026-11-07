using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace EverRealm.World
{
    public enum DayPhase { Day, Evening, Night }

    /// <summary>
    /// The world's time of day, shared by everyone in it, and the sun and ambient light that follow it.
    /// Advances with game time (never touches Time.timeScale). T skips ahead (debug).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class WorldClock : MonoBehaviour
    {
        [Tooltip("Real minutes per in-game day.")]
        public float dayLengthMinutes = 12f;
        [Range(0f, 24f)] public float startHour = 8f;

        [Header("Phases (hour of day)")]
        public float dayStartHour = 6f;
        public float eveningStartHour = 17f;
        public float nightStartHour = 21f;

        [Header("Sun")]
        [SerializeField] Light sun;
        [Tooltip("The sun rises at this hour and sets twelve hours later.")]
        public float sunriseHour = 6f;
        public float dayIntensity = 2f;
        public Color noonColor = Color.white;
        public Color horizonColor = new Color(1f, 0.62f, 0.38f);

        [Header("Ambient")]
        public Color daySky = new Color(0.55f, 0.62f, 0.72f);
        public Color dayEquator = new Color(0.40f, 0.42f, 0.42f);
        public Color dayGround = new Color(0.22f, 0.20f, 0.16f);
        [Tooltip("Night stays dim but playable.")]
        public Color nightSky = new Color(0.16f, 0.20f, 0.34f);
        public Color nightEquator = new Color(0.10f, 0.12f, 0.20f);
        public Color nightGround = new Color(0.05f, 0.05f, 0.08f);

        [Header("Distance haze")]
        [Tooltip("Haze begins this far from the viewer, in metres. Fog itself is switched on in the scene's lighting settings.")]
        public float hazeStart = 140f;
        [Tooltip("Nothing shows through the haze beyond this distance, so other places are found, not seen.")]
        public float hazeEnd = 420f;
        public Color dayHaze = new Color(0.72f, 0.79f, 0.87f);
        public Color nightHaze = new Color(0.05f, 0.06f, 0.11f);

        [Header("Debug")]
        public float skipHours = 2f;

        // Dev-only key, kept out of the player's action map.
        readonly InputAction skipAction = new InputAction("Skip Time", binding: "<Keyboard>/t");
        float sunYaw;

        /// <summary>Hours since the start of day 1.</summary>
        public double TotalHours { get; private set; }
        public float Hour => (float)(TotalHours % 24.0);
        public int Day => (int)(TotalHours / 24.0) + 1;
        public DayPhase Phase => PhaseAt(Hour, dayStartHour, eveningStartHour, nightStartHour);
        public bool IsNight => Phase == DayPhase.Night;
        /// <summary>Dawns since the start of the world; changes by one at each <see cref="dayStartHour"/>.</summary>
        public int Dawns => DawnsBy(TotalHours, dayStartHour);

        public static int DawnsBy(double totalHours, float dayStartHour) => (int)System.Math.Floor((totalHours - dayStartHour) / 24.0);

        public static float HoursPerSecond(float dayLengthMinutes) => 24f / (dayLengthMinutes * 60f);

        public static DayPhase PhaseAt(float hour, float dayStart, float eveningStart, float nightStart)
        {
            if (hour >= dayStart && hour < eveningStart) return DayPhase.Day;
            if (hour >= eveningStart && hour < nightStart) return DayPhase.Evening;
            return DayPhase.Night;
        }

        /// <summary>Sun elevation in degrees: 0 at sunrise, 90 at noon, 180 at sunset, below the horizon at night.</summary>
        public static float SunElevation(float hour, float sunriseHour) => Mathf.Repeat(hour - sunriseHour, 24f) / 24f * 360f;

        void Awake()
        {
            TotalHours = startHour;
            if (sun != null)
            {
                sunYaw = sun.transform.eulerAngles.y;
                RenderSettings.sun = sun;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fogStartDistance = hazeStart;
            RenderSettings.fogEndDistance = hazeEnd;
            ApplyLighting();
        }

        void OnEnable()
        {
            skipAction.performed += OnSkip;
            skipAction.Enable();
        }

        void OnDisable()
        {
            skipAction.performed -= OnSkip;
            skipAction.Disable();
        }

        void OnSkip(InputAction.CallbackContext _) => TotalHours += skipHours;

        void Update()
        {
            TotalHours += Time.deltaTime * HoursPerSecond(dayLengthMinutes);
            ApplyLighting();
        }

        void ApplyLighting()
        {
            float elevation = SunElevation(Hour, sunriseHour);
            float height = Mathf.Sin(elevation * Mathf.Deg2Rad); // 1 at noon, negative at night.

            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(elevation, sunYaw, 0f);
                sun.intensity = dayIntensity * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.05f, 0.25f, height));
                // Stays enabled below the horizon so the procedural sky follows it into night.
                sun.color = Color.Lerp(horizonColor, noonColor, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.5f, height)));
            }

            float daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.2f, 0.3f, height));
            RenderSettings.ambientSkyColor = Color.Lerp(nightSky, daySky, daylight);
            RenderSettings.ambientEquatorColor = Color.Lerp(nightEquator, dayEquator, daylight);
            RenderSettings.ambientGroundColor = Color.Lerp(nightGround, dayGround, daylight);
            RenderSettings.fogColor = Color.Lerp(nightHaze, dayHaze, daylight);
        }
    }
}
