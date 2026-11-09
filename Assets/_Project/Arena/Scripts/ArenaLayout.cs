using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// The figure-8 track's round infield islands, as circles on the ground plane. The AI has no navmesh:
    /// when an island is between a car and where it wants to go, Route() hands back a point to steer for
    /// that curves round it (a tangent to the island, or the next point along its edge when already
    /// circling), so cars follow the loops instead of nosing into the wall. Gifts and reposition points
    /// use it to stay off the islands.
    /// </summary>
    public class ArenaLayout : MonoBehaviour
    {
        [Tooltip("Island centres on the XZ plane (x, z).")]
        public Vector2[] islandCentres;
        [Tooltip("Island radii, one per centre.")]
        public float[] islandRadii;
        [Tooltip("Clearance kept from island walls when testing whether a path is blocked.")]
        public float margin = 3f;
        [Tooltip("Radius beyond the island edge that detours aim for.")]
        public float detourOffset = 6f;
        [Tooltip("When already circling an island, aim this many degrees further round it.")]
        public float orbitLead = 40f;

        int Count => islandCentres == null || islandRadii == null ? 0 : Mathf.Min(islandCentres.Length, islandRadii.Length);

        /// <summary>Where to aim to get from <paramref name="from"/> toward <paramref name="to"/> without hitting an island.</summary>
        public Vector3 Route(Vector3 from, Vector3 to)
        {
            Vector2 a = new Vector2(from.x, from.z), b = new Vector2(to.x, to.z);
            int blocking = -1;
            float nearest = float.MaxValue;
            for (int i = 0; i < Count; i++)
            {
                float r = islandRadii[i] + margin;
                Vector2 c = islandCentres[i];
                if ((a - c).sqrMagnitude < r * r) continue; // scraping its edge: local avoidance handles that
                if (!SegmentHitsCircle(a, b, c, r, out float t)) continue;
                if (t < nearest) { nearest = t; blocking = i; }
            }
            if (blocking < 0) return to;

            Vector2 centre = islandCentres[blocking];
            float rd = islandRadii[blocking] + detourOffset;
            Vector2 rel = a - centre;
            float d = rel.magnitude;
            Vector2 u = rel / Mathf.Max(d, 0.01f);
            Vector2 toB = b - centre;
            float side = Mathf.Sign(u.x * toB.y - u.y * toB.x); // +1: goal is counter-clockwise from us
            if (side == 0f) side = 1f;

            Vector2 aim;
            if (d < rd * 1.2f)
            {
                // Already alongside: keep circling toward the goal.
                aim = centre + Rotate(u, side * orbitLead * Mathf.Deg2Rad) * rd;
            }
            else
            {
                // Tangent to the detour circle on the goal's side.
                float theta = Mathf.Acos(Mathf.Clamp(rd / d, -1f, 1f));
                aim = centre + Rotate(u, side * theta) * rd;
            }
            return new Vector3(aim.x, to.y, aim.y);
        }

        public bool OnIsland(Vector3 p, float extra)
        {
            var q = new Vector2(p.x, p.z);
            for (int i = 0; i < Count; i++)
                if ((q - islandCentres[i]).magnitude < islandRadii[i] + extra) return true;
            return false;
        }

        /// <summary>Moves a point that lies on (or too near) an island out past its edge.</summary>
        public Vector3 PushOffIslands(Vector3 p, float extra)
        {
            for (int i = 0; i < Count; i++)
            {
                var q = new Vector2(p.x, p.z);
                Vector2 rel = q - islandCentres[i];
                float r = islandRadii[i] + extra;
                if (rel.magnitude >= r) continue;
                Vector2 dir = rel.sqrMagnitude > 0.01f ? rel.normalized : Vector2.right;
                Vector2 o = islandCentres[i] + dir * r;
                p.x = o.x;
                p.z = o.y;
            }
            return p;
        }

        static Vector2 Rotate(Vector2 v, float rad)
        {
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>Does segment a-b pass within r of c? t = parameter (0..1) of the closest approach.</summary>
        static bool SegmentHitsCircle(Vector2 a, Vector2 b, Vector2 c, float r, out float t)
        {
            Vector2 d = b - a;
            float len2 = d.sqrMagnitude;
            t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(c - a, d) / len2) : 0f;
            return (a + d * t - c).sqrMagnitude < r * r;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
            for (int i = 0; i < Count; i++)
            {
                Vector3 c = new Vector3(islandCentres[i].x, 1f, islandCentres[i].y);
                float r = islandRadii[i] + margin;
                Vector3 prev = c + new Vector3(r, 0f, 0f);
                for (int k = 1; k <= 48; k++)
                {
                    float ang = k / 48f * Mathf.PI * 2f;
                    Vector3 p = c + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                    Gizmos.DrawLine(prev, p);
                    prev = p;
                }
            }
        }
    }
}
