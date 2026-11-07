using System;
using System.Collections.Generic;
using EverRealm.World;
using UnityEngine;

namespace EverRealm.Settlements
{
    /// <summary>
    /// Refugees: survivors of an abandoned hamlet. Colonists: people resettling an empty one.
    /// Newcomers: people come ashore at the landing (provisional, until births or a wider world exist).
    /// </summary>
    public enum JourneyKind { Refugees, Colonists, Newcomers }

    /// <summary>Stranded: nowhere to go for now. Explicit, so nobody just vanishes from the record.</summary>
    public enum JourneyState { Walking, Stranded, Arrived, Dead }

    /// <summary>
    /// A party on the road and the rules for where it ends up: where it's headed, what it has seen, and what
    /// it does when it gets there. Plain data; <see cref="JourneyDirector"/> handles the bodies. Travellers only
    /// learn a destination's state on arrival, so they can walk into a ruin, and nothing is reserved for them.
    /// </summary>
    public sealed class Journey
    {
        readonly List<IPerson> members;
        readonly List<string> fallen = new List<string>();

        public JourneyKind Kind { get; private set; }
        /// <summary>Where they came from, as they'd tell it.</summary>
        public string Origin { get; }
        public Hamlet Destination { get; private set; }
        public IReadOnlyList<Vector3> Route { get; private set; }
        public JourneyState State { get; private set; } = JourneyState.Walking;
        /// <summary>Where a stranded party is staying.</summary>
        public Vector3 Camp { get; private set; }
        /// <summary>How many have found a home so far.</summary>
        public int Settled { get; private set; }
        public int SetOutWith { get; }
        /// <summary>The hamlet they last told their news in, so it's only said once in each.</summary>
        public Hamlet ToldNewsIn { get; set; }

        public bool IsOver => State is JourneyState.Arrived or JourneyState.Dead;

        public Journey(JourneyKind kind, string origin, IEnumerable<IPerson> people)
        {
            Kind = kind;
            Origin = origin;
            members = new List<IPerson>(people);
            SetOutWith = members.Count;
        }

        public int LivingMembers
        {
            get
            {
                int count = 0;
                foreach (IPerson member in members)
                    if (member.IsAlive) count++;
                return count;
            }
        }

        /// <summary>Everyone still on the road, alive.</summary>
        public List<IPerson> Living() => members.FindAll(m => m.IsAlive);

        public void SetCourse(Hamlet destination, IReadOnlyList<Vector3> route)
        {
            Destination = destination;
            Route = route;
            State = JourneyState.Walking;
        }

        public void Strand(Vector3 camp)
        {
            Camp = camp;
            State = JourneyState.Stranded;
        }

        /// <summary>A stranded party with nowhere else to go takes on an empty hamlet.</summary>
        public void BecomeColonists() => Kind = JourneyKind.Colonists;

        /// <summary>
        /// <paramref name="member"/> has reached the destination: they take a bed if it will have them.
        /// False if it won't: full, under attack, fallen, or empty and they aren't colonists.
        /// </summary>
        public bool TryMoveIn(IPerson member, out House house, out int spot)
        {
            house = null;
            spot = -1;
            if (!Destination.IsViableFor(Kind) || !Destination.TryTakeBed(member, out house, out spot)) return false;
            members.Remove(member);
            Settled++;
            return true;
        }

        /// <summary>Ends the journey once nobody is left on the road: arrived if anyone found a home, otherwise dead.</summary>
        public void Review()
        {
            if (IsOver || LivingMembers > 0) return;
            State = Settled > 0 ? JourneyState.Arrived : JourneyState.Dead;
        }

        // ---- News ----

        /// <summary>Remembers a fallen hamlet they've seen with their own eyes.</summary>
        public void Witness(Hamlet hamlet)
        {
            if (hamlet.State is HamletState.Empty or HamletState.Abandoning && !fallen.Contains(hamlet.Name)) fallen.Add(hamlet.Name);
        }


        /// <summary>What they tell people in <paramref name="here"/>: what they've seen fall elsewhere, or else who they are.</summary>
        public string NewsLine(Hamlet here)
        {
            List<string> elsewhere = fallen.FindAll(name => name != here.Name);
            return elsewhere.Count switch
            {
                0 => Kind == JourneyKind.Colonists ? $"We've come to settle {here.Name}." : $"We've walked from {Origin}.",
                1 => $"{elsewhere[0]} has fallen!",
                _ => $"{string.Join(", ", elsewhere.GetRange(0, elsewhere.Count - 1))} and {elsewhere[elsewhere.Count - 1]} have fallen!",
            };
        }

