using EverRealm.Characters;
using EverRealm.Player;
using NUnit.Framework;
using UnityEngine;

namespace EverRealm.Tests
{
    public class SpeechTests
    {
        [Test]
        public void LouderCarriesFurther_AndShowsBigger()
        {
            Assert.Less(Speech.HearingRadius(Volume.Whisper), Speech.HearingRadius(Volume.Talk));
            Assert.Less(Speech.HearingRadius(Volume.Talk), Speech.HearingRadius(Volume.Shout));
            Assert.Less(SpeechBubbles.FontSize(Volume.Whisper), SpeechBubbles.FontSize(Volume.Talk));
            Assert.Less(SpeechBubbles.FontSize(Volume.Talk), SpeechBubbles.FontSize(Volume.Shout));
        }

        [Test]
        public void ShoutReachesTheWholeHamlet()
        {
            // Default hamlet radius is 14m: a shout from one edge carries across to the other.
            Assert.GreaterOrEqual(Speech.HearingRadius(Volume.Shout), 2 * 14f);
        }

        [TestCase(Volume.Whisper, 2.9f, true)]
        [TestCase(Volume.Whisper, 3.1f, false)]
        [TestCase(Volume.Talk, 11.9f, true)]
        [TestCase(Volume.Talk, 12.1f, false)]
        public void HeardOnlyWithinRange(Volume volume, float distance, bool heard)
        {
            var line = new Utterance(null, "hello", volume, SpeechIntent.Chatter, Vector3.zero, 0f);
            Assert.AreEqual(heard, line.CanBeHeardAt(new Vector3(distance, 0f, 0f)));
        }
    }
}
