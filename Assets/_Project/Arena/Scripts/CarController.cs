using System.Collections.Generic;
using UnityEngine;

namespace CarArena
{
    public enum DriveType { FWD, RWD, AWD }

    /// <summary>
    /// Semi-realistic car: raycast wheels, power-limited engine with an invisible automatic gearbox,
    /// S brakes then reverses, FWD/RWD/AWD torque split.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CarController : MonoBehaviour
    {
        [Header("References")]
        public CarInput input;
        public CarWheel[] wheels;
        public Transform steeringWheel;
        public SurfaceProfile defaultSurface;

        [Header("Drivetrain")]
        public DriveType driveType = DriveType.RWD;
        [Range(0f, 1f)] public float awdFrontShare = 0.4f;
        public float maxPowerKW = 115f;
        [Tooltip("Total torque at the driven wheels in first gear (Nm).")]
        public float maxWheelTorque = 2600f;
        public float topSpeedKmh = 190f;
        public float reverseTopSpeedKmh = 40f;
        [Tooltip("Engine braking torque per driven wheel when off throttle.")]
        public float engineBraking = 90f;
        public float throttleRise = 3.5f;
        public float throttleFall = 8f;

        [Header("Brakes")]
        [Tooltip("Total brake torque across all wheels (Nm).")]
        public float brakeTorque = 8000f;
        [Range(0f, 1f)] public float frontBrakeBias = 0.65f;
        public float handbrakeTorque = 3500f;
        public bool abs = true;

        [Header("Assists")]
        [Tooltip("Cuts drive torque on a wheel spinning past tcSlipLimit x the surface's peak slip.")]
        public bool tractionControl = true;
        public float tcSlipLimit = 2.5f;
        [Tooltip("With TC on, driven tyres sliding past this combined slip (1 = peak grip) lose power.")]
        public float stabilitySlipLimit = 1.25f;
        [Tooltip("With assists on, limit front slip angle to this multiple of the surface's peak slip angle.")]
        public bool steeringAssist = true;
        public float steeringAssistSlip = 1.15f;
        [Tooltip("How strongly the steering assist counter-steers when the rear slides.")]
        public float counterSteerGain = 1.5f;
        [Tooltip("Steering assist stays off this long after the handbrake is released...")]
        public float handbrakeAssistHold = 0.6f;
        [Tooltip("...then fades back in over this many seconds.")]
        public float handbrakeAssistFade = 0.6f;

        [Header("Steering")]
        public float maxSteerAngle = 34f;
        public float highSpeedSteerAngle = 12.5f;
        [Tooltip("Speed (m/s) at which steering lock reaches the high speed angle.")]
        public float steerSpeedRef = 35f;
        public float steerRate = 2.8f;
        public float steerReturnRate = 4.5f;
        public float steeringWheelRatio = 13f;

        [Header("Chassis")]
        public float mass = 1250f;
        public Vector3 centerOfMass = new Vector3(0f, -0.25f, 0f);
        [Tooltip("FWD cars carry the engine over the front axle: COM moves forward by this much.")]
        public float fwdFrontWeightShift = 0.2f;
        public float antiRollFront = 9000f;
        public float antiRollRear = 6000f;
        public float dragCoefficient = 0.42f;
        [Tooltip("Aero downforce, N per (m/s)^2. Adds grip as speed rises.")]
        public float downforce = 4f;
        [Tooltip("Share of downforce on the front axle. >0.5 = sharper turn-in at speed.")]
        [Range(0f, 1f)] public float downforceFrontShare = 0.54f;
        [Tooltip("Tyre forces are applied this far above the contact patch (less body roll).")]
        public float tyreForceLift = 0.15f;
        [Range(0.5f, 1.5f)] public float gripScale = 1f;
        [Tooltip("Rear tyre grip relative to front. >1 = more stable, less tail-happy.")]
        [Range(0.8f, 1.3f)] public float rearGripScale = 1.08f;
        [Range(4, 32)] public int tyreSubsteps = 16;

        public float ForwardSpeed { get; private set; }
        public float SpeedKmh => Mathf.Abs(ForwardSpeed) * 3.6f;
        public bool ReverseGear { get; private set; }
        public bool ReversingView { get; private set; }
        public int Gear { get; private set; } = 1;
        public float Rpm { get; private set; } = 900f;
        public float ThrottleOut { get; private set; }
        public float BrakeOut { get; private set; }
        public float Steer { get; private set; }
        public Rigidbody Body => rb;
        /// <summary>Wrecked: no throttle, steering or brakes from the driver; the car just rolls to a stop.</summary>
        public bool EngineDead { get; set; }
        /// <summary>Traction control is cutting power on a driven wheel right now.</summary>
        public bool TractionControlActive { get; private set; }
        /// <summary>A driven wheel is spinning or sliding well past peak grip (TC on or off).</summary>
        public bool WheelSpin { get; private set; }

        [Header("Boost (boost pads)")]
        [Tooltip("Forward acceleration (m/s^2) while boosting, on top of the engine.")]
        public float boostAccel = 6.5f;
        [Tooltip("Seconds after a boost ends that rams still hit harder (CarHealth.ramBoostBonus).")]
        public float ramBoostWindow = 2f;
        public float BoostTime { get; private set; }
        public bool Boosting => BoostTime > 0f;
        /// <summary>Boosting, or just finished: rams deal extra damage.</summary>
        public bool RamBoosted => BoostTime > 0f || Time.time < ramBoostUntil;
        float ramBoostUntil = -10f;

        /// <summary>Shove the car forward for a while (boost pads); rams hit harder until shortly after.</summary>
        public void Boost(float seconds)
        {
            BoostTime = Mathf.Max(BoostTime, seconds);
            ramBoostUntil = Time.time + seconds + ramBoostWindow;
        }

        static readonly float[] GearTopSpeed = { 0f, 11f, 19f, 28f, 38f, 48f, 60f };
        const float IdleRpm = 900f, RedlineRpm = 6800f;

        Rigidbody rb;
        float[] tcFactor;
        float lastHandbrakeTime = -100f;
        CarWheel frontLeft, frontRight, rearLeft, rearRight;
        readonly Dictionary<Collider, SurfaceProfile> surfaceCache = new Dictionary<Collider, SurfaceProfile>();

        void Awake()
        {
            Application.runInBackground = true;
            rb = GetComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            ApplyCenterOfMass();

            foreach (var w in wheels)
            {
                bool left = transform.InverseTransformPoint(w.transform.position).x < 0f;
                if (w.IsFront) { if (left) frontLeft = w; else frontRight = w; }
                else { if (left) rearLeft = w; else rearRight = w; }
            }
        }

        /// <summary>The campaign picks the drivetrain once per run: the T debug key can't change it.</summary>
        [System.NonSerialized] public bool drivetrainLocked;
        /// <summary>Until the traction control upgrade is bought the assists stay on (B does nothing).</summary>
        [System.NonSerialized] public bool assistsLocked;

        public void SetDriveType(DriveType type)
        {
            driveType = type;
            ApplyCenterOfMass();
        }

        /// <summary>Lock the assists on (no traction control upgrade) or let the driver switch them.</summary>
        public void SetAssistsLocked(bool locked)
        {
            assistsLocked = locked;
            if (locked) { tractionControl = true; abs = true; }
        }

        public void ApplyCenterOfMass()
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            rb.centerOfMass = centerOfMass + Vector3.forward * (driveType == DriveType.FWD ? fwdFrontWeightShift : 0f);
        }

