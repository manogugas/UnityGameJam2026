using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Gold chest the emperor drops into a quiet corner of the arena. It falls in from the sky, bobs and spins
    /// under a light beam, blinks before it expires, and the first living car to drive through it collects it
    /// (ArenaObjectives decides the reward). Spawned by ArenaObjectives far from where the cars are.
    /// </summary>
    public class EmperorGift : MonoBehaviour
    {
        public Transform visual;
        public float dropHeight = 26f;
        public float dropTime = 1.1f;
        public float spinSpeed = 70f;
        public float bobHeight = 0.18f;
        [Tooltip("Blinks for this long before expiring.")]
        public float blinkTime = 4f;

        public ArenaObjectives Owner { get; set; }
        public float Lifetime { get; set; } = 22f;
        /// <summary>Landed and can be collected.</summary>
        public bool Ready => age >= dropTime;

        float age;
        Vector3 groundPos;
        Renderer[] renderers;

        void Awake()
        {
            groundPos = transform.position;
            renderers = GetComponentsInChildren<Renderer>();
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age >= Lifetime)
            {
                Owner?.Expired(this);
                Destroy(gameObject);
                return;
            }

            float drop = Mathf.Clamp01(age / dropTime);
            float fall = (1f - drop * drop) * dropHeight;
            transform.position = groundPos + Vector3.up * fall;
            if (visual != null)
            {
                visual.localRotation = Quaternion.Euler(0f, age * spinSpeed, 0f);
                visual.localPosition = Vector3.up * (Mathf.Sin(age * 2.6f) * bobHeight * drop);
            }

            float left = Lifetime - age;
            bool show = left > blinkTime || Mathf.Repeat(age * (left < 1.5f ? 8f : 4f), 1f) < 0.6f;
            foreach (var r in renderers) if (r != null) r.enabled = show;
        }

        void OnTriggerEnter(Collider other) => TryCollect(other);
        void OnTriggerStay(Collider other) => TryCollect(other);

        void TryCollect(Collider other)
        {
            if (!Ready || other.attachedRigidbody == null) return;
            var car = other.attachedRigidbody.GetComponent<CarHealth>();
            if (car == null || car.IsDead) return;
            Owner?.Collect(this, car);
            Destroy(gameObject);
        }
    }
}
