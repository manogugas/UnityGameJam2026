using System.Collections.Generic;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// For platforms without OnAudioFilterRead (the web build): the procedural EngineSynth rendered offline into
    /// seamless loops at a few rpm steps, off and on the throttle, plus tyre squeal, gravel rumble and steam loops.
    /// CarAudioClips crossfades and pitches them by rpm. One bank per engine layout, shared by every car, built a
    /// clip at a time (Step) so the web build doesn't freeze while it renders.
    /// </summary>
    public static class EngineClipBank
    {
        public static readonly float[] Rpms = { 900f, 1800f, 2800f, 3900f, 5100f, 6400f };
        const float LoopSeconds = 1f, FadeSeconds = 0.15f, WarmupSeconds = 0.3f;

        public class Bank
        {
            public EngineLayout layout;
            public float limiter;
            /// <summary>[rpm step, 0 = off throttle / 1 = on]</summary>
            public AudioClip[,] engine = new AudioClip[Rpms.Length, 2];
            public AudioClip squeal, gravel, steam;
            public bool Ready => next >= Rpms.Length * 2 + 3;
            internal int next;
        }

        static readonly Dictionary<EngineLayout, Bank> banks = new Dictionary<EngineLayout, Bank>();

        public static void Clear() => banks.Clear();

        public static Bank Get(EngineLayout layout, float limiter)
        {
            if (!banks.TryGetValue(layout, out var bank)) banks[layout] = bank = new Bank { layout = layout, limiter = limiter };
            return bank;
        }

        /// <summary>Render the next missing clip. Returns true once everything is ready.</summary>
        public static bool Step(Bank bank)
        {
            if (bank.Ready) return true;
            int sr = AudioSettings.outputSampleRate;
            int i = bank.next++;
            int engineClips = Rpms.Length * 2;
            if (i < engineClips)
            {
                int step = i / 2, load = i % 2;
                var synth = new EngineSynth(bank.layout, sr, 0x9E3779B9u + (uint)i * 7919u);
                float rpm = Rpms[step];
                bank.engine[step, load] = Loop($"Engine_{bank.layout}_{rpm}_{load}", sr, () =>
                {
                    synth.Render(rpm, load, bank.limiter, out float l, out float r);
                    return (l + r) * 0.5f;
                });
            }
            else if (i == engineClips) bank.squeal = Loop("Squeal", sr, SquealSource(sr));
            else if (i == engineClips + 1) bank.gravel = Loop("Gravel", sr, GravelSource());
            else bank.steam = Loop("Steam", sr, SteamSource());
            return bank.Ready;
        }

        /// <summary>Render LoopSeconds of a sample source as a seamless loop: warm it up first, then crossfade the
        /// tail rendered past the end into the start.</summary>
        static AudioClip Loop(string name, int sr, System.Func<float> sample)
        {
            for (int k = 0, warm = (int)(WarmupSeconds * sr); k < warm; k++) sample();
            int len = (int)(LoopSeconds * sr), fade = (int)(FadeSeconds * sr);
            var data = new float[len];
            for (int k = 0; k < len; k++) data[k] = sample();
            for (int k = 0; k < fade; k++)
            {
                float t = (k + 0.5f) / fade;
                data[k] = data[k] * t + sample() * (1f - t);
            }
            for (int k = 0; k < len; k++) data[k] = Mathf.Clamp(data[k], -1f, 1f);
            var clip = AudioClip.Create(name, len, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        // Same recipes as CarAudio's filter, at full intensity (the player scales volume and pitch).
        static System.Func<float> SquealSource(int sr)
        {
            double phase = 0, vib = 0, dt = 1.0 / sr;
            return () =>
            {
                vib += 6.5 * dt; if (vib >= 1.0) vib -= 1.0;
                double f = 720.0 + 22.0 * System.Math.Sin(vib * 6.283185);
                phase += f * dt; if (phase >= 1.0) phase -= 1.0;
                float p = (float)(phase * 6.283185);
                return (Mathf.Sin(p) + 0.3f * Mathf.Sin(p * 2f + 0.6f)) * 0.14f;
            };
        }

        static System.Func<float> GravelSource()
        {
            uint rng = 2463534242u;
            float lp1 = 0f, lp2 = 0f;
            return () =>
            {
                rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                float n = (rng & 0xFFFFFF) / (float)0x7FFFFF - 1f;
                lp1 += (n - lp1) * 0.025f;
                lp2 += (lp1 - lp2) * 0.025f;
                return lp2 * 5f;
            };
        }

        static System.Func<float> SteamSource()
        {
            uint rng = 88172645u;
            float hp = 0f, prev = 0f, lp = 0f;
            return () =>
            {
                rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                float n = (rng & 0xFFFFFF) / (float)0x7FFFFF - 1f;
                hp = 0.85f * (hp + n - prev); prev = n;
                lp += (hp - lp) * 0.5f;
                return lp * 0.18f;
            };
        }
    }
}
