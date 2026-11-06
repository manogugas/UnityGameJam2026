using System;
using UnityEngine;

namespace HauntedCasino.Combat
{
    public enum Sfx { Hit, Jackpot, Bust, Coin, Block, Chip, Whoosh }

    /// <summary>
    /// Placeholder sound effects synthesised at startup, so the prototype needs no audio assets.
    /// Swap clips for real ones later by assigning them in the Inspector.
    /// </summary>
    public class CasinoSfx : MonoBehaviour
    {
        public static CasinoSfx Instance { get; private set; }

        [SerializeField, Range(0f, 1f)] float volume = 0.6f;
        [SerializeField] AudioClip tickClip, hitClip, jackpotClip, bustClip, coinClip, blockClip, chipClip, whooshClip;

        const int Rate = 44100;
        AudioSource oneShots, ticks;
        readonly System.Random rng = new System.Random(7);

        void Awake()
        {
            Instance = this;
            oneShots = gameObject.AddComponent<AudioSource>();
            oneShots.playOnAwake = false;
            ticks = gameObject.AddComponent<AudioSource>();
            ticks.playOnAwake = false;

            if (tickClip == null) tickClip = Make("Tick", 0.035f, t => (Noise() * 0.5f + Sine(3000f, t) * 0.5f) * Mathf.Exp(-t * 160f));
            if (hitClip == null) hitClip = Make("Hit", 0.28f, t =>
                (Mathf.Sin(2f * Mathf.PI * (130f * t - 120f * t * t)) * Mathf.Exp(-t * 14f)) + Noise() * Mathf.Exp(-t * 45f) * 0.6f);
            if (jackpotClip == null) jackpotClip = Make("Jackpot", 0.8f, Jackpot);
            if (bustClip == null) bustClip = Make("Bust", 0.5f, t =>
            {
                float phase = 320f * t - 250f * t * t;
                float saw = 2f * (phase - Mathf.Floor(phase)) - 1f;
                return saw * 0.45f * (1f - t / 0.5f);
            });
            if (coinClip == null) coinClip = Make("Coin", 0.4f, t => (Sine(1568f, t) + Sine(2093f, t) * 0.6f) * Mathf.Exp(-t * 9f) * 0.45f);
            if (blockClip == null) blockClip = Make("Block", 0.3f, t =>
                (Sine(620f, t) + Sine(930f, t) * 0.7f + Noise() * 0.3f) * Mathf.Exp(-t * 16f) * 0.5f);
            if (chipClip == null) chipClip = Make("Chip", 0.1f, t => Noise() * Mathf.Exp(-(t % 0.045f) * 220f) * 0.6f);
            if (whooshClip == null) whooshClip = Make("Whoosh", 0.3f, t => Noise() * Mathf.Sin(Mathf.PI * t / 0.3f) * 0.22f);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static void Tick(float pitch = 1f, float vol = 0.35f)
        {
            if (Instance == null) return;
            Instance.ticks.pitch = pitch;
            Instance.ticks.PlayOneShot(Instance.tickClip, Instance.volume * vol);
        }

        public static void Play(Sfx sfx, float pitch = 1f)
        {
            if (Instance == null) return;
            var s = Instance;
            AudioClip clip;
            switch (sfx)
            {
                case Sfx.Hit: clip = s.hitClip; break;
                case Sfx.Jackpot: clip = s.jackpotClip; break;
                case Sfx.Bust: clip = s.bustClip; break;
                case Sfx.Coin: clip = s.coinClip; break;
                case Sfx.Block: clip = s.blockClip; break;
                case Sfx.Chip: clip = s.chipClip; break;
                default: clip = s.whooshClip; break;
            }
            s.oneShots.pitch = pitch;
            s.oneShots.PlayOneShot(clip, s.volume);
        }

        static float Jackpot(float t)
        {
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
            const float noteLen = 0.11f;
            int idx = Mathf.Min(3, (int)(t / noteLen));
            float local = t - idx * noteLen;
            float s = Sine(notes[idx], t);
            float square = Mathf.Sign(s) * 0.25f + s * 0.4f;
            float decay = idx == 3 ? Mathf.Exp(-local * 3f) : Mathf.Exp(-local * 10f);
            return square * decay * 0.6f;
        }

        static float Sine(float freq, float t) => Mathf.Sin(2f * Mathf.PI * freq * t);

        float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);

        AudioClip Make(string name, float seconds, Func<float, float> wave)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(wave(i / (float)Rate), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
