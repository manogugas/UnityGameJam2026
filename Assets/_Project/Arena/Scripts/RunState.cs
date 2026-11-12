using System;
using System.IO;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// One campaign run: money, upgrades, car number, health and where you are on the ladder.
    /// Saved as JSON in persistentDataPath after every match and purchase.
    /// </summary>
    [Serializable]
    public class RunState
    {
        public int carNumber = -1;
        public int money;
        public int matchIndex;
        public int wins;
        public int kills;
        public int[] upgrades = new int[UpgradeSlots];
        public const int UpgradeSlots = 6;
        [Tooltip("-1 = full health.")]
        public float health = -1f;
        [Tooltip("Number the emperor marked for the next match (-1 = not chosen yet).")]
        public int markedNumber = -1;
        public bool championBeaten;

        public bool HasNumber => carNumber >= 0 && carNumber <= 99;

        public int Level(UpgradeType t) => upgrades != null && (int)t < upgrades.Length ? upgrades[(int)t] : 0;

        public RunState Clone()
        {
            var c = (RunState)MemberwiseClone();
            c.upgrades = (int[])upgrades.Clone();
            return c;
        }

        static string FilePath => Path.Combine(Application.persistentDataPath, "arena_run.json");

        public static bool Exists() => File.Exists(FilePath);

        public void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(this, true)); }
            catch (Exception e) { Debug.LogWarning("[CarArena] Couldn't save run: " + e.Message); }
        }

        public static RunState Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var s = JsonUtility.FromJson<RunState>(File.ReadAllText(FilePath));
                if (s != null && (s.upgrades == null || s.upgrades.Length != UpgradeSlots))
                {
                    // Older saves had fewer upgrade tracks: keep the levels they have.
                    var old = s.upgrades ?? new int[0];
                    s.upgrades = new int[UpgradeSlots];
                    Array.Copy(old, s.upgrades, Mathf.Min(old.Length, UpgradeSlots));
                }
                return s;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CarArena] Couldn't load run: " + e.Message);
                return null;
            }
        }

        public static void Delete()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception e) { Debug.LogWarning("[CarArena] Couldn't delete run: " + e.Message); }
        }
    }
}
