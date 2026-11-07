using EverRealm.Characters;
using EverRealm.Enemies;
using EverRealm.Player;
using EverRealm.Settlements;
using EverRealm.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EverRealm
{
    /// <summary>Dev-only readout of world, controlled-body and performance stats, plus the debug key reminders. F1 hides and shows it; F2 shows the ledger of who is where.</summary>
    public sealed class DebugOverlay : MonoBehaviour
    {
        [SerializeField] WorldBuilder world;
        [SerializeField] PlayerSession session;
        [SerializeField] EncounterSpawner encounters;
        [SerializeField] WorldClock clock;
        [SerializeField] HamletPopulation population;
        [SerializeField] RaidDirector raids;
        [SerializeField] JourneyDirector journeys;

        // Dev-only key, kept out of the player's action map.
        readonly InputAction toggleAction = new InputAction("Toggle Debug Overlay", binding: "<Keyboard>/f1");
        readonly InputAction ledgerAction = new InputAction("Toggle Ledger", binding: "<Keyboard>/f2");

        float smoothedDeltaTime;
        GUIStyle style;
        bool visible = true, ledgerVisible;

        void OnEnable()
        {
            toggleAction.performed += OnToggle;
            toggleAction.Enable();
            ledgerAction.performed += OnToggleLedger;
            ledgerAction.Enable();
        }

        void OnDisable()
        {
            toggleAction.Disable();
            toggleAction.performed -= OnToggle;
            ledgerAction.Disable();
            ledgerAction.performed -= OnToggleLedger;
        }

        void OnToggle(InputAction.CallbackContext _) => visible = !visible;
        void OnToggleLedger(InputAction.CallbackContext _) => ledgerVisible = !ledgerVisible;

        void Update() => smoothedDeltaTime = Mathf.Lerp(smoothedDeltaTime, Time.unscaledDeltaTime, 0.05f);

        void OnGUI()
        {
            style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = true, wordWrap = true };
            if (ledgerVisible) DrawLedger();
            if (!visible) return;

            float fps = smoothedDeltaTime > 0f ? 1f / smoothedDeltaTime : 0f;
            var body = session.CurrentBody;
            string bodyText = body == null ? "No body\n\n" :
                $"{body.name}   HP: {body.Health:F0}/{body.maxHealth:F0}   Stamina: {body.Stamina.Current:F0}/{body.Stamina.max:F0}\n" +
                $"State: {body.State}   Gait: {body.Motor.ResolvedGait}   Dodge: {body.weightClass} ({body.CurrentDodge.style})\n";
            float hour = clock.Hour;
            string timeText = $"Day {clock.Day}  {Mathf.FloorToInt(hour):00}:{Mathf.FloorToInt(hour % 1f * 60f):00}  ({clock.Phase})\n";
            string hamletText = population.Hamlets.Count == 0 ? "No hamlet on this island\n" : "";
            foreach (Hamlet hamlet in population.Hamlets)
            {
                RaidFront front = raids.FrontOf(hamlet);
                string raidDue = front == null ? "" : front.IsAttacking ? "   attacked" : front.IsMustering ? "   raiders gathering" :
                    $"   raid in {Mathf.Max(0f, (float)(front.NextRaidAt - clock.TotalHours)):F0} h";
                hamletText += $"{hamlet.Name}: {hamlet.State}   {hamlet.LivingFighters} fighters, {hamlet.LivingResidents} people, {hamlet.FreeBeds} beds free{raidDue}\n";
            }
            string raidText = $"Raiders: {raids.ActiveRaiders}\n";
            string placesText = "";
            if (body != null)
            {
                foreach (Settlement settlement in world.Settlements)
                    placesText += $"{settlement.Name} {Steering.FlatDistance(body.transform.position, settlement.Square):F0} m   ";
                if (world.Landing.HasValue)
                    placesText += $"Landing {Steering.FlatDistance(body.transform.position, world.Landing.Value):F0} m";
                placesText += "\n";
            }
            string text =
                $"<b>EverRealm M4</b>\n" +
                $"Seed: {world.Settings.seed}   Block: {world.Settings.blockSize} m   FPS: {fps:F0}\n" +
                $"Generate: {world.GenerationMs:F0} ms   Mesh: {world.MeshingMs:F0} ms\n" +
                timeText +
                bodyText +
                placesText +
                hamletText +
                $"Goblins alive: {encounters.AliveCount}\n" +
                raidText +
                $"<color=#bbbbbb>LMB attack · RMB block · Ctrl dodge · Shift sprint · V weight class\n" +
                $"G spawn goblin · N new character · R new island · T skip 2 h · Y raid nearest hamlet · F1 hide this · F2 ledger · Esc cursor</color>";
            GUI.Box(new Rect(10, 10, 640, style.CalcHeight(new GUIContent(text), 640f)), text, style);
        }

        /// <summary>Everyone on the island and where they are, for checking the record against the world. F2 hides and shows it.</summary>
        void DrawLedger()
        {
            int settled = 0, onRoad = 0, stranded = 0;
            foreach (Hamlet hamlet in population.Hamlets) settled += hamlet.LivingResidents;
            string parties = "";
            foreach (Journey journey in journeys.Journeys)
            {
                if (journey.State == JourneyState.Stranded) stranded += journey.LivingMembers;
                else onRoad += journey.LivingMembers;
                string where = journey.State == JourneyState.Stranded ? "stranded" : $"bound for {journey.Destination.Name}";
                parties += $"{journey.LivingMembers} {journey.Kind} from {journey.Origin}, {where}\n";
            }
            int alive = population.CountAlive();
            string balance = alive == settled + onRoad + stranded ? "adds up" : "<color=#ff6666>DOES NOT ADD UP</color>";
            string text =
                $"<b>Ledger</b>\n" +
                $"Alive: {alive} = {settled} settled + {onRoad} on the road + {stranded} stranded ({balance})\n" +
                $"Landed: {journeys.Landed}   Journeys ended: {journeys.ArrivedJourneys} arrived, {journeys.DeadJourneys} died\n\n" +
                (parties.Length > 0 ? parties : "Nobody on the road\n") + "\n" +
                string.Join("\n", journeys.Events);
            float width = 620f;
            GUI.Box(new Rect(Screen.width - width - 10f, 10f, width, style.CalcHeight(new GUIContent(text), width)), text, style);
        }
    }
}
