using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

namespace CarArena
{
    /// <summary>
    /// The workshop tablet a mechanical arm swings in front of the windscreen between matches (TabletArm).
    /// Its screen is a world-space canvas built in code, styled like a rugged diagnostics unit: dark blue
    /// glass, cyan lines, icon tabs on the left.
    ///  - Start: continue / new run.   - Number picker.
    ///  - Garage, WORKSHOP tab: live camera feed of your car, stat bars, and a row of cards to repair and
    ///    buy upgrades.  NEXT MATCH tab: opponents and bounties, the emperor's mark, rewards, run stats.
    ///  RACE / ARENA (how to run the next event) are always in the same spot. The screen boots up once the arm has arrived and
    ///  blanks when it folds away; purchases flash the card and pulse the money.
    /// </summary>
    public class TabletUI : MonoBehaviour
    {
        public CampaignDirector campaign;
        public Canvas canvas;
        public TabletArm arm;
        public GarageCamera garageCamera;

        [Header("Lean in")]
        [Tooltip("Driver camera: zooms in on the tablet while it's presented (the windscreen leaves only a short strip).")]
        public CinemachineCamera driverCam;
        public CarCamera driverHead;
        public float focusFov = 44f;
        [Tooltip("Pitch change while reading (negative = look up).")]
        public float focusPitch = -10f;

        [Header("Icons")]
        public Sprite iconRepair;
        public Sprite iconArmour;
        public Sprite iconEngine;
        public Sprite iconTyres;
        public Sprite iconRam;
        public Sprite iconBoost;
        public Sprite iconGun;
        public Sprite iconLaurel;

        [Header("Colours")]
        public Color screen = new Color(0.025f, 0.05f, 0.075f);
        public Color panel = new Color(0.05f, 0.1f, 0.14f);
        public Color panelLight = new Color(0.08f, 0.15f, 0.2f);
        public Color line = new Color(0.3f, 0.8f, 1f, 0.45f);
        public Color cyan = new Color(0.45f, 0.9f, 1f);
        public Color gold = new Color(1f, 0.76f, 0.25f);
        public Color good = new Color(0.4f, 0.95f, 0.55f);
        public Color bad = new Color(1f, 0.35f, 0.28f);
        public Color muted = new Color(0.52f, 0.64f, 0.72f);

        public bool Visible { get; private set; }
        /// <summary>The arm has folded the tablet away (safe to drive off).</summary>
        public bool Stowed => arm == null || arm.Stowed;

        enum Tab { Workshop, Match }

        class Card
        {
            public UpgradeType type;
            public Image back, flash;
            public TextMeshProUGUI title, detail, price;
            public Image[] pips;
            public Button buy;
        }

        CanvasGroup content;
        TextMeshProUGUI bootText;
        float boot;
        GameObject startScreen, pickScreen, garageScreen, workshopTab, matchTab;
        TextMeshProUGUI startInfo, pickNumber;
        TextMeshProUGUI header, money, numberBadge, hullText, matchList, markText, rewardText, runText;
        Image hullFill, tabWorkshopBar, tabMatchBar;
        static readonly UpgradeType[] UpgradeOrder =
            { UpgradeType.Armour, UpgradeType.Engine, UpgradeType.Tyres, UpgradeType.RamPlate, UpgradeType.Boost, UpgradeType.Gun };
        Image[] statFills = new Image[6];
        TextMeshProUGUI[] statValues = new TextMeshProUGUI[6];
        RawImage carView;
        Button patchButton, fullButton;
        TextMeshProUGUI patchLabel, fullLabel;
        Image repairFlash;
        readonly List<Card> cards = new List<Card>();
        int pickValue = 42;
        float moneyPulse;
        Tab tab = Tab.Workshop;
        Button arenaButton;
        TextMeshProUGUI arenaLabel;
        float baseFov = -1f;

        void Awake()
        {
            Build();
            content.alpha = 0f;
        }

        void Start()
        {
            if (canvas.worldCamera == null) canvas.worldCamera = Camera.main;
            if (driverCam != null) baseFov = driverCam.Lens.FieldOfView;
        }

        // ------------------------------------------------------------------ show / hide

        public void Show()
        {
            Visible = true;
            boot = 0f;
            if (arm != null) arm.Present();
            if (canvas.worldCamera == null) canvas.worldCamera = Camera.main;
            if (garageCamera != null && garageCamera.Texture != null) carView.texture = garageCamera.Texture;
        }

