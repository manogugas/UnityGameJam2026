using System;
using UnityEngine;
using UnityEngine.Audio;

namespace CarArena
{
    /// <summary>
    /// Player options (Esc menu > SETTINGS), kept in PlayerPrefs.
    /// Volumes go through the game's Audio Mixer (Resources/GameMixer: Master > Car, Effects, Crowd). Slider
    /// values 0..1 become decibels with Log10(v) * 20 on the exposed MasterVol / CarVol / EffectsVol / CrowdVol.
    /// Every AudioSource in the game routes itself into one of the groups with <see cref="Route"/>.
    /// Camera shake and speed effects scale CarCamera / ChaseCamera / SpeedEffects directly.
    /// </summary>
    public static class GameSettings
    {
        public enum Channel { Car, Effects, Crowd }

        public const float MinVolume = 0.0001f; // -80 dB: Log10(0) would be -infinity

        public static float Master = 1f;
        public const float DefaultCar = 0.4f; // engine and tyres are loud next to everything else
        public static float Car = DefaultCar;
        public static float Effects = 1f;
        public static float Crowd = 1f;
        /// <summary>Impact shake, head jolt and road rumble.</summary>
        public static float CameraShake = 1f;
        /// <summary>Speed FOV widening, speed lines and vignette.</summary>
        public static float SpeedEffects = 1f;

        public static event Action Changed;

        const string Prefix = "CarArena.";
        static AudioMixer mixer;
        static AudioMixerGroup[] groups;

        public static AudioMixer Mixer
        {
            get
            {
                if (mixer == null) mixer = Resources.Load<AudioMixer>("GameMixer");
                return mixer;
            }
        }

        /// <summary>The mixer group for a channel (null if the mixer is missing).</summary>
        public static AudioMixerGroup Group(Channel channel)
        {
            if (Mixer == null) return null;
            if (groups == null)
            {
                groups = new AudioMixerGroup[3];
                foreach (var g in Mixer.FindMatchingGroups(string.Empty))
                    for (int i = 0; i < groups.Length; i++)
                        if (g.name == ((Channel)i).ToString()) groups[i] = g;
            }
            return groups[(int)channel];
        }

        /// <summary>Send a source through the mixer group for its channel.</summary>
        public static void Route(AudioSource source, Channel channel)
        {
            if (source != null) source.outputAudioMixerGroup = Group(channel);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() // domain reload is off
        {
            mixer = null;
            groups = null;
            Changed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() => Load();

        public static void Load()
        {
            Master = PlayerPrefs.GetFloat(Prefix + "Master", 1f);
            Car = PlayerPrefs.GetFloat(Prefix + "Car", DefaultCar);
            Effects = PlayerPrefs.GetFloat(Prefix + "Effects", 1f);
            Crowd = PlayerPrefs.GetFloat(Prefix + "Crowd", 1f);
            CameraShake = PlayerPrefs.GetFloat(Prefix + "CameraShake", 1f);
            SpeedEffects = PlayerPrefs.GetFloat(Prefix + "SpeedEffects", 1f);
            Apply();
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat(Prefix + "Master", Master);
            PlayerPrefs.SetFloat(Prefix + "Car", Car);
            PlayerPrefs.SetFloat(Prefix + "Effects", Effects);
            PlayerPrefs.SetFloat(Prefix + "Crowd", Crowd);
            PlayerPrefs.SetFloat(Prefix + "CameraShake", CameraShake);
            PlayerPrefs.SetFloat(Prefix + "SpeedEffects", SpeedEffects);
            PlayerPrefs.Save();
        }

        public static void ResetDefaults()
        {
            Master = Effects = Crowd = CameraShake = SpeedEffects = 1f;
            Car = DefaultCar;
            Apply();
        }

        /// <summary>Slider value (0..1) to mixer decibels.</summary>
        public static float ToDecibels(float value) => Mathf.Log10(Mathf.Clamp(value, MinVolume, 1f)) * 20f;

        /// <summary>Push the volumes into the mixer and tell listeners. (SetFloat is ignored in Awake, so the
        /// pause menu calls this again from Start.)</summary>
        public static void Apply()
        {
            var m = Mixer;
            if (m != null)
            {
                m.SetFloat("MasterVol", ToDecibels(Master));
                m.SetFloat("CarVol", ToDecibels(Car));
                m.SetFloat("EffectsVol", ToDecibels(Effects));
                m.SetFloat("CrowdVol", ToDecibels(Crowd));
            }
            Changed?.Invoke();
        }
    }
}
