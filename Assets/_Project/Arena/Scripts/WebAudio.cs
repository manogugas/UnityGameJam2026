using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// The web build's audio is Web Audio underneath: no Audio Mixer, no audio filters (low-pass) and no
    /// OnAudioFilterRead, so the procedural engine and the cabin muffling don't exist there. When this is
    /// Active the game swaps to clip-based versions: pre-rendered engine loops (EngineClipBank / CarAudioClips),
    /// pre-muffled crowd loops, and the settings sliders applied straight to each source's volume.
    /// </summary>
    public static class WebAudio
    {
        /// <summary>Hear the web path in the editor (for testing).</summary>
        public static bool ForceInEditor;

        public static bool Active => ForceInEditor || Application.platform == RuntimePlatform.WebGLPlayer;

        /// <summary>The settings slider for a channel when there's no mixer to apply it (1 when the mixer does it).
        /// The mixer gets Log10(v) * 20 dB, which is an amplitude of exactly v, so this sounds the same.</summary>
        public static float Volume(GameSettings.Channel channel)
        {
            if (!Active) return 1f;
            switch (channel)
            {
                case GameSettings.Channel.Car: return GameSettings.Car;
                case GameSettings.Channel.Effects: return GameSettings.Effects;
                default: return GameSettings.Crowd;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => EngineClipBank.Clear();
    }
}
