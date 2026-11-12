using UnityEngine;
using UnityEngine.InputSystem;

namespace CarArena
{
    /// <summary>
    /// Boost on demand: hold Left Shift to burn the boost meter (CarController.Boost, so rams hit harder too).
    /// The meter trickles back on its own, and boost pads and solid ram hits top it up.
    /// </summary>
    public class CarBoost : MonoBehaviour
    {
        public CarController car;
        public CarDamage damage;
        [Range(0f, 1f)] public float charge = 0.6f;
        [Tooltip("Meter used per second of boost.")]
        public float drainPerSecond = 0.42f;
        public float regenPerSecond = 0.035f;
        [Tooltip("Meter gained per unit of ram severity dealt or taken.")]
        public float ramRefill = 0.25f;
        [Tooltip("Multiplies what boost pads add (the boost upgrade raises it).")]
        public float padRefillScale = 1f;

        public bool Boosting { get; private set; }
        [Tooltip("False until the boost upgrade is bought: Shift does nothing and pads/rams don't fill the meter.")]
        public bool unlocked = true;

        CarInput input;

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            if (damage == null) damage = GetComponent<CarDamage>();
            input = GetComponent<CarInput>();
        }

        void OnEnable() { if (damage != null) damage.Impact += OnImpact; }
        void OnDisable() { if (damage != null) damage.Impact -= OnImpact; }

        void OnImpact(ImpactInfo info)
        {
            if (info.otherBody != null) AddCharge(info.severity * ramRefill);
        }

        public void AddCharge(float amount)
        {
            if (unlocked) charge = Mathf.Clamp01(charge + amount);
        }

        void Update()
        {
            var k = Keyboard.current;
            bool held = k != null && k.leftShiftKey.isPressed;
            bool allowed = unlocked && car != null && !car.EngineDead && (input == null || !input.overrideDriving);
            Boosting = held && allowed && charge > 0.01f;
            float dt = Time.deltaTime;
            if (Boosting)
            {
                car.Boost(0.12f);
                charge = Mathf.Max(0f, charge - drainPerSecond * dt);
            }
            else charge = Mathf.Min(1f, charge + regenPerSecond * dt);
        }
    }
}
