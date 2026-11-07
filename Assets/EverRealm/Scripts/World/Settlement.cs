using System.Collections.Generic;
using UnityEngine;

namespace EverRealm.World
{
    /// <summary>A house: where its residents sleep and how to get in. Points sit on the floor, in world metres.</summary>
    public sealed class House
    {
        /// <summary>Outside, in front of the doorway.</summary>
        public Vector3 Door { get; }
        /// <summary>Inside, just past the doorway.</summary>
        public Vector3 Inside { get; }
        public Vector3 Bed { get; }
        /// <summary>Outer walls on the ground plane (x, z), in metres.</summary>
        public Rect Footprint { get; }

        public House(Vector3 door, Vector3 inside, Vector3 bed, Rect footprint)
        {
            Door = door;
            Inside = inside;
            Bed = bed;
            Footprint = footprint;
        }

        public bool Contains(Vector3 position) => Footprint.Contains(new Vector2(position.x, position.z));

        /// <summary>Where the <paramref name="resident"/>-th resident sleeps: the bed corner first, then toward the door.</summary>
        public Vector3 SleepingSpot(int resident) => Vector3.Lerp(Bed, Inside, resident % 3 * 0.35f);
    }

    /// <summary>A settled place: its houses, where its people work and where they gather.</summary>
    public sealed class Settlement
    {
        public string Name { get; }
        /// <summary>Open ground at the centre, clear of houses; every house and work spot is reachable from it.</summary>
        public Vector3 Square { get; }
        /// <summary>Radius of the flattened ground, in metres.</summary>
        public float Radius { get; }
        public IReadOnlyList<House> Houses { get; }
        public IReadOnlyList<Vector3> WorkSpots { get; }

        public Settlement(string name, Vector3 square, float radius, IReadOnlyList<House> houses, IReadOnlyList<Vector3> workSpots)
        {
            Name = name;
            Square = square;
            Radius = radius;
            Houses = houses;
            WorkSpots = workSpots;
        }

        public House HouseContaining(Vector3 position)
        {
            foreach (House house in Houses)
                if (house.Contains(position)) return house;
            return null;
        }

        /// <summary>
        /// Whether walking straight from <paramref name="a"/> to <paramref name="b"/> stays clear of every house's
        /// walls (widened by <paramref name="margin"/>). Houses containing either end are ignored: entering and
        /// leaving go through the doorway points.
        /// </summary>
        public bool IsPathClear(Vector3 a, Vector3 b, float margin)
        {
            var from = new Vector2(a.x, a.z);
            var to = new Vector2(b.x, b.z);
            foreach (House house in Houses)
            {
                if (house.Contains(a) || house.Contains(b)) continue;
                Rect r = house.Footprint;
                var widened = Rect.MinMaxRect(r.xMin - margin, r.yMin - margin, r.xMax + margin, r.yMax + margin);
                if (SegmentHitsRect(from, to, widened)) return false;
            }
            return true;
        }

        /// <summary>
        /// A waypoint round the first house blocking the way from <paramref name="a"/> to <paramref name="b"/>: the
        /// corner (stood off by <paramref name="margin"/> plus a little) that is clear from <paramref name="a"/> and
        /// makes the shortest detour. False if nothing blocks the way or no corner is usable.
        /// </summary>
        public bool TryDetour(Vector3 a, Vector3 b, float margin, out Vector3 waypoint)
        {
            waypoint = b;
            House blocking = null;
            float nearest = float.PositiveInfinity;
            var from = new Vector2(a.x, a.z);
            var to = new Vector2(b.x, b.z);
            foreach (House house in Houses)
            {
                if (house.Contains(a) || house.Contains(b)) continue;
                Rect r = house.Footprint;
                var widened = Rect.MinMaxRect(r.xMin - margin, r.yMin - margin, r.xMax + margin, r.yMax + margin);
                float distance = Vector2.Distance(from, r.center);
                if (distance < nearest && SegmentHitsRect(from, to, widened))
                {
                    nearest = distance;
                    blocking = house;
                }
            }
            if (blocking == null) return false;

            Rect f = blocking.Footprint;
            float standOff = margin + 0.6f, best = float.PositiveInfinity;
            foreach (var corner in new[]
                     {
                         new Vector2(f.xMin - standOff, f.yMin - standOff), new Vector2(f.xMax + standOff, f.yMin - standOff),
                         new Vector2(f.xMin - standOff, f.yMax + standOff), new Vector2(f.xMax + standOff, f.yMax + standOff),
                     })
            {
                var point = new Vector3(corner.x, a.y, corner.y);
                // Skip the corner we're standing on, or we'd never move on from it.
                if (Vector2.Distance(from, corner) < 0.8f || !IsPathClear(a, point, margin)) continue;
                float cost = Vector2.Distance(from, corner) + Vector2.Distance(corner, to);
                if (cost < best)
                {
                    best = cost;
                    waypoint = point;
                }
            }
            return best < float.PositiveInfinity;
        }

        // Liang–Barsky clipping: the segment hits the rect if some part of it survives clipping to all four edges.
        static bool SegmentHitsRect(Vector2 a, Vector2 b, Rect r)
        {
            float t0 = 0f, t1 = 1f;
            Vector2 d = b - a;
            return Clip(-d.x, a.x - r.xMin, ref t0, ref t1) && Clip(d.x, r.xMax - a.x, ref t0, ref t1) &&
                   Clip(-d.y, a.y - r.yMin, ref t0, ref t1) && Clip(d.y, r.yMax - a.y, ref t0, ref t1);
        }

        static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (Mathf.Approximately(p, 0f)) return q >= 0f;
            float t = q / p;
            if (p < 0f)
            {
                if (t > t1) return false;
                if (t > t0) t0 = t;
            }
            else
            {
                if (t < t0) return false;
                if (t < t1) t1 = t;
            }
            return true;
        }
    }
}
