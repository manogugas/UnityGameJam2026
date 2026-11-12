using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Procedural engine (EngineSynth), tyre squeal, gravel rumble and the steam hiss of a wrecked engine,
    /// plus recorded crash clips for impacts (on a separate 3D-capable source). ImpactSynth is an optional
    /// procedural crash layer.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class CarAudio : MonoBehaviour
    {
        public CarController car;
        public CarDamage damage;
        [Range(0f, 1f)] public float volume = 0.35f;
        [Tooltip("3D sound positioned at the car (enemies). Off = heard from inside the cabin (player).")]
        public bool spatial;
        public float spatialMinDistance = 6f;
        public float spatialMaxDistance = 150f;

        [Range(0f, 1f)] public float masterGain = 1f;

        [Header("Crashes")]
        [Tooltip("Recorded crash / break clips, one is picked at random per impact.")]
        public AudioClip[] crashClips;
        [Range(0f, 1f)] public float crashVolume = 0.85f;
        [Tooltip("Also layer the procedural ImpactSynth under the clips.")]
        public bool synthCrashLayer;

        [Header("Engine")]
        public EngineLayout engineLayout = EngineLayout.V8Crossplane;
        [Range(0f, 2f)] public float engineVolume = 1f;
        [Tooltip("Rev limiter rpm for the sound (combustions get cut above this).")]
        public float limiterRpm = 6700f;
        [Range(0f, 1f)] public float stereoWidth = 0.35f;

        AudioSource source;
        int sampleRate;
        volatile float rpm = 900f, load, squeal, gravel, steam;
        double squealPhase, vibratoPhase;
        float lpGravel, lpGravel2, squealAmp, gravelAmp, rpmSmooth = 900f, loadSmooth;
        float steamSmooth, steamHp, steamIn, steamLp;
        float deadTime = -1f;
        ImpactSynth crashes;
        volatile EngineSynth engine;
        volatile float gainL = 1f, gainR = 1f;
        Transform listener;
        AudioSource crashSource;
        int lastClip = -1;
        float gainSmooth = 1f;
        volatile float gainNow = 1f;
        uint rng = 2463534242u;

        void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate;
            source = GetComponent<AudioSource>();
            source.clip = AudioClip.Create("silence", sampleRate, 1, sampleRate, false);
            source.loop = true;
            // Distance and panning are done in the filter (procedural audio isn't reliably spatialised).
            source.spatialBlend = 0f;
            source.playOnAwake = false;
            GameSettings.Route(source, GameSettings.Channel.Car); // engine, tyres, gravel, steam
            engine = new EngineSynth(engineLayout, sampleRate);
            crashes = new ImpactSynth(sampleRate, (uint)Random.Range(1, int.MaxValue));
            gainSmooth = masterGain;

            // Crash clips get their own child source: the procedural filter on this object would
            // otherwise overwrite them, and recorded clips can use Unity's real 3D audio.
            var go = new GameObject("CrashAudio");
            go.transform.SetParent(transform, false);
            crashSource = go.AddComponent<AudioSource>();
            crashSource.playOnAwake = false;
            crashSource.rolloffMode = AudioRolloffMode.Logarithmic;
            crashSource.minDistance = 8f;
            crashSource.maxDistance = 220f;
            crashSource.dopplerLevel = 0f;
            GameSettings.Route(crashSource, GameSettings.Channel.Effects);
            ApplyCrashSpatial();
        }

        /// <summary>Exterior = heard from outside (intro cameras, enemies). Interior = from the driver's seat.</summary>
        public void SetExterior(bool exterior)
        {
            spatial = exterior;
            listener = null;
            ApplyCrashSpatial();
        }

        void ApplyCrashSpatial()
        {
            if (crashSource != null) crashSource.spatialBlend = spatial ? 1f : 0f;
        }

        void OnEnable()
        {
            if (damage != null) damage.Impact += OnImpact;
            if (source != null && !source.isPlaying) source.Play();
        }

        void OnDisable()
        {
            if (damage != null) damage.Impact -= OnImpact;
        }

        void OnImpact(ImpactInfo i)
        {
            if (synthCrashLayer) crashes?.Trigger(0.15f + i.severity * 0.85f);
            if (crashClips == null || crashClips.Length == 0 || crashSource == null) return;
            int pick = Random.Range(0, crashClips.Length);
            if (crashClips.Length > 1 && pick == lastClip) pick = (pick + 1) % crashClips.Length;
            lastClip = pick;
            // Bigger hits: louder and slightly lower (heavier); small knocks: quieter and higher.
            crashSource.pitch = Mathf.Lerp(1.12f, 0.9f, i.severity) * Random.Range(0.96f, 1.04f);
            crashSource.PlayOneShot(crashClips[pick], Mathf.Lerp(0.25f, 1f, i.severity) * crashVolume * masterGain);
        }

        void Update()
        {
            if (engine == null || engine.Layout != engineLayout) engine = new EngineSynth(engineLayout, sampleRate);
            gainSmooth = Mathf.MoveTowards(gainSmooth, masterGain, Time.deltaTime * 1.5f);
            gainNow = gainSmooth;
            rpm = car.Rpm;
            load = car.ThrottleOut;
            float sq = 0f, gr = 0f;
            foreach (var w in car.wheels)
            {
                if (!w.Grounded || w.Surface == null) continue;
                float slide = Mathf.Clamp01((w.CombinedSlip - 1.1f) * 0.6f) * Mathf.Clamp01(w.ContactSpeed / 3f);
                if (w.Surface.loose) gr = Mathf.Max(gr, Mathf.Clamp01(w.ContactSpeed / 25f) * 0.6f + slide * 0.5f);
                else sq = Mathf.Max(sq, slide);
            }
            squeal = sq;

            // Wrecked: radiator steam, loud at first and dying away over ~10 s.
            if (car.EngineDead) { if (deadTime < 0f) deadTime = Time.time; steam = Mathf.Clamp01(1f - (Time.time - deadTime) / 10f) * 0.8f + 0.1f; }
            else { deadTime = -1f; steam = 0f; }
            gravel = gr;
            UpdateSpatial();
        }

        void UpdateSpatial()
        {
            if (!spatial) { gainL = gainR = 1f; return; }
            if (listener == null)
            {
                var l = FindAnyObjectByType<AudioListener>();
                if (l != null) listener = l.transform;
                if (listener == null) return;
            }
            Vector3 to = transform.position - listener.position;
            float d = to.magnitude;
            float gain = spatialMinDistance / Mathf.Max(d, spatialMinDistance);
            gain *= Mathf.Clamp01((spatialMaxDistance - d) / (spatialMaxDistance * 0.3f));
            float pan = d > 0.01f ? Mathf.Clamp(Vector3.Dot(to / d, listener.right), -1f, 1f) * 0.8f : 0f;
            gainL = gain * Mathf.Sqrt(0.5f * (1f - pan)) * 1.414f;
            gainR = gain * Mathf.Sqrt(0.5f * (1f + pan)) * 1.414f;
        }

        float Noise()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / (float)0x7FFFFF - 1f;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (sampleRate <= 0) return;
            double dt = 1.0 / sampleRate;
            var synth = engine;
            float r = rpm, l = load, sq = squeal, gr = gravel;
            var crash = crashes;
            float st = steam;
            float limiter = limiterRpm, width = stereoWidth, engVol = engineVolume;
            float gm = gainNow;
            float gL = gainL * gm, gR = gainR * gm;

            for (int i = 0; i < data.Length; i += channels)
            {
                float n = Noise();

                // Smooth control values per sample so the engine never steps or clicks.
                rpmSmooth += (r - rpmSmooth) * 0.002f;
                loadSmooth += (l - loadSmooth) * 0.003f;
                float engL = 0f, engR = 0f;
                if (synth != null) synth.Render(rpmSmooth, loadSmooth, limiter, out engL, out engR);
                float engMid = (engL + engR) * 0.5f, engSide = (engL - engR) * 0.5f * width;
                engL = (engMid + engSide) * engVol;
                engR = (engMid - engSide) * engVol;

                // Tyre squeal: clean tone with a slow vibrato, pitch rising with slide. Amplitude is
                // smoothed per sample so it fades instead of clicking.
                squealAmp += (sq - squealAmp) * 0.0004f;
                vibratoPhase += 6.5 * dt; if (vibratoPhase >= 1.0) vibratoPhase -= 1.0;
                double sqFreq = 640.0 + 160.0 * squealAmp + 22.0 * System.Math.Sin(vibratoPhase * 6.283185);
                squealPhase += sqFreq * dt; if (squealPhase >= 1.0) squealPhase -= 1.0;
                float sp = (float)(squealPhase * 6.283185);
                float squealS = (Mathf.Sin(sp) + 0.3f * Mathf.Sin(sp * 2f + 0.6f)) * squealAmp * 0.14f;

                // Loose surface: brown (double low-passed) noise = low rumble rather than hiss.
                gravelAmp += (gr - gravelAmp) * 0.0004f;
                lpGravel += (n - lpGravel) * 0.025f;
                lpGravel2 += (lpGravel - lpGravel2) * 0.025f;
                float gravelS = lpGravel2 * gravelAmp * 5f;

                float impactS = crash != null ? crash.Render() * 1.3f : 0f;

                // Steam: high-passed noise with a slow flutter, smoothed in and out.
                steamSmooth += (st - steamSmooth) * 0.0005f;
                float sn = Noise();
                steamHp = 0.85f * (steamHp + sn - steamIn); steamIn = sn;
                steamLp += (steamHp - steamLp) * 0.5f;
                float steamS = steamSmooth > 0.001f ? steamLp * steamSmooth * 0.18f : 0f;

                float fx = squealS + gravelS + impactS + steamS;
                float sl = Mathf.Clamp((engL + fx) * volume * gL, -1f, 1f);
                float sr = Mathf.Clamp((engR + fx) * volume * gR, -1f, 1f);
                if (channels == 1) data[i] = (sl + sr) * 0.5f;
                else
                {
                    data[i] = sl;
                    data[i + 1] = sr;
                    for (int c = 2; c < channels; c++) data[i + c] = (sl + sr) * 0.5f;
                }
            }
        }
    }
}