        public void Hide()
        {
            Visible = false;
            if (arm != null) arm.Stow();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool arrived = arm == null || arm.Progress > 0.97f;
            if (Visible && arrived) boot += dt;
            else if (!Visible) boot = 0f;

            // Boot: a short splash, then the UI fades in. Blank as soon as it's told to fold away.
            float target = Visible && boot > 0.45f ? 1f : 0f;
            content.alpha = target > 0f ? Mathf.Clamp01((boot - 0.45f) / 0.25f) : Mathf.MoveTowards(content.alpha, 0f, dt * 6f);
            content.interactable = content.blocksRaycasts = Visible && content.alpha > 0.5f;
            bootText.alpha = Visible && arrived && boot < 0.7f ? Mathf.Clamp01(boot / 0.1f) * Mathf.Clamp01((0.7f - boot) / 0.2f) : 0f;

            if (Visible && garageCamera != null && carView.texture != garageCamera.Texture) carView.texture = garageCamera.Texture;
            if (garageScreen.activeSelf) RefreshHull();

            LeanIn();

            moneyPulse = Mathf.MoveTowards(moneyPulse, 0f, dt * 2.5f);
            money.rectTransform.localScale = Vector3.one * (1f + moneyPulse * 0.15f);
            foreach (var c in cards) FadeFlash(c.flash, dt);
            FadeFlash(repairFlash, dt);
        }

        /// <summary>Zoom and tilt the driver's view onto the tablet, following the arm's progress.</summary>
        void LeanIn()
        {
            float p = arm != null ? arm.Progress : (Visible ? 1f : 0f);
            float k = p * p * (3f - 2f * p);
            // FOV belongs to CarCamera (speed feel); only lean in with a pitch change.
            if (driverHead != null && baseFov > 0f && !Mathf.Approximately(focusFov, baseFov))
                driverHead.baseFov = Mathf.Lerp(baseFov, focusFov, k);
            if (driverHead != null) driverHead.pitchOffset = focusPitch * k;
        }

        static void FadeFlash(Image img, float dt)
        {
            if (img == null) return;
            var col = img.color;
            col.a = Mathf.MoveTowards(col.a, 0f, dt * 1.8f);
            img.color = col;
        }

        // ------------------------------------------------------------------ screens

        public void ShowStart()
        {
            SetScreen(startScreen);
            var r = campaign.SavedRun;
            startInfo.text = r != null
                ? $"SAVED RUN   car <b>#{CarNumberPlates.Format(r.carNumber)}</b>   match <b>{r.matchIndex + 1}</b>   <color=#{Hex(gold)}><b>${r.money}</b></color>   {r.wins} wins"
                : "";
        }

        public void ShowNumberPicker(int initial)
        {
            pickValue = Mathf.Clamp(initial, 0, 99);
            SetScreen(pickScreen);
            UpdatePick();
        }

        public void ShowGarage()
        {
            SetScreen(garageScreen);
            SetTab(Tab.Workshop);
            Refresh();
        }

        void SetScreen(GameObject s)
        {
            startScreen.SetActive(s == startScreen);
            pickScreen.SetActive(s == pickScreen);
            garageScreen.SetActive(s == garageScreen);
        }

        void SetTab(Tab t)
        {
            tab = t;
            workshopTab.SetActive(t == Tab.Workshop);
            matchTab.SetActive(t == Tab.Match);
            tabWorkshopBar.color = t == Tab.Workshop ? cyan : Color.clear;
            tabMatchBar.color = t == Tab.Match ? cyan : Color.clear;
        }

        void UpdatePick()
        {
            pickNumber.text = CarNumberPlates.Format(pickValue);
            campaign.PreviewNumber(pickValue);
        }

