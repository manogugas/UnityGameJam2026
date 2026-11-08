using System.Collections.Generic;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Fills the stands with blocky spectators at startup (no mesh assets in the repo) and makes them
    /// jump and roar when something big happens in the arena. Spectators are merged into a few meshes
    /// per side and row band so the whole crowd is a handful of draw calls.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class ArenaCrowd : MonoBehaviour
    {
        [Header("Look")]
        public Mesh cube;
        public Material[] clothes;
        public Material skin;

        [Header("Stands layout (must match the builder)")]
        [Tooltip("Distance from the arena centre to the front of the first row, on each side.")]
        public float firstRowDistance = 77f;
        public float rowDepth = 1.3f;
        public float firstRowTop = 6.6f;
        public float rowRise = 0.75f;
        public int rows = 14;
        public float seatSpacing = 0.85f;
        [Range(0f, 1f)] public float density = 0.65f;
        [Tooltip("Rows grouped per jumping chunk.")]
        public int rowsPerBand = 4;
        public int seed = 7;
        [Tooltip("World-space boxes left empty (e.g. the emperor's box).")]
        public Bounds[] exclusions;

        [Header("Cheering")]
        public float jumpHeight = 0.35f;
        public float excitementDecay = 0.45f;
        [Range(0f, 1f)] public float idleExcitement = 0.08f;

        [Header("Sound")]
        [Range(0f, 1f)] public float volume = 0.18f;

        class Chunk { public Transform t; public float phase, speed; public Vector3 basePos; }

        readonly List<Chunk> chunks = new List<Chunk>();
        float excitement;
        volatile float roar;
        int sampleRate;
        float b1, b2, b3, l1, l2, swell, swellTarget;
        uint rng = 0x2545F491u;

        void Start()
        {
            Build();
            sampleRate = AudioSettings.outputSampleRate;
            var src = GetComponent<AudioSource>();
            src.clip = AudioClip.Create("silence", sampleRate, 1, sampleRate, false);
            src.loop = true;
            src.spatialBlend = 0f;
            src.Play();
        }

        /// <summary>0..1 how exciting the moment was (a big crash = ~1).</summary>
        public void Cheer(float intensity)
        {
            excitement = Mathf.Clamp01(Mathf.Max(excitement, intensity));
        }

        void Build()
        {
            if (cube == null || clothes == null || clothes.Length == 0) return;
            var rnd = new System.Random(seed);
            int materials = clothes.Length + 1; // + skin
            // Directions for the four sides: outward normal and the axis along the side.
            Vector3[] outward = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            for (int side = 0; side < 4; side++)
            {
                Vector3 n = outward[side];
                Vector3 along = Vector3.Cross(Vector3.up, n);
                Quaternion face = Quaternion.LookRotation(-n, Vector3.up);
                for (int band = 0; band * rowsPerBand < rows; band++)
                {
                    var perMat = new List<CombineInstance>[materials];
                    for (int m = 0; m < materials; m++) perMat[m] = new List<CombineInstance>();
                    for (int row = band * rowsPerBand; row < Mathf.Min(rows, (band + 1) * rowsPerBand); row++)
                    {
                        float dist = firstRowDistance + rowDepth * (row + 0.35f);
                        float top = firstRowTop + rowRise * row;
                        // Row length reaches the corners; leave the corner itself empty so sides don't overlap.
                        float half = dist - 1.5f;
                        for (float x = -half; x <= half; x += seatSpacing)
                        {
                            if (rnd.NextDouble() > density) continue;
                            float jitter = (float)(rnd.NextDouble() - 0.5) * 0.2f;
                            float height = 0.62f + (float)rnd.NextDouble() * 0.18f;
                            Vector3 p = n * dist + along * (x + jitter) + Vector3.up * top;
                            if (Excluded(p)) continue;
                            Quaternion yaw = face * Quaternion.Euler(0f, (float)(rnd.NextDouble() - 0.5) * 30f, 0f);
                            int cloth = rnd.Next(clothes.Length);
                            perMat[cloth].Add(new CombineInstance
                            {
                                mesh = cube,
                                transform = Matrix4x4.TRS(p + Vector3.up * (height * 0.5f), yaw, new Vector3(0.48f, height, 0.36f)),
                            });
                            perMat[clothes.Length].Add(new CombineInstance
                            {
                                mesh = cube,
                                transform = Matrix4x4.TRS(p + Vector3.up * (height + 0.15f), yaw, Vector3.one * 0.26f),
                            });
                        }
                    }
                    for (int m = 0; m < materials; m++)
                    {
                        if (perMat[m].Count == 0) continue;
                        var mesh = new Mesh { name = $"Crowd_{side}_{band}_{m}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                        mesh.CombineMeshes(perMat[m].ToArray(), true, true);
                        var go = new GameObject(mesh.name);
                        go.transform.SetParent(transform, false);
                        go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var mr = go.AddComponent<MeshRenderer>();
                        mr.sharedMaterial = m < clothes.Length ? clothes[m] : skin;
                        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        chunks.Add(new Chunk
                        {
                            t = go.transform,
                            basePos = go.transform.localPosition,
                            phase = (float)rnd.NextDouble() * 6.28f,
                            speed = 9f + (float)rnd.NextDouble() * 4f,
                        });
                    }
                }
            }
        }

        bool Excluded(Vector3 p)
        {
            if (exclusions == null) return false;
            foreach (var b in exclusions) if (b.Contains(p)) return true;
            return false;
        }

        void Update()
        {
            excitement = Mathf.MoveTowards(excitement, 0f, excitementDecay * Time.deltaTime);
            float e = Mathf.Max(excitement, idleExcitement);
            float t = Time.time;
            foreach (var c in chunks)
            {
                if (c.t == null) continue;
                float bounce = Mathf.Max(0f, Mathf.Sin(t * c.speed + c.phase)) * jumpHeight * e;
                c.t.localPosition = c.basePos + Vector3.up * bounce;
            }
            roar = e;
        }

        void OnDestroy()
        {
            foreach (var c in chunks)
            {
                if (c.t == null) continue;
                var mf = c.t.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            }
        }

        float Noise()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 8388607.5f - 1f;
        }

        // Crowd roar: many voices = band-limited noise (roughly the voice range) with a slow, irregular
        // swell. Low-passed hard so it reads as a distant roar rather than hiss.
        void OnAudioFilterRead(float[] data, int channels)
        {
            if (sampleRate <= 0) return;
            float r = roar, vol = volume;
            float kSwell = 1f / (sampleRate * 0.4f);
            for (int i = 0; i < data.Length; i += channels)
            {
                if ((i & 1023) == 0) swellTarget = 0.6f + 0.4f * Noise();
                swell += (swellTarget - swell) * kSwell * 60f;

                float n = Noise();
                b1 += (n - b1) * 0.12f;          // remove the hiss
                b2 += (b1 - b2) * 0.12f;
                l1 += (b2 - l1) * 0.02f;         // and the rumble below the voice range
                b3 = b2 - l1;
                l2 += (b3 - l2) * 0.35f;
                float s = l2 * (0.25f + 2.2f * r) * swell * vol * 3f;
                s = Mathf.Clamp(s, -1f, 1f);
                for (int c = 0; c < channels; c++) data[i + c] = s;
            }
        }
    }
}
