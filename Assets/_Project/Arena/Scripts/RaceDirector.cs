using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Standard race on the figure-8: grid start with a 3-2-1 countdown, laps, live positions, finish line.
    /// Checkpoint gates: a lap only counts once every gate has been driven through in order, so cutting across
    /// the sand or circling over the line gets you nowhere. Progress (for positions) = laps x track length +
    /// the last gate passed + how far past it you are (capped at the next gate), with the distance along the
    /// track found by a windowed nearest-point search so the crossing in the middle of the 8 never confuses it.
    /// Wrecked cars drop to the back. The campaign decides what a result is worth (OnPlayerFinished).
    /// Draws its own HUD: position, lap, race time, countdown, wrong-way warning.
    /// </summary>
    public class RaceDirector : MonoBehaviour
    {
        public RaceTrack track;
        public ArenaDirector arena;
        public CarController player;
        public ArenaCrowd crowd;
        public int laps = 3;
        public float countdownTime = 3f;

        public class Entry
        {
            public CarHealth health;
            public CarController car;
            public AICarDriver ai;
            public bool isPlayer;
            public int index;
            public int lap;
            public float s, total;
            public bool finished;
            public float finishTime;
            public int place;
            public float deadTime = -1f;
            public int lastGate;     // last checkpoint passed (0 = finish line)
            public float[] gateSide; // signed distance in front of each gate last frame
            public bool missed;      // further along the track than the next gate without having passed it
        }

        [Header("Checkpoints")]
        [Tooltip("Gate markers, one per checkpoint (index 0 = finish line, may be empty). The next one lights up for the player.")]
        public Renderer[] checkpointMarkers;
        public Color nextGateColour = new Color(0.2f, 1f, 0.35f);
        public Color gateColour = new Color(0.35f, 0.35f, 0.38f);
        [Tooltip("AI cars get extra room either side of a gate, so a wide line never costs them a lap.")]
        public float aiGateSlack = 14f;

        /// <summary>Raised when the player drives through a checkpoint: (gate passed, gates per lap).</summary>
        public event Action<int, int> PlayerCheckpoint;

        public bool Running { get; private set; }
        public bool Countdown { get; private set; }
        public float RaceTime { get; private set; }
        public IReadOnlyList<Entry> Order => order;
        public Entry PlayerEntry { get; private set; }

        /// <summary>(place 1-based, race time)</summary>
        public event Action<int, float> PlayerFinished;

        readonly List<Entry> entries = new List<Entry>();
        readonly List<Entry> order = new List<Entry>();
        int finishedCount;
        float countdownLeft, goFlash, wrongWayTime, lastPlayerTotal, checkpointFlash;
        string checkpointText;
        MaterialPropertyBlock markerBlock;
        int litGate = -2;
        GUIStyle big, mid, small;
        CarInput playerInput;

        /// <summary>Grid up: player at the back, opponents ahead of them, everyone held for the countdown.</summary>
        public void Setup(IList<EnemySpec> opponents)
        {
            Stop();
            if (track == null || player == null) return;
            int count = opponents.Count + 1;
            var slots = new List<Pose>();
            for (int i = 0; i < count; i++) slots.Add(track.GridSlot(i));

            // Opponents take the front slots, the player starts last.
            var enemyHealth = arena.StartMatchOnGrid(opponents, slots.GetRange(0, opponents.Count));
            foreach (var h in enemyHealth) AddEntry(h, false);

            var ps = slots[count - 1];
            var rb = player.Body;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = ps.position;
            rb.rotation = ps.rotation;
            player.transform.SetPositionAndRotation(ps.position, ps.rotation);
            foreach (var w in player.wheels) w.ResetState();
            playerInput = player.GetComponent<CarInput>();
            HoldPlayer(true);
            PlayerEntry = AddEntry(player.GetComponent<CarHealth>(), true);

            foreach (var e in entries) InitProgress(e);
            foreach (var e in entries) { var g = e.car.GetComponent<CarGun>(); if (g != null) g.SetRaceRules(true); }
            UpdateOrder();
        }

        Entry AddEntry(CarHealth h, bool isPlayer)
        {
            var e = new Entry { health = h, car = h.GetComponent<CarController>(), ai = h.GetComponent<AICarDriver>(), isPlayer = isPlayer };
            entries.Add(e);
            order.Add(e);
            return e;
        }

        void InitProgress(Entry e)
        {
            e.index = track.GridIndex(e.car.transform.position);
            e.s = track.FromStart(e.car.transform.position, e.index);
            // Behind the line on the grid: that's the end of "lap -1", every gate of it already done.
            e.lap = e.s > track.Length * 0.5f ? -1 : 0;
            e.lastGate = e.lap < 0 ? track.CheckpointCount - 1 : 0;
            e.gateSide = new float[track.CheckpointCount];
            for (int k = 0; k < e.gateSide.Length; k++) e.gateSide[k] = GateSide(e, k, out _);
            e.total = Progress(e);
        }

        /// <summary>Signed distance of the car in front of gate k (along the racing direction), and how far to the side it is.</summary>
        float GateSide(Entry e, int k, out float lateral)
        {
            var g = track.CheckpointPose(k);
            Vector3 d = e.car.transform.position - g.position;
            d.y = 0f;
            Vector3 fwd = g.rotation * Vector3.forward;
            lateral = Mathf.Abs(Vector3.Dot(d, Vector3.Cross(Vector3.up, fwd)));
            return Vector3.Dot(d, fwd);
        }

        /// <summary>Drive through the next gate forwards, or back out through the last one.</summary>
        void UpdateGates(Entry e)
        {
            int n = track.CheckpointCount;
            int next = (e.lastGate + 1) % n;
            float width = track.halfWidth + track.checkpointSlack + (e.ai != null ? aiGateSlack : 0f);
            for (int k = 0; k < n; k++)
            {
                float side = GateSide(e, k, out float lateral);
                float before = e.gateSide[k];
                e.gateSide[k] = side;
                if (lateral > width || Mathf.Abs(side - before) > 15f) continue; // not through this gate (or a teleport)
                if (k == next && before < 0f && side >= 0f)
                {
                    e.lastGate = k;
                    if (k == 0) e.lap++;
                    if (e.isPlayer) OnPlayerGate(k, n);
                    return;
                }
                if (k == e.lastGate && before >= 0f && side < 0f)
                {
                    // Reversed back through the gate you last passed: it no longer counts.
                    if (k == 0) e.lap--;
                    e.lastGate = (k - 1 + n) % n;
                    return;
                }
            }
        }

        void OnPlayerGate(int k, int n)
        {
            if (k != 0)
            {
                checkpointText = $"CHECKPOINT {k}/{n - 1}";
                checkpointFlash = 1.2f;
            }
            PlayerCheckpoint?.Invoke(k, n);
        }

        /// <summary>Laps x length + last gate + distance past it, capped at the next gate (skipping one doesn't help).</summary>
        float Progress(Entry e)
        {
            float L = track.Length;
            int n = track.CheckpointCount;
            float gate = track.CheckpointDistance(e.lastGate);
            float nextGate = e.lastGate == n - 1 ? L : track.CheckpointDistance(e.lastGate + 1);
            float past = e.s - gate;
            if (past < -L * 0.5f) past += L;
            else if (past > L * 0.5f) past -= L;
            e.missed = past > nextGate - gate + 12f;
            return e.lap * L + gate + Mathf.Clamp(past, 0f, nextGate - gate);
        }

        /// <summary>Start the countdown; cars go on "GO".</summary>
        public void Begin() => StartCoroutine(CountdownRoutine());

        IEnumerator CountdownRoutine()
        {
            Countdown = true;
            countdownLeft = countdownTime;
            while (countdownLeft > 0f)
            {
                countdownLeft -= Time.deltaTime;
                yield return null;
            }
            Countdown = false;
            Running = true;
            RaceTime = 0f;
            goFlash = 1f;
            HoldPlayer(false);
            foreach (var e in entries)
                if (e.ai != null) e.ai.StartRacing(track);
            if (crowd != null) crowd.Cheer(1f);
        }

        void HoldPlayer(bool hold)
        {
            if (playerInput == null) return;
            playerInput.overrideDriving = hold;
            playerInput.overrideThrottle = 0f;
            playerInput.overrideBrake = 0f;
            playerInput.overrideSteer = 0f;
            playerInput.overrideHandbrake = hold;
        }

        /// <summary>End the race and forget the field (arena.ClearArena removes the cars).</summary>
        public void Stop()
        {
            StopAllCoroutines();
            foreach (var e in entries)
                if (e.car != null) { var g = e.car.GetComponent<CarGun>(); if (g != null) g.SetRaceRules(false); }
            Running = false;
            Countdown = false;
            entries.Clear();
            order.Clear();
            finishedCount = 0;
            PlayerEntry = null;
            wrongWayTime = 0f;
            checkpointFlash = 0f;
            LightGates();
        }

        void Update()
        {
            goFlash = Mathf.MoveTowards(goFlash, 0f, Time.deltaTime);
            if (!Running) return;
            RaceTime += Time.deltaTime;
            checkpointFlash = Mathf.MoveTowards(checkpointFlash, 0f, Time.deltaTime);

            foreach (var e in entries)
            {
                if (e.car == null || e.health == null) continue;
                if (e.health.IsDead) { if (e.deadTime < 0f) e.deadTime = RaceTime; continue; }
                if (e.finished) continue;

                e.index = track.Nearest(e.car.transform.position, e.index);
                e.s = track.FromStart(e.car.transform.position, e.index);
                UpdateGates(e);
                e.total = Progress(e);

                if (e.lap >= laps)
                {
                    e.finished = true;
                    e.finishTime = RaceTime;
                    e.place = ++finishedCount;
                    if (crowd != null) crowd.Cheer(e.isPlayer ? 1f : 0.5f);
                    if (e.ai != null) e.ai.StopRacing();
                    if (e.isPlayer) PlayerFinished?.Invoke(e.place, e.finishTime);
                }
            }

            if (PlayerEntry != null && !PlayerEntry.finished && PlayerEntry.car != null)
            {
                // Wrong way: progress going backwards while moving.
                float speed = PlayerEntry.car.Body.linearVelocity.magnitude;
                if (PlayerEntry.total < lastPlayerTotal - 0.05f && speed > 4f) wrongWayTime += Time.deltaTime;
                else wrongWayTime = Mathf.Max(0f, wrongWayTime - Time.deltaTime * 2f);
                lastPlayerTotal = PlayerEntry.total;
            }
            UpdateOrder();
            LightGates();
        }

        /// <summary>Light the player's next gate, dim the rest.</summary>
        void LightGates()
        {
            if (checkpointMarkers == null || checkpointMarkers.Length == 0) return;
            int next = PlayerEntry != null && Running && !PlayerEntry.finished ? (PlayerEntry.lastGate + 1) % track.CheckpointCount : -1;
            if (next == litGate) return;
            litGate = next;
            if (markerBlock == null) markerBlock = new MaterialPropertyBlock();
            for (int k = 0; k < checkpointMarkers.Length; k++)
            {
                var r = checkpointMarkers[k];
                if (r == null) continue;
                Color c = k == next ? nextGateColour : gateColour;
                r.GetPropertyBlock(markerBlock);
                markerBlock.SetColor("_BaseColor", c);
                markerBlock.SetColor("_EmissionColor", k == next ? c * 2.5f : Color.black);
                r.SetPropertyBlock(markerBlock);
            }
        }

        void UpdateOrder()
        {
            order.Sort((a, b) =>
            {
                if (a.finished != b.finished) return a.finished ? -1 : 1;
                if (a.finished) return a.place.CompareTo(b.place);
                bool ad = a.health == null || a.health.IsDead, bd = b.health == null || b.health.IsDead;
                if (ad != bd) return ad ? 1 : -1;
                if (ad) return b.deadTime.CompareTo(a.deadTime);
                return b.total.CompareTo(a.total);
            });
        }

        /// <summary>Current 1-based position of the player.</summary>
        public int PlayerPosition => PlayerEntry != null ? order.IndexOf(PlayerEntry) + 1 : 0;

        static string Ordinal(int n) => n + (n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
        public static string Place(int n) => Ordinal(n);

        public static string FormatTime(float t) => $"{(int)(t / 60f)}:{t % 60f:00.0}";

        void OnGUI()
        {
            if (PlayerEntry == null || (!Running && !Countdown)) return;
            // Make way for the result screen shortly after you finish or get wrecked.
            if ((PlayerEntry.finished && RaceTime - PlayerEntry.finishTime > 1.4f) ||
                (PlayerEntry.deadTime >= 0f && RaceTime - PlayerEntry.deadTime > 2f)) return;
            if (Event.current.type != EventType.Repaint) return;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
                mid = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, richText = true };
                small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, richText = true };
            }
            float w = Screen.width, h = Screen.height;
            big.fontSize = Mathf.RoundToInt(h * 0.16f);
            mid.fontSize = Mathf.RoundToInt(h * 0.045f);
            small.fontSize = Mathf.RoundToInt(h * 0.022f);

            if (Countdown)
            {
                int n = Mathf.CeilToInt(countdownLeft);
                Shadowed(new Rect(0, h * 0.25f, w, h * 0.25f), $"<color=#ffd060>{n}</color>", big);
            }
            else if (goFlash > 0f)
                Shadowed(new Rect(0, h * 0.25f, w, h * 0.25f), $"<color=#60ff80>GO!</color>", big);

            // Position + lap + time, top centre.
            var pe = PlayerEntry;
            int lap = Mathf.Clamp(pe.lap + 1, 1, laps);
            string pos = pe.finished ? $"FINISHED {Ordinal(pe.place)}" : $"{Ordinal(PlayerPosition)} <size=60%>/ {order.Count}</size>";
            Shadowed(new Rect(0, h * 0.02f, w, h * 0.07f), pos, mid);
            Shadowed(new Rect(0, h * 0.075f, w, h * 0.07f), $"<size=70%>LAP {lap}/{laps}     {FormatTime(pe.finished ? pe.finishTime : RaceTime)}</size>", mid);

            // Standings list, top right under the kill counter.
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < order.Count; i++)
            {
                var e = order[i];
                string name = e.isPlayer ? "<b>YOU</b>" : e.health != null ? e.health.displayName : "?";
                string tag = e.finished ? " <color=#9fe0a0>FIN</color>" : e.health != null && e.health.IsDead ? " <color=#ff7060>OUT</color>" : "";
                sb.Append($"{i + 1}. {name}{tag}\n");
            }
            Shadowed(new Rect(w - h * 0.42f, h * 0.09f, h * 0.4f, h * 0.4f), sb.ToString(), small);

            if (wrongWayTime > 1.2f && Mathf.Repeat(Time.time * 2f, 1f) < 0.6f)
                Shadowed(new Rect(0, h * 0.35f, w, h * 0.1f), "<color=#ff5040>WRONG WAY</color>", mid);
            else if (pe.missed && !pe.finished && Mathf.Repeat(Time.time * 2f, 1f) < 0.6f)
                Shadowed(new Rect(0, h * 0.35f, w, h * 0.1f), "<color=#ff9030>MISSED CHECKPOINT - GO BACK</color>", mid);
            else if (checkpointFlash > 0f)
                Shadowed(new Rect(0, h * 0.35f, w, h * 0.1f), $"<color=#60ff80>{checkpointText}</color>", mid);
        }

        static void Shadowed(Rect r, string text, GUIStyle style)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), StripColor(text), style);
            GUI.color = old;
            GUI.Label(r, text, style);
        }

        static string StripColor(string s) => System.Text.RegularExpressions.Regex.Replace(s, "<color=[^>]*>|</color>", "");
    }
}