        public void Refresh()
        {
            var run = campaign.Run;
            var m = campaign.NextMatch;
            if (run == null || m == null) return;
            var settings = campaign.settings;

            int total = settings.LadderLength;
            string matchNo = run.matchIndex < total ? $"MATCH {run.matchIndex + 1}/{total}" : "ENDLESS";
            header.text = $"<color=#{Hex(muted)}>{matchNo}</color>   {m.name.ToUpper()}";
            money.text = $"${run.money}";
            numberBadge.text = $"#{CarNumberPlates.Format(run.carNumber)}";

            UpgradeType[] order = UpgradeOrder;
            for (int i = 0; i < order.Length; i++)
            {
                var track = settings.Track(order[i]);
                int max = track != null ? Mathf.Max(1, track.MaxLevel) : 1;
                int level = run.Level(order[i]);
                statFills[i].rectTransform.anchorMax = new Vector2(Mathf.Clamp01((level + 0.0001f) / max), 1f);
                statValues[i].text = level >= max ? "MAX" : $"LV {level}";
            }

            foreach (var c in cards)
            {
                var track = settings.Track(c.type);
                int level = run.Level(c.type), max = track != null ? track.MaxLevel : 0;
                for (int p = 0; p < c.pips.Length; p++)
                {
                    c.pips[p].gameObject.SetActive(p < max);
                    c.pips[p].color = p < level ? cyan : new Color(cyan.r, cyan.g, cyan.b, 0.15f);
                }
                string next = track != null && level < max && track.levelText != null && level < track.levelText.Length ? track.levelText[level] : "Fully upgraded";
                c.detail.text = next;
                int cost = campaign.UpgradeCost(c.type);
                bool affordable = cost >= 0 && run.money >= cost;
                c.buy.interactable = affordable;
                c.price.text = cost < 0 ? "MAX" : $"${cost}";
                c.price.color = cost < 0 ? muted : affordable ? Color.black : bad;
            }

            bool arenaOpen = campaign.ArenaUnlocked;
            arenaButton.interactable = arenaOpen;
            arenaLabel.text = arenaOpen ? "ARENA" : "ARENA\n<size=80%>LOCKED</size>";
            arenaLabel.fontSize = arenaOpen ? 26 : 22;

            var sb = new StringBuilder();
            foreach (var e in campaign.NextEnemies)
            {
                int bounty = settings.KillReward(e.number);
                if (e.marked) bounty = Mathf.RoundToInt(bounty * settings.markedBountyMultiplier);
                string tag = e.marked ? $"  <color=#{Hex(gold)}><b>MARKED</b></color>" : "";
                sb.Append($"<b>{e.label}</b>{tag}<pos=72%><color=#{Hex(gold)}>${bounty}</color>\n");
            }
            matchList.text = sb.ToString();

            markText.text = campaign.PlayerMarked
                ? $"<size=130%><b>#{CarNumberPlates.Format(run.markedNumber)}  -  YOU</b></size>\nEvery car will hunt you. Survive and the whole match pays x{settings.markedSurvivalMultiplier:0.#}."
                : $"<size=130%><b>#{CarNumberPlates.Format(run.markedNumber)}</b></size>\nEveryone hunts the marked car. Wreck it yourself for x{settings.markedBountyMultiplier:0.#} bounty.";
            markText.color = campaign.PlayerMarked ? gold : Color.white;
            rewardText.text = $"Victory pays <color=#{Hex(gold)}><b>${settings.WinReward(run.matchIndex)}</b></color>, plus the bounty for every car you wreck.";
            runText.text = $"Wins <b>{run.wins}</b>     Kills <b>{run.kills}</b>" + (run.championBeaten ? $"     <color=#{Hex(gold)}>CHAMPION</color>" : "");
            RefreshHull();
        }

        void RefreshHull()
        {
            if (campaign.Run == null) return;
            float hp = campaign.PlayerHealth, max = Mathf.Max(1f, campaign.PlayerMaxHealth);
            float f = hp / max;
            hullFill.rectTransform.anchorMax = new Vector2(f, 1f);
            hullFill.color = Color.Lerp(bad, good, Mathf.InverseLerp(0.25f, 0.8f, f));
            hullText.text = $"{Mathf.CeilToInt(hp)}/{Mathf.RoundToInt(max)}";
            bool damaged = hp < max - 0.5f;
            int patch = campaign.RepairCost(false), full = campaign.RepairCost(true);
            patchButton.interactable = damaged && campaign.Run.money >= patch;
            fullButton.interactable = damaged && campaign.Run.money >= full;
            patchLabel.text = damaged ? $"PATCH +{campaign.settings.patchAmount}\n${patch}" : "PATCH";
            fullLabel.text = damaged ? $"FULL\n${full}" : "HULL OK";
        }

