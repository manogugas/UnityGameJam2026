using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarArena
{
    /// <summary>
    /// Opening sequence: a sweeping shot over the arena, then the player's gate opens and the car rolls
    /// in, then the camera flies into the driver's seat and control is handed over (enemies start
    /// coming in). Space / Enter / Escape skips it.
    /// </summary>
    public class IntroCinematic : MonoBehaviour
    {
        public Camera introCamera;
        public CarController player;
        public ArenaGate playerGate;
        public ArenaDirector director;
        public ArenaCrowd crowd;

        [Header("Timing (s)")]
        public float overviewTime = 5f;
        public float gateTime = 6f;
        public float flyTime = 2.2f;

        [Header("Overview shot")]
        public Vector3 arenaCentre = Vector3.zero;
        public float orbitRadius = 115f;
        public float orbitHeight = 62f;
        public float orbitStartDeg = 215f;
        public float orbitSweepDeg = 55f;

        public bool Playing { get; private set; }

        Camera playerCamera;
        CarInput playerInput;
        CarHud hud;
        CombatHud combatHud;
        bool skip;

        void Awake()
        {
            playerInput = player.GetComponent<CarInput>();
            hud = player.GetComponent<CarHud>();
            combatHud = player.GetComponent<CombatHud>();
            var head = player.GetComponentInChildren<CarCamera>();
            playerCamera = head != null ? head.GetComponent<Camera>() : null;
        }

        void Start() => StartCoroutine(Run());

        void Update()
        {
            var kb = Keyboard.current;
            if (Playing && kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame))
                skip = true;
        }

        IEnumerator Run()
        {
            Playing = true;
            if (playerCamera != null) playerCamera.enabled = false;
            introCamera.enabled = true;
            if (hud != null) hud.enabled = false;
            if (combatHud != null) combatHud.enabled = false;
            Hold(true);

            // 1. Sweep over the arena while the crowd warms up.
            if (crowd != null) crowd.Cheer(0.4f);
            for (float t = 0f; t < overviewTime && !skip; t += Time.deltaTime)
            {
                float a = (orbitStartDeg + orbitSweepDeg * Smooth(t / overviewTime)) * Mathf.Deg2Rad;
                Vector3 pos = arenaCentre + new Vector3(Mathf.Cos(a) * orbitRadius, orbitHeight, Mathf.Sin(a) * orbitRadius);
                introCamera.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(arenaCentre - pos));
                yield return null;
            }

            // 2. Look at the player's gate: it opens and the car rolls out of the tunnel.
            Transform g = playerGate.transform;
            Vector3 shotPos = g.position + g.forward * 22f + g.right * 9f + Vector3.up * 3.5f;
            introCamera.transform.position = shotPos;
            playerGate.Open();
            bool rolling = false, cheered = false;
            for (float t = 0f; t < gateTime && !skip; t += Time.deltaTime)
            {
                if (!rolling && (playerGate.IsFullyOpen || t > 2f)) { rolling = true; Drive(0.45f, 0f); }
                if (!cheered && playerGate.HasPassed(player.transform.position, 0f)) { cheered = true; if (crowd != null) crowd.Cheer(1f); }
                if (playerGate.HasPassed(player.transform.position, 14f)) Drive(0f, 0.6f);
                Vector3 look = Vector3.Lerp(g.position + Vector3.up * 2.5f, player.transform.position + Vector3.up * 0.8f, Smooth(t / 2.5f));
                introCamera.transform.rotation = Quaternion.Slerp(introCamera.transform.rotation,
                    Quaternion.LookRotation(look - introCamera.transform.position), 1f - Mathf.Exp(-Time.deltaTime * 6f));
                yield return null;
            }

            // 3. Fly into the driver's seat.
            if (!skip && playerCamera != null)
            {
                Drive(0f, 0.6f);
                Vector3 fromPos = introCamera.transform.position;
                Quaternion fromRot = introCamera.transform.rotation;
                float fromFov = introCamera.fieldOfView;
                for (float t = 0f; t < flyTime && !skip; t += Time.deltaTime)
                {
                    float k = Smooth(t / flyTime);
                    Transform head = playerCamera.transform;
                    introCamera.transform.SetPositionAndRotation(Vector3.Lerp(fromPos, head.position, k), Quaternion.Slerp(fromRot, head.rotation, k));
                    introCamera.fieldOfView = Mathf.Lerp(fromFov, playerCamera.fieldOfView, k);
                    yield return null;
                }
            }

            Finish();
        }

        void Finish()
        {
            // Skipped while still in the tunnel: put the car just inside the arena.
            if (!playerGate.HasPassed(player.transform.position, 8f))
            {
                var rb = player.Body;
                Vector3 p = playerGate.entryPoint.position + Vector3.up * 0.3f;
                Quaternion r = Quaternion.LookRotation(playerGate.transform.forward, Vector3.up);
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = p;
                rb.rotation = r;
                player.transform.SetPositionAndRotation(p, r);
            }
            playerGate.Close();
            introCamera.enabled = false;
            if (playerCamera != null) playerCamera.enabled = true;
            if (hud != null) hud.enabled = true;
            if (combatHud != null) combatHud.enabled = true;
            Hold(false);
            if (director != null) director.BeginSpawning();
            Playing = false;
        }

        void Hold(bool hold)
        {
            if (playerInput == null) return;
            playerInput.overrideDriving = hold;
            Drive(0f, 0f);
            playerInput.overrideHandbrake = hold;
        }

        /// <summary>Throttle forward, or slow down. Uses the handbrake rather than the brake pedal:
        /// holding the brake at a standstill would engage reverse.</summary>
        void Drive(float throttle, float slow)
        {
            if (playerInput == null) return;
            playerInput.overrideSteer = 0f;
            playerInput.overrideThrottle = throttle;
            playerInput.overrideBrake = 0f;
            playerInput.overrideHandbrake = slow > 0f;
        }

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    }
}
