using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarArena
{
    /// <summary>
    /// Player HUD. In normal play only gameplay feedback is drawn (hit flash, WRECKED banner, kill count):
    /// speed, gear and assists live on the dashboard. F3 toggles the debug overlay with the testing info:
    /// speed/rpm/gear, drive type and assists, wheel surfaces, part damage, controls and the enemy roster.
    /// </summary>
    public class CarHud : MonoBehaviour
    {
        public CarController car;
        public CarDamage damage;
        public CarHealth health;
        public ArenaDirector director;
        [Tooltip("Debug overlay (toggle with F3).")]
        public bool debugVisible;

        GUIStyle big, small, banner, centred, kills;
        float hitFlash, lastHealth = -1f;
        readonly StringBuilder sb = new StringBuilder();

        void Start()
        {
            if (director == null) director = FindAnyObjectByType<ArenaDirector>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame) debugVisible = !debugVisible;
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

            if (health.IsDead)
            {
                GUI.Label(new Rect(0, h * 0.3f, w, 80), "<color=#ff4030>WRECKED</color>", banner);
                GUI.Label(new Rect(0, h * 0.3f + 70, w, 30), "<b>Backspace</b> to repair and get back in", centred);
            }
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

            GUI.Label(new Rect(15, 10, 1000, 22),
                "<b>DEBUG (F3)</b>   <b>WASD</b> drive (hold S to reverse)   <b>Space</b> handbrake   <b>Q/E</b> look   <b>Alt</b> look back   " +
                "<b>T</b> FWD/RWD/AWD   <b>B</b> assists   <b>R</b> flip upright   <b>Backspace</b> repair", small);
        }
    }
}
