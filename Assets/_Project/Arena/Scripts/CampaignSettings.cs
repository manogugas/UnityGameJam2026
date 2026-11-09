using System;
using UnityEngine;

namespace CarArena
{
    public enum EnemyType { Rammer, Flanker, Bruiser, Champion }
    public enum UpgradeType { Armour, Engine, Tyres, RamPlate }

    [Serializable]
    public class MatchDefinition
    {
        public string name = "Match";
        public EnemyType[] enemies = { EnemyType.Rammer };
        [Tooltip("Multiplies enemy health and power for this match.")]
        public float statScale = 1f;
        public bool championMatch;
    }

    [Serializable]
    public class UpgradeTrack
    {
        public UpgradeType type;
        public string displayName;
        [Tooltip("What each level gives, shown on the tablet (one entry per level).")]
        public string[] levelText;
        [Tooltip("Price of each level.")]
        public int[] costs;
        public int MaxLevel => costs != null ? costs.Length : 0;
    }

    /// <summary>
    /// Everything tunable about the campaign in one asset: the match ladder, prices and rewards,
    /// what car numbers do, and the endless mode that follows the champion.
    /// </summary>
    [CreateAssetMenu(menuName = "Car Arena/Campaign Settings", fileName = "CampaignSettings")]
    public class CampaignSettings : ScriptableObject
    {
        [Tooltip("Indexed by EnemyType: Rammer, Flanker, Bruiser, Champion.")]
        public GameObject[] enemyPrefabs = new GameObject[4];
        public MatchDefinition[] matches;

        [Header("Economy")]
        public int startingMoney = 0;
        public int winRewardBase = 150;
        public int winRewardPerMatch = 25;
        public int killRewardBase = 50;
        [Tooltip("Extra bounty per point of the victim's car number (number 80 = +80).")]
        public float bountyPerNumber = 1f;
        [Tooltip("Bounty multiplier for wrecking the car the emperor marked.")]
        public float markedBountyMultiplier = 3f;
        [Tooltip("Money multiplier for the whole match when the emperor marked you and you survived.")]
        public float markedSurvivalMultiplier = 2f;
        [Tooltip("Cash in each emperor's gift (random in this range).")]
        public Vector2Int giftMoney = new Vector2Int(10, 20);
        public float repairCostPerHealth = 2f;
        public int patchAmount = 25;

        [Header("Numbers")]
        [Tooltip("Enemy health bonus per point of car number (99 * 0.004 = +40%).")]
        public float healthPerNumber = 0.004f;
        [Tooltip("Numbers are always two digits (10-99); the champion races as this one.")]
        public int championNumber = 10;

        [Header("Endless (after the champion)")]
        public float endlessScalePerMatch = 0.1f;
        public int endlessMaxEnemies = 6;

        public UpgradeTrack[] upgrades;

        public int LadderLength => matches != null ? matches.Length : 0;

        /// <summary>The ladder match, or a generated endless one past the end of the ladder.</summary>
        public MatchDefinition GetMatch(int index)
        {
            if (matches != null && index < matches.Length) return matches[index];
            int past = index - LadderLength + 1;
            int count = Mathf.Min(endlessMaxEnemies, 3 + past / 2);
            var enemies = new EnemyType[count];
            for (int i = 0; i < count; i++) enemies[i] = (EnemyType)((i + past) % 3);
            return new MatchDefinition
            {
                name = $"Endless {past}",
                enemies = enemies,
                statScale = 1f + endlessScalePerMatch * past,
            };
        }

        public UpgradeTrack Track(UpgradeType type)
        {
            if (upgrades == null) return null;
            foreach (var t in upgrades) if (t.type == type) return t;
            return null;
        }

        public int WinReward(int matchIndex) => winRewardBase + winRewardPerMatch * matchIndex;
        public int KillReward(int victimNumber) => killRewardBase + Mathf.RoundToInt(victimNumber * bountyPerNumber);
    }
}
