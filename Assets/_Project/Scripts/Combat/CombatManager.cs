using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HauntedCasino.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

namespace HauntedCasino.Combat
{
    /// <summary>
    /// Turn-based fight loop.
    /// Round start: the ghost announces its planned attacks (target + game).
    /// Player turns: pick a game (or Party Slots / Revive), stake chips for extra damage, roll.
    /// Everyone not rolling places a BIG/SMALL side bet on the roll.
    /// Ghost turn: it executes its plan; teammates may step in to DEFEND; the defender rolls defence.
    /// Chips are health. Hits build a shared Hot Streak and the team meter for Party Slots.
    /// Input is local hot-seat; rules live here so they can later move to a networked host.
    /// </summary>
    public class CombatManager : MonoBehaviour
    {
        [SerializeField] CombatConfig config = new CombatConfig();
        [SerializeField] string ghostName = "Lucky Lou, the Eternal High Roller";
        [SerializeField] UITheme theme;

        static readonly string[] PlayerNames = { "Ace", "Jinx", "Clover", "Snake Eyes" };
        static readonly Color[] PlayerColors =
        {
            new Color(1f, 0.5f, 0.3f), new Color(0.4f, 0.65f, 1f), new Color(0.45f, 0.95f, 0.5f), new Color(0.85f, 0.55f, 1f)
        };
        static readonly Key[][] NumberKeys =
        {
            new[] { Key.Digit1, Key.Numpad1 }, new[] { Key.Digit2, Key.Numpad2 },
            new[] { Key.Digit3, Key.Numpad3 }, new[] { Key.Digit4, Key.Numpad4 }
        };
        static readonly Key[] BigBetKeys = { Key.Q, Key.W, Key.E, Key.R };
        static readonly Key[] SmallBetKeys = { Key.A, Key.S, Key.D, Key.F };
        static readonly Color ReviveColor = new Color(0.4f, 1f, 0.6f);

        struct PlannedAttack
        {
            public Combatant Target;
            public GambleGame Game;
        }

        enum TurnAction { Game, Party, Revive }

        CombatUI ui;
        CombatStage stage;
        List<GambleGame> games;
        PartySlotsGame partySlots;
        readonly List<Combatant> players = new List<Combatant>();
        readonly List<PlannedAttack> plan = new List<PlannedAttack>();
        Combatant ghost;
        int round, streak, teamMeter;

        // Side bets
        readonly List<Combatant> bettors = new List<Combatant>();
        readonly Dictionary<Combatant, bool> bets = new Dictionary<Combatant, bool>();
        bool betsOpen;
        GambleGame betGame;

        float StreakMultiplier => Mathf.Min(config.maxStreakMultiplier, 1f + streak * config.streakBonusPerStep);
        bool PartyReady => teamMeter >= config.teamMeterMax;
        int MaxStreakSteps => Mathf.CeilToInt((config.maxStreakMultiplier - 1f) / Mathf.Max(0.01f, config.streakBonusPerStep));
        int ghostAttacksDone;

        static string K(string key) => UIFactory.Key(key);

        /// <summary>Turn-order strip: players' seats, then the ghost's planned attacks.</summary>
        void UpdateTurnOrder(int currentSlot)
        {
            var slots = new List<CombatUI.TurnSlot>();
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                var state = p.IsDown ? CombatUI.SlotState.Down
                    : i == currentSlot ? CombatUI.SlotState.Current
                    : i < currentSlot ? CombatUI.SlotState.Done
                    : CombatUI.SlotState.Upcoming;
                slots.Add(new CombatUI.TurnSlot($"P{i + 1}", p.Color, state));
            }
            string ghostLabel = ShortName(ghost).Split(' ').Last().ToUpper();
            int ghostSlots = ghostAttacksDone + plan.Count;
            for (int g = 0; g < ghostSlots; g++)
            {
                int slot = players.Count + g;
                var state = slot == currentSlot ? CombatUI.SlotState.Current
                    : slot < currentSlot ? CombatUI.SlotState.Done
                    : CombatUI.SlotState.Upcoming;
                slots.Add(new CombatUI.TurnSlot(ghostLabel, ghost.Color, state));
            }
            ui.SetTurnOrder(slots);
        }

