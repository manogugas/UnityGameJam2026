using TMPro;
using UnityEngine;

namespace HauntedCasino.UI
{
    /// <summary>
    /// Haunted Art Deco look: fonts, palette and text sizes in one asset you can tweak in the Inspector.
    /// Assign it on the CombatManager; a default is generated if none is set.
    /// </summary>
    [CreateAssetMenu(menuName = "Haunted Casino/UI Theme", fileName = "CasinoTheme")]
    public class UITheme : ScriptableObject
    {
        [Header("Fonts (TTF/OTF; TMP font assets are built at runtime)")]
        public Font displayFont;
        public Font bodyFont;

        [Header("Palette")]
        public Color backdrop = new Color(0.035f, 0.02f, 0.035f, 1f);
        public Color panel = new Color(0.075f, 0.04f, 0.06f, 0.93f);
        public Color panelRaised = new Color(0.12f, 0.06f, 0.08f, 0.96f);
        public Color gold = new Color(0.86f, 0.68f, 0.33f, 1f);
        public Color goldBright = new Color(1f, 0.86f, 0.5f, 1f);
        public Color burgundy = new Color(0.36f, 0.05f, 0.1f, 1f);
        public Color felt = new Color(0.05f, 0.28f, 0.17f, 1f);
        public Color ghost = new Color(0.55f, 0.98f, 0.85f, 1f);
        public Color text = new Color(0.96f, 0.91f, 0.8f, 1f);
        public Color textDim = new Color(0.7f, 0.62f, 0.55f, 1f);
        public Color attack = new Color(1f, 0.45f, 0.2f, 1f);
        public Color defend = new Color(0.35f, 0.72f, 1f, 1f);
        public Color target = new Color(1f, 0.27f, 0.3f, 1f);
        public Color party = new Color(1f, 0.55f, 0.95f, 1f);
        public Color good = new Color(0.45f, 1f, 0.6f, 1f);

        [Header("Text sizes (at 1920x1080)")]
        public int hero = 84;
        public int title = 40;
        public int heading = 30;
        public int body = 22;
        public int small = 18;

        [Header("Motion")]
        [Tooltip("Show the big name card sweep at the start of every turn.")]
        public bool turnCards = true;
        public float turnCardSeconds = 0.75f;

        static UITheme current;
        TMP_FontAsset displayAsset, bodyAsset;
        Material displayShadow, bodyShadow;

        /// <summary>The active theme. Falls back to a generated default.</summary>
        public static UITheme Current
        {
            get
            {
                if (current == null) current = CreateInstance<UITheme>();
                return current;
            }
            set => current = value;
        }

        public TMP_FontAsset Display
        {
            get
            {
                if (displayAsset == null) displayAsset = Build(displayFont);
                return displayAsset;
            }
        }

        public TMP_FontAsset Body
        {
            get
            {
                if (bodyAsset == null) bodyAsset = Build(bodyFont);
                return bodyAsset;
            }
        }

        /// <summary>Font material with a soft drop shadow (underlay) for legibility over 3D.</summary>
        public Material ShadowMaterial(bool display)
        {
            if (display)
            {
                if (displayShadow == null) displayShadow = MakeShadow(Display);
                return displayShadow;
            }
            if (bodyShadow == null) bodyShadow = MakeShadow(Body);
            return bodyShadow;
        }

        static TMP_FontAsset Build(Font font)
        {
            var fallback = TMP_Settings.defaultFontAsset;
            if (font == null) return fallback;
            var asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
            if (asset == null) return fallback;
            asset.name = font.name + " (runtime SDF)";
            if (fallback != null)
            {
                asset.fallbackFontAssetTable ??= new System.Collections.Generic.List<TMP_FontAsset>();
                asset.fallbackFontAssetTable.Add(fallback);
            }
            return asset;
        }

        static Material MakeShadow(TMP_FontAsset font)
        {
            if (font == null) return null;
            var m = new Material(font.material);
            m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            m.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.85f));
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.6f);
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.6f);
            m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.35f);
            m.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.15f);
            return m;
        }

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);
    }
}
