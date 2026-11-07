using System.Collections.Generic;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Spawns starburst cracks made of thin strips on a flat surface (local XY plane).
    /// Used for the windscreen and the dashboard.
    /// </summary>
    public class CrackDecals : MonoBehaviour
    {
        public Material crackMaterial;
        public Mesh stripMesh;
        public Vector2 halfSize = new Vector2(0.75f, 0.3f);
        [Tooltip("Local Z offset of the cracks (toward the driver).")]
        public float surfaceOffset = -0.007f;
        public float stripWidth = 0.004f;
        public int maxCracks = 14;

        readonly List<GameObject> cracks = new List<GameObject>();

        public int Count => cracks.Count;

        public void AddCrackWorld(Vector3 worldPoint, float severity)
        {
            Vector3 p = transform.InverseTransformPoint(worldPoint);
            AddCrack(new Vector2(p.x, p.y), severity);
        }

        public void AddCrack(Vector2 localPos, float severity)
        {
            if (cracks.Count >= maxCracks || crackMaterial == null || stripMesh == null) return;
            localPos.x = Mathf.Clamp(localPos.x, -halfSize.x * 0.9f, halfSize.x * 0.9f);
            localPos.y = Mathf.Clamp(localPos.y, -halfSize.y * 0.9f, halfSize.y * 0.9f);

            var root = new GameObject("Crack");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(localPos.x, localPos.y, surfaceOffset);
            cracks.Add(root);

            int rays = 5 + Mathf.RoundToInt(severity * 8f);
            float reach = 0.08f + severity * 0.4f;
            for (int r = 0; r < rays; r++)
            {
                float angle = (r + Random.Range(-0.3f, 0.3f)) / rays * Mathf.PI * 2f;
                Vector2 pos = Vector2.zero;
                float len = reach * Random.Range(0.4f, 1f);
                int segs = Random.Range(2, 4);
                for (int s = 0; s < segs; s++)
                {
                    angle += Random.Range(-0.35f, 0.35f);
                    Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    float segLen = len / segs;
                    Strip(root.transform, pos, dir, segLen);
                    pos += dir * segLen;
                }
            }
            // Rough ring around the impact point.
            int ringSegs = 7;
            float ringR = 0.02f + severity * 0.05f;
            for (int i = 0; i < ringSegs; i++)
            {
                float a0 = i / (float)ringSegs * Mathf.PI * 2f, a1 = (i + 1) / (float)ringSegs * Mathf.PI * 2f;
                Vector2 p0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * ringR * Random.Range(0.8f, 1.2f);
                Vector2 p1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * ringR;
                Strip(root.transform, p0, (p1 - p0).normalized, (p1 - p0).magnitude);
            }
        }

        void Strip(Transform parent, Vector2 start, Vector2 dir, float length)
        {
            var go = new GameObject("s");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = (Vector3)(start + dir * (length * 0.5f));
            go.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
            go.transform.localScale = new Vector3(stripWidth, length, 0.002f);
            go.AddComponent<MeshFilter>().sharedMesh = stripMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = crackMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public void Clear()
        {
            foreach (var c in cracks) if (c != null) Destroy(c);
            cracks.Clear();
        }
    }
}
