using UnityEngine;

namespace HauntedCasino.UI
{
    /// <summary>
    /// Art Deco UI sprites generated in code (white, tint with Image.color), so the prototype
    /// needs no image assets: chamfered frames with double gold lines, pills, poker chips,
    /// a sunburst, a vignette and a bar gradient. Swap for painted art later.
    /// </summary>
    public static class DecoSprites
    {
        static Sprite frameFill, frameBorder, pillFill, pillBorder, chipBase, chipMarks, sunburst, vignette, barShade, diamond, glow;

        /// <summary>Chamfered panel body (9-sliced).</summary>
        public static Sprite FrameFill => frameFill ??= Chamfer(96, 14f, d => Alpha(d, 0f, 999f), 24);
        /// <summary>Double gold rule that follows the chamfer (9-sliced).</summary>
        public static Sprite FrameBorder => frameBorder ??= Chamfer(96, 14f, d => Mathf.Max(Alpha(d, 0f, 3f), Alpha(d, 6f, 7.5f) * 0.8f), 24);
        public static Sprite PillFill => pillFill ??= Rounded(64, 18f, d => Alpha(d, 0f, 999f), 22);
        public static Sprite PillBorder => pillBorder ??= Rounded(64, 18f, d => Alpha(d, 0f, 2.5f), 22);
        public static Sprite ChipBase => chipBase ??= Circle(128, (r, a) => r <= 1f ? 1f : 0f);
        public static Sprite ChipMarks => chipMarks ??= Circle(128, ChipMarksAlpha);
        public static Sprite Sunburst => sunburst ??= Circle(256, SunburstAlpha);
        public static Sprite Vignette => vignette ??= Circle(256, (r, a) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.45f, r)));
        public static Sprite Glow => glow ??= Circle(128, (r, a) => Mathf.Pow(Mathf.Clamp01(1f - r), 2f));
        public static Sprite Diamond => diamond ??= Make(32, 32, (x, y) =>
        {
            float d = Mathf.Abs(x - 15.5f) + Mathf.Abs(y - 15.5f);
            return Mathf.Clamp01(15.5f - d);
        }, Vector4.zero);
        /// <summary>Vertical shading for bar fills: lighter top, darker bottom.</summary>
        public static Sprite BarShade => barShade ??= Make(4, 32, (x, y) => 1f, Vector4.zero, (x, y) =>
        {
            float t = y / 31f;
            float v = Mathf.Lerp(0.62f, 1f, t) + (t > 0.78f && t < 0.88f ? 0.25f : 0f);
            return new Color(v, v, v, 1f);
        });

        // ---------- Shapes ----------

        static float Alpha(float d, float from, float to) => Mathf.Clamp01(d - from + 0.5f) * Mathf.Clamp01(to - d + 0.5f);

        static Sprite Chamfer(int size, float cut, System.Func<float, float> shade, int border)
        {
            float max = size - 1;
            return Make(size, size, (x, y) =>
            {
                float d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(max - x, max - y));
                const float k = 0.70710678f;
                d = Mathf.Min(d, (x + y - cut) * k);
                d = Mathf.Min(d, ((max - x) + y - cut) * k);
                d = Mathf.Min(d, (x + (max - y) - cut) * k);
                d = Mathf.Min(d, ((max - x) + (max - y) - cut) * k);
                return d < -0.5f ? 0f : shade(d);
            }, new Vector4(border, border, border, border));
        }

        static Sprite Rounded(int size, float radius, System.Func<float, float> shade, int border)
        {
            float max = size - 1;
            return Make(size, size, (x, y) =>
            {
                float cx = Mathf.Clamp(x, radius, max - radius);
                float cy = Mathf.Clamp(y, radius, max - radius);
                float d = radius - Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                d = Mathf.Min(d, Mathf.Min(Mathf.Min(x, y), Mathf.Min(max - x, max - y)));
                return d < -0.5f ? 0f : shade(d);
            }, new Vector4(border, border, border, border));
        }

        /// <summary>r: 0 centre to 1 edge; a: angle in radians.</summary>
        static Sprite Circle(int size, System.Func<float, float, float> shade)
        {
            float half = (size - 1) * 0.5f;
            return Make(size, size, (x, y) =>
            {
                float dx = (x - half) / half, dy = (y - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                return shade(r, Mathf.Atan2(dy, dx));
            }, Vector4.zero);
        }

        static float ChipMarksAlpha(float r, float a)
        {
            if (r > 1f) return 0f;
            // Six white edge inserts, an inner ring and a soft centre disc.
            float seg = Mathf.Repeat(a / (Mathf.PI * 2f) * 6f, 1f);
            float edge = r > 0.78f && r < 0.98f && seg < 0.45f ? 1f : 0f;
            float ring = Mathf.Abs(r - 0.66f) < 0.035f ? 0.9f : 0f;
            float centre = r < 0.5f ? 0.18f : 0f;
            return Mathf.Max(edge, Mathf.Max(ring, centre));
        }

        static float SunburstAlpha(float r, float a)
        {
            float rays = Mathf.Repeat(a / (Mathf.PI * 2f) * 24f, 1f);
            float ray = rays < 0.5f ? 1f : 0f;
            float fade = Mathf.Clamp01(1f - r);
            return ray * fade * fade;
        }

        static Sprite Make(int w, int h, System.Func<int, int, float> alpha, Vector4 border, System.Func<int, int, Color> color = null)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = color != null ? color(x, y) : Color.white;
                c.a *= Mathf.Clamp01(alpha(x, y));
                px[y * w + x] = c;
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }
    }
}
