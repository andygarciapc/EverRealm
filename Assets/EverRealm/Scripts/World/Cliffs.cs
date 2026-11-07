using System.Collections.Generic;
using UnityEngine;

namespace EverRealm.World
{
    /// <summary>A point on the top edge of a cliff.</summary>
    public struct CliffRim
    {
        public int X, Z, Top;
        /// <summary>How many blocks the ground falls away just beyond the edge.</summary>
        public int Drop;
        /// <summary>Horizontal direction out over the drop.</summary>
        public Vector2 Out;
    }

    /// <summary>Finds cliff edges, which overhangs and arches grow from.</summary>
    public static class Cliffs
    {
        const int LookOut = 6, LookBack = 4, Stride = 2;

        static readonly Vector2Int[] Directions =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
        };

        /// <summary>Edges with level ground behind them and a drop of at least <see cref="WorldSettings.minCliffDrop"/> in front.</summary>
        public static List<CliffRim> FindRims(WorldData world, WorldSettings settings, int[] tops)
        {
            var rims = new List<CliffRim>();
            int size = world.SizeInBlocks, margin = LookOut + 2;
            int minDrop = Mathf.CeilToInt(settings.minCliffDrop / world.BlockSize);
            float shore = settings.seaLevel + settings.beachHeight;

            for (int z = margin; z < size - margin; z += Stride)
            for (int x = margin; x < size - margin; x += Stride)
            {
                int top = tops[x + z * size], deepest = 0;
                if ((top + 1) * world.BlockSize <= shore) continue;

                Vector2 outward = Vector2.zero;
                foreach (Vector2Int direction in Directions)
                {
                    int drop = top - tops[x + direction.x * LookOut + (z + direction.y * LookOut) * size];
                    if (drop <= 0) continue;
                    outward += (Vector2)direction * drop;
                    deepest = Mathf.Max(deepest, drop);
                }
                if (deepest < minDrop || outward == Vector2.zero) continue;

                outward.Normalize();
                int behind = tops[x - Mathf.RoundToInt(outward.x * LookBack) + (z - Mathf.RoundToInt(outward.y * LookBack)) * size];
                if (Mathf.Abs(behind - top) > 2) continue;
                rims.Add(new CliffRim { X = x, Z = z, Top = top, Drop = deepest, Out = outward });
            }
            return rims;
        }
    }
}
