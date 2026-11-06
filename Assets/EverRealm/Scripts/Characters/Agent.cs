using EverRealm.Combat;
using UnityEngine;

namespace EverRealm.Characters
{
    /// <summary>
    /// Identity of a world agent: which faction it belongs to and whether it's alive. Registers itself in
    /// <see cref="AgentDirectory"/> so others can perceive it. Says nothing about who controls it.
    /// </summary>
    public sealed class Agent : MonoBehaviour
    {
        [Tooltip("Agents of different factions are hostile to each other.")]
        public int faction;

        public Combatant Combatant { get; private set; }
        public bool IsAlive => Combatant == null || !Combatant.IsDead;

        void Awake() => Combatant = GetComponent<Combatant>();
        void OnEnable() => AgentDirectory.Register(this);
        void OnDisable() => AgentDirectory.Unregister(this);

        public bool IsHostileTo(Agent other) => other != null && other.faction != faction;
        /// <summary>Members of the same faction. Not simply "not hostile": neutral factions will exist.</summary>
        public bool IsAlliedWith(Agent other) => other != null && other.faction == faction;
    }
}
