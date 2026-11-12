using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// The race line: the road's closed centreline (world space, in racing order) with distance-along-track
    /// lookups. Used for lap/position tracking and by the racing AI. The figure 8 crosses itself in the
    /// middle, so nearest-point searches are windowed around where the car was last frame.
    /// </summary>
    public class RaceTrack : MonoBehaviour
    {
        [Tooltip("Closed centreline, in racing order.")]
        public Vector3[] points;
        public float halfWidth = 8f;
        [Tooltip("Start/finish line is at this centreline index.")]
        public int startIndex;
        [Tooltip("Grid slots: metres behind the line for the first row, row spacing and lane offset.")]
        public float gridFirstRow = 6f, gridRowSpacing = 7.5f, gridLaneOffset = 2.8f;

        public int Count => points != null ? points.Length : 0;
        public float Length { get; private set; }

        float[] cum; // distance from points[0] to points[i]
        float[] radius; // corner radius at each point (large on straights)

        void Awake() => Build();

        public void Build()
        {
            if (Count < 3) return;
            cum = new float[Count];
            float d = 0f;
            for (int i = 1; i < Count; i++) { d += Vector3.Distance(points[i - 1], points[i]); cum[i] = d; }
            Length = d + Vector3.Distance(points[Count - 1], points[0]);

            // Corner radius from the heading change over a short window either side of each point.
            radius = new float[Count];
            const int k = 4;
            for (int i = 0; i < Count; i++)
            {
                float arc = 0f;
                for (int j = -k; j < k; j++) arc += Vector3.Distance(points[Wrap(i + j)], points[Wrap(i + j + 1)]);
                float turn = Vector3.Angle(Tangent(i - k), Tangent(i + k)) * Mathf.Deg2Rad;
                radius[i] = turn < 1e-3f ? 10000f : arc / turn;
            }
        }

        /// <summary>Corner radius (m) of the centreline at point i.</summary>
        public float CornerRadius(int i)
        {
            if (radius == null) Build();
            return radius[Wrap(i)];
        }

        /// <summary>
        /// Fastest speed (m/s) a car at point i can carry and still make every corner within the braking horizon:
        /// each corner allows sqrt(lateralAccel * radius), and a corner d metres away allows sqrt(v^2 + 2 * brake * d) now.
        /// <paramref name="radiusOffset"/> shrinks the radius (driving off the centreline, on the inside).
        /// </summary>
        public float SafeSpeed(int i, float lateralAccel, float brakeDecel, float maxSpeed, float radiusOffset = 0f)
        {
            if (radius == null) Build();
            float horizon = maxSpeed * maxSpeed / (2f * Mathf.Max(0.5f, brakeDecel)) + 5f;
            float best = maxSpeed, d = 0f;
            int idx = Wrap(i);
            for (int n = 0; n < Count && d < horizon; n++)
            {
                float r = Mathf.Max(4f, radius[idx] - radiusOffset);
                float corner = Mathf.Sqrt(lateralAccel * r);
                best = Mathf.Min(best, Mathf.Sqrt(corner * corner + 2f * brakeDecel * d));
                int next = Wrap(idx + 1);
                d += Vector3.Distance(points[idx], points[next]);
                idx = next;
            }
            return best;
        }

        int Wrap(int i) => ((i % Count) + Count) % Count;

        /// <summary>Distance along the track from the start line to centreline point i (0..Length).</summary>
        public float FromStart(int i)
        {
            if (cum == null) Build();
            float d = cum[Wrap(i)] - cum[startIndex];
            return d < 0f ? d + Length : d;
        }

        /// <summary>Distance along the track from the start line to world point p, near index i (0..Length).</summary>
        public float FromStart(Vector3 p, int i)
        {
            i = Wrap(i);
            Vector3 a = points[i], b = points[Wrap(i + 1)];
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
            float d = FromStart(i) + ab.magnitude * t;
            return d >= Length ? d - Length : d;
        }

        /// <summary>Nearest centreline index to p, searching around a hint (or everywhere if hint &lt; 0).</summary>
        public int Nearest(Vector3 p, int hint, int back = 6, int ahead = 30)
        {
            if (Count == 0) return 0;
            int best = 0;
            float bestD = float.MaxValue;
            if (hint >= 0)
            {
                for (int k = -back; k <= ahead; k++)
                {
                    int i = Wrap(hint + k);
                    float d = Flat(points[i] - p).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (bestD < 25f * 25f) return best;
            }
            for (int i = 0; i < Count; i++)
            {
                float d = Flat(points[i] - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Index for a car on the grid: searched backwards from the start line only (the grid sits on
        /// the straight that crosses the middle of the 8, where a global search can pick the other leg).</summary>
        public int GridIndex(Vector3 p) => Nearest(p, startIndex, 45, 3);

        /// <summary>Point roughly <paramref name="metres"/> ahead of index i along the centreline.</summary>
        public Vector3 Ahead(int i, float metres, out int index)
        {
            index = Wrap(i);
            float left = metres;
            while (left > 0f)
            {
                int n = Wrap(index + 1);
                float seg = Vector3.Distance(points[index], points[n]);
                if (seg >= left) { index = n; return Vector3.Lerp(points[Wrap(n - 1)], points[n], left / Mathf.Max(seg, 1e-4f)); }
                left -= seg;
                index = n;
            }
            return points[index];
        }

        public Vector3 Tangent(int i) => Flat(points[Wrap(i + 1)] - points[Wrap(i - 1)]).normalized;

        [Header("Checkpoints")]
        [Tooltip("Where the checkpoint gates sit, as fractions of a lap after the start line (the finish line is " +
                 "checkpoint 0). Kept away from the crossing in the middle of the 8.")]
        public float[] checkpointFractions = { 0.17f, 0.33f, 0.6f, 0.75f, 0.88f };
        [Tooltip("A gate counts this far past the edge of the road on either side.")]
        public float checkpointSlack = 6f;

        /// <summary>Gates per lap, including the finish line.</summary>
        public int CheckpointCount => 1 + (checkpointFractions != null ? checkpointFractions.Length : 0);

        /// <summary>Distance from the start line to gate k (gate 0 is the finish line).</summary>
        public float CheckpointDistance(int k) => k <= 0 ? 0f : checkpointFractions[k - 1] * Length;

        /// <summary>Gate k: centre on the centreline, facing the racing direction.</summary>
        public Pose CheckpointPose(int k)
        {
            if (cum == null) Build();
            Vector3 p = Ahead(startIndex, CheckpointDistance(k), out int idx);
            return new Pose(p, Quaternion.LookRotation(Tangent(idx), Vector3.up));
        }

        /// <summary>Grid slot n (0 = pole) behind the start line, facing the racing direction.</summary>
        public Pose GridSlot(int n)
        {
            int row = n / 2;
            float side = n % 2 == 0 ? -1f : 1f;
            float back = gridFirstRow + row * gridRowSpacing;
            // Walk backwards from the start line.
            int index = startIndex;
            float left = back;
            Vector3 pos = points[index];
            while (left > 0f)
            {
                int prev = Wrap(index - 1);
                float seg = Vector3.Distance(points[prev], points[index]);
                if (seg >= left) { pos = Vector3.Lerp(points[index], points[prev], left / Mathf.Max(seg, 1e-4f)); index = prev; break; }
                left -= seg;
                index = prev;
                pos = points[index];
            }
            Vector3 fwd = Tangent(index);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            return new Pose(pos + right * (side * gridLaneOffset) + Vector3.up * 0.9f, Quaternion.LookRotation(fwd, Vector3.up));
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void OnDrawGizmos()
        {
            if (Count < 2) return;
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
            for (int i = 0; i < Count; i++) Gizmos.DrawLine(points[i] + Vector3.up * 0.3f, points[Wrap(i + 1)] + Vector3.up * 0.3f);
            Gizmos.color = Color.white;
            Gizmos.DrawWireCube(points[Mathf.Clamp(startIndex, 0, Count - 1)] + Vector3.up, new Vector3(2f * halfWidth, 2f, 0.4f));
        }
    }
}
