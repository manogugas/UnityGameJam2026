using UnityEngine;

namespace CarArena
{
    /// <summary>Procedural engine (EngineSynth), tyre squeal, gravel rumble and impact thumps (no audio assets).</summary>
    [RequireComponent(typeof(AudioSource))]
    public class CarAudio : MonoBehaviour
    {
        public CarController car;
        public CarDamage damage;
        [Range(0f, 1f)] public float volume = 0.35f;

        [Header("Engine")]
        public EngineLayout engineLayout = EngineLayout.V8Crossplane;
        [Range(0f, 2f)] public float engineVolume = 1f;
        [Tooltip("Rev limiter rpm for the sound (combustions get cut above this).")]
        public float limiterRpm = 6700f;
        [Range(0f, 1f)] public float stereoWidth = 0.35f;

        AudioSource source;
        int sampleRate;
        volatile float rpm = 900f, load, squeal, gravel, impactKick;
        double squealPhase, vibratoPhase, thumpPhase;
        float lpGravel, lpGravel2, lpImpact, impactEnv, squealAmp, gravelAmp, rpmSmooth = 900f, loadSmooth;
        volatile EngineSynth engine;
        uint rng = 2463534242u;

        void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate;
            source = GetComponent<AudioSource>();
            source.clip = AudioClip.Create("silence", sampleRate, 1, sampleRate, false);
            source.loop = true;
            source.spatialBlend = 0f;
            source.playOnAwake = false;
            engine = new EngineSynth(engineLayout, sampleRate);
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

        void OnImpact(ImpactInfo i) => impactKick = Mathf.Max(impactKick, 0.35f + i.severity);

        void Update()
        {
            if (engine == null || engine.Layout != engineLayout) engine = new EngineSynth(engineLayout, sampleRate);
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
            gravel = gr;
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
            if (impactKick > 0f) { impactEnv = Mathf.Max(impactEnv, impactKick); impactKick = 0f; }
            float impactDecay = Mathf.Exp(-6f / sampleRate);
            float limiter = limiterRpm, width = stereoWidth, engVol = engineVolume;

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

                thumpPhase += 48.0 * dt; if (thumpPhase >= 1.0) thumpPhase -= 1.0;
                lpImpact += (n - lpImpact) * 0.2f;
                float impactS = (Mathf.Sin((float)(thumpPhase * 6.283185)) * 0.9f + lpImpact * 1.4f) * impactEnv;
                impactEnv *= impactDecay;

                float fx = squealS + gravelS + impactS;
                float sl = Mathf.Clamp((engL + fx) * volume, -1f, 1f);
                float sr = Mathf.Clamp((engR + fx) * volume, -1f, 1f);
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
