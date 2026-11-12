using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarArena
{
    /// <summary>
    /// Hood-mounted machine gun. Hitscan rounds with a little spread and a gentle aim assist toward cars in
    /// front; a magazine that refills on its own when you stop shooting (so you can't hold the trigger
    /// forever). Hits damage CarHealth with kill credit, knock loose debris about, and throw sparks.
    /// Player: left mouse button or Left Ctrl. AI: SetTrigger().
    /// </summary>
    public class CarGun : MonoBehaviour
    {
        public CarController car;
        public CarHealth health;
        [Tooltip("Barrel tip: rounds leave along its +Z.")]
        public Transform muzzle;
        public bool playerControlled;

        [Header("Ballistics")]
        public float fireRate = 10f;
        public float damage = 1.4f;
        [Tooltip("Damage multiplier for AI gunners (keeps them fair).")]
        public float aiDamageScale = 0.6f;
        public float range = 75f;
        [Tooltip("Random spread cone (degrees).")]
        public float spread = 0.8f;
        [Tooltip("Rounds bend toward a car within this many degrees of the barrel.")]
        public float aimAssist = 6f;
        public float impulse = 60f;

        [Header("Ammo")]
        public int magazine = 60;
        [Tooltip("Seconds without firing before the magazine starts refilling.")]
        public float refillDelay = 1.5f;
        public float refillPerSecond = 6f;

        [Header("Race rules")]
        [Tooltip("In races the gun is a nuisance, not a race-winner: damage and magazine are scaled down.")]
        public float raceDamageScale = 0.3f;
        public float raceAmmoScale = 0.35f;
        public float raceRefillScale = 0.5f;

        [Header("Effects")]
        public LineRenderer tracer;
        public Light flash;
        public Renderer flashRenderer;
        public ParticleSystem sparks;
        [Range(0f, 1f)] public float volume = 0.55f;

        public float Ammo { get; private set; }
        public float Ammo01 => MagazineSize > 0f ? Ammo / MagazineSize : 0f;
        public bool RaceRules { get; private set; }
        float MagazineSize => RaceRules ? Mathf.Max(1f, Mathf.Round(magazine * raceAmmoScale)) : magazine;

        /// <summary>Switch to race rules (weak gun, small magazine) or back to arena rules; refills the gun.</summary>
        public void SetRaceRules(bool race)
        {
            RaceRules = race;
            Ammo = MagazineSize;
        }
        public bool Firing { get; private set; }

        bool aiTrigger;
        float cooldown, sinceShot = 10f, effectT;
        AudioSource audioSource;
        AudioClip shotClip;
        CarInput input;
        ArenaDirector director;
        readonly RaycastHit[] hits = new RaycastHit[16];
        static readonly List<CarHealth> scratch = new List<CarHealth>();

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            if (health == null) health = GetComponent<CarHealth>();
            input = GetComponent<CarInput>();
            Ammo = magazine;
            // Own child object: the car root's AudioSource carries the procedural engine (OnAudioFilterRead),
            // and a second source on the same object would share/garble that filter.
            var audioGo = new GameObject("GunAudio");
            audioGo.transform.SetParent(muzzle != null ? muzzle : transform, false);
            audioSource = audioGo.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            GameSettings.Route(audioSource, GameSettings.Channel.Effects);
            audioSource.spatialBlend = playerControlled ? 0.2f : 1f;
            audioSource.minDistance = 5f;
            audioSource.maxDistance = 120f;
            shotClip = MakeShotClip();
            if (tracer != null) tracer.enabled = false;
            if (flash != null) flash.enabled = false;
            if (flashRenderer != null) flashRenderer.enabled = false;
        }

        void Start() => director = FindAnyObjectByType<ArenaDirector>();

        /// <summary>AI: hold or release the trigger.</summary>
        public void SetTrigger(bool on) => aiTrigger = on;

        /// <summary>False until the player buys the gun upgrade: can't fire, and the gun isn't bolted on.</summary>
        public bool Unlocked { get; private set; } = true;

        public void SetUnlocked(bool on)
        {
            Unlocked = on;
            if (!on) Firing = false;
            // The mount (muzzle's parent) is the visible gun on the bonnet.
            if (muzzle != null && muzzle.parent != null && muzzle.parent != transform) muzzle.parent.gameObject.SetActive(on);
        }

        /// <summary>Full magazine (back in the garage).</summary>
        public void Reload() => Ammo = MagazineSize;

        void Update()
        {
            float dt = Time.deltaTime;
            bool trigger = playerControlled ? PlayerTrigger() : aiTrigger;
            bool canFire = Unlocked && (health == null || !health.IsDead);
            if (input != null && input.overrideDriving && playerControlled) canFire = false; // garage, intro, grid

            cooldown -= dt;
            Firing = trigger && canFire && Ammo >= 1f;
            if (Firing && cooldown <= 0f)
            {
                cooldown += 1f / fireRate;
                if (cooldown < 0f) cooldown = 0f;
                Ammo -= 1f;
                sinceShot = 0f;
                Fire();
            }
            else if (!Firing)
            {
                cooldown = Mathf.Max(cooldown, 0f);
                sinceShot += dt;
                if (sinceShot > refillDelay) Ammo = Mathf.Min(MagazineSize, Ammo + refillPerSecond * (RaceRules ? raceRefillScale : 1f) * dt);
            }

            effectT -= dt;
            bool show = effectT > 0f;
            if (tracer != null && tracer.enabled != show) tracer.enabled = show;
            if (flash != null && flash.enabled != show) flash.enabled = show;
            if (flashRenderer != null && flashRenderer.enabled != show) flashRenderer.enabled = show;
        }

        static bool PlayerTrigger()
        {
            var m = Mouse.current;
            var k = Keyboard.current;
            bool mouse = m != null && m.leftButton.isPressed && Cursor.lockState == CursorLockMode.Locked;
            bool key = k != null && k.leftCtrlKey.isPressed;
            return mouse || key;
        }

        void Fire()
        {
            if (muzzle == null) return;
            Vector3 origin = muzzle.position;
            Vector3 dir = AssistedDirection(origin, muzzle.forward);
            dir = Quaternion.AngleAxis(Random.Range(-spread, spread), muzzle.up) * Quaternion.AngleAxis(Random.Range(-spread, spread) * 0.5f, muzzle.right) * dir;

            Vector3 end = origin + dir * range;
            int n = Physics.RaycastNonAlloc(origin, dir, hits, range, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            int bi = -1;
            for (int i = 0; i < n; i++)
            {
                var rb = hits[i].collider.attachedRigidbody;
                if (car != null && rb == car.Body) continue;
                if (hits[i].distance < best) { best = hits[i].distance; bi = i; }
            }
            if (bi >= 0)
            {
                var h = hits[bi];
                end = h.point;
                var rb = h.collider.attachedRigidbody;
                if (rb != null)
                {
                    rb.AddForceAtPosition(dir * impulse, h.point, ForceMode.Impulse);
                    var victim = rb.GetComponent<CarHealth>();
                    if (victim != null && victim != health && !victim.IsDead)
                        victim.ApplyDamage(damage * (playerControlled ? 1f : aiDamageScale) * (RaceRules ? raceDamageScale : 1f), h.point, health);
                }
                if (sparks != null)
                {
                    sparks.transform.SetPositionAndRotation(h.point, Quaternion.LookRotation(h.normal));
                    sparks.Emit(6);
                }
            }

            if (tracer != null)
            {
                tracer.SetPosition(0, origin);
                tracer.SetPosition(1, Vector3.Lerp(origin, end, Random.Range(0.55f, 1f)));
            }
            effectT = 0.035f;
            if (audioSource != null && shotClip != null)
            {
                audioSource.pitch = Random.Range(0.92f, 1.08f);
                audioSource.PlayOneShot(shotClip, volume * WebAudio.Volume(GameSettings.Channel.Effects));
            }
        }

        /// <summary>Bend the shot toward the car closest to the barrel's line, if it's within the assist cone.</summary>
        Vector3 AssistedDirection(Vector3 origin, Vector3 forward)
        {
            if (director == null || aimAssist <= 0f) return forward;
            float bestAngle = aimAssist;
            Vector3 best = forward;
            foreach (var c in director.Combatants)
            {
                if (c == null || c == health || c.IsDead) continue;
                Vector3 to = c.transform.position + Vector3.up * 0.2f - origin;
                if (to.magnitude > range) continue;
                float a = Vector3.Angle(forward, to);
                if (a < bestAngle) { bestAngle = a; best = to.normalized; }
            }
            return best;
        }

        /// <summary>Short punchy report: noise burst through a falling low-pass, plus a thump.</summary>
        static AudioClip MakeShotClip()
        {
            const int rate = 22050;
            int n = (int)(rate * 0.12f);
            var data = new float[n];
            var rng = new System.Random(5);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-t * 38f);
                float cutoff = Mathf.Lerp(0.6f, 0.08f, t / 0.12f);
                lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * cutoff;
                float thump = Mathf.Sin(2f * Mathf.PI * 90f * t) * Mathf.Exp(-t * 30f);
                data[i] = (lp * 0.9f + thump * 0.7f) * env;
            }
            var clip = AudioClip.Create("GunShot", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
