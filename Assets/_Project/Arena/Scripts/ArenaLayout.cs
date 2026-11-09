using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// The figure-8 track's islands (the two raised spinas the track loops around), as rectangles on the
    /// ground plane. The AI has no navmesh: when an island is between a car and where it wants to go,
    /// Route() hands back the island corner to head for first, so cars drive round the loops instead of
    /// nosing into the wall. Gifts and reposition points use it to stay off the islands.
    /// </summary>
    public class ArenaLayout : MonoBehaviour
    {
        [Tooltip("Island footprints on the XZ plane: x/y = min corner (x, z), width/height = size (x, z).")]
        public Rect[] islands;
        [Tooltip("Clearance kept from island walls when testing whether a path is blocked.")]
        public float margin = 3f;
        [Tooltip("How far outside an island's corner the detour point sits.")]
        public float cornerOffset = 6f;

        /// <summary>Where to aim to get from <paramref name="from"/> toward <paramref name="to"/> without hitting an island.</summary>
        public Vector3 Route(Vector3 from, Vector3 to)
        {
            if (islands == null) return to;
            Vector2 a = new Vector2(from.x, from.z), b = new Vector2(to.x, to.z);
            Rect blocking = default;
            float nearest = float.MaxValue;
            bool found = false;
            foreach (var island in islands)
            {
                var r = Expand(island, margin);
                if (r.Contains(a)) continue; // scraping its side: local avoidance handles that
                if (!SegmentHits(a, b, r, out float tEnter)) continue;
                if (tEnter < nearest) { nearest = tEnter; blocking = island; found = true; }
            }
            if (!found) return to;

            var wall = Expand(blocking, margin);
            var c = Expand(blocking, cornerOffset);
            Vector2[] corners = { new Vector2(c.xMin, c.yMin), new Vector2(c.xMax, c.yMin), new Vector2(c.xMax, c.yMax), new Vector2(c.xMin, c.yMax) };
            Vector2 best = b;
            float bestCost = float.MaxValue;
            foreach (var corner in corners)
            {
                if (SegmentHits(a, corner, Shrink(wall, 0.2f), out _)) continue; // can't see that corner from here
                float cost = Vector2.Distance(a, corner) + Vector2.Distance(corner, b);
                if (SegmentHits(corner, b, wall, out _)) cost += Mathf.Min(c.width, c.height); // still another corner to go
                if (cost < bestCost) { bestCost = cost; best = corner; }
            }
            return new Vector3(best.x, to.y, best.y);
        }

        public bool OnIsland(Vector3 p, float extra)
        {
            if (islands == null) return false;
            var q = new Vector2(p.x, p.z);
            foreach (var island in islands) if (Expand(island, extra).Contains(q)) return true;
            return false;
        }

        /// <summary>Moves a point that lies on (or too near) an island out past its nearest edge.</summary>
        public Vector3 PushOffIslands(Vector3 p, float extra)
        {
            if (islands == null) return p;
            foreach (var island in islands)
            {
                var r = Expand(island, extra);
                if (!r.Contains(new Vector2(p.x, p.z))) continue;
                float left = p.x - r.xMin, right = r.xMax - p.x, down = p.z - r.yMin, up = r.yMax - p.z;
                float m = Mathf.Min(Mathf.Min(left, right), Mathf.Min(down, up));
                if (m == left) p.x = r.xMin;
                else if (m == right) p.x = r.xMax;
                else if (m == down) p.z = r.yMin;
                else p.z = r.yMax;
            }
            return p;
        }

        static Rect Expand(Rect r, float d) => new Rect(r.xMin - d, r.yMin - d, r.width + 2f * d, r.height + 2f * d);
        static Rect Shrink(Rect r, float d) => Expand(r, -d);

        /// <summary>Does segment a-b cross rectangle r (Liang-Barsky)? tEnter = where it enters (0..1).</summary>
        static bool SegmentHits(Vector2 a, Vector2 b, Rect r, out float tEnter)
        {
            float t0 = 0f, t1 = 1f;
            Vector2 d = b - a;
            tEnter = 0f;
            if (!Clip(-d.x, a.x - r.xMin, ref t0, ref t1)) return false;
            if (!Clip(d.x, r.xMax - a.x, ref t0, ref t1)) return false;
            if (!Clip(-d.y, a.y - r.yMin, ref t0, ref t1)) return false;
            if (!Clip(d.y, r.yMax - a.y, ref t0, ref t1)) return false;
            tEnter = t0;
            return true;
        }

        static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (Mathf.Abs(p) < 1e-6f) return q >= 0f;
            float t = q / p;
            if (p < 0f) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }

        void OnDrawGizmos()
        {
            if (islands == null) return;
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
            foreach (var r in islands)
            {
                var e = Expand(r, margin);
                Gizmos.DrawWireCube(new Vector3(e.center.x, 1f, e.center.y), new Vector3(e.width, 2f, e.height));
            }
        }
    }
}
