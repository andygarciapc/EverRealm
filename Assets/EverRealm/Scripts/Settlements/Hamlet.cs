using System;
using System.Collections.Generic;
using EverRealm.World;
using UnityEngine;

namespace EverRealm.Settlements
{
    /// <summary>
    /// Living: going about its days. UnderAttack: a war party has charged and the attack isn't over.
    /// Abandoning: an attack left no fighters, and the survivors leave at dawn unless one turns up.
    /// Empty: nobody lives there until someone moves in.
    /// </summary>
    public enum HamletState { Living, UnderAttack, Abandoning, Empty }

    /// <summary>Someone who can live in a hamlet or walk a road, as the hamlet and journey rules see them.</summary>
    public interface IPerson
    {
        bool IsAlive { get; }
        /// <summary>Living, armed and unbroken: someone who would stand and defend a place.</summary>
        bool IsFighter { get; }
        Vector3 Position { get; }
    }

    /// <summary>
    /// One hamlet's people and fortunes: who has which bed, and the rules for when it's abandoned. Plain data,
    /// so it plays out the same whoever is watching; bodies and clocks are handled by <see cref="HamletPopulation"/>.
    /// </summary>
    public sealed class Hamlet
    {
        /// <summary>Sleeping spots per house (see <see cref="House.SleepingSpot"/>).</summary>
        public const int BedsPerHouse = 3;

        readonly struct Lodging
        {
            public readonly IPerson Person;
            public readonly House House;
            public readonly int Spot;

            public Lodging(IPerson person, House house, int spot)
            {
                Person = person;
                House = house;
                Spot = spot;
            }
        }

        readonly List<Lodging> lodgings = new List<Lodging>();

        public Settlement Settlement { get; }
        public string Name => Settlement.Name;
        public HamletState State { get; private set; } = HamletState.Living;
        /// <summary>When an empty hamlet is due to be resettled, in world hours.</summary>
        public double ColonistsDueAt { get; set; }

        /// <summary>Raised on every change of state, with the state it left.</summary>
        public event Action<Hamlet, HamletState> StateChanged;

        public Hamlet(Settlement settlement) => Settlement = settlement;

        public int Capacity => Settlement.Houses.Count * BedsPerHouse;
        public int FreeBeds => Capacity - LivingResidents;

        public int LivingResidents
        {
            get
            {
                int count = 0;
                foreach (Lodging lodging in lodgings)
                    if (lodging.Person.IsAlive) count++;
                return count;
            }
        }

        public int LivingFighters
        {
            get
            {
                int count = 0;
                foreach (Lodging lodging in lodgings)
                    if (lodging.Person.IsFighter) count++;
                return count;
            }
        }

        /// <summary>Everyone with a home here who is still alive.</summary>
        public List<IPerson> Residents()
        {
            var residents = new List<IPerson>();
            foreach (Lodging lodging in lodgings)
                if (lodging.Person.IsAlive) residents.Add(lodging.Person);
            return residents;
        }

        // ---- Beds ----

        /// <summary>Gives <paramref name="person"/> the lowest free spot in the least crowded house, if there is one.</summary>
        public bool TryTakeBed(IPerson person, out House house, out int spot)
        {
            house = LeastCrowded();
            spot = -1;
            if (house == null) return false;
            for (int candidate = 0; candidate < BedsPerHouse; candidate++)
            {
                if (IsTaken(house, candidate)) continue;
                spot = candidate;
                MoveIn(person, house, spot);
                return true;
            }
            return false;
        }

        /// <summary>Takes <paramref name="person"/> in even when full: they share the least crowded house's first spot.</summary>
        public bool TryTakeShelter(IPerson person, out House house, out int spot)
        {
            if (TryTakeBed(person, out house, out spot)) return true;
            if (house == null) return false;
            spot = 0;
            MoveIn(person, house, spot);
            return true;
        }

