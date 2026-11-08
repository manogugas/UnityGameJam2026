using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Procedural car-crash sound (audio thread, no allocations). Each impact layers:
    ///  - body thump: a low sine falling in pitch (the chassis taking the hit),
    ///  - metal clang: inharmonic decaying partials, like a struck sheet-metal panel,
    ///  - crunch: dense bursts of band-passed noise grains (metal folding, plastic cracking),
    ///  - glass: high, short pings scattered over half a second (big hits only),
    ///  - debris rattle: sparse clicks tailing off after the hit.
    /// Up to 4 impacts overlap. Trigger() is safe to call from the main thread.
    /// </summary>
    public class ImpactSynth
    {
        const int Voices = 4;
        const int Partials = 5;
        static readonly float[] Ratios = { 1f, 2.32f, 3.89f, 5.41f, 7.13f };

        class Voice
        {
            public bool active;
            public float age, severity;
            public float thumpFreq, thumpPhase;
            public readonly float[] freq = new float[Partials];
            public readonly float[] phase = new float[Partials];
            public readonly float[] decay = new float[Partials];
            public readonly float[] amp = new float[Partials];
            public float crunchLength, grainEnv, grainRate;
            public float glassTimer, glassEnv, glassFreq, glassPhase;
            public float rattleLength;
            // band-pass state for the crunch (two one-pole sections: high-pass then low-pass)
            public float hpIn, hpOut, lp;
        }

        readonly Voice[] voices = new Voice[Voices];
        readonly float sampleRate, dt;
        uint rng;
        volatile float pendingSeverity;
        volatile bool pending;
        int nextVoice;

        public ImpactSynth(int sampleRate, uint seed = 0xC0FFEEu)
        {
            this.sampleRate = sampleRate;
            dt = 1f / sampleRate;
            rng = seed == 0 ? 1u : seed;
            for (int i = 0; i < Voices; i++) voices[i] = new Voice();
        }

        /// <summary>severity 0..1 (from CarDamage).</summary>
        public void Trigger(float severity)
        {
            pendingSeverity = Mathf.Max(pending ? pendingSeverity : 0f, Mathf.Clamp01(severity));
            pending = true;
        }

        float Rand01() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777215f; }
        float RandSigned() => Rand01() * 2f - 1f;

        void Start(float s)
        {
            var v = voices[nextVoice];
            nextVoice = (nextVoice + 1) % Voices;
            v.active = true;
            v.age = 0f;
            v.severity = s;
            v.thumpFreq = Mathf.Lerp(95f, 60f, s) * (0.9f + 0.2f * Rand01());
            v.thumpPhase = 0f;
            // Bigger hits = bigger panels = lower ring.
            float f0 = Mathf.Lerp(320f, 170f, s) * (0.85f + 0.3f * Rand01());
            for (int i = 0; i < Partials; i++)
            {
                v.freq[i] = f0 * Ratios[i] * (0.97f + 0.06f * Rand01());
                v.phase[i] = Rand01();
                v.decay[i] = Mathf.Lerp(0.55f, 0.12f, i / (float)(Partials - 1)) * (0.6f + 0.6f * s);
                v.amp[i] = (0.5f + 0.5f * Rand01()) / (1f + i * 0.6f);
            }
            v.crunchLength = 0.12f + 0.45f * s;
            v.grainRate = Mathf.Lerp(60f, 220f, s);
            v.grainEnv = 0f;
            v.glassTimer = s > 0.35f ? 0.02f : -1f;
            v.glassEnv = 0f;
            v.rattleLength = 0.3f + 0.9f * s;
        }

        public float Render()
        {
            if (pending)
            {
                pending = false;
                Start(pendingSeverity);
            }

            float output = 0f;
            for (int vi = 0; vi < Voices; vi++)
            {
                var v = voices[vi];
                if (!v.active) continue;
                float t = v.age, s = v.severity;

                // Body thump: pitch drops ~an octave over 150 ms.
                float thumpEnv = Mathf.Exp(-t * 9f);
                float tf = v.thumpFreq * (0.55f + 0.45f * Mathf.Exp(-t * 14f));
                v.thumpPhase += tf * dt; if (v.thumpPhase > 1f) v.thumpPhase -= 1f;
                float thump = Mathf.Sin(v.thumpPhase * 6.2831853f) * thumpEnv * (0.5f + 0.7f * s);

                // Metal clang.
                float clang = 0f;
                for (int i = 0; i < Partials; i++)
                {
                    v.phase[i] += v.freq[i] * dt; if (v.phase[i] > 1f) v.phase[i] -= 1f;
                    clang += Mathf.Sin(v.phase[i] * 6.2831853f) * v.amp[i] * Mathf.Exp(-t / v.decay[i]);
                }
                clang *= 0.22f * (0.4f + s) * (1f - Mathf.Exp(-t * 900f)); // tiny attack softening

                // Crunch: noise grains, dense at first, thinning out.
                float crunch = 0f;
                if (t < v.crunchLength)
                {
                    float density = 1f - t / v.crunchLength;
                    if (Rand01() < v.grainRate * density * dt) v.grainEnv = 0.6f + 0.4f * Rand01();
                    v.grainEnv *= 0.9975f;
                    float n = RandSigned();
                    v.hpOut = 0.92f * (v.hpOut + n - v.hpIn); v.hpIn = n;   // high-pass ~ 600 Hz
                    v.lp += (v.hpOut - v.lp) * 0.45f;                        // low-pass ~ 4 kHz
                    crunch = v.lp * v.grainEnv * (0.35f + 0.65f * density) * (0.5f + 0.8f * s);
                }

                // Glass: scattered short high pings.
                float glass = 0f;
                if (v.glassTimer >= 0f)
                {
                    v.glassTimer -= dt;
                    if (v.glassTimer <= 0f && t < 0.6f)
                    {
                        v.glassEnv = 0.25f + 0.35f * Rand01();
                        v.glassFreq = 2800f + 3800f * Rand01();
                        v.glassTimer = 0.015f + 0.06f * Rand01() * (1f + t * 4f);
                    }
                    v.glassPhase += v.glassFreq * dt; if (v.glassPhase > 1f) v.glassPhase -= 1f;
                    v.glassEnv *= 0.9985f;
                    glass = Mathf.Sin(v.glassPhase * 6.2831853f) * v.glassEnv * 0.25f * s;
                }

                // Debris rattle: sparse clicks after the hit.
                float rattle = 0f;
                if (t > 0.08f && t < v.rattleLength && Rand01() < 40f * (1f - t / v.rattleLength) * dt)
                    rattle = RandSigned() * 0.25f * s;

                output += thump + clang + crunch + glass + rattle;
                v.age += dt;
                if (v.age > 1.6f) v.active = false;
            }
            return output;
        }
    }
}
