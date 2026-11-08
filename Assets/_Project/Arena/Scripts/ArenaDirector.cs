using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Sandbox spawner: keeps enemyCount enemy cars in the arena. A destroyed enemy stays as a smoking
    /// wreck for a while, and a replacement spawns at the free spawn point furthest from the player.
    /// Enemies pick their targets from Combatants (the player and each other).
    /// </summary>
    public class ArenaDirector : MonoBehaviour
    {
        public CarController player;
        [Tooltip("Enemy prefabs, cycled in order (Rammer, Flanker, Bruiser...).")]
        public GameObject[] enemyPrefabs;
        public Transform[] spawnPoints;
        public int enemyCount = 3;
        public float respawnDelay = 5f;
        [Tooltip("Wrecks are removed after this long.")]
        public float wreckLifetime = 25f;
        public float minSpawnDistance = 30f;
        [Tooltip("Optional: the stands' crowd cheers on big crashes and kills.")]
        public ArenaCrowd crowd;

        public IReadOnlyList<CarHealth> Enemies => enemies;
        /// <summary>Everyone who can be targeted: the player plus all enemies (dead ones are skipped by the AI).</summary>
        public IReadOnlyList<CarHealth> Combatants => combatants;
        public int EnemiesDestroyed { get; private set; }

        readonly List<CarHealth> enemies = new List<CarHealth>();
        readonly List<CarHealth> combatants = new List<CarHealth>();
        CarHealth playerHealth;
        int nextPrefab, spawnCount;

        void Start()
        {
            playerHealth = player != null ? player.GetComponent<CarHealth>() : null;
            if (player != null) WatchForCrowd(player.gameObject);
            for (int i = 0; i < enemyCount; i++) Spawn();
        }

        void RebuildCombatants()
        {
            combatants.Clear();
            if (playerHealth != null) combatants.Add(playerHealth);
            foreach (var e in enemies) if (e != null) combatants.Add(e);
        }

        void Spawn()
        {
            if (enemyPrefabs == null || enemyPrefabs.Length == 0 || spawnPoints == null || spawnPoints.Length == 0) return;
            var prefab = enemyPrefabs[nextPrefab++ % enemyPrefabs.Length];
            Transform point = PickSpawnPoint();
            var go = Instantiate(prefab, point.position, point.rotation);
            var driver = go.GetComponent<AICarDriver>();
            if (driver != null)
            {
                driver.director = this;
                driver.SetTarget(player);
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
        }

        Transform PickSpawnPoint()
        {
            Vector3 p = player != null ? player.transform.position : Vector3.zero;
            Transform best = spawnPoints[0];
            float bestScore = float.MinValue;
            foreach (var sp in spawnPoints)
            {
                float d = Vector3.Distance(sp.position, p);
                bool blocked = false;
                foreach (var e in enemies)
                    if (e != null && Vector3.Distance(e.transform.position, sp.position) < 6f) { blocked = true; break; }
                float score = d + (blocked ? -1000f : 0f) + (d < minSpawnDistance ? -500f : 0f) + Random.Range(0f, 8f);
                if (score > bestScore) { bestScore = score; best = sp; }
            }
            return best;
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
            Spawn();
            yield return new WaitForSeconds(Mathf.Max(0f, wreckLifetime - respawnDelay));
            if (wreck != null) Destroy(wreck.gameObject);
        }
    }
}
