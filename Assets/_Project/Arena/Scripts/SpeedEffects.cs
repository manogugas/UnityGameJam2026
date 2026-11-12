using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Screen-space motion cues for the player at speed: streaks rushing out from the centre of view toward
    /// the screen edges, and a vignette that closes in. Both build up with speed (CarCamera.SpeedFeel) and
    /// get stronger while boosting. Only drawn while driving (the HUD is on).
    /// </summary>
    public class SpeedEffects : MonoBehaviour
    {
        public CarController car;
        public CarCamera driverCamera;
        public CarHud hud;
        [Tooltip("Optional: when the chase camera is live the vignette is softer.")]
        public ChaseCamera chaseCamera;

        [Header("Speed lines")]
        public int lineCount = 46;
        [Range(0f, 1f)] public float lineAlpha = 0.32f;
        [Tooltip("Speed feel needed before streaks appear (0..1).")]
        [Range(0f, 1f)] public float lineThreshold = 0.35f;

        [Header("Vignette")]
        [Range(0f, 1f)] public float vignetteMax = 0.55f;

        struct Streak { public float angle, radius, speed, length, width; }

        Streak[] streaks;
        Texture2D vignette;
        float feel;

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            if (hud == null) hud = GetComponent<CarHud>();
            if (driverCamera == null) driverCamera = GetComponentInChildren<CarCamera>();
            streaks = new Streak[lineCount];
            for (int i = 0; i < lineCount; i++) streaks[i] = NewStreak(Random.Range(0.3f, 1.2f));
            vignette = MakeVignette();
        }

        void Start()
        {
            if (chaseCamera == null) chaseCamera = FindAnyObjectByType<ChaseCamera>();
        }

        static Streak NewStreak(float radius) => new Streak
        {
            angle = Random.Range(0f, 360f),
            radius = radius,
            speed = Random.Range(0.9f, 1.6f),
            length = Random.Range(0.06f, 0.16f),
            width = Random.Range(1.5f, 3.2f),
        };

        void Update()
        {
            // CarCamera already eases speed and boost in; ease once more so streaks fade up softly.
            float target = driverCamera != null ? Mathf.Min(1f, driverCamera.SpeedFeel + 0.3f * driverCamera.BoostFeel) * GameSettings.SpeedEffects : 0f;
            feel = Mathf.Lerp(feel, target, 1f - Mathf.Exp(-Time.deltaTime * 2f));

            float dt = Time.deltaTime * (0.6f + feel * 2.2f);
            for (int i = 0; i < streaks.Length; i++)
            {
                streaks[i].radius += streaks[i].speed * dt;
                if (streaks[i].radius > 1.25f) streaks[i] = NewStreak(Random.Range(0.25f, 0.4f));
            }
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (hud == null || !hud.enabled || feel < 0.02f) return;
            float w = Screen.width, h = Screen.height;
            var old = GUI.color;
            var oldMatrix = GUI.matrix;

            // Vignette (softer in third person).
            float vig = feel * feel * vignetteMax * (chaseCamera != null && chaseCamera.Live ? 0.6f : 1f);
            GUI.color = new Color(0f, 0f, 0f, vig);
            GUI.DrawTexture(new Rect(0, 0, w, h), vignette, ScaleMode.StretchToFill);

            // Streaks rushing out from just above the centre of view.
            float lines = Mathf.Clamp01((feel - lineThreshold) / (1f - lineThreshold));
            if (lines > 0f)
            {
                var centre = new Vector2(w * 0.5f, h * 0.42f);
                float reach = Mathf.Sqrt(w * w + h * h) * 0.5f;
                bool boost = driverCamera != null && driverCamera.BoostFeel > 0.5f;
                foreach (var s in streaks)
                {
                    if (s.radius < 0.35f) continue; // keep the middle of the view clear
                    float a = lines * lineAlpha * Mathf.Clamp01((s.radius - 0.35f) * 3f);
                    GUI.color = boost ? new Color(0.75f, 0.95f, 1f, a * 1.3f) : new Color(1f, 1f, 1f, a);
                    float r = s.radius * reach, len = s.length * reach * (0.6f + lines);
                    Vector2 dir = new Vector2(Mathf.Cos(s.angle * Mathf.Deg2Rad), Mathf.Sin(s.angle * Mathf.Deg2Rad));
                    Vector2 p = centre + dir * r;
                    GUI.matrix = oldMatrix;
                    GUIUtility.RotateAroundPivot(s.angle, p);
                    GUI.DrawTexture(new Rect(p.x, p.y - s.width * 0.5f, len, s.width), Texture2D.whiteTexture);
                }
            }
            GUI.matrix = oldMatrix;
            GUI.color = old;
        }

        static Texture2D MakeVignette()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.Alpha8, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(u * u * 0.8f + v * v);
                    float a = Mathf.Clamp01((d - 0.55f) / 0.6f);
                    px[y * size + x] = new Color(1f, 1f, 1f, a * a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        void OnDestroy()
        {
            if (vignette != null) Destroy(vignette);
        }
    }
}
