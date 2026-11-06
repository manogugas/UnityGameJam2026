using System;
using System.Collections;
using System.Collections.Generic;
using HauntedCasino.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace HauntedCasino.Combat
{
    /// <summary>
    /// Team move unlocked by the team meter. Every standing player owns a reel and stops it with
    /// their own number key. The more reels match, the bigger the hit; all matching is a TEAM JACKPOT.
    /// </summary>
    public class PartySlotsGame : GambleGame
    {
        const int MaxReels = 4;

        [SerializeField] float stepTime = 0.1f;
        [SerializeField] float autoStopTime = 5f;
        [SerializeField] int allMatchPerReel = 15;
        [SerializeField] int allSevensPerReel = 25;
        [SerializeField] int nearMatchPerReel = 8;
        [SerializeField] int pairPerReel = 4;
        [SerializeField] int nothingPerReel = 2;
        [SerializeField] int skullPenalty = 3;

        public override string DisplayName => "Party Slots";
        public override string Blurb => "TEAM MOVE: everyone stops their own reel!";
        public override Color Accent => new Color(1f, 0.55f, 0.95f);

        readonly Image[] windows = new Image[MaxReels];
        readonly TextMeshProUGUI[] mid = new TextMeshProUGUI[MaxReels], ownerLabel = new TextMeshProUGUI[MaxReels];
        readonly Button[] stopButtons = new Button[MaxReels];
        readonly int[][] strips = new int[MaxReels][];
        readonly int[] pos = new int[MaxReels];
        readonly bool[] stopped = new bool[MaxReels];
        readonly Combatant[] reelOwner = new Combatant[MaxReels];
        List<Combatant> owners = new List<Combatant>();
        int reelCount;

        /// <summary>Players taking part, before Play is called.</summary>
        public void SetOwners(List<Combatant> standingPlayers) => owners = standingPlayers;

        protected override void BuildUI(RectTransform root)
        {
            var reels = UIFactory.Rect(root, "Reels").Anchor(0.03f, 0.22f, 0.97f, 0.79f);
            for (int i = 0; i < MaxReels; i++)
            {
                windows[i] = ReelWindow(reels, "Reel" + i);
                ownerLabel[i] = UIFactory.Label(windows[i].transform, "", Theme.small - 2, Color.white, TextAnchor.MiddleCenter, true);
                ownerLabel[i].rectTransform.Anchor(0, 0.8f, 1, 0.97f, 4);
                mid[i] = UIFactory.Label(windows[i].transform, "", Theme.heading + 2, Color.white, TextAnchor.MiddleCenter, true);
                mid[i].rectTransform.Anchor(0, 0.32f, 1, 0.8f);
                int idx = i;
                stopButtons[i] = UIFactory.Button(windows[i].transform, "STOP", Color.Lerp(Theme.burgundy, Theme.party, 0.3f), () => StopReel(idx), Theme.small, true);
                ((RectTransform)stopButtons[i].transform).Anchor(0.08f, 0.04f, 0.92f, 0.28f);
            }
        }

        protected override IEnumerator Run(GambleContext ctx, Action<GambleOutcome> done)
        {
            reelCount = Mathf.Clamp(Mathf.Max(3, owners.Count), 3, MaxReels);
            for (int i = 0; i < MaxReels; i++)
            {
                bool used = i < reelCount;
                windows[i].gameObject.SetActive(used);
                if (!used) continue;
                windows[i].rectTransform.Anchor((float)i / reelCount, 0f, (float)(i + 1) / reelCount, 1f, 6);
                var owner = owners.Count > 0 ? owners[i % owners.Count] : null;
                reelOwner[i] = owner;
                ownerLabel[i].text = owner != null ? $"{owner.Name.ToUpper()}  {KeyCap((owner.Index + 1).ToString())}" : "";
                ownerLabel[i].color = owner != null ? owner.Color : Color.white;
                strips[i] = SlotsGame.Shuffled(SlotsGame.StripTemplate);
                pos[i] = Random.Range(0, strips[i].Length);
                stopped[i] = false;
                windows[i].color = SlotsGame.ReelIdle;
                stopButtons[i].gameObject.SetActive(true);
                SlotsGame.SetSymbol(mid[i], strips[i][pos[i]], 1f);
            }
            ResultText.text = "Everyone: press YOUR number to stop YOUR reel!";
            ResultText.fontSize = Theme.body;

            float timer = 0f, stepTimer = 0f;
            while (!AllStopped())
            {
                timer += Time.deltaTime;
                stepTimer += Time.deltaTime;
                while (stepTimer >= stepTime)
                {
                    stepTimer -= stepTime;
                    for (int i = 0; i < reelCount; i++)
                    {
                        if (stopped[i]) continue;
                        pos[i] = (pos[i] + 1) % strips[i].Length;
                        SlotsGame.SetSymbol(mid[i], strips[i][pos[i]], 1f);
                    }
                    CasinoSfx.Tick(1.3f);
                }

                foreach (var owner in owners)
                {
                    if (!NumberKeyDown(owner.Index)) continue;
                    for (int i = 0; i < reelCount; i++)
                    {
                        if (stopped[i] || reelOwner[i] != owner) continue;
                        StopReel(i);
                        break;
                    }
                }
                for (int i = 0; i < reelCount; i++)
                    if (!stopped[i] && timer >= autoStopTime + i * 0.4f) StopReel(i);
                yield return null;
            }
            done(Score());
        }

        void StopReel(int i)
        {
            if (i >= reelCount || stopped[i]) return;
            stopped[i] = true;
            windows[i].color = SlotsGame.ReelStopped;
            stopButtons[i].gameObject.SetActive(false);
            CasinoSfx.Play(Sfx.Chip, 0.8f + i * 0.12f);
        }

        bool AllStopped()
        {
            for (int i = 0; i < reelCount; i++)
                if (!stopped[i]) return false;
            return true;
        }

        GambleOutcome Score()
        {
            var counts = new int[SlotsGame.Names.Length];
            var names = new List<string>();
            for (int i = 0; i < reelCount; i++)
            {
                int s = strips[i][pos[i]];
                counts[s]++;
                names.Add(SlotsGame.Names[s]);
            }
            int skulls = counts[SlotsGame.Skull];
            int best = 0, bestSymbol = -1;
            for (int s = 0; s < counts.Length; s++)
            {
                if (s == SlotsGame.Skull || counts[s] <= best) continue;
                best = counts[s];
                bestSymbol = s;
            }

            string line = string.Join(" · ", names);
            if (best == reelCount)
            {
                int perReel = bestSymbol == SlotsGame.Seven ? allSevensPerReel : allMatchPerReel;
                int jackpot = perReel * reelCount;
                return new GambleOutcome(jackpot, OutcomeKind.Jackpot, $"{line}\nTEAM JACKPOT!  → {jackpot}");
            }

            int power;
            string desc;
            if (best == reelCount - 1 && best >= 2)
            {
                power = nearMatchPerReel * reelCount;
                desc = $"{best} {SlotsGame.Names[bestSymbol]}s!";
            }
            else if (best >= 2)
            {
                power = pairPerReel * reelCount;
                desc = $"Pair of {SlotsGame.Names[bestSymbol]}";
            }
            else
            {
                power = nothingPerReel * reelCount;
                desc = "No match";
            }
            if (skulls > 0)
            {
                power = Mathf.Max(0, power - skulls * skullPenalty);
                desc += $" (-{skulls * skullPenalty} skull)";
            }
            return new GambleOutcome(power, OutcomeKind.Normal, $"{line}\n{desc}  → {power}");
        }
    }
}
