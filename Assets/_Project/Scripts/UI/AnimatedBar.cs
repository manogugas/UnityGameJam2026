using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HauntedCasino.UI
{
    /// <summary>
    /// HP/chips bar: the fill slides to the new value, a pale "damage chunk" lingers then drains,
    /// and the number counts up or down.
    /// </summary>
    public class AnimatedBar : MonoBehaviour
    {
        RectTransform fill, lag;
        TextMeshProUGUI label;
        string suffix = "";
        int max = 1, target;
        float shown, lagValue, number, lagHold;

        public static AnimatedBar Create(Transform parent, Color fillColor, int fontSize = 20, string suffix = "")
        {
            var t = UITheme.Current;
            var bg = UIFactory.Panel(parent, "Bar", new Color(0f, 0f, 0f, 0.7f), DecoSprites.PillFill, 3f);
            var lagImg = UIFactory.Panel(bg.transform, "Lag", new Color(1f, 0.95f, 0.85f, 0.85f), DecoSprites.PillFill, 3f);
            lagImg.rectTransform.Anchor(0, 0, 1, 1, 3);
            var fillImg = UIFactory.Panel(bg.transform, "Fill", fillColor, DecoSprites.PillFill, 3f);
            fillImg.rectTransform.Anchor(0, 0, 1, 1, 3);
            var shine = UIFactory.Panel(fillImg.transform, "Shine", new Color(1f, 1f, 1f, 0.18f));
            shine.rectTransform.Anchor(0.01f, 0.55f, 0.99f, 0.85f);
            UIFactory.Panel(bg.transform, "Border", t.gold * new Color(1f, 1f, 1f, 0.55f), DecoSprites.PillBorder, 3f);
            var text = UIFactory.Label(bg.transform, "", fontSize, t.text, TextAnchor.MiddleCenter, true);

            var bar = bg.gameObject.AddComponent<AnimatedBar>();
            bar.fill = fillImg.rectTransform;
            bar.lag = lagImg.rectTransform;
            bar.label = text;
            bar.suffix = suffix;
            return bar;
        }

        public RectTransform Root => (RectTransform)transform;

        public void Set(int current, int maxValue, bool instant = false)
        {
            max = Mathf.Max(1, maxValue);
            if (current < target) lagHold = 0.35f;
            target = current;
            if (instant)
            {
                shown = lagValue = Pct(current);
                number = current;
                Apply();
            }
        }

        float Pct(float v) => Mathf.Clamp01(v / max);

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float goal = Pct(target);
            shown = Mathf.MoveTowards(shown, goal, dt * 2.5f);
            if (lagHold > 0f) lagHold -= dt;
            else lagValue = Mathf.MoveTowards(lagValue, shown, dt * 0.6f);
            if (lagValue < shown) lagValue = shown;
            number = Mathf.MoveTowards(number, target, dt * Mathf.Max(20f, Mathf.Abs(target - number) * 6f));
            Apply();
        }

        void Apply()
        {
            fill.anchorMax = new Vector2(shown, 1f);
            fill.gameObject.SetActive(shown > 0.005f);
            lag.anchorMax = new Vector2(lagValue, 1f);
            lag.gameObject.SetActive(lagValue > shown + 0.002f);
            label.text = $"{Mathf.RoundToInt(number)} / {max}{suffix}";
        }
    }
}
