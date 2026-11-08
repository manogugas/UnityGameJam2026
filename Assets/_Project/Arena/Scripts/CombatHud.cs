using System.Collections.Generic;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Combat overlays for the player:
    ///  - health bars floating above enemy cars,
    ///  - damage numbers popping off whatever you hit (and off your own car when you get hit),
    ///  - a hit marker in the centre when you deal damage,
    ///  - red flashes at the screen edge pointing to where you got hit from.
    /// </summary>
    public class CombatHud : MonoBehaviour
    {
        public CarHealth player;
        public ArenaDirector director;

        [Header("Enemy health bars")]
        public float barMaxDistance = 110f;
        public float barHeightAboveCar = 2.1f;
        public Vector2 barSizeNear = new Vector2(90f, 9f);
        public float barNearDistance = 12f;

        [Header("Indicators")]
        public float numberLifetime = 1.1f;
        public float markerTime = 0.22f;
        public float directionTime = 1.2f;
        [Tooltip("Ignore tiny scrapes.")]
        public float minShownDamage = 0.6f;

        struct Popup { public Vector3 world; public string text; public Color color; public float t0; public float offset; }
        struct HitDir { public float angle, strength, t0; }

        readonly List<Popup> popups = new List<Popup>();
        readonly List<HitDir> hitDirs = new List<HitDir>();
        readonly HashSet<CarHealth> watched = new HashSet<CarHealth>();
        float markerT0 = -10f, markerStrength;
        GUIStyle numberStyle, nameStyle;
        Camera cam;

        void Awake()
        {
            if (director == null) director = FindAnyObjectByType<ArenaDirector>();
            if (player != null) Watch(player);
            if (director != null)
            {
                director.CombatantAdded += Watch;
                foreach (var c in director.Combatants) Watch(c);
            }
        }

        void OnDestroy()
        {
            if (director != null) director.CombatantAdded -= Watch;
            foreach (var c in watched) if (c != null) c.Damaged -= OnDamaged;
        }

        void Watch(CarHealth c)
        {
            if (c == null || !watched.Add(c)) return;
            c.Damaged += OnDamaged;
        }

        void OnDamaged(DamageEvent e)
        {
            if (e.amount < minShownDamage) return;
            bool mine = e.attacker != null && e.attacker == player;
            bool toMe = e.victim == player;
            if (!mine && !toMe) return;

            string text = "-" + Mathf.CeilToInt(e.amount);
            if (mine)
            {
                popups.Add(new Popup
                {
                    world = e.victim.transform.position + Vector3.up * (barHeightAboveCar + 0.4f),
                    text = e.victim.IsDead ? text + "  WRECKED!" : text,
                    color = e.victim.IsDead ? new Color(1f, 0.55f, 0.1f) : new Color(1f, 0.92f, 0.3f),
                    t0 = Time.time,
                    offset = Random.Range(-10f, 10f),
                });
                markerT0 = Time.time;
                markerStrength = Mathf.Clamp01(e.amount / 20f);
            }
            if (toMe)
            {
                Vector3 local = e.victim.transform.InverseTransformPoint(e.point);
                hitDirs.Add(new HitDir
                {
                    angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg,
                    strength = Mathf.Clamp01(e.amount / 15f),
                    t0 = Time.time,
                });
                int stacked = 0;
                foreach (var q in popups) if (q.world == Vector3.zero) stacked++;
                popups.Add(new Popup
                {
                    world = Vector3.zero, // drawn at a fixed screen spot, stacked downwards
                    text = text,
                    color = new Color(1f, 0.25f, 0.2f),
                    t0 = Time.time,
                    offset = stacked * 30f,
                });
            }
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            cam = Camera.main;
            if (cam == null) return;
            if (numberStyle == null)
            {
                numberStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.LowerCenter };
            }
            DrawEnemyBars();
            DrawHitDirections();
            DrawPopups();
            DrawHitMarker();
        }

        void DrawEnemyBars()
        {
            if (director == null) return;
            foreach (var e in director.Enemies)
            {
                if (e == null || e == player) continue;
                Vector3 world = e.transform.position + Vector3.up * barHeightAboveCar;
                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.z <= 0.5f || sp.z > barMaxDistance) continue;
                float scale = Mathf.Clamp(barNearDistance / sp.z, 0.35f, 1f);
                float w = barSizeNear.x * scale, h = Mathf.Max(4f, barSizeNear.y * scale);
                float x = sp.x - w * 0.5f, y = Screen.height - sp.y - h;

                float f = e.Health01;
                Color fill = e.IsDead ? new Color(0.4f, 0.4f, 0.4f) : Color.Lerp(new Color(0.95f, 0.15f, 0.1f), new Color(0.35f, 0.9f, 0.3f), f);
                var old = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.65f);
                GUI.DrawTexture(new Rect(x - 1, y - 1, w + 2, h + 2), Texture2D.whiteTexture);
                GUI.color = fill;
                GUI.DrawTexture(new Rect(x, y, w * f, h), Texture2D.whiteTexture);
                // Flash white briefly when this car was just hit.
                float since = Time.time - e.LastDamageTime;
                if (since < 0.2f)
                {
                    GUI.color = new Color(1f, 1f, 1f, 1f - since / 0.2f);
                    GUI.DrawTexture(new Rect(x, y, w * f, h), Texture2D.whiteTexture);
                }
                GUI.color = old;
                if (scale > 0.5f)
                    GUI.Label(new Rect(sp.x - 80, y - 18, 160, 16), e.IsDead ? e.displayName + " (wrecked)" : e.displayName, nameStyle);
            }
        }

        void DrawPopups()
        {
            float now = Time.time;
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var p = popups[i];
                float age = (now - p.t0) / numberLifetime;
                if (age >= 1f) { popups.RemoveAt(i); continue; }
                Vector2 pos;
                if (p.world == Vector3.zero)
                {
                    pos = new Vector2(Screen.width * 0.5f + 140f, Screen.height * 0.62f + p.offset);
                }
                else
                {
                    Vector3 sp = cam.WorldToScreenPoint(p.world);
                    if (sp.z <= 0.5f) continue;
                    pos = new Vector2(sp.x, Screen.height - sp.y - p.offset);
                }
                pos.y -= age * 40f;
                float scale = 1f + (1f - age) * 0.4f * (age < 0.15f ? 1f : 0f);
                var c = p.color;
                c.a = 1f - age * age;
                numberStyle.fontSize = Mathf.RoundToInt(26 * scale);
                var shadow = new Color(0f, 0f, 0f, c.a * 0.8f);
                numberStyle.normal.textColor = shadow;
                GUI.Label(new Rect(pos.x - 99, pos.y - 19, 200, 40), p.text, numberStyle);
                numberStyle.normal.textColor = c;
                GUI.Label(new Rect(pos.x - 100, pos.y - 20, 200, 40), p.text, numberStyle);
            }
        }

        void DrawHitMarker()
        {
            float age = (Time.time - markerT0) / markerTime;
            if (age >= 1f) return;
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            float gap = 10f + age * 8f, len = 10f + markerStrength * 8f;
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 1f - age);
            for (int i = 0; i < 4; i++)
            {
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(45f + 90f * i, new Vector2(cx, cy));
                GUI.DrawTexture(new Rect(cx - 1.5f, cy - gap - len, 3f, len), Texture2D.whiteTexture);
                GUI.matrix = m;
            }
            GUI.color = old;
        }

        void DrawHitDirections()
        {
            float now = Time.time;
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            float radius = Mathf.Min(Screen.width, Screen.height) * 0.38f;
            var old = GUI.color;
            for (int i = hitDirs.Count - 1; i >= 0; i--)
            {
                var d = hitDirs[i];
                float age = (now - d.t0) / directionTime;
                if (age >= 1f) { hitDirs.RemoveAt(i); continue; }
                // Hits are relative to the car; the head may be turned, so subtract the head's yaw.
                float headYaw = cam.transform.localEulerAngles.y;
                float a = d.angle - (headYaw > 180f ? headYaw - 360f : headYaw);
                GUI.color = new Color(1f, 0.1f, 0.05f, (1f - age) * (0.45f + 0.55f * d.strength));
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(a, new Vector2(cx, cy));
                float w = 90f + 120f * d.strength, h = 10f + 8f * d.strength;
                GUI.DrawTexture(new Rect(cx - w * 0.5f, cy - radius - h, w, h), Texture2D.whiteTexture);
                GUI.matrix = m;
            }
            GUI.color = old;
        }
    }
}