        void Buy(Card c)
        {
            if (!campaign.BuyUpgrade(c.type)) return;
            c.flash.color = new Color(cyan.r, cyan.g, cyan.b, 0.6f);
            moneyPulse = 1f;
        }

        void DoRepair(bool full)
        {
            if (!campaign.Repair(full)) return;
            repairFlash.color = new Color(good.r, good.g, good.b, 0.5f);
            moneyPulse = 1f;
        }

        // ------------------------------------------------------------------ building

        void Build()
        {
            var root = canvas.transform;
            Panel(root, "Glass", Vector2.zero, Vector2.one, screen);
            // Faint grid on the glass.
            var grid = Rect(root, "Grid", Vector2.zero, Vector2.one);
            for (int i = 1; i < 16; i++) Panel(grid, "V", new Vector2(i / 16f, 0f), new Vector2(i / 16f, 1f), new Color(cyan.r, cyan.g, cyan.b, 0.035f)).rectTransform.sizeDelta = new Vector2(2f, 0f);
            for (int i = 1; i < 10; i++) Panel(grid, "H", new Vector2(0f, i / 10f), new Vector2(1f, i / 10f), new Color(cyan.r, cyan.g, cyan.b, 0.035f)).rectTransform.sizeDelta = new Vector2(0f, 2f);

            var group = Rect(root, "Content", Vector2.zero, Vector2.one);
            content = group.gameObject.AddComponent<CanvasGroup>();
            bootText = Text(root, "ARENA WORKSHOP OS\n<size=50%>linking to bay 1 ...</size>", 64, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, cyan);
            bootText.fontStyle = FontStyles.Bold;
            var c = group.transform;

            BuildStart(c);
            BuildPicker(c);
            BuildGarage(c);
            SetScreen(startScreen);
        }

        void BuildStart(Transform c)
        {
            startScreen = Rect(c, "Start", Vector2.zero, Vector2.one).gameObject;
            var s = startScreen.transform;
            Icon(s, iconLaurel, new Vector2(0.43f, 0.66f), new Vector2(0.57f, 0.9f), gold);
            var title = Text(s, "THE ARENA", 120, TextAlignmentOptions.Center, new Vector2(0.05f, 0.5f), new Vector2(0.95f, 0.68f), Color.white);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 12f;
            startInfo = Text(s, "", 30, TextAlignmentOptions.Center, new Vector2(0.05f, 0.4f), new Vector2(0.95f, 0.48f), muted);
            Btn(s, "CONTINUE", new Vector2(0.24f, 0.2f), new Vector2(0.49f, 0.34f), () => campaign.ContinueRun(), gold, Color.black);
            Btn(s, "NEW RUN", new Vector2(0.51f, 0.2f), new Vector2(0.76f, 0.34f), () => campaign.NewRun(), panelLight, Color.white, true);
            Btn(s, "SETTINGS", new Vector2(0.33f, 0.05f), new Vector2(0.49f, 0.15f), () =>
            {
                var menu = FindAnyObjectByType<PauseMenu>();
                if (menu != null) menu.ShowSettings();
            }, panelLight, muted, true);
            Btn(s, "QUIT", new Vector2(0.51f, 0.05f), new Vector2(0.67f, 0.15f), () =>
            {
                Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }, panelLight, muted, true);
        }

