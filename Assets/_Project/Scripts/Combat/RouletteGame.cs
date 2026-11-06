using System;
using System.Collections;
using System.Collections.Generic;
using HauntedCasino.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace HauntedCasino.Combat
{
    /// <summary>Pick a bet (safe colour, riskier dozen, or a lucky number), then a quick spin. Green 0 busts.</summary>
    public class RouletteGame : GambleGame
    {
        enum Bet { None, Red, Black, Dozen1, Dozen2, Dozen3, Lucky }

        static readonly int[] WheelOrder =
        {
            0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10,
            5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26
        };
        static readonly HashSet<int> Reds = new HashSet<int>
            { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };

        static readonly Color RedCell = new Color(0.75f, 0.1f, 0.12f);
        static readonly Color BlackCell = new Color(0.1f, 0.1f, 0.12f);
        static readonly Color GreenCell = new Color(0.05f, 0.5f, 0.2f);

        [SerializeField] int colourPower = 12;
        [SerializeField] int dozenPower = 20;
        [SerializeField] int luckyPower = 50;
        [SerializeField] int minSpinSteps = 28;
        [SerializeField] int maxSpinSteps = 40;

        public override string DisplayName => "Roulette";
        public override string Blurb => "Choose your risk: colour, dozen, or one lucky number.";
        public override Color Accent => new Color(1f, 0.4f, 0.4f);

        const int TickerCells = 7;
        readonly TextMeshProUGUI[] cellText = new TextMeshProUGUI[TickerCells];
        readonly Image[] cellBg = new Image[TickerCells];
        RectTransform betRow;
        TextMeshProUGUI luckyText;
        int luckyNumber = 7;
        Bet chosen;

        protected override void BuildUI(RectTransform root)
        {
            var ticker = UIFactory.Rect(root, "Wheel").Anchor(0.05f, 0.5f, 0.95f, 0.77f);
            for (int i = 0; i < TickerCells; i++)
            {
                bool centre = i == TickerCells / 2;
                var slot = UIFactory.Rect(ticker, "Slot" + i)
                    .Anchor((float)i / TickerCells, centre ? 0f : 0.14f, (float)(i + 1) / TickerCells, centre ? 1f : 0.86f, 3);
                cellBg[i] = UIFactory.Panel(slot, "Cell", BlackCell, DecoSprites.FrameFill, 3f);
                if (centre) UIFactory.Panel(slot, "Border", Theme.goldBright, DecoSprites.FrameBorder, 2f);
                cellText[i] = UIFactory.Label(slot, "", centre ? Theme.title + 6 : Theme.body + 2, Color.white, TextAnchor.MiddleCenter, true);
            }

            // Two rows of bets so they fit the side panel.
            betRow = UIFactory.Rect(root, "Bets").Anchor(0.03f, 0.2f, 0.97f, 0.48f);
            var top = UIFactory.Rect(betRow, "Outside").Anchor(0f, 0.52f, 1f, 1f);
            UIFactory.Row(top, 6);
            int s = Theme.small - 2;
            UIFactory.Button(top, $"RED {KeyCap("1")}\n<size=75%>x{colourPower}</size>", RedCell, () => chosen = Bet.Red, s, true);
            UIFactory.Button(top, $"BLACK {KeyCap("2")}\n<size=75%>x{colourPower}</size>", new Color(0.18f, 0.17f, 0.2f), () => chosen = Bet.Black, s, true);
            UIFactory.Button(top, $"1-12 {KeyCap("3")}\n<size=75%>x{dozenPower}</size>", new Color(0.12f, 0.22f, 0.4f), () => chosen = Bet.Dozen1, s, true);
            UIFactory.Button(top, $"13-24 {KeyCap("4")}\n<size=75%>x{dozenPower}</size>", new Color(0.12f, 0.22f, 0.4f), () => chosen = Bet.Dozen2, s, true);
            UIFactory.Button(top, $"25-36 {KeyCap("5")}\n<size=75%>x{dozenPower}</size>", new Color(0.12f, 0.22f, 0.4f), () => chosen = Bet.Dozen3, s, true);
            var bottom = UIFactory.Rect(betRow, "Lucky").Anchor(0.12f, 0f, 0.88f, 0.48f);
            var lucky = UIFactory.Button(bottom, "", Color.Lerp(Theme.burgundy, Theme.gold, 0.35f), () => chosen = Bet.Lucky, s, true);
            ((RectTransform)lucky.transform).Anchor(0.16f, 0f, 0.84f, 1f, 2);
            luckyText = lucky.GetComponentInChildren<TextMeshProUGUI>();
            var minus = UIFactory.Button(bottom, "<", Theme.panelRaised, () => ChangeLucky(-1), Theme.body, true);
            ((RectTransform)minus.transform).Anchor(0f, 0.1f, 0.14f, 0.9f);
            var plus = UIFactory.Button(bottom, ">", Theme.panelRaised, () => ChangeLucky(1), Theme.body, true);
            ((RectTransform)plus.transform).Anchor(0.86f, 0.1f, 1f, 0.9f);
            UpdateLuckyLabel();
        }

        void ChangeLucky(int delta)
        {
            luckyNumber = (luckyNumber - 1 + delta + 36) % 36 + 1; // cycles 1..36
            UpdateLuckyLabel();
        }

        void UpdateLuckyLabel() =>
            luckyText.text = $"LUCKY #{luckyNumber} {KeyCap("6")}\n<size=75%>x{luckyPower}   ·   {KeyCap("←")}{KeyCap("→")} to change</size>";

        protected override IEnumerator Run(GambleContext ctx, Action<GambleOutcome> done)
        {
            int index = Random.Range(0, WheelOrder.Length);
            ShowTicker(index);
            chosen = Bet.None;
            betRow.gameObject.SetActive(!ctx.IsAI);
            ResultText.text = ctx.IsAI ? "The dead place their bet..." : "Place your bet!";

            if (ctx.IsAI)
            {
                yield return new WaitForSeconds(0.6f);
                float roll = Random.value;
                if (roll < 0.5f) chosen = Random.value < 0.5f ? Bet.Red : Bet.Black;
                else if (roll < 0.85f) chosen = Bet.Dozen1 + Random.Range(0, 3);
                else
                {
                    chosen = Bet.Lucky;
                    luckyNumber = Random.Range(1, 37);
                    UpdateLuckyLabel();
                }
            }
            else
            {
                while (chosen == Bet.None)
                {
                    if (KeyDown(Key.Digit1) || KeyDown(Key.Numpad1)) chosen = Bet.Red;
                    else if (KeyDown(Key.Digit2) || KeyDown(Key.Numpad2)) chosen = Bet.Black;
                    else if (KeyDown(Key.Digit3) || KeyDown(Key.Numpad3)) chosen = Bet.Dozen1;
                    else if (KeyDown(Key.Digit4) || KeyDown(Key.Numpad4)) chosen = Bet.Dozen2;
                    else if (KeyDown(Key.Digit5) || KeyDown(Key.Numpad5)) chosen = Bet.Dozen3;
                    else if (KeyDown(Key.Digit6) || KeyDown(Key.Numpad6)) chosen = Bet.Lucky;
                    if (KeyDown(Key.LeftArrow)) ChangeLucky(-1);
                    if (KeyDown(Key.RightArrow)) ChangeLucky(1);
                    yield return null;
                }
            }

            betRow.gameObject.SetActive(false);
            ResultText.text = $"Bet: {BetName(chosen)}";
            CasinoSfx.Play(Sfx.Chip, 1.1f);

            int steps = Random.Range(minSpinSteps, maxSpinSteps + 1);
            for (int s = 0; s < steps; s++)
            {
                index = (index + 1) % WheelOrder.Length;
                ShowTicker(index);
                float t = (float)s / steps;
                if (s % 2 == 0 || t > 0.5f) CasinoSfx.Tick(1.2f - t * 0.4f, 0.25f + t * 0.3f);
                yield return new WaitForSeconds(Mathf.Lerp(0.015f, 0.2f, t * t));
            }
            CasinoSfx.Play(Sfx.Chip, 0.7f);

            done(Evaluate(WheelOrder[index]));
        }

        GambleOutcome Evaluate(int n)
        {
            string landed = n == 0 ? "0 GREEN" : $"{n} {(Reds.Contains(n) ? "RED" : "BLACK")}";
            if (n == 0) return new GambleOutcome(0, OutcomeKind.Bust, $"{landed} — THE HOUSE TAKES ALL. BUST!");

            bool win;
            int power;
            switch (chosen)
            {
                case Bet.Red: win = Reds.Contains(n); power = colourPower; break;
                case Bet.Black: win = !Reds.Contains(n); power = colourPower; break;
                case Bet.Dozen1: win = n <= 12; power = dozenPower; break;
                case Bet.Dozen2: win = n >= 13 && n <= 24; power = dozenPower; break;
                case Bet.Dozen3: win = n >= 25; power = dozenPower; break;
                default: win = n == luckyNumber; power = luckyPower; break;
            }

            if (!win) return new GambleOutcome(0, OutcomeKind.Normal, $"{landed} — bet on {BetName(chosen)} lost  → 0");
            var kind = chosen == Bet.Lucky ? OutcomeKind.Jackpot : OutcomeKind.Normal;
            string prefix = kind == OutcomeKind.Jackpot ? "THE LUCKY NUMBER!" : "Winner!";
            return new GambleOutcome(power, kind, $"{landed} — {prefix}  → {power}");
        }

        string BetName(Bet bet)
        {
            switch (bet)
            {
                case Bet.Red: return "RED";
                case Bet.Black: return "BLACK";
                case Bet.Dozen1: return "1-12";
                case Bet.Dozen2: return "13-24";
                case Bet.Dozen3: return "25-36";
                case Bet.Lucky: return $"LUCKY #{luckyNumber}";
                default: return "-";
            }
        }

        void ShowTicker(int centreIndex)
        {
            int half = TickerCells / 2;
            for (int i = 0; i < TickerCells; i++)
            {
                int n = WheelOrder[(centreIndex + i - half + WheelOrder.Length * 2) % WheelOrder.Length];
                cellText[i].text = n.ToString();
                cellBg[i].color = n == 0 ? GreenCell : Reds.Contains(n) ? RedCell : BlackCell;
            }
        }
    }
}