        void Start()
        {
            Time.timeScale = 1f;
            if (theme != null) UITheme.Current = theme;
            ui = GetOrAdd<CombatUI>();
            stage = GetOrAdd<CombatStage>();
            GetOrAdd<CasinoSfx>();
            games = new List<GambleGame> { GetOrAdd<SlotsGame>(), GetOrAdd<RouletteGame>(), GetOrAdd<DiceGame>() };
            partySlots = GetOrAdd<PartySlotsGame>();

            stage.BuildEnvironment();
            ui.BuildBase();
            foreach (var g in games) g.Build(ui.GameHost);
            partySlots.Build(ui.GameHost);
            ui.ShowSetup(config.playerCount, count =>
            {
                config.playerCount = count;
                StartCoroutine(Battle());
            });
        }

        T GetOrAdd<T>() where T : Component => TryGetComponent<T>(out var c) ? c : gameObject.AddComponent<T>();

        IEnumerator Battle()
        {
            for (int i = 0; i < config.playerCount; i++)
                players.Add(new Combatant(PlayerNames[i], PlayerColors[i], true, i, config.playerMaxHP));
            ghost = new Combatant(ghostName, CombatUI.GhostColor, false, -1, config.ghostHPPerPlayer * config.playerCount);

            stage.BuildActors(players, ghost);
            ui.BuildCombatants(players, ghost);
            ui.SetStreak(streak, StreakMultiplier, MaxStreakSteps);
            ui.SetTeamMeter(teamMeter, config.teamMeterMax);
            ui.Log($"{Tag(ghost)} rises from the card table!");

            while (true)
            {
                round++;
                ui.SetRound(round);
                ui.Log($"<b>— Round {round} —</b>");
                PlanGhostAttacks();

                foreach (var p in players)
                {
                    if (p.IsDown) continue;
                    yield return PlayerTurn(p);
                    if (ghost.IsDown) break;
                }
                if (ghost.IsDown)
                {
                    yield return End(true);
                    yield break;
                }

                yield return GhostTurn();
                if (ghost.IsDown)
                {
                    yield return End(true);
                    yield break;
                }
                if (players.All(p => p.IsDown))
                {
                    yield return End(false);
                    yield break;
                }
            }
        }

        // ---------- Ghost intent ----------

        void PlanGhostAttacks()
        {
            plan.Clear();
            ghostAttacksDone = 0;
            var alive = players.Where(p => !p.IsDown).ToList();
            if (alive.Count == 0) return;
            for (int i = 0; i < config.ghostAttacksPerRound; i++)
                plan.Add(new PlannedAttack { Target = alive[Random.Range(0, alive.Count)], Game = games[Random.Range(0, games.Count)] });
            ShowIntent();
        }

        void ShowIntent()
        {
            if (plan.Count == 0)
            {
                ui.SetIntent("");
                foreach (var p in players) ui.SetPanelIntent(p, "");
                ui.Refresh();
                return;
            }
            var moves = plan.Select(a => $"{a.Game.DisplayName} → {Big(a.Target)}");
            ui.SetIntent($"<color=#ff9a9a>{ShortName(ghost).ToUpper()} PLANS</color>   {string.Join("   ·   ", moves)}");
            foreach (var p in players)
            {
                var mine = plan.Where(a => a.Target == p).Select(a => a.Game.DisplayName).ToList();
                ui.SetPanelIntent(p, mine.Count == 0 ? "" : $"<color=#ff8080>{ShortName(ghost)} plans {string.Join(" + ", mine)} on you</color>");
            }
            ui.Refresh();
        }

