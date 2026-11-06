using System;
using System.Collections.Generic;
using UnityEngine;

namespace EverRealm.Characters
{
    /// <summary>
    /// Lookup of agents currently in the world, for AI perception and debug tools. A plain list for now;
    /// swap in spatial partitioning when agent counts require it.
    /// </summary>
    public static class AgentDirectory
    {
        static readonly List<Agent> Agents = new List<Agent>();

        public static IReadOnlyList<Agent> All => Agents;

        internal static void Register(Agent agent)
        {
            if (!Agents.Contains(agent)) Agents.Add(agent);
        }

        internal static void Unregister(Agent agent) => Agents.Remove(agent);

        /// <summary>Nearest agent within <paramref name="radius"/> (flat distance) that passes <paramref name="filter"/>.</summary>
        public static Agent FindNearest(Vector3 position, float radius, Predicate<Agent> filter)
        {
            Agent best = null;
            float bestSqr = radius * radius;
            foreach (Agent agent in Agents)
            {
                if (!filter(agent)) continue;
                Vector3 offset = agent.transform.position - position;
                offset.y = 0f;
                float sqr = offset.sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = agent;
                }
            }
            return best;
        }

        // Survive "enter Play Mode without domain reload".
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => Agents.Clear();
    }
}
