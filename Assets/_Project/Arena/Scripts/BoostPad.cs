using System.Collections.Generic;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Chevron strip on the track. Driving over it in the arrow's direction gives a short push
    /// (CarController.Boost) and a ram-damage bonus until shortly after: the pads sit on straights aimed
    /// at the crossing pit, so they're for hitting harder, not for running away. The chevrons chase
    /// toward the arrow tip and flash when a car uses the pad.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class BoostPad : MonoBehaviour
    {
        public float boostSeconds = 0.8f;
        [Tooltip("Car must face within this angle of the arrow.")]
        public float maxAngle = 60f;
        [Tooltip("Per-car cooldown.")]
        public float cooldown = 5f;

        [Header("Visuals")]
        [Tooltip("Chevron renderers from the back of the pad to the tip, renderersPerChevron each.")]
        public Renderer[] chevrons;
        public int renderersPerChevron = 2;
        public Material onMaterial, offMaterial;
        public float chaseSpeed = 7f;

        public Vector3 Direction => transform.forward;

        readonly Dictionary<CarController, float> lastUse = new Dictionary<CarController, float>();
        float flash;

        void Update()
        {
            if (chevrons == null || chevrons.Length == 0) return;
            flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 2f);
            int per = Mathf.Max(1, renderersPerChevron);
            int count = chevrons.Length / per;
            int lit = Mathf.FloorToInt(Time.time * chaseSpeed) % (count + 2);
            for (int i = 0; i < chevrons.Length; i++)
            {
                if (chevrons[i] == null) continue;
                int c = i / per;
                bool on = flash > 0f ? Mathf.Repeat(Time.time * 12f, 1f) < 0.5f : (c == lit || c == lit - 1);
                chevrons[i].sharedMaterial = on ? onMaterial : offMaterial;
            }
        }

        void OnTriggerStay(Collider other)
        {
            var rb = other.attachedRigidbody;
            if (rb == null) return;
            var car = rb.GetComponent<CarController>();
            if (car == null || car.EngineDead) return;
            if (lastUse.TryGetValue(car, out float t) && Time.time - t < cooldown) return;
            if (Vector3.Angle(Flat(car.transform.forward), Flat(Direction)) > maxAngle) return;
            if (Vector3.Dot(rb.linearVelocity, Direction) < 2f) return; // must be rolling forward over it
            lastUse[car] = Time.time;
            car.Boost(boostSeconds);
            flash = 1f;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
