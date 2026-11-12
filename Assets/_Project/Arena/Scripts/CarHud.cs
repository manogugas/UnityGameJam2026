using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarArena
{
    /// <summary>
    /// Player HUD. In normal play only gameplay feedback is drawn (hit flash, WRECKED banner, kill count):
    /// speed, gear and assists live on the dashboard. F3 toggles the debug overlay with the testing info:
    /// speed/rpm/gear, drive type and assists, wheel surfaces, part damage, controls and the enemy roster,
    /// plus the experimental third-person camera's switch and tuning sliders (the mouse is freed while it's open).
    /// F4 toggles the third-person camera directly.
    /// </summary>
    public class CarHud : MonoBehaviour
    {
        public CarController car;
        public CarDamage damage;
        public CarHealth health;
        public ArenaDirector director;
        public ChaseCamera chaseCamera;
        CarGun gun;
        CarBoost boost;
        [Tooltip("Shown under WRECKED. Empty = no hint (the campaign shows its own defeat screen).")]
        public string wreckedHint = "<b>Backspace</b> to repair and get back in";
        [Tooltip("Debug overlay (toggle with F3).")]
        public bool debugVisible;

        GUIStyle big, small, banner, centred, kills;
        float hitFlash, lastHealth = -1f;
        readonly StringBuilder sb = new StringBuilder();
        bool savedCursorVisible;
        CursorLockMode savedCursorLock;

        void Start()
        {
            if (director == null) director = FindAnyObjectByType<ArenaDirector>();
            if (chaseCamera == null) chaseCamera = FindAnyObjectByType<ChaseCamera>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f3Key.wasPressedThisFrame) SetDebugVisible(!debugVisible);
            if ((kb.f4Key.wasPressedThisFrame || kb.cKey.wasPressedThisFrame) && chaseCamera != null) chaseCamera.enabledByUser = !chaseCamera.enabledByUser;
        }

        void OnDisable()
        {
            if (debugVisible) SetDebugVisible(false);
        }

        void SetDebugVisible(bool visible)
        {
            if (visible == debugVisible) return;
            debugVisible = visible;
            if (visible)
            {
                savedCursorVisible = Cursor.visible;
                savedCursorLock = Cursor.lockState;
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            else
            {
                Cursor.visible = savedCursorVisible;
                Cursor.lockState = savedCursorLock;
            }
        }

        void OnGUI()
        {
            if (car == null) return;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold };
                small = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
                banner = new GUIStyle(GUI.skin.label) { fontSize = 56, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
                centred = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
                kills = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };
            }

            float w = Screen.width, h = Screen.height;
            DrawGameplay(w, h);
            if (debugVisible) DrawDebug(w, h);
            else DrawKeyHint();
        }

        void DrawGameplay(float w, float h)
        {
            if (health == null) return;
            if (lastHealth >= 0f && health.Health < lastHealth) hitFlash = Mathf.Min(1f, hitFlash + 0.6f);
            lastHealth = health.Health;
            hitFlash = Mathf.MoveTowards(hitFlash, 0f, Time.unscaledDeltaTime * 2.5f);
            if (hitFlash > 0f)
            {
                var old = GUI.color;
                GUI.color = new Color(1f, 0.1f, 0.05f, hitFlash * 0.25f);
                GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
                GUI.color = old;
            }

            if (health.Kills > 0)
            {
                kills.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
                GUI.Label(new Rect(w - 221, 19, 200, 30), $"KILLS {health.Kills}", kills);
                kills.normal.textColor = new Color(1f, 0.85f, 0.4f);
                GUI.Label(new Rect(w - 222, 18, 200, 30), $"KILLS {health.Kills}", kills);
            }

            DrawMeters(w, h);

            if (health.IsDead && !string.IsNullOrEmpty(wreckedHint))
            {
                GUI.Label(new Rect(0, h * 0.3f, w, 80), "<color=#ff4030>WRECKED</color>", banner);
                GUI.Label(new Rect(0, h * 0.3f + 70, w, 30), wreckedHint, centred);
            }
        }

        /// <summary>Ammo and boost bars, bottom centre.</summary>
        void DrawMeters(float w, float h)
        {
            if (gun == null) gun = GetComponent<CarGun>();
            if (boost == null) boost = GetComponent<CarBoost>();
            float bw = Mathf.Min(260f, w * 0.2f), bh = 10f, x = (w - bw) * 0.5f, y = h - 34f;
            if (gun != null && gun.Unlocked) Meter(new Rect(x, y, bw, bh), gun.Ammo01, gun.Firing ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.7f, 0.25f), $"AMMO {Mathf.FloorToInt(gun.Ammo)}");
            if (boost != null && boost.unlocked) Meter(new Rect(x, y - 26f, bw, bh), boost.charge, boost.Boosting ? new Color(0.5f, 0.95f, 1f) : new Color(0.3f, 0.75f, 1f), "BOOST");
        }

        void Meter(Rect r, float f, Color fill, string label)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), Texture2D.whiteTexture);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(f), r.height), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.Label(new Rect(r.x, r.y - 18f, r.width, 18f), label, small);
            GUI.color = old;
        }

        /// <summary>Always-on reminder of the debug and camera keys (the debug overlay's own header lists them when open).</summary>
        void DrawKeyHint()
        {
            if (gun == null) gun = GetComponent<CarGun>();
            if (boost == null) boost = GetComponent<CarBoost>();
            // Only list what's fitted: the gun and boost are bought upgrades.
            string text = (gun != null && gun.Unlocked ? "<b>LMB/Ctrl</b> gun    " : "") +
                          (boost != null && boost.unlocked ? "<b>Shift</b> boost    " : "") +
                          "<b>C</b> camera    <b>Esc</b> menu    <b>F3</b> debug";
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(16, 11, 700, 22), text, small);
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            GUI.Label(new Rect(15, 10, 700, 22), text, small);
            GUI.color = old;
        }

        void DrawDebug(float w, float h)
        {
            string gear = car.ReverseGear ? "R" : car.Gear.ToString();
            GUI.Box(new Rect(w - 250, h - 130, 235, 115), GUIContent.none);
            GUI.Label(new Rect(w - 240, h - 128, 230, 45), $"{car.SpeedKmh:0} km/h", big);
            GUI.Label(new Rect(w - 240, h - 85, 230, 22), $"Gear <b>{gear}</b>   {car.Rpm:0} rpm", small);
            float rpm01 = Mathf.InverseLerp(900f, 6800f, car.Rpm);
            GUI.Box(new Rect(w - 240, h - 60, 215 * rpm01, 10), GUIContent.none);
            GUI.Label(new Rect(w - 240, h - 45, 230, 22),
                $"<b>{car.driveType}</b> (T)   Assists {(car.tractionControl ? "on" : "off")} (B)", small);

            sb.Clear();
            sb.Append("<b>Surface</b>  ");
            foreach (var wh in car.wheels)
            {
                string s = wh.Grounded && wh.Surface != null ? wh.Surface.displayName : "air";
                string col = wh.CombinedSlip > 1.1f ? "#ff7050" : "#ffffff";
                sb.Append($"{wh.name}:<color={col}>{s}</color>  ");
            }
            GUI.Label(new Rect(15, h - 60, 700, 22), sb.ToString(), small);

            if (damage != null)
            {
                sb.Clear();
                sb.Append($"<b>Integrity {damage.Integrity:0}%</b>   ");
                foreach (var p in damage.parts)
                {
                    string col = p.State == DetachablePart.PartState.Attached ? "#c0ffc0"
                        : p.State == DetachablePart.PartState.Loose ? "#ffd060" : "#ff6060";
                    sb.Append($"<color={col}>{p.displayName}</color>  ");
                }
                if (damage.hoodLatch != null && damage.hoodLatch.Popped) sb.Append("<color=#ff6060>Hood latch</color>");
                GUI.Label(new Rect(15, h - 35, 900, 22), sb.ToString(), small);
            }

            if (health != null)
                GUI.Label(new Rect(w - 320, 52, 315, 22), $"<b>Health {health.Health:0}</b> / {health.maxHealth:0}", small);

            if (director != null)
            {
                sb.Clear();
                sb.Append($"<b>Enemies</b>  destroyed {director.EnemiesDestroyed}").Append('\n');
                foreach (var e in director.Enemies)
                {
                    if (e == null) continue;
                    string col = e.IsDead ? "#777777" : e.Health01 > 0.5f ? "#ffffff" : e.Health01 > 0.25f ? "#ffd060" : "#ff6060";
                    var ai = e.GetComponent<AICarDriver>();
                    string st = ai != null && !e.IsDead ? ai.CurrentState.ToString() : "";
                    if (ai != null && !e.IsDead && ai.TargetHealth != null) st += " → " + ai.TargetHealth.displayName;
                    string hp = e.IsDead ? "wrecked" : e.Health.ToString("0");
                    sb.Append($"<color={col}>{e.displayName}  {hp}</color>  <size=11>{st}</size>").Append('\n');
                }
                GUI.Label(new Rect(w - 320, 76, 315, 160), sb.ToString(), small);
            }

            DrawCameraPanel(h);

            GUI.Label(new Rect(15, 10, 1600, 22),
                "<b>DEBUG (F3)</b>   <b>WASD</b> drive (hold S to reverse)   <b>Space</b> handbrake   <b>Q/E</b> look   <b>Alt</b> look back   " +
                "<b>T</b> FWD/RWD/AWD   <b>B</b> assists   <b>R</b> flip upright   <b>Backspace</b> repair   <b>F4</b> 3rd-person cam", small);
        }

        /// <summary>Third-person camera switch and sliders (left side, middle).</summary>
        void DrawCameraPanel(float h)
        {
            if (chaseCamera == null) return;
            var c = chaseCamera;
            float x = 15f, y = h * 0.5f - 150f, w = 300f;
            GUI.Box(new Rect(x - 5, y - 5, w + 10, 290), GUIContent.none);
            c.enabledByUser = GUI.Toggle(new Rect(x, y, w, 22), c.enabledByUser,
                c.Live ? " <b>3rd-person camera</b> (F4)  - live" : " <b>3rd-person camera</b> (F4)", small);
            y += 28f;
            c.distance = Slider(ref y, x, w, "Distance", c.distance, 3f, 15f, "0.0 m");
            c.height = Slider(ref y, x, w, "Height", c.height, 0.5f, 6f, "0.0 m");
            c.lookHeight = Slider(ref y, x, w, "Look height", c.lookHeight, 0f, 2.5f, "0.0 m");
            c.fov = Slider(ref y, x, w, "Field of view", c.fov, 40f, 100f, "0");
            c.rotationDamping = Slider(ref y, x, w, "Swing stiffness", c.rotationDamping, 0.5f, 15f, "0.0");
            c.positionDamping = Slider(ref y, x, w, "Follow stiffness", c.positionDamping, 0.5f, 30f, "0.0");
            c.speedPullback = Slider(ref y, x, w, "Speed pull-back", c.speedPullback, 0f, 0.1f, "0.000");
        }

        float Slider(ref float y, float x, float w, string label, float value, float min, float max, string format)
        {
            GUI.Label(new Rect(x, y, 120, 20), label, small);
            GUI.Label(new Rect(x + w - 60, y, 60, 20), value.ToString(format), small);
            value = GUI.HorizontalSlider(new Rect(x + 120, y + 5, w - 185, 16), value, min, max);
            y += 32f;
            return value;
        }
    }
}