        // ---------- Player turn ----------

        IEnumerator PlayerTurn(Combatant p)
        {
            UpdateTurnOrder(players.IndexOf(p));
            SetRoles(null, null);
            ui.SetDuel("", Color.black);
            ui.SetPrompt("", p.Color);
            yield return ui.TurnCard($"{p.Name.ToUpper()}'S TURN", "Pick a game  ·  stake your chips  ·  roll", p.Color);

            SetRoles(p, ghost, "TARGET");
            ui.SetCallout(p, "YOUR MOVE");
            ui.SetDuel($"{Big(p)}  <size=70%>ATTACKS</size>  {Big(ghost)}", CombatUI.AttackColor);
            var bettorsHere = players.Where(o => o != p && !o.IsDown).ToList();
            OpenSideBets(bettorsHere);
            var downed = players.Where(o => o != p && o.IsDown).ToList();
            string betHint = bettorsHere.Count > 0 ? "      <color=#b8a890>Everyone else: side bet on your card</color>" : "";
            ui.SetPrompt($"{Tag(p)}: press {K("1")}–{K((games.Count + (PartyReady ? 1 : 0) + (downed.Count > 0 ? 1 : 0)).ToString())} or click a game{betHint}", Color.white);

            var options = new List<CombatUI.Choice>();
            var actions = new List<(TurnAction action, int game)>();
            for (int i = 0; i < games.Count; i++)
            {
                options.Add(new CombatUI.Choice($"{games[i].DisplayName}", games[i].Blurb, games[i].Accent, null));
                actions.Add((TurnAction.Game, i));
            }
            if (PartyReady)
            {
                options.Add(new CombatUI.Choice($"PARTY SLOTS", partySlots.Blurb, CombatUI.PartyColor, null));
                actions.Add((TurnAction.Party, -1));
            }
            if (downed.Count > 0)
            {
                options.Add(new CombatUI.Choice($"Revive", $"Skip attack, revive {downed[0].Name} (+{config.reviveHP} chips)", ReviveColor, null));
                actions.Add((TurnAction.Revive, -1));
            }

            int pick = -1;
            yield return Choose(options, i => pick = i);
            var (action, gameIndex) = actions[pick];

            if (action == TurnAction.Revive)
            {
                CancelSideBets();
                var target = downed[0];
                SetRoles(null, null);
                ui.SetCallout(null, null);
                ui.SetPrompt("", Color.white);
                ui.SetDuel($"{Big(p)}  <size=70%>REVIVES</size>  {Big(target)}", ReviveColor);
                int healed = target.Heal(config.reviveHP);
                Popup(target, $"+{healed}", ReviveColor);
                CasinoSfx.Play(Sfx.Coin, 0.8f);
                ui.Log($"{Tag(p)} hauls {Tag(target)} back to the table (+{healed} chips).");
                ui.Refresh();
                yield return new WaitForSeconds(1f);
                yield break;
            }
            if (action == TurnAction.Party)
            {
                CancelSideBets();
                ui.SetCallout(null, null);
                yield return PartyAttack(p);
                yield break;
            }

            var game = games[gameIndex];
            int stake = 0;
            float stakeMult = 1f;
            yield return ChooseStake(p, (s, m) =>
            {
                stake = s;
                stakeMult = m;
            });
            if (stake > 0)
            {
                CasinoSfx.Play(Sfx.Chip, 0.9f);
                ui.Log($"{Tag(p)} stakes <b>{stake} chips</b> for x{stakeMult:0.#} damage.");
            }

            ui.SetPrompt(game == games[0] || game is DiceGame ? $"{Tag(p)}: {K("Space")} to stop" : $"{Tag(p)}: place your bet", Color.white);
            ui.SetCallout(p, "ROLL!");
            var ctx = Context(p, ghost, GambleMode.Attack);
            ctx.StakeMultiplier = stakeMult;
            var outcome = default(GambleOutcome);
            betGame = game;
            yield return game.Play(ctx, o => outcome = o);
            ui.SetCallout(null, null);
            ui.SetPrompt("", Color.white);
            ResolveSideBets(outcome);

            if (outcome.Kind == OutcomeKind.Bust)
            {
                int self = p.TakeDamage(config.backfireDamage + stake);
                streak = 0;
                stage.Hit(p, 0.6f);
                Popup(p, $"BUST -{self}", GambleGame.BustRed);
                ui.Log($"{Tag(p)} busts at {game.DisplayName}! Loses {self} chips. Streak lost.");
                if (p.IsDown) ui.Log($"{Tag(p)} is BUSTED!");
            }
            else if (outcome.Power <= 0)
            {
                int lost = p.TakeDamage(stake);
                streak = 0;
                Popup(ghost, "MISS", Color.gray);
                ui.Log($"{Tag(p)} whiffs at {game.DisplayName}{(lost > 0 ? $" and loses the {lost}-chip stake" : "")}. Streak lost.");
                if (lost > 0) Popup(p, $"-{lost}", GambleGame.BustRed);
            }
            else
            {
                float mult = StreakMultiplier * stakeMult;
                int dmg = ghost.TakeDamage(Mathf.RoundToInt(outcome.Power * mult));
                streak++;
                AddTeamCharge();
                bool jackpot = outcome.Kind == OutcomeKind.Jackpot;
                stage.Hit(ghost, jackpot ? 1f : dmg / 25f);
                if (jackpot) stage.CoinBurst(ghost, 60);
                Popup(ghost, $"{(jackpot ? "JACKPOT! " : "")}-{dmg}", jackpot ? GambleGame.Gold : Color.white);
                ui.Log($"{Tag(p)} hits {Tag(ghost)} with {game.DisplayName} for <b>{dmg}</b>{(mult > 1f ? $" (x{mult:0.00})" : "")}.");
            }
            ui.SetStreak(streak, StreakMultiplier, MaxStreakSteps);
            ui.Refresh();
            yield return new WaitForSeconds(0.7f);
        }

