using UnityEngine;
using UnityEngine.InputSystem;

namespace CarArena
{
    /// <summary>
    /// Esc menu. During an event: Resume / Settings / Retire to garage (the run resets to before the event) / Quit.
    /// In the garage: Resume / Settings / Quit. Pauses time and audio while open and frees the mouse.
    /// Settings: master, engine, effects and crowd volume, camera shake, speed effects and fullscreen (GameSettings,
    /// saved in PlayerPrefs). The tablet's start screen opens the settings page directly.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        public CampaignDirector campaign;

        public bool Open { get; private set; }

        enum Page { Main, Settings }

        Page page;
        bool settingsOnly; // opened straight to settings (tablet): Back closes the menu
        GUIStyle title, button, hint, label, value, toggle;
        bool savedCursorVisible;
        CursorLockMode savedLock;

        void Start() => GameSettings.Apply(); // the mixer ignores SetFloat before the first frame

        void Update()
        {
            var k = Keyboard.current;
            if (k != null && k.escapeKey.wasPressedThisFrame)
            {
                if (Open && page == Page.Settings && !settingsOnly) Back();
                else if (Open) Close();
                else if (CanOpen()) Show();
            }
        }

        bool CanOpen()
        {
            if (campaign == null) return true;
            var p = campaign.CurrentPhase;
            return p == CampaignDirector.Phase.Match || p == CampaignDirector.Phase.Garage || p == CampaignDirector.Phase.Start;
        }

        /// <summary>Open the menu on the settings page (Back closes it).</summary>
        public void ShowSettings()
        {
            if (!Open) Show();
            page = Page.Settings;
            settingsOnly = true;
        }

        void Show()
        {
            Open = true;
            page = Page.Main;
            settingsOnly = false;
            savedCursorVisible = Cursor.visible;
            savedLock = Cursor.lockState;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            Time.timeScale = 0f;
            AudioListener.pause = true;
        }

        void Back()
        {
            GameSettings.Save();
            if (settingsOnly) Close();
            else page = Page.Main;
        }

        void Close(bool restoreCursor = true)
        {
            if (page == Page.Settings) GameSettings.Save();
            Open = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            if (restoreCursor)
            {
                Cursor.visible = savedCursorVisible;
                Cursor.lockState = savedLock;
            }
        }

        void OnDisable()
        {
            if (Open) Close();
        }

