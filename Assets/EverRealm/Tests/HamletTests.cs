using System.Collections.Generic;
using EverRealm.Settlements;
using EverRealm.World;
using NUnit.Framework;
using UnityEngine;

namespace EverRealm.Tests
{
    public class HamletTests
    {
        const float DefendRadius = 30f;

        public sealed class Person : IPerson
        {
            public bool IsAlive { get; set; } = true;
            public bool Armed { get; set; } = true;
            public bool Broken { get; set; }
            public bool IsFighter => IsAlive && Armed && !Broken;
            public Vector3 Position { get; set; }
        }

        public static Hamlet MakeHamlet(int houses, string name = "Testham", Vector3 square = default)
        {
            var list = new List<House>();
            for (int i = 0; i < houses; i++)
                list.Add(new House(square, square, square, new Rect(square.x + i * 10f, square.z, 5f, 5f)));
            return new Hamlet(new Settlement(name, square, 14f, list, new List<Vector3> { square }));
        }

        static Person MoveIn(Hamlet hamlet, bool armed = true)
        {
            var person = new Person { Armed = armed, Position = hamlet.Settlement.Square };
            Assert.IsTrue(hamlet.TryTakeBed(person, out _, out _));
            return person;
        }

        static int Present(Hamlet hamlet, params IPerson[] people) => hamlet.CountFightersPresent(people, DefendRadius);

        [Test]
        public void BedsFillEvenly_AndNeverDouble()
        {
            Hamlet hamlet = MakeHamlet(2);
            var taken = new HashSet<(House, int)>();
            var houses = new List<House>();
            for (int i = 0; i < hamlet.Capacity; i++)
            {
                Assert.IsTrue(hamlet.TryTakeBed(new Person(), out House house, out int spot));
                Assert.IsTrue(taken.Add((house, spot)), "two people in one bed");
                houses.Add(house);
            }
            Assert.AreNotEqual(houses[0], houses[1], "the second person should pick the emptier house");
            Assert.AreEqual(0, hamlet.FreeBeds);
            Assert.IsFalse(hamlet.TryTakeBed(new Person(), out _, out _));
        }

        [Test]
        public void TheDeadFreeTheirBeds()
        {
            Hamlet hamlet = MakeHamlet(1);
            Person first = MoveIn(hamlet);
            MoveIn(hamlet);
            MoveIn(hamlet);
            first.IsAlive = false;
            Assert.AreEqual(1, hamlet.FreeBeds);
            Assert.IsTrue(hamlet.TryTakeBed(new Person(), out _, out int spot));
            Assert.AreEqual(0, spot);
        }

        [Test]
        public void TheLastMilitiaDyingOutsideARaid_ChangesNothing()
        {
            Hamlet hamlet = MakeHamlet(2);
            Person militia = MoveIn(hamlet);
            MoveIn(hamlet, armed: false);
            militia.IsAlive = false;
            hamlet.Review(DefendRadius);
            Assert.AreEqual(HamletState.Living, hamlet.State);
            Assert.IsFalse(hamlet.ReviewAtDawn(0), "nobody leaves without an attack");
            Assert.AreEqual(HamletState.Living, hamlet.State);
        }

        [Test]
        public void ARaidEndingWithNoFighterPresent_MarksAbandoning()
        {
            Hamlet hamlet = MakeHamlet(2);
            MoveIn(hamlet, armed: false);
            hamlet.BeginAttack();
            Assert.AreEqual(HamletState.UnderAttack, hamlet.State);
            hamlet.EndAttack(0);
            Assert.AreEqual(HamletState.Abandoning, hamlet.State);
        }

        [Test]
        public void ARaidEndingWithOneFighterPresent_Holds()
        {
            Hamlet hamlet = MakeHamlet(2);
            MoveIn(hamlet, armed: false);
            hamlet.BeginAttack();
            hamlet.EndAttack(1);
            Assert.AreEqual(HamletState.Living, hamlet.State);
        }

