using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Applies upgrade levels to a car: stats on CarHealth / CarController and visible bolt-on parts.
    /// Effects per level are tuned here; prices and descriptions live in CampaignSettings.
    /// Level 0 = stock.
    /// </summary>
    public class CarUpgrades : MonoBehaviour
    {
        public CarController car;
        public CarHealth health;

        [Header("Effects per level (index 0 = stock)")]
        public float[] armourBonusHealth = { 0f, 25f, 50f, 80f };
        public float[] enginePowerBonus = { 0f, 0.15f, 0.3f, 0.5f };
        public float[] tyreGripBonus = { 0f, 0.06f, 0.12f, 0.2f };
        [Tooltip("Damage you take when you're the one ramming.")]
        public float[] ramTakenMultiplier = { 0.45f, 0.36f, 0.28f, 0.2f };
        [Tooltip("Extra damage dealt to the car you ram.")]
        public float[] ramDealtMultiplier = { 1f, 1.15f, 1.3f, 1.5f };

        [Header("Visuals (enabled from that level up)")]
        public GameObject[] armourLevels;
        public GameObject[] ramPlateLevels;
        public GameObject[] engineLevels;

        float basePower, baseTorque, baseTopSpeed, baseHealth;
        bool captured;

        void Capture()
        {
            if (captured) return;
            captured = true;
            basePower = car.maxPowerKW;
            baseTorque = car.maxWheelTorque;
            baseTopSpeed = car.topSpeedKmh;
            baseHealth = health.maxHealth;
        }

        public float MaxHealthFor(int armourLevel)
        {
            Capture();
            return baseHealth + armourBonusHealth[Mathf.Clamp(armourLevel, 0, armourBonusHealth.Length - 1)];
        }

        /// <summary>Apply levels; current health keeps its missing amount (armour adds fresh plating).</summary>
        public void Apply(int armour, int engine, int tyres, int ram)
        {
            Capture();
            float newMax = MaxHealthFor(armour);
            float missing = health.maxHealth - health.Health;
            health.SetMaxHealth(newMax, Mathf.Max(0f, newMax - missing));

            float p = enginePowerBonus[Mathf.Clamp(engine, 0, enginePowerBonus.Length - 1)];
            car.maxPowerKW = basePower * (1f + p);
            car.maxWheelTorque = baseTorque * (1f + p * 0.6f);
            car.topSpeedKmh = baseTopSpeed * (1f + p * 0.3f);

            car.gripScale = 1f + tyreGripBonus[Mathf.Clamp(tyres, 0, tyreGripBonus.Length - 1)];

            int r = Mathf.Clamp(ram, 0, ramTakenMultiplier.Length - 1);
            health.rammerMultiplier = ramTakenMultiplier[r];
            health.ramDamageDealt = ramDealtMultiplier[r];

            ShowLevels(armourLevels, armour);
            ShowLevels(ramPlateLevels, ram);
            ShowLevels(engineLevels, engine);
        }

        static void ShowLevels(GameObject[] levels, int level)
        {
            if (levels == null) return;
            for (int i = 0; i < levels.Length; i++)
                if (levels[i] != null) levels[i].SetActive(level >= i + 1);
        }
    }
}