        public SurfaceProfile GetSurface(Collider c)
        {
            if (!surfaceCache.TryGetValue(c, out var s))
            {
                var tag = c.GetComponentInParent<DrivingSurface>();
                s = tag != null && tag.profile != null ? tag.profile : defaultSurface;
                surfaceCache[c] = s;
            }
            return s;
        }

        void Update()
        {
            if (input.CycleDrivePressed && !drivetrainLocked)
            {
                driveType = (DriveType)(((int)driveType + 1) % 3);
                ApplyCenterOfMass();
            }
            if (input.ResetPressed && !EngineDead) ResetCar();
            if (input.ToggleAssistsPressed && !assistsLocked)
            {
                tractionControl = !tractionControl;
                abs = tractionControl;
            }

            float dt = Time.deltaTime;
            foreach (var w in wheels) w.UpdateVisual(dt);
            if (steeringWheel != null)
                steeringWheel.localRotation = Quaternion.Euler(0f, 0f, -Steer * maxSteerAngle * steeringWheelRatio * 0.5f);
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            ForwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);

            float throttleIn = EngineDead ? 0f : input.Throttle;
            float brakeIn = EngineDead ? 0f : input.Brake;

            // Gear selection: S brakes to a stop, then holding it engages reverse; W does the opposite.
            if (!ReverseGear && brakeIn > 0.1f && throttleIn < 0.1f && ForwardSpeed < 0.6f) ReverseGear = true;
            else if (ReverseGear && throttleIn > 0.1f && brakeIn < 0.1f && ForwardSpeed > -0.6f) ReverseGear = false;