        void BuildPicker(Transform c)
        {
            pickScreen = Rect(c, "PickNumber", Vector2.zero, Vector2.one).gameObject;
            var s = pickScreen.transform;
            var title = Text(s, "CHOOSE YOUR NUMBER", 54, TextAlignmentOptions.Center, new Vector2(0.05f, 0.84f), new Vector2(0.95f, 0.96f), cyan);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 6f;
            var plate = Panel(s, "Plate", new Vector2(0.36f, 0.42f), new Vector2(0.64f, 0.82f), new Color(0.93f, 0.92f, 0.88f));
            AddOutline(plate, gold, 5f);
            pickNumber = Text(plate.transform, "42", 250, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, new Color(0.06f, 0.06f, 0.06f));
            pickNumber.fontStyle = FontStyles.Bold;
            Btn(s, "-10", new Vector2(0.06f, 0.54f), new Vector2(0.16f, 0.7f), () => Step(-10), panelLight, Color.white, true);
            Btn(s, "-1", new Vector2(0.18f, 0.54f), new Vector2(0.3f, 0.7f), () => Step(-1), panelLight, Color.white, true);
            Btn(s, "+1", new Vector2(0.7f, 0.54f), new Vector2(0.82f, 0.7f), () => Step(1), panelLight, Color.white, true);
            Btn(s, "+10", new Vector2(0.84f, 0.54f), new Vector2(0.94f, 0.7f), () => Step(10), panelLight, Color.white, true);
            Text(s, "Painted on your roof and doors. Before every match the emperor marks one car's number:\n" +
                    "everyone hunts it. Wreck a marked car for triple bounty - or survive being marked for double pay.",
                24, TextAlignmentOptions.Center, new Vector2(0.06f, 0.24f), new Vector2(0.94f, 0.39f), muted);
            Btn(s, "RANDOM", new Vector2(0.24f, 0.07f), new Vector2(0.48f, 0.19f), () => { pickValue = UnityEngine.Random.Range(0, 100); UpdatePick(); }, panelLight, Color.white, true);
            Btn(s, "CONFIRM  >", new Vector2(0.52f, 0.07f), new Vector2(0.76f, 0.19f), () => campaign.ConfirmNumber(pickValue), gold, Color.black);
        }

        void BuildGarage(Transform c)
        {
            garageScreen = Rect(c, "Garage", Vector2.zero, Vector2.one).gameObject;
            var g = garageScreen.transform;

            // Header bar
            var bar = Panel(g, "Header", new Vector2(0f, 0.915f), new Vector2(1f, 1f), panel);
            Panel(bar.transform, "Rule", new Vector2(0f, 0f), new Vector2(1f, 0f), line).rectTransform.sizeDelta = new Vector2(0f, 3f);
            header = Text(g, "", 34, TextAlignmentOptions.Left, new Vector2(0.1f, 0.915f), new Vector2(0.62f, 1f), Color.white);
            header.fontStyle = FontStyles.Bold;
            var badge = Panel(g, "Badge", new Vector2(0.66f, 0.93f), new Vector2(0.76f, 0.985f), new Color(0.93f, 0.92f, 0.88f));
            numberBadge = Text(badge.transform, "", 34, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, new Color(0.06f, 0.06f, 0.06f));
            numberBadge.fontStyle = FontStyles.Bold;
            money = Text(g, "", 46, TextAlignmentOptions.Right, new Vector2(0.77f, 0.915f), new Vector2(0.96f, 1f), gold);
            money.fontStyle = FontStyles.Bold;
            money.rectTransform.pivot = new Vector2(1f, 0.5f);

            // Left icon rail: tabs
            Panel(g, "Rail", new Vector2(0f, 0f), new Vector2(0.08f, 0.915f), panel);
            TabButton(g, iconRepair, "GARAGE", 0.8f, () => SetTab(Tab.Workshop), out tabWorkshopBar);
            TabButton(g, iconLaurel, "MATCH", 0.6f, () => { SetTab(Tab.Match); Refresh(); }, out tabMatchBar);

            BuildWorkshop(g);
            BuildMatch(g);

            // Always in the same place on both tabs.
            // Two ways to run the next event: a race (primary) or an arena fight.
            var fight = Btn(g, "RACE  >", new Vector2(0.69f, 0.36f), new Vector2(0.86f, 0.46f), () => campaign.StartRace(), gold, Color.black);
            arenaButton = Btn(g, "ARENA", new Vector2(0.87f, 0.36f), new Vector2(0.98f, 0.46f), () => campaign.StartMatch(), panelLight, Color.white, true);
            arenaLabel = arenaButton.GetComponentInChildren<TextMeshProUGUI>();
            arenaLabel.fontSize = 26;
            fight.GetComponentInChildren<TextMeshProUGUI>().fontSize = 34;
        }

        void TabButton(Transform g, Sprite icon, string label, float y, Action onClick, out Image activeBar)
        {
            var b = Btn(g, "", new Vector2(0.008f, y - 0.075f), new Vector2(0.072f, y + 0.075f), onClick, panelLight, Color.white, true);
            Icon(b.transform, icon, new Vector2(0.15f, 0.32f), new Vector2(0.85f, 0.92f), cyan);
            Text(b.transform, label, 17, TextAlignmentOptions.Center, new Vector2(0f, 0.04f), new Vector2(1f, 0.3f), muted).fontStyle = FontStyles.Bold;
            activeBar = Panel(g, "Active", new Vector2(0f, y - 0.075f), new Vector2(0.006f, y + 0.075f), cyan);
        }

