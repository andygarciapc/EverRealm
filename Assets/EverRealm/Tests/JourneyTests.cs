using System.Collections.Generic;
using EverRealm.Settlements;
using EverRealm.World;
using NUnit.Framework;
using UnityEngine;
using static EverRealm.Tests.HamletTests;

namespace EverRealm.Tests
{
    public class JourneyTests
    {
        const float DefendRadius = 30f;

        static readonly List<Vector3> AnyRoute = new List<Vector3> { Vector3.zero };

        // Hamlets lie along the x axis, so the distance by "road" is plain distance from the origin.
        static float Road(Hamlet hamlet) => hamlet.Settlement.Square.x;

        static Hamlet Place(string name, float x, int houses = 2) => MakeHamlet(houses, name, new Vector3(x, 0f, 0f));

        static List<Person> Fill(Hamlet hamlet, int count, bool armed = true)
        {
            var people = new List<Person>();
            for (int i = 0; i < count; i++)
            {
                var person = new Person { Armed = armed, Position = hamlet.Settlement.Square };
                Assert.IsTrue(hamlet.TryTakeBed(person, out _, out _));
                people.Add(person);
            }
            return people;
        }

        static void Empty(Hamlet hamlet)
        {
            foreach (Person person in Fill(hamlet, 1)) person.IsAlive = false;
            hamlet.Review(DefendRadius);
            Assert.AreEqual(HamletState.Empty, hamlet.State);
        }

        static void Attack(Hamlet hamlet)
        {
            Fill(hamlet, 1);
            hamlet.BeginAttack();
            Assert.AreEqual(HamletState.UnderAttack, hamlet.State);
        }

        static Journey Party(JourneyKind kind, Hamlet destination, int size, out List<Person> people)
        {
            people = new List<Person>();
            for (int i = 0; i < size; i++) people.Add(new Person());
            var journey = new Journey(kind, "Elsewhere", people);
            journey.SetCourse(destination, AnyRoute);
            return journey;
        }

        [TestCase(HamletState.Living, JourneyKind.Refugees, true)]
        [TestCase(HamletState.Living, JourneyKind.Colonists, true)]
        [TestCase(HamletState.UnderAttack, JourneyKind.Refugees, false)]
        [TestCase(HamletState.UnderAttack, JourneyKind.Colonists, false)]
        [TestCase(HamletState.Abandoning, JourneyKind.Refugees, false)]
        [TestCase(HamletState.Empty, JourneyKind.Refugees, false)]
        [TestCase(HamletState.Empty, JourneyKind.Newcomers, false)]
        [TestCase(HamletState.Empty, JourneyKind.Colonists, true)]
        public void Viability(HamletState state, JourneyKind kind, bool viable)
        {
            Hamlet hamlet = Place("A", 0f);
            switch (state)
            {
                case HamletState.UnderAttack:
                    Attack(hamlet);
                    break;
                case HamletState.Abandoning:
                    Attack(hamlet);
                    hamlet.EndAttack(0);
                    break;
                case HamletState.Empty:
                    Empty(hamlet);
                    break;
            }
            Assert.AreEqual(state, hamlet.State);
            Assert.AreEqual(viable, hamlet.IsViableFor(kind));
        }

        [Test]
        public void AFullHamletIsNotViable()
        {
            Hamlet hamlet = Place("A", 0f, houses: 1);
            Fill(hamlet, 3);
            Assert.IsFalse(hamlet.IsViableFor(JourneyKind.Refugees));
        }

        [Test]
        public void ArrivingAtAViableHamlet_EveryoneMovesIn_AndTheJourneyEnds()
        {
            Hamlet hamlet = Place("A", 100f);
            Journey journey = Party(JourneyKind.Refugees, hamlet, 2, out List<Person> people);
            foreach (Person person in people) Assert.IsTrue(journey.TryMoveIn(person, out _, out _));
            journey.Review();
            Assert.AreEqual(JourneyState.Arrived, journey.State);
            Assert.AreEqual(2, hamlet.LivingResidents);
        }

        [Test]
        public void APartlyFullHamlet_SplitsTheParty()
        {
            Hamlet near = Place("Near", 100f, houses: 1);
            Fill(near, 2);
            Hamlet far = Place("Far", 200f);
            var hamlets = new[] { near, far };
            Journey journey = Party(JourneyKind.Refugees, near, 3, out List<Person> people);

            Assert.IsTrue(journey.TryMoveIn(people[0], out _, out _));
            Assert.IsFalse(journey.TryMoveIn(people[1], out _, out _));
            Assert.AreEqual(2, journey.LivingMembers, "those without beds stay on the road");
            Assert.AreEqual(far, Journey.NearestViable(hamlets, journey.Kind, near, Road));
        }

