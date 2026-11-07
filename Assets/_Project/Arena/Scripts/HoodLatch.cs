using UnityEngine;

namespace CarArena
{
    /// <summary>Hood hinge at the windscreen. Once the latch breaks, wind lifts and flaps the hood.</summary>
    public class HoodLatch : MonoBehaviour
    {
        public Rigidbody carBody;
        public float popAngle = -14f;
        [Tooltip("Extra lift at windSpeed (negative = front edge up).")]
        public float windAngle = -42f;
        public float windSpeed = 28f;
        public float flutter = 7f;
        public float stiffness = 70f;
        public float damping = 7f;

        public bool Popped { get; private set; }

        Quaternion baseRot;
        float angle, angVel;

        void Awake() => baseRot = transform.localRotation;

        public void Pop()
        {
            if (Popped) return;
            Popped = true;
            angVel = -260f;
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
            float target = popAngle + windAngle * wind
                           + (Mathf.Sin(t * 13f) * 0.5f + Mathf.PerlinNoise(t * 4f, 0.5f) - 0.5f) * flutter * (0.3f + wind);

            angVel += ((target - angle) * stiffness - angVel * damping) * dt;
            angle += angVel * dt;
            if (angle > 0f) { angle = 0f; angVel = -Mathf.Abs(angVel) * 0.3f; }
            angle = Mathf.Max(angle, -80f);
            transform.localRotation = baseRot * Quaternion.Euler(angle, 0f, 0f);
        }
    }
}
