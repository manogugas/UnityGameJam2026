using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CarArena
{
    /// <summary>One enemy to bring into a match.</summary>
    public class EnemySpec
    {
        public GameObject prefab;
        public int number;
        public float healthScale = 1f;
        public float powerScale = 1f;
        /// <summary>The emperor's mark: every car prefers to attack it.</summary>
        public bool marked;
        public string label;
    }

    /// <summary>
    /// Brings enemy cars into the arena through the gates (wait in the tunnel, portcullis rises, drive in,
    /// gate shuts) and keeps the list of combatants the AI picks targets from.
    ///  - Match mode (campaign): StartMatch() admits an exact list of enemies, no respawns; MatchCleared
    ///    fires when they're all wrecked; ClearArena() removes wrecks and debris afterwards.
    ///  - Sandbox mode: keeps enemyCount cars alive, replacing wrecks (BeginSpawning()).
    /// </summary>
    public class ArenaDirector : MonoBehaviour
    {
        public CarController player;
        [Tooltip("Sandbox: enemy prefabs, cycled in order (Rammer, Flanker, Bruiser...).")]
        public GameObject[] enemyPrefabs;
        [Tooltip("Gates enemies enter through. The one nearest the player's start is used last.")]
        public ArenaGate[] gates;
        [Tooltip("Sandbox: cars kept alive.")]
        public int enemyCount = 4;
        public float respawnDelay = 5f;
        [Tooltip("Sandbox: wrecks are removed after this long.")]
        public float wreckLifetime = 25f;
        [Tooltip("Don't start the sandbox until BeginSpawning() (the intro or campaign controls it).")]
        public bool waitForIntro;
        [Tooltip("A gate isn't used while the player is closer than this (no enemies spawning on top of you).")]
        public float minPlayerDistance = 30f;
        [Tooltip("Optional: the stands' crowd cheers on big crashes and kills.")]
        public ArenaCrowd crowd;
        [Tooltip("Side objectives (gifts, boost pads). The AI reads them from here.")]
        public ArenaObjectives objectives;
        [Tooltip("Track layout (islands the AI routes around).")]
        public ArenaLayout layout;

        public IReadOnlyList<CarHealth> Enemies => enemies;
        /// <summary>Everyone who can be targeted: the player plus all enemies (dead ones are skipped by the AI).</summary>
        public IReadOnlyList<CarHealth> Combatants => combatants;
        public int EnemiesDestroyed { get; private set; }
        public bool InMatch => matchMode && matchRunning;
        /// <summary>A fight is on (campaign match or sandbox): side objectives run while this is true.</summary>
        public bool Running => (matchMode && matchRunning) || spawning;

        public event System.Action<CarHealth> CombatantAdded;
        /// <summary>(victim, killer or null)</summary>
        public event System.Action<CarHealth, CarHealth> EnemyKilled;
        public event System.Action MatchCleared;

        readonly List<CarHealth> enemies = new List<CarHealth>();
        readonly List<CarHealth> combatants = new List<CarHealth>();
        readonly Queue<EnemySpec> queue = new Queue<EnemySpec>();
        readonly List<Coroutine> running = new List<Coroutine>();
        CarHealth playerHealth;
        int nextPrefab, spawnCount, pending, admitting;
        bool spawning, matchMode, matchRunning;

        void Start()
        {
            playerHealth = player != null ? player.GetComponent<CarHealth>() : null;
            if (player != null) WatchForCrowd(player.gameObject);
            RebuildCombatants();
            if (!waitForIntro) BeginSpawning();
        }

        // ------------------------------------------------------------------ sandbox

        public void BeginSpawning()
        {
            if (spawning || matchMode) return;
            spawning = true;
            pending += enemyCount;
        }

        // ------------------------------------------------------------------ match mode

        public void StartMatch(IEnumerable<EnemySpec> specs)
        {
            matchMode = true;
            matchRunning = true;
            spawning = false;
            pending = 0;
            foreach (var s in specs) queue.Enqueue(s);
        }

        /// <summary>Remove every enemy car and its loose debris, close the gates, stop the match.</summary>
        public void ClearArena()
        {
            foreach (var c in running) if (c != null) StopCoroutine(c);
            running.Clear();
            queue.Clear();
            admitting = 0;
            pending = 0;
            matchRunning = false;
            foreach (var e in enemies)
            {
                if (e == null) continue;
                var dmg = e.GetComponent<CarDamage>();
                if (dmg != null && dmg.parts != null)
                    foreach (var p in dmg.parts)
                        if (p != null && p.transform.parent == null) Destroy(p.gameObject);
                Destroy(e.gameObject);
            }
            enemies.Clear();
            RebuildCombatants();
            if (gates != null)
                foreach (var g in gates) if (g != null) { g.Close(); g.Busy = false; }
        }

        // ------------------------------------------------------------------ spawning

        void Update()
        {
            if (gates == null) return;
            if (matchMode)
            {
                if (matchRunning && queue.Count == 0 && admitting == 0 && enemies.Count > 0 && AllEnemiesDead())
                {
                    matchRunning = false;
                    MatchCleared?.Invoke();
                    return;
                }
                if (!matchRunning || queue.Count == 0) return;
                var gate = PickGate();
                if (gate == null) return;
                running.Add(StartCoroutine(Admit(gate, queue.Dequeue())));
                return;
            }
            if (!spawning || pending <= 0) return;
            var g = PickGate();
            if (g == null) return;
            pending--;
            var spec = new EnemySpec { prefab = enemyPrefabs[nextPrefab++ % enemyPrefabs.Length], number = Random.Range(10, 100) };
            running.Add(StartCoroutine(Admit(g, spec)));
        }

        /// <summary>Free gate furthest from the player, never one the player is parked in front of.</summary>
        ArenaGate PickGate()
        {
            Vector3 p = player != null ? player.transform.position : Vector3.zero;
            ArenaGate best = null;
            float bestD = -1f;
            foreach (var g in gates)
            {
                if (g == null || g.Busy) continue;
                float d = Vector3.Distance(g.transform.position, p);
                if (d < minPlayerDistance) continue;
                if (d > bestD) { bestD = d; best = g; }
            }
            return best;
        }

        IEnumerator Admit(ArenaGate gate, EnemySpec spec)
        {
            admitting++;
            gate.Busy = true;
            var go = Instantiate(spec.prefab, gate.spawnPoint.position, gate.spawnPoint.rotation);
            var driver = go.GetComponent<AICarDriver>();
            if (driver != null)
            {
                driver.director = this;
                driver.Hold();
            }
            var controller = go.GetComponent<CarController>();
            if (controller != null && spec.powerScale != 1f)
            {
                controller.maxPowerKW *= spec.powerScale;
                controller.maxWheelTorque *= spec.powerScale;
            }
            var plates = go.GetComponent<CarNumberPlates>();
            if (plates != null) { plates.SetNumber(spec.number); plates.SetMarked(spec.marked); }
            WatchForCrowd(go);
            var health = go.GetComponent<CarHealth>();
            if (health != null)
            {
                float max = health.maxHealth * spec.healthScale;
                health.SetMaxHealth(max, max);
                health.marked = spec.marked;
                health.displayName = !string.IsNullOrEmpty(spec.label) ? spec.label : $"{health.displayName} #{spec.number}";
                spawnCount++;
                health.Died += OnEnemyDied;
                enemies.Add(health);
            }
            RebuildCombatants();
            if (health != null) CombatantAdded?.Invoke(health);

            gate.Open();
            float t = 0f;
            while (!gate.IsFullyOpen && t < 5f) { t += Time.deltaTime; yield return null; }
            if (driver != null) driver.DriveIn(gate.entryPoint.position);
            if (crowd != null) crowd.Cheer(0.6f);

            t = 0f;
            while (go != null && !gate.HasPassed(go.transform.position, 7f) && t < 14f) { t += Time.deltaTime; yield return null; }
            gate.Close();
            admitting--;
            t = 0f;
            while (!gate.IsFullyClosed && t < 5f) { t += Time.deltaTime; yield return null; }
            gate.Busy = false;
        }

        /// <summary>How many bots (other than <paramref name="except"/>) are currently chasing this car.</summary>
        public int AttackersOf(CarHealth target, AICarDriver except)
        {
            int n = 0;
            foreach (var e in enemies)
            {
                if (e == null || e.IsDead) continue;
                var ai = e.GetComponent<AICarDriver>();
                if (ai != null && ai != except && ai.TargetHealth == target) n++;
            }
            return n;
        }

        void RebuildCombatants()
        {
            combatants.Clear();
            if (playerHealth != null) combatants.Add(playerHealth);
            foreach (var e in enemies) if (e != null) combatants.Add(e);
        }

        void WatchForCrowd(GameObject car)
        {
            if (crowd == null) return;
            var dmg = car.GetComponent<CarDamage>();
            if (dmg != null) dmg.Impact += info => { if (info.severity > 0.15f) crowd.Cheer(info.severity * 1.4f); };
            var h = car.GetComponent<CarHealth>();
            if (h != null && h.isPlayer) h.Died += (v, k) => crowd.Cheer(1f);
        }

        void OnEnemyDied(CarHealth victim, CarHealth killer)
        {
            if (crowd != null) crowd.Cheer(1f);
            EnemiesDestroyed++;
            victim.Died -= OnEnemyDied;
            EnemyKilled?.Invoke(victim, killer);

            if (matchMode)
            {
                if (matchRunning && queue.Count == 0 && admitting == 0 && AllEnemiesDead())
                {
                    matchRunning = false;
                    MatchCleared?.Invoke();
                }
                return;
            }
            running.Add(StartCoroutine(Replace(victim)));
        }

        bool AllEnemiesDead()
        {
            foreach (var e in enemies) if (e != null && !e.IsDead) return false;
            return true;
        }

        IEnumerator Replace(CarHealth wreck)
        {
            yield return new WaitForSeconds(respawnDelay);
            enemies.Remove(wreck);
            RebuildCombatants();
            pending++;
            yield return new WaitForSeconds(Mathf.Max(0f, wreckLifetime - respawnDelay));
            if (wreck != null) Destroy(wreck.gameObject);
        }
    }
}