        [Test]
        public void ADestinationThatFell_SendsThemToTheNextViableHamlet()
        {
            Hamlet fallen = Place("Fallen", 100f);
            Empty(fallen);
            Hamlet next = Place("Next", 300f);
            Hamlet nextButFurther = Place("Further", 500f);
            var hamlets = new[] { fallen, nextButFurther, next };
            Journey journey = Party(JourneyKind.Refugees, fallen, 1, out List<Person> people);

            Assert.IsFalse(journey.TryMoveIn(people[0], out _, out _));
            Assert.AreEqual(next, Journey.NearestViable(hamlets, journey.Kind, fallen, Road));
        }

        [Test]
        public void ADestinationUnderAttack_TurnsThemAway()
        {
            Hamlet hamlet = Place("A", 100f);
            Attack(hamlet);
            Journey journey = Party(JourneyKind.Colonists, hamlet, 1, out List<Person> people);
            Assert.IsFalse(journey.TryMoveIn(people[0], out _, out _));
        }

        [Test]
        public void NoViableHamlet_OrNoRoadToOne_MeansNowhereToGo()
        {
            Hamlet fallen = Place("Fallen", 100f);
            Empty(fallen);
            Hamlet cutOff = Place("Cut off", 200f);
            Assert.IsNull(Journey.NearestViable(new[] { fallen }, JourneyKind.Refugees, null, Road));
            Assert.IsNull(Journey.NearestViable(new[] { fallen, cutOff }, JourneyKind.Refugees, null, _ => float.PositiveInfinity));
        }

        [Test]
        public void StrandedPartiesLeave_WhenADawnCheckFindsAViableHamlet()
        {
            Hamlet full = Place("Full", 100f, houses: 1);
            List<Person> residents = Fill(full, 3);
            var hamlets = new[] { full };
            Journey journey = Party(JourneyKind.Refugees, full, 2, out _);
            journey.Strand(Vector3.one);
            Assert.AreEqual(JourneyState.Stranded, journey.State);
            Assert.IsNull(journey.ChooseAtDawn(hamlets, Road), "another day stranded");

            residents[0].IsAlive = false;
            Assert.AreEqual(full, journey.ChooseAtDawn(hamlets, Road));
            journey.SetCourse(full, AnyRoute);
            Assert.AreEqual(JourneyState.Walking, journey.State);
            Assert.AreEqual(JourneyKind.Refugees, journey.Kind);
        }

        [Test]
        public void StrandedParties_AreFirstToResettleAnEmptyHamlet()
        {
            Hamlet empty = Place("Empty", 100f);
            Empty(empty);
            Journey journey = Party(JourneyKind.Refugees, empty, 2, out List<Person> people);
            journey.Strand(Vector3.zero);

            Assert.AreEqual(empty, journey.ChooseAtDawn(new[] { empty }, Road));
            Assert.AreEqual(JourneyKind.Colonists, journey.Kind);

            journey.SetCourse(empty, AnyRoute);
            Assert.IsTrue(journey.TryMoveIn(people[0], out _, out _));
            Assert.AreEqual(HamletState.Living, empty.State, "moving in revives it");
        }

        [Test]
        public void StrandedPartiesPreferALivingHamlet_OverResettling()
        {
            Hamlet empty = Place("Empty", 100f);
            Empty(empty);
            Hamlet living = Place("Living", 900f);
            Journey journey = Party(JourneyKind.Refugees, empty, 1, out _);
            journey.Strand(Vector3.zero);
            Assert.AreEqual(living, journey.ChooseAtDawn(new[] { empty, living }, Road));
            Assert.AreEqual(JourneyKind.Refugees, journey.Kind);
        }

        [Test]
        public void APartyWithNoLivingMembers_CloseAsDead()
        {
            Journey journey = Party(JourneyKind.Newcomers, Place("A", 100f), 2, out List<Person> people);
            journey.Review();
            Assert.AreEqual(JourneyState.Walking, journey.State);
            foreach (Person person in people) person.IsAlive = false;
            journey.Review();
            Assert.AreEqual(JourneyState.Dead, journey.State);
        }

        [Test]
        public void AStrandedPartyDying_ClosesToo()
        {
            Journey journey = Party(JourneyKind.Refugees, Place("A", 100f), 1, out List<Person> people);
            journey.Strand(Vector3.zero);
            people[0].IsAlive = false;
            journey.Review();
            Assert.AreEqual(JourneyState.Dead, journey.State);
        }

        [Test]
        public void SomeArrivingAndTheRestDying_CountsAsArrived()
        {
            Journey journey = Party(JourneyKind.Refugees, Place("A", 100f), 2, out List<Person> people);
            journey.TryMoveIn(people[0], out _, out _);
            people[1].IsAlive = false;
            journey.Review();
            Assert.AreEqual(JourneyState.Arrived, journey.State);
        }