        IEnumerator ChooseStake(Combatant p, Action<int, float> chosen)
        {
            var options = new List<CombatUI.Choice>();
            var stakes = new List<(int chips, float mult)>();
            options.Add(new CombatUI.Choice("Check", "No stake. Normal damage.", new Color(0.5f, 0.5f, 0.55f), null));
            stakes.Add((0, 1f));
            if (p.HP > config.raiseStake)
            {
                options.Add(new CombatUI.Choice($"Raise {config.raiseStake}",
                    $"Stake {config.raiseStake} chips: x{config.raiseMultiplier:0.#} damage. Lost on a miss.", new Color(0.9f, 0.7f, 0.2f), null));
                stakes.Add((config.raiseStake, config.raiseMultiplier));
            }
            if (p.HP > config.allInStake)
            {
                options.Add(new CombatUI.Choice($"High roller {config.allInStake}",
                    $"Stake {config.allInStake} chips: x{config.allInMultiplier:0.#} damage. Lost on a miss.", new Color(1f, 0.35f, 0.25f), null));
                stakes.Add((config.allInStake, config.allInMultiplier));
            }
            if (stakes.Count == 1)
            {
                chosen(0, 1f);
                yield break;
            }

            ui.SetCallout(p, "STAKE?");
            ui.SetPrompt($"{Tag(p)}: stake chips for more damage   {K("1")}–{K(options.Count.ToString())}   <color=#b8a890>(lost on a miss)</color>", Color.white);
            int pick = -1;
            yield return Choose(options, i => pick = i);
            chosen(stakes[pick].chips, stakes[pick].mult);
        }

