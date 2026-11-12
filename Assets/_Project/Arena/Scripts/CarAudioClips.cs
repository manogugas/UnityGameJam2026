using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Clip-based stand-in for CarAudio's procedural filter on the web build (no OnAudioFilterRead there).
    /// Plays EngineClipBank loops: two neighbouring rpm steps x off/on throttle, crossfaded and pitched to the
    /// exact rpm, plus squeal / gravel / steam loops scaled by CarAudio's values. Voices are split by rpm-step
    /// parity, so a voice only changes clip while its weight is zero (no clicks). Added by CarAudio.
    /// </summary>
    public class CarAudioClips : MonoBehaviour
    {
        public CarAudio carAudio;

        EngineClipBank.Bank bank;
        readonly AudioSource[,] engine = new AudioSource[2, 2]; // [step parity, load]
        readonly int[] voiceStep = { -1, -1 };
        AudioSource squeal, gravel, steam;
        float loadSmooth, squealSmooth, gravelSmooth, steamSmooth;

        void Start()
        {
            bank = EngineClipBank.Get(carAudio.engineLayout, carAudio.limiterRpm);
            for (int p = 0; p < 2; p++)
                for (int l = 0; l < 2; l++)
                    engine[p, l] = Voice($"Engine{p}{l}");
            squeal = Voice("Squeal");
            gravel = Voice("Gravel");
            steam = Voice("Steam");
        }

        AudioSource Voice(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.loop = true;
            s.playOnAwake = false;
            s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.volume = 0f;
            return s;
        }

        void Update()
        {
            if (bank == null || carAudio == null || carAudio.car == null) return;
            if (!bank.Ready) { EngineClipBank.Step(bank); return; }
            var car = carAudio.car;
            float dt = Time.deltaTime;

            float baseVol = carAudio.volume * carAudio.GainNow * WebAudio.Volume(GameSettings.Channel.Car);
            float rpm = Mathf.Clamp(car.Rpm, EngineClipBank.Rpms[0] * 0.8f, EngineClipBank.Rpms[EngineClipBank.Rpms.Length - 1] * 1.08f);
            loadSmooth = Mathf.MoveTowards(loadSmooth, car.ThrottleOut, dt * 4f);

            // The two rpm steps either side of the current rpm and the blend between them.
            var steps = EngineClipBank.Rpms;
            int lo = 0;
            while (lo < steps.Length - 2 && rpm > steps[lo + 1]) lo++;
            float t = Mathf.Clamp01(Mathf.InverseLerp(steps[lo], steps[lo + 1], rpm));
            float wOn = Mathf.Sqrt(loadSmooth), wOff = Mathf.Sqrt(1f - loadSmooth);
            for (int p = 0; p < 2; p++)
            {
                int step = lo % 2 == p ? lo : lo + 1;
                float wStep = step == lo ? Mathf.Sqrt(1f - t) : Mathf.Sqrt(t);
                bool swap = voiceStep[p] != step;
                voiceStep[p] = step;
                for (int l = 0; l < 2; l++)
                {
                    var v = engine[p, l];
                    if (swap || v.clip == null)
                    {
                        v.clip = bank.engine[step, l];
                        v.time = Random.Range(0f, v.clip.length * 0.9f);
                    }
                    Apply(v, wStep * (l == 1 ? wOn : wOff) * carAudio.engineVolume * baseVol, rpm / steps[step]);
                }
            }

            // Smoothed like the filter's per-sample amplitudes, so the layers fade instead of popping.
            float k = 1f - Mathf.Exp(-dt * 12f);
            squealSmooth = Mathf.Lerp(squealSmooth, carAudio.Squeal, k);
            gravelSmooth = Mathf.Lerp(gravelSmooth, carAudio.Gravel, k);
            steamSmooth = Mathf.Lerp(steamSmooth, carAudio.Steam, 1f - Mathf.Exp(-dt * 4f));
            Apply(squeal, bank.squeal, squealSmooth * baseVol, (640f + 160f * squealSmooth) / 720f);
            Apply(gravel, bank.gravel, gravelSmooth * baseVol, 1f);
            Apply(steam, bank.steam, steamSmooth * baseVol, 1f);
        }

        void Apply(AudioSource s, AudioClip clip, float volume, float pitch)
        {
            if (s.clip != clip) s.clip = clip;
            Apply(s, volume, pitch);
        }

        void Apply(AudioSource s, float volume, float pitch)
        {
            s.volume = volume;
            s.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
            bool spatial = carAudio.spatial;
            s.spatialBlend = spatial ? 1f : 0f;
            s.minDistance = carAudio.spatialMinDistance;
            s.maxDistance = carAudio.spatialMaxDistance;
            bool audible = volume > 0.0005f && s.clip != null;
            if (audible && !s.isPlaying) s.Play();
            else if (!audible && s.isPlaying) s.Pause();
        }
    }
}