        [Test]
        public void ColonistsAreSpared_LeavingEnoughBehind_AndTheLastFighter()
        {
            var fighter = new Person();
            var civilians = new List<IPerson> { fighter };
            for (int i = 0; i < 5; i++) civilians.Add(new Person { Armed = false });

            List<IPerson> chosen = Journey.ChooseColonists(civilians, wanted: 5, keep: 3);
            Assert.AreEqual(3, chosen.Count);
            CollectionAssert.DoesNotContain(chosen, fighter);
        }

        [Test]
        public void ColonistsTakeAFighter_WhenTheHamletHasTwo()
        {
            var residents = new List<IPerson> { new Person(), new Person() };
            for (int i = 0; i < 4; i++) residents.Add(new Person { Armed = false });
            List<IPerson> chosen = Journey.ChooseColonists(residents, wanted: 3, keep: 3);
            Assert.AreEqual(3, chosen.Count);
            Assert.AreEqual(1, chosen.FindAll(p => p.IsFighter).Count);
        }

        [Test]
        public void ASmallHamletCantSpareColonists()
        {
            var residents = new List<IPerson> { new Person(), new Person { Armed = false }, new Person { Armed = false }, new Person { Armed = false } };
            CollectionAssert.IsEmpty(Journey.ChooseColonists(residents, wanted: 5, keep: 3));
        }

        [Test]
        public void NewcomersGoToAHamletWithABed()
        {
            Hamlet full = Place("Full", 100f, houses: 1);
            Fill(full, 3);
            Hamlet open = Place("Open", 200f);
            Hamlet chosen = Journey.ChooseForNewcomers(new[] { full, open }, n => n - 1, Road, out JourneyKind kind);
            Assert.AreEqual(open, chosen);
            Assert.AreEqual(JourneyKind.Newcomers, kind);
        }

        [Test]
        public void WithNoHamletLiving_NewcomersResettleTheNearestEmptyOne()
        {
            Hamlet far = Place("Far", 500f);
            Hamlet near = Place("Near", 100f);
            Empty(far);
            Empty(near);
            Assert.AreEqual(near, Journey.ChooseForNewcomers(new[] { far, near }, n => 0, Road, out JourneyKind kind));
            Assert.AreEqual(JourneyKind.Colonists, kind);
        }

        [Test]
        public void IfEverywhereLivingIsFull_NobodyLands()
        {
            Hamlet full = Place("Full", 100f, houses: 1);
            Fill(full, 3);
            Hamlet empty = Place("Empty", 200f);
            Empty(empty);
            Assert.IsNull(Journey.ChooseForNewcomers(new[] { full, empty }, n => 0, Road, out _));
        }

        [Test]
        public void TravellersTellWhatTheySaw()
        {
            Hamlet first = Place("Hollowmere", 100f);
            Hamlet second = Place("Thornwick", 200f);
            Hamlet safe = Place("Marrowdale", 300f);
            Hamlet here = Place("Ashford", 400f);
            Empty(first);
            Empty(second);
            Journey journey = Party(JourneyKind.Refugees, second, 1, out _);
            journey.Witness(first);
            Assert.AreEqual("Hollowmere has fallen!", journey.NewsLine(here));
            journey.Witness(first);
            journey.Witness(safe);
            Assert.AreEqual("Hollowmere has fallen!", journey.NewsLine(here), "said once, and only what fell");
            journey.Witness(second);
            Assert.AreEqual("Hollowmere and Thornwick have fallen!", journey.NewsLine(here));
        }

        [Test]
        public void NobodyIsToldTheirOwnHamletHasFallen()
        {
            Hamlet empty = Place("Ashford", 100f);
            Empty(empty);
            Journey journey = Party(JourneyKind.Colonists, empty, 2, out _);
            journey.Witness(empty);
            Assert.AreEqual("We've come to settle Ashford.", journey.NewsLine(empty));
        }

        [Test]
        public void PeopleAddUp_ThroughLeavingAndSettling()
        {
            Hamlet from = Place("From", 0f);
            Hamlet to = Place("To", 100f, houses: 1);
            Fill(to, 2);
            List<Person> people = Fill(from, 3);
            people[0].Armed = false;
            int total = from.LivingResidents + to.LivingResidents;

            foreach (Person person in people) from.MoveOut(person);
            var journey = new Journey(JourneyKind.Refugees, from.Name, people);
            journey.SetCourse(to, AnyRoute);
            Assert.AreEqual(total, from.LivingResidents + to.LivingResidents + journey.LivingMembers);

            journey.TryMoveIn(people[0], out _, out _);
            journey.TryMoveIn(people[1], out _, out _);
            Assert.AreEqual(total, from.LivingResidents + to.LivingResidents + journey.LivingMembers);
            Assert.AreEqual(2, journey.LivingMembers);

            people[2].IsAlive = false;
            Assert.AreEqual(total - 1, from.LivingResidents + to.LivingResidents + journey.LivingMembers);
        }
    }
}
