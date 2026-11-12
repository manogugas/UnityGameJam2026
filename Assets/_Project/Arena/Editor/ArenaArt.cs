using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CarArena.EditorTools
{
    /// <summary>
    /// Procedural 2D art for the arena, drawn with signed-distance shapes and saved as PNGs:
    /// white tablet icons (tinted in the UI), the red banner with a gold laurel wreath, and a brick wall.
    /// Re-generated on every scene build, so tweak the shapes here.
    /// </summary>
    public static class ArenaArt
    {
        const string Dir = "Assets/_Project/Arena/Art";

        // ---------------------------------------------------------------- SDF helpers (p in -1..1)

        static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;
        static float Ring(Vector2 p, Vector2 c, float r, float w) => Mathf.Abs((p - c).magnitude - r) - w;

        static float Segment(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / ba.sqrMagnitude);
            return (pa - ba * h).magnitude - r;
        }

        static float Box(Vector2 p, Vector2 c, Vector2 half, float angleDeg = 0f)
        {
            Vector2 q = Rot(p - c, -angleDeg * Mathf.Deg2Rad);
            Vector2 d = new Vector2(Mathf.Abs(q.x) - half.x, Mathf.Abs(q.y) - half.y);
            return new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f);
        }

        /// <summary>Leaf: an ellipse along 'angle'.</summary>
        static float Leaf(Vector2 p, Vector2 c, float len, float wid, float angleRad)
        {
            Vector2 q = Rot(p - c, -angleRad);
            q.x /= len;
            q.y /= wid;
            return (q.magnitude - 1f) * wid;
        }

        /// <summary>Convex polygon (counter-clockwise points), approximate distance.</summary>
        static float Poly(Vector2 p, params Vector2[] v)
        {
            float d = float.MinValue;
            for (int i = 0; i < v.Length; i++)
            {
                Vector2 a = v[i], b = v[(i + 1) % v.Length];
                Vector2 e = (b - a).normalized;
                Vector2 n = new Vector2(e.y, -e.x); // outward for CCW
                d = Mathf.Max(d, Vector2.Dot(p - a, n));
            }
            return d;
        }

        static Vector2 Rot(Vector2 v, float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        static float Union(params float[] d)
        {
            float m = float.MaxValue;
            foreach (var x in d) m = Mathf.Min(m, x);
            return m;
        }

        static float Cut(float a, float b) => Mathf.Max(a, -b);

        // ---------------------------------------------------------------- icons

        static float Wrench(Vector2 p)
        {
            Vector2 dir = new Vector2(1f, 1f).normalized;
            float handle = Segment(p, -dir * 0.62f, dir * 0.3f, 0.12f);
            float head = Circle(p, dir * 0.45f, 0.33f);
            float jaw = Union(Circle(p, dir * 0.66f, 0.2f), Box(p, dir * 0.78f, new Vector2(0.11f, 0.3f), -45f));
            float end = Circle(p, -dir * 0.62f, 0.17f);
            return Cut(Union(handle, head, end), Union(jaw, Circle(p, -dir * 0.62f, 0.07f)));
        }

        static float Tyre(Vector2 p)
        {
            float tyre = Ring(p, Vector2.zero, 0.72f, 0.17f);
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                tyre = Cut(tyre, Box(p, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.9f, new Vector2(0.05f, 0.08f), a * Mathf.Rad2Deg));
            }
            float rim = Ring(p, Vector2.zero, 0.42f, 0.05f);
            float hub = Circle(p, Vector2.zero, 0.13f);
            float spokes = float.MaxValue;
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * Mathf.PI * 2f + 0.3f;
                spokes = Mathf.Min(spokes, Segment(p, Vector2.zero, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.42f, 0.045f));
            }
            return Union(tyre, rim, hub, spokes);
        }

        static float Engine(Vector2 p)
        {
            float block = Box(p, new Vector2(0f, -0.12f), new Vector2(0.58f, 0.38f));
            float intake = Box(p, new Vector2(0f, 0.42f), new Vector2(0.34f, 0.1f));
            float stem = Box(p, new Vector2(0f, 0.3f), new Vector2(0.08f, 0.08f));
            float earL = Box(p, new Vector2(-0.72f, -0.05f), new Vector2(0.14f, 0.18f));
            float earR = Box(p, new Vector2(0.72f, -0.05f), new Vector2(0.14f, 0.18f));
            float feet = Union(Box(p, new Vector2(-0.38f, -0.6f), new Vector2(0.1f, 0.1f)), Box(p, new Vector2(0.38f, -0.6f), new Vector2(0.1f, 0.1f)));
            float s = Union(block, intake, stem, earL, earR, feet);
            for (int i = -1; i <= 1; i++) s = Cut(s, Box(p, new Vector2(i * 0.3f, -0.12f), new Vector2(0.05f, 0.24f)));
            return s;
        }

        static float Shield(Vector2 p)
        {
            float outer = Union(Box(p, new Vector2(0f, 0.25f), new Vector2(0.62f, 0.5f)), Circle(p, new Vector2(0f, -0.15f), 0.62f));
            outer = Mathf.Max(outer, Poly(p, new Vector2(0f, -0.92f), new Vector2(0.9f, 0.1f), new Vector2(0.9f, 0.9f), new Vector2(-0.9f, 0.9f), new Vector2(-0.9f, 0.1f)));
            outer = Mathf.Max(outer, p.y - 0.75f);
            float inner = outer + 0.12f;
            float stripe = Box(p, new Vector2(0f, 0f), new Vector2(0.07f, 0.9f));
            return Union(Cut(outer, inner), Mathf.Max(stripe, outer));
        }

        static float Ram(Vector2 p)
        {
            float bar = Box(p, new Vector2(0f, -0.45f), new Vector2(0.8f, 0.12f));
            float plate = Poly(p, new Vector2(-0.7f, -0.35f), new Vector2(0.7f, -0.35f), new Vector2(0.45f, 0.1f), new Vector2(-0.45f, 0.1f));
            float spikes = float.MaxValue;
            for (int i = -1; i <= 1; i++)
            {
                float x = i * 0.4f;
                spikes = Mathf.Min(spikes, Poly(p, new Vector2(x - 0.16f, 0.05f), new Vector2(x + 0.16f, 0.05f), new Vector2(x, 0.78f)));
            }
            return Union(bar, plate, spikes);
        }

        /// <summary>Boost: a flame behind two forward chevrons.</summary>
        static float BoostIcon(Vector2 p)
        {
            float d = float.MaxValue;
            for (int i = 0; i < 2; i++)
            {
                float x = -0.25f + i * 0.45f;
                d = Mathf.Min(d, Segment(p, new Vector2(x - 0.3f, 0.5f), new Vector2(x + 0.15f, 0f), 0.11f));
                d = Mathf.Min(d, Segment(p, new Vector2(x - 0.3f, -0.5f), new Vector2(x + 0.15f, 0f), 0.11f));
            }
            // Exhaust streaks trailing off to the left.
            d = Mathf.Min(d, Segment(p, new Vector2(-0.9f, 0.2f), new Vector2(-0.68f, 0.2f), 0.05f));
            d = Mathf.Min(d, Segment(p, new Vector2(-0.95f, -0.2f), new Vector2(-0.68f, -0.2f), 0.05f));
            return d;
        }

        /// <summary>Gun: crosshair ring, ticks and a centre dot.</summary>
        static float GunIcon(Vector2 p)
        {
            float d = Ring(p, Vector2.zero, 0.55f, 0.07f);
            d = Mathf.Min(d, Circle(p, Vector2.zero, 0.1f));
            d = Mathf.Min(d, Segment(p, new Vector2(0f, 0.32f), new Vector2(0f, 0.9f), 0.06f));
            d = Mathf.Min(d, Segment(p, new Vector2(0f, -0.32f), new Vector2(0f, -0.9f), 0.06f));
            d = Mathf.Min(d, Segment(p, new Vector2(0.32f, 0f), new Vector2(0.9f, 0f), 0.06f));
            d = Mathf.Min(d, Segment(p, new Vector2(-0.32f, 0f), new Vector2(-0.9f, 0f), 0.06f));
            return d;
        }

        /// <summary>Traction control: the dashboard symbol, a car with two wavy skid lines under it.</summary>
        static float TractionIcon(Vector2 p)
        {
            float d = Box(p, new Vector2(0f, 0.38f), new Vector2(0.42f, 0.16f));
            d = Mathf.Min(d, Poly(p, new Vector2(-0.26f, 0.5f), new Vector2(0.26f, 0.5f), new Vector2(0.16f, 0.72f), new Vector2(-0.16f, 0.72f)));
            d = Mathf.Min(d, Circle(p, new Vector2(-0.26f, 0.2f), 0.11f));
            d = Mathf.Min(d, Circle(p, new Vector2(0.26f, 0.2f), 0.11f));
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 6; i++)
                {
                    float y0 = -0.05f - i * 0.13f, y1 = y0 - 0.13f;
                    float x0 = side * 0.3f + (i % 2 == 0 ? -0.09f : 0.09f), x1 = side * 0.3f + (i % 2 == 0 ? 0.09f : -0.09f);
                    d = Mathf.Min(d, Segment(p, new Vector2(x0, y0), new Vector2(x1, y1), 0.045f));
                }
            return d;
        }

        static float Laurel(Vector2 p)
        {
            float d = float.MaxValue;
            const float r = 0.66f;
            for (int side = -1; side <= 1; side += 2)
            {
                // Stem arc from the bottom up each side.
                for (int i = 0; i < 9; i++)
                {
                    float t = i / 8f;
                    float a = Mathf.Lerp(-80f, 70f, t) * Mathf.Deg2Rad;
                    Vector2 c = new Vector2(side * Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                    Vector2 tangent = new Vector2(-side * Mathf.Sin(a), Mathf.Cos(a));
                    Vector2 normal = new Vector2(side * Mathf.Cos(a), Mathf.Sin(a));
                    float size = Mathf.Lerp(0.15f, 0.1f, t);
                    // Leaf pairs: outside and inside of the stem, angled forward.
                    Vector2 fo = (tangent + normal * 0.9f).normalized, fi = (tangent - normal * 0.9f).normalized;
                    d = Mathf.Min(d, Leaf(p, c + fo * size * 0.9f, size, size * 0.42f, Mathf.Atan2(fo.y, fo.x)));
                    d = Mathf.Min(d, Leaf(p, c + fi * size * 0.9f, size, size * 0.42f, Mathf.Atan2(fi.y, fi.x)));
                    if (i < 8)
                    {
                        float a2 = Mathf.Lerp(-80f, 70f, (i + 1) / 8f) * Mathf.Deg2Rad;
                        d = Mathf.Min(d, Segment(p, c, new Vector2(side * Mathf.Cos(a2) * r, Mathf.Sin(a2) * r), 0.025f));
                    }
                }
            }
            // Ribbon tie at the bottom.
            d = Mathf.Min(d, Circle(p, new Vector2(0f, -0.72f), 0.07f));
            d = Mathf.Min(d, Segment(p, new Vector2(0f, -0.72f), new Vector2(-0.2f, -0.92f), 0.04f));
            d = Mathf.Min(d, Segment(p, new Vector2(0f, -0.72f), new Vector2(0.2f, -0.92f), 0.04f));
            return d;
        }

        public static Sprite IconRepair() => IconSprite("IconRepair", Wrench);
        public static Sprite IconTyres() => IconSprite("IconTyres", Tyre);
        public static Sprite IconEngine() => IconSprite("IconEngine", Engine);
        public static Sprite IconArmour() => IconSprite("IconArmour", Shield);
        public static Sprite IconRam() => IconSprite("IconRam", Ram);
        public static Sprite IconLaurel() => IconSprite("IconLaurel", Laurel);
        public static Sprite IconBoost() => IconSprite("IconBoost", BoostIcon);
        public static Sprite IconGun() => IconSprite("IconGun", GunIcon);
        public static Sprite IconTraction() => IconSprite("IconTraction", TractionIcon);

        static Sprite IconSprite(string name, Func<Vector2, float> sdf)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float pixel = 2f / size;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f) * 1.08f;
                    float a = Mathf.Clamp01(0.5f - sdf(p) / pixel);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            string path = Save(tex, name);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ---------------------------------------------------------------- textures

        /// <summary>Hanging red banner: cloth folds, gold top band and border, laurel wreath, swallowtail bottom.</summary>
        public static Texture2D LaurelBanner()
        {
            const int w = 256, h = 512;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            var px = new Color[w * h];
            var red = new Color(0.55f, 0.06f, 0.05f);
            var gold = new Color(0.95f, 0.72f, 0.25f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    // Swallowtail: notch cut into the bottom.
                    if (v < 0.12f && Mathf.Abs(u - 0.5f) < (0.12f - v) * 2.4f) { px[y * w + x] = Color.clear; continue; }
                    float fold = 0.82f + 0.18f * Mathf.Sin(u * Mathf.PI * 6f) * Mathf.Sin(u * Mathf.PI * 2.3f + 0.6f);
                    Color c = red * fold;
                    c.a = 1f;
                    bool border = u < 0.05f || u > 0.95f || v > 0.9f && v < 0.94f;
                    if (border) c = gold * (0.85f + 0.15f * fold);
                    if (v > 0.94f) c = new Color(0.2f, 0.12f, 0.06f); // pole sleeve
                    // Laurel centred in the upper part (aspect-corrected).
                    var p = new Vector2((u - 0.5f) * 2f / 0.82f, (v - 0.6f) * 4f / 0.82f);
                    float d = Laurel(p);
                    float k = Mathf.Clamp01(0.5f - d / 0.012f);
                    c = Color.Lerp(c, gold * (0.9f + 0.1f * fold), k);
                    c.a = 1f;
                    px[y * w + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            string path = Save(tex, "BannerLaurel");
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Weathered brick wall (tileable).</summary>
        public static Texture2D Bricks()
        {
            const int size = 512;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            var px = new Color[size * size];
            var rng = new System.Random(11);
            const int rows = 16, cols = 8;
            var tint = new float[rows * cols];
            for (int i = 0; i < tint.Length; i++) tint[i] = 0.8f + (float)rng.NextDouble() * 0.3f;
            var mortar = new Color(0.36f, 0.32f, 0.27f);
            var brick = new Color(0.58f, 0.46f, 0.36f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fy = y / (float)size * rows;
                    int row = Mathf.FloorToInt(fy);
                    float fx = x / (float)size * cols + (row % 2) * 0.5f;
                    int col = Mathf.FloorToInt(fx) % cols;
                    float bx = fx - Mathf.Floor(fx), by = fy - row;
                    bool isMortar = bx < 0.04f || by < 0.08f;
                    float noise = Mathf.PerlinNoise(x * 0.05f, y * 0.05f) * 0.2f + Mathf.PerlinNoise(x * 0.3f, y * 0.3f) * 0.08f;
                    Color c = isMortar ? mortar * (0.9f + noise) : brick * (tint[row * cols + col] * (0.88f + noise));
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            string path = Save(tex, "Bricks");
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------------------------------------------------------------- tileable ground textures

        /// <summary>Value noise on a lattice that wraps every <paramref name="period"/> cells (so textures tile).</summary>
        static float PeriodicNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(x0, y0, period, seed), b = Hash(x0 + 1, y0, period, seed);
            float c = Hash(x0, y0 + 1, period, seed), d = Hash(x0 + 1, y0 + 1, period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Hash(int i, int j, int period, int seed)
        {
            i = ((i % period) + period) % period;
            j = ((j % period) + period) % period;
            uint n = (uint)(i * 374761393 + j * 668265263 + seed * 1442695041);
            n = (n ^ (n >> 13)) * 1274126177u;
            return ((n ^ (n >> 16)) & 0xffff) / 65535f;
        }

        /// <summary>Fractal noise over a 0..1 tile: octaves from <paramref name="basePeriod"/> cells up, each tiling.</summary>
        static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int period = basePeriod;
            for (int o = 0; o < octaves; o++)
            {
                sum += PeriodicNoise(u * period, v * period, period, seed + o * 31) * amp;
                norm += amp;
                amp *= 0.5f;
                period *= 2;
            }
            return sum / norm;
        }

        /// <summary>
        /// Large-scale sand colour (tile ~50 m): drifting light and dark patches, wheel-scuffed areas and a few
        /// long raked swirls. No fine grain here: that's the detail map, so neither layer shows an obvious repeat.
        /// </summary>
        public static Texture2D SandMacroTexture()
        {
            const int size = 1024;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            var px = new Color[size * size];
            var light = new Color(0.93f, 0.85f, 0.66f);
            var dark = new Color(0.82f, 0.72f, 0.54f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float broad = Fbm(u, v, 3, 6, 41);
                    float patches = Fbm(u + Fbm(u, v, 3, 2, 5) * 0.2f, v, 9, 5, 43); // warped so blobs aren't round
                    float k = 0.5f + (broad - 0.5f) * 0.9f + (patches - 0.5f) * 0.6f;
                    Color c = Color.Lerp(dark, light, Mathf.Clamp01(k));
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            return SaveTiled(tex, px, "SandMacro");
        }

        /// <summary>
        /// Sand grain for the URP detail map (grey around 0.5, multiplied x2 over the macro colour, tile ~4 m):
        /// fine grain, small pebbles and short wind ripples with no large shapes that would show a repeat.
        /// </summary>
        public static Texture2D SandDetailTexture()
        {
            const int size = 512;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            var px = new Color[size * size];
            var rng = new System.Random(13);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    // Only small-scale features (no blobs, no regular ripples): at a distance the grain mips
                    // down to flat grey and leaves the colour to the macro layer, so nothing reads as a tile.
                    float clump = Fbm(u, v, 32, 3, 51) - 0.5f;
                    float g = 0.5f + clump * 0.06f + ((float)rng.NextDouble() - 0.5f) * 0.07f;
                    if (rng.NextDouble() < 0.01) g -= 0.07f; // pebble
                    px[y * size + x] = new Color(g, g, g, 1f);
                }
            return SaveTiled(tex, px, "SandDetail", linear: true);
        }

        /// <summary>
        /// The square where the figure 8 crosses itself: its own surface (no road lines running through it), a
        /// yellow box-junction border and diagonal hatching. Tile = the whole crossing.
        /// </summary>
        public static Texture2D CrossingTexture()
        {
            const int size = 512;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            var px = new Color[size * size];
            var rng = new System.Random(17);
            var baseCol = new Color(0.27f, 0.27f, 0.29f);
            var yellow = new Color(0.95f, 0.75f, 0.12f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    Color c = baseCol * (0.85f + Fbm(u, v, 4, 4, 61) * 0.3f);
                    float speck = (float)rng.NextDouble();
                    if (speck < 0.06f) c *= 1.35f;
                    else if (speck < 0.1f) c *= 0.75f;
                    float edgeDist = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    bool border = edgeDist > 0.03f && edgeDist < 0.06f;
                    bool inside = edgeDist >= 0.06f;
                    bool hatch = inside && (Mathf.Repeat((u + v) * 7f, 1f) < 0.12f || Mathf.Repeat((u - v) * 7f, 1f) < 0.12f);
                    if (border || hatch)
                        c = Color.Lerp(c, yellow, Fbm(u, v, 32, 2, 67) > 0.28f ? 0.85f : 0.45f);
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            return SaveTiled(tex, px, "Crossing");
        }

        /// <summary>Arena sand (tileable): soft colour drift, raked ripples and grain.</summary>
        public static Texture2D SandTexture()
        {
            const int size = 512;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            var px = new Color[size * size];
            var rng = new System.Random(5);
            var light = new Color(0.86f, 0.76f, 0.57f);
            var dark = new Color(0.68f, 0.57f, 0.4f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float drift = Fbm(u, v, 3, 4, 1);
                    float clump = Fbm(u, v, 24, 3, 7);
                    // Raked ripples, bent by the noise so they aren't ruler-straight.
                    float ripple = Mathf.Sin((v * 22f + Fbm(u, v, 4, 2, 3) * 3f) * Mathf.PI * 2f) * 0.5f + 0.5f;
                    float k = 0.5f + (drift - 0.5f) * 0.45f + (clump - 0.5f) * 0.35f + (ripple - 0.5f) * 0.06f;
                    Color c = Color.Lerp(dark, light, Mathf.Clamp01(k * 1.3f - 0.15f));
                    float grain = ((float)rng.NextDouble() - 0.5f) * 0.07f;
                    if (rng.NextDouble() < 0.012) grain -= 0.12f; // dark pebbles
                    c *= 1f + grain;
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            return SaveTiled(tex, px, "Sand");
        }

        /// <summary>
        /// Road surface for the ribbon mesh (u across the road, v along it, one tile = 16 m): asphalt grain with
        /// light aggregate, a darker worn racing line and white edge lines.
        /// </summary>
        public static Texture2D RoadTexture()
        {
            const int size = 512;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            var px = new Color[size * size];
            var rng = new System.Random(9);
            var baseCol = new Color(0.25f, 0.25f, 0.27f);
            var paint = new Color(0.9f, 0.9f, 0.86f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float patch = Fbm(u, v, 4, 4, 11);
                    Color c = baseCol * (0.82f + patch * 0.35f);
                    float speck = (float)rng.NextDouble();
                    if (speck < 0.06f) c *= 1.35f;        // light aggregate
                    else if (speck < 0.1f) c *= 0.75f;    // dark pits
                    // Rubbered-in racing line: two soft dark bands where the wheels run.
                    float wear = Mathf.Exp(-Mathf.Pow((u - 0.35f) / 0.07f, 2f)) + Mathf.Exp(-Mathf.Pow((u - 0.65f) / 0.07f, 2f));
                    c *= 1f - 0.12f * wear;
                    // Paint: solid edge lines only (a race track has no centre line). Worn by noise.
                    bool edge = (u > 0.035f && u < 0.06f) || (u > 0.94f && u < 0.965f);
                    if (edge)
                    {
                        float worn = Fbm(u, v, 32, 2, 17);
                        c = Color.Lerp(c, paint, worn > 0.3f ? 0.92f : 0.5f);
                    }
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            return SaveTiled(tex, px, "Road");
        }

        /// <param name="linear">Detail (x2) maps must be linear: an sRGB 0.5 grey samples as ~0.21 and darkens everything.</param>
        static Texture2D SaveTiled(Texture2D tex, Color[] px, string name, bool linear = false)
        {
            tex.SetPixels(px);
            tex.Apply();
            string path = Save(tex, name);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.anisoLevel = 8;
            imp.sRGBTexture = !linear;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static string Save(Texture2D tex, string name)
        {
            Directory.CreateDirectory(Dir);
            string path = $"{Dir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return path;
        }
    }
}
