using UnityEngine;

namespace CarArena
{
    public enum EngineLayout { Inline4, V6, V8Crossplane }

    /// <summary>
    /// Procedural combustion engine sound (audio thread, no allocations).
    /// The crank turns through a 720 degree cycle; every cylinder fires at its angle in the firing order,
    /// creating a short pressure pulse. Pulses go into the exhaust of their bank, modelled as two
    /// waveguides (header + tail pipe: delay lines with a reflection), then a muffler low-pass.
    /// Uneven firing per bank (cross-plane V8) is what gives the burble.
    /// </summary>
    public class EngineSynth
    {
        class Waveguide
        {
            readonly float[] buf;
            readonly int delay;
            readonly float reflection, damping;
            int index;
            float lp;

            public Waveguide(int delaySamples, float reflection, float damping)
            {
                delay = Mathf.Max(2, delaySamples);
                buf = new float[delay + 1];
                this.reflection = reflection;
                this.damping = damping;
            }

            public float Process(float x)
            {
                int read = index - delay;
                if (read < 0) read += buf.Length;
                lp += (buf[read] - lp) * damping;
                float y = x + reflection * lp;
                buf[index] = y;
                if (++index >= buf.Length) index = 0;
                return y;
            }
        }

        const int Slots = 12;
        const float BodyGain = 2.2f;

        readonly float sampleRate;
        readonly float[] fireAngles;
        readonly int[] fireBank;
        readonly float[] cylinderGain;
        readonly Waveguide[] header = new Waveguide[2];
        readonly Waveguide[] tailPipe = new Waveguide[2];
        readonly float[] pulseAge = new float[Slots];
        readonly float[] pulseAmp = new float[Slots];
        readonly int[] pulseBank = new int[Slots];
        readonly float pulseTime;

        double crank;
        int nextFire, nextSlot;
        uint rng;
        float mufflerL, mufflerR, dcInL, dcOutL, dcInR, dcOutR, intake1, intake2, intakeEnv, body1, body2;

        public EngineLayout Layout { get; }

        public EngineSynth(EngineLayout layout, int sampleRate, uint seed = 0x9E3779B9u)
        {
            Layout = layout;
            this.sampleRate = sampleRate;
            rng = seed == 0 ? 1u : seed;

            switch (layout)
            {
                case EngineLayout.Inline4:
                    // Even 180 degree firing, one exhaust.
                    fireAngles = new[] { 0f, 180f, 360f, 540f };
                    fireBank = new[] { 0, 0, 0, 0 };
                    break;
                case EngineLayout.V6:
                    // 120 degree spacing, alternating banks.
                    fireAngles = new[] { 0f, 120f, 240f, 360f, 480f, 600f };
                    fireBank = new[] { 0, 1, 0, 1, 0, 1 };
                    break;
                default:
                    // Cross-plane V8, firing order 1-8-4-3-6-5-7-2: 90 degree spacing but each bank
                    // fires unevenly (L R L L R L R R), which is where the rumble comes from.
                    fireAngles = new[] { 0f, 90f, 180f, 270f, 360f, 450f, 540f, 630f };
                    fireBank = new[] { 0, 1, 0, 0, 1, 0, 1, 1 };
                    break;
            }

            cylinderGain = new float[fireAngles.Length];
            for (int i = 0; i < cylinderGain.Length; i++) cylinderGain[i] = 0.85f + 0.3f * Rand01();

            pulseTime = 0.0006f;
            // Slightly different pipe lengths per bank so the two sides don't sound identical.
            header[0] = new Waveguide(Mathf.RoundToInt(sampleRate * 0.0017f), -0.55f, 0.55f);
            header[1] = new Waveguide(Mathf.RoundToInt(sampleRate * 0.0019f), -0.55f, 0.55f);
            tailPipe[0] = new Waveguide(Mathf.RoundToInt(sampleRate * 0.0064f), 0.45f, 0.3f);
            tailPipe[1] = new Waveguide(Mathf.RoundToInt(sampleRate * 0.0071f), 0.45f, 0.3f);
            for (int i = 0; i < Slots; i++) pulseAge[i] = 1f;
        }

        float Rand01()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 16777215f;
        }

        float RandSigned() => Rand01() * 2f - 1f;

