using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Driver's-eye camera: Q/E look left/right, Alt looks back, reversing turns the head to the rear window.
    /// Adds g-force head lean and impact shake.
    /// </summary>
    // Runs before the Cinemachine brain so the driver camera reads this frame's head pose.
    [DefaultExecutionOrder(-200)]
    public class CarCamera : MonoBehaviour
    {
        public CarController car;
        public CarInput input;
        public CarDamage damage;

        [Header("Look")]
        public float sideLookAngle = 80f;
        public float backLookAngle = 172f;
        public float lookSmoothTime = 0.11f;
        public float basePitch = 8f;
        [Tooltip("Head moves toward the car centre when looking back.")]
        public Vector3 backLookOffset = new Vector3(0.3f, 0.06f, 0.0f);

        [Header("Feel")]
        public float gLean = 0.005f;
        public float maxLean = 0.08f;
        public float rollPerAccel = 0.35f;
        public float shakeAmplitude = 0.05f;
        public float shakeDecay = 4f;

        Vector3 basePos;
        float yaw, yawVel, shake;
        Vector3 lastVelocity, smoothAccel;

        void Awake() => basePos = transform.localPosition;

        void OnEnable() { if (damage != null) damage.Impact += OnImpact; }
        void OnDisable() { if (damage != null) damage.Impact -= OnImpact; }

        void OnImpact(ImpactInfo info) => shake = Mathf.Min(1.5f, shake + 0.25f + info.severity * 1.3f);

        void FixedUpdate()
        {
            var rb = car.Body;
            if (rb == null) return;
            Vector3 accel = (rb.linearVelocity - lastVelocity) / Time.fixedDeltaTime;
            lastVelocity = rb.linearVelocity;
            Vector3 local = car.transform.InverseTransformDirection(accel);
            smoothAccel = Vector3.Lerp(smoothAccel, Vector3.ClampMagnitude(local, 40f), 1f - Mathf.Exp(-Time.fixedDeltaTime * 6f));
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            bool back = input.LookBack || car.ReversingView;
            float target = 0f;
            if (back) target = backLookAngle;
            else if (input.LookLeft && !input.LookRight) target = -sideLookAngle;
            else if (input.LookRight && !input.LookLeft) target = sideLookAngle;

            // Plain (not shortest-angle) smoothing inside -180..180, so the head turns back through the
            // front like a person would. With SmoothDampAngle, going from looking back (172) to looking
            // left (-80) wrapped yaw to ~280, which kept the look-back head offset on: the view got stuck
            // in the middle of the car.
            yaw = Mathf.SmoothDamp(Mathf.Clamp(yaw, -180f, 180f), target, ref yawVel, lookSmoothTime);
            float backAmount = Mathf.Clamp01(Mathf.InverseLerp(100f, backLookAngle, yaw));

            Vector3 lean = Vector3.ClampMagnitude(new Vector3(-smoothAccel.x, -smoothAccel.y * 0.3f, -smoothAccel.z) * gLean, maxLean);
            float roll = Mathf.Clamp(smoothAccel.x * rollPerAccel, -6f, 6f);

            float t = Time.time * 28f;
            Vector3 shakePos = new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, 0f) * (shake * shakeAmplitude);
            Vector3 shakeRot = new Vector3(Mathf.PerlinNoise(t, 3f) - 0.5f, Mathf.PerlinNoise(3f, t) - 0.5f, Mathf.PerlinNoise(t, t) - 0.5f) * (shake * 6f);
            shake = Mathf.MoveTowards(shake, 0f, shakeDecay * dt * Mathf.Max(0.3f, shake));

            transform.localPosition = basePos + backLookOffset * backAmount + lean + shakePos;
            transform.localRotation = Quaternion.Euler(basePitch * (1f - backAmount * 0.6f), yaw, roll) * Quaternion.Euler(shakeRot);
        }
    }
}
