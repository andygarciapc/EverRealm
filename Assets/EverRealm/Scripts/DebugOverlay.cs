using EverRealm.Combat;
using EverRealm.Enemies;
using EverRealm.World;
using UnityEngine;

namespace EverRealm
{
    /// <summary>Dev-only readout of world, combat and performance stats, plus the debug key reminders.</summary>
    public sealed class DebugOverlay : MonoBehaviour
    {
        [SerializeField] WorldBuilder world;
        [SerializeField] Combatant player;
        [SerializeField] EncounterSpawner encounters;

        float smoothedDeltaTime;
        GUIStyle style;

        void Update() => smoothedDeltaTime = Mathf.Lerp(smoothedDeltaTime, Time.unscaledDeltaTime, 0.05f);

        void OnGUI()
        {
            style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = true };

            float fps = smoothedDeltaTime > 0f ? 1f / smoothedDeltaTime : 0f;
            string text =
                $"<b>EverRealm M2</b>\n" +
                $"Seed: {world.Settings.seed}   Block: {world.Settings.blockSize} m   FPS: {fps:F0}\n" +
                $"Generate: {world.GenerationMs:F0} ms   Mesh: {world.MeshingMs:F0} ms\n" +
                $"HP: {player.Health:F0}/{player.maxHealth:F0}   Stamina: {player.Stamina:F0}/{player.maxStamina:F0}\n" +
                $"State: {player.State}   Dodge: {player.weightClass} ({player.CurrentDodge.style})\n" +
                $"Goblins alive: {encounters.AliveCount}\n" +
                $"<color=#bbbbbb>LMB attack · RMB block · Ctrl dodge · V weight class\n" +
                $"G spawn goblin · R new seed · B block size · Esc free cursor</color>";
            GUI.Box(new Rect(10, 10, 430, 175), text, style);
        }
    }
}
