using System;
using System.Collections;
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
        public static readonly Color Gold = new Color(1f, 0.82f, 0.3f);
        public static readonly Color BustRed = new Color(1f, 0.3f, 0.3f);

        public abstract string DisplayName { get; }
        public abstract string Blurb { get; }
        public abstract Color Accent { get; }

        protected RectTransform Root { get; private set; }
        protected Text ResultText { get; private set; }
        Text titleText;
        bool primaryRequested;

        public void Build(RectTransform host)
        {
            var bg = UIFactory.Panel(host, DisplayName + "Game", new Color(0.06f, 0.03f, 0.07f, 0.93f));
            Root = bg.rectTransform;
            titleText = UIFactory.Label(Root, DisplayName.ToUpper(), 30, Accent, TextAnchor.UpperCenter);
            titleText.rectTransform.Anchor(0, 0.84f, 1, 1, 10);
            ResultText = UIFactory.Label(Root, "", 30, Color.white);
            ResultText.rectTransform.Anchor(0, 0, 1, 0.2f, 8);
            BuildUI(Root);
            Root.gameObject.SetActive(false);
        }

        protected abstract void BuildUI(RectTransform root);
        protected abstract IEnumerator Run(GambleContext ctx, Action<GambleOutcome> done);

        public IEnumerator Play(GambleContext ctx, Action<GambleOutcome> done)
        {
            Root.gameObject.SetActive(true);
            Root.SetAsLastSibling();
            string verb = ctx.Mode == GambleMode.Attack ? "ATTACK" : "DEFENCE";
            titleText.text = $"{DisplayName.ToUpper()} — {verb}  <size=22>({ctx.ActorName})</size>";
            ResultText.text = "";
            ResultText.color = Color.white;
            // Skip a frame so the key that picked this game isn't read as game input.
            yield return null;
            primaryRequested = false;

            var outcome = default(GambleOutcome);
            yield return Run(ctx, o => outcome = o);

            ResultText.text = outcome.Label;
            ResultText.color = outcome.Kind == OutcomeKind.Jackpot ? Gold
                : outcome.Kind == OutcomeKind.Bust ? BustRed : Color.white;
            yield return new WaitForSeconds(1.1f);
            Root.gameObject.SetActive(false);
            done(outcome);
        }

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
    }
}