        IEnumerator PartyAttack(Combatant p)
        {
            var owners = players.Where(o => !o.IsDown).ToList();
            SetRoles(null, ghost, "TARGET");
            ui.SetDuel($"<color=#ff8cf2>THE WHOLE TABLE</color>  <size=70%>ATTACKS</size>  {Big(ghost)}", CombatUI.PartyColor);
            var ownerKeys = string.Join(" ", owners.Select(o => K((o.Index + 1).ToString())));
            ui.SetPrompt($"<color=#ff8cf2><b>PARTY SLOTS!</b></color>   Everyone stop YOUR reel with your number: {ownerKeys}", Color.white);
            yield return ui.TurnCard("PARTY SLOTS!", "Everyone stops their own reel", CombatUI.PartyColor);
            ui.Log($"{Tag(p)} calls in the <color=#ff8cf2>PARTY SLOTS</color>!");
            CasinoSfx.Play(Sfx.Jackpot, 0.8f);

            partySlots.SetOwners(owners);
            var ctx = new GambleContext
            {
                Mode = GambleMode.Attack, IsAI = false, ActorName = "The whole table", ActorColor = CombatUI.PartyColor,
                OpponentName = ShortName(ghost), OpponentColor = ghost.Color, StakeMultiplier = 1f
            };
            var outcome = default(GambleOutcome);
            yield return partySlots.Play(ctx, o => outcome = o);

            teamMeter = 0;
            ui.SetTeamMeter(teamMeter, config.teamMeterMax);
            if (outcome.Power > 0)
            {
                float mult = StreakMultiplier;
                int dmg = ghost.TakeDamage(Mathf.RoundToInt(outcome.Power * mult));
                streak++;
                bool jackpot = outcome.Kind == OutcomeKind.Jackpot;
                stage.Hit(ghost, 1f);
                stage.CoinBurst(ghost, jackpot ? 150 : 40);
                Popup(ghost, $"{(jackpot ? "TEAM JACKPOT! " : "")}-{dmg}", jackpot ? GambleGame.Gold : CombatUI.PartyColor);
                ui.Log($"The whole table hits {Tag(ghost)} for <b>{dmg}</b>!");
            }
            else ui.Log("The party slots come up empty...");
            ui.SetStreak(streak, StreakMultiplier, MaxStreakSteps);
            ui.Refresh();
            yield return new WaitForSeconds(0.8f);
        }

        void AddTeamCharge()
        {
            if (teamMeter >= config.teamMeterMax) return;
            teamMeter++;
            ui.SetTeamMeter(teamMeter, config.teamMeterMax);
            if (PartyReady)
            {
                ui.Log("<color=#ff8cf2>TEAM JACKPOT READY — anyone can call Party Slots on their turn!</color>");
                CasinoSfx.Play(Sfx.Coin, 1.3f);
            }
        }

        // ---------- Ghost turn ----------

