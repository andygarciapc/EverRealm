using EverRealm.World;
using NUnit.Framework;

namespace EverRealm.Tests
{
    public class WorldClockTests
    {
        [TestCase(6f, DayPhase.Day)]
        [TestCase(12f, DayPhase.Day)]
        [TestCase(17f, DayPhase.Evening)]
        [TestCase(20.9f, DayPhase.Evening)]
        [TestCase(21f, DayPhase.Night)]
        [TestCase(0f, DayPhase.Night)]
        [TestCase(5.9f, DayPhase.Night)]
        public void PhaseFollowsTheHour(float hour, DayPhase expected) =>
            Assert.AreEqual(expected, WorldClock.PhaseAt(hour, 6f, 17f, 21f));

        [Test]
        public void TwelveMinuteDay_PassesTwoHoursPerRealMinute() =>
            Assert.AreEqual(2f, WorldClock.HoursPerSecond(12f) * 60f, 1e-4f);

        [TestCase(6f, 0f)]
        [TestCase(12f, 90f)]
        [TestCase(18f, 180f)]
        [TestCase(0f, 270f)]
        public void SunRisesAtSunriseAndPeaksAtNoon(float hour, float elevation) =>
            Assert.AreEqual(elevation, WorldClock.SunElevation(hour, 6f), 1e-3f);
    }
}