        void BuildWorkshop(Transform g)
        {
            workshopTab = Rect(g, "Workshop", Vector2.zero, Vector2.one).gameObject;
            var w = workshopTab.transform;

            // Live camera feed with a blueprint frame.
            var frame = Panel(w, "Feed", new Vector2(0.1f, 0.36f), new Vector2(0.67f, 0.9f), Color.black);
            AddOutline(frame, line, 2f);
            carView = new GameObject("CarView", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            Place(carView.rectTransform, frame.transform, new Vector2(0.005f, 0.008f), new Vector2(0.995f, 0.992f));
            carView.color = new Color(0.82f, 0.95f, 1f);
            carView.raycastTarget = false;
            for (int i = 1; i < 8; i++) Panel(frame.transform, "V", new Vector2(i / 8f, 0f), new Vector2(i / 8f, 1f), new Color(cyan.r, cyan.g, cyan.b, 0.08f)).rectTransform.sizeDelta = new Vector2(2f, 0f);
            for (int i = 1; i < 5; i++) Panel(frame.transform, "H", new Vector2(0f, i / 5f), new Vector2(1f, i / 5f), new Color(cyan.r, cyan.g, cyan.b, 0.08f)).rectTransform.sizeDelta = new Vector2(0f, 2f);
            Brackets(frame.transform);
            Text(frame.transform, "LIVE  BAY 1", 20, TextAlignmentOptions.TopLeft, new Vector2(0.03f, 0.84f), new Vector2(0.5f, 0.97f), cyan).fontStyle = FontStyles.Bold;

            // Right column: hull + upgrade levels
            var stats = Panel(w, "Stats", new Vector2(0.69f, 0.48f), new Vector2(0.98f, 0.9f), panel);
            AddOutline(stats, line, 2f);
            var st = stats.transform;
            Text(st, "HULL", 24, TextAlignmentOptions.Left, new Vector2(0.06f, 0.82f), new Vector2(0.5f, 0.96f), muted).fontStyle = FontStyles.Bold;
            hullText = Text(st, "", 26, TextAlignmentOptions.Right, new Vector2(0.5f, 0.82f), new Vector2(0.94f, 0.96f), Color.white);
            hullText.fontStyle = FontStyles.Bold;
            var hullBack = Panel(st, "HullBack", new Vector2(0.06f, 0.73f), new Vector2(0.94f, 0.81f), panelLight);
            hullFill = Panel(hullBack.transform, "Fill", Vector2.zero, Vector2.one, good);
            string[] names = { "ARMOUR", "ENGINE", "TYRES", "RAM", "BOOST", "GUN" };
            for (int i = 0; i < names.Length; i++)
            {
                float top = 0.67f - i * 0.107f;
                Text(st, names[i], 20, TextAlignmentOptions.Left, new Vector2(0.06f, top - 0.06f), new Vector2(0.5f, top), muted).fontStyle = FontStyles.Bold;
                statValues[i] = Text(st, "", 20, TextAlignmentOptions.Right, new Vector2(0.5f, top - 0.06f), new Vector2(0.94f, top), cyan);
                var back = Panel(st, "Back", new Vector2(0.06f, top - 0.092f), new Vector2(0.94f, top - 0.064f), panelLight);
                statFills[i] = Panel(back.transform, "Fill", Vector2.zero, Vector2.one, cyan);
            }

            // Bottom row: repair + six upgrade cards
            float x0 = 0.1f, x1 = 0.98f, gap = 0.01f;
            float cw = (x1 - x0 - gap * 6f) / 7f;
            RepairCard(w, new Vector2(x0, 0.03f), new Vector2(x0 + cw, 0.33f));
            UpgradeType[] order = UpgradeOrder;
            Sprite[] icons = { iconArmour, iconEngine, iconTyres, iconRam, iconBoost, iconGun };
            string[] titles = { "ARMOUR", "ENGINE", "TYRES", "RAM", "BOOST", "GUN" };
            for (int i = 0; i < order.Length; i++)
            {
                float a = x0 + (cw + gap) * (i + 1);
                cards.Add(UpgradeCard(w, order[i], icons[i], titles[i], new Vector2(a, 0.03f), new Vector2(a + cw, 0.33f)));
            }
        }

        void RepairCard(Transform w, Vector2 min, Vector2 max)
        {
            var back = Panel(w, "RepairCard", min, max, panel);
            AddOutline(back, line, 2f);
            var t = back.transform;
            Icon(t, iconRepair, new Vector2(0.05f, 0.7f), new Vector2(0.36f, 0.95f), cyan);
            var repairTitle = Text(t, "REPAIR", 24, TextAlignmentOptions.Left, new Vector2(0.39f, 0.74f), new Vector2(0.98f, 0.93f), Color.white);
            repairTitle.fontStyle = FontStyles.Bold;
            FitText(repairTitle, 16f, 24f);
            patchButton = Btn(t, "PATCH", new Vector2(0.06f, 0.37f), new Vector2(0.94f, 0.62f), () => DoRepair(false), panelLight, Color.white, true);
            patchLabel = patchButton.GetComponentInChildren<TextMeshProUGUI>();
            fullButton = Btn(t, "FULL", new Vector2(0.06f, 0.07f), new Vector2(0.94f, 0.32f), () => DoRepair(true), panelLight, Color.white, true);
            fullLabel = fullButton.GetComponentInChildren<TextMeshProUGUI>();
            FitText(patchLabel, 13f, 20f);
            FitText(fullLabel, 13f, 20f);
            repairFlash = Panel(t, "Flash", Vector2.zero, Vector2.one, Color.clear);
        }

        Card UpgradeCard(Transform w, UpgradeType type, Sprite icon, string title, Vector2 min, Vector2 max)
        {
            var card = new Card { type = type };
            card.back = Panel(w, "Card", min, max, panel);
            AddOutline(card.back, line, 2f);
            var t = card.back.transform;
            Icon(t, icon, new Vector2(0.05f, 0.7f), new Vector2(0.36f, 0.95f), cyan);
            card.title = Text(t, title, 24, TextAlignmentOptions.Left, new Vector2(0.39f, 0.74f), new Vector2(0.98f, 0.93f), Color.white);
            card.title.fontStyle = FontStyles.Bold;
            FitText(card.title, 16f, 24f);
            card.pips = new Image[3];
            for (int p = 0; p < 3; p++)
                card.pips[p] = Panel(t, "Pip", new Vector2(0.39f + p * 0.19f, 0.67f), new Vector2(0.55f + p * 0.19f, 0.71f), cyan);
            card.detail = Text(t, "", 18, TextAlignmentOptions.TopLeft, new Vector2(0.07f, 0.34f), new Vector2(0.95f, 0.63f), muted);
            FitText(card.detail, 12f, 18f);
            Card captured = card;
            card.buy = Btn(t, "$0", new Vector2(0.06f, 0.07f), new Vector2(0.94f, 0.3f), () => Buy(captured), gold, Color.black);
            card.price = card.buy.GetComponentInChildren<TextMeshProUGUI>();
            card.price.fontSize = 24;
            card.flash = Panel(t, "Flash", Vector2.zero, Vector2.one, Color.clear);
            return card;
        }

        void BuildMatch(Transform g)
        {
            matchTab = Rect(g, "Match", Vector2.zero, Vector2.one).gameObject;
            var m = matchTab.transform;

            var list = Panel(m, "Opponents", new Vector2(0.1f, 0.36f), new Vector2(0.67f, 0.9f), panel);
            AddOutline(list, line, 2f);
            Text(list.transform, "OPPONENTS", 24, TextAlignmentOptions.Left, new Vector2(0.04f, 0.86f), new Vector2(0.6f, 0.97f), cyan).fontStyle = FontStyles.Bold;
            Text(list.transform, "BOUNTY", 20, TextAlignmentOptions.Left, new Vector2(0.72f, 0.86f), new Vector2(0.96f, 0.97f), muted).fontStyle = FontStyles.Bold;
            matchList = Text(list.transform, "", 28, TextAlignmentOptions.TopLeft, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.85f), Color.white);
            matchList.lineSpacing = 18f;

            var mark = Panel(m, "Mark", new Vector2(0.1f, 0.03f), new Vector2(0.67f, 0.33f), panel);
            AddOutline(mark, gold, 2f);
            Icon(mark.transform, iconLaurel, new Vector2(0.02f, 0.15f), new Vector2(0.2f, 0.85f), gold);
            Text(mark.transform, "THE EMPEROR MARKS", 20, TextAlignmentOptions.Left, new Vector2(0.23f, 0.74f), new Vector2(0.98f, 0.95f), gold).fontStyle = FontStyles.Bold;
            markText = Text(mark.transform, "", 24, TextAlignmentOptions.TopLeft, new Vector2(0.23f, 0.06f), new Vector2(0.98f, 0.74f), Color.white);

            var rewards = Panel(m, "Rewards", new Vector2(0.69f, 0.48f), new Vector2(0.98f, 0.9f), panel);
            AddOutline(rewards, line, 2f);
            Text(rewards.transform, "REWARDS", 24, TextAlignmentOptions.Left, new Vector2(0.06f, 0.82f), new Vector2(0.94f, 0.96f), cyan).fontStyle = FontStyles.Bold;
            rewardText = Text(rewards.transform, "", 24, TextAlignmentOptions.TopLeft, new Vector2(0.06f, 0.3f), new Vector2(0.94f, 0.8f), Color.white);
            runText = Text(rewards.transform, "", 22, TextAlignmentOptions.BottomLeft, new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.28f), muted);