        IEnumerator GhostTurn()
        {
            if (plan.Count == 0) yield break;
            UpdateTurnOrder(players.Count);
            SetRoles(null, null);
            ui.SetDuel("", Color.black);
            ui.SetPrompt("", Color.white);
            yield return ui.TurnCard($"{ShortName(ghost).ToUpper()} DEALS", "Watch who it targets  ·  side bet  ·  step in to defend", ghost.Color);

            while (plan.Count > 0)
            {
                var alive = players.Where(p => !p.IsDown).ToList();
                if (alive.Count == 0 || ghost.IsDown) yield break;
                UpdateTurnOrder(players.Count + ghostAttacksDone);

                var planned = plan[0];
                var target = planned.Target;
                if (target.IsDown)
                {
                    target = alive[Random.Range(0, alive.Count)];
                    ui.Log($"{Tag(ghost)} turns its gaze to {Tag(target)} instead.");
                }
                var game = planned.Game;

                SetRoles(ghost, target, "TARGETED!");
                ui.SetDuel($"{Big(ghost)}  <size=70%>ATTACKS</size>  {Big(target)}", CombatUI.TargetColor);
                OpenSideBets(alive);
                ui.SetPrompt($"{Tag(ghost)} readies {game.DisplayName}.   <b>SIDE BETS OPEN</b>: will it roll BIG (10+) or SMALL?   <color=#b8a890>use the keys on your card</color>", Color.white);
                for (float t = 0; t < config.ghostBetDelay; t += Time.deltaTime)
                {
                    ui.SetTimer(1f - t / config.ghostBetDelay, GambleGame.Gold);
                    yield return null;
                }
                ui.HideTimer();
                ui.SetPrompt($"{Tag(ghost)} plays {game.DisplayName} against {Tag(target)}...", Color.white);

                var attack = default(GambleOutcome);
                betGame = game;
                yield return game.Play(Context(ghost, target, GambleMode.Attack), o => attack = o);
                ResolveSideBets(attack);
                plan.RemoveAt(0);
                ghostAttacksDone++;
                ShowIntent();

                if (attack.Kind == OutcomeKind.Bust)
                {
                    int self = ghost.TakeDamage(config.backfireDamage * 2);
                    stage.Hit(ghost, 0.7f);
                    Popup(ghost, $"BUST -{self}", GambleGame.Gold);
                    ui.Log($"{Tag(ghost)} busts and burns itself for {self}!");
                    ui.Refresh();
                    yield return new WaitForSeconds(0.7f);
                    continue;
                }

                int incoming = Mathf.RoundToInt(attack.Power * config.ghostAttackMultiplier);
                if (incoming <= 0)
                {
                    ui.Log($"{Tag(ghost)}'s gamble comes up empty.");
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }

                Combatant defender = target;
                yield return DefendWindow(target, incoming, alive, c => defender = c);

                SetRoles(ghost, defender, "DEFENDING");
                ui.SetDuel(defender == target
                        ? $"{Big(defender)}  <size=70%>DEFENDS vs</size>  {Big(ghost)}"
                        : $"{Big(defender)}  <size=70%>DEFENDS</size>  {Big(target)}",
                    CombatUI.DefendColor);
                ui.SetCallout(defender, "DEFEND!");
                ui.SetPrompt($"{Tag(defender)}: block <b>{incoming}</b> damage. Press {K("1")}–{K(games.Count.ToString())} to pick a game", Color.white);
                int pick = -1;
                yield return Choose(GameOptions(), i => pick = i);
                ui.SetCallout(defender, "ROLL!");
                ui.SetPrompt("", defender.Color);
                var defGame = games[pick];
                var defence = default(GambleOutcome);
                var ctx = Context(defender, ghost, GambleMode.Defence);
                ctx.Incoming = incoming;
                yield return defGame.Play(ctx, o => defence = o);
                ui.SetCallout(null, null);

                int block, reflect = 0;
                if (defence.Kind == OutcomeKind.Bust) block = 0;
                else if (defence.Kind == OutcomeKind.Jackpot)
                {
                    block = incoming;
                    reflect = Mathf.RoundToInt(incoming * config.jackpotReflect);
                }
                else block = Mathf.Min(incoming, Mathf.RoundToInt(defence.Power * config.defenceMultiplier));

                int taken = defender.TakeDamage(incoming - block);
                if (taken > 0)
                {
                    stage.Hit(defender, taken / 15f);
                    Popup(defender, $"-{taken}", GambleGame.BustRed);
                }
                else
                {
                    Popup(defender, "BLOCKED!", CombatUI.DefendColor);
                    CasinoSfx.Play(Sfx.Block);
                    AddTeamCharge();
                }
                ui.Log($"{Tag(defender)} defends with {defGame.DisplayName}: blocks {block}, loses <b>{taken}</b> chips.");

                if (reflect > 0)
                {
                    int r = ghost.TakeDamage(reflect);
                    stage.Hit(ghost, 0.8f);
                    stage.CoinBurst(defender, 40);
                    Popup(ghost, $"REFLECT -{r}", GambleGame.Gold);
                    ui.Log($"Jackpot defence! {Tag(defender)} reflects {r} back at {Tag(ghost)}!");
                }
                if (defender.IsDown) ui.Log($"{Tag(defender)} is BUSTED!");
                ui.Refresh();
                yield return new WaitForSeconds(0.8f);
            }
        }

