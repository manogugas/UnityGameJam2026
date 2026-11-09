using System.Collections.Generic;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// A burning barrel the crowd throws at cars that bunch up. A red warning ring flashes where it will
    /// land while the barrel arcs in from the stands; on landing it bursts (damage and a shove, falling off
    /// with distance) and leaves a fire patch that burns anything parked in it. Spawned by ArenaObjectives.
    /// </summary>
    public class CrowdHazard : MonoBehaviour
    {
        public enum Phase { Incoming, Burning, Done }

        public Transform projectile;
        public Transform warningRing;
        public Renderer warningRenderer;
        public ParticleSystem fire;
        public ParticleSystem blast;
        public Light glow;
        public AudioSource audioSource;
        public AudioClip blastClip;

        [Header("Timing")]
        public float warningTime = 1.9f;
        public float arcHeight = 16f;
        public float fireDuration = 7f;

        [Header("Blast")]
        public float blastRadius = 7f;
        public float blastDamage = 20f;
        [Tooltip("Impulse (N s) at the centre.")]
        public float blastImpulse = 9000f;

        [Header("Fire")]
        public float fireRadius = 4.5f;
        public float fireDamagePerSecond = 7f;

        public ArenaObjectives Owner { get; set; }
        /// <summary>World point the barrel is thrown from (set before the first frame).</summary>
        public Vector3 LaunchFrom { get; set; }
        public Phase CurrentPhase => phase;
        /// <summary>Radius cars should stay out of right now.</summary>
        public float DangerRadius => phase == Phase.Incoming ? blastRadius : phase == Phase.Burning ? fireRadius : 0f;

        Phase phase = Phase.Incoming;
        float t, tick, ringHeight = 0.005f;
        readonly Collider[] overlap = new Collider[48];
        readonly HashSet<Rigidbody> seen = new HashSet<Rigidbody>();

        void Start()
        {
            if (projectile != null) projectile.position = LaunchFrom;
            if (warningRing != null) ringHeight = warningRing.localScale.y;
            if (fire != null) fire.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (glow != null) glow.intensity = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            switch (phase)
            {
                case Phase.Incoming:
                {
                    float k = Mathf.Clamp01(t / warningTime);
                    if (projectile != null)
                    {
                        Vector3 p = Vector3.Lerp(LaunchFrom, transform.position, k);
                        p.y += Mathf.Sin(k * Mathf.PI) * arcHeight;
                        projectile.position = p;
                        projectile.Rotate(300f * dt, 120f * dt, 0f, Space.Self);
                    }
                    if (warningRing != null)
                    {
                        float pulse = 0.85f + Mathf.Sin(t * (10f + k * 18f)) * 0.15f;
                        warningRing.localScale = new Vector3(blastRadius * 2f * pulse, ringHeight, blastRadius * 2f * pulse);
                        if (warningRenderer != null) warningRenderer.enabled = Mathf.Repeat(t * (3f + k * 8f), 1f) < 0.65f;
                    }
                    if (k >= 1f) Detonate();
                    break;
                }
                case Phase.Burning:
                {
                    tick -= dt;
                    if (tick <= 0f)
                    {
                        tick = 0.25f;
                        Burn(0.25f);
                    }
                    if (glow != null) glow.intensity = Mathf.Lerp(0f, 6f, Mathf.Clamp01((fireDuration - t) / 1.5f)) * (0.85f + Mathf.PerlinNoise(t * 6f, 0f) * 0.3f);
                    if (t >= fireDuration)
                    {
                        phase = Phase.Done;
                        t = 0f;
                        if (fire != null) fire.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    }
                    break;
                }
                case Phase.Done:
                    if (glow != null) glow.intensity = Mathf.MoveTowards(glow.intensity, 0f, dt * 6f);
                    if (t > 3f) Destroy(gameObject);
                    break;
            }
        }

        void Detonate()
        {
            phase = Phase.Burning;
            t = 0f;
            if (projectile != null) projectile.gameObject.SetActive(false);
            if (warningRing != null) warningRing.gameObject.SetActive(false);
            if (blast != null) blast.Play(true);
            if (fire != null) fire.Play(true);
            if (audioSource != null && blastClip != null) audioSource.PlayOneShot(blastClip);
            Owner?.HazardLanded(this);

            Vector3 c = transform.position;
            foreach (var rb in CarsWithin(blastRadius))
            {
                float d = Vector3.Distance(rb.worldCenterOfMass, c);
                float falloff = Mathf.Clamp01(1f - d / blastRadius);
                rb.AddExplosionForce(blastImpulse * (0.3f + 0.7f * falloff), c - Vector3.up * 0.5f, blastRadius * 1.3f, 1.2f, ForceMode.Impulse);
                var health = rb.GetComponent<CarHealth>();
                if (health != null) health.ApplyDamage(blastDamage * falloff, rb.worldCenterOfMass, null);
                var dmg = rb.GetComponent<CarDamage>();
                if (dmg != null) dmg.ExternalBump(rb.worldCenterOfMass - c, 0.3f + 0.7f * falloff);
            }
        }

        void Burn(float seconds)
        {
            foreach (var rb in CarsWithin(fireRadius))
            {
                var health = rb.GetComponent<CarHealth>();
                if (health != null) health.ApplyDamage(fireDamagePerSecond * seconds, rb.worldCenterOfMass, null);
            }
        }

        List<Rigidbody> CarsWithin(float radius)
        {
            var result = new List<Rigidbody>();
            seen.Clear();
            int n = Physics.OverlapSphereNonAlloc(transform.position, radius, overlap, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var rb = overlap[i].attachedRigidbody;
                if (rb == null || !seen.Add(rb)) continue;
                if (rb.GetComponent<CarHealth>() == null) continue;
                result.Add(rb);
            }
            return result;
        }
    }
}
