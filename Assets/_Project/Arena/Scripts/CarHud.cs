using System.Text;
using UnityEngine;

namespace CarArena
{
    /// <summary>Debug HUD: speed, gear, drive type, surfaces, damage and controls.</summary>
    public class CarHud : MonoBehaviour
    {
        public CarController car;
        public CarDamage damage;
        public CarHealth health;
        public ArenaDirector director;
        public bool showHelp = true;

        void Start()
        {
            if (director == null) director = FindAnyObjectByType<ArenaDirector>();
        }

        GUIStyle big, small, banner;
        float hitFlash, lastHealth = -1f;
        readonly StringBuilder sb = new StringBuilder();

        void OnGUI()
        {
            if (car == null) return;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold };
                small = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
                banner = new GUIStyle(GUI.skin.label) { fontSize = 56, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
            }

            float w = Screen.width, h = Screen.height;
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

            DrawCombat(w, h);

            if (showHelp)
            {
                GUI.Label(new Rect(15, 10, 900, 22),
                    "<b>WASD</b> drive (hold S to reverse)   <b>Space</b> handbrake   <b>Q/E</b> look   <b>Alt</b> look back   " +
                    "<b>T</b> FWD/RWD/AWD   <b>B</b> assists   <b>R</b> flip upright   <b>Backspace</b> repair   <b>F1</b> hide help", small);
            }
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F1) showHelp = !showHelp;
        }

        void DrawCombat(float w, float h)
        {
            if (health != null)
            {
                // Health bar + kills, top right.
                if (lastHealth >= 0f && health.Health < lastHealth) hitFlash = 1f;
                lastHealth = health.Health;
                hitFlash = Mathf.MoveTowards(hitFlash, 0f, Time.unscaledDeltaTime * 2.5f);

                float bw = 260f, x = w - bw - 15f, y = 38f;
                GUI.Box(new Rect(x - 6, y - 24, bw + 12, 58), GUIContent.none);
                GUI.Label(new Rect(x, y - 22, bw, 20), $"<b>HEALTH {health.Health:0}</b>   Kills <b>{health.Kills}</b>", small);
                var old = GUI.color;
                GUI.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
                GUI.DrawTexture(new Rect(x, y, bw, 14), Texture2D.whiteTexture);
                float f = health.Health01;
                GUI.color = Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(0.3f, 0.85f, 0.3f), f);
                GUI.DrawTexture(new Rect(x, y, bw * f, 14), Texture2D.whiteTexture);
                if (hitFlash > 0f)
                {
                    GUI.color = new Color(1f, 0.1f, 0.05f, hitFlash * 0.25f);
                    GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
                }
                GUI.color = old;

                if (health.IsDead)
                    GUI.Label(new Rect(0, h * 0.3f, w, 80), "<color=#ff4030>WRECKED</color>", banner);
                if (health.IsDead)
                    GUI.Label(new Rect(0, h * 0.3f + 70, w, 30), "<b>Backspace</b> to repair and get back in", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 18 });
            }

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
                GUI.Label(new Rect(w - 320, 100, 315, 160), sb.ToString(), small);
            }
        }
    }
}
