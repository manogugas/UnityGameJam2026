using System;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Player options (Esc menu > SETTINGS), kept in PlayerPrefs. Volumes are 0..1 multipliers that the audio
    /// scripts read every frame; camera shake and speed effects scale CarCamera / ChaseCamera / SpeedEffects.
    /// Static on purpose: no scene object needed, and it's read from the audio thread too (plain floats only).
    /// </summary>
    public static class GameSettings
    {
        public static float Master = 1f;
        public static float Engine = 1f;
        public static float Effects = 1f;
        public static float Crowd = 1f;
        [Tooltip("Impact shake, head jolt and road rumble.")]
        public static float CameraShake = 1f;
        [Tooltip("Speed FOV widening, speed lines and vignette.")]
        public static float SpeedEffects = 1f;

        public static event Action Changed;

        const string Prefix = "CarArena.";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() => Load(); // domain reload is off: reload every play session

        public static void Load()
        {
            Master = PlayerPrefs.GetFloat(Prefix + "Master", 1f);
            Engine = PlayerPrefs.GetFloat(Prefix + "Engine", 1f);
            Effects = PlayerPrefs.GetFloat(Prefix + "Effects", 1f);
            Crowd = PlayerPrefs.GetFloat(Prefix + "Crowd", 1f);
            CameraShake = PlayerPrefs.GetFloat(Prefix + "CameraShake", 1f);
            SpeedEffects = PlayerPrefs.GetFloat(Prefix + "SpeedEffects", 1f);
            Apply();
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat(Prefix + "Master", Master);
            PlayerPrefs.SetFloat(Prefix + "Engine", Engine);
            PlayerPrefs.SetFloat(Prefix + "Effects", Effects);
            PlayerPrefs.SetFloat(Prefix + "Crowd", Crowd);
            PlayerPrefs.SetFloat(Prefix + "CameraShake", CameraShake);
            PlayerPrefs.SetFloat(Prefix + "SpeedEffects", SpeedEffects);
            PlayerPrefs.Save();
        }

        public static void ResetDefaults()
        {
            Master = Engine = Effects = Crowd = CameraShake = SpeedEffects = 1f;
            Apply();
        }

        /// <summary>Push the values out (master volume) and tell listeners.</summary>
        public static void Apply()
        {
            AudioListener.volume = Mathf.Clamp01(Master);
            Changed?.Invoke();
        }
    }
}
