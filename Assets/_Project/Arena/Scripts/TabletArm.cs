using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Floor-mounted workshop arm beside the car that passes the tablet in through the open passenger
    /// window and holds it up in front of the driver, above the steering wheel (so a cracked windscreen
    /// or a popped hood never covers it). It folds the tablet away again when you drive out.
    /// The move is stepped like a real robot: swing from the folded pose to just outside the window with
    /// the tablet edge-on, slide straight in through the window, then turn it to face the driver. The
    /// tablet is held by its back plate while it passes the window and by its right edge once presented.
    /// The two arm segments follow with a two-bone IK solve each frame; a settle wobble and a servo
    /// whine sell the hydraulics.
    /// </summary>
    public class TabletArm : MonoBehaviour
    {
        [Header("Rig")]
        public Transform car;
        public Transform tablet;
        [Tooltip("Turret on top of the pedestal: yaws toward the tablet.")]
        public Transform turret;
        [Tooltip("Shoulder pivot (child of the turret).")]
        public Transform shoulder;
        [Tooltip("Upper arm: its +Z runs from the shoulder to the elbow.")]
        public Transform upperArm;
        [Tooltip("Forearm: its +Z runs from the elbow to the wrist.")]
        public Transform forearm;
        public Transform elbowJoint, wristJoint;
        [Tooltip("Short link from the wrist joint to the tablet mount: +Z runs wrist -> tablet.")]
        public Transform wristLink;
        public float upperLength = 1.8f;
        public float forearmLength = 1.6f;
        [Tooltip("Wrist joint sits this far out from the tablet mount.")]
        public float wristOffset = 0.28f;
        [Tooltip("Tablet-local mount on the back plate (used while passing the window).")]
        public Vector3 backMount = new Vector3(0f, 0f, 0.03f);
        [Tooltip("Tablet-local mount on the right edge (used once presented).")]
        public Vector3 sideMount = new Vector3(0.3f, 0f, 0.02f);
        [Tooltip("Direction the elbow bends toward, in the car's local space.")]
        public Vector3 poleLocal = new Vector3(0f, 0.25f, -1f);

        [Header("Path")]
        [Tooltip("Folded pose, in this object's local space.")]
        public Vector3 stowedPosition = new Vector3(0.5f, 1.9f, 0.9f);
        public Vector3 stowedEuler = new Vector3(0f, 90f, 0f);
        [Tooltip("Just outside the passenger window (car-local), tablet edge-on.")]
        public Vector3 outsidePosition = new Vector3(1.75f, 0.84f, -0.25f);
        public Vector3 throughEuler = new Vector3(0f, 90f, 0f);
        [Tooltip("Just inside the window (car-local), same orientation.")]
        public Vector3 insidePosition = new Vector3(0.25f, 0.84f, -0.25f);
        [Tooltip("Held in front of the driver (car-local).")]
        public Vector3 presentedPosition = new Vector3(-0.37f, 0.757f, -0.1f);
        public Vector3 presentedEuler = new Vector3(8f, 0f, 0f);
        [Range(0.1f, 0.9f)] public float reachWindowAt = 0.42f;
        [Range(0.1f, 0.95f)] public float throughWindowAt = 0.68f;

        [Header("Motion")]
        public float moveTime = 2.6f;
        [Tooltip("How high the tablet arcs up on the first swing.")]
        public float arcHeight = 0.35f;
        public float settleAmplitude = 0.012f;

        [Header("Sound")]
        [Range(0f, 1f)] public float servoVolume = 0.28f;

        /// <summary>0 = stowed, 1 = presented.</summary>
        public float Progress => progress;
        public bool Presented => target > 0.5f && progress >= 1f;
        public bool Stowed => target < 0.5f && progress <= 0f;

        float progress, target, settleT = 10f;
        AudioSource servo;

        void Awake()
        {
            servo = gameObject.AddComponent<AudioSource>();
            servo.clip = MakeServoClip();
            servo.loop = true;
            servo.playOnAwake = false;
            GameSettings.Route(servo, GameSettings.Channel.Effects);
            servo.spatialBlend = 1f;
            servo.minDistance = 3f;
            servo.maxDistance = 30f;
            servo.volume = 0f;
            Apply(10f);
        }

        public void Present() => target = 1f;
        public void Stow() => target = 0f;

        /// <summary>Jump straight to a pose.</summary>
        public void Snap(bool presented)
        {
            target = progress = presented ? 1f : 0f;
            Apply(10f);
        }

        void LateUpdate()
        {
            float before = progress;
            progress = Mathf.MoveTowards(progress, target, Time.deltaTime / Mathf.Max(0.05f, moveTime));
            bool moving = !Mathf.Approximately(before, progress);
            if (moving && Mathf.Approximately(progress, target)) settleT = 0f; // just arrived: wobble
            settleT += Time.deltaTime;

            if (servo != null)
            {
                servo.volume = Mathf.MoveTowards(servo.volume, moving ? servoVolume : 0f, Time.deltaTime * 3f);
                servo.pitch = 0.85f + 0.3f * Mathf.Sin(progress * Mathf.PI * 3f) * Mathf.Sin(progress * Mathf.PI * 3f);
                if (servo.volume > 0.001f && !servo.isPlaying) servo.Play();
                else if (servo.volume <= 0.001f && servo.isPlaying) servo.Stop();
            }
            Apply(settleT);
        }

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Tablet pose along the stepped path, and how far the grip has moved from the back to the edge.</summary>
        void Evaluate(float p, out Vector3 pos, out Quaternion rot, out float sideGrip)
        {
            Vector3 k0 = transform.TransformPoint(stowedPosition);
            Quaternion r0 = transform.rotation * Quaternion.Euler(stowedEuler);
            if (car == null) { pos = k0; rot = r0; sideGrip = 0f; return; }
            Vector3 k1 = car.TransformPoint(outsidePosition);
            Vector3 k2 = car.TransformPoint(insidePosition);
            Vector3 k3 = car.TransformPoint(presentedPosition);
            Quaternion r1 = car.rotation * Quaternion.Euler(throughEuler);
            Quaternion r3 = car.rotation * Quaternion.Euler(presentedEuler);
            sideGrip = 0f;

            if (p < reachWindowAt)
            {
                float e = Smooth(p / reachWindowAt);
                pos = Vector3.Lerp(k0, k1, e) + Vector3.up * (Mathf.Sin(e * Mathf.PI) * arcHeight);
                rot = Quaternion.Slerp(r0, r1, e);
            }
            else if (p < throughWindowAt)
            {
                float e = Smooth((p - reachWindowAt) / (throughWindowAt - reachWindowAt));
                pos = Vector3.Lerp(k1, k2, e);
                rot = r1;
            }
            else
            {
                float e = Smooth((p - throughWindowAt) / (1f - throughWindowAt));
                pos = Vector3.Lerp(k2, k3, e);
                rot = Quaternion.Slerp(r1, r3, e);
                sideGrip = e;
            }
        }

        void Apply(float sinceArrival)
        {
            if (tablet == null || shoulder == null) return;

            Evaluate(progress, out Vector3 pos, out Quaternion rot, out float side);
            float wobble = settleAmplitude * Mathf.Exp(-sinceArrival * 6f) * Mathf.Sin(sinceArrival * 24f);
            pos += Vector3.up * wobble;
            rot = rot * Quaternion.Euler(wobble * 80f, 0f, 0f);
            tablet.SetPositionAndRotation(pos, rot);

            // Grip: back plate while passing the window, right edge once presented.
            Vector3 mount = pos + rot * Vector3.Lerp(backMount, sideMount, side);
            Vector3 outDir = rot * Vector3.Slerp(Vector3.forward, Vector3.right, side);
            Vector3 wrist = mount + outDir * wristOffset;

            if (turret != null)
            {
                Vector3 flat = wrist - turret.position;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.01f) turret.rotation = Quaternion.LookRotation(flat, Vector3.up);
            }

            // Two-bone IK, elbow bending toward the pole (back along the car, a little up) so the forearm
            // comes in level through the window opening.
            Vector3 s = shoulder.position;
            Vector3 toW = wrist - s;
            float d = Mathf.Clamp(toW.magnitude, Mathf.Abs(upperLength - forearmLength) + 0.01f, upperLength + forearmLength - 0.01f);
            Vector3 dir = toW.normalized;
            Vector3 pole = car != null ? car.TransformDirection(poleLocal) : Vector3.up;
            Vector3 perp = pole - Vector3.Dot(pole, dir) * dir;
            if (perp.sqrMagnitude < 1e-4f) perp = Vector3.Cross(dir, Vector3.right);
            perp.Normalize();
            float cosA = Mathf.Clamp((upperLength * upperLength + d * d - forearmLength * forearmLength) / (2f * upperLength * d), -1f, 1f);
            float a = Mathf.Acos(cosA);
            Vector3 elbow = s + (dir * Mathf.Cos(a) + perp * Mathf.Sin(a)) * upperLength;
            Vector3 wristReached = s + dir * d;

            Orient(upperArm, s, elbow, Vector3.up);
            Orient(forearm, elbow, wristReached, Vector3.up);
            if (elbowJoint != null) elbowJoint.position = elbow;
            if (wristJoint != null) wristJoint.position = wristReached;
            if (wristLink != null) Orient(wristLink, wristReached, mount, rot * Vector3.up);
        }

        static void Orient(Transform t, Vector3 from, Vector3 to, Vector3 up)
        {
            if (t == null) return;
            Vector3 d = to - from;
            if (d.sqrMagnitude < 1e-6f) return;
            t.SetPositionAndRotation(from, Quaternion.LookRotation(d, up));
            var s = t.localScale;
            // Segments are built 1 m long along +Z: stretch to fit.
            t.localScale = new Vector3(s.x, s.y, d.magnitude);
        }

        /// <summary>Hydraulic servo: a low buzz with a whiny overtone and some hiss.</summary>
        static AudioClip MakeServoClip()
        {
            const int rate = 22050;
            int n = rate; // 1 s loop
            var data = new float[n];
            var rng = new System.Random(3);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float buzz = Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.15f;
                float whine = Mathf.Sin(2f * Mathf.PI * 880f * t) * 0.12f;
                lp += ((float)rng.NextDouble() * 2f - 1f - lp) * 0.2f;
                data[i] = (buzz + whine + lp * 0.25f) * 0.6f;
            }
            var clip = AudioClip.Create("Servo", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
