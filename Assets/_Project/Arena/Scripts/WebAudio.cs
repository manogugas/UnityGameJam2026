using UnityEngine;
using UnityEngine.InputSystem;

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

        /// <summary>
        /// Browsers keep audio suspended until the first click / key press on the page. Loops started before that
        /// come up with whatever level they had at the start (and a level that never changes afterwards may never be
        /// re-sent), so web loops wait for this before they start. Always true off the web.
        /// </summary>
        public static bool Unlocked
        {
            get
            {
                if (!Active || Application.isEditor) return true;
                if (!unlocked)
                {
                    var m = Mouse.current; var k = Keyboard.current; var t = Touchscreen.current; var g = Gamepad.current;
                    unlocked = (m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame)) ||
                               (k != null && k.anyKey.wasPressedThisFrame) ||
                               (t != null && t.primaryTouch.press.wasPressedThisFrame) ||
                               (g != null && g.startButton.wasPressedThisFrame);
                }
                return unlocked;
            }
        }

        static bool unlocked;

        /// <summary>
        /// Set a looping source's volume on the web, re-sending it every so often even when unchanged (a tiny
        /// alternating nudge), so the browser-side level can't stay stuck at a stale value.
        /// </summary>
        public static void SetVolume(AudioSource s, float volume)
        {
            if (s == null) return;
            if (Active && (Time.frameCount & 31) == 0) volume += (Time.frameCount & 32) == 0 ? 1e-4f : 0f;
            s.volume = volume;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            EngineClipBank.Clear();
            unlocked = false;
        }
    }
}
