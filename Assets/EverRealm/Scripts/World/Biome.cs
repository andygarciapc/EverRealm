using System;
using UnityEngine;

namespace EverRealm.World
{
    public enum TreeShape { None, Round, Giant, Pine, Cactus }

    /// <summary>One kind of land: the shape of its ground, its colours and what grows on it.</summary>
    [Serializable]
    public sealed class Biome
    {
        public string name;

        [Header("Landform (metres above the shore)")]
        public float baseHeight;
        [Tooltip("How far the hills rise above the base height.")]
        public float relief;
        [Tooltip("0 = rounded hills, 1 = sharp ridges.")]
        [Range(0f, 1f)] public float ridged;
        [Tooltip("Height of the cliff bands that step the land, broken by gullies; 0 for none.")]
        public float terrace;
        [Tooltip("How far flat-topped plateaus stand above the land around them; 0 for none.")]
        public float plateauHeight;

        [Header("Colours (each drifts between its pair across the land)")]
        public Color ground;
        public Color groundAlt;
        public Color foliage;
        public Color foliageAlt;
        [Tooltip("Rock is banded in layers of these two.")]
        public Color rock;
        public Color rockAlt;

        [Header("Growth")]
        public TreeShape tree;
        [Tooltip("Chance that a tree grows in each planting cell.")]
        [Range(0f, 1f)] public float treeDensity;
        [Tooltip("How far this land's cliffs overhang, as a share of the widest. 0 for never.")]
        [Range(0f, 1f)] public float overhangs;
        [Tooltip("Chance that a cliff picked for an arch here is used.")]
        [Range(0f, 1f)] public float archChance;
        [Tooltip("Chance that a spot picked for pillars or a mesa here is used.")]
        [Range(0f, 1f)] public float formationChance;
    }

    /// <summary>What a block column looks like, blended from the biomes it sits between.</summary>
    public struct ColumnPaint
    {
        public Color32 Ground, Foliage, Rock, RockAlt;
    }
}
