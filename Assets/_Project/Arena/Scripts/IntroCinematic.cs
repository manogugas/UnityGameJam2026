using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarArena
{
    /// <summary>
    /// Opening sequence driven by Cinemachine cameras and blends:
    ///  1. Overview: fade in from black, slow orbit over the arena (handheld noise), title card.
    ///  2. Gate: the camera swoops down to a tele shot of the player's gate, tracking the car as the
    ///     portcullis rises and it rolls out.
    ///  3. Chase: a follow camera behind the car.
    ///  4. Driver: blends into the driver's seat camera and hands over control.
    /// The car is only heard from outside during the gate/chase shots (silent during the overview).
    /// Letterbox bars frame the whole thing. Space / Enter / Escape skips (hard cut).
    /// </summary>
    public class IntroCinematic : MonoBehaviour
    {
        public CinemachineBrain brain;
        public CarController player;
        public ArenaGate playerGate;
        public ArenaDirector director;
        public ArenaCrowd crowd;

        [Header("Cameras")]
        public CinemachineCamera overviewCam;
        public CinemachineCamera gateCam;
        public CinemachineCamera chaseCam;
        public CinemachineCamera driverCam;

        [Header("Timing (s)")]
        public float overviewTime = 5.5f;
        public float gateTime = 7f;
        public float chaseTime = 2.2f;
        public float handoverTime = 1.8f;

        [Header("Overview orbit")]
        public float orbitStartDeg = 200f;
        public float orbitSweepDeg = 70f;

        [Header("Presentation")]
        public string title = "THE ARENA";
        [Range(0f, 0.2f)] public float letterbox = 0.11f;
        public float fadeInTime = 1.2f;

        public bool Playing { get; private set; }

        const int Off = 0, On = 100;

        CarInput playerInput;
        CarAudio playerAudio;
        CarHud hud;
        CombatHud combatHud;
        CinemachineOrbitalFollow orbit;
        CinemachineBlenderSettings customBlends;
        bool skip;
        float startTime, bars = 1f, titleAlpha, fade = 1f;
        GUIStyle titleStyle;

        void Awake()
        {
            playerInput = player.GetComponent<CarInput>();
            playerAudio = player.GetComponent<CarAudio>();
            hud = player.GetComponent<CarHud>();
            combatHud = player.GetComponent<CombatHud>();
            if (overviewCam != null) orbit = overviewCam.GetComponent<CinemachineOrbitalFollow>();
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
            startTime = Time.time;
            if (hud != null) hud.enabled = false;
            if (combatHud != null) combatHud.enabled = false;
            if (crowd != null) crowd.SetCabin(false);
            if (playerAudio != null) { playerAudio.SetExterior(true); playerAudio.masterGain = 0f; }
            Hold(true);
            SetPriorities(overview: On);

            // 1. Overview: orbit while the crowd warms up.
            if (crowd != null) crowd.Cheer(0.4f);
            for (float t = 0f; t < overviewTime && !skip; t += Time.deltaTime)
            {
                if (orbit != null) orbit.HorizontalAxis.Value = orbitStartDeg + orbitSweepDeg * Smooth(t / overviewTime);
                titleAlpha = Mathf.Clamp01((t - 0.8f) / 0.8f) * Mathf.Clamp01((overviewTime - 0.6f - t) / 0.8f);
                yield return null;
            }
            titleAlpha = 0f;

            // 2. Swoop down to the gate (the brain's custom blend does the move); the car becomes audible.
            SetPriorities(gate: On);
            if (playerAudio != null) playerAudio.masterGain = 1f;
            playerGate.Open();
            bool rolling = false, cheered = false;
            float passedAt = -1f;
            for (float t = 0f; t < gateTime && !skip; t += Time.deltaTime)
            {
                if (!rolling && t > 1.6f && (playerGate.IsFullyOpen || t > 3.5f)) { rolling = true; Drive(0.45f, 0f); }
                if (!cheered && playerGate.HasPassed(player.transform.position, 0f)) { cheered = true; if (crowd != null) crowd.Cheer(1f); }
                if (playerGate.HasPassed(player.transform.position, 6f) && passedAt < 0f) passedAt = t;
                if (passedAt >= 0f && t - passedAt > 0.8f) break;
                yield return null;
            }

            // 3. Chase camera behind the car.
            if (!skip)
            {
                SetPriorities(chase: On);
                for (float t = 0f; t < chaseTime && !skip; t += Time.deltaTime)
                {
                    if (playerGate.HasPassed(player.transform.position, 16f)) Drive(0f, 1f);
                    yield return null;
                }
            }

            // 4. Into the driver's seat.
            Drive(0f, 1f);
            if (skip) HardCut();
            SetPriorities();
            if (crowd != null) crowd.SetCabin(true);
            if (playerAudio != null) playerAudio.SetExterior(false);
            for (float t = 0f; t < handoverTime && !skip; t += Time.deltaTime) yield return null;

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
            SetPriorities();
            playerGate.Close();
            if (crowd != null) crowd.SetCabin(true);
            if (playerAudio != null) { playerAudio.SetExterior(false); playerAudio.masterGain = 1f; }
            if (hud != null) hud.enabled = true;
            if (combatHud != null) combatHud.enabled = true;
            Hold(false);
            if (director != null) director.BeginSpawning();
            titleAlpha = 0f;
            Playing = false;
        }

        /// <summary>Skip: drop the custom blends for a moment so the brain cuts instead of blending.</summary>
        void HardCut()
        {
            if (brain == null) return;
            if (customBlends == null) customBlends = brain.CustomBlends;
            var def = brain.DefaultBlend;
            brain.CustomBlends = null;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            StartCoroutine(RestoreBlends(def));
        }

        IEnumerator RestoreBlends(CinemachineBlendDefinition def)
        {
            yield return null;
            yield return null;
            brain.CustomBlends = customBlends;
            brain.DefaultBlend = def;
        }

        void SetPriorities(int overview = Off, int gate = Off, int chase = Off)
        {
            if (overviewCam != null) overviewCam.Priority.Value = overview;
            if (gateCam != null) gateCam.Priority.Value = gate;
            if (chaseCam != null) chaseCam.Priority.Value = chase;
            if (driverCam != null) driverCam.Priority.Value = 10;
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

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            float dt = Time.unscaledDeltaTime;
            bars = Mathf.MoveTowards(bars, Playing ? 1f : 0f, dt * 1.2f);
            fade = Playing ? Mathf.Clamp01(1f - (Time.time - startTime) / fadeInTime) : 0f;
            if (bars <= 0f && fade <= 0f) return;

            float w = Screen.width, h = Screen.height, bh = h * letterbox * Smooth(bars);
            var old = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, w, bh), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, h - bh, w, bh), Texture2D.whiteTexture);
            if (fade > 0f)
            {
                GUI.color = new Color(0f, 0f, 0f, fade);
                GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            }
            if (titleAlpha > 0f)
            {
                GUI.color = Color.white; // the bars' black tint would otherwise darken the text
                if (titleStyle == null)
                    titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 72, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                titleStyle.fontSize = Mathf.RoundToInt(h * 0.11f);
                titleStyle.normal.textColor = new Color(0f, 0f, 0f, titleAlpha * 0.6f);
                GUI.Label(new Rect(3, h * 0.38f + 3, w, h * 0.2f), title, titleStyle);
                titleStyle.normal.textColor = new Color(0.95f, 0.85f, 0.6f, titleAlpha);
                GUI.Label(new Rect(0, h * 0.38f, w, h * 0.2f), title, titleStyle);
                if (Playing)
                {
                    var hint = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(h * 0.022f), alignment = TextAnchor.MiddleCenter };
                    hint.normal.textColor = new Color(1f, 1f, 1f, titleAlpha * 0.7f);
                    GUI.Label(new Rect(0, h - bh - h * 0.06f, w, h * 0.05f), "Space to skip", hint);
                }
            }
            GUI.color = old;
        }

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    }
}
