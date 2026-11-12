using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Raycast suspension + combined-slip tyre model for one wheel.
    /// The transform is the suspension mount (child of the car, identity rotation).
    /// </summary>
    public class CarWheel : MonoBehaviour
    {
        public enum Axle { Front, Rear }

        [Header("Geometry")]
        public Axle axle = Axle.Front;
        public float radius = 0.34f;
        public float restLength = 0.38f;
        public Transform visual;

        [Header("Suspension")]
        public float springRate = 38000f;
        public float damperRate = 4200f;
        [Tooltip("Extra stiffness when the spring is fully compressed (bump stop).")]
        public float bumpStopRate = 160000f;

        [Header("Wheel")]
        [Tooltip("Wheel + drivetrain rotational inertia (kg m^2).")]
        public float inertia = 1.6f;

        public bool IsFront => axle == Axle.Front;
        public bool Grounded { get; private set; }
        public float Compression { get; private set; }
        public float Load { get; private set; }
        public float Omega { get; set; }
        public float SteerAngle { get; set; }
        public SurfaceProfile Surface { get; private set; }
        public float SlipRatio { get; private set; }
        public float SlipAngle { get; private set; }
        public float CombinedSlip { get; private set; }
        public float ContactSpeed { get; private set; }
        public float WheelSpeed => Omega * radius;

        [Tooltip("Smoothing time (s) for the wheel's visual suspension travel. Physics runs at a fixed rate and the road " +
                 "has fine noise; showing every step made the wheels jitter against the interpolated body.")]
        public float visualSmoothing = 0.045f;

        RaycastHit hit;
        float prevCompression, spinAngle, visualLength = -1f;
        readonly RaycastHit[] hits = new RaycastHit[8];

        public void UpdateSuspension(CarController car, Rigidbody rb, float dt)
        {
            Vector3 up = car.transform.up;
            Vector3 origin = transform.position;
            float maxDist = restLength + radius;

            int n = Physics.RaycastNonAlloc(origin, -up, hits, maxDist, ~0, QueryTriggerInteraction.Ignore);
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider.attachedRigidbody == rb) continue;
                if (hits[i].distance < bestDist) { bestDist = hits[i].distance; best = i; }
            }

            if (best < 0)
            {
                Grounded = false;
                Compression = 0f;
                Load = 0f;
                prevCompression = 0f;
                return;
            }

            hit = hits[best];
            Grounded = true;
            Surface = car.GetSurface(hit.collider);

            float bump = Surface.roughness * (Mathf.PerlinNoise(hit.point.x * 1.3f, hit.point.z * 1.3f) - 0.5f) * 2f;
            float length = hit.distance - radius - bump;
            float rawCompression = restLength - length;
            Compression = Mathf.Clamp(rawCompression, 0f, restLength);

            float velocity = (Compression - prevCompression) / dt;
            float force = springRate * Compression + damperRate * velocity;
            if (rawCompression > restLength) force += (rawCompression - restLength) * bumpStopRate;
            Load = Mathf.Max(0f, force);
            prevCompression = Compression;

            rb.AddForceAtPosition(up * Load, origin);
        }

        public void UpdateTyre(Rigidbody rb, Transform car, float dt, float driveTorque, float brakeTorque,
            bool abs, float gripScale, int substeps, float forceLift)
        {
            if (!Grounded)
            {
                Omega += driveTorque / inertia * dt;
                ApplyBrake(brakeTorque, dt);
                Omega *= 1f - 0.3f * dt;
                SlipRatio = SlipAngle = CombinedSlip = ContactSpeed = 0f;
                return;
            }

            Vector3 normal = hit.normal;
            Vector3 fwd = Quaternion.AngleAxis(SteerAngle, car.up) * car.forward;
            fwd = Vector3.ProjectOnPlane(fwd, normal).normalized;
            Vector3 right = Vector3.Cross(normal, fwd);
            Vector3 pv = rb.GetPointVelocity(hit.point);
            float vLong = Vector3.Dot(pv, fwd);
            float vLat = Vector3.Dot(pv, right);
            ContactSpeed = pv.magnitude;

            SurfaceProfile s = Surface;
            float fMax = s.grip * gripScale * Load;
            // Below ~3 m/s use a fixed reference speed so slip doesn't explode at standstill.
            float refSpeed = Mathf.Max(Mathf.Abs(vLong), 3f);
            float alpha = Mathf.Atan2(vLat, refSpeed) * Mathf.Rad2Deg;
            float sy = alpha / s.peakSlipAngle;

            float sdt = dt / substeps;
            float sumFx = 0f, sumFy = 0f, sumRho = 0f, slip = 0f;
            for (int i = 0; i < substeps; i++)
            {
                slip = (Omega * radius - vLong) / refSpeed;
                float sx = slip / s.peakSlipRatio;
                float rho = Mathf.Sqrt(sx * sx + sy * sy);
                float fx = 0f, fy = 0f;
                if (rho > 1e-5f)
                {
                    float k = fMax * GripCurve(rho, s.slideGrip) / rho;
                    fx = k * sx;
                    fy = -k * sy;
                }

                Omega += (driveTorque - fx * radius) / inertia * sdt;
                float bt = brakeTorque;
                if (abs && slip < -s.peakSlipRatio * 1.4f) bt *= 0.15f;
                ApplyBrake(bt, sdt);

                sumFx += fx;
                sumFy += fy;
                sumRho += rho;
            }

            SlipRatio = slip;
            SlipAngle = alpha;
            CombinedSlip = sumRho / substeps;

            float rolling = -s.rollingResistance * Load * Mathf.Clamp(vLong, -1f, 1f);
            Vector3 force = fwd * (sumFx / substeps + rolling) + right * (sumFy / substeps);
            rb.AddForceAtPosition(force, hit.point + car.up * forceLift);
        }

        void ApplyBrake(float torque, float dt)
        {
            float dw = torque / inertia * dt;
            if (Mathf.Abs(Omega) <= dw) Omega = 0f;
            else Omega -= Mathf.Sign(Omega) * dw;
        }

        /// <summary>Normalised grip vs normalised combined slip: rises to 1 at peak, falls to slideGrip.</summary>
        static float GripCurve(float rho, float slide)
        {
            if (rho < 1f) return Mathf.Sin(rho * Mathf.PI * 0.5f);
            return slide + (1f - slide) * Mathf.Exp(-(rho - 1f) * 1.2f);
        }

        public void ResetState()
        {
            Omega = 0f;
            prevCompression = 0f;
        }

        public void UpdateVisual(float dt)
        {
            if (visual == null) return;
            float len = Grounded ? restLength - Compression : restLength;
            if (visualLength < 0f || dt <= 0f) visualLength = len;
            else visualLength = Mathf.Lerp(visualLength, len, 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, visualSmoothing)));
            visual.localPosition = Vector3.down * visualLength;
            spinAngle = Mathf.Repeat(spinAngle + Omega * Mathf.Rad2Deg * dt, 360f);
            visual.localRotation = Quaternion.Euler(0f, SteerAngle, 0f) * Quaternion.Euler(spinAngle, 0f, 0f);
        }
    }
}
