using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace HauntedCasino.Combat
{
    /// <summary>Tiny helpers to build uGUI from code so the prototype needs no prefabs.</summary>
    public static class UIFactory
    {
        static Font font;

        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Anchor(this RectTransform rt, float minX, float minY, float maxX, float maxY, float pad = 0f)
        {
            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
            return rt;
        }

        public static Image Panel(Transform parent, string name, Color color)
        {
            var rt = Rect(parent, name);
            rt.Anchor(0, 0, 1, 1);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var rt = Rect(parent, "Label");
            rt.Anchor(0, 0, 1, 1);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var shadow = rt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.8f);
            shadow.effectDistance = new Vector2(2, -2);
            return t;
        }

        public static Button Button(Transform parent, string label, Color color, UnityAction onClick, int fontSize = 26)
        {
            var img = Panel(parent, "Button", color);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(onClick);
            var text = Label(img.transform, label, fontSize, Color.white);
            text.rectTransform.Anchor(0, 0, 1, 1, 6);
            return btn;
        }

        public static HorizontalLayoutGroup Row(RectTransform rt, float spacing)
        {
            var row = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = spacing;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;
            return row;
        }
    }

    /// <summary>Simple fill bar with a value label.</summary>
    public class UIBar
    {
        public readonly RectTransform Root;
        readonly RectTransform fill;
        readonly Text text;

        public UIBar(Transform parent, Color fillColor, int fontSize = 20)
        {
            Root = UIFactory.Panel(parent, "Bar", new Color(0, 0, 0, 0.6f)).rectTransform;
            fill = UIFactory.Panel(Root, "Fill", fillColor).rectTransform;
            fill.Anchor(0, 0, 1, 1, 3);
            fill.pivot = new Vector2(0, 0.5f);
            text = UIFactory.Label(Root, "", fontSize, Color.white);
        }

        public void Set(int current, int max)
        {
            float pct = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;
            fill.anchorMax = new Vector2(pct, 1f);
            fill.gameObject.SetActive(pct > 0f);
            text.text = $"{current} / {max}";
        }
    }
}