            float driveIn = ReverseGear ? brakeIn : throttleIn;
            float brakePedal = ReverseGear ? throttleIn : brakeIn;
            ThrottleOut = Mathf.MoveTowards(ThrottleOut, driveIn, (driveIn > ThrottleOut ? throttleRise : throttleFall) * dt);
            BrakeOut = brakePedal;
            ReversingView = ReverseGear && (ForwardSpeed < -0.3f || brakeIn > 0.1f);

            UpdateSteering(dt);

            foreach (var w in wheels) w.UpdateSuspension(this, rb, dt);
            AntiRoll(frontLeft, frontRight, antiRollFront);
            AntiRoll(rearLeft, rearRight, antiRollRear);

            DriveWheels(dt);

            if (BoostTime > 0f)
            {
                BoostTime -= dt;
                bool grounded = false;
                foreach (var w in wheels) grounded |= w.Grounded;
                if (grounded && !EngineDead) rb.AddForce(transform.forward * boostAccel, ForceMode.Acceleration);
            }

            Vector3 v = rb.linearVelocity;
            rb.AddForce(-v * v.magnitude * dragCoefficient);
            ApplyDownforce();

            UpdateRpm(dt);
        }

        void ApplyDownforce()
        {
            if (frontLeft == null || rearLeft == null) return;
            float total = downforce * ForwardSpeed * ForwardSpeed;
            Vector3 down = -transform.up;
            Vector3 front = (frontLeft.transform.position + frontRight.transform.position) * 0.5f;
            Vector3 rear = (rearLeft.transform.position + rearRight.transform.position) * 0.5f;
            rb.AddForceAtPosition(down * (total * downforceFrontShare), front);
            rb.AddForceAtPosition(down * (total * (1f - downforceFrontShare)), rear);
        }

        void UpdateSteering(float dt)
        {
            float target = EngineDead ? 0f : input.Steer;
            bool returning = Mathf.Abs(target) < Mathf.Abs(Steer) || Mathf.Sign(target) != Mathf.Sign(Steer);
            Steer = Mathf.MoveTowards(Steer, target, (returning ? steerReturnRate : steerRate) * dt);
            float lockAngle = Mathf.Lerp(maxSteerAngle, highSpeedSteerAngle, Mathf.Abs(ForwardSpeed) / steerSpeedRef);
            float angle = Steer * lockAngle;

            // The handbrake means "I want the car to rotate": the assist would counter-steer that away
            // (the car snaps back like a bungee). Switch it off while held and fade it back in after release.
            if (input.Handbrake) lastHandbrakeTime = Time.fixedTime;
            float assistWeight = Mathf.Clamp01((Time.fixedTime - lastHandbrakeTime - handbrakeAssistHold) / handbrakeAssistFade);

            // Steering assist: keep the front tyres near peak slip angle relative to where the front axle is
            // actually travelling. Full keyboard lock then turns as hard as grip allows instead of ploughing,
            // and it lets the wheels follow a slide (auto counter-steer).
            if (tractionControl && steeringAssist && assistWeight > 0f && ForwardSpeed > 5f && frontLeft != null && frontRight != null)
            {
                float raw = angle;
                Vector3 axle = (frontLeft.transform.position + frontRight.transform.position) * 0.5f;
                Vector3 v = transform.InverseTransformDirection(rb.GetPointVelocity(axle));
                float travel = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                var surf = frontLeft.Grounded && frontLeft.Surface != null ? frontLeft.Surface : defaultSurface;
                float peak = surf != null ? surf.peakSlipAngle : 8f;
                float limit = peak * steeringAssistSlip;

                // If the rear axle is sliding past its peak, shift the window toward counter-steer
                // (what a driver would do), so the fronts stop pulling the car into a spin.
                Vector3 rearAxle = (rearLeft.transform.position + rearRight.transform.position) * 0.5f;
                Vector3 vr = transform.InverseTransformDirection(rb.GetPointVelocity(rearAxle));
                float rearSlip = Mathf.Atan2(vr.x, Mathf.Max(vr.z, 1f)) * Mathf.Rad2Deg;
                float excess = Mathf.Max(0f, Mathf.Abs(rearSlip) - peak * 1.1f);
                float shift = Mathf.Sign(rearSlip) * excess * counterSteerGain;

                angle = Mathf.Clamp(angle, travel - limit + shift, travel + limit + shift);
                angle = Mathf.Clamp(angle, -maxSteerAngle, maxSteerAngle);
                angle = Mathf.Lerp(raw, angle, assistWeight);
            }
            foreach (var w in wheels) w.SteerAngle = w.IsFront ? angle : 0f;
        }

        void AntiRoll(CarWheel left, CarWheel right, float stiffness)
        {
            if (left == null || right == null) return;
            float diff = left.Compression - right.Compression;
            Vector3 f = transform.up * diff * stiffness;
            if (left.Grounded) rb.AddForceAtPosition(f, left.transform.position);
            if (right.Grounded) rb.AddForceAtPosition(-f, right.transform.position);
        }

        float FrontShare => driveType == DriveType.FWD ? 1f : driveType == DriveType.RWD ? 0f : awdFrontShare;

        void DriveWheels(float dt)
        {
            float frontShare = FrontShare;
            float wheelRadius = wheels[0].radius;
            float drivenSpeed = 0f, weight = 0f;
            foreach (var w in wheels)
            {
                float share = w.IsFront ? frontShare : 1f - frontShare;
                drivenSpeed += Mathf.Abs(w.WheelSpeed) * share;
                weight += share;
            }
            drivenSpeed /= Mathf.Max(weight, 0.001f);

            float limit = (ReverseGear ? reverseTopSpeedKmh : topSpeedKmh) / 3.6f;
            // P = T * omega  =>  T = P * r / v
            float torque = Mathf.Min(maxWheelTorque, maxPowerKW * 1000f * wheelRadius / Mathf.Max(drivenSpeed, 0.1f));
            if (drivenSpeed > limit) torque = 0f;
            float total = torque * ThrottleOut * (ReverseGear ? -1f : 1f);

            bool hand = !EngineDead && input.Handbrake;
            if (EngineDead) BrakeOut = Mathf.Max(BrakeOut, 0.12f); // seized, dragging wreck
            if (tcFactor == null || tcFactor.Length != wheels.Length)
            {
                tcFactor = new float[wheels.Length];
                for (int k = 0; k < tcFactor.Length; k++) tcFactor[k] = 1f;
            }
            bool tcActive = false, spin = false;
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                float share = w.IsFront ? frontShare : 1f - frontShare;
                float drive = total * share * 0.5f;
                if (share > 0f && ThrottleOut < 0.05f) drive -= Mathf.Clamp(w.Omega, -1f, 1f) * engineBraking * share;
                // Traction control: smoothed per-wheel torque factor (an instant cut oscillates and bogs down).
                float tcTarget = 1f;
                if (tractionControl && w.Grounded && w.Surface != null && ThrottleOut > 0.05f &&
                    Mathf.Sign(w.SlipRatio) == Mathf.Sign(drive))
                {
                    float peak = w.Surface.peakSlipRatio;
                    float over = (Mathf.Abs(w.SlipRatio) - peak * tcSlipLimit) / (peak * 2f);
                    tcTarget = Mathf.Clamp(1f - over, 0.25f, 1f);
                }
                // Stability assist: a driven tyre that is sliding (lateral + longitudinal) loses power,
                // so throttle can't keep the car in a slide while TC is on.
                if (tractionControl && w.Grounded && ThrottleOut > 0.05f)
                {
                    float slide = (w.CombinedSlip - stabilitySlipLimit) / 0.5f;
                    tcTarget = Mathf.Min(tcTarget, Mathf.Clamp01(1f - slide));
                }
                tcFactor[i] = Mathf.MoveTowards(tcFactor[i], tcTarget, (tcTarget < tcFactor[i] ? 6f : 2.5f) * dt);
                drive *= tcFactor[i];
                if (share > 0f && tractionControl && tcFactor[i] < 0.9f) tcActive = true;
                if (share > 0f && w.Grounded && w.Surface != null && ThrottleOut > 0.1f &&
                    (Mathf.Abs(w.SlipRatio) > w.Surface.peakSlipRatio * 2.5f || w.CombinedSlip > 1.6f)) spin = true;
                // Rev limiter for a wheel in the air, so it doesn't land spinning at top speed.
                if (!w.Grounded && Mathf.Abs(w.WheelSpeed) > Mathf.Abs(ForwardSpeed) + 8f && Mathf.Sign(drive) == Mathf.Sign(w.Omega))
                    drive = 0f;

                float brake = BrakeOut * brakeTorque * 0.5f * (w.IsFront ? frontBrakeBias : 1f - frontBrakeBias);
                bool wheelAbs = abs;
                if (hand && !w.IsFront)
                {
                    brake += handbrakeTorque;
                    drive = 0f;
                    wheelAbs = false;
                }
                float grip = gripScale * (w.IsFront ? 1f : rearGripScale);
                w.UpdateTyre(rb, transform, dt, drive, brake, wheelAbs, grip, tyreSubsteps, tyreForceLift);
            }
            TractionControlActive = tcActive;
            WheelSpin = spin;
        }

        void UpdateRpm(float dt)
        {
            float speed = 0f, weight = 0f;
            float frontShare = FrontShare;
            foreach (var w in wheels)
            {
                float share = w.IsFront ? frontShare : 1f - frontShare;
                speed += Mathf.Abs(w.WheelSpeed) * share;
                weight += share;
            }
            speed /= Mathf.Max(weight, 0.001f);

            if (ReverseGear) Gear = 1;
            else
            {
                if (Gear < GearTopSpeed.Length - 1 && speed > GearTopSpeed[Gear] * 0.92f) Gear++;
                else if (Gear > 1 && speed < GearTopSpeed[Gear - 1] * 0.6f) Gear--;
            }
            float target = IdleRpm + speed / GearTopSpeed[Gear] * (RedlineRpm - IdleRpm);
            target = Mathf.Max(target, IdleRpm + ThrottleOut * (Gear == 1 ? 2200f : 600f));
            target = Mathf.Min(target, RedlineRpm + 200f);
            if (EngineDead) target = 0f; // engine stalls: the synth stops firing
            Rpm = Mathf.Lerp(Rpm, target, 1f - Mathf.Exp(-dt * (EngineDead ? 3f : 12f)));
        }

        public void ResetCar()
        {
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = rb.position + Vector3.up * 1.2f;
            rb.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            transform.SetPositionAndRotation(rb.position, rb.rotation);
            foreach (var w in wheels) w.ResetState();
            ReverseGear = false;
            ThrottleOut = 0f;
        }
    }
}
