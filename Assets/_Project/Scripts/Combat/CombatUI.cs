using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace HauntedCasino.Combat
{
    /// <summary>Screen-space HUD for the fight: ghost bar, player panels, prompts, choices, log, popups.</summary>
    public class CombatUI : MonoBehaviour
    {
        public struct Choice
        {
            public string Title, Sub;
            public Color Color;
            public Action OnPick;

            public Choice(string title, string sub, Color color, Action onPick)
            {
                Title = title;
                Sub = sub;
                Color = color;
                OnPick = onPick;
            }
        }

        class PlayerPanel
        {
            public Combatant C;
            public Image Bg;
            public Outline Highlight;
            public Text Status;
            public UIBar Bar;
            public Button Cover;
        }

        public static readonly Color GhostColor = new Color(0.55f, 0.95f, 0.95f);

        public RectTransform GameHost { get; private set; }

        RectTransform root, choiceRow, playerRow, overlay;
        Text roundText, streakText, promptText, ghostName, logText, coverTimerText;
        UIBar ghostBar;
        Outline ghostHighlight;
        Combatant ghost;
        readonly List<PlayerPanel> panels = new List<PlayerPanel>();
        readonly List<string> lines = new List<string>();
        Action<int> pendingSetup;
        Action pendingRestart;

        public void BuildBase()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var go = new GameObject("CombatCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            root = (RectTransform)go.transform;

            var top = UIFactory.Panel(root, "GhostBar", new Color(0, 0, 0, 0.65f));
            top.rectTransform.Anchor(0.28f, 0.895f, 0.72f, 0.99f);
            ghostHighlight = top.gameObject.AddComponent<Outline>();
            ghostHighlight.effectColor = GhostColor;
            ghostHighlight.effectDistance = new Vector2(4, 4);
            ghostHighlight.enabled = false;
            ghostName = UIFactory.Label(top.transform, "", 26, GhostColor);
            ghostName.rectTransform.Anchor(0, 0.5f, 1, 1, 4);
            ghostBar = new UIBar(top.transform, new Color(0.3f, 0.75f, 0.75f));
            ghostBar.Root.Anchor(0.02f, 0.08f, 0.98f, 0.5f);

            roundText = UIFactory.Label(root, "", 28, Color.white, TextAnchor.MiddleLeft);
            roundText.rectTransform.Anchor(0.02f, 0.92f, 0.26f, 0.99f);
            streakText = UIFactory.Label(root, "", 28, GambleGame.Gold, TextAnchor.MiddleRight);
            streakText.rectTransform.Anchor(0.74f, 0.92f, 0.98f, 0.99f);

            promptText = UIFactory.Label(root, "", 34, GambleGame.Gold);
            promptText.rectTransform.Anchor(0.12f, 0.8f, 0.88f, 0.885f);

            GameHost = UIFactory.Rect(root, "GameHost").Anchor(0.25f, 0.3f, 0.75f, 0.79f);

            choiceRow = UIFactory.Rect(root, "Choices").Anchor(0.03f, 0.23f, 0.755f, 0.35f);
            UIFactory.Row(choiceRow, 16);

            var logBg = UIFactory.Panel(root, "Log", new Color(0, 0, 0, 0.45f));
            logBg.rectTransform.Anchor(0.765f, 0.3f, 0.99f, 0.88f);
            logText = UIFactory.Label(logBg.transform, "", 19, new Color(0.9f, 0.9f, 0.9f), TextAnchor.LowerLeft);
            logText.rectTransform.Anchor(0, 0, 1, 1, 10);

            coverTimerText = UIFactory.Label(root, "", 30, GambleGame.BustRed);
            coverTimerText.rectTransform.Anchor(0.3f, 0.22f, 0.7f, 0.3f);

            playerRow = UIFactory.Rect(root, "Players").Anchor(0.02f, 0.01f, 0.98f, 0.21f);

            overlay = UIFactory.Panel(root, "Overlay", new Color(0, 0, 0, 0.82f)).rectTransform;
            overlay.gameObject.SetActive(false);
        }

        public void BuildCombatants(List<Combatant> players, Combatant ghostCombatant)
        {
            ghost = ghostCombatant;
            ghostName.text = ghost.Name;
            int n = players.Count;
            for (int i = 0; i < n; i++)
            {
                var c = players[i];
                var p = new PlayerPanel { C = c };
                var tint = c.Color * 0.3f;
                tint.a = 0.88f;
                p.Bg = UIFactory.Panel(playerRow, "Player" + (i + 1), tint);
                p.Bg.rectTransform.Anchor((float)i / n, 0, (float)(i + 1) / n, 1, 8);
                p.Highlight = p.Bg.gameObject.AddComponent<Outline>();
                p.Highlight.effectColor = GambleGame.Gold;
                p.Highlight.effectDistance = new Vector2(5, 5);
                p.Highlight.enabled = false;
                var name = UIFactory.Label(p.Bg.transform, $"P{i + 1}  {c.Name}", 28, c.Color, TextAnchor.MiddleLeft);
                name.rectTransform.Anchor(0.05f, 0.66f, 1, 1);
                p.Bar = new UIBar(p.Bg.transform, c.Color * 0.8f);
                p.Bar.Root.Anchor(0.05f, 0.38f, 0.95f, 0.62f);
                p.Status = UIFactory.Label(p.Bg.transform, "", 22, Color.white, TextAnchor.MiddleLeft);
                p.Status.rectTransform.Anchor(0.05f, 0.02f, 0.6f, 0.36f);
                p.Cover = UIFactory.Button(p.Bg.transform, $"COVER! [{i + 1}]", new Color(0.7f, 0.15f, 0.15f), null, 20);
                ((RectTransform)p.Cover.transform).Anchor(0.58f, 0.04f, 0.96f, 0.35f);
                p.Cover.gameObject.SetActive(false);
                panels.Add(p);
            }
            Refresh();
        }

        public void Refresh()
        {
            if (ghost != null) ghostBar.Set(ghost.HP, ghost.MaxHP);
            foreach (var p in panels)
            {
                p.Bar.Set(p.C.HP, p.C.MaxHP);
                p.Status.text = p.C.IsDown ? "<color=#ff5555>BUSTED — needs revive</color>" : "";
            }
        }

        public void SetRound(int round) => roundText.text = $"ROUND {round}";

        public void SetStreak(int streak, float multiplier) =>
            streakText.text = streak > 0 ? $"HOT STREAK {streak}  x{multiplier:0.00}" : "Hot streak: cold";

        public void SetPrompt(string text, Color color)
        {
            promptText.text = text;
            promptText.color = color;
        }

        public void SetActive(Combatant c)
        {
            ghostHighlight.enabled = c == ghost;
            foreach (var p in panels) p.Highlight.enabled = p.C == c;
        }

        public void Log(string line)
        {
            lines.Add(line);
            while (lines.Count > 16) lines.RemoveAt(0);
            logText.text = string.Join("\n", lines);
        }

        public void ShowChoices(List<Choice> choices)
        {
            ClearChildren(choiceRow);
            foreach (var choice in choices)
            {
                var c = choice;
                var bg = c.Color * 0.45f;
                bg.a = 0.95f;
                UIFactory.Button(choiceRow, $"<b>{c.Title}</b>\n<size=17>{c.Sub}</size>", bg, () => c.OnPick(), 26);
            }
            choiceRow.gameObject.SetActive(true);
        }

        public void HideChoices()
        {
            choiceRow.gameObject.SetActive(false);
            ClearChildren(choiceRow);
        }

        public void ShowCoverButtons(List<Combatant> eligible, Action<Combatant> onCover)
        {
            foreach (var p in panels)
            {
                bool show = eligible.Contains(p.C);
                p.Cover.gameObject.SetActive(show);
                p.Cover.onClick.RemoveAllListeners();
                var who = p.C;
                if (show) p.Cover.onClick.AddListener(() => onCover(who));
            }
        }

        public void SetCoverTimer(float secondsLeft) =>
            coverTimerText.text = secondsLeft > 0 ? $"COVER WINDOW {secondsLeft:0.0}s" : "";

        public void HideCoverButtons()
        {
            foreach (var p in panels) p.Cover.gameObject.SetActive(false);
            SetCoverTimer(0);
        }

        public void Popup(Camera cam, Vector3 worldPos, string text, Color color)
        {
            if (cam == null) return;
            Vector3 screen = cam.WorldToScreenPoint(worldPos);
            if (screen.z < 0) return;
            var label = UIFactory.Label(root, text, 46, color);
            label.fontStyle = FontStyle.Bold;
            var rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(500, 90);
            rt.position = screen;
            StartCoroutine(FloatAway(label));
        }

        IEnumerator FloatAway(Text label)
        {
            var rt = label.rectTransform;
            Vector3 start = rt.position;
            Color c = label.color;
            for (float t = 0; t < 1.3f; t += Time.deltaTime)
            {
                rt.position = start + Vector3.up * (t * 110f * root.localScale.y);
                c.a = Mathf.Clamp01(1.6f - t * 1.3f);
                label.color = c;
                yield return null;
            }
            Destroy(label.gameObject);
        }

        public void ShowSetup(int defaultCount, Action<int> onChosen)
        {
            ShowOverlay("THE HAUNTED CASINO",
                "The dead still gamble here. Beat them at their own games.\nSomewhere in these halls lies the lost lucky number.\n\nHow many gamblers at the table?");
            var row = UIFactory.Rect(overlay, "Counts").Anchor(0.3f, 0.25f, 0.7f, 0.37f);
            UIFactory.Row(row, 20);
            for (int i = 1; i <= 4; i++)
            {
                int count = i;
                var color = i == defaultCount ? new Color(0.6f, 0.45f, 0.05f) : new Color(0.25f, 0.2f, 0.25f);
                UIFactory.Button(row, $"{i} [{i}]", color, () => CompleteSetup(count), 34);
            }
            pendingSetup = onChosen;
        }

        public void ShowEnd(bool victory, int rounds, Action onRestart)
        {
            ShowOverlay(victory ? "THE HOUSE LOSES!" : "THE HOUSE ALWAYS WINS...",
                victory
                    ? $"The ghost is banished after {rounds} rounds.\nIt whispers a digit of the lucky number as it fades..."
                    : "Your whole crew went bust.\nYour souls now belong to the casino floor.");
            var btn = UIFactory.Button(overlay, "PLAY AGAIN [R]", new Color(0.6f, 0.45f, 0.05f), () => CompleteRestart(), 34);
            ((RectTransform)btn.transform).Anchor(0.4f, 0.25f, 0.6f, 0.35f);
            pendingRestart = onRestart;
        }

        void ShowOverlay(string title, string body)
        {
            ClearChildren(overlay);
            overlay.gameObject.SetActive(true);
            overlay.SetAsLastSibling();
            var t = UIFactory.Label(overlay, title, 80, GambleGame.Gold);
            t.fontStyle = FontStyle.Bold;
            t.rectTransform.Anchor(0.1f, 0.66f, 0.9f, 0.85f);
            var b = UIFactory.Label(overlay, body, 30, Color.white);
            b.rectTransform.Anchor(0.15f, 0.4f, 0.85f, 0.66f);
        }

        void CompleteSetup(int count)
        {
            var cb = pendingSetup;
            pendingSetup = null;
            overlay.gameObject.SetActive(false);
            cb?.Invoke(count);
        }

        void CompleteRestart()
        {
            var cb = pendingRestart;
            pendingRestart = null;
            cb?.Invoke();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (pendingSetup != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) CompleteSetup(1);
                else if (kb.digit2Key.wasPressedThisFrame) CompleteSetup(2);
                else if (kb.digit3Key.wasPressedThisFrame) CompleteSetup(3);
                else if (kb.digit4Key.wasPressedThisFrame) CompleteSetup(4);
            }
            else if (pendingRestart != null && kb.rKey.wasPressedThisFrame)
            {
                CompleteRestart();
            }
        }

        static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
        }
    }
}
