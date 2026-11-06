using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace HauntedCasino.UI
{
    /// <summary>Helpers to build themed uGUI from code: Deco frames, pills, TMP labels, key caps.</summary>
    public static class UIFactory
    {
        static UITheme T => UITheme.Current;

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

        /// <summary>Plain flat image stretched over its parent.</summary>
        public static Image Panel(Transform parent, string name, Color color, Sprite sprite = null, float slicedScale = 1f)
        {
            var rt = Rect(parent, name);
            rt.Anchor(0, 0, 1, 1);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                img.pixelsPerUnitMultiplier = slicedScale;
            }
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Art Deco panel: chamfered body plus a double gold rule. Returns the body image.</summary>
        public static Image Deco(Transform parent, string name, Color fill, Color? border = null, float slicedScale = 1f)
        {
            var body = Panel(parent, name, fill, DecoSprites.FrameFill, slicedScale);
            var rule = Panel(body.transform, "Border", border ?? T.gold, DecoSprites.FrameBorder, slicedScale);
            rule.raycastTarget = false;
            return body;
        }

        /// <summary>The gold rule image of a Deco panel or pill (to recolour it).</summary>
        public static Image BorderOf(Component panel)
        {
            var t = panel.transform.Find("Border");
            return t != null ? t.GetComponent<Image>() : null;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, int size, Color color,
            TextAnchor align = TextAnchor.MiddleCenter, bool display = false)
        {
            var rt = Rect(parent, "Label");
            rt.Anchor(0, 0, 1, 1);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            var font = display ? T.Display : T.Body;
            if (font != null) t.font = font;
            var shadow = T.ShadowMaterial(display);
            if (shadow != null) t.fontSharedMaterial = shadow;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = Align(align);
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Themed pill button with gold rim and hover/press motion.</summary>
        public static Button Button(Transform parent, string label, Color color, UnityAction onClick, int fontSize = 24, bool display = false)
        {
            var img = Panel(parent, "Button", color, DecoSprites.PillFill, 1.5f);
            img.raycastTarget = true;
            var rim = Panel(img.transform, "Border", T.gold * new Color(1f, 1f, 1f, 0.75f), DecoSprites.PillBorder, 1.5f);
            rim.raycastTarget = false;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            if (onClick != null) btn.onClick.AddListener(onClick);
            var text = Label(img.transform, label, fontSize, T.text, TextAnchor.MiddleCenter, display);
            text.rectTransform.Anchor(0, 0, 1, 1, 6);
            img.gameObject.AddComponent<UIButtonFX>();
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

        /// <summary>Inline key-cap for prompts, e.g. Key("1") or Key("Space").</summary>
        public static string Key(string key) =>
            $"<nobr><mark={UITheme.Hex(new Color(T.gold.r, T.gold.g, T.gold.b, 0.28f))} padding=\"7,7,3,3\"><color={UITheme.Hex(T.goldBright)}><b>{key}</b></color></mark></nobr>";

        public static string Colored(string text, Color c) => $"<color={UITheme.Hex(c)}>{text}</color>";

        public static TextAlignmentOptions Align(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
        }
    }
}
