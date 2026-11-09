using Unity.Cinemachine;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Sends a Cinemachine impulse when this car crashes. Every Cinemachine camera with an impulse
    /// listener (the driver's view, the intro shots) shakes, fading with distance from the crash.
    /// Only the player's car sends them: an enemy hitting a wall across the arena shouldn't shake your view
    /// (impulses are global, so enemy crashes used to reach the driver camera).
    /// </summary>
    [RequireComponent(typeof(CinemachineImpulseSource))]
    public class CrashImpulse : MonoBehaviour
    {
        public CarDamage damage;
        [Tooltip("Impulse strength at severity 1.")]
        public float strength = 0.9f;
        public float minSeverity = 0.08f;

        [Tooltip("Only send impulses from the player's car.")]
        public bool playerOnly = true;

        CinemachineImpulseSource source;
        CarHealth health;

        void Awake()
        {
            source = GetComponent<CinemachineImpulseSource>();
            health = GetComponent<CarHealth>();
            if (damage == null) damage = GetComponent<CarDamage>();
        }

        void OnEnable() { if (damage != null) damage.Impact += OnImpact; }
        void OnDisable() { if (damage != null) damage.Impact -= OnImpact; }

        void OnImpact(ImpactInfo info)
        {
            if (info.severity < minSeverity || source == null) return;
            if (playerOnly && (health == null || !health.isPlayer)) return;
            Vector3 dir = info.worldDir.sqrMagnitude > 0.01f ? info.worldDir.normalized : Vector3.up;
            // Mostly a jolt along the hit, with a bit of vertical bounce.
            source.GenerateImpulseAt(info.worldPoint, (dir + Vector3.up * 0.4f) * (info.severity * strength));
        }
    }
}
