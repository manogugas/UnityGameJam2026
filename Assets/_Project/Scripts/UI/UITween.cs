using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HauntedCasino.UI
{
    /// <summary>Tiny coroutine tweens on unscaled time (they keep running during hit-stop).</summary>
    public class UITween : MonoBehaviour
    {
        static UITween runner;
        readonly Dictionary<(Object, int), Coroutine> running = new Dictionary<(Object, int), Coroutine>();
        readonly Dictionary<RectTransform, Vector2> homes = new Dictionary<RectTransform, Vector2>();

        const int ScaleChannel = 0, MoveChannel = 1, FadeChannel = 2;

        static UITween Runner
        {
            get
            {
                if (runner == null) runner = new GameObject("UITween").AddComponent<UITween>();
                return runner;
            }
        }

        /// <summary>Scale up from zero with a little overshoot.</summary>
        public static void Pop(Transform t, float delay = 0f, float duration = 0.3f) =>
            Runner.Run(t, ScaleChannel, Runner.ScaleRoutine(t, 0f, 1f, delay, duration, true));

        /// <summary>Quick scale bump for emphasis.</summary>
        public static void Punch(Transform t, float amount = 0.15f, float duration = 0.25f) =>
            Runner.Run(t, ScaleChannel, Runner.PunchRoutine(t, amount, duration));

        /// <summary>Slide from an offset to the element's resting position.</summary>
        public static void SlideIn(RectTransform rt, Vector2 fromOffset, float duration = 0.3f, float delay = 0f)
        {
            var r = Runner;
            if (!r.homes.ContainsKey(rt)) r.homes[rt] = rt.anchoredPosition;
            r.Run(rt, MoveChannel, r.SlideRoutine(rt, r.homes[rt] + fromOffset, r.homes[rt], duration, delay));
        }

        /// <summary>Move to resting position plus an offset (e.g. lift a card).</summary>
        public static void MoveTo(RectTransform rt, Vector2 offset, float duration = 0.2f)
        {
            var r = Runner;
            if (!r.homes.ContainsKey(rt)) r.homes[rt] = rt.anchoredPosition;
            r.Run(rt, MoveChannel, r.SlideRoutine(rt, rt.anchoredPosition, r.homes[rt] + offset, duration, 0f));
        }

        public static void Fade(CanvasGroup g, float to, float duration = 0.25f, float delay = 0f) =>
            Runner.Run(g, FadeChannel, Runner.FadeRoutine(g, to, duration, delay));

        public static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        public static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

        void Run(Object target, int channel, IEnumerator routine)
        {
            if (target == null) return;
            var key = (target, channel);
            if (running.TryGetValue(key, out var c) && c != null) StopCoroutine(c);
            running[key] = StartCoroutine(routine);
        }

        IEnumerator ScaleRoutine(Transform t, float from, float to, float delay, float duration, bool overshoot)
        {
            t.localScale = Vector3.one * from;
            for (float d = 0; d < delay; d += Time.unscaledDeltaTime) yield return null;
            for (float e = 0; e < duration; e += Time.unscaledDeltaTime)
            {
                if (t == null) yield break;
                float k = e / duration;
                float s = Mathf.LerpUnclamped(from, to, overshoot ? EaseOutBack(k) : EaseOutCubic(k));
                t.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            if (t != null) t.localScale = Vector3.one * to;
        }

        IEnumerator PunchRoutine(Transform t, float amount, float duration)
        {
            for (float e = 0; e < duration; e += Time.unscaledDeltaTime)
            {
                if (t == null) yield break;
                float s = 1f + Mathf.Sin(e / duration * Mathf.PI) * amount;
                t.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            if (t != null) t.localScale = Vector3.one;
        }

        IEnumerator SlideRoutine(RectTransform rt, Vector2 from, Vector2 to, float duration, float delay)
        {
            rt.anchoredPosition = from;
            for (float d = 0; d < delay; d += Time.unscaledDeltaTime) yield return null;
            for (float e = 0; e < duration; e += Time.unscaledDeltaTime)
            {
                if (rt == null) yield break;
                rt.anchoredPosition = Vector2.LerpUnclamped(from, to, EaseOutCubic(e / duration));
                yield return null;
            }
            if (rt != null) rt.anchoredPosition = to;
        }

        IEnumerator FadeRoutine(CanvasGroup g, float to, float duration, float delay)
        {
            for (float d = 0; d < delay; d += Time.unscaledDeltaTime) yield return null;
            float from = g.alpha;
            for (float e = 0; e < duration; e += Time.unscaledDeltaTime)
            {
                if (g == null) yield break;
                g.alpha = Mathf.Lerp(from, to, e / duration);
                yield return null;
            }
            if (g != null) g.alpha = to;
        }
    }
}
