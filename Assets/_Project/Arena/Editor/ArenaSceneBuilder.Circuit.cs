using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarArena.EditorTools
{
    /// <summary>
    /// The second race venue: a full road circuit east of the arena, traced from the user's reference layout
    /// (17 corners: long first sector, the 4-5 and 8 hairpins, the 13-16 esses and loop, back to the line).
    /// The second half of the ladder races here (CampaignDirector.TrackFor).
    /// </summary>
    public static partial class ArenaSceneBuilder
    {
        static readonly Vector3 CircuitOrigin = new Vector3(420f, 0f, 0f);
        const float CircuitScale = 0.4f;           // metres per pixel of the reference image
        const float CircuitHalfWidth = 8f;
        const int CircuitStartIndex = 10;          // 20 m along the start/finish straight
        static readonly Vector2 CircuitImageCentre = new Vector2(360f, 425f);

        // Centreline control points in reference-image pixels (x right, y down), in racing order from the line.
        static readonly Vector2[] CircuitTrace =
        {
            new Vector2(268, 57), new Vector2(360, 52), new Vector2(450, 48), new Vector2(540, 46), new Vector2(588, 52),
            new Vector2(617, 68), new Vector2(635, 95), new Vector2(648, 140), new Vector2(654, 200), new Vector2(655, 280),
            new Vector2(656, 360), new Vector2(660, 440), new Vector2(666, 500), new Vector2(673, 545), new Vector2(670, 580),
            new Vector2(663, 620), new Vector2(667, 665), new Vector2(680, 715), new Vector2(692, 760), new Vector2(686, 800),
            new Vector2(662, 815), new Vector2(638, 800), new Vector2(615, 775), new Vector2(585, 752), new Vector2(555, 730),
            new Vector2(530, 705), new Vector2(512, 672), new Vector2(498, 640), new Vector2(484, 622), new Vector2(468, 622),
            new Vector2(445, 635), new Vector2(410, 652), new Vector2(370, 675), new Vector2(330, 700), new Vector2(300, 718),
            new Vector2(260, 732), new Vector2(200, 738), new Vector2(150, 735), new Vector2(122, 722), new Vector2(105, 700),
            new Vector2(96, 670), new Vector2(84, 630), new Vector2(74, 585), new Vector2(72, 530), new Vector2(74, 475),
            new Vector2(84, 440), new Vector2(110, 422), new Vector2(145, 402), new Vector2(180, 378), new Vector2(205, 350),
            new Vector2(219, 315), new Vector2(218, 275), new Vector2(204, 240), new Vector2(180, 212), new Vector2(152, 203),
            new Vector2(127, 212), new Vector2(112, 240), new Vector2(101, 275), new Vector2(82, 302), new Vector2(55, 312),
            new Vector2(32, 298), new Vector2(22, 265), new Vector2(30, 228), new Vector2(48, 190), new Vector2(70, 145),
            new Vector2(98, 100), new Vector2(130, 75), new Vector2(180, 63), new Vector2(225, 59),
        };

        static RaceTrack figure8Track, circuitTrack;

        static RaceTrack BuildCircuit(SurfaceProfile asphalt, SurfaceProfile sand)
        {
            var root = new GameObject("Circuit").transform;
            var pts = CircuitCentreline();

            Vector3 min = pts[0], max = pts[0];
            foreach (var p in pts) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            const float margin = 42f;
            Vector3 centre = (min + max) * 0.5f;
            Vector3 size = max - min + new Vector3(margin * 2f, 0f, margin * 2f);

            // Sand run-off everywhere off the road (same loose surface as the arena).
            Tag(Prim(PrimitiveType.Cube, "Ground", root, new Vector3(centre.x, -0.1f, centre.z), new Vector3(size.x, 0.2f, size.z), mSand, true), sand);

            var road = MeshObj("Road", root, Vector3.zero, SaveMesh(RibbonMesh(pts, CircuitHalfWidth, 0.02f, "CircuitRoad"), "CircuitRoad"), mAsphalt);
            road.AddComponent<MeshCollider>();
            road.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Tag(road, asphalt);

            var track = root.gameObject.AddComponent<RaceTrack>();
            track.displayName = "Grand Circuit";
            track.laps = 2;
            track.points = pts.ToArray();
            track.halfWidth = CircuitHalfWidth;
            track.startIndex = CircuitStartIndex;
            track.checkpointFractions = new[] { 0.14f, 0.3f, 0.45f, 0.6f, 0.74f, 0.87f };
            track.Build();
            StartLine(root, pts[CircuitStartIndex], (pts[CircuitStartIndex + 1] - pts[CircuitStartIndex - 1]).normalized);

            var kerbRed = Mat("KerbRed", new Color(0.78f, 0.12f, 0.1f), 0.2f);
            var kerbWhite = Mat("KerbWhite", new Color(0.92f, 0.9f, 0.86f), 0.2f);
            CircuitKerbs(root, track, kerbRed, kerbWhite);
            CircuitBarriers(root, track);
            CircuitPerimeter(root, centre, size);
            CircuitGrandstand(root, track);
            CircuitTyres(root, track);

            circuitTrack = track;
            return track;
        }

        /// <summary>Smooth closed centreline (centripetal Catmull-Rom through the trace), resampled every 2 m, world space.</summary>
        static List<Vector3> CircuitCentreline()
        {
            int n = CircuitTrace.Length;
            var w = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 px = CircuitTrace[i] - CircuitImageCentre;
                w[i] = new Vector2(px.x, -px.y) * CircuitScale + new Vector2(CircuitOrigin.x, CircuitOrigin.z);
            }

            var dense = new List<Vector2>();
            const int sub = 20;
            for (int i = 0; i < n; i++)
                for (int k = 0; k < sub; k++)
                    dense.Add(CatmullRom(w[(i - 1 + n) % n], w[i], w[(i + 1) % n], w[(i + 2) % n], k / (float)sub));

            var cum = new float[dense.Count + 1];
            for (int i = 1; i <= dense.Count; i++) cum[i] = cum[i - 1] + Vector2.Distance(dense[i - 1], dense[i % dense.Count]);
            float length = cum[dense.Count];
            int count = Mathf.RoundToInt(length / 2f);
            var pts = new List<Vector3>(count);
            int j = 0;
            for (int k = 0; k < count; k++)
            {
                float d = k * length / count;
                while (cum[j + 1] < d) j++;
                Vector2 a = dense[j], b = dense[(j + 1) % dense.Count];
                float t = (d - cum[j]) / Mathf.Max(1e-4f, cum[j + 1] - cum[j]);
                Vector2 q = Vector2.Lerp(a, b, t);
                pts.Add(new Vector3(q.x, 0f, q.y));
            }
            return pts;
        }

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float T(float ti, Vector2 a, Vector2 b) => ti + Mathf.Sqrt(Vector2.Distance(a, b)) + 1e-4f;
            float t0 = 0f, t1 = T(t0, p0, p1), t2 = T(t1, p1, p2), t3 = T(t2, p2, p3);
            float tt = Mathf.Lerp(t1, t2, t);
            Vector2 L(Vector2 a, Vector2 b, float ta, float tb) => ((tb - tt) * a + (tt - ta) * b) / (tb - ta);
            Vector2 a1 = L(p0, p1, t0, t1), a2 = L(p1, p2, t1, t2), a3 = L(p2, p3, t2, t3);
            Vector2 b1 = L(a1, a2, t0, t2), b2 = L(a2, a3, t1, t3);
            return L(b1, b2, t1, t2);
        }

        /// <summary>+1 when the corner at i turns right (outside is on the left), -1 when it turns left.</summary>
        static float TurnSign(RaceTrack track, int i)
        {
            float a = Vector3.SignedAngle(track.Tangent(i - 4), track.Tangent(i + 4), Vector3.up);
            return a >= 0f ? 1f : -1f;
        }

        static Vector3 LeftOf(Vector3 tangent) => new Vector3(-tangent.z, 0f, tangent.x);

        /// <summary>Red/white kerbs on both edges through every corner tighter than 45 m (one mesh, two materials).</summary>
        static void CircuitKerbs(Transform root, RaceTrack track, Material red, Material white)
        {
            var verts = new List<Vector3>();
            var tris = new[] { new List<int>(), new List<int>() };
            int n = track.Count;
            for (int i = 0; i < n; i++)
            {
                if (Mathf.Min(track.CornerRadius(i), track.CornerRadius(i + 1)) > 45f) continue;
                Vector3 a = track.points[i], b = track.points[(i + 1) % n];
                Vector3 la = LeftOf(track.Tangent(i)), lb = LeftOf(track.Tangent(i + 1));
                foreach (float side in new[] { -1f, 1f })
                {
                    float inner = CircuitHalfWidth - 0.4f, outer = CircuitHalfWidth + 0.8f;
                    int v = verts.Count;
                    verts.Add(a + la * side * inner + Vector3.up * 0.035f);
                    verts.Add(a + la * side * outer + Vector3.up * 0.035f);
                    verts.Add(b + lb * side * inner + Vector3.up * 0.035f);
                    verts.Add(b + lb * side * outer + Vector3.up * 0.035f);
                    var list = tris[i % 2];
                    if (side < 0) list.AddRange(new[] { v, v + 2, v + 1, v + 1, v + 2, v + 3 });
                    else list.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });
                }
            }
            var mesh = new Mesh { name = "CircuitKerbs", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(tris[0], 0);
            mesh.SetTriangles(tris[1], 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = MeshObj("Kerbs", root, Vector3.zero, SaveMesh(mesh, "CircuitKerbs"), red);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { red, white };
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>
        /// Low concrete walls on the outside of the corners, 10 m past the road edge, so a missed braking point
        /// ends in the sand and then the wall. Skipped wherever they would come near another part of the track.
        /// </summary>
        static void CircuitBarriers(Transform root, RaceTrack track)
        {
            int n = track.Count;
            const float offset = CircuitHalfWidth + 10f, height = 1.1f, thick = 0.7f;
            var wallPts = new Vector3?[n];
            var sides = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (track.CornerRadius(i) > 80f) continue;
                float s = TurnSign(track, i);
                Vector3 p = track.points[i] + LeftOf(track.Tangent(i)) * (s * offset);
                bool clear = true;
                for (int j = 0; j < n && clear; j++)
                {
                    int along = Mathf.Abs(i - j); along = Mathf.Min(along, n - along);
                    if (along < 12) continue;
                    if ((track.points[j] - p).sqrMagnitude < (CircuitHalfWidth + 6f) * (CircuitHalfWidth + 6f)) clear = false;
                }
                if (!clear) continue;
                wallPts[i] = p;
                sides[i] = s;
            }

            var verts = new List<Vector3>();
            var tris = new List<int>();
            // One-sided quad wound so it faces <paramref name="facing"/> (the wall is a closed strip: outer, inner, top).
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
            {
                int v = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) >= 0f) tris.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
                else tris.AddRange(new[] { v, v + 2, v + 1, v, v + 3, v + 2 });
            }
            for (int i = 0; i < n; i++)
            {
                int k = (i + 1) % n;
                if (!wallPts[i].HasValue || !wallPts[k].HasValue || sides[i] != sides[k]) continue;
                Vector3 a = wallPts[i].Value, b = wallPts[k].Value;
                Vector3 nrm = LeftOf((b - a).normalized) * (thick * 0.5f);
                Vector3 up = Vector3.up * height;
                Quad(a + nrm, b + nrm, b + nrm + up, a + nrm + up, nrm);
                Quad(a - nrm, a - nrm + up, b - nrm + up, b - nrm, -nrm);
                Quad(a + nrm + up, b + nrm + up, b - nrm + up, a - nrm + up, Vector3.up);
            }
            var mesh = new Mesh { name = "CircuitBarriers", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = MeshObj("Barriers", root, Vector3.zero, SaveMesh(mesh, "CircuitBarriers"), mConcrete);
            go.AddComponent<MeshCollider>();
        }

        static void CircuitPerimeter(Transform root, Vector3 centre, Vector3 size)
        {
            var wall = Empty("PerimeterWall", root, Vector3.zero).transform;
            const float h = 3.5f;
            float hx = size.x * 0.5f, hz = size.z * 0.5f;
            Prim(PrimitiveType.Cube, "WallN", wall, new Vector3(centre.x, h * 0.5f, centre.z + hz), new Vector3(size.x + 2f, h, 1.5f), mStone2, true);
            Prim(PrimitiveType.Cube, "WallS", wall, new Vector3(centre.x, h * 0.5f, centre.z - hz), new Vector3(size.x + 2f, h, 1.5f), mStone2, true);
            Prim(PrimitiveType.Cube, "WallE", wall, new Vector3(centre.x + hx, h * 0.5f, centre.z), new Vector3(1.5f, h, size.z), mStone2, true);
            Prim(PrimitiveType.Cube, "WallW", wall, new Vector3(centre.x - hx, h * 0.5f, centre.z), new Vector3(1.5f, h, size.z), mStone2, true);
        }

        /// <summary>Stepped grandstand with a roof and a static crowd along the outside of the start/finish straight.</summary>
        static void CircuitGrandstand(Transform root, RaceTrack track)
        {
            int a = track.startIndex - 8, b = track.startIndex + 40;
            Vector3 pa = track.points[(a + track.Count) % track.Count], pb = track.points[b % track.Count];
            Vector3 along = (pb - pa).normalized;
            Vector3 mid = (pa + pb) * 0.5f;
            // Outside of the straight = away from the rest of the circuit (its centre is the ground centre).
            Vector3 left = LeftOf(along);
            Vector3 toCentre = new Vector3(CircuitOrigin.x, 0f, CircuitOrigin.z) - mid;
            Vector3 outward = Vector3.Dot(left, toCentre) > 0f ? -left : left;
            float length = Vector3.Distance(pa, pb);
            var stand = Empty("Grandstand", root, mid + outward * (CircuitHalfWidth + 14f), Quaternion.LookRotation(-outward)).transform;

            const int rows = 6;
            const float rowDepth = 1.6f, rise = 0.8f;
            for (int r = 0; r < rows; r++)
                Prim(PrimitiveType.Cube, "Step" + r, stand, new Vector3(0f, rise * (r + 1) * 0.5f, -r * rowDepth), new Vector3(length, rise * (r + 1), rowDepth), mStone, true);
            Prim(PrimitiveType.Cube, "BackWall", stand, new Vector3(0f, 4.2f, -rows * rowDepth + 0.5f), new Vector3(length, 8.4f, 0.5f), mStone2, true);
            for (int p = 0; p <= 6; p++)
                Prim(PrimitiveType.Cube, "Pillar", stand, new Vector3(-length * 0.5f + p * length / 6f, 4.2f, 1.1f), new Vector3(0.5f, 8.4f, 0.5f), mStone, true);
            Prim(PrimitiveType.Cube, "Roof", stand, new Vector3(0f, 8.5f, -rows * rowDepth * 0.5f + 0.9f), new Vector3(length + 1f, 0.3f, rows * rowDepth + 1.4f), mClothRed);

            // Spectators: coloured blocks on every other step (static; the arena crowd does the cheering).
            var rng = new System.Random(4242);
            for (int r = 1; r < rows; r += 1)
                for (float x = -length * 0.5f + 1f; x < length * 0.5f - 1f; x += 2.4f)
                {
                    if (rng.NextDouble() < 0.3) continue;
                    var m = mCrowd[rng.Next(mCrowd.Length)];
                    var person = Prim(PrimitiveType.Cube, "Fan", stand, new Vector3(x + (float)rng.NextDouble() * 0.8f, rise * (r + 1) + 0.45f, -r * rowDepth),
                        new Vector3(0.5f, 0.9f, 0.4f), m);
                    person.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
            // Laurel banners hanging off the roof edge.
            var banner = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/BannerLaurel.mat") ?? mClothRed; // made by the garage build
            for (int p = 0; p < 6; p++)
                Prim(PrimitiveType.Cube, "Banner", stand, new Vector3(-length * 0.5f + (p + 0.5f) * length / 6f, 7.4f, 1.45f), new Vector3(3f, 1.8f, 0.05f), banner);
        }

        /// <summary>Loose tyre stacks on the outside of the three hairpins.</summary>
        static void CircuitTyres(Transform root, RaceTrack track)
        {
            var props = Empty("Props", root, Vector3.zero).transform;
            var tight = new List<int>();
            for (int i = 0; i < track.Count; i++)
            {
                if (track.CornerRadius(i) > 16f) continue;
                bool localMin = track.CornerRadius(i) <= track.CornerRadius(i - 1) && track.CornerRadius(i) <= track.CornerRadius(i + 1);
                if (!localMin) continue;
                if (tight.Exists(t => Mathf.Min(Mathf.Abs(t - i), track.Count - Mathf.Abs(t - i)) < 20)) continue;
                tight.Add(i);
            }
            int stack = 0;
            foreach (int i in tight)
            {
                float s = TurnSign(track, i);
                for (int k = -1; k <= 1; k++)
                {
                    int j = i + k * 3;
                    Vector3 p = track.points[(j + track.Count) % track.Count] + LeftOf(track.Tangent(j)) * (s * (CircuitHalfWidth + 5f));
                    TyreStack(props, p, 100 + stack++);
                }
            }
        }
    }
}
