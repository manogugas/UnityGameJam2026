using System;
using System.Collections;
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

        readonly Text[] faces = new Text[2];
        readonly Image[] dice = new Image[2];
        Button rollButton;

        protected override void BuildUI(RectTransform root)
        {
            for (int i = 0; i < 2; i++)
            {
                dice[i] = UIFactory.Panel(root, "Die" + i, new Color(0.95f, 0.93f, 0.88f));
                float x = i == 0 ? 0.27f : 0.53f;
                dice[i].rectTransform.Anchor(x, 0.38f, x + 0.2f, 0.8f, 6);
                faces[i] = UIFactory.Label(dice[i].transform, "", 72, new Color(0.1f, 0.05f, 0.1f));
                faces[i].fontStyle = FontStyle.Bold;
                faces[i].GetComponent<Shadow>().enabled = false;
            }
            rollButton = UIFactory.Button(root, "STOP DICE  [Space]", new Color(0.1f, 0.35f, 0.5f), RequestPrimary);
            ((RectTransform)rollButton.transform).Anchor(0.3f, 0.21f, 0.7f, 0.33f);
        }

        protected override IEnumerator Run(GambleContext ctx, Action<GambleOutcome> done)
        {
            rollButton.gameObject.SetActive(!ctx.IsAI);
            int a = 1, b = 1;
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
                }
                if (ConsumePrimary(ctx) || timer >= autoStopTime || (ctx.IsAI && timer >= aiStop)) break;
                yield return null;
            }
            rollButton.gameObject.SetActive(false);
            a = Random.Range(1, 7);
            b = Random.Range(1, 7);
            Show(a, b, 0f);
            done(Score(a, b));
        }

        void Show(int a, int b, float tilt)
        {
            faces[0].text = a.ToString();
            faces[1].text = b.ToString();
            dice[0].rectTransform.localRotation = Quaternion.Euler(0, 0, tilt);
            dice[1].rectTransform.localRotation = Quaternion.Euler(0, 0, -tilt);
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
