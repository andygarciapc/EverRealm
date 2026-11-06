using System.Collections.Generic;
using EverRealm.Characters;
using UnityEngine;

namespace EverRealm.Player
{
    /// <summary>
    /// Shows what nearby characters say as floating text above their heads, sized by how loudly they said
    /// it. Presentation for the local viewer only: a line appears if the listener (the controlled body, or
    /// the camera when there is none) was in earshot when it was said, and only while the speaker isn't
    /// hidden behind solid ground or walls.
    /// </summary>
    public sealed class SpeechBubbles : MonoBehaviour
    {
        [SerializeField] PlayerSession session;
        [SerializeField] Camera viewCamera;
        public float showSeconds = 2.5f;
        public float fadeSeconds = 0.6f;

        sealed class Bubble
        {
            public Utterance Line;
            public bool Visible;
        }

        // The latest line per speaker.
        readonly Dictionary<Speech, Bubble> bubbles = new Dictionary<Speech, Bubble>();
        readonly List<Speech> expired = new List<Speech>();
        readonly RaycastHit[] hits = new RaycastHit[16];
        GUIStyle style;

        public static int FontSize(Volume volume) => volume switch
        {
            Volume.Whisper => 12,
            Volume.Talk => 18,
            _ => 30,
        };

        void OnEnable() => Speech.Spoken += OnSpoken;
        void OnDisable() => Speech.Spoken -= OnSpoken;

        Vector3 ListenerPosition
        {
            get
            {
                var body = session.CurrentBody;
                return body != null && !body.IsDead ? body.transform.position + Vector3.up * 1.6f : viewCamera.transform.position;
            }
        }

        void OnSpoken(Utterance line)
        {
            if (!line.CanBeHeardAt(ListenerPosition)) return;
            bubbles[line.Speaker] = new Bubble { Line = line };
        }

        void LateUpdate()
        {
            expired.Clear();
            foreach (KeyValuePair<Speech, Bubble> pair in bubbles)
            {
                if (pair.Key == null || Time.time - pair.Value.Line.Time > showSeconds)
                {
                    expired.Add(pair.Key);
                    continue;
                }
                pair.Value.Visible = InClearView(pair.Key);
            }
            foreach (Speech speaker in expired) bubbles.Remove(speaker);
        }

        /// <summary>In front of the camera with no solid world geometry in between; characters don't block.</summary>
        bool InClearView(Speech speaker)
        {
            Vector3 eye = viewCamera.transform.position, mouth = speaker.MouthPosition;
            if (viewCamera.WorldToViewportPoint(mouth).z <= 0f) return false;
            Vector3 toMouth = mouth - eye;
            int count = Physics.RaycastNonAlloc(eye, toMouth.normalized, hits, toMouth.magnitude, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (hits[i].collider.GetComponentInParent<Agent>() == null) return false;
            return true;
        }

        void OnGUI()
        {
            if (bubbles.Count == 0) return;
            style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, richText = false, wordWrap = false };

            foreach (KeyValuePair<Speech, Bubble> pair in bubbles)
            {
                if (pair.Key == null || !pair.Value.Visible) continue;
                Utterance line = pair.Value.Line;
                Vector3 screen = viewCamera.WorldToScreenPoint(pair.Key.MouthPosition + Vector3.up * 0.3f);
                float age = Time.time - line.Time;
                float alpha = Mathf.Clamp01((showSeconds - age) / fadeSeconds);

                style.fontSize = FontSize(line.Volume);
                style.fontStyle = line.Volume == Volume.Shout ? FontStyle.Bold : FontStyle.Normal;
                var content = new GUIContent(line.Text);
                Vector2 size = style.CalcSize(content);
                // GUI space has y pointing down.
                var rect = new Rect(screen.x - size.x * 0.5f, Screen.height - screen.y - size.y, size.x, size.y);

                Color previous = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, alpha * 0.8f);
                GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), content, style);
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Label(rect, content, style);
                GUI.color = previous;
            }
        }
    }
}
