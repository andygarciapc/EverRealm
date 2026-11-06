using EverRealm.Enemies;
using EverRealm.Player;
using EverRealm.Settlements;
using EverRealm.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EverRealm
{
    /// <summary>Dev-only readout of world, controlled-body and performance stats, plus the debug key reminders. F1 hides and shows it.</summary>
    public sealed class DebugOverlay : MonoBehaviour
    {
        [SerializeField] WorldBuilder world;
        [SerializeField] PlayerSession session;
        [SerializeField] EncounterSpawner encounters;
        [SerializeField] WorldClock clock;
        [SerializeField] HamletPopulation population;
        [SerializeField] RaidDirector raids;

        // Dev-only key, kept out of the player's action map.
        readonly InputAction toggleAction = new InputAction("Toggle Debug Overlay", binding: "<Keyboard>/f1");

        float smoothedDeltaTime;
        GUIStyle style;
        bool visible = true;

        void OnEnable()
        {
            toggleAction.performed += OnToggle;
            toggleAction.Enable();
        }

        void OnDisable()
        {
            toggleAction.Disable();
            toggleAction.performed -= OnToggle;
        }

        void OnToggle(InputAction.CallbackContext _) => visible = !visible;

        void Update() => smoothedDeltaTime = Mathf.Lerp(smoothedDeltaTime, Time.unscaledDeltaTime, 0.05f);

        void OnGUI()
        {
            if (!visible) return;
            style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = true };

            float fps = smoothedDeltaTime > 0f ? 1f / smoothedDeltaTime : 0f;
            var body = session.CurrentBody;
            string bodyText = body == null ? "No body\n\n" :
                $"{body.name}   HP: {body.Health:F0}/{body.maxHealth:F0}   Stamina: {body.Stamina.Current:F0}/{body.Stamina.max:F0}\n" +
                $"State: {body.State}   Gait: {body.Motor.ResolvedGait}   Dodge: {body.weightClass} ({body.CurrentDodge.style})\n";
            float hour = clock.Hour;
            string timeText = $"Day {clock.Day}  {Mathf.FloorToInt(hour):00}:{Mathf.FloorToInt(hour % 1f * 60f):00}  ({clock.Phase})\n";
            string hamletText = world.Settlement == null ? "No hamlet on this island\n" :
                $"Villagers alive: {population.CountAlive(Temperament.Militia)} militia, {population.CountAlive(Temperament.Civilian)} civilians\n" +
                population.State switch
                {
                    HamletState.Fallen => $"Hamlet fallen: colonists in {Mathf.Max(0f, (float)population.HoursUntilColonists):F0} h\n",
                    HamletState.Ruin => "Hamlet fallen: a ruin for good\n",
                    _ => $"Free beds: {population.FreeBeds}   Settlers in {Mathf.Max(0f, (float)population.HoursUntilSettlers):F0} h\n",
                };
            string raidText = $"Raiders: {raids.ActiveRaiders}{(raids.IsMustering ? " (gathering)" : "")}   Next raid in {Mathf.Max(0f, (float)raids.HoursUntilNextRaid):F0} h\n" +
                              $"Last raid: {raids.LastRaid}\n";
            string text =
                $"<b>EverRealm M3</b>\n" +
                $"Seed: {world.Settings.seed}   Block: {world.Settings.blockSize} m   FPS: {fps:F0}\n" +
                $"Generate: {world.GenerationMs:F0} ms   Mesh: {world.MeshingMs:F0} ms\n" +
                timeText +
                bodyText +
                hamletText +
                $"Goblins alive: {encounters.AliveCount}\n" +
                raidText +
                $"<color=#bbbbbb>LMB attack · RMB block · Ctrl dodge · Shift sprint · V weight class\n" +
                $"G spawn goblin · N new character · R new island · T skip 2 h · Y raid now · F1 hide this · Esc cursor</color>";
            GUI.Box(new Rect(10, 10, 640, 285), text, style);
        }
    }
}
