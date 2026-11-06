using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace HauntedCasino.Combat
{
    /// <summary>Three fast reels; stop each with Space. Triples hit hard, skulls hurt, 3 skulls bust.</summary>
    public class SlotsGame : GambleGame
    {
        const int Seven = 0, Bar = 1, Bell = 2, Cherry = 3, Skull = 4;
        static readonly string[] Names = { "7", "BAR", "BELL", "CHERRY", "SKULL" };
        static readonly Color[] Colors =
        {
            new Color(1f, 0.25f, 0.25f), new Color(0.95f, 0.95f, 0.95f), new Color(1f, 0.85f, 0.2f),
            new Color(1f, 0.45f, 0.75f), new Color(0.55f, 0.6f, 0.7f)
        };
        // Reel strip composition: rarer symbols appear less.
        static readonly int[] StripTemplate = { Seven, Bar, Bar, Bell, Bell, Bell, Cherry, Cherry, Cherry, Skull, Skull };

        [SerializeField] float stepTime = 0.09f;
        [SerializeField] float autoStopTime = 2.5f;
        [SerializeField] int jackpotPower = 30;
        [SerializeField] int tripleBarPower = 18;
        [SerializeField] int tripleBellPower = 15;
        [SerializeField] int tripleCherryPower = 12;
        [SerializeField] int pairSevenPower = 10;
        [SerializeField] int pairPower = 6;
        [SerializeField] int noMatchPower = 3;
        [SerializeField] int skullPenalty = 2;

        public override string DisplayName => "Slots";
        public override string Blurb => "Stop 3 reels with timing. Match symbols, dodge skulls.";
        public override Color Accent => Gold;

        readonly Text[] above = new Text[3], mid = new Text[3], below = new Text[3];
        readonly Image[] windows = new Image[3];
        readonly int[][] strips = new int[3][];
        readonly int[] pos = new int[3];
        Button stopButton;

        static readonly Color ReelIdle = new Color(0.12f, 0.08f, 0.12f);
        static readonly Color ReelStopped = new Color(0.3f, 0.2f, 0.05f);

        protected override void BuildUI(RectTransform root)
        {
            var reels = UIFactory.Rect(root, "Reels").Anchor(0.12f, 0.36f, 0.88f, 0.83f);
            for (int i = 0; i < 3; i++)
            {
                windows[i] = UIFactory.Panel(reels, "Reel" + i, ReelIdle);
                windows[i].rectTransform.Anchor(i / 3f, 0, (i + 1) / 3f, 1, 8);
                above[i] = UIFactory.Label(windows[i].transform, "", 22, Color.white);
                above[i].rectTransform.Anchor(0, 0.7f, 1, 1);
                mid[i] = UIFactory.Label(windows[i].transform, "", 46, Color.white);
                mid[i].fontStyle = FontStyle.Bold;
                mid[i].rectTransform.Anchor(0, 0.3f, 1, 0.7f);
                below[i] = UIFactory.Label(windows[i].transform, "", 22, Color.white);
                below[i].rectTransform.Anchor(0, 0, 1, 0.3f);
            }
            stopButton = UIFactory.Button(root, "STOP REEL  [Space]", new Color(0.55f, 0.35f, 0.05f), RequestPrimary);
            ((RectTransform)stopButton.transform).Anchor(0.3f, 0.21f, 0.7f, 0.33f);
        }

        protected override IEnumerator Run(GambleContext ctx, Action<GambleOutcome> done)
        {
            stopButton.gameObject.SetActive(!ctx.IsAI);
            for (int i = 0; i < 3; i++)
            {
                strips[i] = Shuffled(StripTemplate);
                pos[i] = Random.Range(0, strips[i].Length);
                windows[i].color = ReelIdle;
                Refresh(i);
            }

            int next = 0;
            float stepTimer = 0f, reelTimer = 0f;
            float aiDelay = Random.Range(0.35f, 0.8f);
            while (next < 3)
            {
                float dt = Time.deltaTime;
                stepTimer += dt;
                reelTimer += dt;
                while (stepTimer >= stepTime)
                {
                    stepTimer -= stepTime;
                    for (int i = next; i < 3; i++)
                    {
                        pos[i] = (pos[i] + 1) % strips[i].Length;
                        Refresh(i);
                    }
                }

                bool stop = ConsumePrimary(ctx) || reelTimer >= autoStopTime || (ctx.IsAI && reelTimer >= aiDelay);
                if (stop)
                {
                    windows[next].color = ReelStopped;
                    next++;
                    reelTimer = 0f;
                    aiDelay = Random.Range(0.35f, 0.8f);
                }
                yield return null;
            }
            stopButton.gameObject.SetActive(false);
            done(Score(Current(0), Current(1), Current(2)));
        }

        int Current(int reel) => strips[reel][pos[reel]];

        void Refresh(int reel)
        {
            int len = strips[reel].Length;
            SetSymbol(above[reel], strips[reel][(pos[reel] + 1) % len], 0.35f);
            SetSymbol(mid[reel], strips[reel][pos[reel]], 1f);
            SetSymbol(below[reel], strips[reel][(pos[reel] - 1 + len) % len], 0.35f);
        }

        static void SetSymbol(Text t, int symbol, float alpha)
        {
            t.text = Names[symbol];
            var c = Colors[symbol];
            c.a = alpha;
            t.color = c;
        }

        GambleOutcome Score(int a, int b, int c)
        {
            int skulls = (a == Skull ? 1 : 0) + (b == Skull ? 1 : 0) + (c == Skull ? 1 : 0);
            string line = $"{Names[a]} · {Names[b]} · {Names[c]}";
            if (skulls == 3)
                return new GambleOutcome(0, OutcomeKind.Bust, $"{line}\nTHREE SKULLS — BUST!");

            if (a == b && b == c)
            {
                if (a == Seven) return new GambleOutcome(jackpotPower, OutcomeKind.Jackpot, $"{line}\nJACKPOT 777!  → {jackpotPower}");
                int triple = a == Bar ? tripleBarPower : a == Bell ? tripleBellPower : tripleCherryPower;
                return new GambleOutcome(triple, OutcomeKind.Normal, $"{line}\nTRIPLE {Names[a]}!  → {triple}");
            }

            int pairSymbol = -1;
            if (a == b && a != Skull) pairSymbol = a;
            else if (a == c && a != Skull) pairSymbol = a;
            else if (b == c && b != Skull) pairSymbol = b;

            int power;
            string desc;
            if (pairSymbol >= 0)
            {
                power = pairSymbol == Seven ? pairSevenPower : pairPower;
                desc = $"Pair of {Names[pairSymbol]}";
            }
            else
            {
                power = noMatchPower;
                desc = "No match";
            }
            if (skulls > 0)
            {
                power = Mathf.Max(0, power - skulls * skullPenalty);
                desc += $" (-{skulls * skullPenalty} skull)";
            }
            return new GambleOutcome(power, OutcomeKind.Normal, $"{line}\n{desc}  → {power}");
        }

        static int[] Shuffled(int[] source)
        {
            var copy = (int[])source.Clone();
            for (int i = copy.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (copy[i], copy[j]) = (copy[j], copy[i]);
            }
            return copy;
        }
    }
}
