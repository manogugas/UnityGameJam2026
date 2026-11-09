using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarArena
{
    /// <summary>
    /// The beat-up tablet held up in front of the windscreen between matches. Its screen is a world-space
    /// canvas built in code: a start screen (continue / new run), the number picker, and the garage
    /// (live 3rd-person view of your car, hull + repairs, upgrades, the upcoming match and the emperor's
    /// lucky digit, and the button to drive out). The tablet rises from the dash when shown.
    /// </summary>
    public class TabletUI : MonoBehaviour
    {
        public CampaignDirector campaign;
        public Canvas canvas;
        [Tooltip("The tablet body that moves between the hidden and shown poses.")]
        public Transform tablet;
        public Vector3 shownPosition, shownEuler;
        public Vector3 hiddenPosition, hiddenEuler;
        public float moveTime = 0.6f;
        public GarageCamera garageCamera;

        [Header("Colours")]
        public Color background = new Color(0.07f, 0.08f, 0.09f);
        public Color panel = new Color(0.13f, 0.14f, 0.16f);
        public Color accent = new Color(1f, 0.72f, 0.2f);
        public Color good = new Color(0.4f, 0.9f, 0.45f);
        public Color bad = new Color(1f, 0.35f, 0.28f);
        public Color muted = new Color(0.6f, 0.62f, 0.66f);

        public bool Visible { get; private set; }

        GameObject startScreen, pickScreen, garageScreen;
        TextMeshProUGUI startInfo, pickNumber, pickHint;
        TextMeshProUGUI header, money, numberBadge, hullText, stats, matchText, luckText;
        Image hullFill;
        RawImage carView;
        Button patchButton, fullButton, fightButton;
        TextMeshProUGUI patchLabel, fullLabel;
        readonly List<(UpgradeType type, TextMeshProUGUI pips, TextMeshProUGUI text, Button buy, TextMeshProUGUI buyLabel)> upgradeRows
            = new List<(UpgradeType, TextMeshProUGUI, TextMeshProUGUI, Button, TextMeshProUGUI)>();
        int pickValue = 7;
        float move; // 0 hidden .. 1 shown

        void Awake()
        {
            Build();
            move = 0f;
            ApplyPose();
            tablet.gameObject.SetActive(false);
        }

        void Start()
        {
            if (canvas.worldCamera == null) canvas.worldCamera = Camera.main;
        }

        // ------------------------------------------------------------------ show / hide

        public void Show()
        {
            Visible = true;
            tablet.gameObject.SetActive(true);
            if (canvas.worldCamera == null) canvas.worldCamera = Camera.main;
            if (garageCamera != null && garageCamera.Texture != null) carView.texture = garageCamera.Texture;
        }

        public void Hide() => Visible = false;

        void Update()
        {
            float target = Visible ? 1f : 0f;
            move = Mathf.MoveTowards(move, target, Time.deltaTime / Mathf.Max(0.01f, moveTime));
            ApplyPose();
            if (!Visible && move <= 0f && tablet.gameObject.activeSelf) tablet.gameObject.SetActive(false);
            if (Visible && garageCamera != null && carView.texture != garageCamera.Texture) carView.texture = garageCamera.Texture;
            if (garageScreen.activeSelf) RefreshHull();
        }

        void ApplyPose()
        {
            float k = move * move * (3f - 2f * move);
            tablet.localPosition = Vector3.Lerp(hiddenPosition, shownPosition, k);
            tablet.localRotation = Quaternion.Slerp(Quaternion.Euler(hiddenEuler), Quaternion.Euler(shownEuler), k);
        }

        // ------------------------------------------------------------------ screens

        public void ShowStart()
        {
            SetScreen(startScreen);
            var r = campaign.SavedRun;
            startInfo.text = r != null
                ? $"Saved run: car <b>#{r.carNumber}</b>   match <b>{r.matchIndex + 1}</b>   <b>${r.money}</b>   {r.wins} wins"
                : "";
        }

        public void ShowNumberPicker(int initial)
        {
            pickValue = Mathf.Clamp(initial, 10, 99);
            SetScreen(pickScreen);
            UpdatePick();
        }

        public void ShowGarage()
        {
            SetScreen(garageScreen);
            Refresh();
        }

        void SetScreen(GameObject s)
        {
            startScreen.SetActive(s == startScreen);
            pickScreen.SetActive(s == pickScreen);
            garageScreen.SetActive(s == garageScreen);
        }

        void UpdatePick()
        {
            pickNumber.text = pickValue.ToString();
            campaign.PreviewNumber(pickValue);
        }

        public void Refresh()
        {
            var run = campaign.Run;
            var m = campaign.NextMatch;
            if (run == null || m == null) return;
            var settings = campaign.settings;

            int total = settings.LadderLength;
            string matchNo = run.matchIndex < total ? $"MATCH {run.matchIndex + 1} / {total}" : "ENDLESS";
            header.text = $"{matchNo}   <color=#{ColorUtility.ToHtmlStringRGB(accent)}>{m.name.ToUpper()}</color>";
            money.text = $"${run.money}";
            numberBadge.text = $"#{run.carNumber}";
            stats.text = $"Wins {run.wins}   Kills {run.kills}" + (run.championBeaten ? "   <color=#ffd060>CHAMPION</color>" : "");

            foreach (var row in upgradeRows)
            {
                var track = settings.Track(row.type);
                int level = run.Level(row.type), max = track != null ? track.MaxLevel : 0;
                var pips = new StringBuilder();
                pips.Append("LV ").Append(level).Append('/').Append(max);
                row.pips.text = pips.ToString();
                string next = track != null && level < max && track.levelText != null && level < track.levelText.Length ? track.levelText[level] : "Maxed";
                row.text.text = $"<b>{track?.displayName}</b>\n<size=85%><color=#{ColorUtility.ToHtmlStringRGB(muted)}>{next}</color></size>";
                int cost = campaign.UpgradeCost(row.type);
                row.buy.interactable = cost >= 0 && run.money >= cost;
                row.buyLabel.text = cost < 0 ? "MAX" : $"${cost}";
            }

            var sb = new StringBuilder();
            sb.Append($"<b>NEXT: {m.name}</b>\n");
            foreach (var e in campaign.NextEnemies)
            {
                int bounty = settings.KillReward(e.number);
                if (e.marked) bounty = Mathf.RoundToInt(bounty * settings.markedBountyMultiplier);
                string star = e.marked ? $" <color=#{ColorUtility.ToHtmlStringRGB(accent)}>MARKED</color>" : "";
                sb.Append($"{e.label}{star}  <color=#{ColorUtility.ToHtmlStringRGB(muted)}>bounty ${bounty}</color>\n");
            }
            sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(muted)}>Win: ${settings.WinReward(run.matchIndex)}</color>");
            matchText.text = sb.ToString();

            luckText.text = campaign.PlayerMarked
                ? $"The emperor marks <b>#{run.markedNumber}</b>  -  YOU. Every car will hunt you. Survive for x{settings.markedSurvivalMultiplier:0.#} pay"
                : $"The emperor marks <b>#{run.markedNumber}</b>  -  everyone hunts it, x{settings.markedBountyMultiplier:0.#} bounty";
            luckText.color = campaign.PlayerMarked ? accent : Color.white;
            RefreshHull();
        }

        void RefreshHull()
        {
            if (campaign.Run == null) return;
            float hp = campaign.PlayerHealth, max = Mathf.Max(1f, campaign.PlayerMaxHealth);
            float f = hp / max;
            hullFill.rectTransform.anchorMax = new Vector2(f, 1f);
            hullFill.color = Color.Lerp(bad, good, Mathf.InverseLerp(0.25f, 0.8f, f));
            hullText.text = $"HULL  {Mathf.CeilToInt(hp)} / {Mathf.RoundToInt(max)}";
            bool damaged = hp < max - 0.5f;
            int patch = campaign.RepairCost(false), full = campaign.RepairCost(true);
            patchButton.interactable = damaged && campaign.Run.money >= patch;
            fullButton.interactable = damaged && campaign.Run.money >= full;
            patchLabel.text = damaged ? $"PATCH +{campaign.settings.patchAmount}  ${patch}" : "PATCH";
            fullLabel.text = damaged ? $"FULL REPAIR  ${full}" : "FULL REPAIR";
        }

        // ------------------------------------------------------------------ building

        void Build()
        {
            var root = canvas.transform;
            Panel(root, "Background", Vector2.zero, Vector2.one, background);

            // Start screen
            startScreen = Rect(root, "Start", Vector2.zero, Vector2.one).gameObject;
            Text(startScreen.transform, "THE ARENA", 110, TextAlignmentOptions.Center, new Vector2(0.05f, 0.62f), new Vector2(0.95f, 0.9f), accent).fontStyle = FontStyles.Bold;
            startInfo = Text(startScreen.transform, "", 30, TextAlignmentOptions.Center, new Vector2(0.05f, 0.48f), new Vector2(0.95f, 0.58f), Color.white);
            Btn(startScreen.transform, "CONTINUE", new Vector2(0.2f, 0.26f), new Vector2(0.48f, 0.42f), () => campaign.ContinueRun(), accent, Color.black);
            Btn(startScreen.transform, "NEW RUN", new Vector2(0.52f, 0.26f), new Vector2(0.8f, 0.42f), () => campaign.NewRun(), panel, Color.white);
            Text(startScreen.transform, "Click with the mouse", 22, TextAlignmentOptions.Center, new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.16f), muted);

            // Number picker
            pickScreen = Rect(root, "PickNumber", Vector2.zero, Vector2.one).gameObject;
            Text(pickScreen.transform, "CHOOSE YOUR NUMBER", 56, TextAlignmentOptions.Center, new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.96f), accent).fontStyle = FontStyles.Bold;
            pickNumber = Text(pickScreen.transform, "7", 230, TextAlignmentOptions.Center, new Vector2(0.3f, 0.42f), new Vector2(0.7f, 0.82f), Color.white);
            pickNumber.fontStyle = FontStyles.Bold;
            Btn(pickScreen.transform, "-10", new Vector2(0.05f, 0.52f), new Vector2(0.15f, 0.7f), () => Step(-10), panel, Color.white);
            Btn(pickScreen.transform, "-1", new Vector2(0.17f, 0.52f), new Vector2(0.27f, 0.7f), () => Step(-1), panel, Color.white);
            Btn(pickScreen.transform, "+1", new Vector2(0.73f, 0.52f), new Vector2(0.83f, 0.7f), () => Step(1), panel, Color.white);
            Btn(pickScreen.transform, "+10", new Vector2(0.85f, 0.52f), new Vector2(0.95f, 0.7f), () => Step(10), panel, Color.white);
            pickHint = Text(pickScreen.transform,
                "It goes on your roof and doors (10-99). Before every match the emperor marks one car's number:\nevery car hunts the marked one. Wreck it for triple bounty - or, if it's you, survive for double pay.",
                24, TextAlignmentOptions.Center, new Vector2(0.05f, 0.24f), new Vector2(0.95f, 0.4f), muted);
            Btn(pickScreen.transform, "RANDOM", new Vector2(0.2f, 0.06f), new Vector2(0.45f, 0.2f), () => { pickValue = UnityEngine.Random.Range(10, 100); UpdatePick(); }, panel, Color.white);
            Btn(pickScreen.transform, "CONFIRM", new Vector2(0.55f, 0.06f), new Vector2(0.8f, 0.2f), () => campaign.ConfirmNumber(pickValue), accent, Color.black);

            // Garage
            garageScreen = Rect(root, "Garage", Vector2.zero, Vector2.one).gameObject;
            var g = garageScreen.transform;
            Panel(g, "HeaderBar", new Vector2(0f, 0.9f), new Vector2(1f, 1f), panel);
            header = Text(g, "", 30, TextAlignmentOptions.Left, new Vector2(0.03f, 0.9f), new Vector2(0.7f, 1f), Color.white);
            header.fontStyle = FontStyles.Bold;
            numberBadge = Text(g, "", 32, TextAlignmentOptions.Right, new Vector2(0.7f, 0.9f), new Vector2(0.8f, 1f), Color.white);
            numberBadge.fontStyle = FontStyles.Bold;
            money = Text(g, "", 38, TextAlignmentOptions.Right, new Vector2(0.8f, 0.9f), new Vector2(0.97f, 1f), accent);
            money.fontStyle = FontStyles.Bold;

            // Left: live car view, hull, repairs
            Panel(g, "ViewFrame", new Vector2(0.02f, 0.33f), new Vector2(0.49f, 0.88f), Color.black);
            carView = new GameObject("CarView", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            Place(carView.rectTransform, g, new Vector2(0.025f, 0.335f), new Vector2(0.485f, 0.875f));
            carView.color = Color.white;
            hullText = Text(g, "", 24, TextAlignmentOptions.Left, new Vector2(0.02f, 0.26f), new Vector2(0.49f, 0.32f), Color.white);
            var hullBack = Panel(g, "HullBack", new Vector2(0.02f, 0.225f), new Vector2(0.49f, 0.255f), panel);
            hullFill = Panel(hullBack.transform, "HullFill", Vector2.zero, Vector2.one, good);
            patchButton = Btn(g, "PATCH", new Vector2(0.02f, 0.12f), new Vector2(0.25f, 0.2f), () => campaign.Repair(false), panel, Color.white);
            patchLabel = patchButton.GetComponentInChildren<TextMeshProUGUI>();
            fullButton = Btn(g, "FULL REPAIR", new Vector2(0.26f, 0.12f), new Vector2(0.49f, 0.2f), () => campaign.Repair(true), panel, Color.white);
            fullLabel = fullButton.GetComponentInChildren<TextMeshProUGUI>();
            patchLabel.fontSize = fullLabel.fontSize = 20;

            // Right top: upgrades
            Text(g, "UPGRADES", 24, TextAlignmentOptions.Left, new Vector2(0.52f, 0.83f), new Vector2(0.98f, 0.88f), accent).fontStyle = FontStyles.Bold;
            UpgradeType[] order = { UpgradeType.Armour, UpgradeType.Engine, UpgradeType.Tyres, UpgradeType.RamPlate };
            for (int i = 0; i < order.Length; i++)
            {
                float top = 0.82f - i * 0.078f, bottom = top - 0.07f;
                Panel(g, "Row", new Vector2(0.52f, bottom), new Vector2(0.98f, top), panel);
                var text = Text(g, "", 21, TextAlignmentOptions.Left, new Vector2(0.535f, bottom), new Vector2(0.77f, top), Color.white);
                var pips = Text(g, "", 19, TextAlignmentOptions.Right, new Vector2(0.76f, bottom), new Vector2(0.84f, top), accent);
                var type = order[i];
                var buy = Btn(g, "$0", new Vector2(0.85f, bottom + 0.008f), new Vector2(0.975f, top - 0.008f), () => campaign.BuyUpgrade(type), accent, Color.black);
                var label = buy.GetComponentInChildren<TextMeshProUGUI>();
                label.fontSize = 22;
                upgradeRows.Add((type, pips, text, buy, label));
            }

            // Right bottom: next match + the emperor's mark
            Panel(g, "MatchPanel", new Vector2(0.52f, 0.12f), new Vector2(0.98f, 0.46f), panel);
            matchText = Text(g, "", 21, TextAlignmentOptions.TopLeft, new Vector2(0.535f, 0.22f), new Vector2(0.97f, 0.45f), Color.white);
            luckText = Text(g, "", 18, TextAlignmentOptions.BottomLeft, new Vector2(0.535f, 0.13f), new Vector2(0.97f, 0.22f), Color.white);

            // Bottom bar
            stats = Text(g, "", 22, TextAlignmentOptions.Left, new Vector2(0.03f, 0.015f), new Vector2(0.5f, 0.1f), muted);
            fightButton = Btn(g, "ENTER THE ARENA  >", new Vector2(0.55f, 0.015f), new Vector2(0.98f, 0.1f), () => campaign.StartMatch(), accent, Color.black);
            fightButton.GetComponentInChildren<TextMeshProUGUI>().fontStyle = FontStyles.Bold;

            SetScreen(startScreen);
        }

        void Step(int d)
        {
            pickValue = Mathf.Clamp(pickValue + d, 10, 99);
            UpdatePick();
        }

        // ------------------------------------------------------------------ UI helpers

        static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            Place(rt, parent, min, max);
            return rt;
        }

        static void Place(RectTransform rt, Transform parent, Vector2 min, Vector2 max)
        {
            rt.SetParent(parent, false);
            rt.gameObject.layer = parent.gameObject.layer; // UI layer: hidden from the garage camera
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.anchoredPosition3D = Vector3.zero;
        }

        static Image Panel(Transform parent, string name, Vector2 min, Vector2 max, Color c)
        {
            var rt = Rect(parent, name, min, max);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        static TextMeshProUGUI Text(Transform parent, string text, float size, TextAlignmentOptions align, Vector2 min, Vector2 max, Color c)
        {
            var rt = Rect(parent, "Text", min, max);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = c;
            t.richText = true;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            return t;
        }

        static Button Btn(Transform parent, string label, Vector2 min, Vector2 max, Action onClick, Color bg, Color fg)
        {
            var img = Panel(parent, "Button", min, max, bg);
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            b.colors = colors;
            b.onClick.AddListener(() => onClick());
            var t = Text(img.transform, label, 30, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, fg);
            t.fontStyle = FontStyles.Bold;
            return b;
        }
    }
}
