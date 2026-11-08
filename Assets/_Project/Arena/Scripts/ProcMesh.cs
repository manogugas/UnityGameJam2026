using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarArena
{
    /// <summary>Procedural meshes for the greybox car and arena.</summary>
    public static class ProcMesh
    {
        /// <summary>Box with each face subdivided (so it can be dented). Real size, centred on center.</summary>
        public static Mesh Box(Vector3 size, Vector3Int segments, Vector3 center = default, string name = "Box")
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            Vector3 h = size * 0.5f;
            segments = Vector3Int.Max(segments, Vector3Int.one);

            void Face(Vector3 normal, Vector3 u, Vector3 v, float uLen, float vLen, int su, int sv, float offset)
            {
                int start = verts.Count;
                for (int j = 0; j <= sv; j++)
                for (int i = 0; i <= su; i++)
                {
                    float a = (i / (float)su - 0.5f) * uLen, b = (j / (float)sv - 0.5f) * vLen;
                    verts.Add(center + normal * offset + u * a + v * b);
                    uvs.Add(new Vector2(a, b));
                }
                bool flip = Vector3.Dot(Vector3.Cross(v, u), normal) < 0f;
                for (int j = 0; j < sv; j++)
                for (int i = 0; i < su; i++)
                {
                    int a0 = start + j * (su + 1) + i, a1 = a0 + 1, b0 = a0 + su + 1, b1 = b0 + 1;
                    if (!flip) { tris.Add(a0); tris.Add(b0); tris.Add(a1); tris.Add(a1); tris.Add(b0); tris.Add(b1); }
                    else { tris.Add(a0); tris.Add(a1); tris.Add(b0); tris.Add(a1); tris.Add(b1); tris.Add(b0); }
                }
            }

            Face(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y, segments.z, segments.y, h.x);
            Face(Vector3.left, Vector3.forward, Vector3.up, size.z, size.y, segments.z, segments.y, h.x);
            Face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z, segments.x, segments.z, h.y);
            Face(Vector3.down, Vector3.right, Vector3.forward, size.x, size.z, segments.x, segments.z, h.y);
            Face(Vector3.forward, Vector3.right, Vector3.up, size.x, size.y, segments.x, segments.y, h.z);
            Face(Vector3.back, Vector3.right, Vector3.up, size.x, size.y, segments.x, segments.y, h.z);
            return Finish(name, verts, uvs, tris);
        }

        /// <summary>Flat disc facing up.</summary>
        public static Mesh Disk(float radius, int segments, string name = "Disk")
        {
            return Annulus(0.001f, radius, 1, segments, 0f, 360f, null, name);
        }

        /// <summary>
        /// Ring (sector) surface facing up. height(radialT, angleT) gives the surface height,
        /// e.g. for a banked wall ramp.
        /// </summary>
        public static Mesh Annulus(float rIn, float rOut, int radialSegs, int angleSegs, float a0Deg, float a1Deg,
            Func<float, float, float> height, string name = "Ring")
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int j = 0; j <= angleSegs; j++)
            {
                float at = j / (float)angleSegs;
                float a = Mathf.Lerp(a0Deg, a1Deg, at) * Mathf.Deg2Rad;
                for (int i = 0; i <= radialSegs; i++)
                {
                    float rt = i / (float)radialSegs;
                    float r = Mathf.Lerp(rIn, rOut, rt);
                    float y = height != null ? height(rt, at) : 0f;
                    var p = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                    verts.Add(p);
                    uvs.Add(new Vector2(p.x * 0.25f, p.z * 0.25f));
                }
            }
            for (int j = 0; j < angleSegs; j++)
            for (int i = 0; i < radialSegs; i++)
            {
                int a = j * (radialSegs + 1) + i, b = a + 1, c = a + radialSegs + 1, d = c + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
            // Make sure the surface faces up.
            Vector3 n = Vector3.Cross(verts[tris[1]] - verts[tris[0]], verts[tris[2]] - verts[tris[0]]);
            if (n.y < 0f)
                for (int t = 0; t < tris.Count; t += 3) (tris[t + 1], tris[t + 2]) = (tris[t + 2], tris[t + 1]);
            return Finish(name, verts, uvs, tris, smooth: true);
        }

        /// <summary>
        /// Straight banked ramp along a wall. Local X runs along the wall (centred), local Z goes from the
        /// inner edge (z=0, ground level) to the wall (z=depth, full height). Ends taper down to the ground.
        /// </summary>
        public static Mesh BankStrip(float length, float depth, float height, int lengthSegs, int depthSegs,
            float taperLength, string name = "BankStrip")
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int j = 0; j <= lengthSegs; j++)
            {
                float x = (j / (float)lengthSegs - 0.5f) * length;
                float fromEnd = length * 0.5f - Mathf.Abs(x);
                float taper = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fromEnd / taperLength));
                for (int i = 0; i <= depthSegs; i++)
                {
                    float zt = i / (float)depthSegs;
                    var p = new Vector3(x, 0.02f + height * Mathf.Pow(zt, 1.7f) * taper, zt * depth);
                    verts.Add(p);
                    uvs.Add(new Vector2(p.x * 0.25f, p.z * 0.25f));
                }
            }
            for (int j = 0; j < lengthSegs; j++)
            for (int i = 0; i < depthSegs; i++)
            {
                int a = j * (depthSegs + 1) + i, b = a + 1, c = a + depthSegs + 1, d = c + 1;
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(b); tris.Add(d); tris.Add(c);
            }
            Vector3 n = Vector3.Cross(verts[tris[1]] - verts[tris[0]], verts[tris[2]] - verts[tris[0]]);
            if (n.y < 0f)
                for (int t = 0; t < tris.Count; t += 3) (tris[t + 1], tris[t + 2]) = (tris[t + 2], tris[t + 1]);
            return Finish(name, verts, uvs, tris, smooth: true);
        }

        /// <summary>Ramp wedge: starts at z=0 at ground level, rises to height at z=length. Origin at the front-bottom centre.</summary>
        public static Mesh Wedge(float width, float length, float height, string name = "Wedge")
        {
            float w = width * 0.5f;
            Vector3 a = new Vector3(-w, 0, 0), b = new Vector3(w, 0, 0);
            Vector3 c = new Vector3(-w, 0, length), d = new Vector3(w, 0, length);
            Vector3 e = new Vector3(-w, height, length), f = new Vector3(w, height, length);
            Vector3 centroid = new Vector3(0, height / 3f, length * 2f / 3f);
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            void Tri(Vector3 p0, Vector3 p1, Vector3 p2)
            {
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                if (Vector3.Dot(n, (p0 + p1 + p2) / 3f - centroid) < 0f) (p1, p2) = (p2, p1);
                int s = verts.Count;
                verts.Add(p0); verts.Add(p1); verts.Add(p2);
                uvs.Add(new Vector2(p0.x, p0.z + p0.y)); uvs.Add(new Vector2(p1.x, p1.z + p1.y)); uvs.Add(new Vector2(p2.x, p2.z + p2.y));
                tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
            }
            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3) { Tri(p0, p1, p2); Tri(p0, p2, p3); }

            Quad(a, b, f, e);   // slope
            Quad(c, d, f, e);   // back
            Quad(a, b, d, c);   // bottom
            Tri(a, c, e);       // left side
            Tri(b, d, f);       // right side
            return Finish(name, verts, uvs, tris);
        }

        static Mesh Finish(string name, List<Vector3> verts, List<Vector2> uvs, List<int> tris, bool smooth = false)
        {
            var m = new Mesh { name = name };
            if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