        [Test]
        public void AVisitorCounts_ButAnAbsentResidentDoesNot()
        {
            Hamlet hamlet = MakeHamlet(2);
            Person resident = MoveIn(hamlet);
            resident.Position = new Vector3(200f, 0f, 0f);
            var visitor = new Person { Position = new Vector3(10f, 0f, 0f) };
            Assert.AreEqual(0, Present(hamlet, resident));
            Assert.AreEqual(1, Present(hamlet, resident, visitor));
        }

        [Test]
        public void BrokenAndDeadMilitia_AndCiviliansDontCount()
        {
            Hamlet hamlet = MakeHamlet(2);
            Person broken = MoveIn(hamlet);
            broken.Broken = true;
            Person dead = MoveIn(hamlet);
            dead.IsAlive = false;
            Person civilian = MoveIn(hamlet, armed: false);
            Assert.AreEqual(0, Present(hamlet, broken, dead, civilian));
        }

        [Test]
        public void AFighterPresentAtDawn_Cancels()
        {
            Hamlet hamlet = MakeHamlet(2);
            MoveIn(hamlet, armed: false);
            hamlet.BeginAttack();
            hamlet.EndAttack(0);
            Assert.IsFalse(hamlet.ReviewAtDawn(1));
            Assert.AreEqual(HamletState.Living, hamlet.State);
        }

        [Test]
        public void NoFighterAtDawn_MeansLeaving()
        {
            Hamlet hamlet = MakeHamlet(2);
            MoveIn(hamlet, armed: false);
            hamlet.BeginAttack();
            hamlet.EndAttack(0);
            Assert.IsTrue(hamlet.ReviewAtDawn(0));
            Assert.AreEqual(HamletState.Empty, hamlet.State);
        }

        [Test]
        public void ARaidKillingEveryone_LeavesItEmpty()
        {
            Hamlet hamlet = MakeHamlet(2);
            Person only = MoveIn(hamlet);
            hamlet.BeginAttack();
            only.IsAlive = false;
            hamlet.Review(DefendRadius);
            Assert.AreEqual(HamletState.UnderAttack, hamlet.State, "still under attack until the raiders are done");
            hamlet.EndAttack(0);
            Assert.AreEqual(HamletState.Empty, hamlet.State);
        }

        [Test]
        public void TheLastResidentDyingAnywhere_LeavesItEmpty()
        {
            Hamlet hamlet = MakeHamlet(2);
            Person only = MoveIn(hamlet);
            only.IsAlive = false;
            hamlet.Review(DefendRadius);
            Assert.AreEqual(HamletState.Empty, hamlet.State);
        }

        [Test]
        public void SomeoneMovingIn_RevivesAnEmptyHamlet()
        {
            Hamlet hamlet = MakeHamlet(2);
            Person only = MoveIn(hamlet);
            only.IsAlive = false;
            hamlet.Review(DefendRadius);
            MoveIn(hamlet);
            Assert.AreEqual(HamletState.Living, hamlet.State);
        }

        [Test]
        public void AnAbsentResidentComingBack_RevivesItOnlyOnceThere()
        {
            // The body the player controls keeps its home when the others leave.
            Hamlet hamlet = MakeHamlet(2);
            Person player = MoveIn(hamlet);
            player.Position = new Vector3(300f, 0f, 0f);
            hamlet.BeginAttack();
            hamlet.EndAttack(0);
            hamlet.ReviewAtDawn(0);
            hamlet.Review(DefendRadius);
            Assert.AreEqual(HamletState.Empty, hamlet.State);
            player.Position = Vector3.zero;
            hamlet.Review(DefendRadius);
            Assert.AreEqual(HamletState.Living, hamlet.State);
        }

        [Test]
        public void OnlyALivingHamletCanBeAttacked()
        {
            Hamlet hamlet = MakeHamlet(2);
            Person only = MoveIn(hamlet);
            only.IsAlive = false;
            hamlet.Review(DefendRadius);
            hamlet.BeginAttack();
            Assert.AreEqual(HamletState.Empty, hamlet.State);
        }

        [Test]
        public void DawnsCountUpAtDayStart()
        {
            Assert.AreEqual(0, WorldClock.DawnsBy(8.0, 6f));
            Assert.AreEqual(0, WorldClock.DawnsBy(29.9, 6f));
            Assert.AreEqual(1, WorldClock.DawnsBy(30.0, 6f));
        }
    }
}
