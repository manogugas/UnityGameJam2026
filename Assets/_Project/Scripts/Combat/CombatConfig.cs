using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace HauntedCasino.Combat
{
    [Serializable]
    public class CombatConfig
    {
        [Range(1, 4)] public int playerCount = 3;
        public int playerMaxHP = 30;
        public int ghostHPPerPlayer = 45;
        [Tooltip("How many times the ghost attacks after all players have acted.")]
        public int ghostAttacksPerRound = 2;
        [Tooltip("Ghost gamble power is multiplied by this to get incoming damage.")]
        public float ghostAttackMultiplier = 1.25f;
        [Tooltip("Defence gamble power is multiplied by this to get blocked damage.")]
        public float defenceMultiplier = 0.8f;
        [Tooltip("Each consecutive successful party hit adds this to the damage multiplier.")]
        public float streakBonusPerStep = 0.25f;
        public float maxStreakMultiplier = 2f;
        [Tooltip("Seconds teammates have to press DEFEND and take the hit for the targeted player.")]
        [FormerlySerializedAs("coverWindowSeconds")]
        public float defendWindowSeconds = 2.5f;
        public int reviveHP = 10;
        public int backfireDamage = 5;
        [Tooltip("Fraction of incoming damage reflected when a defence roll hits a jackpot.")]
        public float jackpotReflect = 0.5f;

        [Header("Stakes (chips are your health)")]
        public int raiseStake = 4;
        public float raiseMultiplier = 1.5f;
        public int allInStake = 8;
        public float allInMultiplier = 2f;

        [Header("Side bets")]
        [Tooltip("A roll counts as BIG when its raw power is at least this.")]
        public int bigThreshold = 10;
        public int sideBetWin = 2;
        public int sideBetLoss = 1;
        [Tooltip("Pause before the ghost rolls so everyone can place side bets.")]
        public float ghostBetDelay = 1.6f;

        [Header("Team move")]
        [Tooltip("Team meter charges needed to unlock Party Slots. Hits and full blocks add a charge.")]
        public int teamMeterMax = 5;
    }
}
