using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Side objectives that pull the fight apart, so cars spread out and meet again at speed:
    ///  - Emperor's gifts: gold chests dropped into the quietest part of the arena (as far from every car as
    ///    possible). Any car can grab one: it repairs a little, and the campaign pays the player cash for it.
    ///  - Boost pads: fixed chevron strips that fire cars across the arena (BoostPad).
    ///  - Crowd hazards: when cars stay bunched up, the crowd throws a burning barrel at the pile (CrowdHazard).
    /// Runs only while the ArenaDirector has a fight on; everything is cleared between matches.
    /// The AI reads gifts, pads and danger zones from here.
    /// </summary>
    public class ArenaObjectives : MonoBehaviour
    {
        public ArenaDirector director;
        public ArenaCrowd crowd;

        [Header("Emperor's gifts")]
        public EmperorGift giftPrefab;
        public float firstGiftDelay = 7f;
        public Vector2 giftInterval = new Vector2(11f, 17f);
        public int maxGifts = 2;
        public float giftLifetime = 22f;
        [Tooltip("Health a gift restores to whoever grabs it.")]
        public float giftRepair = 15f;
        [Tooltip("Gifts land inside +- this many metres of the centre.")]
        public float spawnHalfExtent = 48f;

        [Header("Crowd hazards")]
        public CrowdHazard hazardPrefab;
        [Tooltip("Cars within this distance of each other count as bunched up.")]
        public float clusterRadius = 10f;
        [Tooltip("Seconds three or more cars may stay bunched before the crowd throws something.")]
        public float crowdClusterTime = 3.5f;
        [Tooltip("Seconds two cars may grind against each other before the crowd throws something.")]
        public float pairClusterTime = 7f;
        public float hazardCooldown = 9f;
        public int maxHazards = 2;
        [Tooltip("Barrels are thrown from this far out (the front rows of the stands).")]
        public float throwDistance = 84f;

        [Header("Boost pads")]
        public BoostPad[] pads;

        /// <summary>A car drove through a gift (after the repair was applied).</summary>
        public event Action<CarHealth> GiftCollected;

        public IReadOnlyList<EmperorGift> Gifts => gifts;
        public IReadOnlyList<CrowdHazard> Hazards => hazards;
        public IReadOnlyList<BoostPad> Pads => pads;
        public float CrowdHeat => Mathf.Max(crowdTimer / Mathf.Max(0.1f, crowdClusterTime), pairTimer / Mathf.Max(0.1f, pairClusterTime));

        readonly List<EmperorGift> gifts = new List<EmperorGift>();
        readonly List<CrowdHazard> hazards = new List<CrowdHazard>();
        readonly List<CarHealth> alive = new List<CarHealth>();
        bool running;
        float giftTimer, clusterCheck, crowdTimer, pairTimer, lastHazard = -100f;

        void Update()
        {
            bool shouldRun = director != null && director.Running;
            if (shouldRun != running)
            {
                running = shouldRun;
                Clear();
                giftTimer = firstGiftDelay;
            }
            if (!running) return;

            gifts.RemoveAll(g => g == null);
            hazards.RemoveAll(h => h == null);
            CollectAlive();
            if (alive.Count < 2) return;

            giftTimer -= Time.deltaTime;
            if (giftTimer <= 0f)
            {
                giftTimer = UnityEngine.Random.Range(giftInterval.x, giftInterval.y);
                if (gifts.Count < maxGifts) SpawnGift();
            }

            clusterCheck -= Time.deltaTime;
            if (clusterCheck <= 0f)
            {
                clusterCheck = 0.25f;
                CheckClusters(0.25f);
            }
        }

        /// <summary>Remove every gift and hazard (end of a match).</summary>
        public void Clear()
        {
            foreach (var g in gifts) if (g != null) Destroy(g.gameObject);
            foreach (var h in hazards) if (h != null) Destroy(h.gameObject);
            gifts.Clear();
            hazards.Clear();
            crowdTimer = pairTimer = 0f;
        }

        void CollectAlive()
        {
            alive.Clear();
            foreach (var c in director.Combatants)
                if (c != null && !c.IsDead && c.isActiveAndEnabled) alive.Add(c);
        }

        // ------------------------------------------------------------------ gifts

        void SpawnGift()
        {
            if (giftPrefab == null) return;
            Vector3 best = Vector3.zero;
            float bestScore = -1f;
            for (int i = 0; i < 40; i++)
            {
                var p = new Vector3(UnityEngine.Random.Range(-spawnHalfExtent, spawnHalfExtent), 0f,
                    UnityEngine.Random.Range(-spawnHalfExtent, spawnHalfExtent));
                // Clear of ramps, blocks and props (the sphere sits above flat ground).
                if (Physics.CheckSphere(p + Vector3.up * 1.7f, 1.5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                float score = float.MaxValue;
                foreach (var c in alive) score = Mathf.Min(score, Flat(c.transform.position - p).magnitude);
                foreach (var g in gifts) if (g != null) score = Mathf.Min(score, Flat(g.transform.position - p).magnitude);
                if (score > bestScore) { bestScore = score; best = p; }
            }
            if (bestScore < 0f) return;
            if (Physics.Raycast(best + Vector3.up * 10f, Vector3.down, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore))
                best.y = hit.point.y;

            var gift = Instantiate(giftPrefab, best + Vector3.up * 0.55f, Quaternion.identity, transform);
            gift.Owner = this;
            gift.Lifetime = giftLifetime;
            gifts.Add(gift);
            if (crowd != null) crowd.Cheer(0.35f);
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

        // ------------------------------------------------------------------ hazards

        void CheckClusters(float dt)
        {
            int bestCount = 0;
            Vector3 centre = Vector3.zero, vel = Vector3.zero;
            foreach (var a in alive)
            {
                int n = 0;
                Vector3 sum = Vector3.zero, vsum = Vector3.zero;
                foreach (var b in alive)
                {
                    if (Flat(a.transform.position - b.transform.position).magnitude > clusterRadius) continue;
                    n++;
                    sum += b.transform.position;
                    var rb = b.car != null ? b.car.Body : null;
                    if (rb != null) vsum += rb.linearVelocity;
                }
                if (n > bestCount) { bestCount = n; centre = sum / n; vel = vsum / n; }
            }

            if (bestCount >= 3) crowdTimer += dt; else crowdTimer = Mathf.Max(0f, crowdTimer - dt * 2f);
            if (bestCount >= 2) pairTimer += dt; else pairTimer = Mathf.Max(0f, pairTimer - dt * 2f);

            bool fire = crowdTimer >= crowdClusterTime || pairTimer >= pairClusterTime;
            if (!fire || Time.time - lastHazard < hazardCooldown || hazards.Count >= maxHazards || hazardPrefab == null) return;

            crowdTimer = pairTimer = 0f;
            lastHazard = Time.time;
            // Aim a little ahead of where the pile is drifting.
            Vector3 target = centre + Flat(vel) * (hazardPrefab.warningTime * 0.4f);
            target.x = Mathf.Clamp(target.x, -spawnHalfExtent - 10f, spawnHalfExtent + 10f);
            target.z = Mathf.Clamp(target.z, -spawnHalfExtent - 10f, spawnHalfExtent + 10f);
            target.y = 0f;
            if (Physics.Raycast(target + Vector3.up * 10f, Vector3.down, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore))
                target.y = hit.point.y;

            Vector3 outward = Flat(target).sqrMagnitude > 1f ? Flat(target).normalized : Vector3.forward;
            outward = Quaternion.Euler(0f, UnityEngine.Random.Range(-35f, 35f), 0f) * outward;
            var hazard = Instantiate(hazardPrefab, target, Quaternion.identity, transform);
            hazard.Owner = this;
            hazard.LaunchFrom = outward * throwDistance + Vector3.up * 11f;
            hazards.Add(hazard);
            if (crowd != null) crowd.Cheer(0.5f);
        }

        public void HazardLanded(CrowdHazard hazard)
        {
            if (crowd != null) crowd.Cheer(0.9f);
        }

        /// <summary>Is this point inside (or within margin of) an incoming blast or a fire patch?</summary>
        public bool InDanger(Vector3 point, float margin, out Vector3 centre)
        {
            foreach (var h in hazards)
            {
                if (h == null || h.DangerRadius <= 0f) continue;
                if (Flat(point - h.transform.position).magnitude < h.DangerRadius + margin)
                {
                    centre = h.transform.position;
                    return true;
                }
            }
            centre = Vector3.zero;
            return false;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