            var hint = Panel(m, "Hint", new Vector2(0.69f, 0.03f), new Vector2(0.98f, 0.33f), panel);
            AddOutline(hint, line, 2f);
            Text(hint.transform, "RACE: win to move up the ladder; 2nd and 3rd pay part of the purse. Guns are weak in races - drive. " +
                                 "Win the championship race to unlock ARENA fights. Wrecks earn bounty: higher number, tougher car, bigger reward.",
                20, TextAlignmentOptions.TopLeft, new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.92f), muted);
        }

        void Step(int d)
        {
            pickValue = (pickValue + d + 100) % 100; // wraps: 00 <-> 99
            UpdatePick();
        }

        // ------------------------------------------------------------------ UI helpers

        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

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

        static void AddOutline(Graphic g, Color c, float width)
        {
            var o = g.gameObject.AddComponent<Outline>();
            o.effectColor = c;
            o.effectDistance = new Vector2(width, -width);
        }

        static Image Icon(Transform parent, Sprite sprite, Vector2 min, Vector2 max, Color c)
        {
            var img = Panel(parent, "Icon", min, max, c);
            img.sprite = sprite;
            img.preserveAspect = true;
            if (sprite == null) img.color = Color.clear;
            return img;
        }

        void Brackets(Transform parent)
        {
            const float len = 0.06f, th = 0.012f;
            Vector2[] corners = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            foreach (var k in corners)
            {
                float sx = k.x < 0.5f ? 1f : -1f, sy = k.y < 0.5f ? 1f : -1f;
                Panel(parent, "BracketH", new Vector2(Mathf.Min(k.x, k.x + sx * len), Mathf.Min(k.y, k.y + sy * th * 1.6f)),
                    new Vector2(Mathf.Max(k.x, k.x + sx * len), Mathf.Max(k.y, k.y + sy * th * 1.6f)), cyan);
                Panel(parent, "BracketV", new Vector2(Mathf.Min(k.x, k.x + sx * th), Mathf.Min(k.y, k.y + sy * len * 1.6f)),
                    new Vector2(Mathf.Max(k.x, k.x + sx * th), Mathf.Max(k.y, k.y + sy * len * 1.6f)), cyan);
            }
        }

        /// <summary>Shrink text to fit its box (narrow cards).</summary>
        static void FitText(TextMeshProUGUI t, float min, float max)
        {
            t.enableAutoSizing = true;
            t.fontSizeMin = min;
            t.fontSizeMax = max;
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

        Button Btn(Transform parent, string label, Vector2 min, Vector2 max, Action onClick, Color bg, Color fg, bool outlined = false)
        {
            var img = Panel(parent, "Button", min, max, bg);
            img.raycastTarget = true;
            if (outlined) AddOutline(img, line, 2f);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.55f);
            colors.fadeDuration = 0.08f;
            b.colors = colors;
            b.onClick.AddListener(() => onClick());
            if (!string.IsNullOrEmpty(label))
            {
                var t = Text(img.transform, label, 30, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, fg);
                t.fontStyle = FontStyles.Bold;
            }
            return b;
        }
    }
}
