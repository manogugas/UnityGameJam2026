using Unity.Cinemachine;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Driver's-eye camera: Q/E look left/right, Alt looks back, reversing turns the head to the rear window.
    /// Adds g-force head lean, impact shake and a sprung head jolt: when something hits the car the body
    /// is shoved and your head lags behind (toward the hit), then springs back.
    /// Speed feel: the field of view widens with speed (and kicks out further while boosting), and a road
    /// rumble shakes the head harder the faster you go (rougher on sand).
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

        [Header("Speed feel")]
        public CinemachineCamera vcam;
        public float baseFov = 70f;
        [Tooltip("Extra FOV at top speed.")]
        public float speedFov = 18f;
        [Tooltip("Extra FOV while boosting.")]
        public float boostFov = 7f;
        [Tooltip("Speed (m/s) where the effects start and where they peak.")]
        public float feelSpeedMin = 10f, feelSpeedMax = 38f;
        public float fovSmoothing = 3f;
        [Tooltip("Road rumble at top speed: head movement (m) and rotation (deg).")]
        public float rumblePos = 0.012f, rumbleRot = 0.7f;
        [Tooltip("Rumble multiplier on loose surfaces (sand).")]
        public float looseRumble = 1.8f;

        [Tooltip("Seconds for the speed effects to catch up with a change in speed (a boost pad's jump).")]
        public float feelRiseTime = 1.4f, feelFallTime = 0.9f;
        [Tooltip("Seconds for the boost effects to fade in / out.")]
        public float boostRiseTime = 1.1f, boostFallTime = 1.3f;

        /// <summary>0 at low speed .. 1 at top speed, eased in (for screen effects).</summary>
        public float SpeedFeel { get; private set; }
        /// <summary>0..1 boost effect amount, eased in and out.</summary>
        public float BoostFeel { get; private set; }

        /// <summary>Extra pitch (degrees, negative = look up) set by others, e.g. leaning in to read the garage tablet.</summary>
        [System.NonSerialized] public float pitchOffset;

        Vector3 basePos;
        Vector3 joltPos, joltVel, tilt, tiltVel;
        float yaw, yawVel, shake, fov = -1f, rumbleT;
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

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>Drop all eased state (speed FOV, rumble, shake, jolt, head turn) so the next frame is a clean cut,
        /// e.g. when the car is parked back in the garage after a race.</summary>
        public void ResetView()
        {
            SpeedFeel = BoostFeel = 0f;
            fov = -1f;
            shake = 0f;
            yaw = yawVel = 0f;
            joltPos = joltVel = tilt = tiltVel = Vector3.zero;
            smoothAccel = Vector3.zero;
            lastVelocity = car != null && car.Body != null ? car.Body.linearVelocity : Vector3.zero;
            if (vcam != null) vcam.PreviousStateIsValid = false;
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
            float shakeScale = GameSettings.CameraShake;
            shakePos *= shakeScale;
            shakeRot *= shakeScale;

            // Speed feel: wider FOV and a road rumble that builds with speed.
            float speed = car.Body != null ? car.Body.linearVelocity.magnitude : 0f;
            // Eased: effects build up over a second or so instead of snapping (boost pads jump the speed).
            float rawK = Mathf.Clamp01(Mathf.InverseLerp(feelSpeedMin, feelSpeedMax, speed));
            float kTime = rawK > SpeedFeel ? feelRiseTime : feelFallTime;
            SpeedFeel = Mathf.MoveTowards(SpeedFeel, rawK, dt / Mathf.Max(0.01f, kTime));
            float boostTime = car.Boosting ? boostRiseTime : boostFallTime;
            BoostFeel = Mathf.MoveTowards(BoostFeel, car.Boosting ? 1f : 0f, dt / Mathf.Max(0.01f, boostTime));
            float k = Smooth(SpeedFeel);
            float b = Smooth(BoostFeel);
            if (vcam != null && dt > 0f)
            {
                float fx = GameSettings.SpeedEffects;
                float targetFov = baseFov + (speedFov * Mathf.Pow(k, 1.3f) + boostFov * b) * fx;
                fov = fov < 0f ? targetFov : Mathf.Lerp(fov, targetFov, 1f - Mathf.Exp(-fovSmoothing * dt));
                var lens = vcam.Lens;
                lens.FieldOfView = fov;
                vcam.Lens = lens;
            }
            bool loose = false;
            foreach (var w in car.wheels) if (w.Grounded && w.Surface != null && w.Surface.loose) { loose = true; break; }
            float rumble = k * k * (loose ? looseRumble : 1f) * (1f + 0.5f * b) * shakeScale;
            rumbleT += dt * (18f + speed * 0.9f);
            Vector3 rumblePosV = new Vector3(Mathf.PerlinNoise(rumbleT, 11f) - 0.5f, Mathf.PerlinNoise(11f, rumbleT) - 0.5f, 0f) * (2f * rumblePos * rumble);
            Vector3 rumbleRotV = new Vector3(Mathf.PerlinNoise(rumbleT, 21f) - 0.5f, 0f, Mathf.PerlinNoise(21f, rumbleT) - 0.5f) * (2f * rumbleRot * rumble);

            transform.localPosition = basePos + backLookOffset * backAmount + lean + shakePos + joltPos * shakeScale + rumblePosV;
            transform.localRotation = Quaternion.Euler(tilt * shakeScale)
                * Quaternion.Euler(basePitch * (1f - backAmount * 0.6f) + pitchOffset, yaw, roll) * Quaternion.Euler(shakeRot + rumbleRotV);
        }
    }
}
