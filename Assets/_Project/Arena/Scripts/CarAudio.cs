using UnityEngine;

namespace CarArena
{
    /// <summary>Procedural engine, tyre squeal, gravel rumble and impact thumps (no audio assets).</summary>
    [RequireComponent(typeof(AudioSource))]
    public class CarAudio : MonoBehaviour
    {
        public CarController car;
        public CarDamage damage;
        [Range(0f, 1f)] public float volume = 0.35f;

        AudioSource source;
        int sampleRate;
        volatile float rpm = 900f, load, squeal, gravel, impactKick;
        double phase, subPhase, squealPhase, vibratoPhase, thumpPhase;
        float lpEngine, lpGravel, lpGravel2, lpImpact, impactEnv, squealAmp, gravelAmp;
        uint rng = 2463534242u;

        void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate;
            source = GetComponent<AudioSource>();
            source.clip = AudioClip.Create("silence", sampleRate, 1, sampleRate, false);
            source.loop = true;
            source.spatialBlend = 0f;
            source.playOnAwake = false;
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
            float r = rpm, l = load, sq = squeal, gr = gravel;
            if (impactKick > 0f) { impactEnv = Mathf.Max(impactEnv, impactKick); impactKick = 0f; }
            double f = r / 60.0 * 2.0;
            float engineCut = 0.08f + l * 0.12f + r / 7000f * 0.1f;
            float impactDecay = Mathf.Exp(-6f / sampleRate);

            for (int i = 0; i < data.Length; i += channels)
            {
                phase += f * dt; if (phase >= 1.0) phase -= 1.0;
                subPhase += f * 0.5 * dt; if (subPhase >= 1.0) subPhase -= 1.0;
                float saw = (float)(phase * 2.0 - 1.0);
                float pulse = phase < 0.3 ? 1f : -0.4f;
                float sub = subPhase < 0.5 ? 0.6f : -0.6f;
                float n = Noise();
                float engine = (saw * 0.45f + pulse * 0.35f + sub * 0.3f + n * 0.12f * l) * (0.4f + 0.6f * l);
                lpEngine += (engine - lpEngine) * engineCut;

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

                float s = Mathf.Clamp((lpEngine * 0.7f + squealS + gravelS + impactS) * volume, -1f, 1f);
                for (int c = 0; c < channels; c++) data[i + c] = s;
            }
        }
    }
}
