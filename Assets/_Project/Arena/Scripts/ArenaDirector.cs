using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Sandbox spawner: keeps enemyCount enemy cars in the arena. New cars wait in a gate tunnel, the
    /// portcullis rises, they drive in and the gate shuts behind them. A destroyed enemy stays as a
    /// smoking wreck for a while and is replaced. Enemies pick targets from Combatants (the player and
    /// each other).
    /// </summary>
    public class ArenaDirector : MonoBehaviour
    {
        public CarController player;
        [Tooltip("Enemy prefabs, cycled in order (Rammer, Flanker, Bruiser...).")]
        public GameObject[] enemyPrefabs;
        [Tooltip("Gates enemies enter through. The one nearest the player's start is used last.")]
        public ArenaGate[] gates;
        public int enemyCount = 4;
        public float respawnDelay = 5f;
        [Tooltip("Wrecks are removed after this long.")]
        public float wreckLifetime = 25f;
        [Tooltip("Don't spawn until BeginSpawning() is called (the intro calls it when it ends).")]
        public bool waitForIntro;
        [Tooltip("A gate isn't used while the player is closer than this (no enemies spawning on top of you).")]
        public float minPlayerDistance = 30f;
        [Tooltip("Optional: the stands' crowd cheers on big crashes and kills.")]
        public ArenaCrowd crowd;

        public IReadOnlyList<CarHealth> Enemies => enemies;
        /// <summary>Everyone who can be targeted: the player plus all enemies (dead ones are skipped by the AI).</summary>
        public IReadOnlyList<CarHealth> Combatants => combatants;
        public int EnemiesDestroyed { get; private set; }
        public event System.Action<CarHealth> CombatantAdded;

        readonly List<CarHealth> enemies = new List<CarHealth>();
        readonly List<CarHealth> combatants = new List<CarHealth>();
        CarHealth playerHealth;
        int nextPrefab, spawnCount, pending;
        bool spawning;

        void Start()
        {
            playerHealth = player != null ? player.GetComponent<CarHealth>() : null;
            if (player != null) WatchForCrowd(player.gameObject);
            RebuildCombatants();
            if (!waitForIntro) BeginSpawning();
        }

        public void BeginSpawning()
        {
            if (spawning) return;
            spawning = true;
            pending += enemyCount;
        }

        void Update()
        {
            if (!spawning || pending <= 0 || gates == null) return;
            var gate = PickGate();
            if (gate == null) return;
            pending--;
            StartCoroutine(Admit(gate, enemyPrefabs[nextPrefab++ % enemyPrefabs.Length]));
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

        IEnumerator Admit(ArenaGate gate, GameObject prefab)
        {
            gate.Busy = true;
            var go = Instantiate(prefab, gate.spawnPoint.position, gate.spawnPoint.rotation);
            var driver = go.GetComponent<AICarDriver>();
            if (driver != null)
            {
                driver.director = this;
                driver.Hold();
            }
            WatchForCrowd(go);
            var health = go.GetComponent<CarHealth>();
            if (health != null)
            {
                health.displayName = $"{health.displayName} {++spawnCount}";
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
            StartCoroutine(Replace(victim));
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