        public void MoveOut(IPerson person) => lodgings.RemoveAll(l => l.Person == person);

        void MoveIn(IPerson person, House house, int spot)
        {
            MoveOut(person);
            lodgings.Add(new Lodging(person, house, spot));
            if (State == HamletState.Empty) SetState(HamletState.Living);
        }

        bool IsTaken(House house, int spot)
        {
            foreach (Lodging lodging in lodgings)
                if (lodging.Person.IsAlive && lodging.House == house && lodging.Spot == spot) return true;
            return false;
        }

        House LeastCrowded()
        {
            House best = null;
            int fewest = int.MaxValue;
            foreach (House house in Settlement.Houses)
            {
                int living = 0;
                foreach (Lodging lodging in lodgings)
                    if (lodging.Person.IsAlive && lodging.House == house) living++;
                if (living >= fewest) continue;
                fewest = living;
                best = house;
            }
            return best;
        }

        // ---- Who's here ----

        /// <summary>Would this hamlet take travellers of <paramref name="kind"/>? It needs a free bed and to be at peace; only colonists take on an empty one.</summary>
        public bool IsViableFor(JourneyKind kind) =>
            FreeBeds > 0 && (State == HamletState.Living || (State == HamletState.Empty && kind == JourneyKind.Colonists));

        /// <summary>
        /// How many of <paramref name="people"/> would defend this hamlet right now: fighters standing within
        /// <paramref name="radius"/> of the square, whether they live here or not.
        /// </summary>
        public int CountFightersPresent(IEnumerable<IPerson> people, float radius)
        {
            int count = 0;
            foreach (IPerson person in people)
                if (person.IsFighter && IsWithin(person, radius)) count++;
            return count;
        }

        bool IsWithin(IPerson person, float radius)
        {
            Vector3 offset = person.Position - Settlement.Square;
            offset.y = 0f;
            return offset.sqrMagnitude <= radius * radius;
        }

        // ---- Fortunes ----

        /// <summary>A war party has charged. Only a living hamlet can be attacked.</summary>
        public void BeginAttack()
        {
            if (State == HamletState.Living) SetState(HamletState.UnderAttack);
        }

        /// <summary>
        /// The attack is over: the one moment the hamlet asks whether anyone is left to hold it. Having no
        /// fighters at any other time decides nothing.
        /// </summary>
        public void EndAttack(int fightersPresent)
        {
            if (State != HamletState.UnderAttack) return;
            if (LivingResidents == 0) SetState(HamletState.Empty);
            else SetState(fightersPresent > 0 ? HamletState.Living : HamletState.Abandoning);
        }

        /// <summary>
        /// The dawn after an attack left no fighters. A fighter present by now holds the hamlet; otherwise
        /// it's empty and this returns true: the residents must go.
        /// </summary>
        public bool ReviewAtDawn(int fightersPresent)
        {
            if (State != HamletState.Abandoning) return false;
            if (fightersPresent > 0)
            {
                SetState(HamletState.Living);
                return false;
            }
            SetState(HamletState.Empty);
            return true;
        }

        /// <summary>
        /// Keeps the state true to who is left: a hamlet whose last resident has died is empty, and an empty
        /// one with a resident back in it (someone who stayed away when the others left) lives again.
        /// </summary>
        public void Review(float radius)
        {
            switch (State)
            {
                case HamletState.Living:
                case HamletState.Abandoning:
                    if (LivingResidents == 0) SetState(HamletState.Empty);
                    break;
                case HamletState.Empty:
                    foreach (Lodging lodging in lodgings)
                    {
                        if (!lodging.Person.IsAlive || !IsWithin(lodging.Person, radius)) continue;
                        SetState(HamletState.Living);
                        break;
                    }
                    break;
            }
        }

        void SetState(HamletState state)
        {
            HamletState previous = State;
            State = state;
            StateChanged?.Invoke(this, previous);
        }
    }
}
