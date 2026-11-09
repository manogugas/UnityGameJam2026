using Unity.Cinemachine;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Experimental third-person chase camera (toggle and tune it from the F3 debug menu, or F4).
    /// Drives its own Cinemachine camera: sits behind and above the car, swings round lazily, pulls back
    /// with speed, Q/E/Alt (and reversing) orbit it like the driver's head, and it pulls in when a wall
    /// is in the way. While it's live the crowd and the car's sound are heard from outside.
    /// Only takes over during a fight: never in the garage or during the intro.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class ChaseCamera : MonoBehaviour
    {
        public CarController car;
        public CarInput input;
        public CinemachineCamera vcam;
        public IntroCinematic intro;
        public CampaignDirector campaign;
        public ArenaCrowd crowd;
        public CarAudio carAudio;

        [Tooltip("Player wants the third-person view (the debug menu sets this).")]
        public bool enabledByUser;
        public int livePriority = 50;

        [Header("Framing")]
        [Range(3f, 15f)] public float distance = 6.5f;
        [Range(0.5f, 6f)] public float height = 2.2f;
        [Range(0f, 2.5f)] public float lookHeight = 1.0f;
        [Range(40f, 100f)] public float fov = 66f;
        [Tooltip("Extra distance per m/s of speed.")]
        [Range(0f, 0.1f)] public float speedPullback = 0.035f;

        [Header("Smoothing")]
        [Tooltip("How quickly the camera swings round behind the car (higher = stiffer).")]
        [Range(0.5f, 15f)] public float rotationDamping = 4f;
        [Range(0.5f, 30f)] public float positionDamping = 10f;
        public float sideLookAngle = 85f;
        public float backLookAngle = 180f;
        public float lookSmoothTime = 0.15f;

        [Header("Collision")]
        public float collisionRadius = 0.35f;

        public bool Live { get; private set; }

        float heading, lookYaw, lookVel;
        Vector3 smoothPos;
        readonly RaycastHit[] hits = new RaycastHit[16];

        void Start()
        {
            if (intro == null) intro = FindAnyObjectByType<IntroCinematic>();
            if (campaign == null) campaign = FindAnyObjectByType<CampaignDirector>();
            if (crowd == null) crowd = FindAnyObjectByType<ArenaCrowd>();
            if (vcam != null) vcam.Priority.Value = 0;
        }

        bool Allowed()
        {
            if (!enabledByUser || car == null || vcam == null) return false;
            if (intro != null && intro.Playing) return false;
            if (campaign != null && campaign.isActiveAndEnabled)
                return campaign.CurrentPhase == CampaignDirector.Phase.Match || campaign.CurrentPhase == CampaignDirector.Phase.Result;
            return true;
        }

        void LateUpdate()
        {
            bool live = Allowed();
            if (live != Live) SetLive(live);
            if (!Live) return;

            float dt = Time.deltaTime;
            var t = car.transform;
            var rb = car.Body;

            // Heading follows the car's nose, swinging round lazily.
            float carYaw = Mathf.Atan2(t.forward.x, t.forward.z) * Mathf.Rad2Deg;
            heading = Mathf.LerpAngle(heading, carYaw, 1f - Mathf.Exp(-rotationDamping * dt));

            float target = 0f;
            if (input != null)
            {
                if (input.LookBack || car.ReversingView) target = backLookAngle;
                else if (input.LookLeft && !input.LookRight) target = -sideLookAngle;
                else if (input.LookRight && !input.LookLeft) target = sideLookAngle;
            }
            lookYaw = Mathf.SmoothDamp(lookYaw, target, ref lookVel, lookSmoothTime);

            float speed = rb != null ? rb.linearVelocity.magnitude : 0f;
            float dist = distance + speed * speedPullback;
            Quaternion rot = Quaternion.Euler(0f, heading + lookYaw, 0f);
            Vector3 pivot = t.position + Vector3.up * lookHeight;
            Vector3 desired = t.position + Vector3.up * height - rot * Vector3.forward * dist;

            smoothPos = Vector3.Lerp(smoothPos, desired, 1f - Mathf.Exp(-positionDamping * dt));
            Vector3 pos = PullIn(pivot, smoothPos);

            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(pivot + rot * Vector3.forward * 4f - pos, Vector3.up);
            var lens = vcam.Lens;
            lens.FieldOfView = fov;
            vcam.Lens = lens;
        }

        /// <summary>Move the camera in front of any wall between the car and it (ignores the car itself).</summary>
        Vector3 PullIn(Vector3 pivot, Vector3 pos)
        {
            Vector3 d = pos - pivot;
            float len = d.magnitude;
            if (len < 0.01f) return pos;
            int n = Physics.SphereCastNonAlloc(pivot, collisionRadius, d / len, hits, len, ~0, QueryTriggerInteraction.Ignore);
            float best = len;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.distance <= 0f) continue;
                var body = h.collider.attachedRigidbody;
                if (body != null && (body == car.Body || body.mass < 200f)) continue; // ourselves, debris, props
                best = Mathf.Min(best, h.distance);
            }
            return pivot + d / len * Mathf.Max(1.2f, best);
        }

        void SetLive(bool live)
        {
            Live = live;
            if (vcam != null) vcam.Priority.Value = live ? livePriority : 0;
            if (crowd != null) crowd.SetCabin(!live);
            if (carAudio != null) carAudio.SetExterior(live);
            if (live)
            {
                var t = car.transform;
                heading = Mathf.Atan2(t.forward.x, t.forward.z) * Mathf.Rad2Deg;
                lookYaw = 0f;
                smoothPos = t.position + Vector3.up * height - Quaternion.Euler(0f, heading, 0f) * Vector3.forward * distance;
            }
        }
    }
}