        // ---- Where to next ----

        /// <summary>
        /// The nearest hamlet by road that would take a party of <paramref name="kind"/>, other than
        /// <paramref name="except"/>. Null if there is none or no road to one.
        /// </summary>
        public static Hamlet NearestViable(IEnumerable<Hamlet> hamlets, JourneyKind kind, Hamlet except, Func<Hamlet, float> roadDistance) =>
            Nearest(hamlets, h => h != except && h.IsViableFor(kind), roadDistance);

        /// <summary>The nearest other hamlet by road, whatever state it's in: where refugees head, knowing no better.</summary>
        public static Hamlet NearestOther(IEnumerable<Hamlet> hamlets, Hamlet except, Func<Hamlet, float> roadDistance) =>
            Nearest(hamlets, h => h != except, roadDistance);

        static Hamlet Nearest(IEnumerable<Hamlet> hamlets, Predicate<Hamlet> wanted, Func<Hamlet, float> roadDistance)
        {
            Hamlet best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (Hamlet hamlet in hamlets)
            {
                if (!wanted(hamlet)) continue;
                float distance = roadDistance(hamlet);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = hamlet;
            }
            return best;
        }

        /// <summary>
        /// A stranded party's choice at dawn: the nearest hamlet that would take them, or failing that the
        /// nearest empty one, to resettle. Null means another day stranded.
        /// </summary>
        public Hamlet ChooseAtDawn(IEnumerable<Hamlet> hamlets, Func<Hamlet, float> roadDistance)
        {
            Hamlet next = NearestViable(hamlets, JourneyKind.Refugees, null, roadDistance);
            if (next != null) return next;
            next = NearestViable(hamlets, JourneyKind.Colonists, null, roadDistance);
            if (next != null) BecomeColonists();
            return next;
        }

        /// <summary>
        /// Who a hamlet can spare to resettle another: up to <paramref name="wanted"/> of
        /// <paramref name="residents"/>, leaving <paramref name="keep"/> behind and never its last fighter.
        /// One fighter goes along if the hamlet has two. Empty if it can't send at least two people.
        /// </summary>
        public static List<IPerson> ChooseColonists(IReadOnlyList<IPerson> residents, int wanted, int keep)
        {
            var chosen = new List<IPerson>();
            int spare = Mathf.Min(wanted, residents.Count - keep);
            if (spare < 2) return chosen;

            var fighters = new List<IPerson>();
            var others = new List<IPerson>();
            foreach (IPerson resident in residents) (resident.IsFighter ? fighters : others).Add(resident);

            if (fighters.Count >= 2)
            {
                chosen.Add(fighters[0]);
                fighters.RemoveAt(0);
            }
            for (int i = 0; i < others.Count && chosen.Count < spare; i++) chosen.Add(others[i]);
            for (int i = 1; i < fighters.Count && chosen.Count < spare; i++) chosen.Add(fighters[i]);
            if (chosen.Count < 2) chosen.Clear();
            return chosen;
        }

        /// <summary>
        /// Where a boat's newcomers go: a hamlet with a free bed, picked by <paramref name="pick"/> (given how many
        /// there are), since they know nothing of the island. With no hamlet alive at all they go as colonists to
        /// the nearest empty one. Null if everywhere living is full: nobody lands.
        /// </summary>
        public static Hamlet ChooseForNewcomers(IReadOnlyList<Hamlet> hamlets, Func<int, int> pick, Func<Hamlet, float> roadDistance, out JourneyKind kind)
        {
            kind = JourneyKind.Newcomers;
            var viable = new List<Hamlet>();
            bool anyLiving = false;
            foreach (Hamlet hamlet in hamlets)
            {
                if (hamlet.IsViableFor(JourneyKind.Newcomers)) viable.Add(hamlet);
                if (hamlet.State != HamletState.Empty) anyLiving = true;
            }
            if (viable.Count > 0) return viable[pick(viable.Count)];
            if (anyLiving) return null;
            kind = JourneyKind.Colonists;
            return NearestViable(hamlets, JourneyKind.Colonists, null, roadDistance);
        }
    }
}
