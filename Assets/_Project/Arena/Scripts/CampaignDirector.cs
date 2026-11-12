using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// The campaign loop: start screen -> pick your number -> garage (tablet: repair, upgrades, next match)
    /// -> drive out through the gate -> match -> summary (money earned) -> back to the garage.
    /// Losing a match resets the run to the snapshot taken when you drove out, and you try again.
    /// Before each match the emperor marks one participant's number (you or an enemy): every car hunts the
    /// marked one. Wrecking a marked enemy pays triple; if you're marked and survive, the match pays double.
    /// Enemy numbers are bounties: higher = tougher, pays more. Emperor's gifts picked up in the match add cash.
    /// Each event can be run as a RACE (3 laps of the figure-8 from a grid start; win = 1st place, 2nd/3rd pay
    /// part of the purse and you go again) or an ARENA fight (last car standing). Wrecking every rival wins
    /// either way; being wrecked resets the run to the snapshot taken when you drove out.
    /// </summary>
    public class CampaignDirector : MonoBehaviour
    {
        public enum Phase { Start, PickNumber, Garage, DrivingOut, Match, Result }

        public CampaignSettings settings;
        public CarController player;
        public ArenaDirector arena;
        public IntroCinematic intro;
        public TabletUI tablet;
        public ArenaObjectives objectives;
        public GarageCamera garageCamera;
        public MatchSummaryUI overlay;
        public ArenaGate playerGate;
        public ArenaCrowd crowd;
        public RaceDirector race;
        [Tooltip("Share of the win reward paid for 2nd, 3rd... place in a race.")]
        public float[] placePurse = { 0.5f, 0.25f };

        public Phase CurrentPhase { get; private set; } = Phase.Start;
        public RunState Run => run;
        public RunState SavedRun { get; private set; }
        public MatchDefinition NextMatch { get; private set; }
        public IReadOnlyList<EnemySpec> NextEnemies => nextEnemies;
        public bool PlayerMarked => run != null && run.HasNumber && run.markedNumber == run.carNumber;
        public float PlayerHealth => health != null ? health.Health : 0f;
        public float PlayerMaxHealth => health != null ? health.maxHealth : 0f;
        /// <summary>Arena fights unlock once the race championship (the ladder's final race) has been won.</summary>
        public bool ArenaUnlocked => run != null && run.championBeaten;

        /// <summary>The event in progress (or about to start) is a race rather than an arena fight.</summary>
        public bool RaceMode { get; private set; }

        RunState run, snapshot;
        readonly List<EnemySpec> nextEnemies = new List<EnemySpec>();
        readonly List<(string name, int reward)> matchKills = new List<(string, int)>();
        int matchGifts, giftCount;
        bool sessionIntroPlayed;

        CarHealth health;
        CarUpgrades upgrades;
        CarNumberPlates plates;
        CarInput input;
        CarDamage damage;
        CarHud hud;
        CombatHud combatHud;
        Rigidbody body;

        void Awake()
        {
            health = player.GetComponent<CarHealth>();
            upgrades = player.GetComponent<CarUpgrades>();
            plates = player.GetComponent<CarNumberPlates>();
            input = player.GetComponent<CarInput>();
            damage = player.GetComponent<CarDamage>();
            hud = player.GetComponent<CarHud>();
            combatHud = player.GetComponent<CombatHud>();
            body = player.GetComponent<Rigidbody>();
            if (intro != null) intro.autoPlay = false;
            if (arena != null) arena.waitForIntro = true;
        }

        void Start()
        {
            if (damage != null) damage.allowManualRepair = false;
            if (hud != null) hud.wreckedHint = "";
            health.Died += OnPlayerDied;
            arena.EnemyKilled += OnEnemyKilled;
            arena.MatchCleared += OnMatchCleared;
            if (objectives == null) objectives = FindAnyObjectByType<ArenaObjectives>();
            if (objectives != null) objectives.GiftCollected += OnGiftCollected;
            if (race == null) race = FindAnyObjectByType<RaceDirector>();
            if (race != null) race.PlayerFinished += OnRaceFinished;

            ParkInGarage();
            EnterTabletMode();
            SavedRun = RunState.Load();
            if (SavedRun != null && SavedRun.HasNumber)
            {
                CurrentPhase = Phase.Start;
                ApplyRunToCar(SavedRun);
                tablet.ShowStart();
            }
            else NewRun();
        }

        // ------------------------------------------------------------------ tablet actions

        public void NewRun()
        {
            RunState.Delete();
            SavedRun = null;
            run = new RunState { money = settings.startingMoney };
            damage.RepairAll();
            ApplyRunToCar(run);
            CurrentPhase = Phase.PickNumber;
            tablet.ShowNumberPicker(Random.Range(0, 100));
        }

        public void ContinueRun()
        {
            run = SavedRun.Clone();
            ApplyRunToCar(run);
            EnterGarage();
        }

        public void PreviewNumber(int n) => plates.SetNumber(n);

        public void ConfirmNumber(int n)
        {
            run.carNumber = Mathf.Clamp(n, 0, 99);
            plates.SetNumber(run.carNumber);
            EnterGarage();
        }

        public int UpgradeCost(UpgradeType type)
        {
            var track = settings.Track(type);
            int level = run.Level(type);
            return track != null && level < track.MaxLevel ? track.costs[level] : -1;
        }

        public bool BuyUpgrade(UpgradeType type)
        {
            int cost = UpgradeCost(type);
            if (cost < 0 || run.money < cost) return false;
            run.money -= cost;
            run.upgrades[(int)type]++;
            ApplyUpgrades();
            run.health = health.Health;
            run.Save();
            tablet.Refresh();
            return true;
        }

        public int RepairCost(bool full)
        {
            float missing = health.maxHealth - health.Health;
            float amount = full ? missing : Mathf.Min(settings.patchAmount, missing);
            return Mathf.CeilToInt(amount * settings.repairCostPerHealth);
        }

        public bool Repair(bool full)
        {
            float missing = health.maxHealth - health.Health;
            if (missing < 0.5f) return false;
            int cost = RepairCost(full);
            if (run.money < cost) return false;
            run.money -= cost;
            if (full) damage.RepairAll(); // also straightens the panels and refits lost parts
            else health.SetHealth(health.Health + Mathf.Min(settings.patchAmount, missing));
            run.health = health.Health;
            run.Save();
            tablet.Refresh();
            return true;
        }

        public void StartRace() => StartMatch(true);

        /// <summary>Pause menu: give up the event. The run resets to before it (like being wrecked).</summary>
        public void Retire()
        {
            if (CurrentPhase != Phase.Match && CurrentPhase != Phase.DrivingOut) return;
            CurrentPhase = Phase.Result;
            StopAllCoroutines();
            StartCoroutine(BackToGarage(true));
        }

        public void StartMatch() => StartMatch(false);

        public void StartMatch(bool raceMode)
        {
            if (CurrentPhase != Phase.Garage) return;
            if (raceMode && race == null) raceMode = false;
            if (!raceMode && !ArenaUnlocked) return; // arena is the post-championship mode
            RaceMode = raceMode;
            snapshot = run.Clone();
            snapshot.health = health.Health;
            CurrentPhase = Phase.DrivingOut;
            tablet.Hide();
            garageCamera.SetActive(false);
            SetCursor(false);

            bool marked = PlayerMarked;
            health.marked = marked;
            plates.SetMarked(marked);

            if (raceMode) { StartCoroutine(RaceOut()); return; }
            bool full = !sessionIntroPlayed;
            sessionIntroPlayed = true;
            StartCoroutine(DriveOut(full));
        }

        /// <summary>Race: fade out of the garage straight onto the grid, then the countdown.</summary>
        IEnumerator RaceOut()
        {
            for (float t = 0f; !tablet.Stowed && t < 3f; t += Time.deltaTime) yield return null;
            yield return overlay.FadeTo(1f, 0.5f);
            ResetMatchTally();
            race.Setup(nextEnemies);
            if (hud != null) hud.enabled = true;
            if (combatHud != null) combatHud.enabled = true;
            if (crowd != null) crowd.SetCabin(true);
            CurrentPhase = Phase.Match;
            yield return new WaitForSeconds(0.2f);
            yield return overlay.FadeTo(0f, 0.5f);
            string who = PlayerMarked
                ? "YOU. Every car is coming for you  -  finish first for x" + settings.markedSurvivalMultiplier.ToString("0.#") + " pay"
                : $"{MarkedName()}  -  wreck it for x{settings.markedBountyMultiplier:0.#} bounty";
            overlay.Announce($"THE EMPEROR MARKS #{CarNumberPlates.Format(run.markedNumber)}", who, 3f);
            race.Begin();
        }

        void ResetMatchTally()
        {
            matchKills.Clear();
            matchGifts = 0;
            giftCount = 0;
        }

        void OnRaceFinished(int place, float time)
        {
            if (CurrentPhase != Phase.Match || !RaceMode || health.IsDead) return;
            CurrentPhase = Phase.Result;
            StartCoroutine(place == 1 ? Victory() : RacePlaced(place, time));
        }

        /// <summary>Finished but not first: part of the purse plus bounties, same race again (no reset).</summary>
        IEnumerator RacePlaced(int place, float time)
        {
            yield return new WaitForSeconds(1.5f);
            var lines = new List<(string, string)>();
            float share = place - 2 < placePurse.Length ? placePurse[place - 2] : 0f;
            int purse = Mathf.RoundToInt(settings.WinReward(run.matchIndex) * share);
            lines.Add(($"{RaceDirector.Place(place)} place  ({RaceDirector.FormatTime(time)})", $"${purse}"));
            int sum = purse;
            foreach (var (name, reward) in matchKills) { lines.Add(($"Kill: {name}", $"${reward}")); sum += reward; }
            if (matchGifts > 0) { lines.Add(("Emperor's gifts", $"${matchGifts}")); sum += matchGifts; }
            run.money += sum;
            run.kills += matchKills.Count;
            run.health = health.Health;
            run.Save();
            overlay.ShowVictory($"{RaceDirector.Place(place).ToUpper()} PLACE", "Only a win moves you up the ladder  -  race again",
                lines, $"${sum}", $"Bank: ${run.money}", () => StartCoroutine(BackToGarage(false)));
        }

        /// <summary>Let the workshop arm fold the tablet away before the car rolls (it's right in front).</summary>
        IEnumerator DriveOut(bool fullIntro)
        {
            for (float t = 0f; !tablet.Stowed && t < 3f; t += Time.deltaTime) yield return null;
            intro.Play(fullIntro, BeginMatch);
        }

        // ------------------------------------------------------------------ match

        void BeginMatch()
        {
            CurrentPhase = Phase.Match;
            ResetMatchTally();
            arena.StartMatch(nextEnemies);
            string who = PlayerMarked
                ? $"YOU. Every car is coming for you  -  survive for x{settings.markedSurvivalMultiplier:0.#} pay"
                : $"{MarkedName()}  -  wreck it for x{settings.markedBountyMultiplier:0.#} bounty";
            overlay.Announce($"THE EMPEROR MARKS #{CarNumberPlates.Format(run.markedNumber)}", who, 3.5f);
        }

        string MarkedName()
        {
            foreach (var e in nextEnemies) if (e.marked) return e.label;
            return "#" + CarNumberPlates.Format(run.markedNumber);
        }

        void OnGiftCollected(CarHealth collector)
        {
            if (CurrentPhase != Phase.Match || collector != health) return;
            int amount = Random.Range(settings.giftMoney.x, settings.giftMoney.y + 1);
            matchGifts += amount;
            giftCount++;
            overlay.Ticker($"+${amount}  Emperor's gift");
        }

        void OnEnemyKilled(CarHealth victim, CarHealth killer)
        {
            if (CurrentPhase != Phase.Match || killer != health) return;
            var plate = victim.GetComponent<CarNumberPlates>();
            int number = plate != null ? plate.Number : 0;
            int reward = settings.KillReward(number);
            if (victim.marked) reward = Mathf.RoundToInt(reward * settings.markedBountyMultiplier);
            string name = victim.marked ? victim.displayName + " (marked)" : victim.displayName;
            matchKills.Add((name, reward));
            overlay.Ticker($"+${reward}  {name} wrecked");
        }

        void OnMatchCleared()
        {
            if (CurrentPhase != Phase.Match || health.IsDead) return;
            CurrentPhase = Phase.Result;
            StartCoroutine(Victory());
        }

        IEnumerator Victory()
        {
            yield return new WaitForSeconds(1.5f);
            var lines = new List<(string, string)>();
            int win = settings.WinReward(run.matchIndex);
            bool wonRace = RaceMode && race != null && race.PlayerEntry != null && race.PlayerEntry.finished;
            string how = wonRace ? $"1st place ({RaceDirector.FormatTime(race.PlayerEntry.finishTime)})" : RaceMode ? "Last car running" : "Victory";
            lines.Add(($"{how}: {NextMatch.name}", $"${win}"));
            int sum = win;
            foreach (var (name, reward) in matchKills)
            {
                lines.Add(($"Kill: {name}", $"${reward}"));
                sum += reward;
            }
            if (matchGifts > 0)
            {
                lines.Add((giftCount > 1 ? $"Emperor's gifts x{giftCount}" : "Emperor's gift", $"${matchGifts}"));
                sum += matchGifts;
            }
            int total = sum;
            if (PlayerMarked)
            {
                total = Mathf.RoundToInt(sum * settings.markedSurvivalMultiplier);
                lines.Add(($"Survived the emperor's mark (x{settings.markedSurvivalMultiplier:0.#})", $"+${total - sum}"));
            }

            bool champion = NextMatch.championMatch;
            run.money += total;
            run.wins++;
            run.kills += matchKills.Count;
            if (champion) run.championBeaten = true;
            run.matchIndex++;
            run.markedNumber = -1;
            run.health = health.Health;
            run.Save();

            string title = champion ? "CHAMPION!" : "VICTORY";
            string subtitle = champion ? "The crowd chants your number. ARENA fights and endless mode unlocked." : NextMatch.name;
            overlay.ShowVictory(title, subtitle, lines, $"${total}", $"Bank: ${run.money}", () => StartCoroutine(BackToGarage(false)));
        }

        void OnPlayerDied(CarHealth victim, CarHealth killer)
        {
            if (CurrentPhase != Phase.Match) return;
            CurrentPhase = Phase.Result;
            StartCoroutine(Defeat(killer));
        }

        IEnumerator Defeat(CarHealth killer)
        {
            yield return new WaitForSeconds(2.5f);
            string by = killer != null ? $"Wrecked by {killer.displayName}" : "Wrecked";
            overlay.ShowDefeat("DEFEATED", by + "  -  the run resets to before this match", () => StartCoroutine(BackToGarage(true)));
        }

        IEnumerator BackToGarage(bool retry)
        {
            yield return overlay.FadeTo(1f, 0.6f);
            overlay.ClearAll();
            if (race != null) race.Stop();
            arena.ClearArena();
            if (retry)
            {
                run = snapshot.Clone();
                damage.RepairAll();
                ApplyRunToCar(run);
            }
            health.marked = false;
            plates.SetMarked(false);
            EnterGarage();
            yield return new WaitForSeconds(0.3f);
            yield return overlay.FadeTo(0f, 0.6f);
        }

        // ------------------------------------------------------------------ garage

        void EnterGarage()
        {
            CurrentPhase = Phase.Garage;
            var gun = player.GetComponent<CarGun>();
            if (gun != null) gun.Reload();
            ParkInGarage();
            PrepareNextMatch();
            run.health = health.Health;
            run.Save();
            EnterTabletMode();
            tablet.ShowGarage();
        }

        void EnterTabletMode()
        {
            if (hud != null) hud.enabled = false;
            if (combatHud != null) combatHud.enabled = false;
            if (crowd != null) crowd.SetCabin(true);
            garageCamera.SetActive(true);
            tablet.Show();
            SetCursor(true);
        }

        /// <summary>Roll this match's opponents and the emperor's mark (seeded, so a retry meets the same cars).</summary>
        void PrepareNextMatch()
        {
            NextMatch = settings.GetMatch(run.matchIndex);
            var rng = new System.Random(run.matchIndex * 7919 + run.carNumber * 31 + 17);

            nextEnemies.Clear();
            var used = new HashSet<int> { run.carNumber };
            foreach (var type in NextMatch.enemies)
            {
                int number;
                if (type == EnemyType.Champion)
                {
                    number = Mathf.Clamp(settings.championNumber, 10, 99);
                    if (used.Contains(number)) number = 99;
                }
                else
                {
                    do number = rng.Next(10, 100); while (used.Contains(number));
                }
                used.Add(number);
                float scale = NextMatch.statScale * (1f + number * settings.healthPerNumber);
                nextEnemies.Add(new EnemySpec
                {
                    prefab = settings.enemyPrefabs[(int)type],
                    number = number,
                    healthScale = scale,
                    powerScale = 1f + (NextMatch.statScale - 1f) * 0.5f,
                    label = type == EnemyType.Champion ? $"The Champion #{CarNumberPlates.Format(number)}" : $"{type} #{CarNumberPlates.Format(number)}",
                });
            }

            // The emperor marks one participant: you or one of them. Always drawn, so retries match.
            int pick = rng.Next(0, nextEnemies.Count + 1);
            if (run.markedNumber < 0) run.markedNumber = pick == 0 ? run.carNumber : nextEnemies[pick - 1].number;
            foreach (var e in nextEnemies) e.marked = e.number == run.markedNumber;
        }

        void ParkInGarage()
        {
            var p = playerGate.spawnPoint;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = p.position;
            body.rotation = p.rotation;
            player.transform.SetPositionAndRotation(p.position, p.rotation);
            foreach (var w in player.wheels) w.ResetState();
            input.overrideDriving = true;
            input.overrideThrottle = 0f;
            input.overrideBrake = 0f;
            input.overrideSteer = 0f;
            input.overrideHandbrake = true;
            playerGate.Close();
        }

        void ApplyRunToCar(RunState r)
        {
            ApplyUpgrades(r);
            float hp = r.health < 0f ? health.maxHealth : r.health;
            health.SetHealth(hp);
            plates.SetNumber(r.HasNumber ? r.carNumber : -1);
            plates.SetMarked(false);
        }

        void ApplyUpgrades() => ApplyUpgrades(run);

        void ApplyUpgrades(RunState r)
        {
            if (upgrades == null) return;
            upgrades.Apply(r.Level(UpgradeType.Armour), r.Level(UpgradeType.Engine), r.Level(UpgradeType.Tyres), r.Level(UpgradeType.RamPlate),
                r.Level(UpgradeType.Boost), r.Level(UpgradeType.Gun));
        }

        static void SetCursor(bool visible)
        {
            Cursor.visible = visible;
            Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
        }
    }
}
