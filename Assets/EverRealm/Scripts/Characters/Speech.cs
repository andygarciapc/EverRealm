using System;
using UnityEngine;

namespace EverRealm.Characters
{
    public enum Volume { Whisper, Talk, Shout }

    /// <summary>What a line means to listeners. Nobody parses the words themselves.</summary>
    public enum SpeechIntent { Chatter, Alarm }

    /// <summary>One thing said aloud: who said it, where, how loudly and what it means.</summary>
    public readonly struct Utterance
    {
        public readonly Speech Speaker;
        public readonly string Text;
        public readonly Volume Volume;
        public readonly SpeechIntent Intent;
        public readonly Vector3 Position;
        public readonly float Time;

        public Utterance(Speech speaker, string text, Volume volume, SpeechIntent intent, Vector3 position, float time)
        {
            Speaker = speaker;
            Text = text;
            Volume = volume;
            Intent = intent;
            Position = position;
            Time = time;
        }

        public bool CanBeHeardAt(Vector3 listener) => Vector3.Distance(Position, listener) <= Speech.HearingRadius(Volume);
    }

    /// <summary>
    /// Lets a body say things aloud. Speaking is a world event: <see cref="Spoken"/> tells every listener,
    /// who decide for themselves whether they're in earshot (<see cref="Utterance.CanBeHeardAt"/>). How the
    /// words are shown to the local viewer is up to the presentation layer. Any brain can use it.
    /// </summary>
    [RequireComponent(typeof(Agent))]
    public sealed class Speech : MonoBehaviour
    {
        /// <summary>Raised whenever anyone says something.</summary>
        public static event Action<Utterance> Spoken;

        public Agent Agent { get; private set; }
        /// <summary>The last thing this body said, if anything.</summary>
        public Utterance? Last { get; private set; }
        /// <summary>Just above the head, where words appear to come from.</summary>
        public Vector3 MouthPosition => transform.position + Vector3.up * headHeight;

        float headHeight = 2f;

        public static float HearingRadius(Volume volume) => volume switch
        {
            Volume.Whisper => 3f,
            Volume.Talk => 12f,
            _ => 40f,
        };

        void Awake()
        {
            Agent = GetComponent<Agent>();
            if (TryGetComponent(out CharacterController controller))
                headHeight = controller.center.y + controller.height * 0.5f;
        }

        public void Say(string text, Volume volume, SpeechIntent intent = SpeechIntent.Chatter)
        {
            var utterance = new Utterance(this, text, volume, intent, MouthPosition, UnityEngine.Time.time);
            Last = utterance;
            Spoken?.Invoke(utterance);
        }
    }
}
