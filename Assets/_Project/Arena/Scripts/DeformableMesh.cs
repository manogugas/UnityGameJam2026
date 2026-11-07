using UnityEngine;

namespace CarArena
{
    /// <summary>Clones its mesh at runtime and dents vertices around impact points.</summary>
    [RequireComponent(typeof(MeshFilter))]
    public class DeformableMesh : MonoBehaviour
    {
        [Tooltip("Max distance a vertex can move from its original position.")]
        public float maxDeform = 0.35f;
        [Tooltip("How uneven dents are (0 = smooth bowls).")]
        [Range(0f, 1f)] public float crumple = 0.6f;

        Mesh mesh, sourceMesh;
        Vector3[] original, current;
        float[] noise;
        MeshFilter filter;

        void Awake()
        {
            filter = GetComponent<MeshFilter>();
            sourceMesh = filter.sharedMesh;
            mesh = Instantiate(sourceMesh);
            mesh.name = sourceMesh.name + " (deformed)";
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
            original = mesh.vertices;
            current = (Vector3[])original.Clone();
            noise = new float[original.Length];
            for (int i = 0; i < original.Length; i++)
            {
                Vector3 v = original[i];
                noise[i] = Mathf.PerlinNoise(v.x * 3.7f + v.y * 5.1f + 11f, v.z * 3.3f + 7f);
            }
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }

        /// <summary>Push vertices within radius of worldPoint along worldDir by up to depth metres.</summary>
        public void Deform(Vector3 worldPoint, Vector3 worldDir, float depth, float radius)
        {
            if (current == null || depth <= 0f) return;
            Vector3 p = transform.InverseTransformPoint(worldPoint);
            Vector3 d = transform.InverseTransformDirection(worldDir).normalized;
            float r2 = radius * radius;
            bool changed = false;
            for (int i = 0; i < current.Length; i++)
            {
                float dist2 = (current[i] - p).sqrMagnitude;
                if (dist2 >= r2) continue;
                float f = 1f - Mathf.Sqrt(dist2) / radius;
                f *= f;
                f *= Mathf.Lerp(1f, 0.3f + 1.4f * noise[i], crumple);
                Vector3 moved = current[i] + d * (depth * f);
                current[i] = original[i] + Vector3.ClampMagnitude(moved - original[i], maxDeform);
                changed = true;
            }
            if (changed) Apply();
        }

        /// <summary>Raise the middle of the panel along its local Z (a hood folding up from a frontal hit).</summary>
        public void Buckle(float amount)
        {
            if (current == null || amount <= 0f) return;
            var b = sourceMesh.bounds;
            for (int i = 0; i < current.Length; i++)
            {
                float t = Mathf.InverseLerp(b.min.z, b.max.z, original[i].z);
                float across = 1f - Mathf.Abs(original[i].x) / Mathf.Max(b.extents.x, 0.01f) * 0.4f;
                Vector3 moved = current[i] + Vector3.up * (amount * Mathf.Sin(t * Mathf.PI) * across * (0.7f + 0.6f * noise[i]));
                current[i] = original[i] + Vector3.ClampMagnitude(moved - original[i], maxDeform);
            }
            Apply();
        }

        public void Restore()
        {
            if (current == null) return;
            System.Array.Copy(original, current, original.Length);
            Apply();
        }

        void Apply()
        {
            mesh.vertices = current;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }
}
