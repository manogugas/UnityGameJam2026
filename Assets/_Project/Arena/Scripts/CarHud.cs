using System.Text;
using UnityEngine;

namespace CarArena
{
    /// <summary>Debug HUD: speed, gear, drive type, surfaces, damage and controls.</summary>
    public class CarHud : MonoBehaviour
    {
        public CarController car;
        public CarDamage damage;
        public bool showHelp = true;

        GUIStyle big, small;
        readonly StringBuilder sb = new StringBuilder();

        void OnGUI()
        {
            if (car == null) return;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold };
                small = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
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

            if (showHelp)
            {
                GUI.Label(new Rect(15, 10, 900, 22),
                    "<b>WASD</b> drive (hold S to reverse)   <b>Space</b> handbrake   <b>Q/E</b> look   <b>Alt</b> look back   " +
                    "<b>T</b> FWD/RWD/AWD   <b>B</b> assists   <b>R</b> flip upright   <b>Backspace</b> repair   <b>F1</b> hide help", small);
            }
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F1) showHelp = !showHelp;
        }
    }
}
