using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Turns collisions into dents, part damage, hood buckling and an Impact event
    /// that interior damage, camera shake and audio listen to.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CarDamage : MonoBehaviour
    {
        public CarInput input;
        public DeformableMesh[] deformables;
        public DetachablePart[] parts;
        public DeformableMesh hood;
        public HoodLatch hoodLatch;

        [Header("Thresholds (N s)")]
        public float minImpulse = 2200f;
        public float fullImpulse = 24000f;
        public float cooldown = 0.08f;

        [Header("Dents")]
        public float maxDentDepth = 0.3f;
        public float dentRadius = 0.6f;
        public float dentRadiusGrow = 0.8f;

        [Header("Hood")]
        public float hoodBuckle = 0.14f;
        [Tooltip("Accumulated frontal severity that breaks the hood latch.")]
        public float hoodPopDamage = 1.0f;

        [Header("Parts")]
        public float partDamagePerSeverity = 160f;
        [Tooltip("Damage per 1000 N s when a part's own collider is hit.")]
        public float directHitDamage = 9f;

        public event Action<ImpactInfo> Impact;
        public event Action Repaired;

        public float TotalDamage { get; private set; }
        public float FrontDamage { get; private set; }
        public float Integrity => Mathf.Clamp01(1f - TotalDamage / 5f) * 100f;

        Rigidbody rb;
        readonly Dictionary<Collider, DetachablePart> partByCollider = new Dictionary<Collider, DetachablePart>();
        readonly HashSet<DetachablePart> directHits = new HashSet<DetachablePart>();
        float lastImpactTime = -10f;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            foreach (var p in parts)
                foreach (var c in p.GetComponentsInChildren<Collider>(true))
                    partByCollider[c] = p;
        }

        void Update()
        {
            if (input != null && input.RepairPressed) RepairAll();
        }

        void OnCollisionEnter(Collision c) => HandleCollision(c);

        void HandleCollision(Collision c)
        {
            float impulse = c.impulse.magnitude;
            int count = c.contactCount;
            if (count == 0) return;

            Vector3 point = Vector3.zero, normal = Vector3.zero;
            directHits.Clear();
            for (int i = 0; i < count; i++)
            {
                var cp = c.GetContact(i);
                point += cp.point;
                normal += cp.normal;
                if (partByCollider.TryGetValue(cp.thisCollider, out var part)) directHits.Add(part);
            }
            point /= count;

            if (impulse > minImpulse * 0.3f)
                foreach (var part in directHits) part.ApplyDamage(impulse / 1000f * directHitDamage);

            if (impulse < minImpulse || Time.time - lastImpactTime < cooldown) return;
            lastImpactTime = Time.time;

            // Dent direction: along the contact normal, pointing into the car.
            Vector3 dir = normal.sqrMagnitude > 1e-6f ? normal.normalized : (rb.worldCenterOfMass - point).normalized;
            if (Vector3.Dot(dir, rb.worldCenterOfMass - point) < 0f) dir = -dir;

            var info = new ImpactInfo
            {
                worldPoint = point,
                worldDir = dir,
                localPoint = transform.InverseTransformPoint(point),
                localDir = transform.InverseTransformDirection(dir),
                impulse = impulse,
                severity = Mathf.Clamp01((impulse - minImpulse) / (fullImpulse - minImpulse)),
                otherBody = c.rigidbody,
            };
            info.bottom = info.localDir.y > 0.7f;
            if (info.bottom) info.severity *= 0.25f;
            info.front = info.localPoint.z > 1.3f && !info.bottom;
            info.rear = info.localPoint.z < -1.7f && !info.bottom;
            info.left = info.localPoint.x < -0.6f;
            info.right = info.localPoint.x > 0.6f;
            info.top = info.localPoint.y > 0.85f;

            float s = info.severity;
            float depth = maxDentDepth * s;
            float radius = dentRadius + dentRadiusGrow * s;
            foreach (var d in deformables) d.Deform(point, dir, depth, radius);

            if (info.front)
            {
                FrontDamage += s;
                if (hood != null) hood.Buckle(s * hoodBuckle);
                if (hoodLatch != null && FrontDamage >= hoodPopDamage) hoodLatch.Pop();
            }

            foreach (var part in parts)
            {
                float dist = Vector3.Distance(part.transform.position, point);
                float falloff = Mathf.Clamp01(1f - dist / (radius * 2.2f));
                part.ApplyDamage(s * partDamagePerSeverity * falloff + s * part.joltSensitivity * 30f);
            }

            TotalDamage += s;
            Impact?.Invoke(info);
        }

        public void RepairAll()
        {
            foreach (var d in deformables) d.Restore();
            foreach (var p in parts) p.Repair();
            if (hoodLatch != null) hoodLatch.ResetLatch();
            TotalDamage = 0f;
            FrontDamage = 0f;
            Repaired?.Invoke();
        }
    }
}
