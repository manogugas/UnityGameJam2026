using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Interface sounds: button clicks (two variations, never the same one twice in a row), the upgrade-bought
    /// sound and the "not enough money" sound. One 2D source on the Effects channel that keeps playing while the
    /// pause menu has paused all other audio.
    /// </summary>
    public class UiAudio : MonoBehaviour
    {
        public AudioClip clickA, clickB;
        public AudioClip upgrade;
        public AudioClip noMoney;
        [Range(0f, 1f)] public float clickVolume = 0.6f;
        [Range(0f, 1f)] public float upgradeVolume = 0.8f;
        [Range(0f, 1f)] public float noMoneyVolume = 0.8f;

        AudioSource source;
        bool lastWasA;

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true; // the pause menu pauses everything else
            GameSettings.Route(source, GameSettings.Channel.Effects);
        }

        public void Click()
        {
            // Alternate A / B with a little pitch spread so repeated clicks don't sound machine-gunned.
            var clip = lastWasA ? clickB : clickA;
            if (clip == null) clip = clickA != null ? clickA : clickB;
            lastWasA = !lastWasA;
            Play(clip, clickVolume, Random.Range(0.96f, 1.04f));
        }

        public void Upgrade() => Play(upgrade, upgradeVolume, 1f);

        public void NoMoney() => Play(noMoney, noMoneyVolume, 1f);

        void Play(AudioClip clip, float volume, float pitch)
        {
            if (clip == null || source == null) return;
            source.pitch = pitch;
            source.PlayOneShot(clip, volume * WebAudio.Volume(GameSettings.Channel.Effects));
        }
    }
}