        void OnGUI()
        {
            if (!Open) return;
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                button = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
                hint = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true };
                label = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                value = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight };
                toggle = new GUIStyle(GUI.skin.toggle) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            }
            float w = Screen.width, h = Screen.height;
            title.fontSize = Mathf.RoundToInt(h * 0.07f);
            button.fontSize = Mathf.RoundToInt(h * 0.03f);
            hint.fontSize = Mathf.RoundToInt(h * 0.02f);
            label.fontSize = value.fontSize = toggle.fontSize = Mathf.RoundToInt(h * 0.026f);

            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, page == Page.Settings ? 0.93f : 0.75f);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            GUI.color = old;

            if (page == Page.Settings) DrawSettings(w, h);
            else DrawMain(w, h);
        }

        void DrawMain(float w, float h)
        {
            GUI.Label(new Rect(0, h * 0.14f, w, h * 0.1f), "PAUSED", title);
            float bw = h * 0.42f, bh = h * 0.075f, x = (w - bw) * 0.5f, y = h * 0.3f;
            bool inEvent = campaign != null && campaign.CurrentPhase == CampaignDirector.Phase.Match;

            if (GUI.Button(new Rect(x, y, bw, bh), "RESUME", button)) Close();
            y += bh * 1.3f;
            if (GUI.Button(new Rect(x, y, bw, bh), "SETTINGS", button)) page = Page.Settings;
            y += bh * 1.3f;
            if (inEvent && GUI.Button(new Rect(x, y, bw, bh), "RETIRE TO GARAGE", button))
            {
                Close(false);
                campaign.Retire();
            }
            if (inEvent) y += bh * 1.3f;
            if (GUI.Button(new Rect(x, y, bw, bh), "QUIT GAME", button))
            {
                Close(false);
                Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
            y += bh * 1.6f;
            GUI.Label(new Rect(0, y, w, h * 0.12f),
                "<b>WASD</b> drive   <b>Space</b> handbrake   <b>Shift</b> boost   <b>LMB / Ctrl</b> machine gun\n" +
                "<b>Q / E / Alt</b> look   <b>C</b> camera   <b>R</b> flip upright   " +
                (campaign != null && campaign.player != null && campaign.player.assistsLocked ? "" : "<b>B</b> assists   ") + "<b>F3</b> debug", hint);
        }

        void DrawSettings(float w, float h)
        {
            GUI.Label(new Rect(0, h * 0.08f, w, h * 0.1f), "SETTINGS", title);
            float rowH = h * 0.068f, colW = Mathf.Min(w * 0.9f, h * 1.1f), x = (w - colW) * 0.5f, y = h * 0.21f;

            float before = GameSettings.Master + GameSettings.Car + GameSettings.Effects + GameSettings.Crowd +
                           GameSettings.CameraShake + GameSettings.SpeedEffects;

            Section(x, ref y, colW, rowH, "SOUND");
            // Volumes: 0.0001..1, turned into mixer decibels (Log10 * 20) by GameSettings.Apply.
            float lo = GameSettings.MinVolume;
            GameSettings.Master = Slider(x, ref y, colW, rowH, "Master volume", GameSettings.Master, lo, 1f);
            GameSettings.Car = Slider(x, ref y, colW, rowH, "Car (engine, tyres)", GameSettings.Car, lo, 1f);
            GameSettings.Effects = Slider(x, ref y, colW, rowH, "Effects (crashes, gun, garage)", GameSettings.Effects, lo, 1f);
            GameSettings.Crowd = Slider(x, ref y, colW, rowH, "Crowd", GameSettings.Crowd, lo, 1f);
            y += rowH * 0.25f;
            Section(x, ref y, colW, rowH, "CAMERA");
            GameSettings.CameraShake = Slider(x, ref y, colW, rowH, "Camera shake", GameSettings.CameraShake, 0f, 1.5f);
            GameSettings.SpeedEffects = Slider(x, ref y, colW, rowH, "Speed effects (FOV, speed lines)", GameSettings.SpeedEffects, 0f, 1.5f);

            float after = GameSettings.Master + GameSettings.Car + GameSettings.Effects + GameSettings.Crowd +
                          GameSettings.CameraShake + GameSettings.SpeedEffects;
            if (!Mathf.Approximately(before, after)) GameSettings.Apply();

            y += rowH * 0.25f;
            bool full = GUI.Toggle(new Rect(x, y + rowH * 0.2f, colW * 0.5f, rowH * 0.6f), Screen.fullScreen, "  Fullscreen", toggle);
            if (full != Screen.fullScreen) Screen.fullScreen = full;
            y += rowH * 1.3f;

            float bw = colW * 0.3f, bh = h * 0.07f;
            if (GUI.Button(new Rect(x, y, bw, bh), "BACK", button)) Back();
            if (GUI.Button(new Rect(x + colW - bw, y, bw, bh), "DEFAULTS", button)) GameSettings.ResetDefaults();
        }

        void Section(float x, ref float y, float width, float rowH, string text)
        {
            var c = GUI.color;
            GUI.color = new Color(1f, 0.82f, 0.4f);
            GUI.Label(new Rect(x, y, width, rowH * 0.8f), text, label);
            GUI.color = c;
            y += rowH * 0.8f;
        }

        float Slider(float x, ref float y, float width, float rowH, string name, float v, float min, float max)
        {
            GUI.Label(new Rect(x, y, width * 0.45f, rowH), name, label);
            float sx = x + width * 0.47f, sw = width * 0.4f;
            // Visible track + fill under the (faint) default slider.
            var c = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.18f);
            GUI.DrawTexture(new Rect(sx, y + rowH * 0.5f - 3f, sw, 6f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.82f, 0.4f, 0.9f);
            GUI.DrawTexture(new Rect(sx, y + rowH * 0.5f - 3f, sw * Mathf.Clamp01(v / max), 6f), Texture2D.whiteTexture);
            GUI.color = c;
            v = GUI.HorizontalSlider(new Rect(sx, y + rowH * 0.4f, sw, rowH * 0.3f), v, min, max);
            GUI.Label(new Rect(x + width * 0.87f, y, width * 0.13f, rowH), $"{Mathf.RoundToInt(v * 100f)}%", value);
            y += rowH;
            return v;
        }
    }
}
