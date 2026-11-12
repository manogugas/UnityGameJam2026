using UnityEngine;

namespace CarArena
{
    /// <summary>Hood hinge at the windscreen. Once the latch breaks, wind lifts and flaps the hood.</summary>
    public class HoodLatch : MonoBehaviour
    {
        public Rigidbody carBody;
        public float popAngle = -4f;
        [Tooltip("Extra lift at windSpeed (negative = front edge up).")]
        public float windAngle = -7f;
        public float windSpeed = 28f;
        public float flutter = 2f;
        public float stiffness = 70f;
        public float damping = 7f;
        [Tooltip("Hood never opens further than this (keeps the view mostly clear).")]
        public float maxOpenAngle = -12f;

        public bool Popped { get; private set; }

        Quaternion baseRot;
        float angle, angVel;

        void Awake() => baseRot = transform.localRotation;

        public void Pop()
        {
            if (Popped) return;
            Popped = true;
            angVel = -80f;
        }

        public void ResetLatch()
        {
            Popped = false;
            angle = angVel = 0f;
            transform.localRotation = baseRot;
        }

        void Update()
        {
            if (!Popped) return;
            float dt = Time.deltaTime;
            float speed = carBody != null ? Mathf.Max(0f, Vector3.Dot(carBody.linearVelocity, carBody.transform.forward)) : 0f;
            float wind = Mathf.Clamp01(speed / windSpeed);
            float t = Time.time;
            // Low-frequency flutter (a 13 rad/s sine read as jitter at speed).
            float target = popAngle + windAngle * wind
                           + (Mathf.PerlinNoise(t * 3f, 0.5f) - 0.5f) * 2f * flutter * (0.3f + wind);

            angVel += ((target - angle) * stiffness - angVel * damping) * dt;
            angle += angVel * dt;
            if (angle > 0f) { angle = 0f; angVel = -Mathf.Abs(angVel) * 0.3f; }
            angle = Mathf.Max(angle, maxOpenAngle);
            transform.localRotation = baseRot * Quaternion.Euler(angle, 0f, 0f);
        }
    }
}
