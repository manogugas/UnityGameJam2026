using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarArena
{
    /// <summary>
    /// Full-screen overlays for the campaign: fades, the emperor's announcement at match start,
    /// "+$" kill ticker, the victory summary (money breakdown and total) and the defeat screen.
    /// </summary>
    public class MatchSummaryUI : MonoBehaviour
    {
        public Color gold = new Color(1f, 0.82f, 0.35f);
        public Color red = new Color(1f, 0.3f, 0.22f);
        [Tooltip("Seconds before a result screen accepts input (so a held key doesn't skip it).")]
        public float inputDelay = 1.2f;

        public bool ShowingResult => result != null;

        class Result
        {
            public string title, subtitle, footer, prompt;
            public List<(string label, string value)> lines;
            public string total;
            public Color color;
            public Action onContinue;
            public float t0;
        }

        struct Tick { public string text; public float t0; }

        float fade;
        string announceBig, announceSmall;
        float announceT0 = -100f, announceDuration;
        readonly List<Tick> ticks = new List<Tick>();
        Result result;
        GUIStyle big, mid, small, line, value, prompt;

        // ------------------------------------------------------------------ API

        public IEnumerator FadeTo(float target, float time)
        {
            float start = fade;
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                fade = Mathf.Lerp(start, target, t / time);
                yield return null;
            }
            fade = target;
        }

        public void Announce(string bigText, string smallText, float duration)
        {
            announceBig = bigText;
            announceSmall = smallText;
            announceDuration = duration;
            announceT0 = Time.time;
        }

        public void Ticker(string text) => ticks.Add(new Tick { text = text, t0 = Time.time });

        public void ShowVictory(string title, string subtitle, List<(string, string)> lines, string total, string footer, Action onContinue)
        {
            result = new Result
            {
                title = title, subtitle = subtitle, lines = lines, total = total, footer = footer,
                prompt = "Space / click: back to the garage", color = gold, onContinue = onContinue, t0 = Time.unscaledTime,
            };
            ticks.Clear(); // the summary lists them; the ticker would overlap the title
            SetCursor(true);
        }

        public void ShowDefeat(string title, string subtitle, Action onRetry)
        {
            result = new Result
            {
                title = title, subtitle = subtitle, lines = new List<(string, string)>(), footer = "",
                prompt = "Space / click: back to the garage and try again", color = red, onContinue = onRetry, t0 = Time.unscaledTime,
            };
            SetCursor(true);
        }

        public void ClearAll()
        {
            result = null;
            ticks.Clear();
            announceT0 = -100f;
        }

        static void SetCursor(bool visible)
        {
            Cursor.visible = visible;
            Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
        }

        // ------------------------------------------------------------------ input

        void Update()
        {
            if (result == null || Time.unscaledTime - result.t0 < inputDelay) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool go = (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                      || (mouse != null && mouse.leftButton.wasPressedThisFrame);
            if (!go) return;
            var cb = result.onContinue;
            result = null;
            cb?.Invoke();
        }

        // ------------------------------------------------------------------ drawing

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
                mid = new GUIStyle(big) { fontStyle = FontStyle.Normal };
                small = new GUIStyle(mid);
                line = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, richText = true };
                value = new GUIStyle(line) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
                prompt = new GUIStyle(mid);
            }
            float w = Screen.width, h = Screen.height;
            big.fontSize = Mathf.RoundToInt(h * 0.085f);
            mid.fontSize = Mathf.RoundToInt(h * 0.032f);
            small.fontSize = Mathf.RoundToInt(h * 0.024f);
            line.fontSize = value.fontSize = Mathf.RoundToInt(h * 0.03f);
            prompt.fontSize = Mathf.RoundToInt(h * 0.022f);

            DrawAnnouncement(w, h);
            DrawTicker(w, h);
            if (result != null) DrawResult(w, h);

            if (fade > 0.001f)
            {
                var old = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, fade);
                GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
                GUI.color = old;
            }
        }

        void Shadowed(Rect r, string text, GUIStyle style, Color c)
        {
            var old = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, c.a * 0.7f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
        }

        void DrawAnnouncement(float w, float h)
        {
            float age = Time.time - announceT0;
            if (age > announceDuration) return;
            float a = Mathf.Clamp01(age / 0.3f) * Mathf.Clamp01((announceDuration - age) / 0.6f);
            Shadowed(new Rect(0, h * 0.2f, w, h * 0.1f), announceBig, big, new Color(gold.r, gold.g, gold.b, a));
            Shadowed(new Rect(0, h * 0.3f, w, h * 0.06f), announceSmall, mid, new Color(1f, 1f, 1f, a));
        }

        void DrawTicker(float w, float h)
        {
            float now = Time.time;
            for (int i = ticks.Count - 1; i >= 0; i--)
                if (now - ticks[i].t0 > 3f) ticks.RemoveAt(i);
            for (int i = 0; i < ticks.Count; i++)
            {
                float age = now - ticks[i].t0;
                float a = Mathf.Clamp01(age / 0.2f) * Mathf.Clamp01((3f - age) / 0.5f);
                float y = h * 0.12f + i * h * 0.045f - Mathf.Min(age, 0.3f) * 20f;
                Shadowed(new Rect(0, y, w, h * 0.05f), ticks[i].text, mid, new Color(gold.r, gold.g, gold.b, a));
            }
        }

        void DrawResult(float w, float h)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            GUI.color = old;

            float y = h * 0.14f;
            Shadowed(new Rect(0, y, w, h * 0.11f), result.title, big, result.color);
            y += h * 0.11f;
            if (!string.IsNullOrEmpty(result.subtitle))
            {
                Shadowed(new Rect(0, y, w, h * 0.05f), result.subtitle, mid, Color.white);
                y += h * 0.07f;
            }

            float colW = Mathf.Min(w * 0.5f, 700f), x = (w - colW) * 0.5f, lh = h * 0.045f;
            foreach (var (label, val) in result.lines)
            {
                Shadowed(new Rect(x, y, colW, lh), label, line, Color.white);
                Shadowed(new Rect(x, y, colW, lh), val, value, Color.white);
                y += lh;
            }
            if (!string.IsNullOrEmpty(result.total))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                GUI.DrawTexture(new Rect(x, y + lh * 0.2f, colW, 2f), Texture2D.whiteTexture);
                GUI.color = old;
                y += lh * 0.4f;
                Shadowed(new Rect(x, y, colW, lh * 1.3f), "<b>TOTAL EARNED</b>", line, result.color);
                Shadowed(new Rect(x, y, colW, lh * 1.3f), result.total, value, result.color);
                y += lh * 1.6f;
            }
            if (!string.IsNullOrEmpty(result.footer))
            {
                Shadowed(new Rect(0, y, w, h * 0.05f), result.footer, small, new Color(1f, 1f, 1f, 0.85f));
            }
            float ready = Mathf.Clamp01((Time.unscaledTime - result.t0 - inputDelay) / 0.4f);
            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 4f);
            Shadowed(new Rect(0, h * 0.86f, w, h * 0.05f), result.prompt, prompt, new Color(1f, 1f, 1f, ready * pulse));
        }
    }
}
