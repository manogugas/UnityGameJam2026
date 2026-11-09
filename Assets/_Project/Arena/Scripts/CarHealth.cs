using System;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Health from crash damage. Where you get hit matters: the nose is a crumple zone you attack with,
    /// the doors are weak. At 0 the engine dies, the car smokes and rolls to a stop.
    /// Kill credit goes to the last car that hit us within a few seconds.
    /// </summary>
    /// <summary>One chunk of damage: who took it, from whom (null = scenery) and where.</summary>
    public struct DamageEvent
    {
        public CarHealth victim, attacker;
        public float amount;
        public Vector3 point;
    }

    public class CarHealth : MonoBehaviour
    {
        public CarController car;
        public CarDamage damage;
        public bool isPlayer;
        public string displayName = "Car";

        [Header("Health")]
        public float maxHealth = 100f;
        [Tooltip("Health lost per unit of impact severity (0..1, from CarDamage) before hit-zone multipliers.")]
        public float damagePerSeverity = 65f;
        public float frontMultiplier = 0.3f;
        public float rearMultiplier = 0.85f;
        public float sideMultiplier = 1.15f;
        public float topMultiplier = 1f;
        [Tooltip("Damage multiplier for the car doing the ramming (moving into the other car faster).")]
        public float rammerMultiplier = 0.45f;
        [Tooltip("Damage multiplier for the car being rammed.")]
        public float rammedMultiplier = 1f;
        [Tooltip("Closing speed difference (m/s) that decides who is the rammer.")]
        public float rammingMargin = 1.5f;
        [Tooltip("Scales all damage this car takes (the player gets a small buffer).")]
        public float incomingDamageScale = 1f;
        [Tooltip("Extra damage this car deals when it rams another (ram plate upgrade).")]
        public float ramDamageDealt = 1f;
        [Tooltip("Ram damage multiplier while this car is boosted (boost pad, and a couple of seconds after).")]
        public float ramBoostBonus = 1.25f;
        /// <summary>Multiplier on damage this car deals by ramming right now.</summary>
        public float RamPower => ramDamageDealt * (car != null && car.RamBoosted ? ramBoostBonus : 1f);
        [Tooltip("The emperor has marked this car's number this match: everyone hunts it, and wrecking it pays extra.")]
        public bool marked;
        [Tooltip("Seconds a hit from another car still counts as the kill.")]
        public float killCreditTime = 5f;

        [Header("Smoke")]
        public ParticleSystem smoke;
        [Tooltip("Smoke starts below this health fraction.")]
        [Range(0f, 1f)] public float smokeBelow = 0.6f;
        public float maxSmokeRate = 28f;

        public float Health { get; private set; }
        public float Health01 => maxHealth > 0f ? Health / maxHealth : 0f;
        public bool IsDead { get; private set; }
        public int Kills { get; private set; }
        public float LastDamageTime { get; private set; } = -100f;

        /// <summary>(victim, killer or null)</summary>
        public event Action<CarHealth, CarHealth> Died;
        public event Action<DamageEvent> Damaged;

        CarHealth lastAttacker;
        float lastAttackTime = -100f;

        void Awake()
        {
            Health = maxHealth;
            if (car == null) car = GetComponent<CarController>();
            if (damage == null) damage = GetComponent<CarDamage>();
            UpdateSmoke();
        }

        void OnEnable()
        {
            if (damage != null)
            {
                damage.Impact += OnImpact;
                damage.Repaired += Restore;
            }
        }

        void OnDisable()
        {
            if (damage != null)
            {
                damage.Impact -= OnImpact;
                damage.Repaired -= Restore;
            }
        }

        void OnImpact(ImpactInfo info)
        {
            float zone = info.front ? frontMultiplier
                : info.rear ? rearMultiplier
                : (info.left || info.right) ? sideMultiplier
                : info.top ? topMultiplier : 1f;

            CarHealth attacker = info.otherBody != null ? info.otherBody.GetComponent<CarHealth>() : null;
            if (attacker == this) attacker = null;
            if (attacker != null)
            {
                lastAttacker = attacker;
                lastAttackTime = Time.time;
            }
            ApplyDamage(info.severity * damagePerSeverity * zone * RammingFactor(info, attacker) * incomingDamageScale,
                info.worldPoint, attacker);
        }

        /// <summary>
        /// Car-on-car hits: whoever was driving into the contact point faster is the rammer and takes much
        /// less damage; the one being rammed takes more. Scenery hits are unaffected.
        /// </summary>
        float RammingFactor(ImpactInfo info, CarHealth attacker)
        {
            if (info.otherBody == null || car == null || car.Body == null) return 1f;
            Vector3 toPoint = info.worldPoint - car.Body.worldCenterOfMass;
            toPoint.y = 0f;
            if (toPoint.sqrMagnitude < 0.01f) return 1f;
            toPoint.Normalize();
            float mine = Vector3.Dot(car.Body.linearVelocity, toPoint);
            float theirs = Vector3.Dot(info.otherBody.linearVelocity, -toPoint);
            if (mine > theirs + rammingMargin) return rammerMultiplier;
            if (theirs > mine + rammingMargin) return rammedMultiplier * (attacker != null ? attacker.RamPower : 1f);
            return 1f;
        }

        public void ApplyDamage(float amount) => ApplyDamage(amount, transform.position, null);

        public void ApplyDamage(float amount, Vector3 point, CarHealth attacker)
        {
            if (IsDead || amount <= 0f) return;
            amount = Mathf.Min(amount, Health);
            Health = Mathf.Max(0f, Health - amount);
            LastDamageTime = Time.time;
            if (Health <= 0f) Die();
            Damaged?.Invoke(new DamageEvent { victim = this, attacker = attacker, amount = amount, point = point });
            UpdateSmoke();
        }

        void Die()
        {
            IsDead = true;
            if (car != null) car.EngineDead = true;
            CarHealth killer = lastAttacker != null && Time.time - lastAttackTime <= killCreditTime ? lastAttacker : null;
            if (killer != null && !killer.IsDead) killer.Kills++;
            Died?.Invoke(this, killer);
        }

        /// <summary>Change max health (armour upgrades) and set the current health.</summary>
        public void SetMaxHealth(float max, float current)
        {
            maxHealth = Mathf.Max(1f, max);
            SetHealth(current);
        }

        /// <summary>Set health directly (loading a run, repairs, resetting after a lost match).</summary>
        public void SetHealth(float value)
        {
            Health = Mathf.Clamp(value, 0f, maxHealth);
            IsDead = Health <= 0f;
            if (car != null) car.EngineDead = IsDead;
            UpdateSmoke();
        }

        public void Restore()
        {
            Health = maxHealth;
            IsDead = false;
            if (car != null) car.EngineDead = false;
            lastAttacker = null;
            UpdateSmoke();
        }

        void UpdateSmoke()
        {
            if (smoke == null) return;
            var emission = smoke.emission;
            float t = IsDead ? 1f : Mathf.Clamp01(1f - Health01 / Mathf.Max(smokeBelow, 0.01f));
            emission.rateOverTime = t * maxSmokeRate;
            var main = smoke.main;
            float shade = Mathf.Lerp(0.55f, 0.08f, t);
            main.startColor = new Color(shade, shade, shade, Mathf.Lerp(0.35f, 0.8f, t));
            if (t > 0f && !smoke.isPlaying) smoke.Play();
        }
    }
}
