using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Driver's-eye camera: Q/E look left/right, Alt looks back, reversing turns the head to the rear window.
    /// Adds g-force head lean, impact shake and a sprung head jolt: when something hits the car the body
    /// is shoved and your head lags behind (toward the hit), then springs back.
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

        [Header("Impact jolt")]
        [Tooltip("Head velocity kick (m/s) from the lightest bump.")]
        public float joltMin = 0.25f;
        [Tooltip("Extra head velocity kick (m/s) at full crash strength.")]
        public float joltPerStrength = 2.6f;
        public float joltSpring = 170f;
        public float joltDamping = 15f;
        public float maxJolt = 0.16f;
        [Tooltip("Head tilt kick (deg/s) at full strength.")]
        public float joltTilt = 260f;
        public float maxJoltTilt = 9f;

        Vector3 basePos;
        Vector3 joltPos, joltVel, tilt, tiltVel;
        float yaw, yawVel, shake;
        Vector3 lastVelocity, smoothAccel;

        void Awake() => basePos = transform.localPosition;

        void OnEnable() { if (damage != null) { damage.Impact += OnImpact; damage.Bumped += OnBump; } }
        void OnDisable() { if (damage != null) { damage.Impact -= OnImpact; damage.Bumped -= OnBump; } }

        void OnImpact(ImpactInfo info) => shake = Mathf.Min(1.5f, shake + 0.25f + info.severity * 1.3f);

        /// <summary>The car is shoved along worldPush; the head lags the other way, then springs back.</summary>
        void OnBump(Vector3 worldPush, float strength)
        {
            Vector3 local = car.transform.InverseTransformDirection(worldPush);
            float kick = joltMin + strength * joltPerStrength;
            joltVel += -local * kick;
            // Tilt toward the hit: pitch forward on rear hits / back on front hits, roll on side hits.
            float tiltKick = joltTilt * (0.15f + strength);
            tiltVel += new Vector3(local.z * tiltKick * 0.6f, -local.x * tiltKick * 0.35f, local.x * tiltKick);
            shake = Mathf.Min(1.5f, shake + 0.1f + strength * 0.5f);
        }

        void UpdateJolt(float dt)
        {
            // Semi-implicit spring, sub-stepped so a long frame can't blow it up.
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.008f), 1, 8);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                joltVel += (-joltSpring * joltPos - joltDamping * joltVel) * h;
                joltPos += joltVel * h;
                tiltVel += (-joltSpring * tilt - joltDamping * tiltVel) * h;
                tilt += tiltVel * h;
            }
            joltPos = Vector3.ClampMagnitude(joltPos, maxJolt);
            tilt = Vector3.ClampMagnitude(tilt, maxJoltTilt);
        }

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

            UpdateJolt(dt);

            transform.localPosition = basePos + backLookOffset * backAmount + lean + shakePos + joltPos;
            transform.localRotation = Quaternion.Euler(tilt.x, tilt.y, tilt.z)
                * Quaternion.Euler(basePitch * (1f - backAmount * 0.6f), yaw, roll) * Quaternion.Euler(shakeRot);
        }
    }
}
