using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Side objectives that feed the fight:
    ///  - Emperor's gifts: earned by fighting. After a big hit or a kill the emperor may toss a gold chest
    ///    near the brawl (rarely: one at a time, with a long cooldown). Any car can grab it: a small repair,
    ///    and the campaign pays the player a little cash.
    ///  - Boost pads: short pushes on the track straights, aimed at the crossing pit; rams hit harder for a
    ///    moment afterwards (BoostPad).
    /// Runs only while the ArenaDirector has a fight on; everything is cleared between matches.
    /// The AI reads gifts and pads from here.
    /// </summary>
    public class ArenaObjectives : MonoBehaviour
    {
        public ArenaDirector director;
        public ArenaCrowd crowd;

        [Header("Emperor's gifts")]
        public EmperorGift giftPrefab;
        [Tooltip("No gifts in the first seconds of a fight.")]
        public float firstGiftDelay = 20f;
        [Tooltip("At least this long between gifts.")]
        public float giftCooldown = 40f;
        [Tooltip("A single hit of at least this much damage between two cars can earn a gift (kills always can).")]
        public float bigHitDamage = 12f;
        [Range(0f, 1f)] public float bigHitGiftChance = 0.5f;
        public int maxGifts = 1;
        public float giftLifetime = 18f;
        [Tooltip("Health a gift restores to whoever grabs it.")]
        public float giftRepair = 10f;
        [Tooltip("Gifts land this far (min, max) from the hit that earned them.")]
        public Vector2 giftDistance = new Vector2(12f, 22f);
        [Tooltip("Gifts stay inside +- this many metres of the centre.")]
        public float spawnHalfExtent = 52f;
        [Tooltip("Optional: keeps gifts off the islands.")]
        public ArenaLayout layout;

        [Header("Boost pads")]
        public BoostPad[] pads;

        /// <summary>A car drove through a gift (after the repair was applied).</summary>
        public event Action<CarHealth> GiftCollected;

        public IReadOnlyList<EmperorGift> Gifts => gifts;
        public IReadOnlyList<BoostPad> Pads => pads;

        readonly List<EmperorGift> gifts = new List<EmperorGift>();
        readonly List<CarHealth> alive = new List<CarHealth>();
        readonly List<CarHealth> watched = new List<CarHealth>();
        bool running;
        float runStart, lastGift = -100f;

        void Update()
        {
            bool shouldRun = director != null && director.Running;
            if (shouldRun != running)
            {
                running = shouldRun;
                Clear();
                runStart = Time.time;
            }
            if (!running) return;

            gifts.RemoveAll(g => g == null);
            CollectAlive();
            WatchCombatants();
        }

        /// <summary>Remove every gift (end of a match).</summary>
        public void Clear()
        {
            foreach (var g in gifts) if (g != null) Destroy(g.gameObject);
            gifts.Clear();
        }

        void CollectAlive()
        {
            alive.Clear();
            foreach (var c in director.Combatants)
                if (c != null && !c.IsDead && c.isActiveAndEnabled) alive.Add(c);
        }

        // ------------------------------------------------------------------ gifts

        void WatchCombatants()
        {
            foreach (var c in director.Combatants)
            {
                if (c == null || watched.Contains(c)) continue;
                watched.Add(c);
                c.Damaged += OnDamaged;
                c.Died += OnDied;
            }
            watched.RemoveAll(c => c == null);
        }

        void OnDestroy()
        {
            foreach (var c in watched)
                if (c != null) { c.Damaged -= OnDamaged; c.Died -= OnDied; }
        }

        void OnDamaged(DamageEvent e)
        {
            if (e.attacker == null || e.amount < bigHitDamage) return;
            if (UnityEngine.Random.value < bigHitGiftChance) TryEarnGift(e.point);
        }

        void OnDied(CarHealth victim, CarHealth killer)
        {
            if (killer != null && victim != null) TryEarnGift(victim.transform.position);
        }

        /// <summary>The emperor rewards the fight: a gift lands a short way from where it happened.</summary>
        void TryEarnGift(Vector3 near)
        {
            if (!running || giftPrefab == null || gifts.Count >= maxGifts) return;
            if (Time.time - runStart < firstGiftDelay || Time.time - lastGift < giftCooldown) return;
            if (SpawnGift(near)) lastGift = Time.time;
        }

        bool SpawnGift(Vector3 near)
        {
            Vector3 best = Vector3.zero;
            bool found = false;
            for (int i = 0; i < 30 && !found; i++)
            {
                float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float dist = UnityEngine.Random.Range(giftDistance.x, giftDistance.y);
                var p = Flat(near) + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist;
                if (Mathf.Abs(p.x) > spawnHalfExtent || Mathf.Abs(p.z) > spawnHalfExtent) continue;
                if (layout != null && layout.OnIsland(p, 2f)) continue;
                // Clear of ramps, walls and props (the sphere sits above flat ground).
                if (Physics.CheckSphere(p + Vector3.up * 1.7f, 1.5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                best = p;
                found = true;
            }
            if (!found) return false;
            if (Physics.Raycast(best + Vector3.up * 10f, Vector3.down, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore))
                best.y = hit.point.y;

            var gift = Instantiate(giftPrefab, best + Vector3.up * 0.55f, Quaternion.identity, transform);
            gift.Owner = this;
            gift.Lifetime = giftLifetime;
            gifts.Add(gift);
            if (crowd != null) crowd.Cheer(0.35f);
            return true;
        }

        public void Collect(EmperorGift gift, CarHealth car)
        {
            gifts.Remove(gift);
            car.SetHealth(car.Health + giftRepair);
            if (crowd != null) crowd.Cheer(0.6f);
            GiftCollected?.Invoke(car);
        }

        public void Expired(EmperorGift gift) => gifts.Remove(gift);

        /// <summary>Nearest landed-or-landing gift, or null.</summary>
        public EmperorGift NearestGift(Vector3 from, out float distance)
        {
            EmperorGift best = null;
            distance = float.MaxValue;
            foreach (var g in gifts)
            {
                if (g == null) continue;
                float d = Flat(g.transform.position - from).magnitude;
                if (d < distance) { distance = d; best = g; }
            }
            return best;
        }

        // ------------------------------------------------------------------ boost pads

        /// <summary>
        /// A pad within reach that we can drive straight onto in its arrow direction, and that fires us
        /// roughly toward <paramref name="goal"/> (null goal = any pad).
        /// </summary>
        public BoostPad PadToward(Vector3 from, Vector3? goal, float maxDistance)
        {
            if (pads == null) return null;
            BoostPad best = null;
            float bestD = maxDistance;
            foreach (var p in pads)
            {
                if (p == null) continue;
                Vector3 toPad = Flat(p.transform.position - from);
                float d = toPad.magnitude;
                if (d > bestD || d < 6f) continue;
                Vector3 dir = Flat(p.Direction).normalized;
                if (Vector3.Angle(toPad, dir) > 30f) continue; // can't line up from here
                if (goal.HasValue && Vector3.Dot(dir, Flat(goal.Value - p.transform.position).normalized) < 0.5f) continue;
                bestD = d;
                best = p;
            }
            return best;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
