using System;
using System.Collections;
using System.Collections.Generic;
using HauntedCasino.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace HauntedCasino.Combat
{
    /// <summary>
    /// Haunted Art Deco combat HUD built in code.
    /// Top: round + turn order (left), ghost plate + intent (centre), hot streak + team meter (right).
    /// Middle: duel banner, instruction line with key caps and a countdown bar, game panel (left), table log (right).
    /// Bottom: player cards with chips, role badges, "your move" callouts, side bets and defend buttons.
    /// </summary>
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

        public enum SlotState { Upcoming, Current, Done, Down }

        public struct TurnSlot
        {
            public string Label;
            public Color Color;
            public SlotState State;

            public TurnSlot(string label, Color color, SlotState state)
            {
                Label = label;
                Color = color;
                State = state;
            }
        }

        class PlayerCard
        {
            public Combatant C;
            public RectTransform Root;
            public Image Body;
            public Image Rim;
            public Color BodyColor;
            public TextMeshProUGUI Status, RoleText, CalloutText, Busted;
            public Image RoleBadge;
            public RectTransform Callout;
            public AnimatedBar Bar;
            public Button Defend;
            public RectTransform BetRow;
            public Image BigBg, SmallBg;
            public string Intent = "";
            public string Bet = "";
        }

        static UITheme T => UITheme.Current;
        public static Color GhostColor => T.ghost;
        public static Color AttackColor => T.attack;
        public static Color DefendColor => T.defend;
        public static Color TargetColor => T.target;
        public static Color PartyColor => T.party;
        public static readonly string[] BigKeys = { "Q", "W", "E", "R" };
        public static readonly string[] SmallKeys = { "A", "S", "D", "F" };

        public RectTransform GameHost { get; private set; }

        RectTransform root, choiceRow, playerRow, overlay, turnStrip, streakPips, teamPips, turnCard, duelRoot, timerRoot, timerFill;
        TextMeshProUGUI roundText, streakText, teamText, promptText, ghostName, ghostRoleText, intentText, logText, duelText, turnCardText, turnCardSub;
        Image duelBody, ghostRoleBadge, ghostRim, timerFillImg, overlaySun, turnCardSun;
        AnimatedBar ghostBar;
        CanvasGroup turnCardGroup;
        Combatant ghost;
        readonly List<PlayerCard> cards = new List<PlayerCard>();
        readonly List<string> lines = new List<string>();
        Action<int> pendingSetup;
        Action pendingRestart;
        string lastDuel;
        bool teamReady;

        // ======================================================================
        // Build
        // ======================================================================

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

            UIFactory.Panel(root, "Vignette", new Color(0f, 0f, 0f, 0.8f), DecoSprites.Vignette);

            BuildTopLeft();
            BuildGhostPlate();
            BuildTopRight();
            BuildCentre();

            GameHost = UIFactory.Rect(root, "GameHost").Anchor(0.01f, 0.255f, 0.43f, 0.705f);

            choiceRow = UIFactory.Rect(root, "Choices").Anchor(0.03f, 0.262f, 0.76f, 0.392f);
            UIFactory.Row(choiceRow, 18);
            choiceRow.gameObject.SetActive(false);

            var log = UIFactory.Deco(root, "TableTalk", T.panel);
            log.rectTransform.Anchor(0.775f, 0.235f, 0.99f, 0.705f);
            var logTitle = UIFactory.Label(log.transform, "TABLE TALK", T.small + 2, T.gold, TextAnchor.UpperCenter, true);
            logTitle.rectTransform.Anchor(0, 0.88f, 1, 0.97f);
            logText = UIFactory.Label(log.transform, "", T.small, T.text, TextAnchor.LowerLeft);
            logText.rectTransform.Anchor(0.06f, 0.04f, 0.95f, 0.87f);
            logText.lineSpacing = 6f;

            playerRow = UIFactory.Rect(root, "Players").Anchor(0.012f, 0.012f, 0.988f, 0.2f);

            BuildTurnCard();

            overlay = UIFactory.Panel(root, "Overlay", new Color(T.backdrop.r, T.backdrop.g, T.backdrop.b, 0.94f)).rectTransform;
            overlay.GetComponent<Image>().raycastTarget = true;
            overlay.gameObject.SetActive(false);
        }

        void BuildTopLeft()
        {
            roundText = UIFactory.Label(root, "", T.title, T.gold, TextAnchor.MiddleLeft, true);
            roundText.rectTransform.Anchor(0.015f, 0.935f, 0.27f, 0.99f);
            turnStrip = UIFactory.Rect(root, "TurnOrder").Anchor(0.015f, 0.87f, 0.29f, 0.93f);
        }

        void BuildGhostPlate()
        {
            var plate = UIFactory.Deco(root, "GhostPlate", Color.Lerp(T.panel, T.burgundy, 0.45f));
            plate.rectTransform.Anchor(0.3f, 0.9f, 0.7f, 0.99f);
            ghostRim = UIFactory.BorderOf(plate);
            ghostName = UIFactory.Label(plate.transform, "", T.heading - 2, T.ghost, TextAnchor.MiddleCenter, true);
            ghostName.rectTransform.Anchor(0.22f, 0.5f, 0.97f, 0.97f);
            ghostName.textWrappingMode = TextWrappingModes.NoWrap;
            ghostName.enableAutoSizing = true;
            ghostName.fontSizeMin = 14;
            ghostName.fontSizeMax = T.heading - 2;
            ghostRoleBadge = UIFactory.Panel(plate.transform, "Role", T.target, DecoSprites.PillFill, 2f);
            ghostRoleBadge.rectTransform.Anchor(0.025f, 0.56f, 0.2f, 0.92f);
            ghostRoleText = UIFactory.Label(ghostRoleBadge.transform, "", T.small - 3, Color.white, TextAnchor.MiddleCenter, true);
            ghostRoleBadge.gameObject.SetActive(false);
            ghostBar = AnimatedBar.Create(plate.transform, T.ghost * new Color(0.6f, 0.8f, 0.75f, 1f), T.small);
            ghostBar.Root.Anchor(0.03f, 0.12f, 0.97f, 0.5f);

            var intent = UIFactory.Panel(root, "Intent", new Color(T.burgundy.r * 0.6f, 0.01f, 0.03f, 0.88f), DecoSprites.PillFill, 2f);
            intent.rectTransform.Anchor(0.33f, 0.858f, 0.67f, 0.895f);
            UIFactory.Panel(intent.transform, "Border", T.target * new Color(1, 1, 1, 0.5f), DecoSprites.PillBorder, 2f);
            intentText = UIFactory.Label(intent.transform, "", T.small, new Color(1f, 0.8f, 0.78f));
        }

        void BuildTopRight()
        {
            streakText = UIFactory.Label(root, "", T.heading - 4, T.goldBright, TextAnchor.MiddleRight, true);
            streakText.rectTransform.Anchor(0.72f, 0.945f, 0.985f, 0.99f);
            streakPips = UIFactory.Rect(root, "StreakPips").Anchor(0.86f, 0.918f, 0.985f, 0.945f);
            teamText = UIFactory.Label(root, "", T.small + 2, T.party, TextAnchor.MiddleRight, true);
            teamText.rectTransform.Anchor(0.72f, 0.878f, 0.985f, 0.915f);
            teamPips = UIFactory.Rect(root, "TeamPips").Anchor(0.84f, 0.852f, 0.985f, 0.878f);
        }

        void BuildCentre()
        {
            duelRoot = UIFactory.Rect(root, "Duel").Anchor(0.25f, 0.772f, 0.75f, 0.85f);
            var sun = UIFactory.Panel(duelRoot, "Sunburst", new Color(1f, 1f, 1f, 0.2f), DecoSprites.Sunburst);
            sun.rectTransform.Anchor(0.2f, -1.6f, 0.8f, 2.6f);
            duelBody = UIFactory.Deco(duelRoot, "Banner", T.panelRaised);
            duelText = UIFactory.Label(duelBody.transform, "", T.title - 4, T.text, TextAnchor.MiddleCenter, true);
            duelRoot.gameObject.SetActive(false);

            var promptBack = UIFactory.Panel(root, "PromptBack", new Color(0f, 0f, 0f, 0.55f), DecoSprites.PillFill, 1.5f);
            promptBack.rectTransform.Anchor(0.2f, 0.728f, 0.8f, 0.77f);
            promptText = UIFactory.Label(root, "", T.body + 2, T.text);
            promptText.rectTransform.Anchor(0.1f, 0.726f, 0.9f, 0.772f);
            promptBack.gameObject.AddComponent<PromptBackSync>().Bind(promptText);

            timerRoot = UIFactory.Panel(root, "Timer", new Color(0, 0, 0, 0.7f), DecoSprites.PillFill, 4f).rectTransform;
            timerRoot.Anchor(0.36f, 0.712f, 0.64f, 0.724f);
            timerFillImg = UIFactory.Panel(timerRoot, "Fill", T.gold, DecoSprites.PillFill, 4f);
            timerFill = timerFillImg.rectTransform;
            timerFill.Anchor(0, 0, 1, 1, 1);
            timerRoot.gameObject.SetActive(false);
        }

        void BuildTurnCard()
        {
            turnCard = UIFactory.Rect(root, "TurnCard").Anchor(0f, 0.42f, 1f, 0.62f);
            turnCardGroup = turnCard.gameObject.AddComponent<CanvasGroup>();
            turnCardGroup.blocksRaycasts = false;
            turnCardGroup.alpha = 0f;
            UIFactory.Panel(turnCard, "Band", new Color(0f, 0f, 0f, 0.72f));
            turnCardSun = UIFactory.Panel(turnCard, "Sunburst", new Color(1f, 1f, 1f, 0.25f), DecoSprites.Sunburst);
            turnCardSun.rectTransform.Anchor(0.3f, -1.2f, 0.7f, 2.2f);
            var topRule = UIFactory.Panel(turnCard, "TopRule", T.gold);
            topRule.rectTransform.Anchor(0f, 0.97f, 1f, 0.985f);
            var bottomRule = UIFactory.Panel(turnCard, "BottomRule", T.gold);
            bottomRule.rectTransform.Anchor(0f, 0.015f, 1f, 0.03f);
            turnCardText = UIFactory.Label(turnCard, "", T.hero, T.goldBright, TextAnchor.MiddleCenter, true);
            turnCardText.rectTransform.Anchor(0f, 0.25f, 1f, 1f);
            turnCardSub = UIFactory.Label(turnCard, "", T.body, T.text, TextAnchor.MiddleCenter);
            turnCardSub.rectTransform.Anchor(0f, 0.05f, 1f, 0.32f);
            turnCard.gameObject.SetActive(false);
        }

        public void BuildCombatants(List<Combatant> players, Combatant ghostCombatant)
        {
            ghost = ghostCombatant;
            ghostName.text = ghost.Name.ToUpper();
            ghostBar.Set(ghost.HP, ghost.MaxHP, true);
            int n = players.Count;
            for (int i = 0; i < n; i++) cards.Add(BuildCard(players[i], i, n));
            Refresh();
        }

        PlayerCard BuildCard(Combatant c, int i, int n)
        {
            var card = new PlayerCard { C = c };
            card.BodyColor = Color.Lerp(T.panel, c.Color, 0.14f);
            card.BodyColor.a = 0.95f;
            card.Body = UIFactory.Deco(playerRow, "Player" + (i + 1), card.BodyColor);
            card.Root = card.Body.rectTransform;
            card.Root.Anchor((float)i / n, 0f, (float)(i + 1) / n, 1f, 8f);
            card.Rim = UIFactory.BorderOf(card.Body);

            // Poker chip with the seat number.
            var chip = UIFactory.Rect(card.Root, "Chip");
            chip.anchorMin = chip.anchorMax = new Vector2(0f, 0.5f);
            chip.pivot = new Vector2(0f, 0.5f);
            chip.sizeDelta = new Vector2(118f, 118f);
            chip.anchoredPosition = new Vector2(16f, 0f);
            UIFactory.Panel(chip, "Base", c.Color, DecoSprites.ChipBase);
            UIFactory.Panel(chip, "Marks", new Color(1f, 1f, 1f, 0.9f), DecoSprites.ChipMarks);
            UIFactory.Label(chip, $"P{i + 1}", T.heading - 2, Color.white, TextAnchor.MiddleCenter, true);

            // Content starts right of the chip, whatever the card width (2-4 players).
            float cardWidth = 1920f * 0.976f / n - 16f;
            float left = 150f / cardWidth;
            var name = UIFactory.Label(card.Root, c.Name.ToUpper(), T.heading - 4, c.Color, TextAnchor.MiddleLeft, true);
            name.rectTransform.Anchor(left, 0.64f, 0.75f, 0.95f);

            card.RoleBadge = UIFactory.Panel(card.Root, "Role", T.attack, DecoSprites.PillFill, 2f);
            card.RoleBadge.rectTransform.Anchor(0.66f, 0.68f, 0.97f, 0.93f);
            card.RoleText = UIFactory.Label(card.RoleBadge.transform, "", T.small - 4, Color.white, TextAnchor.MiddleCenter, true);
            card.RoleBadge.gameObject.SetActive(false);

            card.Bar = AnimatedBar.Create(card.Root, c.Color * new Color(0.85f, 0.85f, 0.85f, 1f), T.small - 1, " chips");
            card.Bar.Root.Anchor(left, 0.4f, 0.97f, 0.62f);
            card.Bar.Set(c.HP, c.MaxHP, true);

            card.Status = UIFactory.Label(card.Root, "", T.small - 1, T.textDim, TextAnchor.MiddleLeft);
            card.Status.rectTransform.Anchor(left, 0.05f, 0.97f, 0.37f);

            card.BetRow = UIFactory.Rect(card.Root, "SideBets").Anchor(0.56f, 0.06f, 0.97f, 0.36f);
            UIFactory.Row(card.BetRow, 6);
            card.BigBg = UIFactory.Button(card.BetRow, $"BIG {BigKeys[i]}", T.panelRaised, null, T.small - 3, true).GetComponent<Image>();
            card.SmallBg = UIFactory.Button(card.BetRow, $"SMALL {SmallKeys[i]}", T.panelRaised, null, T.small - 3, true).GetComponent<Image>();
            card.BetRow.gameObject.SetActive(false);

            card.Defend = UIFactory.Button(card.Root, $"DEFEND!  {i + 1}", T.defend * new Color(0.55f, 0.55f, 0.55f, 1f), null, T.small, true);
            ((RectTransform)card.Defend.transform).Anchor(0.56f, 0.05f, 0.97f, 0.37f);
            card.Defend.gameObject.SetActive(false);

            // "Your move" callout above the card.
            var callout = UIFactory.Panel(card.Root, "Callout", T.goldBright, DecoSprites.PillFill, 1.5f);
            card.Callout = callout.rectTransform;
            card.Callout.anchorMin = new Vector2(0.28f, 1f);
            card.Callout.anchorMax = new Vector2(0.72f, 1f);
            card.Callout.pivot = new Vector2(0.5f, 0f);
            card.Callout.sizeDelta = new Vector2(0f, 42f);
            card.Callout.anchoredPosition = new Vector2(0f, 8f);
            card.CalloutText = UIFactory.Label(card.Callout, "", T.small + 2, T.backdrop, TextAnchor.MiddleCenter, true);
            card.CalloutText.fontSharedMaterial = T.Display != null ? T.Display.material : card.CalloutText.fontSharedMaterial;
            card.Callout.gameObject.SetActive(false);

            card.Busted = UIFactory.Label(card.Root, "BUSTED", T.title + 6, T.target, TextAnchor.MiddleCenter, true);
            card.Busted.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 10f);
            card.Busted.gameObject.SetActive(false);
            return card;
        }

        // ======================================================================
        // State updates
        // ======================================================================

        public void Refresh()
        {
            if (ghost != null) ghostBar.Set(ghost.HP, ghost.MaxHP);
            foreach (var c in cards)
            {
                c.Bar.Set(c.C.HP, c.C.MaxHP);
                bool down = c.C.IsDown;
                if (c.Busted.gameObject.activeSelf != down)
                {
                    c.Busted.gameObject.SetActive(down);
                    if (down) UITween.Pop(c.Busted.transform);
                }
                c.Body.color = down ? new Color(0.06f, 0.05f, 0.06f, 0.95f) : c.BodyColor;
                c.Status.text = down ? "Needs a teammate to revive"
                    : !string.IsNullOrEmpty(c.Bet) ? c.Bet
                    : c.Intent;
                bool narrow = c.BetRow.gameObject.activeSelf || c.Defend.gameObject.activeSelf;
                c.Status.rectTransform.anchorMax = new Vector2(narrow ? 0.55f : 0.97f, 0.37f);
            }
        }

        public void SetRound(int round)
        {
            roundText.text = $"ROUND {round}";
            UITween.Punch(roundText.transform, 0.2f);
        }

        public void SetStreak(int streak, float multiplier, int maxSteps)
        {
            streakText.text = streak > 0 ? $"HOT STREAK  x{multiplier:0.00}" : "STREAK  COLD";
            streakText.color = streak > 0 ? T.goldBright : T.textDim;
            SetPips(streakPips, Mathf.Min(streak, maxSteps), maxSteps, T.attack);
            if (streak > 0) UITween.Punch(streakText.transform, 0.12f);
        }

        public void SetTeamMeter(int value, int max)
        {
            bool ready = value >= max;
            teamText.text = ready ? "TEAM JACKPOT READY!" : "TEAM JACKPOT";
            if (ready && !teamReady) UITween.Punch(teamText.transform, 0.3f, 0.4f);
            teamReady = ready;
            SetPips(teamPips, value, max, T.party);
        }

        static void SetPips(RectTransform row, int lit, int total, Color color)
        {
            for (int i = row.childCount - 1; i >= 0; i--) Destroy(row.GetChild(i).gameObject);
            if (total <= 0) return;
            float w = 1f / total;
            for (int i = 0; i < total; i++)
            {
                // Right-aligned pips.
                int slot = total - 1 - i;
                var pip = UIFactory.Panel(row, "Pip", i < lit ? color : new Color(1f, 1f, 1f, 0.15f), DecoSprites.Diamond);
                pip.rectTransform.Anchor(1f - (slot + 1) * w, 0f, 1f - slot * w, 1f, 2f);
                pip.preserveAspect = true;
            }
        }

        public void SetPrompt(string text, Color color)
        {
            promptText.text = text;
            promptText.color = color;
        }

        /// <summary>Countdown bar under the instruction line. fraction 1 = full.</summary>
        public void SetTimer(float fraction, Color color)
        {
            if (!timerRoot.gameObject.activeSelf) timerRoot.gameObject.SetActive(true);
            timerFill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            timerFillImg.color = Color.Lerp(T.target, color, Mathf.Clamp01(fraction * 2f));
        }

        public void HideTimer() => timerRoot.gameObject.SetActive(false);

        /// <summary>Big centre banner, e.g. "ACE  attacks  LUCKY LOU". Empty text hides it.</summary>
        public void SetDuel(string richText, Color tint)
        {
            bool show = !string.IsNullOrEmpty(richText);
            duelRoot.gameObject.SetActive(show);
            if (!show)
            {
                lastDuel = null;
                return;
            }
            duelText.text = richText;
            duelBody.color = Color.Lerp(T.panelRaised, tint, 0.22f);
            var rim = UIFactory.BorderOf(duelBody);
            if (rim != null) rim.color = Color.Lerp(T.gold, tint, 0.35f);
            duelRoot.GetChild(0).GetComponent<Image>().color = new Color(tint.r, tint.g, tint.b, 0.22f);
            if (richText != lastDuel) UITween.Pop(duelRoot, 0f, 0.28f);
            lastDuel = richText;
        }

        public void SetIntent(string richText)
        {
            bool show = !string.IsNullOrEmpty(richText);
            var strip = intentText.transform.parent.gameObject;
            if (show && (!strip.activeSelf || intentText.text != richText)) UITween.Pop(strip.transform, 0f, 0.25f);
            intentText.text = richText ?? "";
            strip.SetActive(show);
        }

        public void SetPanelIntent(Combatant c, string text)
        {
            var card = cards.Find(x => x.C == c);
            if (card != null) card.Intent = text;
        }

        /// <summary>Badges and outlines for who attacks and who defends; active human cards lift up.</summary>
        public void SetRoles(Combatant attacker, Combatant defender, string defenderLabel = "DEFENDING")
        {
            Color DefColor() => defenderLabel == "DEFENDING" ? T.defend : T.target;

            bool ghostActive = ghost == attacker || ghost == defender;
            ghostRoleBadge.gameObject.SetActive(ghostActive);
            if (ghostActive)
            {
                var color = ghost == attacker ? T.attack : DefColor();
                ghostRoleBadge.color = color;
                ghostRoleText.text = ghost == attacker ? "ATTACKING" : defenderLabel;
                ghostRim.color = color;
            }
            else ghostRim.color = T.gold;

            foreach (var c in cards)
            {
                bool isAttacker = c.C == attacker, isDefender = c.C == defender;
                bool active = isAttacker || isDefender;
                c.RoleBadge.gameObject.SetActive(active);
                var color = isAttacker ? T.attack : DefColor();
                if (active)
                {
                    bool wasActive = c.RoleText.text == (isAttacker ? "ATTACKING" : defenderLabel);
                    c.RoleBadge.color = color;
                    c.RoleText.text = isAttacker ? "ATTACKING" : defenderLabel;
                    if (!wasActive) UITween.Pop(c.RoleBadge.transform);
                }
                else c.RoleText.text = "";
                c.Rim.color = active ? color : T.gold;
                UITween.MoveTo(c.Root, new Vector2(0f, active ? 14f : 0f));
            }
        }

        /// <summary>"YOUR MOVE"-style tag above one player's card. Null hides all.</summary>
        public void SetCallout(Combatant who, string text)
        {
            foreach (var c in cards)
            {
                bool show = c.C == who && !string.IsNullOrEmpty(text);
                bool changed = show && (!c.Callout.gameObject.activeSelf || c.CalloutText.text != text);
                c.Callout.gameObject.SetActive(show);
                if (!show) continue;
                c.CalloutText.text = text;
                if (changed) UITween.Pop(c.Callout);
            }
        }

        public void SetTurnOrder(List<TurnSlot> slots)
        {
            for (int i = turnStrip.childCount - 1; i >= 0; i--) Destroy(turnStrip.GetChild(i).gameObject);
            const float size = 46f, gap = 8f;
            float x = 0f;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                bool current = s.State == SlotState.Current;
                bool faded = s.State == SlotState.Done || s.State == SlotState.Down;
                var chip = UIFactory.Rect(turnStrip, s.Label);
                chip.anchorMin = chip.anchorMax = new Vector2(0f, 0.5f);
                chip.pivot = new Vector2(0f, 0.5f);
                float sz = current ? size * 1.2f : size;
                chip.sizeDelta = new Vector2(sz, sz);
                chip.anchoredPosition = new Vector2(x, 0f);
                x += sz + gap;
                if (current) UIFactory.Panel(chip, "Glow", T.goldBright * new Color(1, 1, 1, 0.7f), DecoSprites.Glow).rectTransform.Anchor(-0.35f, -0.35f, 1.35f, 1.35f);
                var color = faded ? s.Color * new Color(0.35f, 0.35f, 0.35f, 1f) : s.Color;
                UIFactory.Panel(chip, "Base", color, DecoSprites.ChipBase);
                UIFactory.Panel(chip, "Marks", new Color(1f, 1f, 1f, faded ? 0.3f : 0.85f), DecoSprites.ChipMarks);
                var label = UIFactory.Label(chip, s.Label, current ? T.small : T.small - 4, faded ? T.textDim : Color.white, TextAnchor.MiddleCenter, true);
                if (s.State == SlotState.Down) label.text = $"<s>{s.Label}</s>";
                if (current) UITween.Pop(chip);
                if (i == slots.Count - 1) continue;
                // Little gold diamond between player seats and the ghost's moves.
                if (slots[i + 1].Label != s.Label && (slots[i + 1].Color == T.ghost) != (s.Color == T.ghost))
                {
                    var sep = UIFactory.Panel(turnStrip, "Sep", T.gold, DecoSprites.Diamond).rectTransform;
                    sep.anchorMin = sep.anchorMax = new Vector2(0f, 0.5f);
                    sep.pivot = new Vector2(0f, 0.5f);
                    sep.sizeDelta = new Vector2(14f, 14f);
                    sep.anchoredPosition = new Vector2(x, 0f);
                    x += 14f + gap;
                }
            }
        }

        /// <summary>Full-width name card that sweeps across at the start of a turn.</summary>
        public IEnumerator TurnCard(string title, string sub, Color color)
        {
            if (!T.turnCards) yield break;
            turnCard.gameObject.SetActive(true);
            turnCard.SetAsLastSibling();
            turnCardText.text = title;
            turnCardText.color = color;
            turnCardSub.text = sub;
            turnCardSun.color = new Color(color.r, color.g, color.b, 0.22f);
            float total = T.turnCardSeconds;
            var textRt = turnCardText.rectTransform;
            for (float e = 0; e < total; e += Time.unscaledDeltaTime)
            {
                float k = e / total;
                turnCardGroup.alpha = k < 0.2f ? k / 0.2f : k > 0.8f ? (1f - k) / 0.2f : 1f;
                // Fast in, slow drift, fast out.
                float slide = k < 0.25f ? Mathf.Lerp(-500f, 0f, UITween.EaseOutCubic(k / 0.25f))
                    : k > 0.8f ? Mathf.Lerp(0f, 500f, (k - 0.8f) / 0.2f)
                    : Mathf.Lerp(0f, 40f, (k - 0.25f) / 0.55f);
                textRt.anchoredPosition = new Vector2(slide, 0f);
                turnCardSun.rectTransform.localRotation = Quaternion.Euler(0f, 0f, e * 20f);
                yield return null;
            }
            turnCardGroup.alpha = 0f;
            turnCard.gameObject.SetActive(false);
        }

        public void Log(string line)
        {
            lines.Add(line);
            while (lines.Count > 9) lines.RemoveAt(0);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                int age = lines.Count - 1 - i;
                float alpha = Mathf.Clamp01(1f - age * 0.09f);
                string bullet = age == 0 ? UIFactory.Colored("• ", T.goldBright) : UIFactory.Colored("• ", T.gold * new Color(1, 1, 1, 0.5f));
                sb.Append($"<alpha=#{Mathf.RoundToInt(alpha * 255):X2}>{bullet}{lines[i]}");
                if (i < lines.Count - 1) sb.Append('\n');
            }
            logText.text = sb.ToString();
        }

        // ======================================================================
        // Choices
        // ======================================================================

        public void ShowChoices(List<Choice> choices)
        {
            ClearChildren(choiceRow);
            for (int i = 0; i < choices.Count; i++)
            {
                var c = choices[i];
                var body = UIFactory.Deco(choiceRow, "Choice", Color.Lerp(T.panelRaised, c.Color, 0.18f), Color.Lerp(T.gold, c.Color, 0.4f));
                body.raycastTarget = true;
                var btn = body.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => c.OnPick());
                body.gameObject.AddComponent<UIButtonFX>();

                var key = UIFactory.Panel(body.transform, "Key", T.gold * new Color(1, 1, 1, 0.3f), DecoSprites.PillFill, 2f);
                key.rectTransform.anchorMin = key.rectTransform.anchorMax = new Vector2(0f, 1f);
                key.rectTransform.pivot = new Vector2(0f, 1f);
                key.rectTransform.sizeDelta = new Vector2(40f, 40f);
                key.rectTransform.anchoredPosition = new Vector2(12f, -10f);
                UIFactory.Label(key.transform, (i + 1).ToString(), T.body, T.goldBright, TextAnchor.MiddleCenter, true);

                var title = UIFactory.Label(body.transform, c.Title.ToUpper(), T.body + 2, Color.Lerp(T.text, c.Color, 0.55f), TextAnchor.MiddleCenter, true);
                title.rectTransform.Anchor(0.12f, 0.5f, 0.97f, 0.92f);
                var sub = UIFactory.Label(body.transform, c.Sub, T.small - 3, T.textDim, TextAnchor.MiddleCenter);
                sub.rectTransform.Anchor(0.05f, 0.08f, 0.95f, 0.52f);
                UITween.Pop(body.transform, i * 0.05f, 0.28f);
            }
            choiceRow.gameObject.SetActive(true);
        }

        public void HideChoices()
        {
            choiceRow.gameObject.SetActive(false);
            ClearChildren(choiceRow);
        }

        // ======================================================================
        // Side bets & defend window
        // ======================================================================

        public void ShowSideBets(List<Combatant> bettors, Action<Combatant, bool> onBet)
        {
            foreach (var c in cards)
            {
                bool show = bettors.Contains(c.C);
                bool wasShown = c.BetRow.gameObject.activeSelf;
                c.BetRow.gameObject.SetActive(show);
                c.Bet = show ? UIFactory.Colored("Side bet: big or small?", T.gold) : "";
                c.BigBg.color = T.panelRaised;
                c.SmallBg.color = T.panelRaised;
                var big = c.BigBg.GetComponent<Button>();
                var small = c.SmallBg.GetComponent<Button>();
                big.onClick.RemoveAllListeners();
                small.onClick.RemoveAllListeners();
                if (!show) continue;
                if (!wasShown) UITween.Pop(c.BetRow, 0.05f * cards.IndexOf(c));
                var who = c.C;
                big.onClick.AddListener(() => onBet(who, true));
                small.onClick.AddListener(() => onBet(who, false));
            }
            Refresh();
        }

        public void SetBet(Combatant who, bool big)
        {
            var c = cards.Find(x => x.C == who);
            if (c == null) return;
            c.BigBg.color = big ? Color.Lerp(T.panelRaised, T.gold, 0.7f) : T.panelRaised;
            c.SmallBg.color = big ? T.panelRaised : Color.Lerp(T.panelRaised, T.gold, 0.7f);
            UITween.Punch(big ? c.BigBg.transform : c.SmallBg.transform, 0.18f);
            c.Bet = UIFactory.Colored($"Bet placed: {(big ? "BIG" : "SMALL")}", T.goldBright);
            Refresh();
        }

        public void LockSideBets()
        {
            foreach (var c in cards)
            {
                c.BetRow.gameObject.SetActive(false);
                if (c.Bet.Contains("?")) c.Bet = "";
            }
            Refresh();
        }

        public void ClearSideBets()
        {
            foreach (var c in cards)
            {
                c.BetRow.gameObject.SetActive(false);
                c.Bet = "";
            }
            Refresh();
        }

        public void ShowDefendButtons(List<Combatant> eligible, Action<Combatant> onDefend)
        {
            foreach (var c in cards)
            {
                bool show = eligible.Contains(c.C);
                c.Defend.gameObject.SetActive(show);
                c.Defend.onClick.RemoveAllListeners();
                var who = c.C;
                if (!show) continue;
                c.Defend.onClick.AddListener(() => onDefend(who));
                UITween.Pop(c.Defend.transform);
            }
            Refresh();
        }

        public void HideDefendButtons()
        {
            foreach (var c in cards) c.Defend.gameObject.SetActive(false);
            HideTimer();
            Refresh();
        }

        // ======================================================================
        // Popups & overlays
        // ======================================================================

        public void Popup(Camera cam, Vector3 worldPos, string text, Color color)
        {
            if (cam == null) return;
            Vector3 screen = cam.WorldToScreenPoint(worldPos);
            if (screen.z < 0) return;
            var label = UIFactory.Label(root, text, T.title + 10, color, TextAnchor.MiddleCenter, true);
            var rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(600, 100);
            rt.position = screen;
            StartCoroutine(FloatAway(label));
        }

        IEnumerator FloatAway(TextMeshProUGUI label)
        {
            var rt = label.rectTransform;
            Vector3 start = rt.position;
            Color c = label.color;
            for (float t = 0; t < 1.3f; t += Time.unscaledDeltaTime)
            {
                float scale = t < 0.15f ? Mathf.Lerp(0.4f, 1.35f, t / 0.15f) : Mathf.Lerp(1.35f, 1f, Mathf.Clamp01((t - 0.15f) / 0.2f));
                rt.localScale = Vector3.one * scale;
                rt.position = start + Vector3.up * (t * 90f * root.localScale.y);
                c.a = Mathf.Clamp01(1.6f - t * 1.3f);
                label.color = c;
                yield return null;
            }
            Destroy(label.gameObject);
        }

        public void ShowSetup(int defaultCount, Action<int> onChosen)
        {
            var card = ShowOverlay("THE HAUNTED CASINO",
                "The dead still gamble here. Beat them at their own games.\nSomewhere in these halls lies the lost lucky number.");
            var ask = UIFactory.Label(card, "HOW MANY GAMBLERS AT THE TABLE?", T.heading - 4, T.gold, TextAnchor.MiddleCenter, true);
            ask.rectTransform.Anchor(0.05f, 0.44f, 0.95f, 0.52f);

            var row = UIFactory.Rect(card, "Counts").Anchor(0.2f, 0.25f, 0.8f, 0.43f);
            for (int i = 1; i <= 4; i++)
            {
                int count = i;
                var cell = UIFactory.Rect(row, "Cell" + i).Anchor((i - 1) / 4f, 0f, i / 4f, 1f, 10f);
                var chip = UIFactory.Rect(cell, "Count" + i);
                chip.sizeDelta = new Vector2(150f, 150f);
                var baseImg = UIFactory.Panel(chip, "Base", i == defaultCount ? T.gold : T.burgundy, DecoSprites.ChipBase);
                baseImg.raycastTarget = true;
                UIFactory.Panel(chip, "Marks", new Color(1f, 1f, 1f, 0.85f), DecoSprites.ChipMarks);
                UIFactory.Label(chip, i.ToString(), T.title + 8, Color.white, TextAnchor.MiddleCenter, true);
                var btn = chip.gameObject.AddComponent<Button>();
                btn.targetGraphic = baseImg;
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => CompleteSetup(count));
                chip.gameObject.AddComponent<UIButtonFX>();
                UITween.Pop(chip, 0.15f + i * 0.06f);
            }

            var k = (Func<string, string>)UIFactory.Key;
            var help = UIFactory.Label(card,
                $"Press {k("1")}–{k("4")} or click a chip.\n\n" +
                $"Your <color={UITheme.Hex(T.goldBright)}>chips are your life</color>: stake them to hit harder.   " +
                $"Not your roll? Side bet BIG/SMALL: P1 {k("Q")}{k("A")}  P2 {k("W")}{k("S")}  P3 {k("E")}{k("D")}  P4 {k("R")}{k("F")}\n" +
                $"Watch the ghost's NEXT moves and press your number to DEFEND a teammate.",
                T.body - 2, T.textDim, TextAnchor.MiddleCenter);
            help.rectTransform.Anchor(0.04f, 0.03f, 0.96f, 0.25f);
            pendingSetup = onChosen;
        }

        public void ShowEnd(bool victory, int rounds, Action onRestart)
        {
            var card = ShowOverlay(victory ? "THE HOUSE LOSES!" : "THE HOUSE ALWAYS WINS",
                victory
                    ? $"The ghost is banished after {rounds} rounds.\nAs it fades, it whispers one digit of the lucky number..."
                    : "Your whole crew went bust.\nYour souls now belong to the casino floor.");
            var btn = UIFactory.Button(card, $"PLAY AGAIN  {UIFactory.Key("R")}", T.burgundy, () => CompleteRestart(), T.heading - 2, true);
            ((RectTransform)btn.transform).Anchor(0.32f, 0.16f, 0.68f, 0.3f);
            UITween.Pop(btn.transform, 0.3f);
            pendingRestart = onRestart;
        }

        Transform ShowOverlay(string title, string body)
        {
            ClearChildren(overlay);
            overlay.gameObject.SetActive(true);
            overlay.SetAsLastSibling();
            overlaySun = UIFactory.Panel(overlay, "Sunburst", T.gold * new Color(1, 1, 1, 0.12f), DecoSprites.Sunburst);
            overlaySun.rectTransform.Anchor(0.05f, -0.4f, 0.95f, 1.4f);
            overlaySun.preserveAspect = true;

            var card = UIFactory.Deco(overlay, "Card", T.panel);
            card.rectTransform.Anchor(0.17f, 0.1f, 0.83f, 0.9f);
            var t = UIFactory.Label(card.transform, title, T.hero, T.goldBright, TextAnchor.MiddleCenter, true);
            t.rectTransform.Anchor(0.03f, 0.74f, 0.97f, 0.95f);
            var rule = UIFactory.Panel(card.transform, "Rule", T.gold, DecoSprites.Diamond);
            rule.rectTransform.Anchor(0.48f, 0.715f, 0.52f, 0.745f);
            rule.preserveAspect = true;
            var b = UIFactory.Label(card.transform, body, T.body + 2, T.text, TextAnchor.MiddleCenter);
            b.rectTransform.Anchor(0.08f, 0.53f, 0.92f, 0.71f);
            UITween.Pop(card.transform, 0f, 0.35f);
            return card.transform;
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
            float now = Time.unscaledTime;
            if (overlaySun != null && overlay.gameObject.activeSelf)
                overlaySun.rectTransform.localRotation = Quaternion.Euler(0f, 0f, now * 6f);
            foreach (var c in cards)
                if (c.Callout.gameObject.activeSelf)
                    c.Callout.anchoredPosition = new Vector2(0f, 8f + Mathf.Abs(Mathf.Sin(now * 4f)) * 6f);
            if (teamReady)
                teamText.color = Color.Lerp(T.party, Color.white, Mathf.PingPong(now * 2f, 1f) * 0.6f);
            else teamText.color = T.party;

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
