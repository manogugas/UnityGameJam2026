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

        [Header("Recorded ambience")]
        public AudioClip crowdLoop;
        public AudioClip ambientLoop;
        [Range(0f, 1f)] public float crowdVolume = 0.45f;
        [Tooltip("Extra crowd volume at full excitement (big crash, kill).")]
        [Range(0f, 1f)] public float cheerBoost = 0.45f;
        [Range(0f, 1f)] public float ambientVolume = 0.3f;
        [Tooltip("Web build only (no audio filters there): the loops pre-muffled as heard from the cabin " +
                 "(CabinAudioBaker). Crossfaded with the originals instead of the live low-pass.")]
        public AudioClip crowdLoopCabin;
        public AudioClip ambientLoopCabin;

        [Header("Heard from inside the car")]
        [Tooltip("0 = outside (intro cameras), 1 = inside the cabin. Set with SetCabin().")]
        [Range(0f, 1f)] public float cabin = 1f;
        [Tooltip("Volume multiplier inside the cabin.")]
        [Range(0f, 1f)] public float cabinVolume = 0.4f;
        [Tooltip("Low-pass cutoff inside the cabin (body panels and glass block the highs).")]
        public float cabinCutoff = 1100f;
        public float cabinFadeTime = 1.2f;

        [Header("Procedural crowd (optional layer)")]
        public bool useSynth;
        [Range(0f, 2f)] public float volume = 0.4f;

        class Chunk { public Transform t; public float phase, speed; public Vector3 basePos; }

        readonly List<Chunk> chunks = new List<Chunk>();
        float excitement;
        volatile float roar;
        int sampleRate;
        volatile CrowdSynth crowdSynth;
        AudioSource crowdSource, ambientSource, crowdCabinSource, ambientCabinSource;
        AudioLowPassFilter crowdFilter, ambientFilter;
        float cabinTarget = 1f;
        bool webLoopsStarted;
        float muteLevel = 1f, muteTarget = 1f;

        /// <summary>Fade the crowd out (in the garage) or back in.</summary>
        public void SetMuted(bool muted) => muteTarget = muted ? 0f : 1f;

        void Start()
        {
            Build();
            sampleRate = AudioSettings.outputSampleRate;
            cabinTarget = cabin;
            float crowdStart = crowdLoop != null ? Random.Range(0f, crowdLoop.length * 0.9f) : 0f;
            float ambientStart = ambientLoop != null ? Random.Range(0f, ambientLoop.length * 0.9f) : 0f;
            crowdSource = MakeLoop("CrowdLoop", crowdLoop, crowdStart, out crowdFilter);
            ambientSource = MakeLoop("AmbientLoop", ambientLoop, ambientStart, out ambientFilter);
            if (WebAudio.Active)
            {
                // No filters on the web: play the pre-muffled copies in sync and crossfade (see ApplyMix).
                crowdCabinSource = MakeLoop("CrowdLoopCabin", crowdLoopCabin, crowdStart, out _);
                ambientCabinSource = MakeLoop("AmbientLoopCabin", ambientLoopCabin, ambientStart, out _);
            }
            GameSettings.Route(GetComponent<AudioSource>(), GameSettings.Channel.Crowd);
            if (useSynth)
            {
                crowdSynth = new CrowdSynth(sampleRate);
                var src = GetComponent<AudioSource>();
                src.clip = AudioClip.Create("silence", sampleRate, 1, sampleRate, false);
                src.loop = true;
                src.spatialBlend = 0f;
                src.Play();
            }
            ApplyMix(0f);
        }

        /// <summary>Looping 2D source on its own child (keeps it out of the procedural filter chain).</summary>
        AudioSource MakeLoop(string name, AudioClip clip, float startTime, out AudioLowPassFilter filter)
        {
            filter = null;
            if (clip == null) return null;
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            GameSettings.Route(src, GameSettings.Channel.Crowd);
            src.clip = clip;
            src.loop = true;
            src.spatialBlend = 0f;
            src.playOnAwake = false;
            src.volume = 0f; // ApplyMix sets the real level (before play on the web)
            if (!WebAudio.Active) filter = go.AddComponent<AudioLowPassFilter>();
            // Start each loop at a random point so the two never line up the same way.
            src.time = Mathf.Min(startTime, clip.length * 0.9f);
            if (!WebAudio.Active) src.Play(); // web: started in Update once the browser has unlocked audio
            return src;
        }

        /// <summary>Inside the car (muffled, quieter) or outside (intro cameras).</summary>
        public void SetCabin(bool inside) => cabinTarget = inside ? 1f : 0f;

        void ApplyMix(float e)
        {
            if (WebAudio.Active) { ApplyWebMix(e); return; }
            float vol = Mathf.Lerp(1f, cabinVolume, cabin) * muteLevel;
            float cutoff = Mathf.Lerp(22000f, cabinCutoff, Mathf.Sqrt(cabin));
            if (crowdSource != null)
            {
                crowdSource.volume = (crowdVolume + cheerBoost * Mathf.Clamp01(e)) * vol;
                crowdSource.pitch = 1f + 0.04f * Mathf.Clamp01(e); // a touch more urgent when they roar
                crowdFilter.cutoffFrequency = cutoff;
            }
            if (ambientSource != null)
            {
                ambientSource.volume = ambientVolume * vol;
                ambientFilter.cutoffFrequency = cutoff;
            }
        }

        /// <summary>Web: same levels as the desktop mix, with the cabin low-pass replaced by a crossfade to the
        /// pre-muffled copies (sqrt matches how the desktop cutoff falls), and the Crowd slider applied directly.</summary>
        void ApplyWebMix(float e)
        {
            float vol = Mathf.Lerp(1f, cabinVolume, cabin) * muteLevel * WebAudio.Volume(GameSettings.Channel.Crowd);
            float muffled = Mathf.Sqrt(cabin);
            float crowd = (crowdVolume + cheerBoost * Mathf.Clamp01(e)) * vol;
            float pitch = 1f + 0.04f * Mathf.Clamp01(e);
            SetLoop(crowdSource, crowdCabinSource, crowd, muffled, pitch);
            SetLoop(ambientSource, ambientCabinSource, ambientVolume * vol, muffled, 1f);
        }

        static void SetLoop(AudioSource open, AudioSource cabinCopy, float volume, float muffled, float pitch)
        {
            if (cabinCopy == null) muffled = 0f; // no baked copy: fall back to the plain loop
            if (open != null) { WebAudio.SetVolume(open, volume * (1f - muffled)); open.pitch = pitch; }
            if (cabinCopy != null) { WebAudio.SetVolume(cabinCopy, volume * muffled); cabinCopy.pitch = pitch; }
        }

        /// <summary>Web: start the loops (levels already set) in the same frame, each pair from the same point.</summary>
        void StartWebLoops()
        {
            webLoopsStarted = true;
            foreach (var s in new[] { crowdSource, crowdCabinSource, ambientSource, ambientCabinSource })
                if (s != null && !s.isPlaying) s.Play();
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
            cabin = Mathf.MoveTowards(cabin, cabinTarget, Time.deltaTime / Mathf.Max(0.01f, cabinFadeTime));
            muteLevel = Mathf.MoveTowards(muteLevel, muteTarget, Time.deltaTime / 1.2f);
            ApplyMix(excitement);
            if (WebAudio.Active && !webLoopsStarted && WebAudio.Unlocked) StartWebLoops();
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

        void OnAudioFilterRead(float[] data, int channels)
        {
            var synth = crowdSynth;
            if (synth == null || !useSynth || WebAudio.Active) return;
            synth.excitement = roar;
            float vol = volume;
            for (int i = 0; i < data.Length; i += channels)
            {
                synth.Render(out float l, out float r);
                l = Mathf.Clamp(l * vol, -1f, 1f);
                r = Mathf.Clamp(r * vol, -1f, 1f);
                if (channels == 1) data[i] = (l + r) * 0.5f;
                else
                {
                    data[i] = l;
                    data[i + 1] = r;
                    for (int c = 2; c < channels; c++) data[i + c] = (l + r) * 0.5f;
                }
            }
        }
    }
}
