using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Procedural stadium crowd (audio thread, stereo, no allocations).
    ///  - Babble: noise through vowel formant band-passes (~500 / 1200 / 2600 Hz) with a random
    ///    syllable-rate wobble per band, so it reads as thousands of voices instead of hiss.
    ///  - Body: low rumble of the mass of people.
    ///  - Shouts: individual voices (buzzy glide through a formant) popping out of the mix.
    ///  - Whistles: two-finger whistles, glides around 2-3.5 kHz, on big moments.
    ///  - Applause: random hand-clap clicks whose rate follows the excitement.
    /// Excitement (0..1) is set from the main thread; it raises level, brightness and event rates.
    /// </summary>
    public class CrowdSynth
    {
        struct Biquad
        {
            float b0, b1, b2, a1, a2, x1, x2, y1, y2;

            public static Biquad BandPass(float sampleRate, float freq, float q)
            {
                float w = 2f * Mathf.PI * freq / sampleRate;
                float alpha = Mathf.Sin(w) / (2f * q);
                float a0 = 1f + alpha;
                return new Biquad
                {
                    b0 = alpha / a0, b1 = 0f, b2 = -alpha / a0,
                    a1 = -2f * Mathf.Cos(w) / a0, a2 = (1f - alpha) / a0,
                };
            }

            public float Process(float x)
            {
                float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }

        class Channel
        {
            public Biquad f1, f2, f3, body, shoutFormant, clap;
            public float wob1, wob2, wob3, wobT1, wobT2, wobT3;
            public float shoutEnv, shoutPhase, shoutFreq, shoutGlide;
            public float whistleEnv, whistlePhase, whistleFreq, whistleGlide, vibPhase;
            public float clapEnv;
        }

        readonly Channel left, right;
        readonly float sampleRate, dt;
        uint rng;
        float swell = 0.7f, swellTarget = 0.7f;
        int counter;

        public volatile float excitement;

        public CrowdSynth(int sampleRate, uint seed = 0x51F15EEDu)
        {
            this.sampleRate = sampleRate;
            dt = 1f / sampleRate;
            rng = seed == 0 ? 1u : seed;
            left = MakeChannel(0.97f);
            right = MakeChannel(1.03f);
        }

        Channel MakeChannel(float detune) => new Channel
        {
            f1 = Biquad.BandPass(sampleRate, 520f * detune, 2.2f),
            f2 = Biquad.BandPass(sampleRate, 1250f * detune, 2.5f),
            f3 = Biquad.BandPass(sampleRate, 2600f * detune, 3f),
            body = Biquad.BandPass(sampleRate, 190f * detune, 1.2f),
            shoutFormant = Biquad.BandPass(sampleRate, 900f * detune, 3.5f),
            clap = Biquad.BandPass(sampleRate, 1800f * detune, 1.4f),
            wob1 = 1f, wob2 = 1f, wob3 = 1f, wobT1 = 1f, wobT2 = 1f, wobT3 = 1f,
        };

        float Rand01() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777215f; }
        float RandSigned() => Rand01() * 2f - 1f;

        public void Render(out float l, out float r)
        {
            float e = excitement;
            if ((++counter & 2047) == 0) swellTarget = 0.6f + 0.4f * Rand01();
            swell += (swellTarget - swell) * 0.00005f;
            l = RenderChannel(left, e);
            r = RenderChannel(right, e);
        }

        float RenderChannel(Channel c, float e)
        {
            // Syllable-rate wobble per formant band (random targets, smoothed).
            if (Rand01() < 5f * dt) c.wobT1 = 0.5f + Rand01();
            if (Rand01() < 6f * dt) c.wobT2 = 0.4f + Rand01() * 1.2f;
            if (Rand01() < 7f * dt) c.wobT3 = 0.3f + Rand01() * 1.4f;
            c.wob1 += (c.wobT1 - c.wob1) * 0.002f;
            c.wob2 += (c.wobT2 - c.wob2) * 0.002f;
            c.wob3 += (c.wobT3 - c.wob3) * 0.002f;

            float n = RandSigned();
            float babble = c.f1.Process(n) * c.wob1 * 1.0f
                         + c.f2.Process(n) * c.wob2 * (0.55f + 0.6f * e)
                         + c.f3.Process(n) * c.wob3 * (0.18f + 0.45f * e);
            float body = c.body.Process(RandSigned()) * 0.6f;
            float level = (0.3f + 1.5f * e) * swell;
            float mix = (babble * 1.6f + body) * level;

            // Individual shouts.
            if (c.shoutEnv < 0.01f && Rand01() < (0.4f + 4f * e) * dt)
            {
                c.shoutEnv = 0.4f + 0.6f * Rand01();
                c.shoutFreq = 170f + 200f * Rand01();
                c.shoutGlide = (Rand01() < 0.5f ? -1f : 1f) * (40f + 80f * Rand01());
            }
            if (c.shoutEnv > 0.001f)
            {
                c.shoutFreq += c.shoutGlide * dt;
                c.shoutPhase += c.shoutFreq * dt; if (c.shoutPhase > 1f) c.shoutPhase -= 1f;
                float saw = c.shoutPhase * 2f - 1f;
                mix += c.shoutFormant.Process(saw) * c.shoutEnv * 0.35f;
                c.shoutEnv *= 0.99985f;
            }

            // Whistles on big moments.
            if (c.whistleEnv < 0.01f && e > 0.45f && Rand01() < (e - 0.45f) * 2.5f * dt)
            {
                c.whistleEnv = 0.6f + 0.4f * Rand01();
                c.whistleFreq = 2000f + 1400f * Rand01();
                c.whistleGlide = 300f + 900f * Rand01();
            }
            if (c.whistleEnv > 0.001f)
            {
                c.whistleFreq += c.whistleGlide * dt;
                c.vibPhase += 6f * dt;
                float f = c.whistleFreq * (1f + 0.015f * Mathf.Sin(c.vibPhase * 6.2831853f));
                c.whistlePhase += f * dt; if (c.whistlePhase > 1f) c.whistlePhase -= 1f;
                mix += Mathf.Sin(c.whistlePhase * 6.2831853f) * c.whistleEnv * 0.07f;
                c.whistleEnv *= 0.99992f;
            }

            // Applause: claps whose rate follows excitement.
            if (Rand01() < (5f + 160f * e * e) * dt) c.clapEnv = 0.5f + 0.5f * Rand01();
            if (c.clapEnv > 0.001f)
            {
                mix += c.clap.Process(RandSigned()) * c.clapEnv * 0.5f;
                c.clapEnv *= 0.9965f;
            }

            return mix;
        }
    }
}