        /// <summary>Short window where any standing teammate can step in and defend the target instead.</summary>
        IEnumerator DefendWindow(Combatant target, int incoming, List<Combatant> alive, Action<Combatant> setDefender)
        {
            var helpers = alive.Where(p => p != target).ToList();
            if (helpers.Count == 0) yield break;

            Combatant stepIn = null;
            var keys = string.Join(" ", helpers.Select(h => K((h.Index + 1).ToString())));
            ui.SetPrompt($"<b>{incoming} damage</b> incoming at {Tag(target)}!   Teammates: press {keys} to <b>DEFEND</b> them", Color.white);
            ui.ShowDefendButtons(helpers, c => stepIn = c);
            for (float t = 0; t < config.defendWindowSeconds && stepIn == null; t += Time.deltaTime)
            {
                ui.SetTimer(1f - t / config.defendWindowSeconds, CombatUI.DefendColor);
                foreach (var h in helpers)
                    if (NumberPressed(h.Index)) stepIn = h;
                yield return null;
            }
            ui.HideDefendButtons();
            if (stepIn != null)
            {
                ui.Log($"{Tag(stepIn)} steps in to defend {Tag(target)}!");
                CasinoSfx.Play(Sfx.Block, 1.3f);
                setDefender(stepIn);
            }
        }

        // ---------- Side bets ----------

        void OpenSideBets(IEnumerable<Combatant> who)
        {
            bets.Clear();
            bettors.Clear();
            bettors.AddRange(who);
            betGame = null;
            betsOpen = bettors.Count > 0;
            if (betsOpen) ui.ShowSideBets(bettors, PlaceBet);
        }

        void PlaceBet(Combatant c, bool big)
        {
            if (!betsOpen || (betGame != null && betGame.Resolved) || !bettors.Contains(c)) return;
            bets[c] = big;
            ui.SetBet(c, big);
            CasinoSfx.Play(Sfx.Chip, big ? 1.25f : 1.05f);
        }

        void CancelSideBets()
        {
            betsOpen = false;
            bets.Clear();
            ui.ClearSideBets();
        }

        void ResolveSideBets(GambleOutcome outcome)
        {
            if (!betsOpen) return;
            betsOpen = false;
            if (bets.Count == 0)
            {
                ui.ClearSideBets();
                return;
            }

            bool big = outcome.Kind != OutcomeKind.Bust && outcome.Power >= config.bigThreshold;
            int correct = 0;
            foreach (var kv in bets)
            {
                var c = kv.Key;
                if (kv.Value == big)
                {
                    correct++;
                    int won = c.Heal(config.sideBetWin);
                    Popup(c, won > 0 ? $"BET +{won}" : "BET WON", GambleGame.Gold);
                }
                else
                {
                    int lost = c.TakeDamage(Mathf.Min(config.sideBetLoss, c.HP - 1));
                    if (lost > 0) Popup(c, $"BET -{lost}", Color.gray);
                }
            }
            ui.Log($"Side bets: it rolled <b>{(big ? "BIG" : "SMALL")}</b>. {correct}/{bets.Count} called it.");
            if (correct > 0) CasinoSfx.Play(Sfx.Coin, 1.1f);
            if (correct == bets.Count)
            {
                streak++;
                ui.Log("<color=#ffd24d>Every bettor called it — the table is HOT! Streak +1.</color>");
                ui.SetStreak(streak, StreakMultiplier, MaxStreakSteps);
            }
            ui.ClearSideBets();
        }