        /// <summary>Render one sample. rpm = crank speed, load = throttle 0..1, redline = limiter rpm.</summary>
        public void Render(float rpm, float load, float redline, out float left, out float right)
        {
            // Crank advance (degrees per sample) and firing events.
            crank += rpm / 60.0 * 360.0 / sampleRate;
            while (true)
            {
                double target = nextFire < fireAngles.Length ? fireAngles[nextFire] : 720.0;
                if (crank < target) break;
                if (nextFire < fireAngles.Length) { Fire(nextFire, rpm, load, redline); nextFire++; }
                else { crank -= 720.0; nextFire = 0; }
            }

            // Combustion pressure pulses (alpha function x*e^(1-x), roughened with noise).
            float e0 = 0f, e1 = 0f;
            float invTau = 1f / pulseTime, dt = 1f / sampleRate;
            for (int i = 0; i < Slots; i++)
            {
                float age = pulseAge[i];
                if (age >= pulseTime * 10f) continue;
                float x = age * invTau;
                float v = pulseAmp[i] * x * Mathf.Exp(1f - x) * (0.75f + 0.5f * RandSigned());
                if (pulseBank[i] == 0) e0 += v; else e1 += v;
                pulseAge[i] = age + dt;
            }
            if (Layout == EngineLayout.Inline4) e1 = e0;

            // Exhaust: header resonance, then tail pipe.
            float l = tailPipe[0].Process(header[0].Process(e0));
            float r = tailPipe[1].Process(header[1].Process(e1));

            // Muffler: opens up with throttle.
            float cutoff = 700f + 1600f * load + rpm * 0.08f;
            float k = 1f - Mathf.Exp(-6.2831853f * cutoff / sampleRate);
            mufflerL += (l - mufflerL) * k;
            mufflerR += (r - mufflerR) * k;

            // Intake: band-limited noise that breathes with the firing pulses and throttle.
            intakeEnv += ((Mathf.Abs(e0) + Mathf.Abs(e1)) - intakeEnv) * 0.01f;
            float n = RandSigned();
            intake1 += (n - intake1) * 0.18f;
            intake2 += (intake1 - intake2) * 0.18f;
            float intake = (intake1 - intake2) * intakeEnv * load * 0.6f;

            // Structure-borne rumble: what the driver feels through the car body (two-pole low-pass ~110 Hz).
            float kb = 1f - Mathf.Exp(-6.2831853f * 110f / sampleRate);
            body1 += ((e0 + e1) - body1) * kb;
            body2 += (body1 - body2) * kb;
            float body = body2 * BodyGain;

            // DC blockers and gentle saturation for growl.
            float outL = mufflerL + intake + body, outR = mufflerR + intake + body;
            dcOutL = outL - dcInL + 0.995f * dcOutL; dcInL = outL;
            dcOutR = outR - dcInR + 0.995f * dcOutR; dcInR = outR;
            left = Saturate(dcOutL * 1.2f);
            right = Saturate(dcOutR * 1.2f);
        }

        static float Saturate(float x)
        {
            // Cheap tanh-like soft clip.
            if (x > 3f) return 1f;
            if (x < -3f) return -1f;
            float x2 = x * x;
            return x * (27f + x2) / (27f + 9f * x2);
        }

        void Fire(int cylinder, float rpm, float load, float redline)
        {
            float amp;
            bool overrun = load < 0.05f && rpm > 2200f;
            if (load > 0.3f && rpm > redline - 150f && Rand01() < 0.7f)
                return; // rev limiter: skipped combustion
            if (overrun)
            {
                // Fuel cut: weak pulses, with the occasional unburnt-fuel pop in the exhaust.
                amp = Rand01() < 0.05f ? 1.1f + Rand01() * 0.9f : 0.12f;
            }
            else
            {
                // Low load = lean, slightly irregular; the idle gets a lumpier lope.
                float irregular = rpm < 1300f ? 0.35f : 0.12f;
                amp = Mathf.Lerp(0.38f, 1f, load) * (1f - irregular * 0.5f + irregular * Rand01());
            }

            int slot = nextSlot;
            nextSlot = (nextSlot + 1) % Slots;
            pulseAge[slot] = 0f;
            pulseAmp[slot] = amp * cylinderGain[cylinder];
            pulseBank[slot] = fireBank[cylinder];
        }
    }
}
