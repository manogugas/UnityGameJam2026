using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

namespace HauntedCasino.Combat
{
    /// <summary>
    /// Turn-based fight loop. Each round every standing player attacks with a casino game of their
    /// choice, then the ghost attacks with the same games. The targeted player (or a teammate who
    /// covers them) rolls defence. Successful hits build a shared Hot Streak damage multiplier.
    /// Input is local hot-seat; rules live here so they can later move to a networked host.
    /// </summary>
    public class CombatManager : MonoBehaviour
    {
        [SerializeField] CombatConfig config = new CombatConfig();
        [SerializeField] string ghostName = "Lucky Lou, the Eternal High Roller";

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
        static readonly Color ReviveColor = new Color(0.4f, 1f, 0.6f);

        CombatUI ui;
        CombatStage stage;
        List<GambleGame> games;
        readonly List<Combatant> players = new List<Combatant>();
        Combatant ghost;
        int round;
        int streak;

        float StreakMultiplier => Mathf.Min(config.maxStreakMultiplier, 1f + streak * config.streakBonusPerStep);

        void Start()
        {
            ui = GetOrAdd<CombatUI>();
            stage = GetOrAdd<CombatStage>();
            games = new List<GambleGame> { GetOrAdd<SlotsGame>(), GetOrAdd<RouletteGame>(), GetOrAdd<DiceGame>() };

            stage.BuildEnvironment();
            ui.BuildBase();
            foreach (var g in games) g.Build(ui.GameHost);
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
            ui.SetStreak(streak, StreakMultiplier);
            ui.Log($"<color=#8ff>{ghost.Name}</color> rises from the card table!");

            while (true)
            {
                round++;
                ui.SetRound(round);
                ui.Log($"<b>— Round {round} —</b>");

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

        // ---------- Player turn ----------

        IEnumerator PlayerTurn(Combatant p)
        {
            Focus(p);
            var downed = players.Where(o => o != p && o.IsDown).ToList();
            ui.SetPrompt($"{p.Name}'s turn — pick a game to ATTACK", p.Color);

            int pick = -1;
            yield return ChooseGame(downed.Count > 0, i => pick = i);

            if (pick == games.Count)
            {
                var target = downed[0];
                int healed = target.Heal(config.reviveHP);
                stage.SetDown(target, false);
                Popup(target, $"+{healed}", ReviveColor);
                ui.Log($"{Tag(p)} hauls {Tag(target)} back to the table (+{healed} HP).");
                ui.Refresh();
                yield return new WaitForSeconds(0.8f);
                yield break;
            }

            var game = games[pick];
            ui.SetPrompt($"{p.Name} attacks with {game.DisplayName}!", p.Color);
            var outcome = default(GambleOutcome);
            yield return game.Play(Context(p, GambleMode.Attack, false), o => outcome = o);

            if (outcome.Kind == OutcomeKind.Bust)
            {
                int self = p.TakeDamage(config.backfireDamage);
                streak = 0;
                stage.Hit(p);
                Popup(p, $"BUST -{self}", GambleGame.BustRed);
                ui.Log($"{Tag(p)} busts at {game.DisplayName}! Backfire for {self}. Streak lost.");
                if (p.IsDown) ui.Log($"{Tag(p)} is BUSTED!");
            }
            else if (outcome.Power <= 0)
            {
                streak = 0;
                Popup(ghost, "MISS", Color.gray);
                ui.Log($"{Tag(p)} whiffs at {game.DisplayName}. Streak lost.");
            }
            else
            {
                float mult = StreakMultiplier;
                int dmg = ghost.TakeDamage(Mathf.RoundToInt(outcome.Power * mult));
                streak++;
                stage.Hit(ghost);
                string jackpot = outcome.Kind == OutcomeKind.Jackpot ? "JACKPOT! " : "";
                Popup(ghost, $"{jackpot}-{dmg}", outcome.Kind == OutcomeKind.Jackpot ? GambleGame.Gold : Color.white);
                ui.Log($"{Tag(p)} hits with {game.DisplayName} for <b>{dmg}</b>{(mult > 1f ? $" (x{mult:0.00} streak)" : "")}.");
            }
            ui.SetStreak(streak, StreakMultiplier);
            ui.Refresh();
            yield return new WaitForSeconds(0.7f);
        }

        // ---------- Ghost turn ----------

        IEnumerator GhostTurn()
        {
            for (int a = 0; a < config.ghostAttacksPerRound; a++)
            {
                var alive = players.Where(p => !p.IsDown).ToList();
                if (alive.Count == 0 || ghost.IsDown) yield break;

                var target = alive[Random.Range(0, alive.Count)];
                var game = games[Random.Range(0, games.Count)];
                Focus(ghost);
                ui.SetPrompt($"{ShortGhost} deals {game.DisplayName} against {target.Name}!", CombatUI.GhostColor);

                var attack = default(GambleOutcome);
                yield return game.Play(Context(ghost, GambleMode.Attack, true), o => attack = o);

                if (attack.Kind == OutcomeKind.Bust)
                {
                    int self = ghost.TakeDamage(config.backfireDamage * 2);
                    stage.Hit(ghost);
                    Popup(ghost, $"BUST -{self}", GambleGame.Gold);
                    ui.Log($"The ghost busts and burns itself for {self}!");
                    ui.Refresh();
                    yield return new WaitForSeconds(0.7f);
                    continue;
                }

                int incoming = Mathf.RoundToInt(attack.Power * config.ghostAttackMultiplier);
                if (incoming <= 0)
                {
                    ui.Log("The ghost's gamble comes up empty.");
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }

                Combatant defender = target;
                yield return CoverWindow(target, incoming, alive, c => defender = c);

                Focus(defender);
                ui.SetPrompt($"{defender.Name}: DEFEND against {incoming} damage! Pick a game", defender.Color);
                int pick = -1;
                yield return ChooseGame(false, i => pick = i);
                var defGame = games[pick];
                var defence = default(GambleOutcome);
                yield return defGame.Play(Context(defender, GambleMode.Defence, false), o => defence = o);

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
                    stage.Hit(defender);
                    Popup(defender, $"-{taken}", GambleGame.BustRed);
                }
                else Popup(defender, "BLOCKED!", ReviveColor);
                ui.Log($"{Tag(defender)} defends with {defGame.DisplayName}: blocks {block}, takes <b>{taken}</b>.");

                if (reflect > 0)
                {
                    int r = ghost.TakeDamage(reflect);
                    stage.Hit(ghost);
                    Popup(ghost, $"REFLECT -{r}", GambleGame.Gold);
                    ui.Log($"Jackpot defence! {Tag(defender)} reflects {r} back at the ghost!");
                }
                if (defender.IsDown) ui.Log($"{Tag(defender)} is BUSTED!");
                ui.Refresh();
                yield return new WaitForSeconds(0.8f);
            }
        }

        IEnumerator CoverWindow(Combatant target, int incoming, List<Combatant> alive, Action<Combatant> setDefender)
        {
            var helpers = alive.Where(p => p != target).ToList();
            if (helpers.Count == 0) yield break;

            Combatant cover = null;
            ui.SetPrompt($"{incoming} damage incoming at {target.Name}! Teammates: COVER with your number!", GambleGame.BustRed);
            ui.ShowCoverButtons(helpers, c => cover = c);
            for (float t = 0; t < config.coverWindowSeconds && cover == null; t += Time.deltaTime)
            {
                ui.SetCoverTimer(config.coverWindowSeconds - t);
                foreach (var h in helpers)
                    if (NumberPressed(h.Index)) cover = h;
                yield return null;
            }
            ui.HideCoverButtons();
            if (cover != null)
            {
                ui.Log($"{Tag(cover)} dives in front of {Tag(target)}!");
                setDefender(cover);
            }
        }

        // ---------- Shared helpers ----------

        IEnumerator ChooseGame(bool allowRevive, Action<int> chosen)
        {
            int pick = -1;
            var options = new List<CombatUI.Choice>();
            for (int i = 0; i < games.Count; i++)
            {
                int idx = i;
                options.Add(new CombatUI.Choice($"[{i + 1}] {games[i].DisplayName}", games[i].Blurb, games[i].Accent, () => pick = idx));
            }
            if (allowRevive)
                options.Add(new CombatUI.Choice($"[{games.Count + 1}] Revive", $"Skip attack, revive a teammate (+{config.reviveHP} HP)",
                    ReviveColor, () => pick = games.Count));

            ui.ShowChoices(options);
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
            Focus(null);
            ui.SetPrompt(victory ? "The ghost is banished!" : "Everyone went bust...", victory ? GambleGame.Gold : GambleGame.BustRed);
            yield return new WaitForSeconds(1.2f);
            ui.ShowEnd(victory, round, () => SceneManager.LoadScene(SceneManager.GetActiveScene().name));
        }

        void Focus(Combatant c)
        {
            ui.SetActive(c);
            stage.SetActive(c);
        }

        void Popup(Combatant c, string text, Color color) => ui.Popup(stage.Cam, stage.PopupPoint(c), text, color);

        string ShortGhost => ghostName.Split(',')[0];

        static string Tag(Combatant c) => $"<color=#{ColorUtility.ToHtmlStringRGB(c.Color)}>{(c.IsPlayer ? c.Name : c.Name.Split(',')[0])}</color>";

        static GambleContext Context(Combatant c, GambleMode mode, bool ai) =>
            new GambleContext { Mode = mode, IsAI = ai, ActorName = c.IsPlayer ? c.Name : c.Name.Split(',')[0], ActorColor = c.Color };

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