        void Update()
        {
            if (!betsOpen || Keyboard.current == null) return;
            if (betGame != null && betGame.Resolved)
            {
                ui.LockSideBets();
                return;
            }
            foreach (var c in bettors)
            {
                if (c.Index < 0 || c.Index >= BigBetKeys.Length) continue;
                if (Keyboard.current[BigBetKeys[c.Index]].wasPressedThisFrame) PlaceBet(c, true);
                else if (Keyboard.current[SmallBetKeys[c.Index]].wasPressedThisFrame) PlaceBet(c, false);
            }
        }

        // ---------- Shared helpers ----------

        List<CombatUI.Choice> GameOptions()
        {
            var options = new List<CombatUI.Choice>();
            for (int i = 0; i < games.Count; i++)
                options.Add(new CombatUI.Choice(games[i].DisplayName, games[i].Blurb, games[i].Accent, null));
            return options;
        }

        /// <summary>Shows the options as buttons and waits for a click or the matching number key.</summary>
        IEnumerator Choose(List<CombatUI.Choice> options, Action<int> chosen)
        {
            int pick = -1;
            var wired = new List<CombatUI.Choice>();
            for (int i = 0; i < options.Count; i++)
            {
                int idx = i;
                var o = options[i];
                wired.Add(new CombatUI.Choice(o.Title, o.Sub, o.Color, () => pick = idx));
            }
            ui.ShowChoices(wired);
            yield return null;
            while (pick < 0)
            {
                for (int k = 0; k < options.Count; k++)
                    if (NumberPressed(k)) pick = k;
                yield return null;
            }
            ui.HideChoices();
            chosen(pick);
        }

        IEnumerator End(bool victory)
        {
            CancelSideBets();
            SetRoles(null, null);
            ui.SetCallout(null, null);
            ui.HideTimer();
            ui.SetDuel("", Color.black);
            ui.SetIntent("");
            ui.SetPrompt(victory ? "The ghost is banished!" : "Everyone went bust...", victory ? GambleGame.Gold : GambleGame.BustRed);
            if (victory)
            {
                stage.CoinBurst(ghost, 200);
                CasinoSfx.Play(Sfx.Jackpot);
            }
            else CasinoSfx.Play(Sfx.Bust, 0.7f);
            yield return new WaitForSeconds(1.5f);
            ui.ShowEnd(victory, round, () => SceneManager.LoadScene(SceneManager.GetActiveScene().name));
        }

        void SetRoles(Combatant attacker, Combatant defender, string defenderLabel = "DEFENDING")
        {
            ui.SetRoles(attacker, defender, defenderLabel);
            stage.SetRoles(attacker, defender, defenderLabel == "DEFENDING");
        }

        void Popup(Combatant c, string text, Color color) => ui.Popup(stage.Cam, stage.PopupPoint(c), text, color);

        static string ShortName(Combatant c) => c.IsPlayer ? c.Name : c.Name.Split(',')[0];

        static string Tag(Combatant c) => $"<color=#{ColorUtility.ToHtmlStringRGB(c.Color)}>{ShortName(c)}</color>";

        static string Big(Combatant c) => $"<color=#{ColorUtility.ToHtmlStringRGB(c.Color)}>{ShortName(c).ToUpper()}</color>";

        GambleContext Context(Combatant actor, Combatant opponent, GambleMode mode) =>
            new GambleContext
            {
                Mode = mode,
                IsAI = !actor.IsPlayer,
                ActorName = ShortName(actor),
                ActorColor = actor.Color,
                OpponentName = ShortName(opponent),
                OpponentColor = opponent.Color,
                DefenceMultiplier = config.defenceMultiplier,
                StakeMultiplier = 1f
            };

        static bool NumberPressed(int index)
        {
            var kb = Keyboard.current;
            if (kb == null || index < 0 || index >= NumberKeys.Length) return false;
            foreach (var key in NumberKeys[index])
                if (kb[key].wasPressedThisFrame) return true;
            return false;
        }
    }
}
