using System;
using System.Collections;
using HauntedCasino.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HauntedCasino.Combat
{
    /// <summary>
    /// Base for every casino minigame. Players and ghosts play the exact same game;
    /// the ghost just plays it with IsAI = true. Add a new game by subclassing this.
    /// </summary>
    public abstract class GambleGame : MonoBehaviour
    {
        public static Color Gold => UITheme.Current.goldBright;
        public static Color BustRed => UITheme.Current.target;
        protected static UITheme Theme => UITheme.Current;

        public abstract string DisplayName { get; }
        public abstract string Blurb { get; }
        public abstract Color Accent { get; }

        /// <summary>True once the result is decided (side bets lock at this point).</summary>
        public bool Resolved { get; private set; }

        protected RectTransform Root { get; private set; }
        protected TextMeshProUGUI ResultText { get; private set; }
        TextMeshProUGUI titleText, subtitleText;
        Image background, rim, roleStripe;
        bool primaryRequested;

        public void Build(RectTransform host)
        {
            background = UIFactory.Deco(host, DisplayName + "Game", Theme.panel);
            Root = background.rectTransform;
            rim = UIFactory.BorderOf(background);
            roleStripe = UIFactory.Panel(Root, "RoleStripe", Theme.attack);
            roleStripe.rectTransform.Anchor(0.3f, 0.955f, 0.7f, 0.965f);
            titleText = UIFactory.Label(Root, DisplayName.ToUpper(), Theme.title - 4, Accent, TextAnchor.MiddleCenter, true);
            titleText.rectTransform.Anchor(0.03f, 0.86f, 0.97f, 0.95f);
            subtitleText = UIFactory.Label(Root, "", Theme.small, Theme.textDim, TextAnchor.MiddleCenter);
            subtitleText.rectTransform.Anchor(0.03f, 0.8f, 0.97f, 0.865f);
            ResultText = UIFactory.Label(Root, "", Theme.body + 4, Theme.text, TextAnchor.MiddleCenter, true);
            ResultText.rectTransform.Anchor(0.03f, 0.03f, 0.97f, 0.2f);
            BuildUI(Root);
            Root.gameObject.SetActive(false);
        }

        protected abstract void BuildUI(RectTransform root);
        protected abstract IEnumerator Run(GambleContext ctx, Action<GambleOutcome> done);

        public IEnumerator Play(GambleContext ctx, Action<GambleOutcome> done)
        {
            Resolved = false;
            Root.gameObject.SetActive(true);
            Root.SetAsLastSibling();
            UITween.SlideIn(Root, new Vector2(-260f, 0f), 0.28f);

            bool attack = ctx.Mode == GambleMode.Attack;
            var roleColor = attack ? Theme.attack : Theme.defend;
            rim.color = Color.Lerp(Theme.gold, roleColor, 0.55f);
            roleStripe.color = roleColor;
            background.color = Color.Lerp(Theme.panel, roleColor, 0.1f);
            string actor = UIFactory.Colored(ctx.ActorName, ctx.ActorColor);
            string opponent = UIFactory.Colored(ctx.OpponentName, ctx.OpponentColor);
            titleText.text = $"{DisplayName.ToUpper()}  <size=70%>{(attack ? "ATTACK" : "DEFENCE")}</size>";
            titleText.color = Color.Lerp(Accent, roleColor, 0.35f);
            string stake = ctx.StakeMultiplier > 1f ? UIFactory.Colored($"   ·   stake x{ctx.StakeMultiplier:0.#}", Theme.goldBright) : "";
            subtitleText.text = attack
                ? $"{actor} rolls to hit {opponent}{stake}"
                : $"{actor} blocks {opponent}'s <b>{ctx.Incoming}</b> dmg   ·   block = result x{ctx.DefenceMultiplier:0.#}";
            ResultText.text = "";
            ResultText.color = Theme.text;
            // Skip a frame so the key that picked this game isn't read as game input.
            yield return null;
            primaryRequested = false;

            var outcome = default(GambleOutcome);
            yield return Run(ctx, o => outcome = o);
            Resolved = true;

            ResultText.text = outcome.Label;
            ResultText.color = outcome.Kind == OutcomeKind.Jackpot ? Gold
                : outcome.Kind == OutcomeKind.Bust ? BustRed : Theme.text;
            UITween.Punch(ResultText.transform, outcome.Kind == OutcomeKind.Normal ? 0.12f : 0.3f, 0.35f);
            if (outcome.Kind == OutcomeKind.Jackpot) CasinoSfx.Play(Sfx.Jackpot);
            else if (outcome.Kind == OutcomeKind.Bust) CasinoSfx.Play(Sfx.Bust);
            yield return new WaitForSeconds(1.1f);
            Root.gameObject.SetActive(false);
            done(outcome);
        }

        /// <summary>Instruction line inside the panel with key caps, e.g. "Press [Space] to stop".</summary>
        protected static string KeyCap(string k) => UIFactory.Key(k);

        protected void RequestPrimary() => primaryRequested = true;

        /// <summary>True when a human pressed Space or the on-screen action button this frame.</summary>
        protected bool ConsumePrimary(GambleContext ctx)
        {
            if (ctx.IsAI) return false;
            bool pressed = primaryRequested || KeyDown(Key.Space);
            primaryRequested = false;
            return pressed;
        }

        protected static bool KeyDown(Key key) => Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;

        static readonly Key[] DigitKeys = { UnityEngine.InputSystem.Key.Digit1, UnityEngine.InputSystem.Key.Digit2, UnityEngine.InputSystem.Key.Digit3, UnityEngine.InputSystem.Key.Digit4 };
        static readonly Key[] NumpadKeys = { UnityEngine.InputSystem.Key.Numpad1, UnityEngine.InputSystem.Key.Numpad2, UnityEngine.InputSystem.Key.Numpad3, UnityEngine.InputSystem.Key.Numpad4 };

        /// <summary>Number key for a player slot (0 = key 1).</summary>
        protected static bool NumberKeyDown(int index) =>
            index >= 0 && index < DigitKeys.Length && (KeyDown(DigitKeys[index]) || KeyDown(NumpadKeys[index]));

        /// <summary>Slot-style reel window: dark inset with a gold rim.</summary>
        protected static Image ReelWindow(Transform parent, string name) =>
            UIFactory.Deco(parent, name, new Color(0.05f, 0.03f, 0.05f, 1f), Theme.gold * new Color(1, 1, 1, 0.7f), 2f);
    }
}
