using System;
using System.Collections;
using HauntedCasino.UI;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace HauntedCasino.Combat
{
    /// <summary>Fastest game: two tumbling dice. Sum = power, doubles x2, snake eyes bust.</summary>
    public class DiceGame : GambleGame
    {
        [SerializeField] float tumbleStep = 0.06f;
        [SerializeField] float autoStopTime = 1.6f;
        [SerializeField] int doublesMultiplier = 2;

        public override string DisplayName => "Dice";
        public override string Blurb => "Quick and steady. Doubles x2, snake eyes bust.";
        public override Color Accent => new Color(0.5f, 0.85f, 1f);

        static readonly int[][] PipLayouts =
        {
            new[] { 4 }, new[] { 0, 8 }, new[] { 0, 4, 8 }, new[] { 0, 2, 6, 8 }, new[] { 0, 2, 4, 6, 8 }, new[] { 0, 2, 3, 5, 6, 8 }
        };

        readonly Image[][] pips = new Image[2][];
        readonly RectTransform[] dice = new RectTransform[2];
        Button rollButton;

        protected override void BuildUI(RectTransform root)
        {
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? 0.25f : 0.55f;
                var cell = UIFactory.Rect(root, "DieCell" + i).Anchor(x, 0.36f, x + 0.2f, 0.77f);
                var slot = UIFactory.Rect(cell, "Die" + i);
                slot.sizeDelta = new Vector2(150f, 150f);
                dice[i] = slot;
                UIFactory.Panel(slot, "Face", new Color(0.96f, 0.93f, 0.86f), DecoSprites.PillFill, 1.2f);
                UIFactory.Panel(slot, "Border", Theme.gold, DecoSprites.PillBorder, 1.2f);
                pips[i] = new Image[9];
                for (int p = 0; p < 9; p++)
                {
                    int col = p % 3, row = p / 3;
                    var pip = UIFactory.Panel(slot, "Pip" + p, new Color(0.45f, 0.04f, 0.08f), DecoSprites.ChipBase);
                    float cx = 0.24f + col * 0.26f, cy = 0.76f - row * 0.26f;
                    pip.rectTransform.Anchor(cx - 0.09f, cy - 0.09f, cx + 0.09f, cy + 0.09f);
                    pips[i][p] = pip;
                }
            }
            rollButton = UIFactory.Button(root, $"STOP DICE  {KeyCap("Space")}", new Color(0.08f, 0.22f, 0.32f), RequestPrimary, Theme.body, true);
            ((RectTransform)rollButton.transform).Anchor(0.25f, 0.22f, 0.75f, 0.33f);
        }

        protected override IEnumerator Run(GambleContext ctx, Action<GambleOutcome> done)
        {
            rollButton.gameObject.SetActive(!ctx.IsAI);
            int a = Random.Range(1, 7), b = Random.Range(1, 7);
            SetFace(0, a);
            SetFace(1, b);
            float timer = 0f, stepTimer = 0f;
            float aiStop = Random.Range(0.5f, 1f);
            while (true)
            {
                timer += Time.deltaTime;
                stepTimer += Time.deltaTime;
                if (stepTimer >= tumbleStep)
                {
                    stepTimer = 0f;
                    a = Random.Range(1, 7);
                    b = Random.Range(1, 7);
                    Show(a, b, Random.Range(-15f, 15f));
                    CasinoSfx.Tick(Random.Range(0.6f, 0.9f), 0.3f);
                }
                if (ConsumePrimary(ctx) || timer >= autoStopTime || (ctx.IsAI && timer >= aiStop)) break;
                yield return null;
            }
            rollButton.gameObject.SetActive(false);
            a = Random.Range(1, 7);
            b = Random.Range(1, 7);
            Show(a, b, 0f);
            CasinoSfx.Play(Sfx.Chip, 0.75f);
            done(Score(a, b));
        }

        void Show(int a, int b, float tilt)
        {
            SetFace(0, a);
            SetFace(1, b);
            dice[0].localRotation = Quaternion.Euler(0, 0, tilt);
            dice[1].localRotation = Quaternion.Euler(0, 0, -tilt);
            if (tilt == 0f)
            {
                UITween.Punch(dice[0], 0.15f);
                UITween.Punch(dice[1], 0.15f);
            }
        }

        void SetFace(int die, int value)
        {
            var on = PipLayouts[value - 1];
            for (int p = 0; p < 9; p++) pips[die][p].enabled = Array.IndexOf(on, p) >= 0;
        }

        GambleOutcome Score(int a, int b)
        {
            if (a == 1 && b == 1) return new GambleOutcome(0, OutcomeKind.Bust, "SNAKE EYES — BUST!");
            int sum = a + b;
            if (a == b)
            {
                int power = sum * doublesMultiplier;
                return new GambleOutcome(power, OutcomeKind.Jackpot, $"DOUBLE {a}s! {sum} x{doublesMultiplier}  → {power}");
            }
            return new GambleOutcome(sum, OutcomeKind.Normal, $"{a} + {b}  → {sum}");
        }
    }
}
