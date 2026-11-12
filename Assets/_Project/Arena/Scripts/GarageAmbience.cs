using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// The workshop's own sound: a generator hum that fades in while you're in the garage (tablet up) and out
    /// when you drive to the arena. The campaign mutes the crowd and the car at the same time.
    /// On the Crowd/ambience volume channel. Web: waits for the browser to unlock audio before it starts.
    /// </summary>
    public class GarageAmbience : MonoBehaviour
    {
        public AudioClip loop;
        [Range(0f, 1f)] public float volume = 0.2f; // about 80% of the old (muffled crowd) garage level
        public float fadeTime = 1.2f;

        AudioSource source;
        float level, target;

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.clip = loop;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            GameSettings.Route(source, GameSettings.Channel.Crowd);
        }

        /// <summary>Fade the hum in (in the garage) or out (leaving it).</summary>
        public void SetActive(bool on) => target = on ? 1f : 0f;

        void Update()
        {
            if (source == null || loop == null) return;
            level = Mathf.MoveTowards(level, target, Time.deltaTime / Mathf.Max(0.01f, fadeTime));
            WebAudio.SetVolume(source, level * volume * WebAudio.Volume(GameSettings.Channel.Crowd));
            bool audible = level > 0.001f && WebAudio.Unlocked;
            if (audible && !source.isPlaying) source.Play();
            else if (!audible && source.isPlaying) source.Pause();
        }
    }
}
