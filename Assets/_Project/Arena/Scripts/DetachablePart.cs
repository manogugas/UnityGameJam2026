using System.Collections;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// A part that loosens, wobbles and then breaks off. Physics parts fly away as rigidbodies;
    /// interior parts drop onto a resting pose inside the cabin.
    /// </summary>
    public class DetachablePart : MonoBehaviour
    {
        public enum Mode { Physics, InteriorDrop }
        public enum PartState { Attached, Loose, Detached }

        public string displayName = "Part";
        public Mode mode = Mode.Physics;
        public float maxHealth = 100f;
        [Range(0f, 1f)] public float looseAt = 0.5f;
        [Tooltip("Damage taken from every impact anywhere on the car (x severity x 30).")]
        public float joltSensitivity;
        public float detachedMass = 12f;

        [Header("Loose pose")]
        public Vector3 looseOffset;
        public Vector3 looseEuler;
        public float wobble = 3f;

        [Header("Interior drop")]
        public Vector3 dropLocalPosition;
        public Vector3 dropLocalEuler;

        public float Health { get; private set; }
        public PartState State { get; private set; }

        Transform originalParent;
        Vector3 basePos;
        Quaternion baseRot;
        Rigidbody carRb, ownRb;
        Collider[] colliders;
        bool pendingDetach;
        float dropT;
        Vector3 dropStartPos;
        Quaternion dropStartRot;
        Coroutine ignoreRoutine;
        float wobbleTime, wobbleSeed;

        void Awake()
        {
            originalParent = transform.parent;
            basePos = transform.localPosition;
            baseRot = transform.localRotation;
            carRb = GetComponentInParent<Rigidbody>();
            colliders = GetComponentsInChildren<Collider>(true);
            Health = maxHealth;
            wobbleSeed = Random.Range(0f, 100f);
        }

        public void ApplyDamage(float amount)
        {
            if (State == PartState.Detached || amount <= 0f) return;
            Health -= amount;
            if (Health <= 0f) pendingDetach = true;
            else if (Health < maxHealth * looseAt && State == PartState.Attached)
            {
                State = PartState.Loose;
                transform.localPosition = basePos + looseOffset;
            }
        }

        void FixedUpdate()
        {
            if (!pendingDetach) return;
            pendingDetach = false;
            Detach();
        }

        void Update()
        {
            if (State == PartState.Loose)
            {
                // Smooth, low-frequency rattle (was a fast sine that sped up with speed and read as jitter).
                float speed = carRb != null ? carRb.linearVelocity.magnitude : 0f;
                float amp = wobble * Mathf.Clamp01(0.2f + speed / 30f);
                wobbleTime += Time.deltaTime * (2.5f + Mathf.Min(speed, 30f) * 0.08f);
                float a = (Mathf.PerlinNoise(wobbleTime, wobbleSeed) - 0.5f) * 2f * amp;
                float b = (Mathf.PerlinNoise(wobbleSeed, wobbleTime * 0.8f) - 0.5f) * 2f * amp;
                Quaternion target = baseRot * Quaternion.Euler(looseEuler) * Quaternion.Euler(a, b * 0.4f, b * 0.7f);
                transform.localRotation = Quaternion.Slerp(transform.localRotation, target, 1f - Mathf.Exp(-Time.deltaTime * 12f));
            }
            else if (State == PartState.Detached && mode == Mode.InteriorDrop && dropT < 1f)
            {
                dropT = Mathf.Min(1f, dropT + Time.deltaTime * 2.2f);
                float e = Bounce(dropT);
                transform.localPosition = Vector3.LerpUnclamped(dropStartPos, dropLocalPosition, e);
                transform.localRotation = Quaternion.SlerpUnclamped(dropStartRot, Quaternion.Euler(dropLocalEuler), Mathf.Min(1f, dropT * 1.6f));
            }
        }

        static float Bounce(float t)
        {
            if (t < 0.6f) { float a = t / 0.6f; return a * a; }
            float b = (t - 0.6f) / 0.4f;
            return 1f - Mathf.Sin(b * Mathf.PI) * 0.12f * (1f - b);
        }

        void Detach()
        {
            if (State == PartState.Detached) return;
            State = PartState.Detached;
            Health = 0f;

            if (mode == Mode.InteriorDrop)
            {
                foreach (var c in colliders) c.enabled = false;
                dropStartPos = transform.localPosition;
                dropStartRot = transform.localRotation;
                dropT = 0f;
                return;
            }

            Vector3 v = carRb != null ? carRb.GetPointVelocity(transform.position) : Vector3.zero;
            transform.SetParent(null, true);
            ownRb = gameObject.AddComponent<Rigidbody>();
            ownRb.mass = detachedMass;
            ownRb.interpolation = RigidbodyInterpolation.Interpolate;
            ownRb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            ownRb.linearVelocity = v * 0.85f + Random.insideUnitSphere * 2f + Vector3.up * 1.5f;
            ownRb.angularVelocity = Random.insideUnitSphere * 6f;
            if (carRb != null) ignoreRoutine = StartCoroutine(IgnoreCarFor(1.5f));
        }

        IEnumerator IgnoreCarFor(float seconds)
        {
            var carCols = carRb.GetComponentsInChildren<Collider>();
            SetIgnore(carCols, true);
            yield return new WaitForSeconds(seconds);
            SetIgnore(carCols, false);
            ignoreRoutine = null;
        }

        void SetIgnore(Collider[] carCols, bool ignore)
        {
            foreach (var a in colliders)
                foreach (var b in carCols)
                    if (a != null && b != null) Physics.IgnoreCollision(a, b, ignore);
        }

        public void Repair()
        {
            if (ignoreRoutine != null)
            {
                StopCoroutine(ignoreRoutine);
                ignoreRoutine = null;
                if (carRb != null) SetIgnore(carRb.GetComponentsInChildren<Collider>(), false);
            }
            if (ownRb != null) { Destroy(ownRb); ownRb = null; }
            if (transform.parent != originalParent) transform.SetParent(originalParent, false);
            transform.localPosition = basePos;
            transform.localRotation = baseRot;
            foreach (var c in colliders) c.enabled = true;
            Health = maxHealth;
            State = PartState.Attached;
            pendingDetach = false;
        }
    }
}
